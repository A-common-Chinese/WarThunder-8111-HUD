// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 A-common-Chinese
// This file is part of War Thunder HUD (8111), licensed under the GNU AGPL v3.0 or later.

namespace WtHud.Hud;

/// <summary>
/// 谁在请求隐藏 HUD。枚举值只是身份（不是优先级 —— 地图和记分板同为 P2，
/// 优先级统一由 <see cref="HudHideState.PriorityOf"/> 给出）。
/// <para>每个来源必须能独立记住自己的 bool：地图 + 记分板同时隐藏时，松开其中一个
/// 不能让 HUD 提前放行（issue #2 的一类「按键切换/重置」错乱就是这么来的）。</para>
/// </summary>
public enum HideReason
{
    /// <summary>P0：按住隐藏键（默认 U，按住超过 220 ms 生效，松开恢复）。</summary>
    Manual = 0,

    /// <summary>P1：视角隐藏（座舱 / 机头 / 导引头 / 吊舱），每帧由
    /// <see cref="PodViewState.HidesHud"/> 计算。</summary>
    View = 1,

    /// <summary>P1：人在回路（武器操作员视角）隐藏：独立锁存，按一下切换。</summary>
    ManLoop = 4,

    /// <summary>P2：地图键（默认 M，按住隐藏）。</summary>
    Map = 2,

    /// <summary>P2：记分板键（默认 TAB，按一下切换锁存）。</summary>
    Scoreboard = 3,
}

/// <summary>
/// HUD 隐藏状态机：把「为什么隐藏」拆成互相独立的状态，任一为 true 即隐藏。
/// <para>优先级（数字越小越强）：P0 手动按住 &gt; P1 视角 / 人在回路 &gt; P2 地图 / 记分板。
/// Esc 只清 P2；F5 / 失焦 / 换局清全部。地面静止自动隐藏不在这里 —— 它仍然是
/// 渲染循环里的每帧条件，不进状态机。</para>
/// <para>输入轮询线程（5 ms）和渲染线程（每帧）都会读写，所以内部用一把锁；
/// 逻辑全是纯内存操作，自检里不依赖 Windows / 游戏即可覆盖。</para>
/// </summary>
public sealed class HudHideState
{
    /// <summary>固定顺序：日志、ActiveReasons 都按这个顺序输出，测试和日志才稳定。</summary>
    private static readonly HideReason[] AllReasons =
    {
        HideReason.Manual, HideReason.View, HideReason.ManLoop, HideReason.Map, HideReason.Scoreboard,
    };

    private readonly object _gate = new();
    private bool _manual;
    private bool _view;
    private bool _manLoop;
    private bool _map;
    private bool _scoreboard;

    /// <summary>来源的优先级：数字越小越强。地图和记分板都是 2（同级）。</summary>
    public static int PriorityOf(HideReason reason) => reason switch
    {
        HideReason.Manual => 0,
        HideReason.View => 1,
        HideReason.ManLoop => 1,
        HideReason.Map => 2,
        HideReason.Scoreboard => 2,
        _ => int.MaxValue,
    };

    /// <summary>日志里用的短名字（与状态机一一对应，不要翻成别的词）。</summary>
    public static string NameOf(HideReason reason) => reason switch
    {
        HideReason.Manual => "manual",
        HideReason.View => "view",
        HideReason.ManLoop => "manloop",
        HideReason.Map => "map",
        HideReason.Scoreboard => "scoreboard",
        _ => "unknown",
    };

    /// <summary>任一来源为 true → HUD 必须隐藏。draw 的隐藏判断用它。</summary>
    public bool IsHidden
    {
        get
        {
            lock (_gate) return _manual || _view || _manLoop || _map || _scoreboard;
        }
    }

    /// <summary>单个来源是否隐藏。</summary>
    public bool IsSet(HideReason reason)
    {
        lock (_gate) return Get(reason);
    }

