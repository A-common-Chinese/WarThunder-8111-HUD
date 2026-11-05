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
            case "replay":
                if (args.Length < 2) { Console.WriteLine("usage: WtHud replay <file.jsonl> [speed]"); return 2; }
                ReplaySource.Play(args[1], args.Length > 2 ? double.Parse(args[2], CultureInfo.InvariantCulture) : 1.0);
                return 0;
            case "selftest":
                return SelfTest();
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

        Console.WriteLine("selftest done");
        return Environment.ExitCode;
    }

    private static int DumpGeometry(string[] args)
    {
        int w = args.Length > 1 ? int.Parse(args[1], CultureInfo.InvariantCulture) : 1920;
        int h = args.Length > 2 ? int.Parse(args[2], CultureInfo.InvariantCulture) : 1080;
        var snap = new TelemetrySnapshot
        {
            Valid = true, InFlight = true, VehicleType = "j_16",
            IasKmh = 912, TasKmh = 1044, Mach = 0.87, AltM = 3520, VyMs = 12.4,
            AoaDeg = 5.2, AosDeg = 1.1, NyG = 2.4, HeadingDeg = 275, FuelKg = 4100, Fuel0Kg = 9400,
        };
        var att = new AttitudeSolution { PitchDeg = 8, BankDeg = 24, Source = AttitudeSource.Estimated, Confidence = 1 };
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

        FusionNativeBias();
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
                HeadingDeg = heading(t), TasKmh = tas(t), IasKmh = tas(t) * 0.9,
                VyMs = vy(t), AoaDeg = aoa(t), NyG = ny(t), AltM = 2000,
            };
            att = fusion.Update(snap);
        }
        bool pass = Math.Abs(att.BankDeg - bankExpected) < 6 && Math.Abs(att.PitchDeg - pitchExpected) < 6;
        Console.WriteLine($"  {(pass ? "PASS" : "FAIL")}  {name,-28} got bank={att.BankDeg,6:F1} (want {bankExpected,5:F1})  " +
                          $"pitch={att.PitchDeg,6:F1} (want {pitchExpected,5:F1})  src={att.Source}");
        if (!pass) Environment.ExitCode = 1;
    }

    private static void FusionNativeBias()
    {
        // Native horizon carries a constant -4.3 deg offset; the fusion must learn it out.
        var fusion = new AttitudeFusion();
        AttitudeSolution att = default;
        for (int i = 0; i < 300; i++)
        {
            double t = i * 0.04;
            var snap = new TelemetrySnapshot
            {
                Valid = true, InFlight = true, ElapsedSeconds = t,
                HeadingDeg = 90, TasKmh = 900, IasKmh = 810, VyMs = 10, AoaDeg = 3.0, NyG = 1.2, AltM = 2000,
                PitchDeg = (Math.Asin(10 / (900 / 3.6)) * 180 / Math.PI + 3.0) - 4.3,
                BankDeg = 12.0,
            };
            att = fusion.Update(snap);
        }
        double expectedPitch = Math.Asin(10 / (900 / 3.6)) * 180 / Math.PI + 3.0;
        bool pass = Math.Abs(att.PitchDeg - expectedPitch) < 1.0 && Math.Abs(att.BankDeg - 12.0) < 1.0 &&
                    att.Source == AttitudeSource.Game;
        Console.WriteLine($"  {(pass ? "PASS" : "FAIL")}  native bias learning          got pitch={att.PitchDeg:F1} " +
                          $"(want {expectedPitch:F1})  bank={att.BankDeg:F1} (want 12.0)  src={att.Source}");
        if (!pass) Environment.ExitCode = 1;
    }

    /// <summary>Opens the settings dialog on a dedicated STA thread (the overlay has its own pump).</summary>
    private static void OpenSettings(HudConfig cfg, HudCalibration cal, HudOptions opt, bool currentShow, Action<bool> setShow)
    {
        var thread = new Thread(() =>
        {
            try
            {
                settingsOpen = true;
                NativeMethods.SetCursor(NativeMethods.LoadCursor(IntPtr.Zero, (IntPtr)32512));
                using var form = new WtHud.UI.SettingsForm(cfg, currentShow, opt.BodyFixedOnly);
                if (form.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    cfg.Extrapolate = form.Extrapolate;
                    cfg.HudVisible = form.HudVisible;
                    cfg.RollCoupling = form.RollCoupling;
                    cfg.BankSign = form.BankSign;
                    cfg.HideInCockpit = form.HideCockpit;
                    cfg.HideInNose = form.HideNose;
                    if (!string.IsNullOrWhiteSpace(form.ViewKey)) cfg.ViewKey = form.ViewKey.Trim().ToUpperInvariant();
                    if (!string.IsNullOrWhiteSpace(form.ZoomKey)) cfg.ZoomKey = form.ZoomKey.Trim().ToUpperInvariant();
                    if (!string.IsNullOrWhiteSpace(form.MapKey)) cfg.MapKey = form.MapKey.Trim().ToUpperInvariant();
                    if (!string.IsNullOrWhiteSpace(form.ScoreboardKey)) cfg.ScoreboardKey = form.ScoreboardKey.Trim().ToUpperInvariant();
                    if (!string.IsNullOrWhiteSpace(form.HoldHideKey)) cfg.HoldHideKey = form.HoldHideKey.Trim().ToUpperInvariant();
                    if (!string.IsNullOrWhiteSpace(form.SettingsKey)) cfg.SettingsKey = form.SettingsKey.Trim().ToUpperInvariant();
                    if (!string.IsNullOrWhiteSpace(form.PitchZeroKey)) cfg.PitchZeroKey = form.PitchZeroKey.Trim().ToUpperInvariant();
                    if (!string.IsNullOrWhiteSpace(form.BankFlipKey)) cfg.BankFlipKey = form.BankFlipKey.Trim().ToUpperInvariant();
                    if (!string.IsNullOrWhiteSpace(form.BodyFixedKey)) cfg.BodyFixedKey = form.BodyFixedKey.Trim().ToUpperInvariant();
                    if (!string.IsNullOrWhiteSpace(form.ShowKey)) cfg.ShowKey = form.ShowKey.Trim().ToUpperInvariant();
                    if (!string.IsNullOrWhiteSpace(form.ExitKey)) cfg.ExitKey = form.ExitKey.Trim().ToUpperInvariant();
                    if (!string.IsNullOrWhiteSpace(form.SaveKey)) cfg.SaveKey = form.SaveKey.Trim().ToUpperInvariant();
                    if (!string.IsNullOrWhiteSpace(form.CursorHoleKey)) cfg.CursorHoleKey = form.CursorHoleKey.Trim().ToUpperInvariant();
                    if (!string.IsNullOrWhiteSpace(form.ResetKey)) cfg.ResetKey = form.ResetKey.Trim().ToUpperInvariant();
                    cfg.AimOnRightMouse = form.AimOnRightMouse;
                    cfg.HideOnGround = form.HideOnGround;
                    cfg.GroundHideDelayMs = form.GroundHideDelayMs;
                    cfg.TapeRadarAltitude = form.TapeRadarAltitude;
                    cfg.EnemyAirfieldAlertKm = form.EnemyAirfieldAlertKm;
                    cfg.AoaWarnDeg = form.AoaWarnDeg;
                    cfg.BingoFuelSeconds = form.BingoFuelMinutes * 60.0;
                    cfg.ShowPitchLadder = form.ShowPitchLadder;
                    cfg.ShowFpm = form.ShowFpm;
                    RebindeKeys(cfg);
                    overlayRef?.ApplyHotkeys(cfg);
                    cfg.VerticalFovDeg = form.Fov;
                    cfg.LayoutOffsetYDeg = form.OffsetY;
                    cfg.PitchZeroDeg = form.PitchZero;
                    cfg.OverspeedKmh = form.OverspeedKmh;
                    cfg.OverspeedMach = form.OverspeedMach;
                    cfg.OverspeedByMach = form.OverspeedByMach;
                    cfg.EnduranceScale = form.EnduranceScale;
                    cfg.CursorHoleHalfPx = form.CursorHoleHalfPx;

                    cal.VerticalFovDeg = cfg.VerticalFovDeg;
                    cal.LayoutOffsetYDeg = cfg.LayoutOffsetYDeg;
                    cal.RollCoupling = cfg.RollCoupling;
                    opt.BodyFixedOnly = form.BodyFixedOnly;
                    setShow(form.HudVisible);
                    cfg.Save();
                    Console.WriteLine("settings applied and saved");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("settings error: " + ex.Message);
            }
            finally
            {
                settingsOpen = false;
            }
        })
        { IsBackground = true, Name = "settings" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    private static int RunOverlay(string[] args)
    {
        double seconds = args.Length > 1 ? double.Parse(args[1], CultureInfo.InvariantCulture) : 0;   // 0 = run until exit
        bool demo = args.Length > 2 && args[2].Equals("demo", StringComparison.OrdinalIgnoreCase);

        NativeMethods.SetProcessDpiAwarenessContext(NativeMethods.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
        int w = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXSCREEN);
        int h = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYSCREEN);
        Console.WriteLine($"overlay {w}x{h} for {(seconds <= 0 ? "unlimited" : seconds + "s")} demo={demo}");

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
                if (viewState != 0) { viewState = 0; Console.WriteLine("view state reset: vehicle changed"); }
            }
            if (s.Valid && !prevValid) { viewState = 0; Console.WriteLine("view state reset: match entered"); }
            prevValid = s.Valid;

            latest = s;
            lastAttitude = fusion.Update(s);
        };
        telemetry.Start();
        using var mapData = new WtHud.Telemetry.MapData();
        mapData.Start();

        var opt = new HudOptions();
        var cfg = HudConfig.Load();
        var cal = new HudCalibration { VerticalFovDeg = cfg.VerticalFovDeg, LayoutOffsetYDeg = cfg.LayoutOffsetYDeg, RollCoupling = cfg.RollCoupling };
        var geo = new HudGeometry(opt, cal);
        var empty = new List<HudPrim>();

        bool show = true;
        using var overlay = new OverlayWindow(w, h);

        // The overlay is a console app; its console window steals the foreground and makes
        // Windows paint the arrow cursor over the game. Redirect output to a log file and
        // hide the console, then give focus back to the game.
        var logPath = Path.Combine(AppContext.BaseDirectory, "wthud-overlay.log");
        try { Console.SetOut(TextWriter.Synchronized(new StreamWriter(logPath, false, new System.Text.UTF8Encoding(false)) { AutoFlush = true })); } catch { }
        Console.WriteLine($"overlay start {DateTime.Now:HH:mm:ss} {w}x{h}");
        overlayRef = overlay;
        overlay.ApplyHotkeys(cfg);
        overlay.SetCursorHole(cfg.CursorHoleHalfPx);
        overlay.NudgeForegroundCursor();
        var console = NativeMethods.GetConsoleWindow();
        if (console != IntPtr.Zero) NativeMethods.ShowWindow(console, NativeMethods.SW_HIDE);
        var game = System.Diagnostics.Process.GetProcessesByName("aces").FirstOrDefault();
        int gamePid = game?.Id ?? 0;
        if (game?.MainWindowHandle is IntPtr gh && gh != IntPtr.Zero) NativeMethods.SetForegroundWindow(gh);

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
                OpenSettings(cfg, cal, opt, show, v => show = v);
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
                bool foreground = gamePid != 0 && IsForegroundProcess(gamePid);

                // While another application owns the foreground, the overlay window is hidden
                // entirely: a topmost window can still swallow clicks over its whole area even
                // when it reports HTTRANSPARENT, which made the desktop unusable.
                if (overlay.IsVisible != foreground) overlay.SetVisible(foreground);

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

            fusion.ForcedPitchSign = cfg.PitchSign;   // 0 = auto-detect the native pitch sign

            // Apply the live calibration on top of the raw solution.
            var shown = new AttitudeSolution
            {
                PitchDeg = att.PitchDeg + cfg.PitchZeroDeg,
                BankDeg = att.BankDeg * cfg.BankSign + cfg.BankTrimDeg,
                Source = att.Source,
                Confidence = att.Confidence,
            };
            opt.FpmTrimDeg = cfg.FpmTrimDeg;

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
                double threshold = hasCal ? calibratedAoa : cfg.StallAoaDeg;
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

                if (snap.AoaIndexer is double idx && idx > 0.9 && opt.StallWarnLevel == 0)
                    opt.StallWarnLevel = idx > 0.97 ? 2 : 1;

                if (opt.StallWarnLevel != stallLogged)
                {
                    stallLogged = opt.StallWarnLevel;
                    Console.WriteLine($"stall level {opt.StallWarnLevel}: AoA={aoa:F1} thr={threshold:F1} Ny={snap.NyG:F1} accel={accelMs2:F1} vy={snap.VyMs:F0}");
                }
            }
            else
            {
                stallSince = 0;
            }

            opt.OverspeedAlert = opt.OverspeedByMach
                ? opt.OverspeedMach > 0.05 && snap.Mach > opt.OverspeedMach
                : opt.OverspeedKmh > 0 && snap.IasKmh > opt.OverspeedKmh;

            if (opt.OverspeedAlert != overspeedLogged)
            {
                overspeedLogged = opt.OverspeedAlert;
                Console.WriteLine(opt.OverspeedAlert
                    ? $"overspeed ON: M={snap.Mach:F2} IAS={snap.IasKmh:F0} km/h (byMach={opt.OverspeedByMach} thr={opt.OverspeedMach:F2}M/{opt.OverspeedKmh:F0}kmh)"
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
            // Overspeed threshold: use the aircraft's own Vne when the cockpit exposes it
            // (m/s -> km/h); fall back to the configured fixed value otherwise.
            if (snap.VneMs is double vneMs && vneMs > 10)
            {
                opt.OverspeedKmh = vneMs * 3.6;
                opt.OverspeedByMach = false;
                if (vneLogged != snap.VehicleType)
                {
                    vneLogged = snap.VehicleType;
                    Console.WriteLine($"Vne from cockpit on {snap.VehicleType}: {vneMs:F1} m/s = {vneMs * 3.6:F0} km/h");
                }
            }
            else
            {
                // Manual threshold: IAS (km/h) or Mach, whichever the user selected.
                opt.OverspeedKmh = cfg.OverspeedKmh;
                opt.OverspeedByMach = cfg.OverspeedByMach;
                opt.OverspeedMach = cfg.OverspeedMach;
            }

            opt.FpmAosSign = cfg.FpmAosSign != 0 ? Math.Sign(cfg.FpmAosSign) : -1;
            opt.AoaWarnAlert = snap.InFlight && snap.AoaDeg > cfg.AoaWarnDeg;
            opt.BingoFuelAlert = opt.EnduranceSeconds >= 0 && opt.EnduranceSeconds < cfg.BingoFuelSeconds;
            // Radar altitude only when the aircraft actually reports one: the API exposes no
            // terrain data, so an estimate was hopeless over hilly maps. Everything else shows
            // barometric altitude, exactly like before.
            bool hasRadioAlt = snap.RadioAltM.HasValue;
            if (hasRadioAlt && !radioAltLogged)
            {
                radioAltLogged = true;
                Console.WriteLine($"radio altimeter available on {snap.VehicleType}: {snap.RadioAltM:F0} m (native)");
            }
            bool showRadarAlt = cfg.TapeRadarAltitude && hasRadioAlt;
            opt.AltTapeIsRadar = showRadarAlt;
            opt.AltTapeM = showRadarAlt ? snap.RadioAltM!.Value : snap.AltM;
            UpdateNavigation(opt, mapData, snap, cfg);

            // Terrain warnings.
            //   precise: only when the aircraft reports a radio altimeter
            //   estimate: sustained high sink rate below 500 m (altitude above sea level)
            long alertTicks = Environment.TickCount64;

            // PULL UP: only with a real radio altimeter, and the trigger altitude grows with the
            // sink rate - what matters is the time to impact, so a fast dive warns higher up.
            double ralt = snap.RadioAltM ?? double.NaN;
            bool hasRalt = !double.IsNaN(ralt);
            double tti = hasRalt && snap.VyMs < -5 ? ralt / -snap.VyMs : double.MaxValue;
            bool pullUpCond = snap.InFlight && hasRalt && ralt < 1500 && snap.VyMs < -10 && tti < 3.5;

            // SINK RATE: estimate-based fallback for every aircraft (sea-level altitude, no terrain).
            bool sinkCond = snap.InFlight && snap.AltM < 500 && snap.VyMs < -40;

            if (pullUpCond) { if (pullUpSince == 0) pullUpSince = alertTicks; } else pullUpSince = 0;
            if (sinkCond) { if (sinkRateSince == 0) sinkRateSince = alertTicks; } else sinkRateSince = 0;

            bool pullUpNow = pullUpSince != 0 && alertTicks - pullUpSince > 250;
            bool sinkNow = sinkRateSince != 0 && alertTicks - sinkRateSince > 1000;

            if (pullUpNow != opt.PullUpAlert)
                Console.WriteLine($"PULL UP {(pullUpNow ? "ON" : "off")}: ralt={snap.RadioAltM:F0} m vy={snap.VyMs:F0} m/s tti={tti:F1} s");
            if (sinkNow != opt.SinkRateAlert)
                Console.WriteLine($"SINK RATE {(sinkNow ? "ON" : "off")}: alt={snap.AltM:F0} m vy={snap.VyMs:F0} m/s");

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
                    if (gearNow > lastGear + 0.05) opt.GearTransitText = "GEAR DOWN";
                    else if (gearNow < lastGear - 0.05) opt.GearTransitText = "GEAR UP";
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

            // Flaps: transit is detected from movement (they can stop at intermediate settings).
            double flapsNow = snap.FlapsPercent;
            long nowTicks = Environment.TickCount64;
            if (flapsNow >= 0 && lastFlaps >= 0 && Math.Abs(flapsNow - lastFlaps) > 1.0)
            {
                opt.FlapsTransitText = flapsNow > lastFlaps ? "FLAPS DOWN" : "FLAPS UP";
                lastFlapsChangeTicks = nowTicks;
            }
            lastFlaps = flapsNow;
            opt.FlapsTransitActive = snap.Valid && nowTicks - lastFlapsChangeTicks < 350 && !string.IsNullOrEmpty(opt.FlapsTransitText);

            // Airbrake: same movement-based detection; a deployed brake also gets a static box.
            double abNow = snap.AirbrakePercent;
            if (abNow >= 0 && lastAirbrake >= 0 && Math.Abs(abNow - lastAirbrake) > 1.0)
            {
                opt.AirbrakeTransitText = abNow > lastAirbrake ? "AIRBRAKE OUT" : "AIRBRAKE IN";
                lastAirbrakeChangeTicks = nowTicks;
            }
            lastAirbrake = abNow;
            opt.AirbrakeTransitActive = snap.Valid && nowTicks - lastAirbrakeChangeTicks < 350 && !string.IsNullOrEmpty(opt.AirbrakeTransitText);

            // Flaps: blink only while moving; settled at a non-zero setting -> steady box.
            opt.AirbrakeDeployed = snap.Valid && abNow > 5 && !opt.AirbrakeTransitActive;
            opt.FlapsDeployed = snap.Valid && !opt.FlapsTransitActive && flapsNow > 0.5;
            opt.FlapsSteadyText = flapsNow.ToString("0");

            // Only submit a frame when something actually changed: the API updates at 17-25 Hz
            // while we would otherwise recompose the full 4K surface at 100+ Hz.
            long key = FrameKey(snap, shown, opt, cal, draw, hiddenByView);
            if (key != lastFrameKey)
            {
                // Build the symbology only for frames we actually submit.
                var prims = draw ? geo.Build(snap, shown, w, h) : empty;
                overlay.Present(prims);
                lastFrameKey = key;
                presents++;
            }
            frames++;

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
                Console.WriteLine($"      att={att}  pitchSign={fusion.DetectedPitchSign}");
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
    private static void UpdateNavigation(HudOptions opt, WtHud.Telemetry.MapData map, TelemetrySnapshot snap, HudConfig cfg)
    {
        opt.GridText = "";
        opt.EdgeWarning = false;
        opt.NavFriendly = "";
        opt.NavEnemy = "";

        if (map.Self is not { } self || !snap.Valid) return;

        double sizeX = map.MapSizeM.X > 1 ? map.MapSizeM.X : 52719;
        double sizeY = map.MapSizeM.Y > 1 ? map.MapSizeM.Y : 55385;

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
            double rel = bearingToCentre - snap.HeadingDeg;
            while (rel < 0) rel += 360;
            while (rel >= 360) rel -= 360;
            opt.EdgeArrow = Arrows[(int)Math.Round(rel / 45.0) % 8];
        }

        // --- nearest friendly / enemy airfield ---
        double bestFriendly = double.MaxValue, bestEnemy = double.MaxValue;
        double relFriendly = 0, relEnemy = 0;
        foreach (var af in map.Airfields)
        {
            double dEast = (af.X - self.X) * sizeX;
            double dNorth = (af.Y - self.Y) * sizeY * (cfg.MapYFlip ? -1 : 1);
            double dist = Math.Sqrt(dEast * dEast + dNorth * dNorth);
            double bearing = Math.Atan2(dEast, dNorth) * 180.0 / Math.PI;
            double rel = bearing - snap.HeadingDeg;
            while (rel < 0) rel += 360;
            while (rel >= 360) rel -= 360;

            if (af.Friendly && dist < bestFriendly) { bestFriendly = dist; relFriendly = rel; }
            else if (!af.Friendly && dist < bestEnemy) { bestEnemy = dist; relEnemy = rel; }
        }

        string Arrow(double rel) => Arrows[(int)Math.Round(rel / 45.0) % 8];
        if (bestFriendly < double.MaxValue)
            opt.NavFriendly = $"AF  {Arrow(relFriendly)} {bestFriendly / 1000.0:F1}km";
        opt.EnemyAirfieldAlert = false;
        if (bestEnemy < double.MaxValue)
        {
            opt.NavEnemy = $"AF! {Arrow(relEnemy)} {bestEnemy / 1000.0:F1}km";
            double alertKm = cfg.EnemyAirfieldAlertKm;
            opt.EnemyAirfieldAlert = alertKm > 0 && bestEnemy / 1000.0 < alertKm;
        }
    }

    private static bool IsForegroundProcess(int pid)
    {
        var h = NativeMethods.GetForegroundWindow();
        if (h == IntPtr.Zero) return false;
        NativeMethods.GetWindowThreadProcessId(h, out int fg);
        return fg == pid;
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
            Mix(o.OverspeedByMach ? 1 : 0, 1);
            Mix(o.OverspeedMach, 0.01);
            Mix(o.PullUpAlert ? 1 : 0, 1);
            Mix(o.AoaWarnAlert ? 1 : 0, 1);
            Mix(o.BingoFuelAlert ? 1 : 0, 1);
            Mix(o.SinkRateAlert ? 1 : 0, 1);
            Mix(o.AltTapeM, 0.5);
            Mix(o.AltTapeIsRadar ? 1 : 0, 1);
            return k;
        }
    }

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
    private static long lastExitPress;
    private static bool lastCursorShowing = true;
    private static WtHud.Input.KeyBinding _zoomKeyBinding = WtHud.Input.KeyBinding.Parse("Z", 0x5A, "Z");
    private static WtHud.Input.KeyBinding _viewKeyBinding = WtHud.Input.KeyBinding.Parse("V", 0x56, "V");
    private static WtHud.Input.KeyBinding _mapKeyBinding = WtHud.Input.KeyBinding.Parse("M", 0x4D, "M");
    private static WtHud.Input.KeyBinding _scoreKeyBinding = WtHud.Input.KeyBinding.Parse("TAB", 0x09, "TAB");
    private static WtHud.Input.KeyBinding _holdHideKeyBinding = WtHud.Input.KeyBinding.Parse("U", 0x55, "U");
    private static WtHud.Input.KeyBinding _resetKeyBinding = WtHud.Input.KeyBinding.Parse("F5", 0x74, "F5");
    private static WtHud.Input.KeyBinding _stallKeyBinding = WtHud.Input.KeyBinding.Parse("CTRL+ALT+S", 0x53, "CTRL+ALT+S");

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
    private static long stallSince;
    private static int stallLogged = -1;
    private static string stallCalWarned = "";
    private static double fuelLastSample = -1;
    private static double fuelFlowAt;
    private static OverlayWindow? overlayRef;
    private static long groundStopSince;
    private static string vneLogged = "";
    private static long pullUpSince;
    private static long sinkRateSince;

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
            _ => new AttitudeSolution { PitchDeg = 8.0, BankDeg = 24.0, Source = AttitudeSource.Estimated, Confidence = 1 },
        };

        var opt = new HudOptions();
        if (variant == "zoom") { opt.ZoomLayout = true; opt.ZoomBlend = 1.0; }
        if (variant == "flaps") { opt.FlapsDeployed = true; opt.FlapsSteadyText = "50"; }
        opt.AltTapeM = snap.AltM;   // previews default to barometric altitude on the tape
        opt.GridText = "F6";
        opt.NavFriendly = "AF  " + (char)0x2193 + " 4.2km";
        opt.NavEnemy = "AF! " + (char)0x2197 + " 9.8km";
        if (variant == "edge") { opt.EdgeWarning = true; opt.EdgeArrow = "↑"; opt.ForceTransitVisible = true; }
        if (variant == "pullup") { opt.PullUpAlert = true; opt.ForceTransitVisible = true; }
        if (variant == "sink") { opt.SinkRateAlert = true; opt.ForceTransitVisible = true; }
        if (variant == "aoa") { opt.AoaWarnAlert = true; opt.ForceTransitVisible = true; }
        if (variant == "speed") { opt.OverspeedAlert = true; opt.ForceTransitVisible = true; }
        if (variant == "stall") { opt.StallWarnLevel = 2; opt.ForceTransitVisible = true; }
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
            opt.GearTransitText = "GEAR DOWN";
            if (variant == "gearzoom") { opt.ZoomLayout = true; opt.ZoomBlend = 1.0; }
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
}



















































































































































