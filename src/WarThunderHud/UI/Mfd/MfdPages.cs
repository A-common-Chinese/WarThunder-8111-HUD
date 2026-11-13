// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 A-common-Chinese
// This file is part of War Thunder HUD (8111), licensed under the GNU AGPL v3.0 or later.

using WtHud.Hud;
using WtHud.UI;

namespace WtHud.UI.Mfd;

internal enum ItemKind
{
    Toggle,
    Number,
    Enum,
    Action,
    KeyBinding,
    Text,
    Preset,
    Detail,
    ReadOnly,

    /// <summary>通往下一级页面的一行。值列显示子页摘要（如「梯队 · 2」），不显示具体数值。</summary>
    Submenu,
}

/// <summary>One cell of a page (two columns x five rows, filled row-major).</summary>
internal sealed class MfdItem
{
    public required string LabelKey { get; init; }

    /// <summary>
    /// 动态行的原文标签（例如「老阳 · F-4S」这种 L10n 里不可能有的文本）。
    /// <para>null = 查 L10n；设置后优先用它。只影响显示，帮助页仍按 <see cref="LabelKey"/> 查。</para>
    /// </summary>
    public string? LabelText { get; init; }

    /// <summary>渲染用的标签文本。</summary>
    public string Label => LabelText ?? L10n.Get(LangScope.Screen, LabelKey);

    public ItemKind Kind { get; init; } = ItemKind.Toggle;
    public double Step { get; init; } = 1;
    public double Min { get; init; } = double.MinValue;
    public double Max { get; init; } = double.MaxValue;
    public string Unit { get; init; } = "";
    public Func<HudConfig, bool>? GetBool { get; init; }
    public Action<HudConfig, bool>? SetBool { get; init; }
    public Func<HudConfig, double>? GetNumber { get; init; }
    public Action<HudConfig, double>? SetNumber { get; init; }
    public Func<HudConfig, string>? GetText { get; init; }
    public Action<HudConfig, string>? SetText2 { get; init; }
    public Func<HudConfig, string>? GetPreset { get; init; }
    public Action<HudConfig, string>? SetPreset { get; init; }
    public Action<HudConfig>? Run { get; init; }

    /// <summary>动作执行后给面板的提示文本（null = 不弹 toast）。</summary>
    public Func<HudConfig, string?>? ToastAfterRun { get; init; }

    /// <summary>True when the action opens its own sub-page instead of the generic confirm sheet.</summary>
    public bool Direct { get; init; }
    public string[]? Choices { get; init; }
    public Func<HudConfig, int>? GetChoice { get; init; }
    public Action<HudConfig, int>? SetChoice { get; init; }

    /// <summary>
    /// 何时可用（null = 永远可用）。联动用：B 只在 A 开启时可用。
    /// <para>★ 必须是 Func 而不是 bool：同页里切了 A 之后 B 要立刻变，
    /// bool 只在 Build() 时算一次就固化了。</para>
    /// </summary>
    public Func<HudConfig, bool>? EnabledWhen { get; init; }

    /// <summary>不可用时右侧显示的文本（替掉 ValueText）。null = 只把整行变灰，不解释原因。</summary>
    public Func<HudConfig, string>? DisabledText { get; init; }

    /// <summary>下一级页面的构造函数（只对 <see cref="ItemKind.Submenu"/> 有效）。每次进入都新建定义，实例由表单按 Key 缓存。</summary>
    public Func<MfdPage>? SubPage { get; init; }

    /// <summary>子页摘要，显示在值列（例如「梯队 · 2」）。null = 只显示一个箭头。</summary>
    public Func<HudConfig, string>? SubSummary { get; init; }

    /// <summary>
    /// 值列的覆盖文本（null = 按 <see cref="Kind"/> 给默认值）。
    /// <para>用于「默认值之外还要说一句」的行：数据链开关在运行中改不了，值列会写
    /// 「开 · 重启生效」，而不是只写「开」让人以为已经生效。</para>
    /// </summary>
    public Func<HudConfig, string>? ValueOverride { get; init; }

    /// <summary>当前是否可用。绘制与命令分派都从这里取，避免两处判断不一致。</summary>
    public bool IsEnabled(HudConfig cfg) => EnabledWhen?.Invoke(cfg) ?? true;

    public string ValueText(HudConfig cfg)
    {
        if (ValueOverride is not null) return ValueOverride(cfg);
        switch (Kind)
        {
            case ItemKind.Toggle:
                return L10n.Get(LangScope.Screen, GetBool?.Invoke(cfg) == true ? "on" : "off");
            case ItemKind.Number:
            {
                var v = GetNumber?.Invoke(cfg) ?? 0;
                var decimals = Step >= 1 ? 0 : Step >= 0.1 ? 1 : 2;
                return v.ToString("F" + decimals, System.Globalization.CultureInfo.InvariantCulture) + Unit;
            }
            case ItemKind.Enum:
            {
                var idx = GetChoice?.Invoke(cfg) ?? 0;
                var key = Choices != null && idx >= 0 && idx < Choices.Length ? Choices[idx] : "";
                return L10n.Get(LangScope.Screen, key);
            }
            case ItemKind.Action:
                return "▶";
            case ItemKind.Submenu:
                return (SubSummary?.Invoke(cfg) ?? "") + " ›";
            case ItemKind.Detail:
                return "";
            case ItemKind.Preset:
                return L10n.Get(LangScope.Screen, "color." + (GetPreset?.Invoke(cfg) ?? "green"));
            case ItemKind.KeyBinding:
                return string.IsNullOrWhiteSpace(GetText?.Invoke(cfg)) ? "—" : GetText!.Invoke(cfg)!;
            case ItemKind.Text:
                return string.IsNullOrWhiteSpace(GetText?.Invoke(cfg)) ? "—" : GetText!.Invoke(cfg)!;
            default:
                return GetText?.Invoke(cfg) ?? "—";
        }
    }

    /// <summary>direction = +1 for left click / CONFIRM, -1 for right click / SHIFT.</summary>
    public void Activate(HudConfig cfg, int direction = 1)
    {
        // 联动项不可用时，左右步进也要一起失效（按钮层只拦住鼠标/侧键）
        if (!IsEnabled(cfg)) return;

        switch (Kind)
        {
            case ItemKind.Toggle:
                SetBool?.Invoke(cfg, !(GetBool?.Invoke(cfg) ?? false));
                break;
            case ItemKind.Number:
            {
                var v = (GetNumber?.Invoke(cfg) ?? 0) + Step * direction;
                v = Math.Clamp(Math.Round(v, 4), Min, Max);
                SetNumber?.Invoke(cfg, v);
                break;
            }
            case ItemKind.Enum:
            {
                if (Choices == null || Choices.Length == 0) break;
                var i = (GetChoice?.Invoke(cfg) ?? 0) + direction;
                i = (i % Choices.Length + Choices.Length) % Choices.Length;
                SetChoice?.Invoke(cfg, i);
                break;
            }
            case ItemKind.Action:
                Run?.Invoke(cfg);
                break;
        }
    }
}

internal sealed class MfdPage
{
    public required string TitleKey { get; init; }
    public required string MenuKey { get; init; }
    public required string MenuHintKey { get; init; }

    /// <summary>页面标识，用于缓存渲染实例（保留选中位置）。空 = 用 TitleKey。</summary>
    public string Key { get; init; } = "";

    /// <summary>下一级页面填父页标题，标题行会显示「父 › 子」。null = 顶层模块。</summary>
    public string? ParentTitleKey { get; init; }

    public string CacheKey => Key.Length > 0 ? Key : TitleKey;

    /// <summary>
    /// 内容会变的页面：每次进入都重建渲染实例，不进缓存。
    /// <para>★ 缓存本身是为了「返回后还停在原来那一行」，但 <c>MfdItemsPage</c> 在构造时就把
    /// <see cref="Items"/> 绑死了 —— 一个会变的页面被缓存下来，等于把第一次打开时的内容冻住。
    /// 「接收配置」就是这么坏的：摘要实时算（`● 1 个配置`），点进去还是空的。</para>
    /// <para>动态页请一律设 true，不要靠把内容拼进 <see cref="Key"/> 绕 —— Key 还兼作
    /// 详情页/帮助页的标识，拼长了会连带影响别处。</para>
    /// </summary>
    public bool NoCache { get; init; }

