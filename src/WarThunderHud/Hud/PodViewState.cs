// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 A-common-Chinese
// This file is part of War Thunder HUD (8111), licensed under the GNU AGPL v3 or later.

namespace WtHud.Hud;

/// <summary>
/// 当前在哪个视角。取代以前 <c>int viewState 0/1/2</c> 的魔数 —— 加了导引头/吊舱之后
/// 「1 = 座舱」这种写法已经读不懂了，而且 V 序列依赖顺序，必须能一眼看出状态。
/// </summary>
public enum ViewKind
{
    /// <summary>第三人称。</summary>
    Third = 0,

    /// <summary>导引头（电视/红外制导头，<c>ID_CAMERA_SEEKER</c>）。</summary>
    Seeker = 1,

    /// <summary>瞄准吊舱（<c>ID_CAMERA_OPTICS</c>），可能有 1–2 个，用 <see cref="ViewState.PodIndex"/> 区分。</summary>
    Pod = 2,

    /// <summary>座舱。</summary>
    Cockpit = 3,

    /// <summary>机头视角。</summary>
    Nose = 4,
}

/// <summary>视角状态：种类 + 吊舱序号（1 起；非吊舱视角固定 1）。</summary>
public readonly record struct ViewState(ViewKind Kind, int PodIndex = 1)
{
    public static readonly ViewState Third = new(ViewKind.Third, 1);

    /// <summary>导引头/吊舱都是「外部相机」，V 序列里排在第三人称和座舱之间。</summary>
    public bool IsPodCamera => Kind is ViewKind.Seeker or ViewKind.Pod;

    /// <summary>日志/提示用的短名：<c>third</c> / <c>seeker</c> / <c>pod1</c> / <c>cockpit</c> / <c>nose</c>。</summary>
    public override string ToString() => Kind switch
    {
        ViewKind.Third => "third",
        ViewKind.Seeker => "seeker",
        ViewKind.Pod => "pod" + Math.Max(1, PodIndex),
        ViewKind.Cockpit => "cockpit",
        ViewKind.Nose => "nose",
        _ => "third",
    };
}

/// <summary>
/// 视角状态机的纯函数：V 循环、直键跳转、吊舱/第三人称组合键、开关变化后的强制回退。
///
/// <para>V 序列（<c>podViewEnabled=true</c>）：</para>
/// <list type="bullet">
/// <item>seeker 开：第三人称 → 导引头 → 吊舱1..N → 座舱 → 机头 → 第三人称</item>
/// <item>seeker 关：第三人称 → 吊舱1..N → 座舱 → 机头 → 第三人称</item>
/// <item>总开关关：旧序列第三人称 → 座舱 → 机头 → 第三人称</item>
/// </list>
///
/// <para>★ 直键幂等：导引头键按到导引头后**再按不退出**；反复按同一视角键不变化。
/// 离开导引头/吊舱主要靠 V；吊舱/第三人称组合键是唯一例外（同一键在两者间切换）。</para>
///
/// <para>纯逻辑，自检直接覆盖所有分支，不依赖 Windows / 游戏。</para>
/// </summary>
public static class PodViewState
{
    /// <summary>吊舱视角最多 2 个（游戏里 F-15E / Su-34 之类的前后吊舱）。</summary>
    public const int MaxPods = 2;

    /// <summary>吊舱数量夹到 1..2；配置被手改成 0 或 9 也安全。</summary>
    public static int ClampPodCount(int count) => Math.Clamp(count, 1, MaxPods);

    /// <summary>
    /// 把状态收敛到当前配置允许的视角：
    /// 总开关关 → 第三人称；导引头关闭 → 若停在导引头则第三人称；吊舱序号超出数量 → 夹回最后一个。
    /// <para>返回 == 入参表示「不需要动」；调用方据此决定是否提示。</para>
    /// </summary>
    public static ViewState Normalize(ViewState cur, bool enabled, bool seekerEnabled, int podCount)
    {
        // 总开关关闭只收走外部相机（导引头/吊舱）；座舱/机头属于旧序列，必须保留。
        if (!enabled) return cur.IsPodCamera ? ViewState.Third : cur;
        if (cur.Kind == ViewKind.Seeker && !seekerEnabled) return ViewState.Third;
        if (cur.Kind == ViewKind.Pod)
            return new ViewState(ViewKind.Pod, Math.Clamp(cur.PodIndex, 1, ClampPodCount(podCount)));
        return cur;
    }

