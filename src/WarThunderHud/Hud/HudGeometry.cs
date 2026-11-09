// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 A-common-Chinese
// This file is part of War Thunder HUD (8111), licensed under the GNU AGPL v3.0 or later.

using WtHud.Telemetry;

using WtHud.UI;

namespace WtHud.Hud;

public sealed class HudOptions
{
    public HudColor Color { get; set; } = new(60, 255, 110);
    public HudColor WarnColor { get; set; } = new(255, 190, 60);
    public HudColor DangerColor { get; set; } = new(255, 60, 60);
    public bool ShowPitchLadder { get; set; } = true;
    public bool ShowHorizon { get; set; } = true;
    public bool ShowBankScale { get; set; } = true;
    public bool ShowSpeedTape { get; set; } = true;
    public bool ShowAltTape { get; set; } = true;
    public bool ShowHeadingTape { get; set; } = true;
    public bool ShowFpm { get; set; } = true;
    public bool ShowDataBlock { get; set; } = true;
    public bool ShowSourceLabel { get; set; } = true;
    /// <summary>Vertical trim for the flight path marker, degrees (positive = lower).</summary>
    public double FpmTrimDeg { get; set; }

    /// <summary>
    /// Aiming layout, toggled with the game's zoom key (Z): tapes move outwards, the pitch
    /// ladder drops towards the bottom, the bank scale stays put and the heading tape goes
    /// to the very top of the screen.
    /// </summary>
    public bool ZoomLayout { get; set; }

    /// <summary>
    /// 0..1 blend between the normal and the aiming layout, animated so the switch glides
    /// instead of snapping. ZoomLayout is the target flag; the render loop drives this.
    /// </summary>
    public double ZoomBlend { get; set; }

    /// <summary>True while the landing gear is in transit (retracting or extending).</summary>
    public bool GearTransitActive { get; set; }

    /// <summary>Text to blink: "GEAR DOWN" or "GEAR UP".</summary>
    public string GearTransitText { get; set; } = "";

    /// <summary>True while the flaps are moving.</summary>
    public bool FlapsTransitActive { get; set; }

    /// <summary>Text to blink: "FLAPS DOWN" or "FLAPS UP".</summary>
    public string FlapsTransitText { get; set; } = "";

    /// <summary>True while the airbrake is moving.</summary>
    /// <summary>Preview helper: render only the alert boxes (STALL/AOA/BINGO/REDUCE SPEED/PULL UP).</summary>
    public bool OnlyAlerts { get; set; }

    /// <summary>Preview helper: render only the GEAR/FLAPS/AIRBRAKE boxes.</summary>
    public bool OnlyConfigBoxes { get; set; }

    /// <summary>SPLASH 播报主文字（"SPLASH"），空 = 不显示。</summary>
    public string SplashMain { get; set; } = "";

    /// <summary>SPLASH 副文字（被击落的目标名）。</summary>
    public string SplashSub { get; set; } = "";

    /// <summary>SPLASH 透明度（0..1，淡入淡出由渲染循环驱动）。</summary>
    public double SplashAlpha { get; set; }

    /// <summary>A-10 family: split-aileron deceleron brake, so the box must not blink.</summary>
    public bool DeceleronAirbrake { get; set; }

    /// <summary>Text of the steady airbrake box ("AIRBRAKE" / "AIR BRAKE" for the A-10 family).</summary>
    public string AirbrakeSteadyText { get; set; } = "AIRBRAKE";

    public bool AirbrakeTransitActive { get; set; }

    /// <summary>Text to blink: "AIRBRAKE OUT" or "AIRBRAKE IN".</summary>
    public string AirbrakeTransitText { get; set; } = "";

    /// <summary>Short green confirmation right after the gear reaches its position.</summary>
    public bool GearConfirmActive { get; set; }

    /// <summary>Gear reports fully down: steady readout, no transition needed.</summary>
    public bool GearDownSteady { get; set; }

    /// <summary>Show the raw configuration channel values and derived flags.</summary>
    public bool ConfigDebug { get; set; }

    /// <summary>Lines of the configuration debug block (already formatted).</summary>
    public string[] ConfigDebugLines { get; set; } = System.Array.Empty<string>();

    /// <summary>"GEAR DOWN" / "GEAR UP" for the confirmation box.</summary>
    public string GearConfirmText { get; set; } = "";

    /// <summary>True when the airbrake is deployed but not moving (steady box).</summary>
    public bool AirbrakeDeployed { get; set; }

    /// <summary>Full roll angle in degrees (-180..180), used for the upright/inverted sign.</summary>
    public double RollFullDeg { get; set; }

    /// <summary>True when the aircraft is more than 90 deg rolled (belly up).</summary>
    public bool Inverted { get; set; }

    /// <summary>True when the flaps are settled at a non-zero position (steady box).</summary>
    public bool FlapsDeployed { get; set; }

    /// <summary>Flaps position shown in the steady box.</summary>
    public string FlapsSteadyText { get; set; } = "";

    /// <summary>Estimated fuel endurance in seconds (-1 = unknown).</summary>
    public double EnduranceSeconds { get; set; } = -1;

    /// <summary>0 = no stall warning, 1 = approaching stall (amber), 2 = stalled (red).</summary>
    public int StallWarnLevel { get; set; }

    /// <summary>True while the speed exceeds the active limit (drives the red readout and boxes).</summary>
    public bool OverspeedAlert { get; set; }

    /// <summary>Overspeed warning by Mach number instead of IAS.</summary>
    public bool OverspeedByMach { get; set; }

    /// <summary>Mach threshold for the manual overspeed warning.</summary>
    public double OverspeedMach { get; set; } = 1.8;

    /// <summary>IAS above which the speed readout turns red (km/h).</summary>
    public double OverspeedKmh { get; set; } = 1400;

    /// <summary>Longitudinal acceleration in m/s^2, derived from dTAS/dt.</summary>
    public double AccelMs2 { get; set; }

    /// <summary>Preview helper: keep the transit boxes visible instead of blinking.</summary>
    public bool ForceTransitVisible { get; set; }

    /// <summary>Blink an AOA-over-limit warning.</summary>
    public bool AoaWarnAlert { get; set; }

    /// <summary>Blink a bingo-fuel warning.</summary>
    public bool BingoFuelAlert { get; set; }

    /// <summary>Sign convention for the lateral flight-path offset (+1 / -1).</summary>
    public int FpmAosSign { get; set; } = -1;

    /// <summary>True when the aircraft reports a radio altimeter and is about to hit the ground.</summary>
    public bool PullUpAlert { get; set; }

    /// <summary>True for a sustained high descent rate at low altitude (estimate-based warning).</summary>
    public bool SinkRateAlert { get; set; }

    /// <summary>Map grid cell under the aircraft, e.g. "F6" (empty when unknown).</summary>
    public string GridText { get; set; } = "";

    /// <summary>True when the aircraft is close to the playable map edge.</summary>
    public bool EdgeWarning { get; set; }

    /// <summary>Arrow pointing back towards the map centre (relative to the nose).</summary>
    public string EdgeArrow { get; set; } = "";