    public List<MfdItem> Items { get; init; } = new();

    /// <summary>首页显示的数量：动态页面（如回归快照）用它替代 <see cref="Items"/>.Count。</summary>
    public Func<int>? LiveCount { get; init; }
}

internal static class MfdPages
{
    public static List<MfdPage> Build() => new()
    {
        HudDisplay(), Formation(), DataLink(), Attitude(), Alerts(), Keys(),
        Panel(), Airframes(), Snapshots(), Diag(),
    };

    private static MfdItem Toggle(string label, Func<HudConfig, bool> get, Action<HudConfig, bool> set,
                                  Func<HudConfig, bool>? enabledWhen = null, string? disabledKey = null,
                                  Func<HudConfig, string>? valueOverride = null) =>
        new()
        {
            LabelKey = label, Kind = ItemKind.Toggle, GetBool = get, SetBool = set, EnabledWhen = enabledWhen,
            DisabledText = disabledKey is null ? null : _ => L10n.Get(LangScope.Screen, disabledKey),
            ValueOverride = valueOverride,
        };

    private static MfdItem Number(string label, Func<HudConfig, double> get, Action<HudConfig, double> set,
        double step, double min, double max, string unit = "", Func<HudConfig, bool>? enabledWhen = null,
        string? disabledKey = null) =>
        new()
        {
            LabelKey = label, Kind = ItemKind.Number, GetNumber = get, SetNumber = set,
            Step = step, Min = min, Max = max, Unit = unit, EnabledWhen = enabledWhen,
            // 与 Enum 一致：不可用时值列直接说原因，比只变灰好懂
            DisabledText = disabledKey is null ? null : _ => L10n.Get(LangScope.Screen, disabledKey),
        };

    private static MfdItem Action(string label, Action<HudConfig> run,
                                  Func<HudConfig, bool>? enabledWhen = null) =>
        new() { LabelKey = label, Kind = ItemKind.Action, Run = run, EnabledWhen = enabledWhen };

    /// <summary>直接执行的动作，并在执行后显示一条提示（例如导入结果）。</summary>
    private static MfdItem ActionDirectMsg(string label, Action<HudConfig> run, Func<HudConfig, string?> toast,
                                           Func<HudConfig, bool>? enabledWhen = null) =>
        new()
        {
            LabelKey = label, Kind = ItemKind.Action, Run = run, Direct = true,
            ToastAfterRun = toast, EnabledWhen = enabledWhen,
        };

    /// <summary>通往下一级页面。summary 给值列用（不填只有箭头）。</summary>
    private static MfdItem Submenu(string label, Func<MfdPage> build,
                                   Func<HudConfig, string>? summary = null) =>
        new() { LabelKey = label, Kind = ItemKind.Submenu, SubPage = build, SubSummary = summary };

    private static MfdItem Info(string label, Func<HudConfig, string> text,
                                Func<HudConfig, bool>? enabledWhen = null) =>
        new() { LabelKey = label, Kind = ItemKind.ReadOnly, GetText = text, EnabledWhen = enabledWhen };

    private static MfdItem Airframe(string id) =>
        new()
        {
            LabelKey = id,
            Kind = ItemKind.Detail,
            GetText = _ =>
            {
                var p = WtHud.Hud.AirframeDb.Get(id);
                var att = p.Attitude == "game" ? "AHRS:GAME" : "AHRS:EST";
                return $"{att} · RA:{(p.RadarAltitude ? "Y" : "N")} · AB:{(p.Airbrake == "key" ? "KEY" : "TEL")}";
            },
        };

    private static MfdItem Preset(string label, Func<HudConfig, string> get, Action<HudConfig, string> set,
                                  Func<HudConfig, bool>? enabledWhen = null) =>
        new() { LabelKey = label, Kind = ItemKind.Preset, GetPreset = get, SetPreset = set, EnabledWhen = enabledWhen };

    private static MfdItem Key(string label, Func<HudConfig, string> get, Action<HudConfig, string> set,
                               Func<HudConfig, bool>? enabledWhen = null) =>
        new() { LabelKey = label, Kind = ItemKind.KeyBinding, GetText = get, SetText2 = set, EnabledWhen = enabledWhen };

    private static MfdItem Text(string label, Func<HudConfig, string> get, Action<HudConfig, string> set,
                                Func<HudConfig, bool>? enabledWhen = null) =>
        new() { LabelKey = label, Kind = ItemKind.Text, GetText = get, SetText2 = set, EnabledWhen = enabledWhen };

    private static MfdItem Enum(string label, string[] choices, Func<HudConfig, int> get, Action<HudConfig, int> set,
                                Func<HudConfig, bool>? enabledWhen = null, string? disabledKey = null) =>
        new()
        {
            LabelKey = label, Kind = ItemKind.Enum, Choices = choices,
            GetChoice = get, SetChoice = set, EnabledWhen = enabledWhen,
            // 不可用时值列改说原因（比如「仅自定义队形」），比只变灰更好懂
            DisabledText = disabledKey is null ? null : _ => L10n.Get(LangScope.Screen, disabledKey),
        };

    /// <summary>
    /// HUD 显示：画什么、多大、在哪、什么时候自动隐藏。
    /// <para>编队的 8 项已搬到「编队」页 —— 一个主题只留一个入口。</para>
    /// </summary>
    private static MfdPage HudDisplay() => new()
    {
        TitleKey = "page.display",
        MenuKey = "page.display",
        MenuHintKey = "menu.hint.display",
        Items =
        {
            Toggle("row.hideCockpit", c => c.HideInCockpit, (c, v) => c.HideInCockpit = v),
            Toggle("row.hideNose", c => c.HideInNose, (c, v) => c.HideInNose = v),
            Toggle("row.extrapolate", c => c.Extrapolate, (c, v) => c.Extrapolate = v),
            Toggle("row.hideGround", c => c.HideOnGround, (c, v) => c.HideOnGround = v),
            Number("row.groundSpeed", c => c.GroundHideSpeedKmh, (c, v) => c.GroundHideSpeedKmh = v, 5, 0, 100, "km/h"),
            Number("row.groundDelay", c => c.GroundHideDelayMs, (c, v) => c.GroundHideDelayMs = v, 100, 0, 3000, "ms"),
            Toggle("row.showHud", c => c.HudVisible, (c, v) => c.HudVisible = v),
            Number("row.fov", c => c.VerticalFovDeg, (c, v) => c.VerticalFovDeg = v, 1, 15, 120, "°"),
            Number("row.yoff", c => c.LayoutOffsetYDeg, (c, v) => c.LayoutOffsetYDeg = v, 0.5, -10, 10, "°"),
            Number("row.holeX", c => c.CursorHoleOffsetX, (c, v) => c.CursorHoleOffsetX = (int)v, 4, -1200, 1200, "px"),
            Number("row.holeY", c => c.CursorHoleOffsetY, (c, v) => c.CursorHoleOffsetY = (int)v, 4, -1200, 1200, "px"),
            Number("row.hole", c => c.CursorHoleHalfPx, (c, v) => c.CursorHoleHalfPx = (int)v, 2, 0, 200, "px"),
            Toggle("row.ladder", c => c.ShowPitchLadder, (c, v) => c.ShowPitchLadder = v),
            Toggle("row.fpm", c => c.ShowFpm, (c, v) => c.ShowFpm = v),
            Number("row.endurance", c => c.EnduranceScale, (c, v) => c.EnduranceScale = v, 0.05, 0.5, 3.0, ""),
            Toggle("row.configDebug", c => c.ConfigDebug, (c, v) => c.ConfigDebug = v),
            Toggle("row.splashEnabled", c => c.SplashEnabled, (c, v) => c.SplashEnabled = v),
            Text("row.playerName", c => c.PlayerName, (c, v) => c.PlayerName = v),
            // 诊断读数统一放 DIAG 页（这里不再重复）
        },
    };

