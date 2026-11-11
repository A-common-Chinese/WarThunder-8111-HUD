// SPDX-License-Identifier: AGPL-3.0-or-later



// Copyright (C) 2026 A-common-Chinese
// This file is part of War Thunder HUD (8111), licensed under the GNU AGPL v3.0 or later.

using System.Globalization;
using WtHud.Telemetry;
using WtHud.Hud;
using WtHud.Graphics;
using WtHud.Overlay;

namespace WtHud;

public static class Program
{
    public static int Main(string[] args)
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

        string mode = args.Length > 0 ? args[0].ToLowerInvariant() : "overlay";   // double-click = start the HUD

        switch (mode)
        {
            case "probe":
                return Probe(args.Length > 1 ? double.Parse(args[1], CultureInfo.InvariantCulture) : 6.0);
            case "mapprobe":
                return MapProbe();
            case "mfd":
                return MfdInteractive();
            case "mfdpreview":
            {
                // 第 3 个参数既可能是数值（edit 页序号），也可能是文本（detail <机型>），按能否解析区分。
                string a3 = args.Length > 3 ? args[3] : "";
                bool hasItem = int.TryParse(a3, NumberStyles.Integer, CultureInfo.InvariantCulture, out int itemIdx);
                return MfdPreview(args.Length > 1 ? args[1] : "mfd-panel.png",
                    args.Length > 2 ? args[2] : "",
                    hasItem ? itemIdx : 0,
                    !hasItem ? a3 : (args.Length > 4 ? args[4] : ""));
            }
            case "selftest":
                return SelfTest();
            case "scopetest":
                return ScopeTest();
            case "preview":
                return Preview(args);
            case "dumpgeo":
                return DumpGeometry(args);
            case "overlay":
                return RunOverlay(args.Length > 0 ? args : new[] { "overlay" });
            case "fusiontest":
                return FusionTest();
            default:
                Console.WriteLine("usage: WtHud                       # start the HUD overlay (default)\n         WtHud overlay [seconds] [demo]\n         WtHud probe [seconds] | replay <file> [speed] | preview <out.png> [w] [h] [variant] | selftest | fusiontest | dumpgeo");
                return 2;
        }
    }

    private static int Probe(double seconds)
    {
        var client = new TelemetryClient(40);
        int count = 0;
        TelemetrySnapshot? last = null;
        client.SnapshotReceived += s => { last = s; count++; };
        client.Start();

        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (clock.Elapsed.TotalSeconds < seconds)
        {
            Thread.Sleep(500);
            if (last != null) Console.WriteLine(last.ToString());
        }
        client.Dispose();

        Console.WriteLine();
        Console.WriteLine($"frames={count}  rate={(count / seconds):F1} Hz  state={client.AvgStateRequestMs:F1}ms ind={client.AvgIndRequestMs:F1}ms cycle={client.AvgCycleMs:F1}ms");
        if (last != null) Console.WriteLine("caps: " + last.Caps);
        return last != null ? 0 : 1;
    }

    private static int SelfTest()
    {
        // Synthetic payloads exercising both vehicle capability profiles.
        string state = """
        {"valid":true,"IAS, km/h":1039,"TAS, km/h":1123,"M":0.92,"H, m":1590,"Vy, m/s":20.5,
         "AoA, deg":0.9,"AoS, deg":0.2,"Ny":1.02,"Wx, deg/s":0.4,"Mfuel, kg":540,"Mfuel0, kg":1800}
        """;
        string indFull = """
        {"valid":true,"army":"air","type":"mig-19j_6a","compass":263.5,"aviahorizon_pitch":-4.55,
         "aviahorizon_roll":-0.51,"bank":-0.70,"turn":-0.0014,"vario":8.19,"altitude_hour":1010.9,
         "ammo_counter1":120,"ammo_counter2":120,"ammo_counter3":60,"fuel":540}
        """;
        string indJet = """
        {"valid":true,"army":"air","type":"j_16","compass":304.1,"throttle":1.1}
        """;

        var a = TelemetryParser.Parse(state, indFull, TimeSpan.Zero, DateTime.UtcNow);
        var b = TelemetryParser.Parse(state, indJet, TimeSpan.Zero, DateTime.UtcNow);

        Check("early jet type", a.VehicleType == "mig-19j_6a");
        Check("early jet attitude", a.PitchDeg.HasValue && a.BankDeg.HasValue);
        Check("early jet caps", a.Caps.HasNativeAttitude && a.Caps.HasAmmo);
        Check("early jet ammo", a.Ammo.Count == 3);
        Check("early jet inflight", a.InFlight);
        Check("top jet no attitude", b.PitchDeg == null && b.BankDeg == null);
        Check("top jet caps", !b.Caps.HasNativeAttitude);
        Check("heading from compass", Math.Abs(a.HeadingDeg - 263.5) < 0.01);
        Check("flight path angle", Math.Abs(a.FlightPathAngleDeg - 3.66) < 0.3);

        // Ground state: garbage AoA must be zeroed.
        string stateGround = state.Replace("\"valid\":true", "\"valid\":true").Replace("\"IAS, km/h\":1039", "\"IAS, km/h\":0")
                                  .Replace("\"AoA, deg\":0.9", "\"AoA, deg\":-28.9");
        var g = TelemetryParser.Parse(stateGround, indFull, TimeSpan.Zero, DateTime.UtcNow);
        Check("ground not inflight", !g.InFlight);
        Check("ground aoa zeroed", Math.Abs(g.AoaDeg) < 0.001);

        // 雷达高度：8111 的 radio_altitude 是英尺，解析时换算成米（1000 ft -> 304.8 m）
        const string indRalt = """{"valid":true,"type":"f-4j","radio_altitude":1000,"compass":90}""";
        var ralt = TelemetryParser.Parse(state, indRalt, TimeSpan.Zero, DateTime.UtcNow);
        Check("radio altitude ft->m", ralt.RadioAltM is double rm && Math.Abs(rm - 304.8) < 0.01);
        const string indRaltZero = """{"valid":true,"type":"f-4j","radio_altitude":0,"compass":90}""";
        var ralt0 = TelemetryParser.Parse(state, indRaltZero, TimeSpan.Zero, DateTime.UtcNow);
        Check("radio altitude zero", ralt0.RadioAltM is double rz && Math.Abs(rz) < 0.001);

        // 战雷 controls .blk 导入：解析 + 组合键还原（减速板 H / 计分板 CTRL+N）
        const string blk = """
        ID_AIR_BRAKE { keyboardKey:i=35; }
        ID_MPSTATSCREEN { keyboardKey:i=29; keyboardKey:i=49; }
        """;
        var wt = WtHud.Input.WtControlsFile.Parse(blk);
        Check("blk airbrake parsed", wt.TryGetValue("ID_AIR_BRAKE", out var ab) && ab.Length == 1);
        Check("blk airbrake binding H", WtHud.Input.WtControlsFile.BuildBinding(new[] { 35 }) == "H");
        Check("blk scoreboard binding CTRL+N",
            WtHud.Input.WtControlsFile.BuildBinding(new[] { 29, 49 }) == "CTRL+N");

        // SPLASH 解析：只认「击落了」，剥掉联队/颜色标记（^DFCN^ 等）和零宽字符
        const string killLine = "^DFCN^ ACCvTARDISdevice (F/A-18A) 击\u200B落\u200B了 =FdX= The_Gho\u0161t (F-4S)";
        Check("killfeed parse own kill",
            HudKillFeed.TryParse(killLine, out var kActor, out var kType, out var kTarget)
            && kActor == "ACCvTARDISdevice" && kType == "F/A-18A"
            && kTarget == "=FdX= The_Gho\u0161t (F-4S)");
        Check("killfeed own name case-insensitive",
            HudKillFeed.IsOwnKill(killLine, "accvtardisdevice", out var kT2) && kT2.StartsWith("=FdX="));
        Check("killfeed ignores other players", !HudKillFeed.IsOwnKill(killLine, "someone_else", out _));
        Check("killfeed ignores AI destroy",
            !HudKillFeed.TryParse("^DFCN^ ACCvTARDISdevice (F/A-18A) 击\u200B毁\u200B了 AI\u96C5\u514B-38M", out _, out _, out _));
        Check("killfeed ignores awards",
            !HudKillFeed.TryParse("\u22C6 DFCN^ ACCvTARDISdevice (F/A-18A) \u83B7\u200B\u5F97\u200B\u5609\u200B\u5956", out _, out _, out _));
        Check("killfeed requires name", !HudKillFeed.IsOwnKill(killLine, "", out _));

        Console.WriteLine("selftest done");
        return Environment.ExitCode;
    }

    /// <summary>
    /// 按机型设置的作用域自检：每台机型只读自己的覆盖值（不能串台）、优先级 覆盖 &gt; 档案 &gt; 全局。
    /// 曾经的问题：详情页用 Scoped.Get（跟着面板当前编辑的机型走）读值，导致 j16 的数据覆盖 f4j 的显示；
    /// 且运行时根本没读 perVehicleOverrides，这些「按机型」参数实际不生效。
    /// </summary>
    private static int ScopeTest()
    {
        Console.WriteLine("=== per-airframe setting scope ===");
        int failed = 0;
        void Check(string name, double got, double want)
        {
            bool pass = Math.Abs(got - want) < 1e-6;
            if (!pass) failed++;
            Console.WriteLine($"  {(pass ? "PASS" : "FAIL")}  {name,-46} got={got,8:F2} want={want,8:F2}");
        }

        var cfg = new HudConfig
        {
            AoaWarnDeg = 18, OverspeedKmh = 1400, OverspeedMach = 1.80,
            StallAoaDeg = 25, SinkRateMs = 40, PitchZeroDeg = 0.5, BankTrimDeg = 0.2, FpmTrimDeg = -0.1,
        };
        cfg.PerVehicleOverrides.Clear();
        WtHud.Hud.SettingsRouter.SetFor(cfg, "f-4j", "row.overspeedIas", 1458);
        WtHud.Hud.SettingsRouter.SetFor(cfg, "f-4j", "row.pitchzero", 1.5);
        WtHud.Hud.SettingsRouter.SetFor(cfg, "j_16", "row.overspeedIas", 1500);
        WtHud.Hud.SettingsRouter.SetFor(cfg, "j_16", "row.overspeedMach", 2.20);
        WtHud.Hud.SettingsRouter.SetFor(cfg, "j_16", "row.stallAoa", 9.5);

        var f4 = new AirframeProfile { OverspeedIas = 1300, OverspeedMach = 2.00, AoaWarn = 16 };
        var j16 = new AirframeProfile();
        var f16 = new AirframeProfile { OverspeedIas = 1400, AoaWarn = 18 };

        Check("f-4j IAS override > profile", WtHud.Hud.EffectiveSettings.OverspeedKmh(cfg, "f-4j", f4), 1458);
        Check("j_16 IAS override independent", WtHud.Hud.EffectiveSettings.OverspeedKmh(cfg, "j_16", j16), 1500);
        Check("j_16 Mach override", WtHud.Hud.EffectiveSettings.OverspeedMach(cfg, "j_16", j16), 2.20);
        Check("j_16 stall override", WtHud.Hud.EffectiveSettings.StallAoaDeg(cfg, "j_16", j16), 9.5);
        Check("f-4j Mach keeps profile value", WtHud.Hud.EffectiveSettings.OverspeedMach(cfg, "f-4j", f4), 2.00);
        Check("f-4j aoaWarn keeps profile value", WtHud.Hud.EffectiveSettings.AoaWarnDeg(cfg, "f-4j", f4), 16);
        Check("f_16a profile IAS (no override)", WtHud.Hud.EffectiveSettings.OverspeedKmh(cfg, "f_16a_block_10", f16), 1400);
        Check("unknown vehicle falls back to global IAS", WtHud.Hud.EffectiveSettings.OverspeedKmh(cfg, "zzz_none", new AirframeProfile()), 1400);
        Check("unknown vehicle falls back to global Mach", WtHud.Hud.EffectiveSettings.OverspeedMach(cfg, "zzz_none", new AirframeProfile()), 1.80);
        Check("unknown vehicle falls back to global aoaWarn", WtHud.Hud.EffectiveSettings.AoaWarnDeg(cfg, "zzz_none", new AirframeProfile()), 18);
        Check("unknown vehicle falls back to global stall", WtHud.Hud.EffectiveSettings.StallAoaDeg(cfg, "zzz_none", new AirframeProfile()), 25);
        Check("f-4j pitch-zero override", WtHud.Hud.EffectiveSettings.PitchZeroDeg(cfg, "f-4j"), 1.5);
        Check("j_16 pitch-zero falls back to global", WtHud.Hud.EffectiveSettings.PitchZeroDeg(cfg, "j_16"), 0.5);
        Check("f-4j bank-trim falls back to global", WtHud.Hud.EffectiveSettings.BankTrimDeg(cfg, "f-4j"), 0.2);
        Check("detail page f-4j IAS = own override", WtHud.Hud.SettingsRouter.GetForVehicle(cfg, "f-4j", "row.overspeedIas", f4.OverspeedIas), 1458);
        Check("detail page j_16 IAS = own override", WtHud.Hud.SettingsRouter.GetForVehicle(cfg, "j_16", "row.overspeedIas", j16.OverspeedIas), 1500);
        Check("detail page f-4j does not leak j_16 stall", WtHud.Hud.SettingsRouter.GetForVehicle(cfg, "f-4j", "row.stallAoa", f4.StallAoa), 0);

        // 下降率告警：机型覆盖 > 机型档案 > 全局
        WtHud.Hud.SettingsRouter.SetFor(cfg, "f-4j", "row.sinkRate", 25);
        Check("f-4j sink override > profile",
            WtHud.Hud.EffectiveSettings.SinkRateMs(cfg, "f-4j", new AirframeProfile { SinkRate = 30 }), 25);
        Check("j_16 sink falls back to global",
            WtHud.Hud.EffectiveSettings.SinkRateMs(cfg, "j_16", new AirframeProfile()), 40);
        Check("profile sink used without override",
            WtHud.Hud.EffectiveSettings.SinkRateMs(cfg, "zzz_none", new AirframeProfile { SinkRate = 30 }), 30);

        // 详情页「来源」标注：全局回退必须标成 Global，否则会被显示成「默认」，看着像没生效。
        void CheckSource(string name, WtHud.Hud.ValueSource got, WtHud.Hud.ValueSource want)
        {
            bool pass = got == want;
            if (!pass) failed++;
            Console.WriteLine($"  {(pass ? "PASS" : "FAIL")}  {name,-46} got={got} want={want}");
        }
        var (vIas, sIas) = WtHud.Hud.EffectiveSettings.Resolve(cfg, "f-4j", "row.overspeedIas", f4.OverspeedIas, cfg.OverspeedKmh);
        Check("f-4j IAS source = Override", vIas, 1458);
        CheckSource("f-4j IAS source tag", sIas, WtHud.Hud.ValueSource.Override);
        var (vMach, sMach) = WtHud.Hud.EffectiveSettings.Resolve(cfg, "f-4j", "row.overspeedMach", f4.OverspeedMach, cfg.OverspeedMach);
        CheckSource("f-4j Mach source tag = Profile", sMach, WtHud.Hud.ValueSource.Profile);
        Check("f-4j Mach resolved value", vMach, 2.00);
        var (vA10Ias, sA10Ias) = WtHud.Hud.EffectiveSettings.Resolve(cfg, "a_10a", "row.overspeedIas", 0, 850);
        Check("a_10a falls back to global IAS", vA10Ias, 850);
        CheckSource("a_10a IAS source tag = Global", sA10Ias, WtHud.Hud.ValueSource.Global);
        var (vA10Stall, sA10Stall) = WtHud.Hud.EffectiveSettings.Resolve(cfg, "a_10a", "row.stallAoa", 0, cfg.StallAoaDeg);
        Check("a_10a falls back to global stall", vA10Stall, 25);
        CheckSource("a_10a stall source tag = Global", sA10Stall, WtHud.Hud.ValueSource.Global);
        var (vA10Aoa, sA10Aoa) = WtHud.Hud.EffectiveSettings.Resolve(cfg, "a_10a", "row.aoaWarn", 0, 0);
        CheckSource("no value on any layer = None", sA10Aoa, WtHud.Hud.ValueSource.None);
        Check("no value on any layer = 0", vA10Aoa, 0);

        // 「自动」写入层：识别到机型时应写该机型覆盖，而不是全局（v0.3.2 的 bug）
        WtHud.Hud.AirframeDb.CurrentVehicle = "f-4j";
        WtHud.Hud.EditScope.UseAuto();
        cfg.PerVehicleOverrides.Clear();
        cfg.OverspeedKmh = 1400;
        WtHud.Hud.Scoped.Set(cfg, "row.overspeedIas", 1600, () => cfg.OverspeedKmh = 1600);
        Check("auto write -> airframe override",
            WtHud.Hud.SettingsRouter.GetForVehicle(cfg, "f-4j", "row.overspeedIas", -1), 1600);
        Check("auto write keeps global", cfg.OverspeedKmh, 1400);

        WtHud.Hud.EditScope.UseGlobal();
        WtHud.Hud.Scoped.Set(cfg, "row.overspeedIas", 1500, () => cfg.OverspeedKmh = 1500);
        Check("explicit global writes global", cfg.OverspeedKmh, 1500);
        Check("explicit global leaves override",
            WtHud.Hud.SettingsRouter.GetForVehicle(cfg, "f-4j", "row.overspeedIas", -1), 1600);

        WtHud.Hud.AirframeDb.CurrentVehicle = "";
        WtHud.Hud.EditScope.UseAuto();
        WtHud.Hud.Scoped.Set(cfg, "row.overspeedIas", 1700, () => cfg.OverspeedKmh = 1700);
        Check("auto without airframe writes global", cfg.OverspeedKmh, 1700);

        // PULL UP 提前量：机型覆盖 > 机型档案 > 全局
        WtHud.Hud.SettingsRouter.SetFor(cfg, "f-4j", "row.pullUp", 6.5);
        Check("f-4j pull-up override > profile",
            WtHud.Hud.EffectiveSettings.PullUpTtiS(cfg, "f-4j", new AirframeProfile { PullUpTti = 4.5 }), 6.5);
        Check("j_16 pull-up profile fallback",
            WtHud.Hud.EffectiveSettings.PullUpTtiS(cfg, "j_16", new AirframeProfile { PullUpTti = 4.0 }), 4.0);
        Check("unknown pull-up global default",
            WtHud.Hud.EffectiveSettings.PullUpTtiS(cfg, "zzz_none", new AirframeProfile()), 5.0);

        WtHud.Hud.AirframeDb.CurrentVehicle = "f-4j";
        WtHud.Hud.Scoped.Set(cfg, "row.realisticKeys", 1, () => cfg.Mfd.RealisticKeys = true);
        Check("global row always global", cfg.Mfd.RealisticKeys ? 1 : 0, 1);
        Check("global row never override",
            WtHud.Hud.SettingsRouter.GetForVehicle(cfg, "f-4j", "row.realisticKeys", -1), -1);

        Console.WriteLine(failed == 0 ? "scopetest done" : $"scopetest FAILED ({failed})");
        return failed == 0 ? 0 : 1;
    }

    private static int DumpGeometry(string[] args)
    {
        int w = args.Length > 1 ? int.Parse(args[1], CultureInfo.InvariantCulture) : 1920;
        int h = args.Length > 2 ? int.Parse(args[2], CultureInfo.InvariantCulture) : 1080;
        double dumpPitch = args.Length > 3 ? double.Parse(args[3], CultureInfo.InvariantCulture) : 8;
        double dumpBank = args.Length > 4 ? double.Parse(args[4], CultureInfo.InvariantCulture) : 24;
        var snap = new TelemetrySnapshot
        {
            Valid = true, InFlight = true, VehicleType = "j_16",
            IasKmh = 912, TasKmh = 1044, Mach = 0.87, AltM = 3520, VyMs = 12.4,
            AoaDeg = 5.2, AosDeg = 1.1, NyG = 2.4, HeadingDeg = 275, FuelKg = 4100, Fuel0Kg = 9400,
        };
        var att = new AttitudeSolution { PitchDeg = dumpPitch, BankDeg = dumpBank, Source = AttitudeSource.Estimated, Confidence = 1 };
        var cal = new HudCalibration { VerticalFovDeg = 45 };
        var geo = new HudGeometry(new HudOptions(), cal);
        var prims = geo.Build(snap, att, w, h);
        Console.WriteLine($"px/deg={cal.EffectivePxPerDeg(h):F2} center=({w / 2.0:F0},{h / 2.0:F0}) prims={prims.Count}");
        foreach (var p in prims)
        {
            string pts = string.Join(" ", p.Points.Select(q => $"{q.X:F1},{q.Y:F1}"));
            Console.WriteLine($"{p.Kind,-8} w={p.Width:F1} fs={p.FontSize:F1} al={p.Align,-6} t='{p.Text}' pts=[{pts}]");
        }
        return 0;
    }

    private static int FusionTest()
    {
        Console.WriteLine("=== attitude fusion scenarios (25 Hz) ===");
        FusionScenario("level straight  bank~0", bankExpected: 0, pitchExpected: 2.0,
            heading: _ => 90, tas: _ => 900, vy: _ => 0, aoa: _ => 2.0, ny: _ => 1.0);

        FusionScenario("level turn 3 deg/s  bank~53", bankExpected: 53.2, pitchExpected: 2.0,
            heading: t => 90 + 3.0 * t, tas: _ => 900, vy: _ => 0, aoa: _ => 2.0, ny: _ => 1.55);

        FusionScenario("loop / high G  bank~0", bankExpected: 0, pitchExpected: 33.7,
            heading: _ => 90, tas: _ => 800, vy: t => 60, aoa: _ => 18.0, ny: _ => 7.0);

        return Environment.ExitCode;
    }

    private static void FusionScenario(string name, double bankExpected, double pitchExpected,
        Func<double, double> heading, Func<double, double> tas, Func<double, double> vy,
        Func<double, double> aoa, Func<double, double> ny)
    {
        var fusion = new AttitudeFusion();
        AttitudeSolution att = default;
        double t = 0;
        for (int i = 0; i < 250; i++)          // 10 s at 25 Hz
        {
            t = i * 0.04;
            var snap = new TelemetrySnapshot
            {
                Valid = true, InFlight = true, ElapsedSeconds = t,
                HeadingDeg = heading(t), HasHeading = true,
                TasKmh = tas(t), IasKmh = tas(t) * 0.9,
                VyMs = vy(t), AoaDeg = aoa(t), NyG = ny(t), AltM = 2000,
            };
            att = fusion.Update(snap);
        }
        bool pass = Math.Abs(att.BankDeg - bankExpected) < 6 && Math.Abs(att.PitchDeg - pitchExpected) < 6;
        Console.WriteLine($"  {(pass ? "PASS" : "FAIL")}  {name,-28} got bank={att.BankDeg,6:F1} (want {bankExpected,5:F1})  " +
                          $"pitch={att.PitchDeg,6:F1} (want {pitchExpected,5:F1})  src={att.Source}");
        if (!pass) Environment.ExitCode = 1;
    }

    /// <summary>Opens the MFD settings panel (F1) on its own STA thread.</summary>
    private static void OpenMfdSettings(HudConfig cfg, HudCalibration cal, Action<bool> setShow)
    {
        // 面板已打开：F1 变成“关闭”，避免叠出多个设置窗口
        if (_mfdPanel is { } openPanel)
        {
            try { openPanel.RequestClosePanel(); } catch { }
            return;
        }

        var thread = new Thread(() =>
        {
            try
            {
                settingsOpen = true;
                NativeMethods.SetCursor(NativeMethods.LoadCursor(IntPtr.Zero, (IntPtr)32512));
                using var form = new WtHud.UI.Mfd.MfdFrameForm(cfg);
                _mfdPanel = form;
                form.ShowDialog();
                _mfdPanel = null;
                if (form.Saved)
                {
                    cal.VerticalFovDeg = cfg.VerticalFovDeg;
                    cal.LayoutOffsetYDeg = cfg.LayoutOffsetYDeg;

                    // 面板保存后重新解析键位并刷新热键，否则自定义按键要重启才生效
                    // 俯仰符号按机型记忆：F-4J 这类反向机设一次就永久生效
                    if (cfg.PitchSign != 0 && !string.IsNullOrEmpty(AirframeDb.CurrentVehicle))
                    {
                        WtHud.Hud.SettingsRouter.SetFor(cfg, AirframeDb.CurrentVehicle, "row.pitchSign", cfg.PitchSign);
                        Console.WriteLine($"pitch sign saved for {AirframeDb.CurrentVehicle}: {cfg.PitchSign}");
                    }

                    RebindeKeys(cfg);
                    pendingHotkeyRefresh = true;     // 由 overlay 线程重建热键（线程亲和性）
                    overlayRef?.SetCursorHole(cfg.CursorHoleHalfPx);
                    overlayRef?.SetCursorHoleCenter(cfg.CursorHoleOffsetX, cfg.CursorHoleOffsetY);
                    Console.WriteLine("MFD settings saved (keys rebound)");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("MFD settings error: " + ex.Message);
            }
            finally
            {
                settingsOpen = false;
            }
        })
        { IsBackground = true, Name = "mfd-settings" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    private static int RunOverlay(string[] args)
    {
        double seconds = args.Length > 1 && double.TryParse(args[1], NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedSeconds) ? parsedSeconds : 0;   // 0 = run until exit
        bool demo = args.Length > 2 && args[2].Equals("demo", StringComparison.OrdinalIgnoreCase);
        bool perf = args.Any(a => a.Equals("--perf", StringComparison.OrdinalIgnoreCase));

        // 单实例：两个 WtHud 同时跑会互相偷热键（后起的那个 RegisterHotKey 报 err=1409，F10/F1 就
        // 归了先起的那个），而且先起的那个压在游戏上会把点击吃掉（旧版本连点击回跳都没有），
        // 表现就是"HUD 还在、但整块屏幕点不动"。自动提权重启时旧进程要等用户点 UAC 才退，
        // 所以 --elevated-retry 多等一会儿再判定。
        bool elevatedRetry = args.Any(a => a.Equals("--elevated-retry", StringComparison.OrdinalIgnoreCase));
        var singleInstance = new Mutex(false, @"Local\WtHudOverlaySingleInstance");
        bool owned;
        try { owned = singleInstance.WaitOne(elevatedRetry ? TimeSpan.FromSeconds(10) : TimeSpan.Zero); }
        catch (AbandonedMutexException) { owned = true; }   // 上一个实例是被强杀的，锁归我们
        if (!owned)
        {
            Console.WriteLine("another WtHud overlay is already running -- exiting (single instance).\n" +
                              "  已经有一个 WtHud 在跑了。两个实例会互相抢热键（F10 失灵）、叠加 HUD，\n" +
                              "  旧实例还会把游戏窗口上的点击吃掉。请在任务管理器里结束所有 WtHud.exe 后只开一个。");
            // 双击启动时没有可见控制台，必须弹窗，否则用户只看到"双击了没反应"。
            if (!elevatedRetry)
            {
                try
                {
                    System.Windows.Forms.MessageBox.Show(
                        "已经有一个 WtHud 在运行了。\n\n" +
                        "同时开两个会互相抢热键（F10/F1 可能失灵）、HUD 叠在一起，\n" +
                        "而且旧的那个会把游戏画面上的点击吃掉。\n\n" +
                        "请在任务管理器里结束所有 WtHud.exe，然后只启动一个。",
                        "WtHud", System.Windows.Forms.MessageBoxButtons.OK,
                        System.Windows.Forms.MessageBoxIcon.Warning);
                }
                catch { }
            }
            return 3;
        }

        NativeMethods.SetProcessDpiAwarenessContext(NativeMethods.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);

        // 帮助文案的版本检查放在启动时做，而不是等用户按 HELP 才触发：
        // 否则老用户目录里那份旧 help-desc.json 会一直是旧的，出了问题看文件也会误判。
        try { WtHud.UI.Mfd.HelpDescriptions.EnsureLoaded(); } catch { }

        // 跟随游戏客户区：窗口化 / 副屏 / 非 100% 缩放都能对上；找不到窗口时回退主屏
        // 覆盖层只覆盖游戏所在的那块显示器：不会挡住其它屏幕的点击
        int monX = 0, monY = 0, monW = 0, monH = 0;
        bool tracking0 = GameWindowTracker.TryGetClientRect(out _, out _, out _, out _, out _);
        bool haveMonitor = tracking0 && GameWindowTracker.TryGetMonitorRect(out monX, out monY, out monW, out monH);
        int scrX = haveMonitor ? monX : 0;
        int scrY = haveMonitor ? monY : 0;
        int scrW = haveMonitor ? monW : NativeMethods.GetSystemMetrics(NativeMethods.SM_CXSCREEN);
        int scrH = haveMonitor ? monH : NativeMethods.GetSystemMetrics(NativeMethods.SM_CYSCREEN);
        if (scrW <= 0 || scrH <= 0)
        {
            scrX = scrY = 0;
            scrW = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXSCREEN);
            scrH = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYSCREEN);
        }
        int w = scrW, h = scrH;                     // 覆盖层继续铺满整屏
        bool tracking = GameWindowTracker.TryGetClientRect(out var wx, out var wy, out var gw, out var gh, out _);
        Console.WriteLine($"overlay virtual {scrX},{scrY} {scrW}x{scrH}  game {(tracking ? $"{wx},{wy} {gw}x{gh}" : "not found")} " +
                          $"for {(seconds <= 0 ? "unlimited" : seconds + "s")} demo={demo}");

        telemetry = new TelemetryClient(0);
        fusion = new AttitudeFusion();
        telemetry.SnapshotReceived += s =>
        {
            prevSnapshot = latest;
            prevAttitude = lastAttitude;

            // View-state resync: a new vehicle or entering a match always starts in the
            // third-person camera, so the V counter must be reset.
            if (!string.IsNullOrEmpty(s.VehicleType) && s.VehicleType != lastVehicle)
            {
                lastVehicle = s.VehicleType;
                AirframeDb.CurrentVehicle = s.VehicleType;
                // 机型数据库只收飞机：坦克/地面载具不参与飞行告警阈值
                if (s.VehicleType.StartsWith("tankModels", StringComparison.OrdinalIgnoreCase)
                    || s.VehicleType.StartsWith("tank", StringComparison.OrdinalIgnoreCase))
                {
                    AirframeDb.CurrentVehicle = "";   // 空串 = 不是飞机，不参与飞行阈值
                }
                else
                {
                    // 机型数据库：换机即按当前遥测能力刷新（游戏更新后会自动跟上）
                    var created = AirframeDb.Refresh(
                        s.VehicleType,
                        s.RadioAltM.HasValue,
                        s.Caps.HasNativeAttitude,
                        s.VehicleType.StartsWith("a_10", StringComparison.OrdinalIgnoreCase) ? "key" : "telemetry",
                        s.AoaDeg);
                    Console.WriteLine($"airframe: {s.VehicleType}  RA={(created.RadarAltitude ? "yes" : "no")}  " +
                                      $"ATT={created.Attitude}  AB={created.Airbrake}");
                }
                }
            if (s.Valid && !prevValid)
            {
                viewState = 0;
                Console.WriteLine("view state reset: match entered");
                if (!string.IsNullOrEmpty(s.VehicleType))
                {
                    var p2 = AirframeDb.Refresh(
                        s.VehicleType,
                        s.RadioAltM.HasValue,
                        s.Caps.HasNativeAttitude,
                        s.VehicleType.StartsWith("a_10", StringComparison.OrdinalIgnoreCase) ? "key" : "telemetry");
                    Console.WriteLine($"airframe(match): {s.VehicleType} ATT={p2.Attitude} " +
                                      $"RA={(p2.RadarAltitude ? "yes" : "no")} AB={p2.Airbrake}");
                }
            }
            prevValid = s.Valid;

            latest = s;
            // Pitch sign: manual config first, then the sign learned for this vehicle, then the
            // automatic vote; the result is written back per vehicle so it never flips again.
            fusion.ForcedPitchSign = pendingPitchSign;
            lastAttitude = fusion.Update(s);
            detectedPitchSign = fusion.DetectedPitchSign;
            pitchSignDecided = fusion.SignDecided;
            pitchSignVehicle = s.VehicleType;
        };
        telemetry.Start();
        using var mapData = new WtHud.Telemetry.MapData();
        mapData.Start();
        hudMsgWatcher.OwnKill += t => splashQueue.Enqueue(t);

        var opt = new HudOptions();
        var cfg = HudConfig.Load();
        WtHud.UI.L10n.LoadFromConfig(cfg.Language);
        var envPanel = Environment.GetEnvironmentVariable("WT_MFD_PANEL");
        if (!string.IsNullOrEmpty(envPanel)) WtHud.UI.L10n.Panel = envPanel == "zh" ? WtHud.UI.Lang.Zh : WtHud.UI.Lang.En;
        var envScreen = Environment.GetEnvironmentVariable("WT_MFD_SCREEN");
        if (!string.IsNullOrEmpty(envScreen)) WtHud.UI.L10n.Screen = envScreen == "zh" ? WtHud.UI.Lang.Zh : WtHud.UI.Lang.En;
        // 坡度耦合：auto = 座舱视角开、第三人称关；也可强制开/关
        var coupling = cfg.RollCouplingMode == "auto"
            ? 1.0   // 座舱为主的拟真玩法：自动即耦合（普通模式可用“关”）
            : cfg.RollCouplingMode == "on" ? 1.0
            : cfg.RollCouplingMode == "off" ? 0.0 : cfg.RollCoupling;
        var cal = new HudCalibration { VerticalFovDeg = cfg.VerticalFovDeg, LayoutOffsetYDeg = cfg.LayoutOffsetYDeg, RollCoupling = coupling };
        void ApplyWindowGeometry()
        {
            if (GameWindowTracker.TryGetClientRect(out var gx0, out var gy0, out var gw0, out var gh0, out _))
            {
                cal.CenterOffsetX = gx0 + gw0 / 2.0 - (scrX + scrW / 2.0);
                cal.CenterOffsetY = gy0 + gh0 / 2.0 - (scrY + scrH / 2.0);
                cal.PxReferenceHeight = gh0;         // 角度→像素用游戏窗口高度
                overlayRef?.SetCursorHoleCenter((int)Math.Round(cal.CenterOffsetX), (int)Math.Round(cal.CenterOffsetY));
                Console.WriteLine($"geometry -> game {gx0},{gy0} {gw0}x{gh0} offset {cal.CenterOffsetX:F0},{cal.CenterOffsetY:F0} pxRef={cal.PxReferenceHeight:F0}");
            }
            else
            {
                cal.CenterOffsetX = 0; cal.CenterOffsetY = 0; cal.PxReferenceHeight = 0;
                Console.WriteLine("geometry -> fallback (no game window)");
            }
        }
        ApplyWindowGeometry();
        var geo = new HudGeometry(opt, cal);
        var empty = new List<HudPrim>();

        bool show = true;
        using var overlay = new OverlayWindow(scrW, scrH, scrX, scrY);

        // The overlay is a console app; its console window steals the foreground and makes
        // Windows paint the arrow cursor over the game. Redirect output to a log file and
        // hide the console, then give focus back to the game.
        var logPath = Path.Combine(AppContext.BaseDirectory, "wthud-overlay.log");
        try
        {
            Console.SetOut(TextWriter.Synchronized(new StreamWriter(logPath, false, new System.Text.UTF8Encoding(false)) { AutoFlush = true }));
        }
        catch
        {
            // 另一个实例还占着日志（双开，或自动提权重启时旧进程没退干净）。
            // 以前这里静默吞掉异常 → 这个实例的日志直接丢失，排查时会被"日志里只有一次
            // overlay start"误导。改成落到带 pid 的文件，两份日志都留得下。
            try
            {
                var alt = Path.Combine(AppContext.BaseDirectory, $"wthud-overlay-{Environment.ProcessId}.log");
                Console.SetOut(TextWriter.Synchronized(new StreamWriter(alt, false, new System.Text.UTF8Encoding(false)) { AutoFlush = true }));
                Console.WriteLine($"(main log busy -> writing to {Path.GetFileName(alt)})");
            }
            catch { }
        }
        Console.WriteLine($"overlay start {DateTime.Now:HH:mm:ss} {scrW}x{scrH} dpiScale={overlay.DpiScale:F3} " +
                          $"pid={Environment.ProcessId} ver={typeof(Program).Assembly.GetName().Version} " +
                          $"elevated={IsProcessElevated(Environment.ProcessId)}");
        // 排查"点了没反应 / 整块屏点不动"必须知道用户的这几个设置，以前日志里没有，
        // 只能反复问人要 hud-config.json。
        Console.WriteLine($"cfg: cursorHole={cfg.CursorHoleHalfPx} " +
                          $"show={cfg.ShowKey} settings={cfg.SettingsKey} reset={cfg.ResetKey} view={cfg.ViewKey} " +
                          $"pitchZero={cfg.PitchZeroKey} bankFlip={cfg.BankFlipKey} bodyFixed={cfg.BodyFixedKey} " +
                          $"exit={cfg.ExitKey} save={cfg.SaveKey} " +
                          $"guidance(kp={cfg.FormationKp:F3}/{cfg.FormationKpNear:F2} blend={cfg.FormationBlendM:F0}m " +
                          $"ki={cfg.FormationKi:F4} kv={cfg.FormationKv:F2}) " +
                          $"playerName={(string.IsNullOrWhiteSpace(cfg.PlayerName) ? "(empty)" : cfg.PlayerName)}");
        overlayRef = overlay;
        int hotkeyFails = overlay.ApplyHotkeys(cfg);
        if (hotkeyFails > 0)
        {
            // 自动提权重启时，旧进程可能还占着热键几毫秒：稍后重试注册。
            hotkeyRetryAtTicks = Environment.TickCount64 + 1500;
            hotkeyRetryCount = 0;
        }
        overlay.SetCursorHole(cfg.CursorHoleHalfPx);
        overlay.SetCursorHoleCenter(cfg.CursorHoleOffsetX, cfg.CursorHoleOffsetY);
        overlay.NudgeForegroundCursor();
        var console = NativeMethods.GetConsoleWindow();
        if (console != IntPtr.Zero) NativeMethods.ShowWindow(console, NativeMethods.SW_HIDE);
        var game = System.Diagnostics.Process.GetProcessesByName("aces").FirstOrDefault();
        int gamePid = game?.Id ?? 0;
        // 游戏是管理员权限、而本程序不是：Windows UIPI 会让全局热键在游戏前台时收不到。
        // 直接把 WtHud 以管理员身份重启（只弹一次 UAC），否则游戏里 F1/F10/F9 全部无效。
        elevationCheckedForPid = gamePid;
        EnsureElevationMatchesGame(gamePid);
        if (game?.MainWindowHandle is IntPtr gameHwnd && gameHwnd != IntPtr.Zero) NativeMethods.SetForegroundWindow(gameHwnd);

        overlay.HotkeyPressed += id =>
        {
            if (id == 1) { show = !show; Console.WriteLine($"F10 -> HUD {(show ? "on" : "off")}"); }
            else if (id == 2) { opt.BodyFixedOnly = !opt.BodyFixedOnly; Console.WriteLine($"F9 -> body-fixed mode {(opt.BodyFixedOnly ? "on" : "off")}"); }
            else if (id == 3)
            {
                // Double-press to quit: one F11 is far too close to the F10 toggle.
                long nowTick = Environment.TickCount64;
                if (nowTick - lastExitPress < 700)
                {
                    Console.WriteLine("F11 x2 -> exit");
                    overlay.RequestClose();
                }
                else
                {
                    lastExitPress = nowTick;
                    Console.WriteLine("F11 pressed once (press again within 0.7 s to exit)");
                }
            }
            else if (id == 4) { cal.LayoutOffsetYDeg += 0.5; cfg.LayoutOffsetYDeg = cal.LayoutOffsetYDeg; Console.WriteLine($"Ctrl+Alt+Up   -> LayoutOffsetYDeg = {cal.LayoutOffsetYDeg:F1}"); }
            else if (id == 5) { cal.LayoutOffsetYDeg -= 0.5; cfg.LayoutOffsetYDeg = cal.LayoutOffsetYDeg; Console.WriteLine($"Ctrl+Alt+Down -> LayoutOffsetYDeg = {cal.LayoutOffsetYDeg:F1}"); }
            else if (id == 6)
            {
                // Fly level, press F7: the ladder then reads 0 against the real horizon.
                cfg.PitchZeroDeg = -lastAttitude.PitchDeg;
                Console.WriteLine($"F7  -> pitch zero set to {cfg.PitchZeroDeg:F1} deg (raw was {lastAttitude.PitchDeg:F1})");
            }
            else if (id == 7)
            {
                cfg.BankSign = -cfg.BankSign;
                Console.WriteLine($"F8  -> bank sign = {cfg.BankSign}");
            }
            else if (id == 8)
            {
                cfg.VerticalFovDeg = Math.Max(15, cfg.VerticalFovDeg - 1);
                cal.VerticalFovDeg = cfg.VerticalFovDeg;
                Console.WriteLine($"Ctrl+Alt+PgUp   -> FOV = {cfg.VerticalFovDeg:F0} deg (symbols bigger)");
            }
            else if (id == 9)
            {
                cfg.VerticalFovDeg = Math.Min(120, cfg.VerticalFovDeg + 1);
                cal.VerticalFovDeg = cfg.VerticalFovDeg;
                Console.WriteLine($"Ctrl+Alt+PgDn   -> FOV = {cfg.VerticalFovDeg:F0} deg (symbols smaller)");
            }
            else if (id == 14)
            {
                // Ctrl+Alt+H cycles the cursor-hole size so the minimum acceptable value can be
                // found in game (smaller hole = less HUD occlusion near the boresight).
                int[] sizes = { 0, 16, 24, 32, 40, 56, 70 };
                int cur = Array.IndexOf(sizes, overlay.CursorHoleHalfPx);
                int next = sizes[(cur + 1) % sizes.Length];
                overlay.SetCursorHole(next);
                cfg.CursorHoleHalfPx = next;
                Console.WriteLine($"cursor hole -> {next * 2}x{next * 2} px  (Ctrl+Alt+H cycles, F12 saves)");
                Console.WriteLine($"  cursor now : {NativeMethods.DescribeCursor()}");
                Thread.Sleep(220);
                Console.WriteLine($"  cursor +220: {NativeMethods.DescribeCursor()}");
            }
            else if (id == 13)
            {
                viewState = 0;
                mapHeld = false;
                Console.WriteLine("F5 -> view state + map state reset");
            }
            else if (id == 12)
            {
                OpenMfdSettings(cfg, cal, v => show = v);
            }
            else if (id == 11)
            {
                cfg.RollCoupling = Math.Abs(cfg.RollCoupling) < 0.5 ? 1.0 : 0.0;
                cal.RollCoupling = cfg.RollCoupling;
                Console.WriteLine($"Ctrl+Alt+R -> camera roll coupling = {cfg.RollCoupling:F1} (0=third person, 1=cockpit 1:1)");
            }
            else if (id == 10)
            {
                cfg.Save();
                Console.WriteLine($"F12 -> saved hud-config.json (fov={cfg.VerticalFovDeg:F0} yoff={cfg.LayoutOffsetYDeg:F1} pzero={cfg.PitchZeroDeg:F1} bankSign={cfg.BankSign})");
            }
        };

        // 游戏以无边框窗口跑、用户切到另一块屏之后，点游戏画面回不来（只能 Alt+Tab 或点任务栏
        // 图标）。那种合成状态下顶层覆盖层可能把这一下点击吃掉。正常情况覆盖层是穿透的、根本
        // 收不到 WM_LBUTTONDOWN，所以这个回调不会触发；一旦触发，就说明点击被吃了，把它转成
        // 「激活游戏」，用户就有"点一下就回来"的效果。
        overlay.LeftClickWhileInactive = () =>
        {
            if (GameWindowTracker.IsActive) return;          // 已经在前台，不需要
            if (GameWindowTracker.GameHwnd == IntPtr.Zero)
                GameWindowTracker.TryGetClientRect(out _, out _, out _, out _, out _);   // 游戏后启动时补找一次
            var gh = GameWindowTracker.GameHwnd;
            if (gh == IntPtr.Zero) return;
            Console.WriteLine($"overlay absorbed a click while the game was inactive -> activating game {gh}");
            if (NativeMethods.IsIconic(gh)) NativeMethods.ShowWindow(gh, NativeMethods.SW_RESTORE);
            ForceForegroundWindow(gh);
        };

        // Dedicated input/cursor watcher.
        //   * cursor: the render loop only runs ~50 fps, which left the arrow flickering back
        //   * Z zoom key: polling once per rendered frame missed short presses when the frame
        //     rate dropped, so it is sampled here at 5 ms together with the "pressed since the
        //     last poll" flag, which cannot miss a key event.
        bool cursorStop = false;
        var inputThread = new Thread(() =>
        {
            bool prevZoom = false;
            bool prevView = false;
            RebindeKeys(cfg);   // parse from config at startup (and again after F1 saves)
            long lastViewToggleTicks = 0;
            long lastToggleTicks = 0;
            bool wasForeground = false;
            IntPtr arrow = NativeMethods.LoadCursor(IntPtr.Zero, (IntPtr)32512);
            while (!cursorStop)
            {
                // 游戏可能在 WtHud 启动之后才起来；稍后重新找一次，并补做权限检查。
                long searchNow = Environment.TickCount64;
                if (gamePid == 0 && searchNow - lastGameSearchTicks > 5000)
                {
                    lastGameSearchTicks = searchNow;
                    var late = System.Diagnostics.Process.GetProcessesByName("aces").FirstOrDefault();
                    if (late != null) gamePid = late.Id;
                }
                if (gamePid != 0 && elevationCheckedForPid != gamePid)
                {
                    elevationCheckedForPid = gamePid;
                    EnsureElevationMatchesGame(gamePid);
                }

                bool foreground = gamePid != 0 && IsForegroundProcess(gamePid);

                // While another application owns the foreground, the overlay window is hidden
                // entirely: a topmost window can still swallow clicks over its whole area even
                // when it reports HTTRANSPARENT, which made the desktop unusable.
                // 例外：F1 面板打开时（面板是模态窗口、会抢前台）必须继续显示 HUD，
                // 否则一开面板 HUD 就消失、改设置看不到效果。
                //
                // ★ 2026-11-10：以前这里还有 `|| !cfg.HideWhenInactive`，让"后台不隐藏"的用户保留
                // HUD。但那个分支正好造出注释里说的不可用状态：反馈者实测——切到另一块屏之后，
                // 游戏那块屏（主屏）"什么都点不了，像被蒙住了"，单实例也一样。现在隐藏是无条件的，
                // 那个开关连同字段已经删除（见 HudConfig 注释），别再给它加回来的口子。
                bool wantVisible = foreground || settingsOpen;
                if (overlay.IsVisible != wantVisible) overlay.SetVisible(wantVisible);

                if (foreground != wasForeground)
                {
                    Console.WriteLine($"foreground -> {(foreground ? "game (overlay shown)" : "other app (overlay hidden)")}");
                    wasForeground = foreground;

                    // Coming back from another app the OS may still be drawing that app's arrow
                    // (War Thunder pins the pointer to the screen centre, so no new WM_SETCURSOR
                    // ever arrives and the arrow stays). Post one so the game hides it again.
                    if (foreground)
                    {
                        overlay.ForceInvisibleCursor();
                        overlay.NudgeForegroundCursor();
                        Thread.Sleep(30);
                        overlay.NudgeForegroundCursor();
                    }
                }

                // Keep the drawn cursor null while the game is in front. ShowCursor must NOT be
                // used here: its counter balancing is what made the pointer reappear.
                if (foreground && !settingsOpen) NativeMethods.SetCursor(IntPtr.Zero);

                long now = Environment.TickCount64;

                // Watchdog: the game pins the pointer and only re-hides the cursor inside its
                // WM_SETCURSOR handler. If the OS still reports a visible cursor while the game
                // owns the foreground, keep poking it until it hides.
                if (foreground && !settingsOpen && now - lastCursorNudge > 150)
                {
                    lastCursorNudge = now;
                    string curState = NativeMethods.DescribeCursor();
                    if (curState.Contains("showing=True"))
                    {
                        cursorNudges++;
                        if (cursorNudges <= 8 || cursorNudges % 40 == 0)
                            Console.WriteLine($"cursor watchdog #{cursorNudges}: {curState}");
                        overlay.ForceInvisibleCursor();
                        overlay.NudgeForegroundCursor();
                    }
                    else
                    {
                        cursorNudges = 0;
                    }
                }
                bool armed = foreground && !settingsOpen;

                // Aiming layout: Z/key toggles it, holding the right mouse button forces it on
                // for as long as it is held (momentary), which matches how most players look
                // through the sight.
                bool zoomNow = _zoomKeyBinding.IsDown();
                bool zoomEdge = (zoomNow && !prevZoom) || _zoomKeyBinding.PressedSinceLastPoll();
                if (zoomEdge && armed && now - lastToggleTicks > 180)
                {
                    lastToggleTicks = now;
                    zoomLatched = !zoomLatched;
                    Console.WriteLine($"aim key -> layout {(zoomLatched ? "on" : "off")}");
                }
                prevZoom = zoomNow;

                bool rightHeld = cfg.AimOnRightMouse && WtHud.Input.KeyBinding.IsVirtualKeyDown(0x02);
                opt.ZoomLayout = zoomLatched || (armed && rightHeld);

                bool viewNow = _viewKeyBinding.IsDown();
                bool viewEdge = (viewNow && !prevView) || _viewKeyBinding.PressedSinceLastPoll();
                if (viewEdge && armed && now - lastViewToggleTicks > 180)
                {
                    lastViewToggleTicks = now;
                    viewState = (viewState + 1) % 3;   // 0 third person, 1 cockpit, 2 nose
                    if (zoomLatched)
                    {
                        zoomLatched = false;
                        Console.WriteLine("view key -> aiming layout reset");
                    }
                    Console.WriteLine($"view key -> view state {viewState} ({ViewName(viewState)})");
                }
                prevView = viewNow;

                // Both the map and the scoreboard are hold-to-hide in this setup. The map key
                // ignores modifier state: pressing M together with another key used to fall
                // through and let the HUD pop back up.
                // Single-key map binding ignores modifier state (pressing M with Shift held used
                // to fall through and pop the HUD back up); a real combo binding stays strict.
                bool mapHasMods = _mapKeyBinding.Ctrl || _mapKeyBinding.Shift || _mapKeyBinding.Alt;
                mapHeld = armed && (mapHasMods ? _mapKeyBinding.IsDown() : _mapKeyBinding.IsKeyDownIgnoringModifiers());

                // Scoreboard is a toggle rather than hold-to-show: press once to hide the HUD,
                // press again to bring it back.
                bool scoreNow = _scoreKeyBinding.IsDown();
                bool scoreEdge = (scoreNow && !prevScore) || _scoreKeyBinding.PressedSinceLastPoll();
                if (scoreEdge && armed && now - lastScoreToggle > 200)
                {
                    lastScoreToggle = now;
                    scoreboardLatched = !scoreboardLatched;
                    Console.WriteLine($"scoreboard key -> hidden {(scoreboardLatched ? "on" : "off")}");
                }
                prevScore = scoreNow;
                scoreboardHeld = armed && scoreboardLatched;

                // A-10 family: airbrake box follows the key (deceleron telemetry is unusable).
                if (armed && latest is { } abSnap
                    && abSnap.VehicleType.StartsWith("a_10", StringComparison.OrdinalIgnoreCase)
                    && _airbrakeKeyBinding.PressedSinceLastPoll())
                {
                    airbrakeKeyOn = !airbrakeKeyOn;
                    Console.WriteLine("airbrake key (" + _airbrakeKeyBinding.Text + ") -> " + (airbrakeKeyOn ? "ON" : "off"));
                }

                // Stall calibration: press while the aircraft is at the buffet to remember this
                // vehicle's stall AOA (per-vehicle table in hud-config.json).
                if (armed && _stallKeyBinding.PressedSinceLastPoll() && latest is { } cur && cur.AoaDeg > 1)
                {
                    cfg.StallAoaByVehicle[cur.VehicleType] = Math.Round(cur.AoaDeg, 1);
                    cfg.Save();
                    Console.WriteLine($"stall key -> {cur.VehicleType} stall AOA = {cur.AoaDeg:F1} deg (saved)");
                }

                // Reset key (default F5, configurable): clear view counter / map / scoreboard state.
                if (armed && _resetKeyBinding.PressedSinceLastPoll())
                {
                    viewState = 0;
                    mapHeld = false;
                    scoreboardLatched = false;
                    Console.WriteLine($"reset key ({_resetKeyBinding.Text}) -> view/map/scoreboard state cleared");
                }

                // Long-press hide key (default U): hold it to blank the HUD.
                bool holdKeyDown = armed && _holdHideKeyBinding.IsKeyDownIgnoringModifiers();
                if (holdKeyDown)
                {
                    if (holdKeyStart == 0) holdKeyStart = now;
                }
                else
                {
                    holdKeyStart = 0;
                }
                holdKeyHidden = holdKeyStart != 0 && now - holdKeyStart > HoldHideDelayMs;

                Thread.Sleep(5);
            }
        }) { IsBackground = true, Name = "input-watcher" };
        inputThread.Start();

        var clock = System.Diagnostics.Stopwatch.StartNew();
        double lastLog = 0;
        int frames = 0;
        double frameBudget = 1.0 / 30.0;   // idle loop rate; raised while an animation runs
        double nextFrame = 0;
        long lastFrameKey = long.MinValue;
        int presents = 0;
        double zoomBlend = 0;
        double lastFrameTime = 0;

        while (overlay.Running && (seconds <= 0 || clock.Elapsed.TotalSeconds < seconds))
        {
            if (!overlay.PumpMessages()) break;
        // 面板 SAVE 后要重建热键：必须回到创建窗口的线程（overlay 线程）里做，
        // 否则 UnregisterHotKey 注销不到原注册、RegisterHotKey 会全部报 already in use。
        if (pendingHotkeyRefresh)
        {
            pendingHotkeyRefresh = false;
            int fails = overlay.ApplyHotkeys(cfg);
            if (fails > 0) { hotkeyRetryAtTicks = Environment.TickCount64 + 1500; hotkeyRetryCount = 0; }
        }
        if (hotkeyRetryAtTicks != 0 && Environment.TickCount64 >= hotkeyRetryAtTicks)
        {
            hotkeyRetryAtTicks = 0;
            if (hotkeyRetryCount < 3)
            {
                hotkeyRetryCount++;
                if (overlay.ApplyHotkeys(cfg) > 0)
                    hotkeyRetryAtTicks = Environment.TickCount64 + 1500;
            }
        }

            var rawSnap = latest ?? (demo ? DemoSnapshot() : new TelemetrySnapshot { Valid = false });
            var rawAtt = latest == null && demo
                ? new AttitudeSolution { PitchDeg = 6, BankDeg = -14, Source = AttitudeSource.Estimated, Confidence = 1 }
                : lastAttitude;

            // The API only updates at ~25 Hz while we render at 100+; extrapolate up to one
            // sample ahead so the symbology does not visibly step and lag behind the world.
            // Attitude always uses the raw sample: predicting the ladder overshot noticeably.
            var att = rawAtt;
            double frac = cfg.Extrapolate ? ExtrapolationFraction(prevSnapshot, latest, DateTime.UtcNow) : 0;
            var snap = frac > 0.02 ? Extrapolate(prevSnapshot, rawSnap, frac) : rawSnap;


            // Animate the aiming-layout transition (~0.28 s) instead of snapping.
            double nowSec = clock.Elapsed.TotalSeconds;
            double dt = Math.Clamp(nowSec - lastFrameTime, 0, 0.1);
            lastFrameTime = nowSec;
            double zoomTarget = opt.ZoomLayout ? 1.0 : 0.0;
            double zoomStep = dt / 0.28;
            zoomBlend += Math.Clamp(zoomTarget - zoomBlend, -zoomStep, zoomStep);
            opt.ZoomBlend = zoomBlend;

            bool flying = snap.Valid;   // ground counts too: tapes/gear/flaps are useful while taxiing
            bool onGround = snap.Valid && !snap.InFlight;
            bool hiddenByView = (viewState == 1 && cfg.HideInCockpit) || (viewState == 2 && cfg.HideInNose);
            bool hiddenByPanels = mapHeld || scoreboardHeld || holdKeyHidden;
            // Only hide once the aircraft has actually stopped: taxiing still shows the HUD.
            bool stationary = snap.IasKmh < cfg.GroundHideSpeedKmh;
            bool hiddenOnGround = cfg.HideOnGround && onGround && stationary && !demo;
            bool draw = show && !hiddenByView && !hiddenByPanels && !hiddenOnGround && (flying || demo);

            // Pitch sign for the next update: manual config wins, then the sign learned for this
            // vehicle; whatever the detector decides is stored per vehicle so it never flips again.
            pendingPitchSign = cfg.PitchSign;
            if (pendingPitchSign == 0 && !string.IsNullOrEmpty(snap.VehicleType)
                && WtHud.Hud.SettingsRouter.GetFor(cfg, snap.VehicleType, "row.pitchSign", out var storedSign))
            {
                pendingPitchSign = storedSign;
            }
            if (cfg.PitchSign == 0 && pitchSignDecided && !string.IsNullOrEmpty(pitchSignVehicle)
                && !WtHud.Hud.SettingsRouter.HasFor(cfg, pitchSignVehicle, "row.pitchSign"))
            {
                WtHud.Hud.SettingsRouter.SetFor(cfg, pitchSignVehicle, "row.pitchSign", detectedPitchSign);
                cfg.Save();
                Console.WriteLine($"pitch sign for {pitchSignVehicle} = {detectedPitchSign} (saved to config)");
            }

            // Apply the live calibration on top of the raw solution. 校准值可以是机型覆盖
            // （EffectiveSettings：机型 > 档案 > 全局），不是只有全局配置。
            var liveVehicle = snap.VehicleType;
            var shown = new AttitudeSolution
            {
                PitchDeg = att.PitchDeg + WtHud.Hud.EffectiveSettings.PitchZeroDeg(cfg, liveVehicle),
                BankDeg = att.BankDeg * cfg.BankSign + WtHud.Hud.EffectiveSettings.BankTrimDeg(cfg, liveVehicle),
                Source = att.Source,
                Confidence = att.Confidence,
            };
            opt.FpmTrimDeg = WtHud.Hud.EffectiveSettings.FpmTrimDeg(cfg, liveVehicle);

            // ---- stall warning ----
            //   calibrated per vehicle (B) > cockpit AOA indexer (C) > generic rule (A)
            // High-speed pulls reach a high AOA without stalling, so the AOA test alone is not
            // enough: require that the wing is also unloaded OR the aircraft is decelerating,
            // i.e. the classic approach to a stall rather than a hard turn.
            opt.StallWarnLevel = 0;
            if (snap.InFlight && !string.IsNullOrEmpty(snap.VehicleType))
            {
                // Calibrations below 12 deg are meaningless for a wing stall (they come from pressing the
// key while cruising); ignore them instead of spamming false warnings.
                bool hasCal = cfg.StallAoaByVehicle.TryGetValue(snap.VehicleType, out double calibratedAoa)
                              && calibratedAoa >= 12;
                if (cfg.StallAoaByVehicle.TryGetValue(snap.VehicleType, out double storedAoa) && storedAoa < 12 && stallCalWarned != snap.VehicleType)
                {
                    stallCalWarned = snap.VehicleType;
                    Console.WriteLine($"stall calibration for {snap.VehicleType} ignored ({storedAoa:F1} deg < 12); recalibrate in a buffet");
                }
                double threshold = hasCal
                    ? calibratedAoa
                    : WtHud.Hud.EffectiveSettings.StallAoaDeg(cfg, snap.VehicleType, AirframeDb.Get(snap.VehicleType));
                double aoa = snap.AoaDeg;
                bool decel = accelMs2 < -0.6;                 // losing speed
                // A hard pull at high speed reaches a big AOA while still loaded (Ny well above 1).
                // A real stall approach is *unloaded*: the wing cannot give more than ~2.5 G at that
                // AOA any more. Requiring that removes the high-speed-pull false alarms.
                bool unloaded = snap.NyG < 2.5;
                bool sinking = snap.VyMs < -1.0;
                bool cond = aoa >= threshold && unloaded && (decel || sinking || aoa >= threshold + 3);
                if (cond)
                {
                    if (stallSince == 0) stallSince = Environment.TickCount64;
                }
                else
                {
                    stallSince = 0;
                }

                if (stallSince != 0 && Environment.TickCount64 - stallSince > 350)
                {
                    bool hardStall = aoa >= threshold + 4.0 || snap.NyG < 1.2;
                    opt.StallWarnLevel = hardStall ? 2 : 1;
                }

                if (snap.AoaIndexer is double idx)
                {
                    // 座舱 AOA 指示器：不同机型语义不同，而且拉杆瞬间会抖。
                    // 之前是「单帧 >0.9 立刻点亮」→ F/A-18A 这种指示器在 0.9 附近抖的机型会一拉就闪。
                    // 现在要求持续 200 ms 才点亮，回落 0.85 以下才复位。
                    if (idx > 0.9)
                    {
                        if (aoaIdxSince == 0) aoaIdxSince = Environment.TickCount64;
                        if (opt.StallWarnLevel == 0 && Environment.TickCount64 - aoaIdxSince > 200)
                            opt.StallWarnLevel = idx > 0.97 ? 2 : 1;
                    }
                    else if (idx < 0.85)
                    {
                        aoaIdxSince = 0;
                    }
                }
                else
                {
                    aoaIdxSince = 0;
                }

                if (opt.StallWarnLevel != stallLogged)
                {
                    stallLogged = opt.StallWarnLevel;
                    Console.WriteLine($"stall level {opt.StallWarnLevel}: AoA={aoa:F1} thr={threshold:F1}" +
                                      $" cal={(hasCal ? "yes" : "no")} Ny={snap.NyG:F1} accel={accelMs2:F1} vy={snap.VyMs:F0}" +
                                      $" idx={(snap.AoaIndexer?.ToString("F2") ?? "-")}");
                }
            }
            else
            {
                stallSince = 0;
                aoaIdxSince = 0;
            }

            var overIas = opt.OverspeedKmh > 0 && snap.IasKmh > opt.OverspeedKmh;
            var overMach = opt.OverspeedMach > 0.05 && snap.Mach > opt.OverspeedMach;
            opt.OverspeedAlert = overIas || overMach;   // 双路：IAS 或 Mach 任一超限

            if (opt.OverspeedAlert != overspeedLogged)
            {
                overspeedLogged = opt.OverspeedAlert;
                Console.WriteLine(opt.OverspeedAlert
                    ? $"overspeed ON: M={snap.Mach:F2} IAS={snap.IasKmh:F0} km/h (thr={opt.OverspeedMach:F2}M/{opt.OverspeedKmh:F0}kmh)"
                    : "overspeed off");
            }

            // Longitudinal acceleration: least-squares slope of TAS over a 3 s window. The raw
            // per-sample derivative of an integer km/h counter is mostly quantisation noise,
            // which made the trend bar jump around.
            double tasMsNow = snap.TasKmh / 3.6;
            double tNow = clock.Elapsed.TotalSeconds;
            if (tasMsNow > 1)
            {
                speedWindow.Enqueue((tNow, tasMsNow));
                while (speedWindow.Count > 2 && tNow - speedWindow.Peek().T > 3.0) speedWindow.Dequeue();
                while (speedWindow.Count > 200) speedWindow.Dequeue();

                if (speedWindow.Count >= 6)
                {
                    double sx = 0, sy = 0, sxy = 0, sxx = 0;
                    int cnt = 0;
                    foreach (var (tt, vv) in speedWindow)
                    {
                        sx += tt; sy += vv; sxy += tt * vv; sxx += tt * tt; cnt++;
                    }
                    double denom = cnt * sxx - sx * sx;
                    if (Math.Abs(denom) > 1e-6)
                    {
                        double rawAccel = (cnt * sxy - sx * sy) / denom;
                        if (Math.Abs(rawAccel) < 60) accelMs2 = accelMs2 * 0.7 + rawAccel * 0.3;
                    }
                }
            }
            accelLastTas = tasMsNow;
            accelLastTime = tNow;
            opt.AccelMs2 = accelMs2;
            // Overspeed threshold: 机型显式覆盖 > 座舱 Vne > 机型档案 > 全局。
            // 用户按机型改过阈值时必须以用户为准，否则面板里的值永远不会生效。
            var alertProfile = AirframeDb.Get(snap.VehicleType);
            bool iasOverride = WtHud.Hud.SettingsRouter.TryGetForVehicle(cfg, snap.VehicleType, "row.overspeedIas", out double iasOverrideKmh);
            bool machOverride = WtHud.Hud.SettingsRouter.TryGetForVehicle(cfg, snap.VehicleType, "row.overspeedMach", out double machOverrideValue);
            if (!iasOverride && !machOverride && snap.VneMs is double vneMs && vneMs > 10)
            {
                opt.OverspeedKmh = vneMs * 3.6;
                if (vneLogged != snap.VehicleType)
                {
                    vneLogged = snap.VehicleType;
                    Console.WriteLine($"Vne from cockpit on {snap.VehicleType}: {vneMs:F1} m/s = {vneMs * 3.6:F0} km/h");
                }
            }
            else
            {
                // 双路同时判断（IAS 或 Mach 任一超限即告警），不再有「极速依据」开关
                opt.OverspeedKmh = iasOverride
                    ? iasOverrideKmh
                    : WtHud.Hud.EffectiveSettings.OverspeedKmh(cfg, snap.VehicleType, alertProfile);
                opt.OverspeedMach = machOverride
                    ? machOverrideValue
                    : WtHud.Hud.EffectiveSettings.OverspeedMach(cfg, snap.VehicleType, alertProfile);
            }

            opt.FpmAosSign = cfg.FpmAosSign != 0 ? Math.Sign(cfg.FpmAosSign) : -1;
            var aoaWarn = WtHud.Hud.EffectiveSettings.AoaWarnDeg(cfg, snap.VehicleType, alertProfile);
            opt.AoaWarnAlert = snap.InFlight && snap.AoaDeg > aoaWarn;
            opt.BingoFuelAlert = opt.EnduranceSeconds >= 0 && opt.EnduranceSeconds < cfg.BingoFuelSeconds;
            // Radar altitude only when the aircraft actually reports one: the API exposes no
            // terrain data, so an estimate was hopeless over hilly maps. Everything else shows
            // barometric altitude, exactly like before.
            bool hasRadioAlt = snap.RadioAltM.HasValue;
            if (hasRadioAlt && !radioAltLogged)
            {
                radioAltLogged = true;
                Console.WriteLine($"radio altimeter available on {snap.VehicleType}: {snap.RadioAltM:F0} m AGL (8111 feet -> m)");
            }
            bool showRadarAlt = cfg.TapeRadarAltitude && hasRadioAlt;
            opt.AltTapeIsRadar = showRadarAlt;
            opt.AltTapeM = showRadarAlt ? snap.RadioAltM!.Value : snap.AltM;
            UpdateNavigation(opt, mapData, snap, cfg);

            // ---- SPLASH 击落播报：只认「自己击落了别人」，被打下/击毁 AI 都不播 ----
            if (snap.Valid != splashInMatch)
            {
                splashInMatch = snap.Valid;
                if (!splashInMatch)
                {
                    splashQueue.Clear();
                    splashTarget = "";
                    hudMsgWatcher.Reset();
                }
            }
            if (!cfg.SplashEnabled)
            {
                splashQueue.Clear();
                splashTarget = "";
            }
            hudMsgWatcher.Poll(cfg.PlayerName, snap.Valid && cfg.SplashEnabled);
            UpdateSplash(opt);

            // Terrain warnings.
            //   precise: only when the aircraft reports a radio altimeter
            //   estimate: sustained high sink rate below 500 m (altitude above sea level)
            long alertTicks = Environment.TickCount64;

            // PULL UP: only with a real radio altimeter, and the trigger altitude grows with the
            // sink rate - what matters is the time to impact, so a fast dive warns higher up.
            double ralt = snap.RadioAltM ?? double.NaN;
            bool hasRalt = !double.IsNaN(ralt);
            double tti = hasRalt && snap.VyMs < -5 ? ralt / -snap.VyMs : double.MaxValue;
            double pullUpTti = WtHud.Hud.EffectiveSettings.PullUpTtiS(cfg, snap.VehicleType, alertProfile);
            bool pullUpCond = snap.InFlight && hasRalt && ralt < 1500 && snap.VyMs < -10 && tti < pullUpTti;

            // SINK RATE: estimate-based fallback for every aircraft (sea-level altitude, no terrain).
            // 阈值：机型覆盖 > 机型档案 > 全局（0 = 用内置默认 40 m/s）
            var sinkThr = WtHud.Hud.EffectiveSettings.SinkRateMs(
                cfg, snap.VehicleType, AirframeDb.Get(snap.VehicleType));
            if (sinkThr <= 0) sinkThr = 40;
            bool sinkCond = snap.InFlight && snap.AltM < 500 && snap.VyMs < -sinkThr;

            if (pullUpCond) { if (pullUpSince == 0) pullUpSince = alertTicks; } else pullUpSince = 0;
            if (sinkCond) { if (sinkRateSince == 0) sinkRateSince = alertTicks; } else sinkRateSince = 0;

            bool pullUpNow = pullUpSince != 0 && alertTicks - pullUpSince > 250;
            bool sinkNow = sinkRateSince != 0 && alertTicks - sinkRateSince > 1000;

            if (pullUpNow != opt.PullUpAlert)
                Console.WriteLine($"PULL UP {(pullUpNow ? "ON" : "off")}: ralt={snap.RadioAltM:F0} m vy={snap.VyMs:F0} m/s tti={tti:F1} s");
            if (sinkNow != opt.SinkRateAlert)
                Console.WriteLine($"SINK RATE {(sinkNow ? "ON" : "off")}: alt={snap.AltM:F0} m vy={snap.VyMs:F0} m/s thr={sinkThr:F0}");

            opt.PullUpAlert = pullUpNow;
            opt.SinkRateAlert = !pullUpNow && sinkNow;
            opt.ShowPitchLadder = cfg.ShowPitchLadder;
            opt.ShowFpm = cfg.ShowFpm;

            // Full roll angle for the upright / inverted indicator. The bank estimate used by
            // the ladder is clamped to +/-80 deg, so it can never detect an inverted attitude;
            // integrate Wx instead and gently pull it back whenever the aircraft is upright.
            double stepRoll = Math.Clamp(tNow - rollLastTime, 0, 0.2);
            rollLastTime = tNow;
            rollFull += snap.RollRateDegS * stepRoll;
            while (rollFull > 180) rollFull -= 360;
            while (rollFull < -180) rollFull += 360;
            // Roll integration was removed: it drifted after full rolls and the clamped,
            // turn-rate based bank is more trustworthy on the long run.
            opt.RollFullDeg = shown.BankDeg;
            opt.Inverted = Math.Abs(shown.BankDeg) > 90;

            // Fuel endurance: least-squares slope of the fuel mass over a 12 s sliding window.
            // A per-sample derivative of an integer kg counter is mostly quantisation noise and
            // made the estimate wander upwards.
            double fuelNow = snap.FuelKg;
            if (fuelNow > 0.5)
            {
                // Refuelling / rearming makes the fuel mass jump upwards; drop the window so the
                // regression does not average a rise into a bogus consumption rate.
                if (fuelLastSample > 1 && fuelNow > fuelLastSample + 3) fuelWindow.Clear();
                fuelLastSample = fuelNow;

                fuelWindow.Enqueue((tNow, fuelNow));
                while (fuelWindow.Count > 2 && tNow - fuelWindow.Peek().T > 12.0) fuelWindow.Dequeue();
                while (fuelWindow.Count > 400) fuelWindow.Dequeue();

                if (fuelWindow.Count >= 8)
                {
                    double sx = 0, sy = 0, sxy = 0, sxx = 0;
                    int n = 0;
                    foreach (var (tt, ff) in fuelWindow)
                    {
                        sx += tt; sy += ff; sxy += tt * ff; sxx += tt * tt; n++;
                    }
                    double denom = n * sxx - sx * sx;
                    if (Math.Abs(denom) > 1e-6)
                    {
                        double slope = (n * sxy - sx * sy) / denom;   // kg/s, negative while burning
                        double flow = -slope;
                        // Only a genuine decrease counts: a positive slope means fuel was added.
                        if (flow > 0.02 && flow < 200)
                        {
                            fuelFlow = flow;
                            fuelFlowAt = tNow;
                        }
                    }
                }
            }

            // A stale estimate produces nonsense endurance, so expire it quickly.
            bool freshFlow = fuelFlow > 0.05 && tNow - fuelFlowAt < 8.0;
            opt.EnduranceSeconds = freshFlow && fuelNow > 1
                ? fuelNow / (fuelFlow * Math.Max(0.2, cfg.EnduranceScale))
                : -1;

            // Landing gear in transit: 0..100 % in /state, blink the direction.
            double gearNow = snap.GearPercent;
            if (gearNow >= 0)
            {
                if (lastGear >= 0 && Math.Abs(gearNow - lastGear) > 0.05)
                {
                    if (gearNow > lastGear + 0.05) opt.GearTransitText = WtHud.UI.L10n.Get(WtHud.UI.LangScope.Alerts, "gear.down");
                    else if (gearNow < lastGear - 0.05) opt.GearTransitText = WtHud.UI.L10n.Get(WtHud.UI.LangScope.Alerts, "gear.up");
                }
                lastGear = gearNow;
            }
            bool gearTransitNow = gearNow > 0.5 && gearNow < 99.5 && snap.Valid;
            long gearTicks = Environment.TickCount64;
            if (gearWasTransit && !gearTransitNow && snap.Valid && gearNow >= 0)
            {
                // Gear just settled: flash a green STB confirmation for 0.8 s.
                opt.GearConfirmText = "STB";   // compact steady confirmation after the gear settles
                gearConfirmUntil = gearTicks + 800;
                Console.WriteLine($"gear settled -> {opt.GearConfirmText}");
            }
            gearWasTransit = gearTransitNow;
            opt.GearTransitActive = gearTransitNow;
            opt.GearConfirmActive = gearTicks < gearConfirmUntil;

            // Steady readout from the reported state: aircraft whose gear reaches 100 % instantly
            // never show a transition, and the player still wants to see that it is down.
            opt.GearDownSteady = snap.Valid && gearNow >= 99.5 && !opt.GearTransitActive;

            // Flaps: transit is detected from movement (they can stop at intermediate settings).
            double flapsNow = snap.FlapsPercent;
            long nowTicks = Environment.TickCount64;
            if (flapsNow >= 0 && lastFlaps >= 0 && Math.Abs(flapsNow - lastFlaps) > 1.0)
            {
                opt.FlapsTransitText = flapsNow > lastFlaps
                    ? WtHud.UI.L10n.Get(WtHud.UI.LangScope.Alerts, "flaps.down")
                    : WtHud.UI.L10n.Get(WtHud.UI.LangScope.Alerts, "flaps.up");
                lastFlapsChangeTicks = nowTicks;
            }
            lastFlaps = flapsNow;
            opt.FlapsTransitActive = snap.Valid && nowTicks - lastFlapsChangeTicks < 350 && !string.IsNullOrEmpty(opt.FlapsTransitText);

            // Airbrake: same movement-based detection; a deployed brake also gets a static box.
            // The A-10 family is special: its split-aileron (deceleron) brake makes the reported
            // percentage follow the ailerons, so those types follow the airbrake key instead.
            double abNow = snap.AirbrakePercent;
            bool deceleron = snap.VehicleType.StartsWith("a_10", StringComparison.OrdinalIgnoreCase);
            opt.DeceleronAirbrake = deceleron;

            if (deceleron)
            {
                opt.AirbrakeSteadyText = "AIR BRAKE";
                opt.AirbrakeDeployed = airbrakeKeyOn;
                opt.AirbrakeTransitActive = false;
                lastAirbrake = abNow;
            }
            else
            {
                opt.AirbrakeSteadyText = WtHud.UI.L10n.Get(WtHud.UI.LangScope.Alerts, "brake.steady");
                if (abNow >= 0 && lastAirbrake >= 0 && Math.Abs(abNow - lastAirbrake) > 1.0)
                {
                    opt.AirbrakeTransitText = abNow > lastAirbrake
                    ? WtHud.UI.L10n.Get(WtHud.UI.LangScope.Alerts, "brake.out")
                    : WtHud.UI.L10n.Get(WtHud.UI.LangScope.Alerts, "brake.in");
                    lastAirbrakeChangeTicks = nowTicks;
                }
                lastAirbrake = abNow;
                opt.AirbrakeTransitActive = snap.Valid && nowTicks - lastAirbrakeChangeTicks < 350
                                            && !string.IsNullOrEmpty(opt.AirbrakeTransitText);
                // Flaps: blink only while moving; settled at a non-zero setting -> steady box.
                opt.AirbrakeDeployed = snap.Valid && abNow > 5 && !opt.AirbrakeTransitActive;
            }
            opt.FlapsDeployed = snap.Valid && !opt.FlapsTransitActive && flapsNow > 0.5;
            opt.FlapsSteadyText = flapsNow.ToString("0");

            // ---- configuration debug block (toggled in F1) ----
            opt.ConfigDebug = cfg.ConfigDebug;
            if (cfg.ConfigDebug)
            {
                opt.ConfigDebugLines = new[]
                {
                    "CFG gear " + snap.GearPercent.ToString("F0") + "% t=" + B(opt.GearTransitActive)
                        + " c=" + B(opt.GearConfirmActive) + " d=" + B(opt.GearDownSteady),
                    "CFG flaps " + snap.FlapsPercent.ToString("F0") + "% t=" + B(opt.FlapsTransitActive)
                        + " d=" + B(opt.FlapsDeployed) + " raw=" + opt.FlapsSteadyText,
                    "CFG air " + snap.AirbrakePercent.ToString("F0") + "% t=" + B(opt.AirbrakeTransitActive)
                        + " d=" + B(opt.AirbrakeDeployed) + " dec=" + B(opt.DeceleronAirbrake)
                        + " key=" + B(airbrakeKeyOn),
                };
            }
            else
            {
                opt.ConfigDebugLines = System.Array.Empty<string>();
            }

            // Only submit a frame when something actually changed: the API updates at 17-25 Hz
            // while we would otherwise recompose the full 4K surface at 100+ Hz.
        // 每帧刷新坡度耦合：auto = 地图无其它飞机标记时视为全真（座舱）→ 开
        var simLike = lastAircraftMarkers == 0;
        cal.RollCoupling = cfg.RollCouplingMode == "auto"
            ? (simLike ? 1.0 : 0.0)
            : cfg.RollCouplingMode == "on" ? 1.0
            : cfg.RollCouplingMode == "off" ? 0.0 : cfg.RollCoupling;

            // 跟随游戏窗口：覆盖层铺满整屏，只调整 HUD 的中心偏移和角度比例
            if (nowTicks - lastFramePollTicks > 400)
            {
                lastFramePollTicks = nowTicks;
                if (GameWindowTracker.IsActive
                    && GameWindowTracker.TryGetClientRect(out var fx, out var fy, out var fw2, out var fh2, out _))
                {
                    // 游戏换到另一块显示器时，把覆盖层挪过去
                    if (GameWindowTracker.TryGetMonitorRect(out var nmx, out var nmy, out var nmw, out var nmh)
                        && (nmx != scrX || nmy != scrY || nmw != scrW || nmh != scrH))
                    {
                        scrX = nmx; scrY = nmy; scrW = nmw; scrH = nmh;
                        overlay.SetFrame(scrX, scrY, scrW, scrH);
                        Console.WriteLine($"overlay monitor -> {scrX},{scrY} {scrW}x{scrH}");
                    }

                    var ox = fx + fw2 / 2.0 - (scrX + scrW / 2.0);
                    var oy = fy + fh2 / 2.0 - (scrY + scrH / 2.0);
                    if (Math.Abs(ox - cal.CenterOffsetX) > 0.5 || Math.Abs(oy - cal.CenterOffsetY) > 0.5
                        || Math.Abs(cal.PxReferenceHeight - fh2) > 0.5)
                    {
                        cal.CenterOffsetX = ox;
                        cal.CenterOffsetY = oy;
                        cal.PxReferenceHeight = fh2;
                        overlay.SetCursorHoleCenter((int)Math.Round(ox), (int)Math.Round(oy));
                        lastFrameKey = -1;
                        Console.WriteLine($"overlay follow -> game {fx},{fy} {fw2}x{fh2} offset {ox:F0},{oy:F0}");
                    }
                }
            }

            if (Environment.GetEnvironmentVariable("WT_CURSORDBG") == "1"
                && nowTicks - cursorProbeTicks > 2000
                && NativeMethods.GetCursorPos(out var curPos))
            {
                cursorProbeTicks = nowTicks;
                Console.WriteLine($"cursor probe: cursor={curPos.X},{curPos.Y} " +
                                  $"offset=({cal.CenterOffsetX:F0},{cal.CenterOffsetY:F0}) " +
                                  $"hole=({cal.CenterOffsetX:F0},{cal.CenterOffsetY:F0}) screen={scrW}x{scrH}");
            }

            // 俯仰方向检测：用游戏姿态 vs 航迹角(gamma) 的相关系数判断符号
            if (WtHud.Hud.PitchDetect.Active)
                WtHud.Hud.PitchDetect.Feed(snap.PitchDeg, att.PitchDeg);   // att = 实际画到 HUD 上的俯仰

            if (WtHud.Hud.PitchDetect.Active && nowTicks - lastPitchProbeTicks > 2000)
            {
                lastPitchProbeTicks = nowTicks;
                var dbgPitch = snap.PitchDeg;
                Console.WriteLine("pitchdetect probe: pitch=" +
                                  (dbgPitch.HasValue ? dbgPitch.Value.ToString("F1") : "null") +
                                  " gamma=" + snap.FlightPathAngleDeg.ToString("F1") +
                                  " samples=" + WtHud.Hud.PitchDetect.SampleCount +
                                  " skip=" + WtHud.Hud.PitchDetect.Rejected);
            }

            long key = FrameKey(snap, shown, opt, cal, draw, hiddenByView);
            if (key != lastFrameKey)
            {
                // Build the symbology only for frames we actually submit.
                long b0 = perf ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
                var prims = draw ? geo.Build(snap, shown, w, h) : empty;
                long b1 = perf ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
                overlay.Present(prims);
                if (perf)
                {
                    var msTick = 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                    perfBuildMs += (b1 - b0) * msTick;
                    perfDrawMs += overlay.LastDrawMs;
                    perfPresentMs += overlay.LastPresentMs;
                    perfPresents++;
                }
                lastFrameKey = key;
                presents++;
            }
            frames++;

            if (perf && nowTicks - perfLastPrintTicks > 2000)
            {
                var perfDt = (nowTicks - perfLastPrintTicks) / 1000.0;
                perfLastPrintTicks = nowTicks;
                var n = Math.Max(1, perfPresents);
                var fps = perfPresents / Math.Max(0.001, perfDt);
                Console.WriteLine($"perf: fps={fps:F1} presents={perfPresents} samples={n}  " +
                                  $"build={perfBuildMs / n:F2}ms draw={perfDrawMs / n:F2}ms present={perfPresentMs / n:F2}ms  " +
                                  $"loop={perfDt * 1000.0 / Math.Max(1, perfLoops):F2}ms");
                perfBuildMs = perfDrawMs = perfPresentMs = 0;
                perfPresents = 0;
                perfLoops = 0;
            }
            perfLoops++;

            // Cursor visibility probe: the OS cursor is drawn either by our overlay (invisible
            // class cursor) or by the game window underneath. Log every transition so a stray
            // arrow can be traced to a window/process instead of guessed at.
            if ((frames & 7) == 0)
            {
                string curDesc = NativeMethods.DescribeCursor();
                bool curShowing = curDesc.Contains("showing=True");
                if (curShowing != lastCursorShowing)
                {
                    lastCursorShowing = curShowing;
                    Console.WriteLine($"cursor transition -> {curDesc}  hole={overlay.CursorHoleHalfPx * 2}px fg={NativeMethods.GetForegroundWindow()}");
                }
            }


            if (clock.Elapsed.TotalSeconds - lastLog > 2.5)
            {
                lastLog = clock.Elapsed.TotalSeconds;
                Console.WriteLine($"t={clock.Elapsed.TotalSeconds:F1}s loop={frames / clock.Elapsed.TotalSeconds:F0}fps presents={presents} draw={draw} " +
                                  $"cursor[{NativeMethods.DescribeCursor()}] " +
                                  $"snap={(latest?.ToString() ?? "none")}");
                Console.WriteLine($"      att={att}  pitchSign={fusion.DetectedPitchSign} hasHdg={snap.HasHeading} wx={snap.RollRateDegS:F0}");
                Console.WriteLine("      cfgDiag gear=" + snap.GearPercent.ToString("F0") + " gt=" + opt.GearTransitActive
                                  + " gc=" + opt.GearConfirmActive + " gd=" + opt.GearDownSteady
                                  + " | flaps=" + snap.FlapsPercent.ToString("F0") + " ft=" + opt.FlapsTransitActive
                                  + " fd=" + opt.FlapsDeployed
                                  + " | air=" + snap.AirbrakePercent.ToString("F0") + " ad=" + opt.AirbrakeDeployed
                                  + " at=" + opt.AirbrakeTransitActive + " dec=" + opt.DeceleronAirbrake);
                if (snap.Valid && fuelFlow > 0.01 && clock.Elapsed.TotalSeconds - fuelLogAt > 10)
                {
                    fuelLogAt = clock.Elapsed.TotalSeconds;
                    int secs = (int)Math.Max(0, opt.EnduranceSeconds);
                    Console.WriteLine($"      fuel: flow={fuelFlow:F2} kg/s scale={cfg.EnduranceScale:F2} fuel={snap.FuelKg:F0} kg -> endurance={secs / 60}:{secs % 60:00}");
                }
                Console.WriteLine($"      map self={mapData.Self?.ToString() ?? "null"} size={mapData.MapSizeM.X:F0}x{mapData.MapSizeM.Y:F0} airfields={mapData.Airfields.Count}");
                if (!string.IsNullOrEmpty(opt.GridText) || !string.IsNullOrEmpty(opt.NavFriendly) || !string.IsNullOrEmpty(opt.NavEnemy))
                    Console.WriteLine($"      nav({gridDebug}) grid={opt.GridText} friendly='{opt.NavFriendly}' enemy='{opt.NavEnemy}' edge={opt.EdgeWarning}");
            }

            // Only spin fast while the aiming layout is animating; otherwise a 30 Hz loop is
            // plenty because the API itself only delivers 17-25 frames per second.
            frameBudget = Math.Abs(zoomTarget - zoomBlend) > 0.005 ? 1.0 / 90.0 : 1.0 / 30.0;

            // frame pacing: a 1 ms Thread.Sleep rounds up to the 15.6 ms timer tick and
            // pins the overlay to ~40 fps, so sleep only for coarse waits and spin the rest.
            double now = clock.Elapsed.TotalSeconds;
            nextFrame += frameBudget;
            double wait = nextFrame - now;
            if (wait > 0.002) Thread.Sleep((int)(wait * 1000));
            else if (wait > 0) Thread.SpinWait(300);
            else nextFrame = now;
        }

        cursorStop = true;
        telemetry.Dispose();
        Console.WriteLine($"overlay closed after {frames} frames");
        return 0;
    }


    // ---------------------------------------------------------------- navigation
    private static readonly string[] Arrows =
        { "↑", "↗", "→", "↘", "↓", "↙", "←", "↖" };

    /// <summary>
    /// Grid cell, airfield distances and the map-edge warning. Only the player icon and static
    /// airfields are used - enemy and ground entries are filtered out inside MapData.
    /// </summary>
    /// <summary>推进 SPLASH 播放队列：每条 2.2 s，带 120 ms 淡入 / 300 ms 淡出。</summary>
    private static void UpdateSplash(HudOptions opt)
    {
        var now = DateTime.UtcNow;
        if (!string.IsNullOrEmpty(splashTarget) && now >= splashUntilUtc)
            splashTarget = "";

        if (string.IsNullOrEmpty(splashTarget) && splashQueue.Count > 0)
        {
            splashTarget = splashQueue.Dequeue();
            splashStartUtc = now;
            splashUntilUtc = now.AddSeconds(1.2);
        }

        if (string.IsNullOrEmpty(splashTarget))
        {
            opt.SplashMain = "";
            opt.SplashSub = "";
            opt.SplashAlpha = 0;
            return;
        }

        double total = (splashUntilUtc - splashStartUtc).TotalSeconds;
        _ = total;
        opt.SplashMain = WtHud.UI.L10n.Get(WtHud.UI.LangScope.Alerts, "splash");
        opt.SplashSub = splashTarget;
        opt.SplashAlpha = 1.0;      // 无动画：直接出现，到点直接消失
    }

    /// <summary>
    /// 最近一架友机的距离变化率（正 = 正在接近，m/s）。
    /// <para>目标切换时距离会突变上千米，这时候历史必须清空，否则会算出一个荒唐的接近率。</para>
    /// </summary>
    private static double UpdateClosingRate(double rangeM, double nowS)
    {
        // 换了目标（距离突变上千米）就把引导状态整体清掉：平滑值和动作滞回都只对前一架有效
        if (closingHistory.Count > 0 && Math.Abs(rangeM - closingHistory[^1].R) > 1000)
            ResetGuideState();

        closingHistory.Add((nowS, rangeM));
        while (closingHistory.Count > 2 && nowS - closingHistory[0].T > ClosingWindowS)
            closingHistory.RemoveAt(0);

        if (closingHistory.Count < 2) return 0;

        double span = closingHistory[^1].T - closingHistory[0].T;
        if (span < 0.5) return 0;

        return (closingHistory[0].R - closingHistory[^1].R) / span;
    }

    /// <summary>
    /// 油门建议的一阶低通。时间常数 1.2 s：够快能跟上编队修正，够慢不会让数字来回跳。
    /// <para>首帧、断档（切图/暂停）或目标切换时直接取当前值，避免从 0 慢慢爬上去。</para>
    /// </summary>
    private static double SmoothGuideDelta(double raw, double nowS)
    {
        double dt = nowS - guideDeltaLastT;
        if (!guideDeltaValid || dt <= 0 || dt > 1.0)
        {
            guideDeltaSmooth = raw;
            guideDeltaValid = true;
            guideDeltaLastT = nowS;
            return guideDeltaSmooth;
        }

        double k = Math.Min(1.0, dt / 1.2);
        guideDeltaSmooth += (raw - guideDeltaSmooth) * k;
        guideDeltaLastT = nowS;
        return guideDeltaSmooth;
    }

    /// <summary>切目标或关闭编队时把引导状态清干净，免得拿上一架的平滑值接着算。</summary>
    private static void ResetGuideState()
    {
        closingHistory.Clear();
        guideDeltaSmooth = 0;
        guideDeltaLastT = 0;
        guideDeltaValid = false;
        guideActionPrev = FormationAction.None;
        guideIntegral = 0;
        guideIntegralLastT = 0;
    }

    private static void UpdateNavigation(HudOptions opt, WtHud.Telemetry.MapData map, TelemetrySnapshot snap, HudConfig cfg)
    {
        // 编队开关：关掉时既不解析友机（MapData.CollectFriendlies），也不做方位/距离计算。
        bool formationOn = cfg.FormationAssist || cfg.FormationOnly;
        map.CollectFriendlies = formationOn;
        opt.FormationAssist = formationOn;
        opt.FormationOnly = cfg.FormationOnly;
        opt.Friendlies.Clear();
        opt.Guide = null;
        if (!formationOn) ResetGuideState();

        opt.GridText = "";
        opt.EdgeWarning = false;
        opt.NavFriendly = "";
        opt.NavEnemy = "";

        if (map.Self is not { } self || !snap.Valid) return;

        // Heading used for the bearing arrows: the aircraft compass when available, otherwise the
        // course over ground derived from map position changes (compass-less types like Rafale C).
        double navHeading = snap.HeadingDeg;
        bool hasHeading = snap.HasHeading;
        if (!hasHeading && map.CourseDeg is double compasslessCog)
        {
            navHeading = compasslessCog;
            hasHeading = true;
        }

        double sizeX = map.MapSizeM.X > 1 ? map.MapSizeM.X : 52719;
        double sizeY = map.MapSizeM.Y > 1 ? map.MapSizeM.Y : 55385;

        // --- 编队辅助：友机相对方位 + 距离（开关关闭时整段跳过） ---
        // 敌人已在 MapData 里按 color 丢掉，这里只剩友军；只取最近的 4 架，避免糊成一片。
        if (formationOn)
        {
            foreach (var f in map.Friendlies)
            {
                double dEast = (f.X - self.X) * sizeX;
                double dNorth = (f.Y - self.Y) * sizeY * (cfg.MapYFlip ? -1 : 1);
                double range = Math.Sqrt(dEast * dEast + dNorth * dNorth);
                double bearing = Math.Atan2(dEast, dNorth) * 180.0 / Math.PI;
                if (bearing < 0) bearing += 360;
                // 直接算成相对方位（navHeading 已含"无罗盘时用航迹角兜底"），几何层不再需要航向
                double rel = bearing - navHeading;
                while (rel > 180) rel -= 360;
                while (rel < -180) rel += 360;
                opt.Friendlies.Add(new FriendlyContact(rel, range, f.Icon));
            }
            opt.Friendlies.Sort((a, b) => a.RangeM.CompareTo(b.RangeM));
            while (opt.Friendlies.Count > 4) opt.Friendlies.RemoveAt(opt.Friendlies.Count - 1);

            // --- 直接引导：只针对最近那一架（"往哪转、加减速"） ---
            // 门槛都写在这里而不是几何层，方便按真机手感调，不用碰绘制代码。
            if (opt.Friendlies.Count > 0)
            {
                var lead = opt.Friendlies[0];
                double closing = UpdateClosingRate(lead.RangeM, snap.ElapsedSeconds);

                FormationAction action = FormationAction.None;
                double deltaKmh = 0;

                // 偏角大时先转向，不给油门建议：斜着几十度去修速度没有意义。
                if (Math.Abs(lead.RelBearingDeg) < 50)
                {
                    // 级联 PID：外环 PI 把距离误差换成"期望接近率"，内环 P 把接近率之差换成油门。
                    // 三个增益都在 hud-config.json 里（formationKp / formationKi / formationKv）。
                    double err = lead.RangeM - FormationTargetRangeM;

                    double nowS = snap.ElapsedSeconds;
                    double dt = guideIntegralLastT > 0
                        ? Math.Clamp(nowS - guideIntegralLastT, 0, 0.5)
                        : 0;
                    guideIntegralLastT = nowS;

                    // 增益调度：距离误差大时用小增益（慢慢追，别给自己挖坑），
                    // 逼近 400 m 时过渡到大增益（快速收尾）。这一条把到位时间又压掉两成。
                    double blend = Math.Max(100.0, cfg.FormationBlendM);
                    double far = Math.Clamp(err / blend, 0.0, 1.0);
                    double kp = err > 0
                        ? cfg.FormationKp + (cfg.FormationKpNear - cfg.FormationKp) * (1.0 - far)
                        : cfg.FormationKpNear;      // 太近了要果断拉开，直接用大增益

                    double wantClosing = kp * err + cfg.FormationKi * guideIntegral;
                    // ★ 这一层限幅是安全底线。放开它会在大距离上索要上百 m/s 的接近率，
                    //   物理上根本刹不住（实测交叉场景需要 10 km 才能减速到位）。
                    wantClosing = Math.Clamp(wantClosing, -20, 30);

                    // 内环：期望与实际接近率之差 → 油门；按偏角修正（斜着飞时沿视线的分量更小）
                    double cosOff = Math.Max(0.5, Math.Cos(lead.RelBearingDeg * Math.PI / 180));
                    double dvRaw = (wantClosing - closing) * cfg.FormationKv / cosOff * 3.6;

                    // 平滑 + 收窄：不这么做的话数字会在几百 km/h 之间跳，看着没法用
                    deltaKmh = Math.Clamp(SmoothGuideDelta(dvRaw, nowS), -120, 120);

                    // 抗积分饱和：外环顶到限幅、或油门已经饱和时都不再累积
                    if (dt > 0 && Math.Abs(deltaKmh) < 114 && Math.Abs(wantClosing) < 29.4)
                        guideIntegral = Math.Clamp(guideIntegral + err * dt,
                                                   -GuideIntegralLimit, GuideIntegralLimit);

                    // 滞回：进入要 18，已经在动作里只要还大于 8 就继续，避免在两个动作之间抖
                    bool wasAccel = guideActionPrev == FormationAction.Accelerate;
                    bool wasDecel = guideActionPrev == FormationAction.Decelerate;
                    if (Math.Abs(deltaKmh) >= 18
                        || (wasAccel && deltaKmh >= 8)
                        || (wasDecel && deltaKmh <= -8))
                    {
                        action = deltaKmh > 0 ? FormationAction.Accelerate : FormationAction.Decelerate;
                    }
                    else
                    {
                        // "保持"也要滞回：已经稳住时放宽门槛，否则会在 400 m 附近一闪一闪
                        bool wasHold = guideActionPrev == FormationAction.Hold;
                        double holdErr = wasHold ? 140 : 100;
                        double holdClosing = wasHold ? 12 : 8;
                        if (Math.Abs(err) <= holdErr && Math.Abs(closing) < holdClosing)
                            action = FormationAction.Hold;
                    }
                }

                // 滞回状态每帧都更新（偏角大到没给建议时也要清掉，否则残留到下一段）
                guideActionPrev = action;

                opt.Guide = new FormationLead(lead.RelBearingDeg, lead.RangeM,
                                             FormationTargetRangeM, action, deltaKmh);
            }
            else
            {
                opt.Guide = null;
                ResetGuideState();
            }
        }

        // --- grid cell (letters = east axis, numbers = north axis) ---
        double colF = (self.X * sizeX - map.GridZeroM.X) / map.GridStepM.X;
        double rowF = (self.Y * sizeY - map.GridZeroM.Y) / map.GridStepM.Y;
        // NOTE: the flip only applies to bearings; the grid row already counts from the map origin.
        int col = (int)Math.Floor(colF);
        int row = (int)Math.Floor(rowF);
        if (col >= 0 && col < 26 && row >= -1 && row < 40)
            opt.GridText = $"{(char)('A' + col)}{row + 1}";

        // --- map edge warning (approximation of the battle area) ---
        double margin = Math.Min(Math.Min(self.X, 1 - self.X), Math.Min(self.Y, 1 - self.Y));
        if (snap.InFlight && margin < cfg.EdgeMarginFraction)
        {
            opt.EdgeWarning = true;
            double dEast = (0.5 - self.X) * sizeX;
            double dNorth = (0.5 - self.Y) * sizeY * (cfg.MapYFlip ? -1 : 1);
            double bearingToCentre = Math.Atan2(dEast, dNorth) * 180.0 / Math.PI;
            double rel = bearingToCentre - navHeading;
            while (rel < 0) rel += 360;
            while (rel >= 360) rel -= 360;
            opt.EdgeArrow = hasHeading ? Arrows[(int)Math.Round(rel / 45.0) % 8] : "";
        }

        if (Environment.GetEnvironmentVariable("WT_NAVDBG") == "1"
            && (DateTime.UtcNow - navDbgUtc).TotalSeconds >= 2)
        {
            navDbgUtc = DateTime.UtcNow;
            Console.WriteLine($"[navdbg] enter map=ok " +
                              $"self={(map.Self is null ? "null" : "ok")} units={map.GroundUnits.Count} " +
                              $"fields={map.Airfields.Count}");
        }

        lastAircraftMarkers = map.AircraftMarkers;

        // --- nearest friendly / enemy airfield ---
        double bestFriendly = double.MaxValue, bestEnemy = double.MaxValue;
        double relFriendly = 0, relEnemy = 0;
        foreach (var af in map.Airfields)
        {
            double dEast = (af.X - self.X) * sizeX;
            double dNorth = (af.Y - self.Y) * sizeY * (cfg.MapYFlip ? -1 : 1);
            double dist = Math.Sqrt(dEast * dEast + dNorth * dNorth);
            double bearing = Math.Atan2(dEast, dNorth) * 180.0 / Math.PI;
            double rel = bearing - navHeading;
            while (rel < 0) rel += 360;
            while (rel >= 360) rel -= 360;

            if (af.Friendly && dist < bestFriendly) { bestFriendly = dist; relFriendly = rel; }
            else if (!af.Friendly && dist < bestEnemy) { bestEnemy = dist; relEnemy = rel; }
        }

        string Arrow(double rel) => Arrows[(int)Math.Round(rel / 45.0) % 8];

        // Without a compass the "relative to nose" bearing is meaningless (heading reads 0), so
        // Without a compass the relative bearing is meaningless: distance only.
        var afLabel = WtHud.UI.L10n.Get(WtHud.UI.LangScope.Alerts, "af");
        var afEnemyLabel = WtHud.UI.L10n.Get(WtHud.UI.LangScope.Alerts, "afenemy");
        if (bestFriendly < double.MaxValue)
            opt.NavFriendly = hasHeading
                ? $"{afLabel} {Arrow(relFriendly)} {bestFriendly / 1000.0:F1}km"
                : $"{afLabel} {bestFriendly / 1000.0:F1}km";
        // 友方地面单位：取 30 km 内最近的 2 个，显示类型（防空优先）与相对方位
        opt.NavUnit1 = "";
        opt.NavUnit2 = "";
        if (map.Self is { } selfPos && map.GroundUnits.Count > 0)
        {
            string IcoShort(string icon) => icon switch
            {
                "SPAA" => "AA",
                "IFV" => "IFV",
                "MediumTank" => "MBT",
                "LightTank" => "LT",
                _ => icon.Length > 3 ? icon[..3].ToUpperInvariant() : icon.ToUpperInvariant(),
            };

            var near = map.GroundUnits
                .Select(u =>
                {
                    double dx = (u.X - selfPos.X) * sizeX;
                    double dn = (u.Y - selfPos.Y) * sizeY * (cfg.MapYFlip ? -1 : 1);
                    double d = Math.Sqrt(dx * dx + dn * dn);
                    double brg = Math.Atan2(dx, dn) * 180.0 / Math.PI;
                    return (Dist: d, Brg: brg, u.Icon);
                })
                .Where(u => cfg.FriendlySpaaKm > 0 && u.Dist / 1000.0 <= cfg.FriendlySpaaKm && u.Icon == "SPAA")
                .OrderBy(u => u.Dist)
                .Take(1)
                .ToList();

            string Fmt((double Dist, double Brg, string Icon) u)
            {
                var km = u.Dist / 1000.0;
                var label = IcoShort(u.Icon);
                if (hasHeading)
                {
                    double relU = u.Brg - navHeading;
                    while (relU < 0) relU += 360;
                    while (relU >= 360) relU -= 360;
                    return $"{label} {Arrow(relU)} {relU:F0}° {km:F1}km";
                }
                return $"{label} {km:F1}km";
            }

            if (near.Count > 0) opt.NavUnit1 = Fmt(near[0]);   // 只显示最近的友军防空

        }

        opt.EnemyAirfieldAlert = false;
        if (bestEnemy < double.MaxValue)
        {
            opt.NavEnemy = hasHeading
                ? $"{afEnemyLabel} {Arrow(relEnemy)} {bestEnemy / 1000.0:F1}km"
                : $"{afEnemyLabel} {bestEnemy / 1000.0:F1}km";
            double alertKm = cfg.EnemyAirfieldAlertKm;
            opt.EnemyAirfieldAlert = alertKm > 0 && bestEnemy / 1000.0 < alertKm;
        }
    }

    /// <summary>
    /// 把游戏窗口拉回前台。SetForegroundWindow 受前台权限限制，而 WtHud 自己是
    /// WS_EX_NOACTIVATE（拿不到前台授权），所以借用当前前台线程的输入队列再调用。
    /// </summary>
    private static void ForceForegroundWindow(IntPtr hwnd)
    {
        var fg = NativeMethods.GetForegroundWindow();
        uint myTid = NativeMethods.GetCurrentThreadId();
        uint fgTid = fg == IntPtr.Zero ? 0 : (uint)NativeMethods.GetWindowThreadProcessId(fg, out _);
        bool attached = fgTid != 0 && fgTid != myTid && NativeMethods.AttachThreadInput(myTid, fgTid, true);
        try
        {
            NativeMethods.SetForegroundWindow(hwnd);
        }
        finally
        {
            if (attached) NativeMethods.AttachThreadInput(myTid, fgTid, false);
        }
    }

    private static bool IsForegroundProcess(int pid)
    {
        var h = NativeMethods.GetForegroundWindow();
        if (h == IntPtr.Zero) return false;
        NativeMethods.GetWindowThreadProcessId(h, out int fg);
        return fg == pid;
    }

    /// <summary>进程是否以管理员（高完整性级别）运行。</summary>
    private static bool IsProcessElevated(int pid)
    {
        if (pid == 0) return false;
        IntPtr h = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h == IntPtr.Zero) return false;
        try
        {
            if (!NativeMethods.OpenProcessToken(h, NativeMethods.TOKEN_QUERY, out IntPtr token)) return false;
            try
            {
                var info = new NativeMethods.TOKEN_ELEVATION();
                if (!NativeMethods.GetTokenInformation(token, NativeMethods.TokenElevation, ref info,
                        (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.TOKEN_ELEVATION>(),
                        out _))
                    return false;
                return info.TokenIsElevated != 0;
            }
            finally { NativeMethods.CloseHandle(token); }
        }
        finally { NativeMethods.CloseHandle(h); }
    }

    /// <summary>
    /// 游戏以管理员运行、而 WtHud 不是：UIPI 会挡住游戏在前台时的全局热键（F1/F10/F9…）。
    /// 自动以管理员身份重启自己（只弹一次 UAC）。用 --elevated-retry 防止无限重启。
    /// </summary>
    private static void EnsureElevationMatchesGame(int gamePid)
    {
        if (gamePid == 0) return;
        if (Environment.GetCommandLineArgs().Any(a => a == "--elevated-retry")) return;
        if (!IsProcessElevated(gamePid) || IsProcessElevated(Environment.ProcessId)) return;

        Console.WriteLine("game is elevated but WtHud is not -> restarting WtHud as administrator " +
                          "(UIPI blocks hotkeys while an elevated window has focus)");
        try
        {
            string exe = Environment.ProcessPath ?? "WtHud.exe";
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = exe,
                Arguments = "overlay --elevated-retry",
                UseShellExecute = true,
                Verb = "runas",
            };
            System.Diagnostics.Process.Start(psi);
        }
        catch (Exception ex)
        {
            Console.WriteLine("elevation relaunch cancelled/failed: " + ex.Message +
                              " -- in-game hotkeys will NOT work; run WtHud as administrator manually");
            return;
        }
        Environment.Exit(0);
    }

    /// <summary>How far (0..1 of one sample) to extrapolate past the newest snapshot.</summary>
    private static double ExtrapolationFraction(TelemetrySnapshot? prev, TelemetrySnapshot? curr, DateTime now)
    {
        if (prev == null || curr == null) return 0;
        double span = (curr.TimestampUtc - prev.TimestampUtc).TotalMilliseconds;
        if (span < 5 || span > 250) return 0;
        double age = (now - curr.TimestampUtc).TotalMilliseconds;
        return Math.Clamp(age / span, 0, 2.5);
    }

    private static long FrameKey(TelemetrySnapshot s, AttitudeSolution a, HudOptions o, HudCalibration c, bool draw, bool hiddenByView)
    {
        unchecked
        {
            long k = 1469598103934665603;
            void Mix(double v, double q)
            {
                k ^= (long)Math.Round(v / q);
                k *= 1099511628211;
            }
            Mix(draw ? 1 : 0, 1);
            Mix(hiddenByView ? 1 : 0, 1);
            Mix(o.BodyFixedOnly ? 1 : 0, 1);
            Mix(o.ZoomBlend, 0.01);
            Mix(c.VerticalFovDeg, 0.01);
            Mix(c.LayoutOffsetYDeg, 0.01);
            Mix(c.RollCoupling, 0.01);
            Mix(s.IasKmh, 0.5); Mix(s.Mach, 0.005); Mix(s.AltM, 0.5);
            Mix(s.VyMs, 0.5); Mix(s.AoaDeg, 0.1); Mix(s.AosDeg, 0.1); Mix(s.NyG, 0.05);
            Mix(s.HeadingDeg, 0.1); Mix(s.FuelKg, 5); Mix(a.PitchDeg, 0.05); Mix(a.BankDeg, 0.1);
            Mix((int)a.Source, 1);

            // Navigation text and the altitude-tape value must be able to trigger a repaint on
            // their own, otherwise the grid cell / bearing arrows only refresh when some other
            // number happens to move.
            void MixStr(string? str)
            {
                int h = 0;
                if (str != null) foreach (char ch in str) h = unchecked(h * 31 + ch);
                Mix(h, 1);
            }
            MixStr(o.GridText);
            MixStr(o.NavFriendly);
            MixStr(o.NavEnemy);
            MixStr(o.EdgeArrow);
            Mix(o.EdgeWarning ? 1 : 0, 1);
            Mix(o.EnemyAirfieldAlert ? 1 : 0, 1);
            Mix(o.OverspeedAlert ? 1 : 0, 1);
            Mix(o.StallWarnLevel, 1);
    
            Mix(o.OverspeedMach, 0.01);
            Mix(o.PullUpAlert ? 1 : 0, 1);
            Mix(o.AoaWarnAlert ? 1 : 0, 1);
            Mix(o.BingoFuelAlert ? 1 : 0, 1);
            Mix(o.SinkRateAlert ? 1 : 0, 1);
            Mix(o.AltTapeM, 0.5);
            Mix(o.AltTapeIsRadar ? 1 : 0, 1);
            Mix(o.ConfigDebug ? 1 : 0, 1);
            bool blinking = o.PullUpAlert || o.AoaWarnAlert || o.BingoFuelAlert || o.SinkRateAlert
                            || o.EdgeWarning || o.OverspeedAlert || o.StallWarnLevel > 0
                            || o.EnemyAirfieldAlert || o.GearTransitActive || o.FlapsTransitActive
                            || (o.AirbrakeTransitActive && !o.DeceleronAirbrake);
            Mix(blinking ? (Environment.TickCount64 / 32) % 20 : 0, 1);
            foreach (string dbgLine in o.ConfigDebugLines) MixStr(dbgLine);
            Mix(o.GearTransitActive ? 1 : 0, 1);
            Mix(o.GearConfirmActive ? 1 : 0, 1);
            Mix(o.GearDownSteady ? 1 : 0, 1);
            Mix(o.FlapsTransitActive ? 1 : 0, 1);
            Mix(o.FlapsDeployed ? 1 : 0, 1);
            Mix(o.AirbrakeTransitActive ? 1 : 0, 1);
            Mix(o.AirbrakeDeployed ? 1 : 0, 1);
            Mix(o.DeceleronAirbrake ? 1 : 0, 1);
            MixStr(o.GearTransitText);
            MixStr(o.FlapsTransitText);
            MixStr(o.FlapsSteadyText);
            MixStr(o.AirbrakeTransitText);
            MixStr(o.AirbrakeSteadyText);
            return k;
        }
    }

    private static string B(bool v) => v ? "T" : "F";

    private static double Lerp(double a, double b, double f) => a + (b - a) * f;

    private static TelemetrySnapshot Extrapolate(TelemetrySnapshot? prev, TelemetrySnapshot curr, double f)
    {
        if (prev == null) return curr;
        return new TelemetrySnapshot
        {
            TimestampUtc = curr.TimestampUtc,
            ElapsedSeconds = curr.ElapsedSeconds,
            Valid = curr.Valid,
            InFlight = curr.InFlight,
            VehicleType = curr.VehicleType,
            IasKmh = Lerp(prev.IasKmh, curr.IasKmh, Math.Min(f, 2.0)),
            TasKmh = Lerp(prev.TasKmh, curr.TasKmh, f),
            Mach = Lerp(prev.Mach, curr.Mach, f),
            AltM = Lerp(prev.AltM, curr.AltM, Math.Min(f, 2.0)),
            VyMs = Lerp(prev.VyMs, curr.VyMs, f),
            AoaDeg = Lerp(prev.AoaDeg, curr.AoaDeg, f),
            AosDeg = Lerp(prev.AosDeg, curr.AosDeg, f),
            NyG = Lerp(prev.NyG, curr.NyG, f),
            RollRateDegS = Lerp(prev.RollRateDegS, curr.RollRateDegS, f),
            HeadingDeg = StepHeading(prev.HeadingDeg, curr.HeadingDeg, f),
            FuelKg = curr.FuelKg,
            Fuel0Kg = curr.Fuel0Kg,
            PitchDeg = curr.PitchDeg,
            BankDeg = curr.BankDeg,
            TurnRateDegS = curr.TurnRateDegS,
            Ammo = curr.Ammo,
            Caps = curr.Caps,

            // Discrete / configuration channels are not interpolated, but they must still be
            // carried over: omitting them made gear/flaps/airbrake read as 0 whenever
            // extrapolation was on, so their boxes never appeared.
            GearPercent = curr.GearPercent,
            FlapsPercent = curr.FlapsPercent,
            AirbrakePercent = curr.AirbrakePercent,
            RadioAltM = curr.RadioAltM,
            VneMs = curr.VneMs,
            AoaIndexer = curr.AoaIndexer,
            HasHeading = curr.HasHeading,
        };
    }

    private static double StepHeading(double a, double b, double f)
    {
        double d = b - a;
        if (d > 180) d -= 360;
        else if (d < -180) d += 360;
        double v = a + d * f;
        while (v < 0) v += 360;
        while (v >= 360) v -= 360;
        return v;
    }

    private static AttitudeSolution ExtrapolateAttitude(AttitudeSolution prev, AttitudeSolution curr, double f) => new()
    {
        PitchDeg = Lerp(prev.PitchDeg, curr.PitchDeg, Math.Min(f, 1.0)),
        BankDeg = Lerp(prev.BankDeg, curr.BankDeg, Math.Min(f, 1.0)),
        Source = curr.Source,
        Confidence = curr.Confidence,
    };

    private static volatile bool settingsOpen;
    private static WtHud.UI.Mfd.MfdFrameForm? _mfdPanel;
    private static long lastExitPress;
    /// <summary>F1 保存后由 overlay 线程重建热键（Unregister/RegisterHotKey 必须在注册它的线程上调用）。</summary>
    private static volatile bool pendingHotkeyRefresh;

    /// <summary>已经检查过权限的游戏 pid（游戏重启后 pid 变化会重新检查）。</summary>
    private static int elevationCheckedForPid;
    private static long lastGameSearchTicks;

    /// <summary>热键注册失败时的重试（自动提权重启的瞬间，旧进程可能仍占着热键）。</summary>
    private static long hotkeyRetryAtTicks;
    private static int hotkeyRetryCount;

    private static bool lastCursorShowing = true;
    private static WtHud.Input.KeyBinding _zoomKeyBinding = WtHud.Input.KeyBinding.Parse("Z", 0x5A, "Z");
    private static WtHud.Input.KeyBinding _viewKeyBinding = WtHud.Input.KeyBinding.Parse("V", 0x56, "V");
    private static WtHud.Input.KeyBinding _mapKeyBinding = WtHud.Input.KeyBinding.Parse("M", 0x4D, "M");
    private static WtHud.Input.KeyBinding _scoreKeyBinding = WtHud.Input.KeyBinding.Parse("TAB", 0x09, "TAB");
    private static WtHud.Input.KeyBinding _holdHideKeyBinding = WtHud.Input.KeyBinding.Parse("U", 0x55, "U");
    private static WtHud.Input.KeyBinding _resetKeyBinding = WtHud.Input.KeyBinding.Parse("F5", 0x74, "F5");
    private static WtHud.Input.KeyBinding _stallKeyBinding = WtHud.Input.KeyBinding.Parse("CTRL+ALT+S", 0x53, "CTRL+ALT+S");
    private static WtHud.Input.KeyBinding _airbrakeKeyBinding = WtHud.Input.KeyBinding.Parse("H", 0x48, "H");
    private static bool airbrakeKeyOn;

    /// <summary>Re-parse every key binding from the config (called on startup and after F1 save).</summary>
    private static void RebindeKeys(HudConfig c)
    {
        _zoomKeyBinding = WtHud.Input.KeyBinding.Parse(c.ZoomKey, 0x5A, "Z");
        _viewKeyBinding = WtHud.Input.KeyBinding.Parse(c.ViewKey, 0x56, "V");
        _mapKeyBinding = WtHud.Input.KeyBinding.Parse(c.MapKey, 0x4D, "M");
        _scoreKeyBinding = WtHud.Input.KeyBinding.Parse(c.ScoreboardKey, 0x09, "TAB");
        _holdHideKeyBinding = WtHud.Input.KeyBinding.Parse(c.HoldHideKey, 0x55, "U");
        _resetKeyBinding = WtHud.Input.KeyBinding.Parse(c.ResetKey, 0x74, "F5");
        _stallKeyBinding = WtHud.Input.KeyBinding.Parse(c.StallKey, 0x53, "CTRL+ALT+S");
        _airbrakeKeyBinding = WtHud.Input.KeyBinding.Parse(c.AirbrakeKey, 0x48, "H");
        Console.WriteLine($"keys: view={_viewKeyBinding.Text} zoom={_zoomKeyBinding.Text} map={_mapKeyBinding.Text} " +
                          $"score={_scoreKeyBinding.Text} hold={_holdHideKeyBinding.Text}");
    }

    private static bool prevScore;
    private static long lastScoreToggle;
    private static bool scoreboardLatched;
    private static bool radioAltLogged;
    private static string gridDebug = "";
    private static double fuelLogAt;
    private static bool overspeedLogged;
    private static int pendingPitchSign;        // fed to AttitudeFusion before each update
    private static int detectedPitchSign = 1;
    private static bool pitchSignDecided;
    private static string pitchSignVehicle = "";
    private static long stallSince;
    private static long aoaIdxSince;      // 座舱 AOA 指示器持续超限计时（防抖）
    private static int stallLogged = -1;
    private static string stallCalWarned = "";
    private static double fuelLastSample = -1;
    private static double fuelFlowAt;
    private static OverlayWindow? overlayRef;
    private static string vneLogged = "";
    private static long pullUpSince;
    private static long sinkRateSince;
    private static DateTime navDbgUtc = DateTime.MinValue;
    private static int lastAircraftMarkers = -1;
    private static long lastFramePollTicks;
    private static long lastPitchProbeTicks;
    private static long perfLastPrintTicks;
    private static double perfBuildMs, perfDrawMs, perfPresentMs;
    private static int perfPresents, perfLoops;
    private static long cursorProbeTicks;

    private static bool zoomLatched;          // Z key toggle state (RMB adds momentary aiming)
    private static long holdKeyStart;         // long-press hide timer
    private static bool holdKeyHidden;
    private const long HoldHideDelayMs = 220;
    private static long lastCursorNudge;
    private static int cursorNudges;

    private static string lastVehicle = "";
    private static bool prevValid;
    private static int viewState;   // 0 third person, 1 cockpit, 2 nose
    private static volatile bool mapHeld;

    // SPLASH：自己击落敌机时的播报（4 Hz 轮询 /hudmsg，只认「击落了」）
    private static readonly WtHud.Telemetry.HudMsgWatcher hudMsgWatcher = new();
    private static readonly System.Collections.Generic.Queue<string> splashQueue = new();
    private static DateTime splashStartUtc = DateTime.MinValue;
    private static DateTime splashUntilUtc = DateTime.MinValue;
    private static string splashTarget = "";
    private static bool splashInMatch;
    private static double lastGear = -1;
    private static bool gearWasTransit;
    private static long gearConfirmUntil;
    private static readonly System.Collections.Generic.Queue<(double T, double V)> speedWindow = new();
    private static double rollFull;
    private static double rollLastTime;

    private static double accelMs2;
    private static double accelLastTas;
    private static double accelLastTime;
    private static readonly System.Collections.Generic.Queue<(double T, double Fuel)> fuelWindow = new();


    private static double fuelFlow;

    // 编队引导用的距离历史。map_obj 只有 ~4 Hz，逐帧差分会把 0 和几百 m/s 混在一起跳，
    // 所以在 2 秒窗口上取首尾斜率：代价是动作提示有约 1 秒延迟，编队跟飞够用。
    private const double ClosingWindowS = 2.0;
    private static readonly System.Collections.Generic.List<(double T, double R)> closingHistory = new();

    // 编队引导的目标间距：最终要稳定在友机后方 400 m。
    private const double FormationTargetRangeM = 400;

    // 油门建议的平滑状态。closing 本身有噪声，不滤波的话数字会在几百 km/h 之间跳。
    private static double guideDeltaSmooth;
    private static double guideDeltaLastT;
    private static bool guideDeltaValid;
    private static FormationAction guideActionPrev;

    // 级联 PID 的积分项。只有外环没顶到限幅、油门也没饱和时才累积（抗积分饱和）。
    private static double guideIntegral;
    private static double guideIntegralLastT;
    private const double GuideIntegralLimit = 400.0;   // m·s，够大但不至于失控
    private static double lastFlaps = -1;
    private static long lastFlapsChangeTicks;
    private static double lastAirbrake = -1;
    private static long lastAirbrakeChangeTicks;
    private static volatile bool scoreboardHeld;

    private static string ViewName(int state) => state switch { 0 => "third person", 1 => "cockpit", 2 => "nose", _ => "?" };
    private static TelemetrySnapshot? prevSnapshot;
    private static AttitudeSolution prevAttitude;

    private static TelemetryClient? telemetry;

    private static AttitudeFusion? fusion;
    private static TelemetrySnapshot? latest;
    private static AttitudeSolution lastAttitude;

    private static TelemetrySnapshot WithNy(TelemetrySnapshot s, double ny) => new()
    {
        TimestampUtc = s.TimestampUtc, ElapsedSeconds = s.ElapsedSeconds, Valid = s.Valid, InFlight = s.InFlight,
        VehicleType = s.VehicleType, IasKmh = s.IasKmh, TasKmh = s.TasKmh, Mach = s.Mach, AltM = s.AltM,
        VyMs = s.VyMs, AoaDeg = s.AoaDeg, AosDeg = s.AosDeg, NyG = ny, RollRateDegS = s.RollRateDegS,
        HeadingDeg = s.HeadingDeg, FuelKg = s.FuelKg, Fuel0Kg = s.Fuel0Kg, PitchDeg = s.PitchDeg,
        BankDeg = s.BankDeg, TurnRateDegS = s.TurnRateDegS, Ammo = s.Ammo, Caps = s.Caps,
    };

    private static TelemetrySnapshot WithFlaps(TelemetrySnapshot s, double pct) => new()
    {
        TimestampUtc = s.TimestampUtc, ElapsedSeconds = s.ElapsedSeconds, Valid = s.Valid, InFlight = s.InFlight,
        VehicleType = s.VehicleType, IasKmh = s.IasKmh, TasKmh = s.TasKmh, Mach = s.Mach, AltM = s.AltM,
        VyMs = s.VyMs, AoaDeg = s.AoaDeg, AosDeg = s.AosDeg, NyG = s.NyG, RollRateDegS = s.RollRateDegS,
        HeadingDeg = s.HeadingDeg, FuelKg = s.FuelKg, Fuel0Kg = s.Fuel0Kg, PitchDeg = s.PitchDeg,
        BankDeg = s.BankDeg, TurnRateDegS = s.TurnRateDegS, Ammo = s.Ammo, Caps = s.Caps,
        GearPercent = s.GearPercent, FlapsPercent = pct,
    };

    private static TelemetrySnapshot DemoSnapshot() => new()
    {
        Valid = true, InFlight = true, VehicleType = "demo",
        IasKmh = 912, TasKmh = 1044, Mach = 0.87, AltM = 3520, VyMs = 12.4,
        AoaDeg = 5.2, AosDeg = 1.1, NyG = 2.4, HeadingDeg = 275, FuelKg = 4100, Fuel0Kg = 9400,
    };

    private static int Preview(string[] args)
    {
        var pcfg = HudConfig.Load();
        WtHud.UI.L10n.LoadFromConfig(pcfg.Language);
        var envAlerts = Environment.GetEnvironmentVariable("WT_MFD_ALERTS");
        if (!string.IsNullOrEmpty(envAlerts))
            WtHud.UI.L10n.Alerts = envAlerts == "zh" ? WtHud.UI.Lang.Zh : WtHud.UI.Lang.En;
        string outPath = args.Length > 1 ? args[1] : "hud-preview.png";
        int w = args.Length > 2 ? int.Parse(args[2], CultureInfo.InvariantCulture) : 1920;
        int h = args.Length > 3 ? int.Parse(args[3], CultureInfo.InvariantCulture) : 1080;
        string variant = args.Length > 4 ? args[4].ToLowerInvariant() : "est";

        var snap = new TelemetrySnapshot
        {
            Valid = true,
            InFlight = true,
            VehicleType = "j_16",
            IasKmh = 912,
            TasKmh = 1044,
            Mach = 0.87,
            AltM = 3520,
            VyMs = 12.4,
            AoaDeg = 5.2,
            AosDeg = 1.1,
            NyG = 2.4,
            RollRateDegS = 0,
            HeadingDeg = 275,
            FuelKg = 4100,
            Fuel0Kg = 9400,
            Caps = new Capabilities { IndicatorKeyCount = 29 },
        };

        AttitudeSolution att = variant switch
        {
            "game" => new AttitudeSolution { PitchDeg = 8.0, BankDeg = 24.0, Source = AttitudeSource.Game, Confidence = 1 },
            "none" => new AttitudeSolution { Source = AttitudeSource.Unavailable },
            "level" => new AttitudeSolution { PitchDeg = 0.0, BankDeg = 0.0, Source = AttitudeSource.Estimated, Confidence = 1 },
            "pitch5" => new AttitudeSolution { PitchDeg = 5.0, BankDeg = 0.0, Source = AttitudeSource.Estimated, Confidence = 1 },
            "bank30" => new AttitudeSolution { PitchDeg = 0.0, BankDeg = 30.0, Source = AttitudeSource.Estimated, Confidence = 1 },
            _ => new AttitudeSolution { PitchDeg = 8.0, BankDeg = 24.0, Source = AttitudeSource.Estimated, Confidence = 1 },
        };
        // 预览姿态覆盖："pitch,bank"，用于检查特定俯仰角下的布局碰撞
        var envAtt = Environment.GetEnvironmentVariable("WT_PREVIEW_ATT");
        if (!string.IsNullOrEmpty(envAtt))
        {
            var parts = envAtt.Split(',');
            att = new AttitudeSolution
            {
                PitchDeg = double.Parse(parts[0], CultureInfo.InvariantCulture),
                BankDeg = parts.Length > 1 ? double.Parse(parts[1], CultureInfo.InvariantCulture) : 0,
                Source = AttitudeSource.Estimated,
                Confidence = 1,
            };
        }

        var opt = new HudOptions();
        if (variant == "zoom") { opt.ZoomLayout = true; opt.ZoomBlend = 1.0; }

        // 预览用的假友机：WT_PREVIEW_FRIENDS="30,1200;160,3400"（方位°,距离m）
        if (Environment.GetEnvironmentVariable("WT_PREVIEW_FRIENDS") is { Length: > 0 } friendSpec)
        {
            opt.FormationAssist = true;
            foreach (var part in friendSpec.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = part.Split(',');
                if (kv.Length == 2
                    && double.TryParse(kv[0], NumberStyles.Any, CultureInfo.InvariantCulture, out double fb)
                    && double.TryParse(kv[1], NumberStyles.Any, CultureInfo.InvariantCulture, out double fr))
                    opt.Friendlies.Add(new FriendlyContact(fb, fr, "Fighter"));
            }
        }
        if (variant == "formation")
        {
            opt.FormationOnly = true;
            opt.FormationAssist = true;
            opt.ZoomLayout = false;
            opt.ZoomBlend = 0;
            if (opt.Friendlies.Count == 0)
            {
                opt.Friendlies.Add(new FriendlyContact(12, 420, "Fighter"));
                opt.Friendlies.Add(new FriendlyContact(-38, 1800, "Fighter"));
                opt.Friendlies.Add(new FriendlyContact(150, 5200, "Bomber"));
            }
            // 引导样张：WT_PREVIEW_GUIDE="转角,距离,目标距离,动作(0=无 1=加速 2=减速 3=保持),油门km/h"
            var guideSpec = Environment.GetEnvironmentVariable("WT_PREVIEW_GUIDE");
            if (!string.IsNullOrWhiteSpace(guideSpec))
            {
                var g = guideSpec.Split(',');
                if (g.Length >= 3
                    && double.TryParse(g[0], NumberStyles.Any, CultureInfo.InvariantCulture, out double gTurn)
                    && double.TryParse(g[1], NumberStyles.Any, CultureInfo.InvariantCulture, out double gRange)
                    && double.TryParse(g[2], NumberStyles.Any, CultureInfo.InvariantCulture, out double gTarget))
                {
                    var gAct = g.Length >= 4 && int.TryParse(g[3], out int ai)
                        ? (FormationAction)ai : FormationAction.None;
                    double gDelta = g.Length >= 5
                        && double.TryParse(g[4], NumberStyles.Any, CultureInfo.InvariantCulture, out double gd) ? gd : 0;
                    opt.Guide = new FormationLead(gTurn, gRange, gTarget, gAct, gDelta);
                }
            }
            else
            {
                opt.Guide = new FormationLead(12, 820, FormationTargetRangeM, FormationAction.Decelerate, -90);
            }
        }
        if (variant == "flaps") { opt.FlapsDeployed = true; opt.FlapsSteadyText = "50"; }
        opt.AltTapeM = snap.AltM;   // previews default to barometric altitude on the tape
        opt.GridText = "F6";
        opt.NavFriendly = "AF  " + (char)0x2193 + " 4.2km";
        opt.NavEnemy = "AF! " + (char)0x2197 + " 9.8km";
        if (variant == "edge") { opt.EdgeWarning = true; opt.EdgeArrow = "↑"; opt.ForceTransitVisible = true; }
        if (variant == "pullup") { opt.PullUpAlert = true; opt.ForceTransitVisible = true; }
        if (variant == "pullupall") { opt.PullUpAlert = true; opt.SinkRateAlert = true; opt.OnlyAlerts = true; opt.ForceTransitVisible = true; }
        if (variant == "sink") { opt.SinkRateAlert = true; opt.ForceTransitVisible = true; }
        if (variant == "splash")
        {
            opt.SplashMain = WtHud.UI.L10n.Get(WtHud.UI.LangScope.Alerts, "splash");
            opt.SplashSub = "=FdX= Ghost (F-4S)";
            opt.SplashAlpha = 1.0;
        }
        if (variant == "aoa") { opt.AoaWarnAlert = true; opt.ForceTransitVisible = true; }
        if (variant == "speed") { opt.OverspeedAlert = true; opt.OnlyAlerts = true; opt.ForceTransitVisible = true; }
        if (variant == "stall") { opt.StallWarnLevel = 2; opt.ForceTransitVisible = true; }
        if (variant == "stallaoa") { opt.StallWarnLevel = 2; opt.AoaWarnAlert = true; opt.OnlyAlerts = true; opt.ForceTransitVisible = true; }
        if (variant == "bingo") { opt.BingoFuelAlert = true; opt.ForceTransitVisible = true; }
        if (variant == "ra") { opt.AltTapeIsRadar = true; opt.AltTapeM = 35; }
        if (variant == "all")
        {
            // Everything at once, for the README screenshot.
            opt.ForceTransitVisible = true;
            opt.GridText = "F6";
            opt.NavFriendly = "AF " + (char)0x2196 + " 4.2km";
            opt.NavEnemy = "AF! " + (char)0x2197 + " 9.8km";
            opt.EdgeWarning = true;
            opt.EdgeArrow = (char)0x2190 + "";
            opt.PullUpAlert = true;
            opt.AoaWarnAlert = true;
            opt.BingoFuelAlert = true;
            opt.GearConfirmActive = true;
            opt.GearConfirmText = "STB";
            opt.FlapsDeployed = true;
            opt.FlapsSteadyText = "50";
            opt.AirbrakeDeployed = true;
            opt.AltTapeIsRadar = true;
            opt.AltTapeM = 128;
            opt.EnduranceSeconds = 368;    // 6:08 for the showcase
            opt.AccelMs2 = 3.2;
        }
        if (variant == "ra10") { opt.AltTapeIsRadar = true; opt.AltTapeM = 8; }
        if (variant == "gear" || variant == "flaps" || variant == "gearzoom") opt.ForceTransitVisible = true;
        if (variant == "gear" || variant == "gearzoom")
        {
            opt.GearTransitActive = true;
            opt.GearTransitText = WtHud.UI.L10n.Get(WtHud.UI.LangScope.Alerts, "gear.down");
            if (variant == "gearzoom") { opt.ZoomLayout = true; opt.ZoomBlend = 1.0; }
        }
        if (variant == "cfg")
        {
            opt.GearTransitActive = true; opt.GearTransitText = WtHud.UI.L10n.Get(WtHud.UI.LangScope.Alerts, "gear.down");
            opt.GearDownSteady = true;
            opt.FlapsDeployed = true; opt.FlapsSteadyText = "50";
            opt.AirbrakeDeployed = true; opt.AirbrakeSteadyText = WtHud.UI.L10n.Get(WtHud.UI.LangScope.Alerts, "brake.steady");
            opt.OnlyConfigBoxes = true;
            opt.ForceTransitVisible = true;
        }
        if (variant == "hg") snap = WithNy(snap, 13.0);
        if (variant == "mg") snap = WithNy(snap, 9.5);
        if (variant == "nw") snap = WithNy(snap, -2.5);
        if (variant == "nd") snap = WithNy(snap, -4.5);
        var geo = new HudGeometry(opt, new HudCalibration { VerticalFovDeg = 45 });
        var prims = geo.Build(snap, att, w, h);

        using var canvas = new HudCanvas(w, h);
        canvas.RenderFrame(18, 22, 26, 255, prims);
        var rgba = canvas.ReadBackRgba();
        PngWriter.Write(outPath, w, h, rgba);
        Console.WriteLine($"wrote {outPath}  {w}x{h}  prims={prims.Count}  attitude={att}");
        return 0;
    }

    private static void Check(string name, bool ok)
    {
        Console.WriteLine((ok ? "  PASS  " : "  FAIL  ") + name);
        if (!ok) Environment.ExitCode = 1;
    }

    private static int MapProbe()
    {
        using var map = new WtHud.Telemetry.MapData();
        map.Start();
        for (int i = 0; i < 12; i++)
        {
            Thread.Sleep(500);
            Console.WriteLine($"[{i * 0.5:F1}s] map_obj bytes={map.RawBytes,5}  airfields={map.AirfieldCount,3}  self={(map.Self is { } s ? $"{s.X:F3},{s.Y:F3}" : "null"),-18} cog={(map.CourseDeg is { } c ? c.ToString("F0") : "null")}  mapSize={map.MapSizeM.X:F0}x{map.MapSizeM.Y:F0}  err={map.LastError}");
        }
        return 0;
    }
    /// <summary>Renders the MFD panel to a PNG (headless verification of the bezel + text).</summary>
    private static int MfdPreview(string outPath, string view = "", int item = 0, string extra = "")
    {
        var cfg = HudConfig.Load();
        WtHud.UI.L10n.LoadFromConfig(cfg.Language);
        var envPanel = Environment.GetEnvironmentVariable("WT_MFD_PANEL");
        if (!string.IsNullOrEmpty(envPanel)) WtHud.UI.L10n.Panel = envPanel == "zh" ? WtHud.UI.Lang.Zh : WtHud.UI.Lang.En;
        var envScreen = Environment.GetEnvironmentVariable("WT_MFD_SCREEN");
        if (!string.IsNullOrEmpty(envScreen)) WtHud.UI.L10n.Screen = envScreen == "zh" ? WtHud.UI.Lang.Zh : WtHud.UI.Lang.En;
        var thread = new Thread(() =>
        {
            using var form = new WtHud.UI.Mfd.MfdFrameForm(cfg);
            // 语言覆盖必须在窗体构造（会重读配置）之后再应用
            var envPanel2 = Environment.GetEnvironmentVariable("WT_MFD_PANEL");
            if (!string.IsNullOrEmpty(envPanel2)) WtHud.UI.L10n.Panel = envPanel2 == "zh" ? WtHud.UI.Lang.Zh : WtHud.UI.Lang.En;
            var envScreen2 = Environment.GetEnvironmentVariable("WT_MFD_SCREEN");
            if (!string.IsNullOrEmpty(envScreen2)) WtHud.UI.L10n.Screen = envScreen2 == "zh" ? WtHud.UI.Lang.Zh : WtHud.UI.Lang.En;
            Application.DoEvents();
            Thread.Sleep(200);
            if (!string.IsNullOrEmpty(view) && int.TryParse(view, out var pageIdx))
            {
                if (item >= 0) form.PreviewOpen(pageIdx, Math.Max(0, item));
            }
            else if (view == "help") { form.PreviewHelp(0); }
            else if (view == "preset") { form.PreviewOpen(4, 0); form.PreviewPreset(); }
            else if (view == "edit") { form.PreviewEdit(0, 7); }   // FOV 行（数值行）
            else if (view == "alerts") { form.PreviewOpen(2, 0); }
            else if (view == "display") { form.PreviewOpen(0, 0); }
            else if (view == "display2") { form.PreviewOpen(0, 0); form.PreviewItemPage(1); }
            else if (view == "diag") { form.PreviewOpen(7, 0); }
            else if (view == "keys") { form.PreviewOpen(3, 0); }
            else if (view == "keys2") { form.PreviewOpen(3, 0); form.PreviewItemPage(1); }
            else if (view == "editscoped") { form.PreviewEdit(2, 0); }   // 告警页首行（按机型）
            else if (view == "capture") { form.PreviewCapture(3, 0); }
            else if (view == "discard")
            {
                form.PreviewDiscard();
            }
            else if (view == "confirm")
            {
                form.PreviewConfirm(5, 9);   // DIAG 页的“检查更新”
            }
            else if (view == "snapshots") { form.PreviewSnapshots(); }
            else if (view == "snapconfirm") { form.PreviewSnapshotConfirm(); }
            else if (view == "snaprestore") { form.PreviewRestoreNewest(); }
            else if (view == "snapback")
            {
                form.PreviewSnapshotConfirm();
                form.PreviewSideKey(3);          // 确认页上的「取消」侧键
            }
            else if (view == "snapclick")
            {
                form.PreviewSnapshotConfirm();
                form.PreviewConfirmRowClick(1);  // 单击「取消」行：只选中，不执行
            }
            else if (view == "snapclickok")
            {
                form.PreviewSnapshotConfirm();
                form.PreviewConfirmRowClick(0, 2);   // 双击「确认」行：执行恢复
            }
            else if (view == "detail")
            {
                if (!string.IsNullOrEmpty(extra)) form.PreviewDetailVehicle(extra);
                else form.PreviewDetail();
            }
            else if (view == "detailback") { form.PreviewDetailBack(); }
            else if (view == "help3") { form.PreviewHelpLast(); }
            else if (view == "helpback") { form.PreviewHelpToggle(); }
            else if (view == "pickvehicle") { form.PreviewPickVehicle(); }
            else if (view == "editcommit") { form.PreviewEditCommit(); }
            else if (view == "pickpicked") { form.PreviewPickPicked(); }
            else if (view == "pickback") { form.PreviewPickBack(); }
            else if (view == "presetapply") { form.PreviewPresetApply(); }
            else if (view == "pitch") { form.PreviewPitch(); }
            else if (view == "detailreset") { form.PreviewDetailReset(); }
            using var bmp = new System.Drawing.Bitmap(form.Width, form.Height);
            using (var g = System.Drawing.Graphics.FromImage(bmp))
            {
                form.RenderTo(g);
            }
            var path = Path.IsPathRooted(outPath) ? outPath : Path.Combine(Path.GetTempPath(), outPath);
            bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            Console.WriteLine("mfd preview -> " + path);
            form.Close();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        return 0;
    }
    /// <summary>Opens the MFD panel standalone (no game, scratch config) for interaction testing.</summary>
    private static int MfdInteractive()
    {
        var cfg = HudConfig.Load();
        var scratch = Path.Combine(Path.GetTempPath(), "wthud-mfd-scratch.json");
        Console.WriteLine("MFD panel (scratch mode). SAVE writes to: " + scratch);
        Console.WriteLine("  F1 no longer needed - this window IS the panel. ESC/BACK at menu closes.");
        var thread = new Thread(() =>
        {
            Application.EnableVisualStyles();
            using var form = new WtHud.UI.Mfd.MfdFrameForm(cfg, scratch);
            Application.Run(form);
            Console.WriteLine(form.Saved ? "saved to scratch file" : "closed without saving");
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        return 0;
    }}