    /// <summary>True when the nearest enemy airfield is inside the alert radius (blinking box).</summary>
    public bool EnemyAirfieldAlert { get; set; }

    /// <summary>Navigation readouts for the nearest friendly / enemy airfield.</summary>
    public string NavFriendly { get; set; } = "";
    public string NavEnemy { get; set; } = "";

    /// <summary>Two nearest friendly ground units, e.g. "AA ↗ 83° 3.1km".</summary>
    public string NavUnit1 { get; set; } = "";
    public string NavUnit2 { get; set; } = "";

    /// <summary>Value shown on the altitude tape (barometric or radar altitude, chosen by the app).</summary>
    public double AltTapeM { get; set; }

    /// <summary>True when <see cref="AltTapeM"/> is a radar (above-ground) altitude.</summary>
    public bool AltTapeIsRadar { get; set; }

    /// <summary>Degraded mode: hide everything that depends on attitude.</summary>
    public bool BodyFixedOnly { get; set; }

    public HudOptions Clone() => (HudOptions)MemberwiseClone();
}

/// <summary>
/// Builds screen-space primitives for the HUD.
///
/// Everything is laid out in *degrees relative to the boresight* and converted with
/// the calibrated px/degree, so 1080p and 4K produce an identical silhouette and the
/// symbology stays locked to the world horizon instead of the pixel grid.
/// Pure maths: no graphics API, so it can be previewed and unit tested.
/// </summary>
public sealed class HudGeometry
{
    private const double Deg2Rad = Math.PI / 180.0;

    // Layout anchors, in degrees of view angle.
    private const double TapeOffsetDeg = 12.0;   // horizontal distance from boresight to tapes
    private const double TapeHalfSpanDeg = 6.2;  // vertical half-height of the tapes
    private const double HeadingOffsetDeg = 12.5; // above boresight
    private const double HeadingHalfSpanDeg = 6.0;
    private const double BankRadiusDeg = 8.6;

    // Pitch ladder / horizon shape (degrees of view angle).
    private const double LadderWideHalfDeg = 5.4;    // ±10° rungs (the long ones)
    private const double LadderNarrowHalfDeg = 3.2;  // all other rungs
    private const double LadderGapDeg = 1.8;         // centre gap of each rung
    private const double LadderLabelGapDeg = 0.40;   // rung end → number
    private const double LadderHookDeg = 0.55;       // hook tick height at the rung ends
    private const double HorizonGapDeg = 2.4;        // centre gap of the horizon line
    // Nose reference: fixed short lines at the ladder anchor (screen-horizontal, no roll).
    // Their inner end is exactly where the horizon line ends, so in level flight the horizon
    // and the two reference lines read as one continuous long line.
    private const double NoseRefInnerDeg = 7.4;
    private const double NoseRefOuterDeg = 8.8;

    // Aiming (zoom) layout overrides.
    private const double ZoomTapeOffsetDeg = 19.0;
    private const double ZoomHeadingOffsetDeg = ZoomHeadingPushDeg;
    private const double ZoomLadderDropDeg = 5.0;
    private const double ZoomBankRadiusDeg = 17.2;   // 2x the normal bank-scale radius
    private const double ZoomBankUpDeg = 0.5;        // whole bank scale lifts this much
    private const double ZoomHeadingPushDeg = 19.5;  // keeps the heading tape clear of it

    private readonly HudOptions _opt;
    private readonly HudCalibration _cal;

    // Per-build layout values (the geometry object is only used from the render thread).
    private double _tapeOffsetDeg = TapeOffsetDeg;
    private double _headingOffsetDeg = HeadingOffsetDeg;
    private double _ladderCenterOffsetPx;
    private double _blend;
    private double _bankRadiusDeg = BankRadiusDeg;
    private double _bankCenterUpPx;

    public HudGeometry(HudOptions opt, HudCalibration cal)
    {
        _opt = opt;
        _cal = cal;
    }

    public List<HudPrim> Build(TelemetrySnapshot s, AttitudeSolution att, int width, int height)
    {
        var prims = new List<HudPrim>();
        double pxDeg = _cal.EffectivePxPerDeg(height);
        double cx = width / 2.0 + _cal.CenterOffsetX;
        double cy = height / 2.0 + _cal.CenterOffsetY - _cal.LayoutOffsetYDeg * pxDeg;

        double blend = SmoothStep(Math.Clamp(_opt.ZoomBlend, 0, 1));
        _blend = blend;
        _tapeOffsetDeg = TapeOffsetDeg + (ZoomTapeOffsetDeg - TapeOffsetDeg) * blend;
        _headingOffsetDeg = HeadingOffsetDeg + (ZoomHeadingOffsetDeg - HeadingOffsetDeg) * blend;
        _ladderCenterOffsetPx = ZoomLadderDropDeg * pxDeg * blend;
        _bankRadiusDeg = BankRadiusDeg + (ZoomBankRadiusDeg - BankRadiusDeg) * blend;
        _bankCenterUpPx = ZoomBankUpDeg * pxDeg * blend;
        double cyLadder = cy + _ladderCenterOffsetPx;

        double line = Math.Max(1.0, 0.050 * pxDeg);
        double font = 0.72 * pxDeg;
        double fontSmall = 0.55 * pxDeg;

        if (_opt.OnlyAlerts)
        {
            AddGearFlaps(prims, cx, cy, pxDeg, font);
            AddTerrainAlerts(prims, cx, cy, pxDeg, font);
            AddStallAlert(prims, cx, cy, pxDeg, font);
            AddOverspeedAlert(prims, cx, cy, pxDeg, font);
            return prims;
        }

        if (_opt.OnlyConfigBoxes)
        {
            AddGearFlaps(prims, cx, cy, pxDeg, font);
            return prims;
        }

        double attAlpha = att.Source == AttitudeSource.Unavailable ? 0.0 : Math.Clamp(att.Confidence, 0, 1);
        if (_opt.BodyFixedOnly) attAlpha = 0.0;

        if (_opt.ShowPitchLadder && attAlpha > 0.05) AddPitchLadder(prims, att, cx, cyLadder, pxDeg, line, font, attAlpha);
        // 地平线与俯仰梯同开关：关掉俯仰梯时一起隐藏，避免只剩一条孤线
        if (_opt.ShowPitchLadder && _opt.ShowHorizon && attAlpha > 0.05)
            AddHorizon(prims, att, cx, cyLadder, pxDeg, line, attAlpha);
        if (_opt.ShowBankScale && attAlpha > 0.05) AddBankScale(prims, att, cx, cy, pxDeg, line, fontSmall, attAlpha);
        if (_opt.ShowHeadingTape) AddHeadingTape(prims, s, cx, cy, pxDeg, line, font, fontSmall);
        if (_opt.ShowSpeedTape) AddSpeedTape(prims, s, cx, cy, pxDeg, line, font, fontSmall);
        if (_opt.ShowAltTape) AddAltTape(prims, s, cx, cy, pxDeg, line, font, fontSmall);
        if (_opt.ShowFpm) AddFpm(prims, s, cx, cy, pxDeg, line);
        if (_opt.ShowDataBlock) AddDataBlock(prims, s, cx, cy, pxDeg, font);
        if (_opt.ShowSourceLabel) AddSourceLabel(prims, att, cx, cy, pxDeg, fontSmall);
        AddGearFlaps(prims, cx, cy, pxDeg, font);
        AddConfigDebug(prims, cx, cy, pxDeg, fontSmall);
        AddNavigation(prims, cx, cy, pxDeg, font, fontSmall);
        AddTerrainAlerts(prims, cx, cy, pxDeg, font);
        AddOverspeedAlert(prims, cx, cy, pxDeg, font);
        AddStallAlert(prims, cx, cy, pxDeg, font);
        AddSplash(prims, cx, cy, pxDeg, font, line);

        return prims;
    }