    /// <summary>
    /// 姿态与校准：HUD 准不准。原先叫「相机」，但里面装的是俯仰零点、坡度耦合、
    /// 符号源这些解算参数，没有一项和相机有关；校准动作的触发键也一并收在这。
    /// </summary>
    private static MfdPage Attitude() => new()
    {
        TitleKey = "page.camera",
        MenuKey = "page.camera",
        MenuHintKey = "menu.hint.camera",
        Items =
        {
            Number("row.pitchzero",
                c => WtHud.Hud.Scoped.Get(c, "row.pitchzero", c.PitchZeroDeg),
                (c, v) => WtHud.Hud.Scoped.Set(c, "row.pitchzero", v, () => c.PitchZeroDeg = v),
                0.1, -20, 20, "°"),
            Enum("row.rollCoupleMode", new[] { "opt.auto", "opt.on", "opt.off" },
                c => c.RollCouplingMode == "on" ? 1 : c.RollCouplingMode == "off" ? 2 : 0,
                (c, i2) => c.RollCouplingMode = i2 == 1 ? "on" : i2 == 2 ? "off" : "auto"),
            Number("row.rollCouple", c => c.RollCoupling, (c, v) => c.RollCoupling = v, 0.5, 0, 1),
            Enum("row.bankSign", new[] { "opt.normal", "opt.invert" },
                c => c.BankSign >= 0 ? 0 : 1,
                (c, i) => c.BankSign = i == 0 ? 1 : -1),
            Enum("row.pitchSign", new[] { "opt.auto", "opt.positive", "opt.negative" },
                c => c.PitchSign switch { > 0 => 1, < 0 => 2, _ => 0 },
                (c, i) => c.PitchSign = i switch { 1 => 1, 2 => -1, _ => 0 }),
            Number("row.bankTrim",
                c => WtHud.Hud.Scoped.Get(c, "row.bankTrim", c.BankTrimDeg),
                (c, v) => WtHud.Hud.Scoped.Set(c, "row.bankTrim", v, () => c.BankTrimDeg = v),
                0.5, -30, 30, "°"),
            Number("row.fpmTrim",
                c => WtHud.Hud.Scoped.Get(c, "row.fpmTrim", c.FpmTrimDeg),
                (c, v) => WtHud.Hud.Scoped.Set(c, "row.fpmTrim", v, () => c.FpmTrimDeg = v),
                0.5, -20, 20, "°"),
            Toggle("row.tapeRadar", c => c.TapeRadarAltitude, (c, v) => c.TapeRadarAltitude = v),
            Toggle("row.aimRmb", c => c.AimOnRightMouse, (c, v) => c.AimOnRightMouse = v),
            Info("row.source", c => c.PitchSign == 0 ? "AUTO" : (c.PitchSign > 0 ? "+1" : "-1")),
            // ---- 校准动作的触发键：校准时人就在这一页，键位跟着放这里比放「按键」页顺手 ----
            Key("row.keyPitchZero", c => c.PitchZeroKey, (c, v) => c.PitchZeroKey = v),
            Key("row.keyBankFlip", c => c.BankFlipKey, (c, v) => c.BankFlipKey = v),
            Key("row.keyBodyFixed", c => c.BodyFixedKey, (c, v) => c.BodyFixedKey = v),
            Key("row.keyReset", c => c.ResetKey, (c, v) => c.ResetKey = v),
            Key("row.keyStall", c => c.StallKey, (c, v) => c.StallKey = v),
        },
    };

    private static MfdPage Alerts() => new()
    {
        TitleKey = "page.alerts",
        MenuKey = "page.alerts",
        MenuHintKey = "menu.hint.alerts",
        Items =
        {
            Number("row.stallAoa",
                c => WtHud.Hud.Scoped.Get(c, "row.stallAoa", c.StallAoaDeg),
                (c, v) => WtHud.Hud.Scoped.Set(c, "row.stallAoa", v, () => c.StallAoaDeg = v),
                1, 5, 40, "°"),
            Number("row.spaaRange", c => c.FriendlySpaaKm, (c, v) => c.FriendlySpaaKm = v, 5, 0, 200, "km"),
            Number("row.aoaWarn",
                c => WtHud.Hud.Scoped.Get(c, "row.aoaWarn", c.AoaWarnDeg),
                (c, v) => WtHud.Hud.Scoped.Set(c, "row.aoaWarn", v, () => c.AoaWarnDeg = v),
                1, 5, 45, "°"),
            Number("row.overspeedMach",
                c => WtHud.Hud.Scoped.Get(c, "row.overspeedMach", c.OverspeedMach),
                (c, v) => WtHud.Hud.Scoped.Set(c, "row.overspeedMach", v, () => c.OverspeedMach = v),
                0.05, 0.5, 5, "M"),
            Number("row.overspeedIas",
                c => WtHud.Hud.Scoped.Get(c, "row.overspeedIas", c.OverspeedKmh),
                (c, v) => WtHud.Hud.Scoped.Set(c, "row.overspeedIas", v, () => c.OverspeedKmh = v),
                50, 200, 3000, "km/h"),
            Number("row.pullUp",
                c => WtHud.Hud.Scoped.Get(c, "row.pullUp", c.PullUpTtiS),
                (c, v) => WtHud.Hud.Scoped.Set(c, "row.pullUp", v, () => c.PullUpTtiS = v),
                0.5, 1.5, 10, " s"),
            Number("row.sinkRate",
                c => WtHud.Hud.Scoped.Get(c, "row.sinkRate", c.SinkRateMs),
                (c, v) => WtHud.Hud.Scoped.Set(c, "row.sinkRate", v, () => c.SinkRateMs = v),
                1, 5, 200, " m/s"),
            Number("row.bingo", c => c.BingoFuelSeconds, (c, v) => c.BingoFuelSeconds = v, 15, 0, 3600, "s"),
            Number("row.afAlert", c => c.EnemyAirfieldAlertKm, (c, v) => c.EnemyAirfieldAlertKm = v, 1, 0, 100, "km"),
            Number("row.edgeMargin", c => c.EdgeMarginFraction, (c, v) => c.EdgeMarginFraction = v, 0.01, 0.01, 0.4),
        },
    };

    private static MfdPage Keys() => new()
    {
        TitleKey = "page.keys",
        MenuKey = "page.keys",
        MenuHintKey = "menu.hint.keys",
        Items =
        {
            Key("row.keyShow", c => c.ShowKey, (c, v) => c.ShowKey = v),
            Key("row.keyZoom", c => c.ZoomKey, (c, v) => c.ZoomKey = v),
            Key("row.keyView", c => c.ViewKey, (c, v) => c.ViewKey = v),
            Key("row.keyMap", c => c.MapKey, (c, v) => c.MapKey = v),
            Key("row.keyScore", c => c.ScoreboardKey, (c, v) => c.ScoreboardKey = v),
            Key("row.keyBrake", c => c.AirbrakeKey, (c, v) => c.AirbrakeKey = v),
            Key("row.keySave", c => c.SaveKey, (c, v) => c.SaveKey = v),
            Key("row.keyHole", c => c.CursorHoleKey, (c, v) => c.CursorHoleKey = v),
            Key("row.keySettings", c => c.SettingsKey, (c, v) => c.SettingsKey = v),
            // 编队辅助开关（长机/僚机都用）：原来只有默认值，面板里改不了 ——
            // 其余热键都能在这一页改，唯独它漏了
            Key("row.keyFormation", c => c.FormationKey, (c, v) => c.FormationKey = v),
            Key("row.keyExit", c => c.ExitKey, (c, v) => c.ExitKey = v),
            Key("row.keyHoldHide", c => c.HoldHideKey, (c, v) => c.HoldHideKey = v),
            // 固定组合键（不可改，只读展示）
            Info("row.keyFix1", _ => "CTRL+ALT+\u2191/\u2193"),
            Info("row.keyFix2", _ => "CTRL+ALT+PgUp/PgDn"),
            Info("row.keyFix3", _ => "CTRL+ALT+R"),
            // 从战雷导出的 controls .blk 导入（减速板 / 计分板）
            ActionDirectMsg("row.importWt",
                c => WtHud.Input.WtControlsImport.PickAndApply(c),
                _ => WtHud.Input.WtControlsImport.LastSummary),
        },
    };

