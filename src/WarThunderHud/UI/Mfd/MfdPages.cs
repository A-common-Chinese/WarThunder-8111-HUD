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
    Preset,
    Detail,
    ReadOnly,
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
    public string[]? Choices { get; init; }
    public Func<HudConfig, int>? GetChoice { get; init; }
    public Action<HudConfig, int>? SetChoice { get; init; }

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
            case ItemKind.Detail:
                return "";
            case ItemKind.Preset:
                return L10n.Get(LangScope.Screen, "color." + (GetPreset?.Invoke(cfg) ?? "green"));
            case ItemKind.KeyBinding:
                return string.IsNullOrWhiteSpace(GetText?.Invoke(cfg)) ? "—" : GetText!.Invoke(cfg)!;
            default:
                return GetText?.Invoke(cfg) ?? "—";
        }
    }

    /// <summary>direction = +1 for left click / CONFIRM, -1 for right click / SHIFT.</summary>
    public void Activate(HudConfig cfg, int direction = 1)
    {
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
    public List<MfdItem> Items { get; init; } = new();
}

internal static class MfdPages
{
    public static List<MfdPage> Build() => new()
    {
        Display(), Camera(), Alerts(), Keys(), Panel(), Language(), Airframes(), Diag(),
    };

    private static MfdItem Toggle(string label, Func<HudConfig, bool> get, Action<HudConfig, bool> set) =>
        new() { LabelKey = label, Kind = ItemKind.Toggle, GetBool = get, SetBool = set };

    private static MfdItem Number(string label, Func<HudConfig, double> get, Action<HudConfig, double> set,
        double step, double min, double max, string unit = "") =>
        new()
        {
            LabelKey = label, Kind = ItemKind.Number, GetNumber = get, SetNumber = set,
            Step = step, Min = min, Max = max, Unit = unit,
        };

    private static MfdItem Action(string label, Action<HudConfig> run) =>
        new() { LabelKey = label, Kind = ItemKind.Action, Run = run };

    private static MfdItem Info(string label, Func<HudConfig, string> text) =>
        new() { LabelKey = label, Kind = ItemKind.ReadOnly, GetText = text };


    private static MfdItem PresetRow(string label, Func<HudConfig, string> get, Action<HudConfig, string> set) =>
        new() { LabelKey = label, Kind = ItemKind.Preset, GetPreset = get, SetPreset = set };

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

    private static MfdItem Preset(string label, Func<HudConfig, string> get, Action<HudConfig, string> set) =>
        new() { LabelKey = label, Kind = ItemKind.Preset, GetPreset = get, SetPreset = set };

    private static MfdItem Key(string label, Func<HudConfig, string> get, Action<HudConfig, string> set) =>
        new() { LabelKey = label, Kind = ItemKind.KeyBinding, GetText = get, SetText2 = set };

    private static MfdItem Enum(string label, string[] choices, Func<HudConfig, int> get, Action<HudConfig, int> set) =>
        new() { LabelKey = label, Kind = ItemKind.Enum, Choices = choices, GetChoice = get, SetChoice = set };

    private static MfdPage Display() => new()
    {
        TitleKey = "page.display",
        MenuKey = "page.display",
        MenuHintKey = "menu.hint.display",
        Items =
        {
            Toggle("row.showHud", c => c.HudVisible, (c, v) => c.HudVisible = v),
            Toggle("row.hideBg", c => c.HideWhenInactive, (c, v) => c.HideWhenInactive = v),
            Number("row.fov", c => c.VerticalFovDeg, (c, v) => c.VerticalFovDeg = v, 1, 15, 120, "°"),
            Number("row.yoff", c => c.LayoutOffsetYDeg, (c, v) => c.LayoutOffsetYDeg = v, 0.5, -10, 10, "°"),
            Number("row.pitchzero", c => c.PitchZeroDeg, (c, v) => c.PitchZeroDeg = v, 0.1, -20, 20, "°"),
            Number("row.hole", c => c.CursorHoleHalfPx, (c, v) => c.CursorHoleHalfPx = (int)v, 2, 0, 200, "px"),
            Toggle("row.ladder", c => c.ShowPitchLadder, (c, v) => c.ShowPitchLadder = v),
            Toggle("row.fpm", c => c.ShowFpm, (c, v) => c.ShowFpm = v),
            Toggle("row.extrapolate", c => c.Extrapolate, (c, v) => c.Extrapolate = v),
            Info("row.diagState", _ => DiagProbe.StateText),
            Info("row.diagInd", _ => DiagProbe.IndicatorsText),
            Info("row.diagMap", _ => DiagProbe.MapText),
            Info("row.diagInfo", _ => DiagProbe.MapInfoText),
            Info("row.version", _ => "v" + System.Windows.Forms.Application.ProductVersion),
        },
    };