    /// <summary>
    /// SPLASH：自己击落敌机时的播报，大号带框文字 + 目标名。
    /// 放在中心下方，避开俯仰梯/数据块；透明度由渲染循环的淡入淡出控制。
    /// </summary>
    private void AddSplash(List<HudPrim> prims, double cx, double cy, double pxDeg, double font, double line)
    {
        if (string.IsNullOrEmpty(_opt.SplashMain) || _opt.SplashAlpha <= 0.02) return;
        double alpha = Math.Clamp(_opt.SplashAlpha, 0, 1);
        var col = Fade(_opt.Color, alpha);
        double y = cy + 11.8 * pxDeg;
        AddBoxedText(prims, cx, y, _opt.SplashMain, font * 2.1, HudAlign.Center, col, line * 2.2, pxDeg);
        if (!string.IsNullOrEmpty(_opt.SplashSub))
        {
            prims.Add(new HudPrim
            {
                Kind = HudPrimKind.Text, Text = _opt.SplashSub, Align = HudAlign.Center,
                FontSize = font * 1.05, Color = Fade(_opt.Color, alpha * 0.95),
                Points = { (cx, y + 2.9 * pxDeg) },
            });
        }
    }

    // ---------------------------------------------------------------- attitude

    private void AddPitchLadder(List<HudPrim> prims, AttitudeSolution att, double cx, double cy,
        double pxDeg, double line, double font, double alpha)
    {
        double theta = -att.BankDeg * _cal.RollCoupling * Deg2Rad;
        double cos = Math.Cos(theta), sin = Math.Sin(theta);
        var col = Fade(_opt.Color, alpha);
        double gap = LadderGapDeg * pxDeg;
        double hook = LadderHookDeg * pxDeg;
        double labelGap = LadderLabelGapDeg * pxDeg;

        for (int p = -90; p <= 90; p += 5)
        {
            if (p == 0) continue;
            double dy = (att.PitchDeg - p) * pxDeg;
            if (Math.Abs(dy) > 7.0 * pxDeg) continue;

            double half = (Math.Abs(p) <= 10 ? LadderWideHalfDeg : LadderNarrowHalfDeg) * pxDeg;
            bool dashed = p < 0;
            AddRotatedSegment(prims, cx, cy, -half, dy, -gap, dy, cos, sin, line, col, dashed);
            AddRotatedSegment(prims, cx, cy, gap, dy, half, dy, cos, sin, line, col, dashed);

            // hook ticks at both ends pointing toward the horizon (classic HUD ladder shape)
            double tickDir = p > 0 ? 1 : -1;
            AddRotatedSegment(prims, cx, cy, half, dy, half, dy + tickDir * hook, cos, sin, line, col, dashed);
            AddRotatedSegment(prims, cx, cy, -half, dy, -half, dy + tickDir * hook, cos, sin, line, col, dashed);

            double lx = half + labelGap;
            AddRotatedText(prims, cx, cy, lx, dy, p.ToString(), font, HudAlign.Left, cos, sin, Fade(_opt.Color, alpha * 0.95));
            AddRotatedText(prims, cx, cy, -lx, dy, p.ToString(), font, HudAlign.Right, cos, sin, Fade(_opt.Color, alpha * 0.95));
        }

        // 机头基准短线：固定在俯仰梯锚点高度、不随坡度旋转。梯子移动时，这两段线对准的刻度
        // 就是机头当前指向的俯仰值；内端与地平线半宽相同，平飞时与地平线连成一条长横线。
        double refW = line * 1.7;
        var refCol = Fade(_opt.Color, alpha * 0.95);
        AddSegment(prims, cx - NoseRefOuterDeg * pxDeg, cy, cx - NoseRefInnerDeg * pxDeg, cy, refW, refCol);
        AddSegment(prims, cx + NoseRefInnerDeg * pxDeg, cy, cx + NoseRefOuterDeg * pxDeg, cy, refW, refCol);
    }

    /// <summary>地平线：加粗加长到与机头基准短线内端相接，平飞时读作一条贯穿的长横线。</summary>
    private void AddHorizon(List<HudPrim> prims, AttitudeSolution att, double cx, double cy, double pxDeg, double line, double alpha)
    {
        double theta = -att.BankDeg * _cal.RollCoupling * Deg2Rad;
        double cos = Math.Cos(theta), sin = Math.Sin(theta);
        double dy = att.PitchDeg * pxDeg;
        double half = NoseRefInnerDeg * pxDeg;      // 与机头基准短线内端对齐
        double gap = HorizonGapDeg * pxDeg;
        var col = Fade(_opt.Color, alpha);
        double wide = line * 2.0;                   // 比普通梯线粗一倍：地平线要醒目
        AddRotatedSegment(prims, cx, cy, -half, dy, -gap, dy, cos, sin, wide, col, false);
        AddRotatedSegment(prims, cx, cy, gap, dy, half, dy, cos, sin, wide, col, false);
    }