    private static MfdPage Panel() => new()
    {
        TitleKey = "page.panel",
        MenuKey = "page.panel",
        MenuHintKey = "menu.hint.panel",
        Items =
        {
            Preset("row.inkColor", c => c.Mfd.InkColor, (c, v) => c.Mfd.InkColor = v),
            Preset("row.accentColor", c => c.Mfd.AccentColor, (c, v) => c.Mfd.AccentColor = v),
            Toggle("row.realisticKeys", c => c.Mfd.RealisticKeys, (c, v) => c.Mfd.RealisticKeys = v),
            // ---- 语言：原先独立一个模块；3 + 5 项合并后一屏装得下 ----
            Enum("lang.panel", new[] { "opt.en", "opt.zh" },
                _ => L10n.Panel == Lang.Zh ? 1 : 0, (_, i) => L10n.Panel = i == 1 ? Lang.Zh : Lang.En),
            Enum("lang.screen", new[] { "opt.en", "opt.zh" },
                _ => L10n.Screen == Lang.Zh ? 1 : 0, (_, i) => L10n.Screen = i == 1 ? Lang.Zh : Lang.En),
            Enum("lang.alerts", new[] { "opt.en", "opt.zh" },
                _ => L10n.Alerts == Lang.Zh ? 1 : 0, (_, i) => L10n.Alerts = i == 1 ? Lang.Zh : Lang.En),
            Toggle("lang.follow", _ => L10n.FollowSystem, (_, v) => { L10n.FollowSystem = v; L10n.ApplyFollowSystem(); }),
            Action("lang.reset", _ =>
            {
                L10n.Panel = Lang.En;
                L10n.Screen = Lang.Zh;
                L10n.Alerts = Lang.En;
                L10n.FollowSystem = false;
            }),
        },
    };

    /// <summary>入口行摘要：预设名 + 编号（自定义也带编号，编号永远是编号）。</summary>
    private static string FormationShapeSummary(HudConfig cfg)
    {
        var shape = WtHud.Hud.FormationGeometry.IsKnownShape(cfg.FormationShape)
            ? cfg.FormationShape : WtHud.Hud.FormationGeometry.ShapeCustom;
        var name = L10n.Get(LangScope.Screen, "opt.shape" + char.ToUpperInvariant(shape[0]) + shape[1..]);
        var idx = Math.Clamp(cfg.FormationSlot, 0, WtHud.Hud.FormationGeometry.MaxSlots - 1) + 1;
        return name + " · " + idx;
    }

    /// <summary>选预设：把四个数值填好。只在「自定义 → 预设」这一刻存快照，预设之间互切不动它。</summary>
    private static void ApplyShapePreset(HudConfig c, string shape)
    {
        if (!WtHud.Hud.FormationGeometry.IsKnownShape(shape))
            shape = WtHud.Hud.FormationGeometry.ShapeCustom;

        if (shape == WtHud.Hud.FormationGeometry.ShapeCustom)
        {
            c.FormationShape = shape;
            c.RestoreCustom();
            return;
        }

        if (c.FormationShape == WtHud.Hud.FormationGeometry.ShapeCustom) c.SnapshotCustom();
        c.FormationShape = shape;
        c.FormationSlot = Math.Clamp(c.FormationSlot, 0, WtHud.Hud.FormationGeometry.MaxSlots - 1);
        WtHud.Hud.FormationGeometry.ApplyPreset(c, shape, c.FormationSlot);
    }

    /// <summary>手工改四个数值之一：标签立刻落回自定义，并把当前值变成新的自定义快照。</summary>
    private static void ValuesEdited(HudConfig c)
    {
        c.FormationShape = WtHud.Hud.FormationGeometry.ShapeCustom;
        c.SnapshotCustom();
    }

    /// <summary>角色序号：0 = 长机，1..3 = 僚机#N。读的是现有两个字段，不新增配置项。</summary>
    internal static int RoleIndex(HudConfig c)
        => c.LeadMode ? 0 : Math.Clamp(c.FormationSlot, 0, 2) + 1;

    /// <summary>角色身份串（只用来判断「值有没有变」）。</summary>
    private static string RoleId(HudConfig c)
        => c.LeadMode ? "L" : "W" + (Math.Clamp(c.FormationSlot, 0, 2) + 1);

    /// <summary>
    /// 选角色：一次把「能飞」需要的东西配好。
    /// <para>★ 只在角色值真的变化时才动队形，否则「手调完数值、回面板看一眼角色」
    /// 就会把微调重置成标准队形 —— 那是最容易被骂的一种体贴。</para>
    /// <para>长机不碰队形：长机没有站位，把它的编号套给谁都会撞位。</para>
    /// </summary>
    internal static void SetRole(HudConfig c, int index)
    {
        string before = RoleId(c);
        if (index <= 0)
        {
            c.LeadMode = true;
        }
        else
        {
            c.LeadMode = false;
            c.FormationSlot = Math.Clamp(index - 1, 0, 2);
        }
        if (RoleId(c) == before) return;      // 同一个角色：只写字段，不动其它

        c.FormationAssist = true;
        c.DataLinkEnabled = true;
        // ★ 不动 FormationOnly：那是「只显示编队、隐藏其余 HUD」的显示偏好，
        //   由用户自己在编队页决定。选个角色把整个 HUD 变成极简，是以前两件事共用一个
        //   开关留下的副作用（0.4.36 拆开）。
        if (index > 0) ApplyShapePreset(c, WtHud.Hud.FormationGeometry.ShapeEchelon);
    }

    /// <summary>
    /// 判定余量（只读）：完成门槛 vs 链路一拍能飞多远。
    /// <para>这一行存在的唯一目的，是让人在做不到的距离上当场看出「做不到」，
    /// 而不是飞起来才发现到位提示永远等不到。</para>
    /// </summary>
    internal static string MarginText(HudConfig c, double latencyM)
    {
        double band = WtHud.Hud.FormationGeometry.Station(c).RangeM
                      * Math.Max(1.0, c.FormationDonePct) / 100.0;
        if (latencyM <= 0.5) return $"门槛 {band:0} m / 延迟 —";
        return $"门槛 {band:0} m / 延迟 {latencyM:0} m" + (latencyM > band ? " ✗" : "");
    }

