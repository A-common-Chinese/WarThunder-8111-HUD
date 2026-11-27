// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 A-common-Chinese
// This file is part of War Thunder HUD (8111), licensed under the GNU AGPL v3 or later.

namespace WtHud.Hud;

/// <summary>
/// <c>/indicators</c> 的 <c>aoa_indexer1</c> 量纲：大多数机型是 0–1 归一值，少数（su_34 /
/// su_25tm / su_27sm / f_16c_block_52_aesa / f_5e）是迎角度数。
///
/// <para>★ 这是**用户自检**的结果，不是系统推断。WtHud 不做自动识别（值域猜测会在
/// 归一机型大迎角时误判），只在用户主动按「AOA 指示器自检」键时采样判定，
/// 结果按机型存进 hud-config.json。默认 0 = 旧行为，未自检的机型零变化。</para>
/// </summary>
public enum AoaIndexerUnit
{
    /// <summary>默认：0–1 归一值（0.5.10 及以前的唯一假设）。</summary>
    Default = 0,

    /// <summary>度数：和 <c>/state</c> 的 <c>AoA, deg</c> 基本同值。</summary>
    Degrees = 1,

    /// <summary>关闭：不参与失速告警，只留 AoA 阈值兜底。</summary>
    Off = 2,
}

/// <summary>自检判定结果。</summary>
public enum AoaIndexerVerdict
{
    /// <summary>无法判定（样本不足 / 迎角没变化 / 两种假设都不成立）。</summary>
    Failed = 0,

    /// <summary>度数制。</summary>
    Degrees = 1,

    /// <summary>归一制（面板上就是「默认」）。</summary>
    Normalized = 2,
}

/// <summary>一个自检采样点：座舱指示器读数 + 同一时刻的 AoA（deg）。</summary>
public readonly record struct AoaIndexerSample(double Idx, double AoaDeg);

/// <summary>自检判定输出（字段全部保留给日志/自检看，不参与控制流以外的用途）。</summary>
public sealed record AoaIndexerResult(
    AoaIndexerVerdict Verdict,
    int Samples,
    double AoaSpanDeg,
    double MaxAbsAoaDeg,
    double DegResidualP90,
    double NormResidualP90,
    string Reason)
{
    public bool Ok => Verdict != AoaIndexerVerdict.Failed;

    /// <summary>判定成功时该写进机型表的单位；失败时返回 <see cref="AoaIndexerUnit.Default"/>。</summary>
    public AoaIndexerUnit Unit => Verdict switch
    {
        AoaIndexerVerdict.Degrees => AoaIndexerUnit.Degrees,
        AoaIndexerVerdict.Normalized => AoaIndexerUnit.Default,
        _ => AoaIndexerUnit.Default,
    };
}

/// <summary>
/// AOA 指示器自检：采样窗口内同时记录 <c>(idx, AoA)</c>，然后比较两种量纲假设的残差。
///
/// <para>判定（纯函数，自检直接喂合成样本）：</para>
/// <list type="bullet">
/// <item>有效样本 ≥ 10，且迎角跨度 ≥ 1.5° 或 |AoA| ≥ 3°（不许在平飞小迎角下判）。</item>
/// <item>**度数假设**：允许一个固定偏置（取 idx−AoA 的中位数），去掉偏置后的残差
///   p90 ≤ max(0.6°, 8% × 最大 |AoA|)。</item>
/// <item>**归一假设**：所有 idx 落在 [-0.05, 1.05]，且 p90(|idx − AoA/stallAoa|) ≤ 0.12。</item>
/// <item>只有一个成立 → 就是它；都成立 → 残差小的赢；都不成立 → 失败。</item>
/// </list>
///
/// <para>偏置是必须允许的：su_34 实测 idx 与 <c>/state</c> 的 AoA 差 1–3°（采样错位 + 仪表安装角），
/// 不允许偏置会把真度数机判失败。</para>
/// </summary>
public static class AoaIndexerCheck
{
    /// <summary>判定所需的最少有效样本。</summary>
    public const int MinSamples = 10;

    /// <summary>一次自检最多收多少个样本（25 Hz 下 1 s 约 25 个）。</summary>
    public const int MaxSamples = 30;

    /// <summary>采样窗口（毫秒）。</summary>
    public const int SampleWindowMs = 1000;

    /// <summary>迎角跨度下限（度）。</summary>
    public const double MinAoaSpanDeg = 1.5;

    /// <summary>|AoA| 下限（度）：跨度不够时只要迎角本身够大也能判。</summary>
    public const double MinAbsAoaDeg = 3.0;