    private void AddBankScale(List<HudPrim> prims, AttitudeSolution att, double cx, double cyIn, double pxDeg,
        double line, double font, double alpha)
    {
        // In the aiming layout the scale grows to ~2x and lifts, so it hugs the top of the
        // screen instead of sitting near the boresight.
        double cy = cyIn - _bankCenterUpPx;
        double r = _bankRadiusDeg * pxDeg;
        for (int a = -60; a <= 60; a += 10)
        {
            double ang = (-90 + a) * Deg2Rad;
            bool major = a == 0 || Math.Abs(a) == 30 || Math.Abs(a) == 60;
            double len = (major ? 1.0 : 0.55) * pxDeg;
            prims.Add(new HudPrim
            {
                Kind = HudPrimKind.Line,
                Width = line,
                Color = Fade(_opt.Color, alpha * (major ? 1.0 : 0.65)),
                Points =
                {
                    (cx + Math.Cos(ang) * r, cy + Math.Sin(ang) * r),
                    (cx + Math.Cos(ang) * (r + len), cy + Math.Sin(ang) * (r + len)),
                },
            });
            if (major && a != 0)
            {
                double tr = r + len + 1.2 * pxDeg;
                prims.Add(new HudPrim
                {
                    Kind = HudPrimKind.Text,
                    Text = Math.Abs(a).ToString(),
                    Align = HudAlign.Center,
                    FontSize = font,
                    Color = Fade(_opt.Color, alpha * 0.8),
                    Points = { (cx + Math.Cos(ang) * tr, cy + Math.Sin(ang) * tr) },
                });
            }
        }

        // current bank pointer
        double pa = (-90 + Math.Clamp(att.BankDeg, -60, 60)) * Deg2Rad;   // clamped bank pointer (as before)
        double tipR = r - 1.6 * pxDeg;
        double baseR = r - 3.9 * pxDeg;
        double hw = 0.9 * pxDeg;
        double tx = cx + Math.Cos(pa) * tipR, ty = cy + Math.Sin(pa) * tipR;
        double bx = cx + Math.Cos(pa) * baseR, by = cy + Math.Sin(pa) * baseR;
        double px = -Math.Sin(pa) * hw, py = Math.Cos(pa) * hw;
        prims.Add(new HudPrim
        {
            Kind = HudPrimKind.Polyline,
            Width = line,
            Color = Fade(_opt.WarnColor, alpha),
            Points = { (tx, ty), (bx + px, by + py), (bx - px, by - py), (tx, ty) },
        });

        // Bank pointer interior: full roll magnitude on top, upright/inverted sign underneath.
        // "+" = belly down (normal), "-" = belly up (inverted).
        string bankText = Math.Round(Math.Abs(att.BankDeg)).ToString("0");
        string bankSign = att.BankDeg >= 0 ? "+" : "-";
        double midR = (tipR + baseR) / 2.0 + 0.25 * pxDeg;   // slightly toward the tip
        prims.Add(new HudPrim
        {
            Kind = HudPrimKind.Text,
            Text = bankText,
            Align = HudAlign.Center,
            FontSize = font * 0.78,
            Color = Fade(_opt.WarnColor, alpha),
            Points = { (cx + Math.Cos(pa) * midR, cy + Math.Sin(pa) * midR) },
        });
        double signR = midR - 0.95 * pxDeg;   // toward the base = "below" the number
        prims.Add(new HudPrim
        {
            Kind = HudPrimKind.Text,
            Text = bankSign,
            Align = HudAlign.Center,
            FontSize = font * 1.05,           // deliberately bigger: it is the upright/inverted flag
            Color = Fade(_opt.WarnColor, alpha),
            Points = { (cx + Math.Cos(pa) * signR, cy + Math.Sin(pa) * signR) },
        });
    }

    // -------------------------------------------------------------------- tapes

    private void AddSpeedTape(List<HudPrim> prims, TelemetrySnapshot s, double cx, double cy, double pxDeg,
        double line, double font, double fontSmall)
    {
        double x = cx - _tapeOffsetDeg * pxDeg;
        double span = TapeHalfSpanDeg * pxDeg;
        double pxPerKmh = 1.5 * pxDeg / 50.0;   // 50 km/h per 1.5 deg
        var col = _opt.Color;

        prims.Add(new HudPrim { Kind = HudPrimKind.Line, Width = line, Color = col, Points = { (x, cy - span), (x, cy + span) } });

        double startV = Math.Floor((s.IasKmh - span / pxPerKmh) / 50) * 50;
        for (double v = startV; v <= s.IasKmh + span / pxPerKmh; v += 50)
        {
            if (v < 0) continue;
            double y = cy + (s.IasKmh - v) * pxPerKmh;
            if (Math.Abs(y - cy) > span) continue;
            bool major = Math.Abs(v % 100) < 0.01;
            prims.Add(new HudPrim
            {
                Kind = HudPrimKind.Line, Width = line, Color = Fade(col, 0.8),
                Points = { (x + (major ? -1.0 : -0.6) * pxDeg, y), (x, y) },
            });
            if (major && Math.Abs(y - cy) > 2.8 * pxDeg)
            {
                prims.Add(new HudPrim
                {
                    Kind = HudPrimKind.Text, Text = ((int)v).ToString("0"),
                    Align = HudAlign.Right, FontSize = fontSmall, Color = Fade(col, 0.7),
                    Points = { (x - 2.6 * pxDeg, y) },
                });
            }
        }

        bool overspeed = _opt.OverspeedAlert;
        AddBoxedText(prims, x - 0.6 * pxDeg, cy, s.IasKmh.ToString("0"), font, HudAlign.Right,
            overspeed ? _opt.DangerColor : col, line, pxDeg);

        // Longitudinal acceleration (dTAS/dt) takes the slot the Mach readout used to occupy;
        // Mach moves one row down. Both sit INSIDE the speed tape.
        prims.Add(new HudPrim
        {
            Kind = HudPrimKind.Text, Text = "ACC " + _opt.AccelMs2.ToString("+0.0;-0.0"),
            Align = HudAlign.Left, FontSize = fontSmall,
            Color = Math.Abs(_opt.AccelMs2) > 5 ? _opt.WarnColor : Fade(col, 0.9),
            Points = { (x + 1.7 * pxDeg, cy + 2.6 * pxDeg) },
        });
        prims.Add(new HudPrim
        {
            Kind = HudPrimKind.Text, Text = "M " + s.Mach.ToString("0.00"),
            Align = HudAlign.Left, FontSize = fontSmall, Color = Fade(col, 0.9),
            Points = { (x + 1.7 * pxDeg, cy + 4.1 * pxDeg) },
        });

        // Acceleration trend bar, mirroring the vertical-speed bar on the altitude tape.
        // Both bars share the same baseline (cy) and get a short tick so the zero is readable.
        double accLen = Math.Clamp(Math.Abs(_opt.AccelMs2) * 0.50 * pxDeg, 0, 5.0 * pxDeg);   // 0-10 m/s^2 maps to 0-5 deg
        double accBarX = x + 1.3 * pxDeg;   // between the tape and the readouts
        prims.Add(new HudPrim
        {
            Kind = HudPrimKind.Line, Width = line, Color = Fade(col, 0.55),
            Points = { (accBarX - 0.25 * pxDeg, cy), (accBarX + 0.25 * pxDeg, cy) },
        });
        if (accLen > 0.15 * pxDeg)
        {
            double dir = _opt.AccelMs2 >= 0 ? -1 : 1;
            prims.Add(new HudPrim
            {
                Kind = HudPrimKind.Line, Width = line, Color = Fade(col, 0.85),
                Points = { (accBarX, cy), (accBarX, cy + dir * accLen) },
            });
        }
        prims.Add(new HudPrim
        {
            Kind = HudPrimKind.Text, Text = "KM/H",
            Align = HudAlign.Right, FontSize = fontSmall * 0.85, Color = Fade(col, 0.5),
            Points = { (x - 4.6 * pxDeg, cy - 3.4 * pxDeg) },
        });
    }