    /// <summary>
    /// 队形（三级页）。
    /// <para>★ 0.4.21 起「预设」只是填数按钮：真正的几何永远是下面四行数值；改任一行立刻落回自定义。
    /// 这样「编号」和「侧位」不再各说一套 —— 编号只是编号，侧位永远说了算。</para>
    /// </summary>
    private static MfdPage FormationShapePage() => new()
    {
        Key = "formation.shape",
        TitleKey = "page.formationShape",
        ParentTitleKey = "page.formationTune",
        MenuKey = "page.formationShape",
        MenuHintKey = "menu.hint.formationShape",
        Items =
        {
            // 预设：选一个就把下面四个值填好；切回自定义会还原套预设之前的数值
            Enum("row.fShape", new[] { "opt.shapeCustom", "opt.shapeEchelon", "opt.shapeColumn", "opt.shapeDiamond" },
                 c => ShapeIndex(c.FormationShape),
                 (c, i) => ApplyShapePreset(c, WtHud.Hud.FormationGeometry.ShapeIds[i])),
            // 编号：只是「我是第几号僚机」。预设下换编号会按新编号重填一遍数值
            Enum("row.fSlot", new[] { "slot.1", "slot.2", "slot.3" },
                 c => Math.Clamp(c.FormationSlot, 0, 2),
                 (c, i) =>
                 {
                     c.FormationSlot = i;
                     if (c.FormationShape != WtHud.Hud.FormationGeometry.ShapeCustom)
                         WtHud.Hud.FormationGeometry.ApplyPreset(c, c.FormationShape, i);
                 }),
            // 间距：预设的基准间隔，改它只整体缩放，不落回自定义
            Number("row.fSpacing", c => WtHud.Hud.FormationGeometry.Spacing(c, c.FormationShape),
                   (c, v) =>
                   {
                       WtHud.Hud.FormationGeometry.SetSpacing(c, c.FormationShape, v);
                       if (c.FormationShape != WtHud.Hud.FormationGeometry.ShapeCustom)
                           WtHud.Hud.FormationGeometry.ApplyPreset(c, c.FormationShape, c.FormationSlot);
                   },
                   10, 100, 1500, "m",
                   c => c.FormationShape != WtHud.Hud.FormationGeometry.ShapeCustom,
                   "hint.presetOnly"),
            // 派生几何：只读，改下面任何一项立刻跟着变
            Info("row.fSlant", c => $"{WtHud.Hud.FormationGeometry.Station(c).RangeM:0} m"),
            Info("row.fSideM", c => $"{WtHud.Hud.FormationGeometry.Station(c).SideM:0} m"),
            // 判定余量：完成门槛（斜距 × 完成%）对链路一拍的位移（TAS × 0.25 s）。
            // ★ 原来这里是「纵向」只读行，它和后距输入行是同一个数，占着一格没有新信息。
            Info("row.fMargin", c => MarginText(c, WtHud.Program.FormationLatencyM)),
            // 四个真正的输入：改哪个都立刻落回自定义
            Enum("row.wingSide", new[] { "opt.wingLeft", "opt.wingTrail", "opt.wingRight" },
                 c => c.FormationWingSide + 1,
                 (c, i) => { c.FormationWingSide = i - 1; ValuesEdited(c); }),
            Number("row.fWingOffset", c => c.FormationWingOffsetM,
                   (c, v) => { c.FormationWingOffsetM = v; ValuesEdited(c); }, 10, 0, 600, "m"),
            // 下限 200 m：再近的话 4 Hz 链路的滞后就超过完成门槛（见上面那行判定余量），
            // 那个距离上本来也是目视编队更稳。
            Number("row.fStationBack", c => c.FormationStationBackM,
                   (c, v) => { c.FormationStationBackM = v; ValuesEdited(c); }, 10, 200, 1500, "m"),
            Number("row.fUpM", c => c.FormationUpM,
                   (c, v) => { c.FormationUpM = v; ValuesEdited(c); }, 5, -200, 200, "m"),
        },
    };

    /// <summary>长机转向辅助（三级页）：按键、目标 G、容差、广播开关，加两行只读。
    /// <para>值是「配置」不是「实时读数」：目标坡度由 G 算出，实际坡度和指令只在 HUD 上看。</para>
    /// </summary>
    private static MfdPage TurnAssistPage() => new()
    {
        Key = "formation.turnassist",
        TitleKey = "page.turnAssist",
        ParentTitleKey = "page.formationLead",
        MenuKey = "page.turnAssist",
        MenuHintKey = "menu.hint.turnAssist",
        Items =
        {
            Key("row.turnKey", c => c.FormationTurnAssistKey, (c, v) => c.FormationTurnAssistKey = v),
            Number("row.turnG", c => c.FormationTurnTargetG, (c, v) => c.FormationTurnTargetG = v, 0.1, 1.2, 9.0, "G"),
            Number("row.turnTol", c => c.FormationTurnTolG, (c, v) => c.FormationTurnTolG = v, 0.05, 0.05, 1.50, "G"),
            Toggle("row.turnBcast", c => c.FormationTurnBroadcast, (c, v) => c.FormationTurnBroadcast = v),
            Info("row.turnBank", c => WtHud.Program.TurnAssistTargets(c.FormationTurnTargetG, 0).BankDeg
                                          .ToString("0") + "°"),
            Info("row.turnStatus", _ => WtHud.Program.TurnAssistStatusText()),
            Info("row.turnWing", _ => WtHud.Program.PartnerTurnBankText()),
        },
    };

    private static int ShapeIndex(string shape) =>
        Math.Max(0, Array.IndexOf(WtHud.Hud.FormationGeometry.ShapeIds, shape));

    /// <summary>
    /// 编队调参页：僚机的制导增益与包线。
    /// <para>这些数**不该经常动**，所以单独一页、不混在常用设置里。改动前先看面板 HELP
    /// （每行都有实测依据）；改坏了想回默认，把数字改成帮助里写的默认值即可。</para>
    /// </summary>
    private static MfdPage Formation() => new()
    {
        TitleKey = "page.formationTune",
        MenuKey = "page.formationTune",
        MenuHintKey = "menu.hint.formationTune",
        Items =
        {
            // ---- 角色与开关：两边都要看的留在这一页 ----
            // 角色第 1 行：换角色是"我要飞哪种位子"的第一件事，比辅助开关更靠前
            Enum("row.role", new[] { "opt.roleLead", "opt.roleWing1", "opt.roleWing2", "opt.roleWing3" },
                 RoleIndex,
                 (c, i) => SetRole(c, i)),
            // 编队辅助只写辅助开关。以前它顺手把 FormationOnly 也置上，
            // 于是「开编队辅助」= 整个 HUD 只剩编队信息，两个概念一个开关。
            Toggle("row.formation", c => c.FormationAssist, (c, v) => c.FormationAssist = v),
            // 只显示编队：显示偏好，和辅助分开。灰掉只在没开辅助时说明原因，
            // 不强制清零 —— 有人就想要「极简 HUD + 不画友机方位」。
            Toggle("row.formationOnly", c => c.FormationOnly, (c, v) => c.FormationOnly = v),

            // 队形几何（形状 + 槽位 + 三个距离）：两边必须用同一个队形，放通用页
            Submenu("row.formationShapeMenu", () => FormationShapePage(), FormationShapeSummary),

            // ---- 完成与脱离判定：长机也要跑同一套状态机，所以是通用项 ----
            Number("row.fDonePct", c => c.FormationDonePct, (c, v) => c.FormationDonePct = v, 1, 1, 50, "%"),
            Number("row.fDoneHold", c => c.FormationDoneHoldS, (c, v) => c.FormationDoneHoldS = v, 0.5, 0.5, 10, "s"),
            Number("row.fDropPct", c => c.FormationDropPct, (c, v) => c.FormationDropPct = v, 5, 5, 100, "%"),

            // ---- 两个角色页：各自的专属参数，进去才看得到 ----
            Submenu("row.fWingMenu", () => FormationWingPage(), WingPageSummary),
            Submenu("row.fLeadMenu", () => FormationLeadPage(), LeadPageSummary),

            // ---- 数据链跳板：编队要开数据链，但连接参数不在这页改 ----
            Submenu("row.dlConnMenu", () => DataLinkConnectionPage("page.formationTune"), c =>
                L10n.Get(LangScope.Screen, c.DataLinkEnabled ? "val.linkOn" : "val.linkOff")),
        },
    };

    /// <summary>通用页上「僚机参数」那一行的摘要：槽位和站位容差是进去最常改的两个。</summary>
    private static string WingPageSummary(HudConfig c)
        => string.Format(L10n.Get(LangScope.Screen, "val.slotShort"), c.FormationSlot + 1) + " · " +
           string.Format(L10n.Get(LangScope.Screen, "val.tolShort"), c.FormationAlignDeg.ToString("0"));

    /// <summary>通用页上「长机参数」那一行的摘要：没设马赫就说没设，别显示 0.00。</summary>
    private static string LeadPageSummary(HudConfig c)
        => c.LeadMach > 0.05
            ? string.Format(L10n.Get(LangScope.Screen, "val.machShort"), c.LeadMach.ToString("F2"))
            : L10n.Get(LangScope.Screen, "val.noMach");