    /// <summary>度数假设的残差下限（度）：小迎角时允许 0.6° 的固定误差。</summary>
    public const double DegResidualFloorDeg = 0.6;

    /// <summary>度数假设的残差斜率：随最大迎角放宽 8%（仪表刻度在不同迎角下不是线性的）。</summary>
    public const double DegResidualSlope = 0.08;

    /// <summary>归一假设的残差上限（归一单位）。</summary>
    public const double NormResidualMax = 0.12;

    /// <summary>归一假设的 idx 合法区间下限。</summary>
    public const double NormIndexMin = -0.05;

    /// <summary>归一假设的 idx 合法区间上限。</summary>
    public const double NormIndexMax = 1.05;

    /// <summary>地面判定：雷达高度低于它（米）且起落架放下 = 地面上。</summary>
    public const double GroundRadarAltM = 30.0;

    /// <summary>「起落架放下」的百分比门限（8111 的 gear, % 0–100）。</summary>
    public const double GearDownPercent = 50.0;

    /// <summary>
    /// 地面判定：起落架放下 **且** 雷达高度 &lt; 30 m。
    /// <para>没有雷达高度的机型永远不算地面（宁可让用户在空中自检，也不要在滑跑时采到地面静止的假样本）。</para>
    /// </summary>
    public static bool IsGround(double gearPercent, double? radarAltM) =>
        radarAltM is double ralt && double.IsFinite(ralt) && ralt < GroundRadarAltM
        && gearPercent >= GearDownPercent;

    /// <summary>是否应该开始/继续采样：飞行中、机型已知、不在地面、当前单位是默认。</summary>
    public static bool ShouldSample(bool inFlight, string? vehicle, bool ground, AoaIndexerUnit unit) =>
        inFlight && !string.IsNullOrWhiteSpace(vehicle) && !ground && unit == AoaIndexerUnit.Default;

    /// <summary>最近秩 p90（升序第 ceil(0.9n) 个）。空输入返回 0。</summary>
    public static double Percentile90(IReadOnlyList<double> values)
    {
        if (values.Count == 0) return 0;
        var sorted = new double[values.Count];
        for (int i = 0; i < values.Count; i++) sorted[i] = values[i];
        Array.Sort(sorted);
        int rank = (int)Math.Ceiling(0.9 * sorted.Length);
        return sorted[Math.Clamp(rank - 1, 0, sorted.Length - 1)];
    }

    /// <summary>中位数（偶数取中间两个的平均）。空输入返回 0。</summary>
    public static double Median(IReadOnlyList<double> values)
    {
        if (values.Count == 0) return 0;
        var sorted = new double[values.Count];
        for (int i = 0; i < values.Count; i++) sorted[i] = values[i];
        Array.Sort(sorted);
        int mid = sorted.Length / 2;
        return sorted.Length % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2.0;
    }