    /// <summary>写入一个来源的状态。幂等：重复写同一个值不产生任何副作用。</summary>
    public void Set(HideReason reason, bool hidden)
    {
        lock (_gate)
        {
            switch (reason)
            {
                case HideReason.Manual: _manual = hidden; break;
                case HideReason.View: _view = hidden; break;
                case HideReason.ManLoop: _manLoop = hidden; break;
                case HideReason.Map: _map = hidden; break;
                case HideReason.Scoreboard: _scoreboard = hidden; break;
                default: throw new ArgumentOutOfRangeException(nameof(reason), reason, "unknown hide reason");
            }
        }
    }

    /// <summary>当前生效的来源（固定顺序：manual, view, map, scoreboard）。</summary>
    public List<HideReason> ActiveReasons()
    {
        lock (_gate)
        {
            var list = new List<HideReason>(AllReasons.Length);
            foreach (var reason in AllReasons)
                if (Get(reason)) list.Add(reason);
            return list;
        }
    }

    /// <summary>
    /// 清除 priority &gt; <paramref name="priority"/> 的来源，返回实际被清掉的项（用于日志）。
    /// <para>Esc 用 <c>ClearAbovePriority(1)</c>：只清 P2（地图 / 记分板），P0/P1 保持不动；
    /// 没有可清项时返回空表 —— 调用方据此决定「什么都不做、不打扰游戏 Esc 菜单」。</para>
    /// </summary>
    public List<HideReason> ClearAbovePriority(int priority)
    {
        lock (_gate)
        {
            var cleared = new List<HideReason>(AllReasons.Length);
            foreach (var reason in AllReasons)
            {
                if (PriorityOf(reason) <= priority) continue;
                if (!Get(reason)) continue;
                SetUnsafe(reason, false);
                cleared.Add(reason);
            }
            return cleared;
        }
    }

    /// <summary>清除全部来源（F5 / 失焦 / 换局）。返回实际被清掉的项。</summary>
    public List<HideReason> ClearAll() => ClearAbovePriority(int.MinValue);

    /// <summary>
    /// 当前状态的日志行：隐藏时带生效来源，例如 <c>hud hide -&gt; on (map,scoreboard)</c>；
    /// 否则 <c>hud hide -&gt; off</c>。纯逻辑，自检直接覆盖文本。
    /// </summary>
    public string LogLine()
    {
        lock (_gate) return FormatLog(IsHiddenUnsafe(), ActiveReasonsUnsafe());
    }

    /// <summary>纯函数版日志生成（自检用）。active 为空时仍然输出 <c>on</c> 而不是 <c>on ()</c>。</summary>
    public static string FormatLog(bool hidden, IEnumerable<HideReason>? active)
    {
        if (!hidden) return "hud hide -> off";
        string text = ActiveText(active);
        return text.Length == 0 ? "hud hide -> on" : $"hud hide -> on ({text})";
    }

    /// <summary>来源列表 → <c>map,scoreboard</c> 这种逗号串（顺序固定，空表为空串）。</summary>
    public static string ActiveText(IEnumerable<HideReason>? reasons)
    {
        if (reasons is null) return "";
        return string.Join(",", reasons.Select(NameOf));
    }

    // ---- 锁内使用（调用方已持锁）----
    private bool IsHiddenUnsafe() => _manual || _view || _manLoop || _map || _scoreboard;

    private List<HideReason> ActiveReasonsUnsafe()
    {
        var list = new List<HideReason>(AllReasons.Length);
        foreach (var reason in AllReasons)
            if (Get(reason)) list.Add(reason);
        return list;
    }

    private bool Get(HideReason reason) => reason switch
    {
        HideReason.Manual => _manual,
        HideReason.View => _view,
        HideReason.ManLoop => _manLoop,
        HideReason.Map => _map,
        HideReason.Scoreboard => _scoreboard,
        _ => false,
    };

    private void SetUnsafe(HideReason reason, bool hidden)
    {
        switch (reason)
        {
            case HideReason.Manual: _manual = hidden; break;
            case HideReason.View: _view = hidden; break;
            case HideReason.ManLoop: _manLoop = hidden; break;
            case HideReason.Map: _map = hidden; break;
            case HideReason.Scoreboard: _scoreboard = hidden; break;
        }
    }
}