    /// <summary>
    /// 僚机页（三级）：只有僚机用得上的东西 —— 长机识别、站位判定、跟随增益、油门与提前量。
    /// <para>这一页原来和长机参数混在编队页里，41 行、四屏；长机打开时那 20 行全是噪音。</para>
    /// </summary>
    private static MfdPage FormationWingPage() => new()
    {
        Key = "formation.wing",
        TitleKey = "page.formationWing",
        ParentTitleKey = "page.formationTune",
        MenuKey = "page.formationWing",
        MenuHintKey = "menu.hint.formationWing",
        Items =
        {
            // ---- 认长机 ----
            // 长机机型只在数据链失效时参与：收到节点后靠位置配对，更准
            Text("row.leadIcon", c => c.LeadIcon, (c, v) => c.LeadIcon = v),
            // 长机节点 ID：0 = 自动。长机模式下强制 0（自己就是长机，这一项没意义）
            Number("row.dataLinkLeadId", c => c.LeadMode ? 0 : c.DataLinkLeadId,
                   (c, v) => { c.DataLinkLeadId = (int)v; WtHud.Program.ApplyDataLinkPeers(c); },
                   1, 0, 999999, ""),
            // 填了跟一行只读状态：ID 抄错了、对方还没开，当场能看出来 —— 不然飞行员只会看到
            // 「找不到长机」，分不清是填错还是对面没起来。
            Info("row.dataLinkLeadState", c => WtHud.Program.DataLinkLeadStateText(c),
                 c => !c.LeadMode && c.DataLinkEnabled),

            // ---- 站位判定：到位/保持/脱离三条线 ----
            Number("row.fAlignDeg", c => c.FormationAlignDeg, (c, v) => c.FormationAlignDeg = v, 1, 0, 45, "°"),
            Number("row.fSteadyDeg", c => c.FormationSteadyDeg, (c, v) => c.FormationSteadyDeg = v, 0.5, 0, 20, "°"),
            Number("row.fSteadyKmh", c => c.FormationSteadyKmh, (c, v) => c.FormationSteadyKmh = v, 5, 0, 80, "km/h"),

            // ---- 垂直引导：门槛决定「多近算到位」，限幅决定「最多让你爬多快」----
            // 引导模式：一段式 = 全套一起显示（默认）；两段式 = 高度没到位只给爬升率
            Enum("row.fVertFirst", new[] { "opt.oneStage", "opt.twoStage" },
                 c => c.FormationVerticalFirst ? 1 : 0,
                 (c, i) => c.FormationVerticalFirst = i == 1),
            Number("row.fVertGate", c => c.FormationVertGateM, (c, v) => c.FormationVertGateM = v, 5, 5, 200, "m"),
            Number("row.fVertMax", c => c.FormationVertMaxMs, (c, v) => c.FormationVertMaxMs = v, 1, 2, 30, "m/s"),
            // 爬升显示距离：只在「只显示爬升率」模式下有意义，-1 = 不显示
            Number("row.fFarRange", c => c.FormationFarRangeM, (c, v) => c.FormationFarRangeM = v, 100, -1, 20000, "m",
                   c => c.FormationVerticalFirst, "hint.vertFirstOnly"),

            // ---- 跟随增益（级联 PID）----
            // 外环比例：距离误差 → 期望接近率。调大追得急，容易冲过头
            Number("row.fKp", c => c.FormationKp, (c, v) => c.FormationKp = v, 0.01, 0.01, 0.50, ""),
            // 近距离比例：进入 blend 距离后从 Kp 渐变到 KpNear，调大刹车更早
            Number("row.fKpNear", c => c.FormationKpNear, (c, v) => c.FormationKpNear = v, 0.01, 0.05, 1.00, ""),
            // 积分：消除稳态误差，给大了会振荡。0.006 在两位小数里显示不出来，按千分之一编辑
            Number("row.fKi", c => c.FormationKi * 1000, (c, v) => c.FormationKi = v / 1000.0, 1, 1, 50, "‰"),
            // 接近率环增益
            Number("row.fKv", c => c.FormationKv, (c, v) => c.FormationKv = v, 0.1, 0.2, 3.0, ""),
            // 远近增益融合距离
            Number("row.fBlendM", c => c.FormationBlendM, (c, v) => c.FormationBlendM = v, 50, 100, 2000, "m"),
            // 油门指令的低通时间常数：调大更稳但更慢
            Number("row.fTau", c => c.FormationGuideTauS, (c, v) => c.FormationGuideTauS = v, 0.1, 0.2, 3.0, "s"),

            // ---- 转弯提前量 ----
            Toggle("row.fLeadFeed", c => c.FormationLeadFeed, (c, v) => c.FormationLeadFeed = v),
            Number("row.fLeadMin", c => c.FormationLeadMinRate, (c, v) => c.FormationLeadMinRate = v, 0.1, 0.2, 5.0, "°/s"),
            Number("row.fLeadMax", c => c.FormationLeadMaxDeg, (c, v) => c.FormationLeadMaxDeg = v, 1, 5, 45, "°"),
            // 长机转弯超过这个 G 就提示「跟不住」—— 是僚机侧的告警，所以放这一页
            Number("row.formationWarnG", c => c.FormationWarnG, (c, v) => c.FormationWarnG = v, 0.1, 0.5, 5.0, "G"),

            // ---- 速度前馈上下限（相对长机真空速的倍数）----
            Number("row.fMaxRatio", c => c.FormationWingMaxRatio, (c, v) => c.FormationWingMaxRatio = v, 0.05, 1.00, 2.00, "×"),
            Number("row.fMinRatio", c => c.FormationWingMinRatio, (c, v) => c.FormationWingMinRatio = v, 0.05, 0.50, 1.00, "×"),
        },
    };

    /// <summary>
    /// 长机页（三级）：长机自己飞什么、喊什么。
    /// <para>这里没有增益 —— 长机不跑跟随环。转向辅助是长机专属的三级页，挂在这一页下面。</para>
    /// </summary>
    private static MfdPage FormationLeadPage() => new()
    {
        Key = "formation.lead",
        TitleKey = "page.formationLead",
        ParentTitleKey = "page.formationTune",
        MenuKey = "page.formationLead",
        MenuHintKey = "menu.hint.formationLead",
        Items =
        {
            Number("row.leadHeading", c => c.LeadHeadingDeg, (c, v) => c.LeadHeadingDeg = v, 1, -1, 359, "°"),
            Toggle("row.formationOmni", c => c.FormationOmniFollow, (c, v) => c.FormationOmniFollow = v),
            Number("row.leadMach", c => c.LeadMach, (c, v) => c.LeadMach = v, 0.01, 0.30, 2.00, "M"),
            // 转向辅助（按键 / 目标 G / 容差 / 广播）：长机专属
            Submenu("row.turnAssistMenu", () => TurnAssistPage(), c =>
                c.FormationTurnTargetG.ToString("F1") + "G"),
            // 僚机自报的站位：谁掉队、掉多少。长机在 HUD 上也能看到同一份
            Info("row.dataLinkWings", _ => WtHud.Program.DataLinkWingText(), c => c.DataLinkEnabled),
        },
    };

    /// <summary>
    /// 数据链（二级模块）：只管连接。
    /// <para>原来这 12 行塞在编队页里，其中 8 行是只读状态，把设置页挤成了四屏。
    /// 现在设置归设置，状态归状态，两个入口（这里和诊断页）打开的是同一份行定义。</para>
    /// </summary>
    private static MfdPage DataLink() => DataLinkConnectionPage(null);

    /// <summary>
    /// 连接页。做两用：主菜单里的模块（<paramref name="parentKey"/> = null），
    /// 以及编队通用页跳过来的三级页（带父标题）。行定义只有这一份。
    /// </summary>
    private static MfdPage DataLinkConnectionPage(string? parentKey) => new()
    {
        Key = parentKey is null ? "datalink.conn" : "datalink.conn." + parentKey,
        TitleKey = "page.datalink",
        ParentTitleKey = parentKey,
        MenuKey = "page.datalink",
        MenuHintKey = "menu.hint.datalink",
        Items =
        {
            // 数据链的收发包线程只在启动时建一次：运行中改这个开关要重启才生效，
            // 值列必须说出来（A4「成对字段靠约定同步」的收口）。
            Toggle("row.dataLinkOn", c => c.DataLinkEnabled, (c, v) => c.DataLinkEnabled = v,
                   valueOverride: WtHud.Program.DataLinkStateText),
            // 地图判定：数据链认出的长机在地图上没显示时给不给水平引导（全真 1 km 外不显示友机）
            Toggle("row.fMapOnly", c => c.FormationMapOnly, (c, v) => c.FormationMapOnly = v,
                   c => !c.LeadMode, "hint.wingOnly"),
            // 对端地址：留空 = 广播（只能同一局域网）；填 ip:port = 单播，跨网段/组网必须填。
            // 改完立即生效 —— 联调时 IP 会变好几次，重启 HUD 太慢。
            Text("row.dataLinkPeer",
                c => HudConfig.PeersToText(c.DataLinkPeers),
                (c, v) =>
                {
                    c.DataLinkPeers = HudConfig.TextToPeers(v);
                    WtHud.Program.ApplyDataLinkPeers(c);
                }),
            // 「发送目标」紧挨着地址：填完就能看实际发了几个，联调时这一步最常看
            Info("row.dataLinkPeerNow", _ => WtHud.Program.DataLinkPeerText()),
            Info("row.dataLinkId", c => c.DataLinkId == 0 ? "-" : c.DataLinkId.ToString(),
                 c => c.DataLinkEnabled),
            // 这一行既是实时状态又是入口：改地址时不用跳页就能看通没通，
            // 想看节点和接口健康就按确认进去。★ 不要再单独放一行只读状态 ——
            // 名字一样的两行「链路状态」，一行能按一行不能按，是最容易点错的那种设计。
            Submenu("row.dlStatusMenu", () => DataLinkStatusPage("page.datalink"),
                    _ => WtHud.Program.DataLinkStatusText()),
            // 配置传递：广播/接收/导出/导入。紧挨状态入口 —— 联调时这两件事总是一起做
            Submenu("row.cfgMenu", () => ConfigMenuPage()),
        },
    };