    private void AddAltTape(List<HudPrim> prims, TelemetrySnapshot s, double cx, double cy, double pxDeg,
        double line, double font, double fontSmall)
    {
        double x = cx + _tapeOffsetDeg * pxDeg;
        double span = TapeHalfSpanDeg * pxDeg;
        double pxPerM = 1.5 * pxDeg / 100.0;    // 100 m per 1.5 deg
        double tapeAlt = _opt.AltTapeM;
        var col = _opt.Color;

        prims.Add(new HudPrim { Kind = HudPrimKind.Line, Width = line, Color = col, Points = { (x, cy - span), (x, cy + span) } });

        double startV = Math.Floor((tapeAlt - span / pxPerM) / 100) * 100;
        for (double v = startV; v <= tapeAlt + span / pxPerM; v += 100)
        {
            if (v < 0) continue;
            double y = cy + (tapeAlt - v) * pxPerM;
            if (Math.Abs(y - cy) > span) continue;
            bool major = Math.Abs(v % 200) < 0.01;
            prims.Add(new HudPrim
            {
                Kind = HudPrimKind.Line, Width = line, Color = Fade(col, 0.8),
                Points = { (x, y), (x + (major ? 1.0 : 0.6) * pxDeg, y) },
            });
            if (major && Math.Abs(y - cy) > 2.8 * pxDeg)
            {
                prims.Add(new HudPrim
                {
                    Kind = HudPrimKind.Text, Text = ((int)v).ToString("0"),
                    Align = HudAlign.Left, FontSize = fontSmall, Color = Fade(col, 0.7),
                    Points = { (x + 2.6 * pxDeg, y) },
                });
            }
        }

                // Radar altitude on the tape turns amber below 50 m and red below 10 m.
        HudColor tapeColor = col;
        if (_opt.AltTapeIsRadar)
        {
            if (tapeAlt < 10) tapeColor = _opt.DangerColor;
            else if (tapeAlt < 50) tapeColor = _opt.WarnColor;
        }
        AddBoxedText(prims, x + 0.6 * pxDeg, cy, tapeAlt.ToString("0"), font, HudAlign.Left, tapeColor, line, pxDeg);
        if (_opt.AltTapeIsRadar)
        {
            prims.Add(new HudPrim
            {
                Kind = HudPrimKind.Text, Text = "RA",
                Align = HudAlign.Left, FontSize = fontSmall * 0.85, Color = Fade(tapeColor, 0.8),
                Points = { (x + 2.2 * pxDeg, cy - 2.4 * pxDeg) },
            });
        }

        // Vertical-speed trend bar: same baseline (cy) as the acceleration bar, with a tick.
        double vyLen = Math.Clamp(Math.Abs(s.VyMs) * 0.05 * pxDeg, 0, 5.0 * pxDeg);   // 0-100 m/s maps to 0-5 deg
        double vyBarX = x - 1.3 * pxDeg;   // between the tape and the V/S readout
        prims.Add(new HudPrim
        {
            Kind = HudPrimKind.Line, Width = line, Color = Fade(col, 0.55),
            Points = { (vyBarX - 0.25 * pxDeg, cy), (vyBarX + 0.25 * pxDeg, cy) },
        });
        if (vyLen > 0.15 * pxDeg)
        {
            double dir = s.VyMs >= 0 ? -1 : 1;
            prims.Add(new HudPrim
            {
                Kind = HudPrimKind.Line, Width = line, Color = Fade(col, 0.85),
                Points = { (vyBarX, cy), (vyBarX, cy + dir * vyLen) },
            });
        }
        // Vertical speed sits INSIDE the altitude tape (towards the screen centre).
        prims.Add(new HudPrim
        {
            Kind = HudPrimKind.Text, Text = "V/S " + (s.VyMs >= 0 ? "+" : "") + s.VyMs.ToString("0.0"),
            Align = HudAlign.Right, FontSize = fontSmall, Color = Fade(col, 0.9),
            Points = { (x - 1.5 * pxDeg, cy + 2.6 * pxDeg) },
        });
        prims.Add(new HudPrim
        {
            Kind = HudPrimKind.Text, Text = "M",
            Align = HudAlign.Left, FontSize = fontSmall * 0.85, Color = Fade(col, 0.5),
            Points = { (x + 4.6 * pxDeg, cy - 3.4 * pxDeg) },
        });
    }

    private void AddHeadingTape(List<HudPrim> prims, TelemetrySnapshot s, double cx, double cy, double pxDeg,
        double line, double font, double fontSmall)
    {
        double y = cy - _headingOffsetDeg * pxDeg;
        double span = HeadingHalfSpanDeg * pxDeg;
        double pxPer10Deg = 2.4 * pxDeg;
        var col = _opt.Color;

        prims.Add(new HudPrim { Kind = HudPrimKind.Line, Width = line, Color = col, Points = { (cx - span, y), (cx + span, y) } });

        double hdg = s.HeadingDeg;
        double first = Math.Ceiling((hdg - HeadingHalfSpanDeg * 10 / 2.4) / 10) * 10;
        for (double d = first; d <= hdg + HeadingHalfSpanDeg * 10 / 2.4; d += 10)
        {
            double x = cx + (d - hdg) * pxPer10Deg / 10.0;
            if (Math.Abs(x - cx) > span) continue;
            bool major = Math.Abs(d % 30) < 0.01;
            prims.Add(new HudPrim
            {
                Kind = HudPrimKind.Line, Width = line, Color = Fade(col, 0.8),
                Points = { (x, y), (x, y + (major ? 0.9 : 0.5) * pxDeg) },
            });
            if (major)
            {
                int label = ((int)Math.Round(d) % 360 + 360) % 360;
                prims.Add(new HudPrim
                {
                    Kind = HudPrimKind.Text, Text = (label / 10).ToString("00"),
                    Align = HudAlign.Center, FontSize = fontSmall, Color = Fade(col, 0.8),
                    Points = { (x, y - 0.9 * pxDeg) },
                });
            }
        }

        prims.Add(new HudPrim
        {
            Kind = HudPrimKind.Polyline, Width = line, Color = col,
            Points =
            {
                (cx - 0.7 * pxDeg, y + 1.6 * pxDeg),
                (cx, y + 0.35 * pxDeg),
                (cx + 0.7 * pxDeg, y + 1.6 * pxDeg),
            },
        });
        prims.Add(new HudPrim
        {
            Kind = HudPrimKind.Text, Text = ((int)Math.Round(hdg) % 360).ToString("000"),
            Align = HudAlign.Center, FontSize = font, Color = col,
            Points = { (cx, y + 2.3 * pxDeg) },
        });
    }

    // ---------------------------------------------------------------------- fpm

    private void AddFpm(List<HudPrim> prims, TelemetrySnapshot s, double cx, double cy, double pxDeg, double line)
    {
        // Body-fixed: no attitude needed. AoA moves the velocity vector down, AoS sideways.
        double fx = cx + s.AosDeg * _opt.FpmAosSign * pxDeg;   // sign is a convention, see config
        double fy = cy + (s.AoaDeg + _opt.FpmTrimDeg) * pxDeg;
        double r = 0.7 * pxDeg;
        var col = _opt.Color;

        prims.Add(new HudPrim { Kind = HudPrimKind.Circle, Radius = r, Width = line, Color = col, Points = { (fx, fy) } });
        prims.Add(new HudPrim { Kind = HudPrimKind.Line, Width = line, Color = col, Points = { (fx - r * 2.4, fy), (fx - r * 1.2, fy) } });
        prims.Add(new HudPrim { Kind = HudPrimKind.Line, Width = line, Color = col, Points = { (fx + r * 1.2, fy), (fx + r * 2.4, fy) } });
        prims.Add(new HudPrim { Kind = HudPrimKind.Line, Width = line, Color = col, Points = { (fx, fy - r), (fx, fy - r * 1.8) } });
    }

