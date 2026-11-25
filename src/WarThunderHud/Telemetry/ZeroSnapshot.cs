// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 A-common-Chinese
// This file is part of War Thunder HUD (8111), licensed under the GNU AGPL v3.0 or later.

using System.Globalization;

namespace WtHud.Telemetry;

/// <summary>
/// 「全 0 快照」判定：8111 掉线/冻结时会回一条 <c>valid=true</c> 但关键数值全 0 的帧
/// （<c>inflight=false</c>，IAS/TAS/ALT/油量/航向/姿态都是 0）。这种帧如果写进 jsonl，
/// FlightLog 报告里就会出现一条冲向 0 的 V 形陡降（t≈31.8 s、101.6 s 那种），
/// 所以记录端在写样本前先把它判掉。
/// <para>判定故意保守 —— 三个条件必须同时成立才叫全 0：
/// ① <c>valid=true</c>；② <c>inflight=false</c>；③ 下面列的关键数值全部为 0。
/// 真实停在地面时只有 IAS/TAS/Mach/Vy/AoA 是 0，高度/油量/航向/Ny 一般不为 0，
/// 所以落地/滑行/停机数据不会被误杀。</para>
/// <para>gear/flaps/airbrake 这三个操纵面量**故意不参与判定**：它们是 0..100 的档位，
/// 既可能是真实收起状态（空中/停机），也可能在 8111 半冻结时留着旧值；
/// 把它们当必要条件只会增加漏杀，而关键飞行量全 0 本身已经足够说明这一帧没有意义。</para>
/// </summary>
internal static class ZeroSnapshot
{
    /// <summary>
    /// 连续多少帧全 0 才认定为「8111 冻结」（而不是单帧抖动）。
    /// 单帧只缓冲、不丢；连续 <see cref="ConfirmAfter"/> 帧才跳过。
    /// </summary>
    internal const int ConfirmAfter = 2;

    /// <summary>判定「数值为 0」的容差：JSON 反序列化出来的冻结帧是精确 0，容差只防浮点噪声。</summary>
    internal const double Epsilon = 1e-9;

    /// <summary>第一次跳过的日志行（只打一次）。</summary>
    internal const string FirstSkipLogLine = "record: skip zero snapshot";

    /// <summary>跳过汇总日志的最小间隔（毫秒）：之后每 5 s 最多打一条 <c>skip zero xN</c>。</summary>
    internal const long SummaryIntervalMs = 5000;

    /// <summary>
    /// 这一帧是不是「全 0 / 无意义」快照。纯函数，自检直接断言边界，不依赖记录器。
    /// </summary>
    internal static bool IsZeroSnapshot(TelemetrySnapshot snap)
    {
        if (snap is null) return false;
        // 8111 冻结帧的签名：still claims valid, but off-flight and everything zero.
        if (!snap.Valid || snap.InFlight) return false;

        // 速度 / 高度 / 垂直速度 / 气流角 / 载荷 / 滚转角速度 / 航向 / 油量
        return IsZero(snap.IasKmh)
            && IsZero(snap.TasKmh)
            && IsZero(snap.Mach)
            && IsZero(snap.AltM)
            && IsZero(snap.VyMs)
            && IsZero(snap.AoaDeg)
            && IsZero(snap.AosDeg)
            && IsZero(snap.NyG)
            && IsZero(snap.RollRateDegS)
            && IsZero(snap.HeadingDeg)
            && IsZero(snap.FuelKg)
            && IsZero(snap.Fuel0Kg)
            // 姿态/雷达高度：机型不提供时是 null，按「没有值」放行；给值就必须是 0。
            && IsZeroOrMissing(snap.PitchDeg)
            && IsZeroOrMissing(snap.BankDeg)
            && IsZeroOrMissing(snap.TurnRateDegS)
            && IsZeroOrMissing(snap.RadioAltM);
    }

    /// <summary>跳过汇总日志行。样例：<c>record: skip zero x7</c>（窗口内跳过 7 条样本）。</summary>
    internal static string SkipSummaryLogLine(int count)
        => string.Create(CultureInfo.InvariantCulture, $"record: skip zero x{count}");

    private static bool IsZero(double value) => Math.Abs(value) <= Epsilon;

    private static bool IsZeroOrMissing(double? value) => value is not double v || IsZero(v);
}

/// <summary>记录端对单帧快照的处置结论。</summary>
internal enum ZeroSnapshotVerdict
{
    /// <summary>正常样本：直接写。</summary>
    Write,

    /// <summary>第一帧全 0：先缓冲，等下一帧确认，单帧抖动不丢数据。</summary>
    Hold,

    /// <summary>连续 ≥<see cref="ZeroSnapshot.ConfirmAfter"/> 帧全 0：判定为冻结，跳过不写。</summary>
    Skip,
}

/// <summary>
/// 全 0 快照的连续去抖状态机（纯内存状态，不碰文件）。
/// <para>语义：单帧全 0 只 <see cref="ZeroSnapshotVerdict.Hold"/>（调用方缓冲，下一帧不是全 0 就补写回去）；
/// 连续 <see cref="ZeroSnapshot.ConfirmAfter"/> 帧全 0 才 <see cref="ZeroSnapshotVerdict.Skip"/>，
/// 此时缓冲区里那条也一起作废 —— 一次确认丢 2 条，之后每帧丢 1 条。</para>
/// <para>状态由调用方在记录锁里推进，自检可以按任意序列驱动它。</para>
/// </summary>
internal sealed class ZeroSnapshotFilter
{
    /// <summary>当前连续全 0 帧数（遇到正常样本归零）。</summary>
    public int Streak { get; private set; }

    /// <summary>本次记录里累计跳过的样本条数（给日志/诊断用）。</summary>
    public int SkippedTotal { get; private set; }

    /// <summary>喂一帧，返回处置结论；<paramref name="skippedNow"/> 是本次新跳过的条数。</summary>
    public ZeroSnapshotVerdict Offer(TelemetrySnapshot snap, out int skippedNow)
    {
        skippedNow = 0;
        if (!ZeroSnapshot.IsZeroSnapshot(snap))
        {
            Streak = 0;
            return ZeroSnapshotVerdict.Write;
        }

        Streak++;
        if (Streak < ZeroSnapshot.ConfirmAfter) return ZeroSnapshotVerdict.Hold;

        // 确认那一帧要把前一条被缓冲的样本一起算掉（2 条），之后每次只多 1 条。
        skippedNow = Streak == ZeroSnapshot.ConfirmAfter ? ZeroSnapshot.ConfirmAfter : 1;
        SkippedTotal += skippedNow;
        return ZeroSnapshotVerdict.Skip;
    }

    /// <summary>开始/结束一次记录时清空，避免上一局的连续计数串到下一局。</summary>
    public void Reset()
    {
        Streak = 0;
        SkippedTotal = 0;
    }
}