    /// <summary>
    /// 配置传递（三级页）：广播 / 接收 / 导出 / 从文件导入。
    /// <para>★ 广播走独立端口 48018：对方连数据链都没开也能收到，这正是「新装机上线就能飞」要走的路。</para>
    /// </summary>
    private static MfdPage ConfigMenuPage() => new()
    {
        Key = "datalink.cfg",
        TitleKey = "page.cfgMenu",
        ParentTitleKey = "page.datalink",
        MenuKey = "page.cfgMenu",
        MenuHintKey = "menu.hint.cfgMenu",
        Items =
        {
            ActionDirectMsg("row.cfgBroadcast", c => WtHud.Program.BroadcastFormationConfig(c),
                            _ => WtHud.Program.ConfigActionText),
            // 这一行既是入口也是状态：收到包时摘要会写「● 1 个配置」，不用进去才知道
            Submenu("row.cfgInbox", ConfigInboxPage, _ => WtHud.Program.ConfigInboxSummary()),
            ActionDirectMsg("row.cfgExport", c => WtHud.Program.ExportFormationConfig(c),
                            _ => WtHud.Program.ConfigActionText),
            ActionDirectMsg("row.cfgImport", c => WtHud.Program.ImportFormationConfigFile(c),
                            _ => WtHud.Program.ConfigActionText),
        },
    };

    /// <summary>收到的配置包列表。每行是「谁发来的」，进去才是确认页。</summary>
    private static MfdPage ConfigInboxPage()
    {
        var page = new MfdPage
        {
            Key = "datalink.cfg.inbox",
            // 内容随收到的包变：缓存下来就永远停在第一次打开时那份列表
            NoCache = true,
            TitleKey = "page.cfgInbox",
            ParentTitleKey = "page.cfgMenu",
            MenuKey = "page.cfgInbox",
            MenuHintKey = "menu.hint.cfgInbox",
        };

        var offers = WtHud.Program.ConfigOffers();
        if (offers.Length == 0)
        {
            // 空列表必须说明「为什么空」，否则用户会以为是入口坏了
            page.Items.Add(Info("row.cfgNone", _ => ""));
            return page;
        }

        foreach (var offer in offers)
        {
            var captured = offer;       // 闭包捕获：每行绑定自己那份
            page.Items.Add(new MfdItem
            {
                LabelKey = "row.cfgOffer",
                LabelText = captured.Label,
                Kind = ItemKind.Submenu,
                SubPage = () => ConfigConfirmPage(captured),
                SubSummary = _ => (captured.NodeId.Length > 0 ? "#" + captured.NodeId : captured.Address)
                                  + (captured.TeamVerified ? "" : " · ?"),
            });
        }
        return page;
    }

    /// <summary>确认页：谁发的、他是什么角色、会改你哪些项。确认后才写进草稿，落盘仍靠 SAVE。</summary>
    internal static MfdPage ConfigConfirmPage(WtHud.Telemetry.ConfigOffer offer) => new()
    {
        Key = "datalink.cfg.confirm." + offer.Address + "." + offer.NodeId,
        // 同一台机器改了队形再广播，Key 还是一样的 —— 不重建就会把旧档案写进去
        NoCache = true,
        TitleKey = "page.cfgConfirm",
        ParentTitleKey = "page.cfgInbox",
        MenuKey = "page.cfgConfirm",
        MenuHintKey = "menu.hint.cfgConfirm",
        Items =
        {
            Info("row.cfgFrom", _ => offer.Label
                + (offer.Type.Length > 0 ? " · " + offer.Type : "")),
            Info("row.cfgNode", _ => (offer.NodeId.Length > 0 ? "#" + offer.NodeId : offer.Address)
                + " · " + offer.AgeS.ToString("0") + "s"
                + (offer.TeamVerified ? "" : " · ?")),
            Info("row.cfgRole", _ => offer.Profile.SenderLead
                ? L10n.Get(LangScope.Screen, "opt.roleLead")
                : string.Format(L10n.Get(LangScope.Screen, "opt.roleWingN"), offer.Profile.Slot + 1)),
            Info("row.cfgForm", _ => ProfileSummary(offer.Profile)),
            Info("row.cfgDiff", c => DiffText(c, offer.Profile)),
            ActionDirectMsg("row.cfgApply", c => WtHud.Program.ApplyConfigOffer(c, offer),
                            _ => WtHud.Program.ConfigActionText),
        },
    };

    /// <summary>一份档案的队形摘要：形状 · 编号 · 斜距。</summary>
    internal static string ProfileSummary(WtHud.Hud.FormationProfile p)
    {
        string shape = WtHud.Hud.FormationGeometry.IsKnownShape(p.Shape)
            ? p.Shape : WtHud.Hud.FormationGeometry.ShapeCustom;
        string name = L10n.Get(LangScope.Screen, "opt.shape" + char.ToUpperInvariant(shape[0]) + shape[1..]);
        double slant = Math.Sqrt(p.LateralM * p.LateralM + p.BackM * p.BackM);
        return name + " · " + (p.Slot + 1) + " · " + slant.ToString("0") + " m";
    }

    /// <summary>
    /// 会改哪些项（返回 L10n 行键，便于双语与自检）。
    /// <para>只报「名字」，不报新旧值：MFD 一行放不下两列数字，值进二级页反而更乱。</para>
    /// </summary>
    internal static List<string> DiffKeys(HudConfig c, WtHud.Hud.FormationProfile p)
    {
        var keys = new List<string>();
        string shape = WtHud.Hud.FormationGeometry.IsKnownShape(p.Shape)
            ? p.Shape : WtHud.Hud.FormationGeometry.ShapeCustom;

        if (!p.SenderLead && Math.Clamp(p.Slot, 0, 2) != Math.Clamp(c.FormationSlot, 0, 2))
            keys.Add("row.fSlot");
        if (shape != c.FormationShape) keys.Add("row.fShape");
        if (Math.Abs(p.LateralM - c.FormationWingOffsetM) > 0.5) keys.Add("row.fWingOffset");
        if (Math.Abs(p.BackM - c.FormationStationBackM) > 0.5) keys.Add("row.fStationBack");
        if (Math.Abs(p.UpM - c.FormationUpM) > 0.5) keys.Add("row.fUpM");
        // 间距按「对方那个预设下会用到的值」比：自定义没有间距，不参与
        if (shape != WtHud.Hud.FormationGeometry.ShapeCustom
            && Math.Abs(p.SpacingM - WtHud.Hud.FormationGeometry.Spacing(c, shape)) > 0.5)
            keys.Add("row.fSpacing");
        if (Math.Abs(p.DonePct - c.FormationDonePct) > 0.1) keys.Add("row.fDonePct");
        if (Math.Abs(p.HoldS - c.FormationDoneHoldS) > 0.01) keys.Add("row.fDoneHold");
        if (Math.Abs(p.DropPct - c.FormationDropPct) > 0.1) keys.Add("row.fDropPct");
        if (p.MapOnly != c.FormationMapOnly) keys.Add("row.fMapOnly");
        if (p.Assist != c.FormationAssist) keys.Add("row.formation");
        if ((p.LeadIcon ?? "").Trim() != (c.LeadIcon ?? "").Trim()) keys.Add("row.leadIcon");
        return keys;
    }