    // --------------------------------------------------------------- data block

    private void AddDataBlock(List<HudPrim> prims, TelemetrySnapshot s, double cx, double cy, double pxDeg, double font)
    {
        // Fixed position even in the aiming layout: only the tapes move outwards, the numeric
        // block (G / AOA / FUEL / ALT) must not jump around.
        double x = cx - TapeOffsetDeg * pxDeg;
        // In the aiming layout the numeric block sinks slightly (the ladder already moved
        // down); animated with the same blend so it glides instead of jumping.
        double y = cy + (TapeHalfSpanDeg + 1.6 + 2.0 * _blend) * pxDeg;
        double rowH = font * 1.45;
        double valueX = x + 4.2 * pxDeg;
        var col = _opt.Color;

        void Row(string label, string value, HudColor c)
        {
            prims.Add(new HudPrim { Kind = HudPrimKind.Text, Text = label, Align = HudAlign.Left, FontSize = font, Color = Fade(c, 0.6), Points = { (x - 1.2 * pxDeg, y) } });
            prims.Add(new HudPrim { Kind = HudPrimKind.Text, Text = value, Align = HudAlign.Right, FontSize = font, Color = c, Points = { (valueX, y) } });
            y += rowH;
        }

        // Positive and negative load factors have separate limits:
        //   +9 G warn / +12 G danger, -2 G warn / -4 G danger.
        double g = s.NyG;
        HudColor gColor = g >= 12.0 ? _opt.DangerColor
                        : g >= 9.0 ? _opt.WarnColor
                        : g <= -4.0 ? _opt.DangerColor
                        : g <= -2.0 ? _opt.WarnColor
                        : col;
        Row("G", s.NyG.ToString("0.0"), gColor);
        Row(L10n.Get(LangScope.Alerts, "aoa"), s.AoaDeg.ToString("0.0"), col);
        // Endurance instead of raw fuel mass: estimated from the measured fuel-flow rate.
        if (_opt.EnduranceSeconds >= 0)
        {
            int secs = (int)_opt.EnduranceSeconds;
            string endText = $"{secs / 60}:{secs % 60:00}";
            var endColor = _opt.EnduranceSeconds < 120 ? _opt.DangerColor
                         : _opt.EnduranceSeconds < 300 ? _opt.WarnColor
                         : col;
            Row(L10n.Get(LangScope.Alerts, "label.fuel"), endText, endColor);
        }
        else
        {
            Row(L10n.Get(LangScope.Alerts, "label.fuel"), "--:--", col);
        }
        Row(L10n.Get(LangScope.Alerts, "label.alt"), s.AltM.ToString("0"), col);
        if (!string.IsNullOrEmpty(_opt.GridText)) Row(L10n.Get(LangScope.Alerts, "label.grid"), _opt.GridText, col);
    }


    /// <summary>
    /// Boxed, blinking GEAR / FLAPS transit warnings. They sit near the centre (not inside the
    /// numeric block) but share the aiming layout's vertical offset so the whole cluster still
    /// travels together on Z.
    /// </summary>
    /// <summary>Diagnostic block: raw configuration channel values and the derived flags.</summary>
    private void AddConfigDebug(List<HudPrim> prims, double cx, double cy, double pxDeg, double fontSmall)
    {
        if (!_opt.ConfigDebug || _opt.ConfigDebugLines.Length == 0) return;

        double x = cx + 9.0 * pxDeg;
        double y = cy + 13.0 * pxDeg;
        double line = 1.25 * pxDeg;
        double lw = Math.Max(1.0, 0.035 * pxDeg);
        foreach (string text in _opt.ConfigDebugLines)
        {
            AddBoxedTextFixed(prims, x, y, 11.0, text, fontSmall * 0.9, _opt.Color, lw, pxDeg);
            y += line;
        }
    }


    private void AddGearFlaps(List<HudPrim> prims, double cx, double cy, double pxDeg, double font)
    {
        // Fixed slots, never stacked: GEAR / FLAPS / AIRBRAKE (and the two alert slots) always live
        // on the same row so the position itself carries the meaning. All boxes share one width so
        // the column edges line up; the text is centred inside.
        bool phaseOn = _opt.ForceTransitVisible || (Environment.TickCount64 / 260) % 2 == 0;

        const double rowStepDeg = 1.9;
        const double boxWidthDeg = 5.0;
        double lineWidth = Math.Max(1.0, 0.045 * pxDeg);
        double leftX = cx - 7.2 * pxDeg;
        double rightX = cx - 1.8 * pxDeg;
        double baseY = cy + 8.3 * pxDeg + 2.0 * _blend * pxDeg;

        double gearY = baseY;
        double flapsY = baseY + rowStepDeg * pxDeg;
        double brakeY = baseY + 2 * rowStepDeg * pxDeg;

        // ---- left column: GEAR / FLAPS / AIRBRAKE ----
        if (_opt.GearTransitActive && !string.IsNullOrEmpty(_opt.GearTransitText))
        {
            if (phaseOn)
                AddBoxedTextFixed(prims, leftX, gearY, boxWidthDeg, _opt.GearTransitText, font, _opt.WarnColor, lineWidth, pxDeg);
        }
        else if (_opt.GearConfirmActive && !string.IsNullOrEmpty(_opt.GearConfirmText))
        {
            AddBoxedTextFixed(prims, leftX, gearY, boxWidthDeg, _opt.GearConfirmText, font, Fade(_opt.Color, 0.95), lineWidth, pxDeg);
        }
        else if (_opt.GearDownSteady)
        {
            AddBoxedTextFixed(prims, leftX, gearY, boxWidthDeg, L10n.Get(LangScope.Alerts, "gear.down"), font, Fade(_opt.Color, 0.9), lineWidth, pxDeg);
        }

        if (_opt.FlapsTransitActive && !string.IsNullOrEmpty(_opt.FlapsTransitText))
        {
            if (phaseOn)
                AddBoxedTextFixed(prims, leftX, flapsY, boxWidthDeg, _opt.FlapsTransitText, font, _opt.WarnColor, lineWidth, pxDeg);
        }
        else if (_opt.FlapsDeployed)
        {
            AddBoxedTextFixed(prims, leftX, flapsY, boxWidthDeg, L10n.Get(LangScope.Alerts, "label.flaps") + " " + _opt.FlapsSteadyText, font,
                Fade(_opt.WarnColor, 0.85), lineWidth, pxDeg);
        }

        if (_opt.AirbrakeTransitActive && !string.IsNullOrEmpty(_opt.AirbrakeTransitText))
        {
            // A-10 decelerons wobble on every roll, so their box stays solid; others blink.
            if (_opt.DeceleronAirbrake || phaseOn)
                AddBoxedTextFixed(prims, leftX, brakeY, boxWidthDeg, _opt.AirbrakeTransitText, font, _opt.WarnColor, lineWidth, pxDeg);
        }
        else if (_opt.AirbrakeDeployed)
        {
            AddBoxedTextFixed(prims, leftX, brakeY, boxWidthDeg, _opt.AirbrakeSteadyText, font, Fade(_opt.WarnColor, 0.85), lineWidth, pxDeg);
        }

        // ---- right column: BINGO (row 1) / AOA (row 2) ----
        if (_opt.BingoFuelAlert && phaseOn)
            AddBoxedTextFixed(prims, rightX, gearY, BoxWidthDeg(L10n.Get(LangScope.Alerts, "bingo"), font, pxDeg, boxWidthDeg), L10n.Get(LangScope.Alerts, "bingo"), font, _opt.WarnColor, lineWidth, pxDeg);
        if (_opt.AoaWarnAlert && phaseOn)
            AddBoxedTextFixed(prims, rightX, flapsY, BoxWidthDeg(L10n.Get(LangScope.Alerts, "aoa"), font, pxDeg, boxWidthDeg), L10n.Get(LangScope.Alerts, "aoa"), font, _opt.DangerColor, lineWidth, pxDeg);
    }