    /// <summary>
    /// 判定量纲。<paramref name="stallAoaDeg"/> 用「该机型的有效失速迎角」
    /// （标定 &gt; 机型档案 &gt; 全局），归一假设拿它做分母。
    /// </summary>
    public static AoaIndexerResult Classify(IReadOnlyList<AoaIndexerSample> samples, double stallAoaDeg)
    {
        // 只有 (idx, AoA) 都有限的样本能参与判定；NaN/Inf 来自坏包，直接丢。
        var usable = new List<AoaIndexerSample>(samples.Count);
        foreach (var s in samples)
            if (double.IsFinite(s.Idx) && double.IsFinite(s.AoaDeg)) usable.Add(s);

        if (usable.Count < MinSamples)
            return new AoaIndexerResult(AoaIndexerVerdict.Failed, usable.Count, 0, 0, 0, 0,
                $"样本不足（{usable.Count}/{MinSamples}）");

        double aoaMin = double.MaxValue, aoaMax = double.MinValue, maxAbs = 0;
        double idxMin = double.MaxValue, idxMax = double.MinValue;
        foreach (var s in usable)
        {
            aoaMin = Math.Min(aoaMin, s.AoaDeg);
            aoaMax = Math.Max(aoaMax, s.AoaDeg);
            maxAbs = Math.Max(maxAbs, Math.Abs(s.AoaDeg));
            idxMin = Math.Min(idxMin, s.Idx);
            idxMax = Math.Max(idxMax, s.Idx);
        }
        double span = aoaMax - aoaMin;
        if (span < MinAoaSpanDeg && maxAbs < MinAbsAoaDeg)
            return new AoaIndexerResult(AoaIndexerVerdict.Failed, usable.Count, span, maxAbs, 0, 0,
                "迎角变化不足（保持 5–10° 迎角、速度稳定后再按一次）");

        // ---- 度数假设：允许固定偏置，看去掉偏置后的残差 ----
        var offsets = new double[usable.Count];
        for (int i = 0; i < usable.Count; i++) offsets[i] = usable[i].Idx - usable[i].AoaDeg;
        double bias = Median(offsets);
        var degResiduals = new double[usable.Count];
        for (int i = 0; i < usable.Count; i++)
            degResiduals[i] = Math.Abs(usable[i].Idx - usable[i].AoaDeg - bias);
        double degP90 = Percentile90(degResiduals);
        double degLimit = Math.Max(DegResidualFloorDeg, DegResidualSlope * maxAbs);
        bool degOk = degP90 <= degLimit;

        // ---- 归一假设：idx 必须像归一值，且 idx ≈ AoA / stallAoa ----
        bool idxInRange = idxMin >= NormIndexMin && idxMax <= NormIndexMax;
        var normResiduals = new double[usable.Count];
        bool canNormalize = stallAoaDeg > 1.0;
        if (canNormalize)
            for (int i = 0; i < usable.Count; i++)
                normResiduals[i] = Math.Abs(usable[i].Idx - usable[i].AoaDeg / stallAoaDeg);
        else
            Array.Fill(normResiduals, double.PositiveInfinity);
        double normP90 = canNormalize ? Percentile90(normResiduals) : double.PositiveInfinity;
        bool normOk = canNormalize && idxInRange && normP90 <= NormResidualMax;

        if (degOk && normOk)
        {
            // 两种都成立（理论上不会：归一机的 deg 残差约 0.6×迎角，度数机的 norm 残差同样很大）。
            // 谁残差小谁赢；完全一样时按旧的归一语义（更保守，不会把普通机型改成度数）。
            var verdict = degP90 < normP90 ? AoaIndexerVerdict.Degrees : AoaIndexerVerdict.Normalized;
            return new AoaIndexerResult(verdict, usable.Count, span, maxAbs, degP90, normP90,
                verdict == AoaIndexerVerdict.Degrees ? "度数残差更小" : "归一残差更小");
        }
        if (degOk)
            return new AoaIndexerResult(AoaIndexerVerdict.Degrees, usable.Count, span, maxAbs, degP90, normP90,
                "迎角与指示器同量纲");
        if (normOk)
            return new AoaIndexerResult(AoaIndexerVerdict.Normalized, usable.Count, span, maxAbs, degP90, normP90,
                "指示器为归一值");
        return new AoaIndexerResult(AoaIndexerVerdict.Failed, usable.Count, span, maxAbs, degP90, normP90,
            "两种量纲假设都不成立（保持 5–10° 迎角、速度稳定后再按一次）");
    }

    /// <summary>
    /// 运行时换算：把指示器读数折算成 0–1 归一值，供原来的 0.9 / 0.97 阈值使用。
    /// <para>默认 = 原值（0.5.10 行为一字不差）；度数 = idx / stallAoa；关闭 = null（跳过分支）。</para>
    /// </summary>
    public static double? ToNormalized(double idx, AoaIndexerUnit unit, double stallAoaDeg) => unit switch
    {
        AoaIndexerUnit.Off => null,
        AoaIndexerUnit.Degrees => stallAoaDeg > 1.0 ? idx / stallAoaDeg : idx,
        _ => idx,
    };

    /// <summary>归一值 → 告警等级（0 = 不报，1 = 接近失速，2 = 失速）。与旧代码的 0.9 / 0.97 一致。</summary>
    public static int LevelOf(double normalized) => normalized > 0.97 ? 2 : normalized > 0.9 ? 1 : 0;

    /// <summary>面板/日志用的单位名（中文）。</summary>
    public static string UnitTextZh(AoaIndexerUnit unit) => unit switch
    {
        AoaIndexerUnit.Degrees => "度数",
        AoaIndexerUnit.Off => "关闭",
        _ => "默认（归一）",
    };

    /// <summary>
    /// 「疑似度数制」提示的触发条件：当前单位是默认，且指示器读数超出归一值域（|idx| &gt; 1.2）。
    /// <para>★ 只用于提示，**绝不据此改配置** —— 归一机型在大迎角时 idx 也可能 &gt;1.2，
    /// 值域推断不可靠；真正的判定只能由用户主动自检完成。</para>
    /// <para>「每个机型本次运行只提示一次」由调用方的 HashSet 去重（见 Program.TakeSuspectedDegreesHint）。</para>
    /// </summary>
    public static bool SuspectedDegrees(AoaIndexerUnit unit, double idx) =>
        unit == AoaIndexerUnit.Default && double.IsFinite(idx) && Math.Abs(idx) > 1.2;
}