    /// <summary>
    /// 差异文案：短名、最多 3 项，多的折成「等 N 项」。
    /// <para>★ 不能直接用行标签（「预设队形」「侧位横偏」）：一格放不下，右对齐的长文本会从左边
    /// 溢出去压住隔壁那一格 —— 这是 0.4.34 出图时肉眼发现的。</para>
    /// </summary>
    internal static string DiffText(HudConfig c, WtHud.Hud.FormationProfile p)
    {
        var keys = DiffKeys(c, p);
        if (keys.Count == 0) return L10n.Get(LangScope.Screen, "cfg.diff.none");
        // 不带「将改：」前缀：这一行的标签已经是「将改动」，前缀只会把值挤到左边压住标签
        var shown = keys.Take(3).Select(k => L10n.Get(LangScope.Screen, "cfg.short." + k));
        string text = string.Join(L10n.Screen == Lang.Zh ? "、" : ", ", shown);
        if (keys.Count > 3)
            text += string.Format(L10n.Get(LangScope.Screen, "cfg.diff.more"), keys.Count - 3);
        return text;
    }

    /// <summary>
    /// 数据链状态（三级页，**两个入口共用**）：数据链页底部一行、诊断页顶部一行。
    /// <para><paramref name="parentKey"/> 既决定标题栏的「父 › 子」，也进缓存键 ——
    /// 两处各缓存一份，行定义是同一段代码，内容不会漂。</para>
    /// </summary>
    private static MfdPage DataLinkStatusPage(string parentKey) => new()
    {
        Key = "datalink.status." + parentKey,
        TitleKey = "page.dataLinkStatus",
        ParentTitleKey = parentKey,
        MenuKey = "page.dataLinkStatus",
        MenuHintKey = "menu.hint.dataLinkStatus",
        Items =
        {
            Info("row.apiHealth", _ => WtHud.Program.ApiHealthZh(WtHud.Program.ApiHealthNow)),
            // 本机角色：由「长机模式」推导，写进每一包，僚机端靠它认出谁是长机
            // ★ 这两个键在 Screen 表里，不在 Panel 表。用错作用域的表现就是屏幕上
            //   直接印出 "val.roleWing" 这个键名 —— 不会报错，只是没人看得懂。
            Info("row.dataLinkRole", c => WtHud.UI.L10n.Get(LangScope.Screen,
                     c.LeadMode ? "val.roleLead" : "val.roleWing"), c => c.DataLinkEnabled),
            Info("row.dataLinkState", _ => WtHud.Program.DataLinkStatusText(), c => c.DataLinkEnabled),
            Info("row.dataLinkNodes", _ => WtHud.Program.DataLinkNodeText(), c => c.DataLinkEnabled),
            // 异队过滤：机场锚点颜色对不上就丢包。数字一直是 0 说明网段里只有自己人
            Info("row.dataLinkTeam", _ => WtHud.Program.DataLinkTeamText(), c => c.DataLinkEnabled),
            // 僚机自报的站位：谁掉队、掉多少。长机 HUD 也有同一份，这行用于复盘
            Info("row.dataLinkWings", _ => WtHud.Program.DataLinkWingText(), c => c.DataLinkEnabled),
            Info("row.dataLinkPort", _ => "48017 / UDP"),
            Info("row.dataLinkDesc", _ => WtHud.UI.L10n.Get(LangScope.Screen, "val.dataLinkDesc")),
        },
    };

    private static MfdPage Airframes() => new()
    {
        TitleKey = "page.airframe",
        MenuKey = "page.airframe",
        MenuHintKey = "menu.hint.airframe",
        Items = WtHud.Hud.AirframeDb.All.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
            .Select(Airframe).Cast<MfdItem>().ToList(),
    };

    /// <summary>回归快照：没有固定行，列表由 <c>SnapshotsPage</c> 直接绘制，这里只占首页一个模块位。</summary>
    private static MfdPage Snapshots() => new()
    {
        TitleKey = "page.snapshots",
        MenuKey = "page.snapshots",
        MenuHintKey = "menu.hint.snapshots",
        LiveCount = () => WtHud.Hud.ConfigSnapshots.ListRestorable().Count,
    };

    private static MfdPage Diag() => new()
    {
        TitleKey = "page.diag",
        MenuKey = "page.diag",
        MenuHintKey = "menu.hint.diag",
        Items =
        {
            // 数据链状态：和数据链页里那一行打开的是同一份行定义（同一个状态页），
            // 标题按来路显示「诊断 › 链路状态」，返回也回到诊断页
            Submenu("row.dlStatusMenu", () => DataLinkStatusPage("page.diag"),
                    _ => WtHud.Program.DataLinkStatusText()),
            Info("row.diagState", _ => DiagProbe.StateText),
            Info("row.diagInd", _ => DiagProbe.IndicatorsText),
            Info("row.diagMap", _ => DiagProbe.MapText),
            Info("row.diagInfo", _ => DiagProbe.MapInfoText),
            Info("row.version", _ => "v" + WtHud.Program.ShortVersion),
            Info("row.mapFlip", c => c.MapYFlip ? "Y" : "N"),
            Info("row.stallMem", c => c.StallAoaByVehicle.Count + " veh"),
            Info("row.pitchMem", c => c.PerVehicleOverrides.Count + " veh"),
            Info("row.dupKeys", c => DiagState.DuplicateKeyHint(c)),
            Action("row.copyDiag", _ => DiagState.RequestCopy()),
            Info("row.update", _ => UpdateState.Text),
            Action("row.checkUpdate", _ => UpdateState.Start()),
        },
    };
}

/// <summary>Shared state for the "check update" action (result shown on the DIAG page).</summary>
internal static class UpdateState
{
    public static string Text { get; private set; } = "—";

    public static void Start()
    {
        var current = WtHud.Program.ShortVersion;
        Text = "…";
        _ = Task.Run(async () =>
        {
            var r = await WtHud.Update.GitHubUpdater.CheckAsync(current, CancellationToken.None);
            Text = r.Message switch
            {
                "newer" => L10n.Screen == Lang.Zh ? $"发现新版本 {r.LatestTag}" : $"NEW {r.LatestTag}",
                "current" => L10n.Screen == Lang.Zh ? "已是最新" : "UP TO DATE",
                "timeout" => L10n.Screen == Lang.Zh ? "检查超时" : "TIMEOUT",
                _ => L10n.Screen == Lang.Zh ? "检查失败" : "CHECK FAILED",
            };
        });
    }
}

/// <summary>Small holder for DIAG page actions.</summary>
internal static class DiagState
{
    /// <summary>把当前诊断信息复制到剪贴板（DIAG 页「复制诊断信息」行）；剪贴板被占用时静默忽略。</summary>
    public static void RequestCopy()
    {
        var text = string.Join("\n\n",
            DiagProbe.StateText, DiagProbe.IndicatorsText, DiagProbe.MapText, DiagProbe.MapInfoText);
        try { System.Windows.Forms.Clipboard.SetText(text); }
        catch { /* 剪贴板忙：忽略，不打断面板 */ }
    }

    public static string DuplicateKeyHint(HudConfig c)
    {
        var keys = new[]
        {
            c.ViewKey, c.ZoomKey, c.MapKey, c.ScoreboardKey, c.SettingsKey,
            c.ShowKey, c.SaveKey, c.ExitKey, c.AirbrakeKey, c.CursorHoleKey,
        }.Where(k => !string.IsNullOrWhiteSpace(k)).ToArray();
        var dup = keys.GroupBy(k => k).Where(g => g.Count() > 1).Select(g => g.Key).ToArray();
        return dup.Length == 0 ? "OK" : string.Join(",", dup);
    }
}