    private void AddTerrainAlerts(List<HudPrim> prims, double cx, double cy, double pxDeg, double font)
    {
        if (!_opt.PullUpAlert && !_opt.SinkRateAlert) return;
        if (!(_opt.ForceTransitVisible || (Environment.TickCount64 / 250) % 2 == 0)) return;

        double lineWidth = Math.Max(1.0, 0.05 * pxDeg);

        if (_opt.PullUpAlert)
        {
            // Same height as the ACC / V/S readouts, centred between them.
            AddBoxedText(prims, cx, cy + 2.6 * pxDeg, L10n.Get(LangScope.Alerts, "pullup"), font, HudAlign.Center,
                _opt.DangerColor, lineWidth, pxDeg);
        }
        if (_opt.SinkRateAlert)
        {
            AddBoxedText(prims, cx, cy + 4.8 * pxDeg, L10n.Get(LangScope.Alerts, "sinkrate"), font, HudAlign.Center,
                _opt.WarnColor, lineWidth, pxDeg);
        }
    }

    /// <summary>Blinking STALL boxes, same column as REDUCE SPEED but one row higher.</summary>
    private void AddStallAlert(List<HudPrim> prims, double cx, double cy, double pxDeg, double font)
    {
        if (_opt.StallWarnLevel <= 0) return;
        if (!(_opt.ForceTransitVisible || (Environment.TickCount64 / 220) % 2 == 0)) return;

        double lineWidth = Math.Max(1.0, 0.05 * pxDeg);
        double boxWidthDeg = BoxWidthDeg(L10n.Get(LangScope.Alerts, "reducespeed"), font * 0.92, pxDeg, 5.4);
        double y = cy - 1.5 * pxDeg;                 // one row above the REDUCE SPEED boxes
        var col = _opt.StallWarnLevel >= 2 ? _opt.DangerColor : _opt.WarnColor;

        // Same (animating) tape offset as the readouts, so it stays aligned in the aiming layout.
        double tapeLeft = cx - _tapeOffsetDeg * pxDeg;
        double tapeRight = cx + _tapeOffsetDeg * pxDeg;
        var stallW = BoxWidthDeg(L10n.Get(LangScope.Alerts, "stall"), font * 0.92, pxDeg, boxWidthDeg);
        AddBoxedTextFixed(prims, tapeLeft + 2.0 * pxDeg, y, stallW, L10n.Get(LangScope.Alerts, "stall"), font * 0.92, col, lineWidth, pxDeg);
        AddBoxedTextFixed(prims, tapeRight - 2.0 * pxDeg - stallW * pxDeg, y, stallW, L10n.Get(LangScope.Alerts, "stall"),
            font * 0.92, col, lineWidth, pxDeg);
    }


    /// <summary>Blinking "REDUCE SPEED" boxes above the ACC and V/S readouts.</summary>
    private void AddOverspeedAlert(List<HudPrim> prims, double cx, double cy, double pxDeg, double font)
    {
        if (!_opt.OverspeedAlert) return;
        if (!(_opt.ForceTransitVisible || (Environment.TickCount64 / 250) % 2 == 0)) return;

        double lineWidth = Math.Max(1.0, 0.05 * pxDeg);
        double boxWidthDeg = BoxWidthDeg(L10n.Get(LangScope.Alerts, "reducespeed"), font * 0.92, pxDeg, 5.4);
        double y = cy + 0.9 * pxDeg;

        // The readouts sit 2 deg inside the tapes, so the boxes use the same (animating) tape
        // offset - otherwise they would drift away from ACC / V-S when the aiming layout is on.
        double tapeLeft = cx - _tapeOffsetDeg * pxDeg;
        double tapeRight = cx + _tapeOffsetDeg * pxDeg;
        var rsW = BoxWidthDeg(L10n.Get(LangScope.Alerts, "reducespeed"), font * 0.92, pxDeg, boxWidthDeg);
        AddBoxedTextFixed(prims, tapeLeft + 2.0 * pxDeg, y, rsW, L10n.Get(LangScope.Alerts, "reducespeed"), font * 0.92,
            _opt.DangerColor, lineWidth, pxDeg);
        AddBoxedTextFixed(prims, tapeRight - 2.0 * pxDeg - rsW * pxDeg, y, rsW, L10n.Get(LangScope.Alerts, "reducespeed"),
            font * 0.92, _opt.DangerColor, lineWidth, pxDeg);
    }

    private void AddNavigation(List<HudPrim> prims, double cx, double cy, double pxDeg, double font, double fontSmall)
    {
        double sink = 2.0 * _blend * pxDeg;


        double navX = cx + 7.8 * pxDeg;      // clear of the extended horizon line
        double navY = cy + 10.4 * pxDeg + sink;
        if (!string.IsNullOrEmpty(_opt.NavFriendly))
        {
            prims.Add(new HudPrim
            {
                Kind = HudPrimKind.Text, Text = _opt.NavFriendly,
                Align = HudAlign.Left, FontSize = fontSmall, Color = Fade(_opt.Color, 0.85),
                Points = { (navX, navY) },
            });
            navY += 1.55 * pxDeg;
        }
        if (!string.IsNullOrEmpty(_opt.NavEnemy))
        {
            if (_opt.EnemyAirfieldAlert)
            {
                // Inside the warning radius: blinking box so it cannot be missed.
                if ((Environment.TickCount64 / 300) % 2 == 0)
                {
                    double lw = Math.Max(1.0, 0.045 * pxDeg);
                    AddBoxedText(prims, navX, navY, _opt.NavEnemy, fontSmall * 0.95, HudAlign.Left,
                        _opt.WarnColor, lw, pxDeg);
                }
            }
            else
            {
                prims.Add(new HudPrim
                {
                    Kind = HudPrimKind.Text, Text = _opt.NavEnemy,
                    Align = HudAlign.Left, FontSize = fontSmall, Color = Fade(_opt.WarnColor, 0.9),
                    Points = { (navX, navY) },
                });
            }
        }

        // "Return to battle area" sits at the lower edge of the pitch ladder and deliberately does
        // NOT follow the aiming layout, so it stays put when Z is engaged.
        // 友方防空：排在 AF! 之下，避免与其重叠
        var unitY = string.IsNullOrEmpty(_opt.NavEnemy) ? navY : navY + 1.55 * pxDeg;
        foreach (var unit in new[] { _opt.NavUnit1, _opt.NavUnit2 })
        {
            if (string.IsNullOrEmpty(unit)) continue;
            prims.Add(new HudPrim
            {
                Kind = HudPrimKind.Text, Text = unit,
                Align = HudAlign.Left, FontSize = fontSmall * 0.92, Color = Fade(_opt.Color, 0.80),
                Points = { (navX, unitY) },
            });
            unitY += 1.45 * pxDeg;
        }


        if (_opt.EdgeWarning && (_opt.ForceTransitVisible || (Environment.TickCount64 / 320) % 2 == 0))
        {
            double lineWidth = Math.Max(1.0, 0.045 * pxDeg);
            AddBoxedText(prims, cx, cy + 6.5 * pxDeg,
                L10n.Get(LangScope.Alerts, "return") + " " + _opt.EdgeArrow, font, HudAlign.Center, _opt.DangerColor, lineWidth, pxDeg);
        }
    }