    /// <summary>V 键序列的下一站。</summary>
    public static ViewState Next(ViewState cur, bool enabled, bool seekerEnabled, int podCount)
    {
        if (!enabled)
        {
            return cur.Kind switch
            {
                ViewKind.Third => new ViewState(ViewKind.Cockpit, 1),
                ViewKind.Cockpit => new ViewState(ViewKind.Nose, 1),
                _ => ViewState.Third,
            };
        }

        int count = ClampPodCount(podCount);
        var c = Normalize(cur, enabled: true, seekerEnabled, count);
        switch (c.Kind)
        {
            case ViewKind.Third:
                return seekerEnabled ? new ViewState(ViewKind.Seeker, 1) : new ViewState(ViewKind.Pod, 1);
            case ViewKind.Seeker:
                return new ViewState(ViewKind.Pod, 1);
            case ViewKind.Pod:
                return c.PodIndex < count
                    ? new ViewState(ViewKind.Pod, c.PodIndex + 1)
                    : new ViewState(ViewKind.Cockpit, 1);
            case ViewKind.Cockpit:
                return new ViewState(ViewKind.Nose, 1);
            default:
                return ViewState.Third;
        }
    }

    /// <summary>导引头直键：跳到导引头；已在导引头则不变（不退出）。功能关闭时不动。</summary>
    public static ViewState JumpSeeker(ViewState cur, bool enabled, bool seekerEnabled) =>
        enabled && seekerEnabled ? new ViewState(ViewKind.Seeker, 1) : cur;

    /// <summary>吊舱直键：跳到吊舱 1；已在吊舱不变。功能关闭时不动。</summary>
    public static ViewState JumpPodFirst(ViewState cur, bool enabled) =>
        enabled ? new ViewState(ViewKind.Pod, 1) : cur;

    /// <summary>第三人称直键。</summary>
    public static ViewState JumpThird(ViewState cur) => ViewState.Third;

    /// <summary>
    /// 吊舱/第三人称键（<c>Ctrl+V</c> 那种两动作共用一键的情况）：
    /// <list type="bullet">
    /// <item>同一键：吊舱 → 第三人称；第三人称 → 吊舱 1；导引头/座舱/机头 → 第三人称</item>
    /// <item>不同键：各自直跳（吊舱 1 / 第三人称），互不影响</item>
    /// </list>
    /// </summary>
    public static ViewState ApplyPodTps(ViewState cur, bool enabled, bool sameKey,
                                       bool podPressed, bool tpsPressed)
    {
        if (!enabled) return cur;
        bool pressed = podPressed || tpsPressed;
        if (!pressed) return cur;

        if (sameKey)
        {
            return cur.Kind switch
            {
                ViewKind.Pod => ViewState.Third,
                ViewKind.Third => new ViewState(ViewKind.Pod, 1),
                _ => ViewState.Third,
            };
        }

        var next = cur;
        if (podPressed) next = JumpPodFirst(next, enabled);
        if (tpsPressed) next = JumpThird(next);
        return next;
    }

    /// <summary>导引头快速开关：翻转布尔（纯函数版，自检直接覆盖）。</summary>
    public static bool ToggleSeeker(bool current) => !current;

    /// <summary>
    /// 主键/备用键同帧按下时只算一次切换（否则 airbrake / 人在回路会双触发抵消）。
    /// 返回新锁存值。
    /// </summary>
    public static bool Toggle(bool current, bool edgeA, bool edgeB) =>
        edgeA || edgeB ? !current : current;

    /// <summary>
    /// 该视角是否隐藏 HUD（P1 视角隐藏）：
    /// 导引头/吊舱是外部相机，总是隐藏；座舱/机头照旧看开关；第三人称显示。
    /// </summary>
    public static bool HidesHud(ViewState view, bool hideInCockpit, bool hideInNose) => view.Kind switch
    {
        ViewKind.Seeker or ViewKind.Pod => true,
        ViewKind.Cockpit => hideInCockpit,
        ViewKind.Nose => hideInNose,
        _ => false,
    };

    /// <summary>日志/提示用中文名。</summary>
    public static string DescribeZh(ViewState view) => view.Kind switch
    {
        ViewKind.Third => "第三人称",
        ViewKind.Seeker => "导引头",
        ViewKind.Pod => "吊舱" + Math.Clamp(view.PodIndex, 1, MaxPods),
        ViewKind.Cockpit => "座舱",
        ViewKind.Nose => "机头",
        _ => "第三人称",
    };
}