    private static MfdPage Camera() => new()
    {
        TitleKey = "page.camera",
        MenuKey = "page.camera",
        MenuHintKey = "menu.hint.camera",
        Items =
        {
            Toggle("row.hideCockpit", c => c.HideInCockpit, (c, v) => c.HideInCockpit = v),
            Toggle("row.hideNose", c => c.HideInNose, (c, v) => c.HideInNose = v),
            Enum("row.rollCoupleMode", new[] { "opt.auto", "opt.on", "opt.off" },
                c => c.RollCouplingMode == "on" ? 1 : c.RollCouplingMode == "off" ? 2 : 0,
                (c, i2) => c.RollCouplingMode = i2 == 1 ? "on" : i2 == 2 ? "off" : "auto"),
            Number("row.rollCouple", c => c.RollCoupling, (c, v) => c.RollCoupling = v, 0.5, 0, 1),
            Enum("row.bankSign", new[] { "opt.normal", "opt.invert" },
                c => c.BankSign >= 0 ? 0 : 1, (c, i) => c.BankSign = i == 0 ? 1 : -1),
            Enum("row.pitchSign", new[] { "opt.auto", "opt.positive", "opt.negative" },
                c => c.PitchSign switch { > 0 => 1, < 0 => 2, _ => 0 },
                (c, i) => c.PitchSign = i switch { 1 => 1, 2 => -1, _ => 0 }),
            Number("row.bankTrim", c => c.BankTrimDeg, (c, v) => c.BankTrimDeg = v, 0.5, -30, 30, "°"),
            Number("row.fpmTrim", c => c.FpmTrimDeg, (c, v) => c.FpmTrimDeg = v, 0.5, -20, 20, "°"),
            Toggle("row.tapeRadar", c => c.TapeRadarAltitude, (c, v) => c.TapeRadarAltitude = v),
            Toggle("row.aimRmb", c => c.AimOnRightMouse, (c, v) => c.AimOnRightMouse = v),
            Info("row.source", c => c.PitchSign == 0 ? "AUTO" : (c.PitchSign > 0 ? "+1" : "-1")),
        },
    };

    private static MfdPage Alerts() => new()
    {
        TitleKey = "page.alerts",
        MenuKey = "page.alerts",
        MenuHintKey = "menu.hint.alerts",
        Items =
        {
            Number("row.stallAoa", c => c.StallAoaDeg, (c, v) => c.StallAoaDeg = v, 1, 5, 40, "°"),
            Number("row.spaaRange", c => c.FriendlySpaaKm, (c, v) => c.FriendlySpaaKm = v, 5, 0, 200, "km"),
            Number("row.aoaWarn", c => c.AoaWarnDeg, (c, v) => c.AoaWarnDeg = v, 1, 5, 45, "°"),
            Number("row.overspeedMach", c => c.OverspeedMach, (c, v) => c.OverspeedMach = v, 0.05, 0.5, 5, "M"),
            Number("row.overspeedIas", c => c.OverspeedKmh, (c, v) => c.OverspeedKmh = v, 50, 200, 3000, "km/h"),
            Number("row.bingo", c => c.BingoFuelSeconds, (c, v) => c.BingoFuelSeconds = v, 15, 0, 3600, "s"),
            Number("row.afAlert", c => c.EnemyAirfieldAlertKm, (c, v) => c.EnemyAirfieldAlertKm = v, 1, 0, 100, "km"),
            Number("row.edgeMargin", c => c.EdgeMarginFraction, (c, v) => c.EdgeMarginFraction = v, 0.01, 0.01, 0.4),
            Number("row.groundSpeed", c => c.GroundHideSpeedKmh, (c, v) => c.GroundHideSpeedKmh = v, 5, 0, 100, "km/h"),
            Toggle("row.hideGround", c => c.HideOnGround, (c, v) => c.HideOnGround = v),
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
        },
    };

    private static MfdPage Language() => new()
    {
        TitleKey = "page.language",
        MenuKey = "page.language",
        MenuHintKey = "menu.hint.language",
        Items =
        {
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

    private static MfdPage Airframes() => new()
    {
        TitleKey = "page.airframe",
        MenuKey = "page.airframe",
        MenuHintKey = "menu.hint.airframe",
        Items = WtHud.Hud.AirframeDb.All.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
            .Select(Airframe).Cast<MfdItem>().ToList(),
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
            Info("row.version", _ => "v" + System.Windows.Forms.Application.ProductVersion),
            Info("row.endurance", c => c.EnduranceScale.ToString("F2")),
            Info("row.groundDelay", c => c.GroundHideDelayMs.ToString("F0") + "ms"),
            Info("row.mapFlip", c => c.MapYFlip ? "Y" : "N"),
            Info("row.configDebug", c => c.ConfigDebug ? "ON" : "OFF"),
            Info("row.stallMem", c => c.StallAoaByVehicle.Count + " veh"),
            Info("row.pitchMem", c => c.PitchSignByVehicle.Count + " veh"),
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
        var current = System.Windows.Forms.Application.ProductVersion;
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

/// <summary>Small holder for DIAG page requests; wired by the host program.</summary>
internal static class DiagState
{
    public static Action? CopyRequested;
    public static Action? CheckUpdateRequested;

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

    public static void RequestCopy() => CopyRequested?.Invoke();
    public static void RequestCheckUpdate() => CheckUpdateRequested?.Invoke();
}