    private void AddSourceLabel(List<HudPrim> prims, AttitudeSolution att, double cx, double cy, double pxDeg, double font)
    {
        string text = att.Source switch
        {
            AttitudeSource.Game => "AHRS GAME",
            AttitudeSource.Estimated => "AHRS EST",
            _ => "AHRS N/A",
        };
        var col = att.Source == AttitudeSource.Estimated ? _opt.WarnColor : Fade(_opt.Color, 0.55);
        prims.Add(new HudPrim
        {
            Kind = HudPrimKind.Text, Text = text, Align = HudAlign.Left, FontSize = font, Color = col,
            Points = { (cx + (_tapeOffsetDeg - 3.0) * pxDeg, cy + (TapeHalfSpanDeg + 1.6) * pxDeg) },
        });
    }

    // ------------------------------------------------------------------ helpers

    private static double SmoothStep(double t) => t * t * (3 - 2 * t);

    /// <summary>
    /// Box width in degrees, sized to the given text so a longer/shorter translation still fits.
    /// 0.62 * fontSize per glyph is a good average for the HUD's monospace-ish font.
    /// </summary>
    /// <summary>
    /// Approximate text width in em: ASCII digits/letters are ~0.62 em in the HUD font while CJK
    /// glyphs are full-width, so localised labels need a wider box than their character count.
    /// </summary>
    private static double TextWidthEm(string text)
    {
        double w = 0;
        foreach (var ch in text) w += ch < 0x80 ? 0.62 : 1.0;
        return w;
    }

    private static double BoxWidthDeg(string text, double fontSize, double pxDeg, double minimum)
    {
        var em = Math.Max(1.8, TextWidthEm(text));
        var widthPx = em * fontSize + fontSize * 1.1;
        return Math.Max(minimum, widthPx / Math.Max(1.0, pxDeg));
    }

    private static HudColor Fade(HudColor c, double a) =>
        new(c.R, c.G, c.B, (byte)Math.Clamp((int)(c.A * Math.Clamp(a, 0, 1)), 0, 255));

    /// <summary>Screen-space (non-rotated) segment; used by body-fixed reference marks.</summary>
    private static void AddSegment(List<HudPrim> prims, double x1, double y1, double x2, double y2,
        double width, HudColor color)
    {
        prims.Add(new HudPrim
        {
            Kind = HudPrimKind.Line, Width = width, Color = color,
            Points = { (x1, y1), (x2, y2) },
        });
    }

    private static void AddRotatedSegment(List<HudPrim> prims, double cx, double cy,
        double x1, double y1, double x2, double y2, double cos, double sin, double width, HudColor color, bool dashed)
    {
        if (!dashed)
        {
            prims.Add(new HudPrim
            {
                Kind = HudPrimKind.Line, Width = width, Color = color,
                Points = { Rot(cx, cy, x1, y1, cos, sin), Rot(cx, cy, x2, y2, cos, sin) },
            });
            return;
        }

        double dx = x2 - x1, dy = y2 - y1;
        double len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 1) return;
        const int dashes = 7;
        for (int i = 0; i < dashes; i++)
        {
            double t0 = (double)i / dashes;
            double t1 = t0 + 0.55 / dashes;
            double ax = x1 + dx * t0, ay = y1 + dy * t0;
            double bx = x1 + dx * t1, by = y1 + dy * t1;
            prims.Add(new HudPrim
            {
                Kind = HudPrimKind.Line, Width = width, Color = color,
                Points = { Rot(cx, cy, ax, ay, cos, sin), Rot(cx, cy, bx, by, cos, sin) },
            });
        }
    }

    private static void AddRotatedText(List<HudPrim> prims, double cx, double cy, double x, double y,
        string text, double font, HudAlign align, double cos, double sin, HudColor color)
    {
        var p = Rot(cx, cy, x, y, cos, sin);
        prims.Add(new HudPrim
        {
            Kind = HudPrimKind.Text, Text = text, Align = align, FontSize = font, Color = color,
            Points = { p },
        });
    }

    private static (double X, double Y) Rot(double cx, double cy, double x, double y, double cos, double sin) =>
        (cx + x * cos - y * sin, cy + x * sin + y * cos);

    /// <summary>Box with a fixed width so a whole column of alerts lines up; text stays centred.</summary>
    private void AddBoxedTextFixed(List<HudPrim> prims, double left, double y, double widthDeg,
        string text, double font, HudColor color, double line, double pxDeg)
    {
        double w = widthDeg * pxDeg;
        double h = font * 1.45;
        double top = y - h / 2;
        prims.Add(new HudPrim
        {
            Kind = HudPrimKind.Polyline, Width = line, Color = color,
            Points = { (left, top), (left + w, top), (left + w, top + h), (left, top + h), (left, top) },
        });
        prims.Add(new HudPrim
        {
            Kind = HudPrimKind.Text, Text = text, Align = HudAlign.Center, FontSize = font, Color = color,
            Points = { (left + w / 2, y) },
        });
    }

    private void AddBoxedText(List<HudPrim> prims, double x, double y, string text, double font, HudAlign align,
        HudColor color, double line, double pxDeg)
    {
        double w = Math.Max(font * 2.0, TextWidthEm(text) * font + font * 0.35);
        double h = font * 1.45;
        double left = align == HudAlign.Right ? x - w : (align == HudAlign.Center ? x - w / 2 : x);
        double top = y - h / 2;
        prims.Add(new HudPrim
        {
            Kind = HudPrimKind.Polyline, Width = line, Color = color,
            Points = { (left, top), (left + w, top), (left + w, top + h), (left, top + h), (left, top) },
        });
        prims.Add(new HudPrim
        {
            Kind = HudPrimKind.Text, Text = text, Align = HudAlign.Center, FontSize = font, Color = color,
            Points = { (left + w / 2, y) },
        });
        _ = pxDeg;
    }
}











































































