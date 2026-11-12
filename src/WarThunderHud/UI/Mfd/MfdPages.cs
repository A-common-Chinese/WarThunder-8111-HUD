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

    /// <summary>当前是否可用。绘制与命令分派都从这里取，避免两处判断不一致。</summary>
    public bool IsEnabled(HudConfig cfg) => EnabledWhen?.Invoke(cfg) ?? true;

    public string ValueText(HudConfig cfg)
    {
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
    public List<MfdItem> Items { get; init; } = new();

    /// <summary>首页显示的数量：动态页面（如回归快照）用它替代 <see cref="Items"/>.Count。</summary>
    public Func<int>? LiveCount { get; init; }
}

internal static class MfdPages
{
    public static List<MfdPage> Build() => new()
    {
        HudDisplay(), Formation(), Attitude(), Alerts(), Keys(),
        Panel(), Airframes(), Snapshots(), Diag(),
    };

    private static MfdItem Toggle(string label, Func<HudConfig, bool> get, Action<HudConfig, bool> set,
                                  Func<HudConfig, bool>? enabledWhen = null) =>
        new() { LabelKey = label, Kind = ItemKind.Toggle, GetBool = get, SetBool = set, EnabledWhen = enabledWhen };

    private static MfdItem Number(string label, Func<HudConfig, double> get, Action<HudConfig, double> set,
        double step, double min, double max, string unit = "", Func<HudConfig, bool>? enabledWhen = null) =>
        new()
        {
            LabelKey = label, Kind = ItemKind.Number, GetNumber = get, SetNumber = set,
            Step = step, Min = min, Max = max, Unit = unit, EnabledWhen = enabledWhen,
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


    private static MfdItem PresetRow(string label, Func<HudConfig, string> get, Action<HudConfig, string> set,
                                     Func<HudConfig, bool>? enabledWhen = null) =>
        new() { LabelKey = label, Kind = ItemKind.Preset, GetPreset = get, SetPreset = set, EnabledWhen = enabledWhen };

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

    /// <summary>入口行摘要：队形名 + 槽位（自定义时不带槽位号）。</summary>
    private static string FormationShapeSummary(HudConfig cfg)
    {
        var shape = WtHud.Hud.FormationGeometry.IsKnownShape(cfg.FormationShape)
            ? cfg.FormationShape : WtHud.Hud.FormationGeometry.ShapeCustom;
        var name = L10n.Get(LangScope.Screen, "opt.shape" + char.ToUpperInvariant(shape[0]) + shape[1..]);
        if (shape == WtHud.Hud.FormationGeometry.ShapeCustom) return name;
        var slots = WtHud.Hud.FormationGeometry.SlotCount(shape);
        var idx = Math.Clamp(cfg.FormationSlot, 0, slots - 1) + 1;
        return name + " · " + idx;
    }

    /// <summary>
    /// 队形（三级页）：选形状与槽位，几何由 <see cref="WtHud.Hud.FormationGeometry"/> 推导。
    /// <para>下面三个距离只在「自定义」时可改 —— 具名队形的槽位是算出来的，手改没有意义，
    /// 所以它们跟着形状变灰（EnabledWhen），而不是藏起来：藏起来会让人以为设置丢了。</para>
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
            // 形状：改形状时把槽位收回到第 1 个，否则会停在一个该队形没有的位置上
            Enum("row.fShape", new[] { "opt.shapeCustom", "opt.shapeEchelon", "opt.shapeColumn", "opt.shapeDiamond" },
                 c => ShapeIndex(c.FormationShape),
                 (c, i) =>
                 {
                     c.FormationShape = WtHud.Hud.FormationGeometry.ShapeIds[i];
                     c.FormationSlot = 0;
                 }),
            Enum("row.fSlot", new[] { "slot.1", "slot.2", "slot.3" },
                 c => Math.Clamp(c.FormationSlot, 0, 2),
                 (c, i) => c.FormationSlot = i,
                 c => c.FormationShape != WtHud.Hud.FormationGeometry.ShapeCustom,
                 "hint.shapeCustomOnly"),
            // 派生几何：只读，改了形状/槽位立刻跟着变，用来看「这个站位到底在哪」
            Info("row.fSlant", c => $"{WtHud.Hud.FormationGeometry.Current(c).RangeM:0} m"),
            Info("row.fSideM", c => $"{WtHud.Hud.FormationGeometry.Current(c).SideM:0} m"),
            Info("row.fBackM", c => $"{WtHud.Hud.FormationGeometry.Current(c).BackM:0} m"),
            // 自定义队形的三块砖（原样保留，默认形状就是自定义，行为不变）
            Number("row.fRange", c => c.FormationRangeM, (c, v) => c.FormationRangeM = v, 10, 200, 1200, "m",
                   c => c.FormationShape == WtHud.Hud.FormationGeometry.ShapeCustom),
            Number("row.fWingOffset", c => c.FormationWingOffsetM, (c, v) => c.FormationWingOffsetM = v, 10, 0, 600, "m",
                   c => c.FormationShape == WtHud.Hud.FormationGeometry.ShapeCustom),
            Number("row.fStationBack", c => c.FormationStationBackM, (c, v) => c.FormationStationBackM = v, 10, 200, 1200, "m",
                   c => c.FormationShape == WtHud.Hud.FormationGeometry.ShapeCustom),
            // 自定义队形的左右选择（就是原来的「僚机侧位」）
            Enum("row.wingSide", new[] { "opt.wingLeft", "opt.wingTrail", "opt.wingRight" },
                 c => c.FormationWingSide + 1, (c, i) => c.FormationWingSide = i - 1,
                 c => c.FormationShape == WtHud.Hud.FormationGeometry.ShapeCustom,
                 "hint.shapeCustomOnly"),
        },
    };

    /// <summary>长机转向辅助（三级页）：按键、目标 G、容差、广播开关，加两行只读。
    /// <para>值是「配置」不是「实时读数」：目标坡度由 G 算出，实际坡度和指令只在 HUD 上看。</para>
    /// </summary>
    private static MfdPage TurnAssistPage() => new()
    {
        Key = "formation.turnassist",
        TitleKey = "page.turnAssist",
        ParentTitleKey = "page.formationTune",
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
            // ---- 开关与角色：原先散在「显示」页第 2 屏，找编队要跳两个模块 ----
            Toggle("row.formation", c => c.FormationAssist,
                   (c, v) => { c.FormationAssist = v; c.FormationOnly = v; }),
            Text("row.leadIcon", c => c.LeadIcon, (c, v) => c.LeadIcon = v),
            Toggle("row.leadMode", c => c.LeadMode, (c, v) => c.LeadMode = v),
            // 队形几何（原「僚机侧位」+ 三个距离）搬进三级页：这些数只跟队形有关，
            // 混在增益和判定里既占屏又容易被误改。入口行显示当前队形 + 槽位。
            Submenu("row.formationShapeMenu", () => FormationShapePage(), FormationShapeSummary),
            // 摘要不要再带箭头：ValueText 会给每个 Submenu 追加一个「›」
            Submenu("row.turnAssistMenu", () => TurnAssistPage(), c =>
                c.FormationTurnTargetG.ToString("F1") + "G"),
            Number("row.leadHeading", c => c.LeadHeadingDeg, (c, v) => c.LeadHeadingDeg = v, 1, -1, 359, "°"),
            Toggle("row.formationOmni", c => c.FormationOmniFollow, (c, v) => c.FormationOmniFollow = v),
            Number("row.leadMach", c => c.LeadMach, (c, v) => c.LeadMach = v, 0.01, 0.30, 2.00, "M"),
            Number("row.formationWarnG", c => c.FormationWarnG, (c, v) => c.FormationWarnG = v, 0.1, 0.5, 5.0, "G"),
            // ---- 状态机判定与保持死区（改站位时这几个要跟着看） ----
            Number("row.fDoneM", c => c.FormationDoneM, (c, v) => c.FormationDoneM = v, 5, 10, 200, "m"),
            Number("row.fDoneHold", c => c.FormationDoneHoldS, (c, v) => c.FormationDoneHoldS = v, 0.5, 0.5, 10, "s"),
            Number("row.fDropM", c => c.FormationDropM, (c, v) => c.FormationDropM = v, 10, 50, 500, "m"),
            Number("row.fSteadyDeg", c => c.FormationSteadyDeg, (c, v) => c.FormationSteadyDeg = v, 0.5, 0, 20, "°"),
            Number("row.fSteadyKmh", c => c.FormationSteadyKmh, (c, v) => c.FormationSteadyKmh = v, 5, 0, 80, "km/h"),
            // 外环比例：距离误差 → 期望接近率。调大追得急，容易冲过头
            Number("row.fKp", c => c.FormationKp, (c, v) => c.FormationKp = v, 0.01, 0.01, 0.50, ""),
            // 近距离比例：进入 blend 距离后从 Kp 渐变到 KpNear，调大刹车更早
            Number("row.fKpNear", c => c.FormationKpNear, (c, v) => c.FormationKpNear = v, 0.01, 0.05, 1.00, ""),
            // 积分：消除稳态误差，给大了会振荡
            // 0.006 在两位小数里显示不出来，所以按千分之一为单位编辑（6 = 0.006）
            Number("row.fKi", c => c.FormationKi * 1000, (c, v) => c.FormationKi = v / 1000.0, 1, 1, 50, "‰"),
            // 接近率环增益
            Number("row.fKv", c => c.FormationKv, (c, v) => c.FormationKv = v, 0.1, 0.2, 3.0, ""),
            // 远近增益融合距离
            Number("row.fBlendM", c => c.FormationBlendM, (c, v) => c.FormationBlendM = v, 50, 100, 2000, "m"),
            // 油门指令的低通时间常数：调大更稳但更慢
            Number("row.fTau", c => c.FormationGuideTauS, (c, v) => c.FormationGuideTauS = v, 0.1, 0.2, 3.0, "s"),
            // 转弯提前量总开关
            Toggle("row.fLeadFeed", c => c.FormationLeadFeed, (c, v) => c.FormationLeadFeed = v),
            // 提前量起作用的转向率门槛
            Number("row.fLeadMin", c => c.FormationLeadMinRate, (c, v) => c.FormationLeadMinRate = v, 0.1, 0.2, 5.0, "°/s"),
            // 提前量上限
            Number("row.fLeadMax", c => c.FormationLeadMaxDeg, (c, v) => c.FormationLeadMaxDeg = v, 1, 5, 45, "°"),
            // 速度前馈上下限（相对长机真空速的倍数）
            Number("row.fMaxRatio", c => c.FormationWingMaxRatio, (c, v) => c.FormationWingMaxRatio = v, 0.05, 1.00, 2.00, "×"),
            Number("row.fMinRatio", c => c.FormationWingMinRatio, (c, v) => c.FormationWingMinRatio = v, 0.05, 0.50, 1.00, "×"),
            // ---- 数据链：编队的高度差靠它，所以放在同一页而不是单独一个模块 ----
            Toggle("row.dataLinkOn", c => c.DataLinkEnabled, (c, v) => c.DataLinkEnabled = v),
            // 对端地址：留空 = 广播（只能同一局域网）；填 ip:port = 单播，跨网段/组网必须填。
            // 改完立即生效 —— 联调时 IP 会变好几次，重启 HUD 太慢。
            Text("row.dataLinkPeer",
                c => HudConfig.PeersToText(c.DataLinkPeers),
                (c, v) =>
                {
                    c.DataLinkPeers = HudConfig.TextToPeers(v);
                    WtHud.Program.ApplyDataLinkPeers(c);
                }),
            Info("row.dataLinkPeerNow", _ => WtHud.Program.DataLinkPeerText()),
            Info("row.dataLinkId", c => c.DataLinkId == 0 ? "-" : c.DataLinkId.ToString(),
                 c => c.DataLinkEnabled),
            Info("row.dataLinkState", _ => WtHud.Program.DataLinkStatusText(), c => c.DataLinkEnabled),
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



