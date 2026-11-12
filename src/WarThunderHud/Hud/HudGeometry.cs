// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 A-common-Chinese
// This file is part of War Thunder HUD (8111), licensed under the GNU AGPL v3.0 or later.

using WtHud.Telemetry;

using WtHud.UI;

namespace WtHud.Hud;

/// <summary>一架友机的**相对**方位（已减去本机航向）与距离。几何层不需要知道航向，无罗盘机型也能用。</summary>
public readonly record struct FriendlyContact(double RelBearingDeg, double RangeM, string Icon);

/// <summary>引导动作。只表达"该加速、该减速、还是已经稳住"，具体文字由几何层按语言输出。</summary>
public enum FormationAction
{
    None,
    Accelerate,
    Decelerate,
    Hold,
}

/// <summary>
/// 针对最近一架友机的直接引导：往哪边转多少度、现在多远、目标多远、油门怎么动。
/// <para>TurnDeg 就是相对方位（+ = 友机在右侧 = 右转）。</para>
/// <para>DeltaKmh 是"油门大概要动多少"：正 = 需要加速，负 = 需要减速，单位 km/h。
/// 由接近率与期望接近率之差反推，是按 TAS 估的近似值（HUD 显示 IAS，高速时两者有差）。</para>
/// <para>高度差不在这里 —— map_obj 没有 z，单机拿不到友机高度。</para>
/// </summary>
public readonly record struct FormationLead(
    double TurnDeg, double RangeM, double TargetRangeM, FormationAction Action, double DeltaKmh,
    // 长机转向率（°/s，正 = 右转）与来源。0 = 没有提前量，界面不显示这一行。
    double LeadRateDeg = 0, bool LeadFromLink = false,
    // 长机转弯的横向过载（G）与是否超过告警阈值；超了就把上面那行换成「转弯过急」
    double TurnG = 0, bool OverG = false,
    // 本机真空速（km/h）。速度条要画「我在哪、该到哪」两个标记，只有 DeltaKmh 是画不出来的。
    // 0 = 未知，此时不画速度条（地面静止也走这条路，没有编队意义）。
    double CurrentKmh = 0);

/// <summary>长机转向辅助的指令：坡压多了 / 压少了 / 正好。</summary>
public enum TurnAssistAction { BankMore, BankLess, Hold }

/// <summary>
/// 垂直引导。正 <paramref name="ErrorM"/> = 本机比该在的位置低，需要爬。
/// <para><paramref name="Priority"/> 在「编队未完成且高度差超门槛」时为真：
/// 这条会提到速度条下面、加大字号，同时油门建议让位（先爬到位再修速度）。</para>
/// <para><paramref name="FarRangeM"/> 非空 = 爬升阶段被甩得太远，破例多画一行距离
/// （只画距离，横向引导仍然等高度到位）。<paramref name="FarTargetM"/> 是那行右半边的目标距离。</para>
/// </summary>
public readonly record struct VerticalCue(double ErrorM, double RateMs, bool Stale, bool Priority, bool AtTarget,
                                          double? FarRangeM = null, double? FarTargetM = null);

/// <summary>
/// 长机转向辅助的显示数据。
/// <para>横带两端固定 ±60°，绿标是当前坡度、琥珀标是目标坡度 ——
/// 和僚机那套「圈=该飞的方向」不同，长机这边两个标都是**坡度**，所以要放在单独的带上，
/// 不能挂在航向带上（同一符号两套语义会误读）。</para>
/// </summary>
public readonly record struct TurnAssistCue(
    int Dir, double BankNowDeg, double TargetBankDeg, double TargetRateDeg,
    double TargetG, TurnAssistAction Action, bool Arming);

/// <summary>
/// 编队引导两条条的刻度换算。
/// <para>抽成纯函数是为了能断言映射本身（满偏、归零、越界钳位），不必渲染出图。</para>
/// </summary>
public static class FormationBarScale
{
    /// <summary>
    /// 速度条半量程（km/h）：轴心是目标速度，左右各 120。
    /// <para>和建议量的上限（114 km/h）对齐 —— 原来给 200，点最多只能走到 57% 的位置，
    /// 远看永远像"偏了一点点"，反而读不出力度。</para>
    /// </summary>
    public const double SpeedHalfKmh = 120;

    /// <summary>加速度条半量程（km/h）：轴心是「不用改」，左右各 120。</summary>
    public const double AccelHalfKmh = 120;

    /// <summary>
    /// 速度条位置（-1..1）：正 = 比目标快（块在轴心右侧），负 = 比目标慢。
    /// <para>用「目标速度」当轴心而不是本机速度：目标是飞行员要落到的地方，把它钉在中心，
    /// 块往哪边偏就是往哪边修，位置本身就成了「差多少」的读数。</para>
    /// </summary>
    public static double SpeedFrac(double currentKmh, double targetKmh)
        => Math.Clamp((currentKmh - targetKmh) / SpeedHalfKmh, -1.0, 1.0);

    /// <summary>加速度条位置（-1..1）：正 = 该加速，负 = 该减速。</summary>
    public static double AccelFrac(double deltaKmh)
        => Math.Clamp(deltaKmh / AccelHalfKmh, -1.0, 1.0);
}

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

    /// <summary>编队辅助：在航向带上标出友机方位与距离（只画友军，敌人永远不进这个列表）。</summary>
    public bool FormationAssist { get; set; }

    /// <summary>
    /// 编队模式：收掉俯仰梯 / 数据带 / 数值区 / 导航 / 构型，只留友机方位 + 最小自机读数
    /// （速度、高度、航向）。安全告警（地形/失速/超速）和击落播报仍然保留 —— 它们平时不显示，
    /// 触发时才出现，不占视野，但省掉会出人命。
    /// </summary>
    public bool FormationOnly { get; set; }

    /// <summary>
    /// 编队状态提示（空 = 不显示）：到位时显示「编队完成」，解除时显示「编队已解除」。
    /// <para>只在事件发生后几秒内出现，不常驻 —— 常驻会变成噪音。</para>
    /// </summary>
    public string FormationNotice { get; set; } = "";

    /// <summary>友机标记用的颜色（与主色区分开，便于一眼辨认）。</summary>
    public HudColor FriendlyColor { get; set; } = new(120, 200, 255);

    /// <summary>
    /// 编队引导（只对最近一架友机）。null = 不画。
    /// 由宿主算好角度/距离/接近率，几何层只负责排版，不碰地图坐标。
    /// </summary>
    public FormationLead? Guide { get; set; }

    /// <summary>
    /// 长机模式的状态行（空 = 不画）。长机只报状态不给数字：
    /// 主行如 FORMATION / WINGMAN LATE，副行如 TURN R / ADD POWER。
    /// <para>长机不该看到"右转 12°"这种追人数字，那会诱导它去追僚机。</para>
    /// </summary>
    public string LeadStatusMain { get; set; } = "";

    /// <summary>
    /// 长机的僚机表：每行「W1 / 到位」「W2 / 掉队 480m」。空 = 不画。
    /// <para>主行只说整体（编队完整 / 僚机掉队），具体谁怎么样看这张表 ——
    /// 三人编队时一句话说不清，列表才能一眼扫完。</para>
    /// </summary>
    public List<(string Label, string State, bool Warn)> LeadWingRows { get; } = new();

    /// <summary>长机模式的副状态行（姿态偏差），空则不画。</summary>
    public string LeadStatusSub { get; set; } = "";

    /// <summary>主状态是否该用警告色（僚机掉队、距离过近、没设速度等）。</summary>
    public bool LeadStatusWarn { get; set; }

    /// <summary>
    /// 长机模式的速度条数据：当前真空速 / 目标真空速（按设定马赫换算）/ 差值。
    /// <para>长机不走僚机那套引导，但「我该飞多快」同样需要一眼看得出 —— 只留一句
    /// ADD POWER 的话，飞行员还得自己找空速表心算差多少。null = 不画。</para>
    /// </summary>
    public (double CurrentKmh, double TargetKmh, double DeltaKmh)? LeadSpeedBar { get; set; }

    /// <summary>长机转向辅助：有值就画坡度横带（只有长机模式会设它）。</summary>
    public TurnAssistCue? TurnAssist { get; set; }

    /// <summary>数据链收到的长机目标坡度（僚机侧）。有值就在航向带下面报一行「长机 R54°」。</summary>
    public double? LeadTargetBankDeg { get; set; }

    /// <summary>高度差是否为「保留的旧值」（链路断了但还没超过保留期）。</summary>
    public bool AltDiffStale { get; set; }

    /// <summary>垂直引导：误差（米，正 = 我偏低要爬）+ 建议升降率 + 数据是否过期。</summary>
    public VerticalCue? Vertical { get; set; }

    /// <summary>
    /// 编队引导的提示行：找不到长机时说明原因是哪一种。空 = 不画。
    /// <para>没有它的话，条件不满足时界面上一个像素都不会变，飞行员只能猜是功能坏了还是设置错了。</para>
    /// </summary>
    public string FormationHint { get; set; } = "";

    /// <summary>
    /// 系统希望我飞的航向（度）。航向带上画一个琥珀色三角指过去。
    /// <para>nil = 不画（没有目标航向时，比如长机开了全向跟随）。</para>
    /// </summary>
    public double? TargetHeadingDeg { get; set; }

    /// <summary>
    /// 与对方的气压高度差（m）。正 = **对方比我高**（我该爬升）。
    /// null = 没有数据链数据（链路没开或对方没发），此时整行不画。
    /// <para>高度是 map_obj 里唯一没有的量，所以这一项只能来自数据链。</para>
    /// </summary>
    public double? AltDiffM { get; set; }

    /// <summary>本帧的友机相对方位与距离，由宿主（Program）每帧填充。</summary>
    public List<FriendlyContact> Friendlies { get; } = new();
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

    /// <summary>Mach threshold for the manual overspeed warning.</summary>
    public double OverspeedMach { get; set; } = 1.8;

    /// <summary>IAS above which the speed readout turns red (km/h).</summary>
    public double OverspeedKmh { get; set; } = 1400;

    /// <summary>Longitudinal acceleration in m/s^2, derived from dTAS/dt.</summary>
    public double AccelMs2 { get; set; }

    /// <summary>
    /// 编队目标速度（真空速）。由 <c>AddFormationGuidance</c> 写入，<c>AddFormationReadout</c>
    /// 拿去在速度带上画琥珀三角 —— 原来这个值只喂给下面那条独立横条，横条一删就没了来源。
    /// </summary>
    public double? FormationTargetKmh { get; set; }

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

        if (_opt.FormationOnly)
        {
            // 编队模式：视野中心完全留给外部视觉，只保留编队信息 + 三个最小读数。
            // 航向带保留 —— 友机标记的方位是相对航向的，没有带就没有参照。
            // FPM 必须保留：引导圈的用法就是「把 FPM 横移进圈里」，藏了 FPM 这个动作无从谈起。
            if (_opt.ShowHeadingTape) AddHeadingTape(prims, s, cx, cy, pxDeg, line, font, fontSmall);
            if (_opt.ShowFpm) AddFpm(prims, s, cx, cy, pxDeg, line);
            AddFormationAssist(prims, s, cx, cy, pxDeg, line, fontSmall);
            AddFormationGuidance(prims, s, cx, cy, pxDeg, font, fontSmall);
            AddFormationReadout(prims, s, cx, cy, pxDeg, line, font, fontSmall);
            AddTerrainAlerts(prims, cx, cy, pxDeg, font);
            AddOverspeedAlert(prims, cx, cy, pxDeg, font);
            AddStallAlert(prims, cx, cy, pxDeg, font);
            AddSplash(prims, cx, cy, pxDeg, font, line);
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
        if (_opt.FormationAssist) AddFormationAssist(prims, s, cx, cy, pxDeg, line, fontSmall);
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
    /// SPLASH：自己击落敌机时的播报（中文「击落」/ 英文 SPLASH），实心绿底 + 目标名。
    /// 位于中心上方；无动画，由渲染循环直接开关。
    /// </summary>
    private void AddSplash(List<HudPrim> prims, double cx, double cy, double pxDeg, double font, double line)
    {
        if (string.IsNullOrEmpty(_opt.SplashMain) || _opt.SplashAlpha <= 0.02) return;
        double alpha = Math.Clamp(_opt.SplashAlpha, 0, 1);
        double big = font * 2.6;
        double y = cy - 6.0 * pxDeg;                     // 屏幕中心上方，正对俯仰梯中部

        // 中文「击落」：两个字按固定字距排、框宽按内容收紧。
        // 直接用一段文本居中会「只有中间有字」或「分得太开」，两种都难看。
        string main = _opt.SplashMain;
        bool cjkPair = main.Length == 2 && main[0] >= 0x2E80 && main[1] >= 0x2E80;
        double glyphW = big;
        double charGap = big * 0.28;
        double textW = cjkPair ? glyphW * 2 + charGap : TextWidthEm(main) * big;
        double w = Math.Max(big * 2.0, textW + big * 0.55);
        double h = big * 1.10;                           // 扁一点，别顶到上下
        double left = cx - w / 2;
        double top = y - h / 2;

        // 实心绿底：HudPrim 的 Polyline 只描边，用横线扫描填充（无动画，直接出现/消失）
        var fill = Fade(_opt.Color, alpha * 0.92);
        const int rows = 18;
        double rh = h / rows;
        for (int i = 0; i < rows; i++)
        {
            double ly = top + rh * (i + 0.5);
            prims.Add(new HudPrim
            {
                Kind = HudPrimKind.Line, Width = rh + 0.6, Color = fill,
                Points = { (left, ly), (left + w, ly) },
            });
        }
        if (cjkPair)
        {
            double dx = (glyphW + charGap) / 2;
            prims.Add(new HudPrim
            {
                Kind = HudPrimKind.Text, Text = main[0].ToString(), Align = HudAlign.Center, FontSize = big,
                Color = Fade(new HudColor(2, 20, 10, 255), alpha), Points = { (cx - dx, y) },
            });
            prims.Add(new HudPrim
            {
                Kind = HudPrimKind.Text, Text = main[1].ToString(), Align = HudAlign.Center, FontSize = big,
                Color = Fade(new HudColor(2, 20, 10, 255), alpha), Points = { (cx + dx, y) },
            });
        }
        else
        {
            prims.Add(new HudPrim
            {
                Kind = HudPrimKind.Text, Text = main, Align = HudAlign.Center, FontSize = big,
                Color = Fade(new HudColor(2, 20, 10, 255), alpha), Points = { (cx, y) },
            });
        }

        // 被击落目标名：无框，普通绿字，放在框下面一点
        if (!string.IsNullOrEmpty(_opt.SplashSub))
        {
            prims.Add(new HudPrim
            {
                Kind = HudPrimKind.Text, Text = _opt.SplashSub, Align = HudAlign.Center,
                FontSize = font * 1.15, Color = Fade(_opt.Color, alpha * 0.95),
                Points = { (cx, y + h / 2 + font * 1.35) },
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

    /// <summary>
    /// 编队辅助：友机三角标在航向带**上方**（标在带上会和刻度数字打架）。
    /// 超出航向带可见范围（±25°）就退化成端头指向箭头，提示往哪边转能看到他。
    /// 这里没有高度差：map_obj 不含 z 坐标，单机拿不到，只能等数据链。
    /// </summary>
    private void AddFormationAssist(List<HudPrim> prims, TelemetrySnapshot s, double cx, double cy,
                                    double pxDeg, double line, double fontSmall)
    {
        if (_opt.Friendlies.Count == 0) return;

        double y = cy - _headingOffsetDeg * pxDeg;
        double span = HeadingHalfSpanDeg * pxDeg;
        double pxPerDeg = 0.24 * pxDeg;          // 与 AddHeadingTape 的刻度比例必须一致
        var col = _opt.FriendlyColor;

        int shown = 0;
        foreach (var f in _opt.Friendlies)
        {
            if (shown >= 4) break;

            double d = f.RelBearingDeg;
            while (d > 180) d -= 360;
            while (d < -180) d += 360;

            double x = cx + d * pxPerDeg;
            bool onTape = Math.Abs(x - cx) <= span;

            if (onTape)
            {
                prims.Add(new HudPrim
                {
                    Kind = HudPrimKind.Polyline, Width = line, Color = col,
                    Points =
                    {
                        (x, y - 1.0 * pxDeg), (x - 0.55 * pxDeg, y - 1.65 * pxDeg),
                        (x + 0.55 * pxDeg, y - 1.65 * pxDeg), (x, y - 1.0 * pxDeg),
                    },
                });
                prims.Add(new HudPrim
                {
                    Kind = HudPrimKind.Text, Text = FormatRange(f.RangeM), Align = HudAlign.Center,
                    FontSize = fontSmall, Color = col, Points = { (x, y - 2.4 * pxDeg) },
                });
            }
            else
            {
                double dir = d > 0 ? 1 : -1;
                double ex = cx + dir * span;
                prims.Add(new HudPrim
                {
                    Kind = HudPrimKind.Polyline, Width = line, Color = col,
                    Points =
                    {
                        (ex - dir * 0.5 * pxDeg, y - 1.6 * pxDeg), (ex, y - 1.05 * pxDeg),
                        (ex - dir * 0.5 * pxDeg, y - 0.5 * pxDeg),
                    },
                });
                prims.Add(new HudPrim
                {
                    Kind = HudPrimKind.Text, Text = FormatRange(f.RangeM),
                    Align = dir > 0 ? HudAlign.Right : HudAlign.Left,
                    FontSize = fontSmall, Color = col,
                    Points = { (ex - dir * 0.75 * pxDeg, y - 2.4 * pxDeg) },
                });
            }
            shown++;
        }
    }

    /// <summary>编队看的是百米级间距，所以 1 km 以内直接显示米（取整到 10 m）。</summary>
    private static string FormatRange(double metres)
        => metres < 1000 ? ((int)(Math.Round(metres / 10.0) * 10)).ToString()
                         : (metres / 1000.0).ToString("F1") + "km";

    /// <summary>同上，但带单位。距离行和「远距例外」行共用，避免两处取整规则漂移。</summary>
    private static string FormatRangeShort(double metres)
        => metres < 1000 ? ((int)(Math.Round(metres / 10.0) * 10)).ToString() + "m"
                         : (metres / 1000.0).ToString("F1") + "km";

    /// <summary>
    /// 编队模式的三个最小读数：速度 / 高度 / 航向，放在画面下方，避开视野中心。
    /// 数值在上、单位在下，两行一组。
    /// </summary>
    /// <summary>
    /// 编队引导：准星正下方，三行，按"先方向、再距离、最后动作"排。
    /// <para>编队时眼睛大部分时间在窗外，扫一眼就要能照做，所以方向用最大字号，
    /// 距离/接近率用辅助字号，加减速只在需要时出现（平时不占视觉）。</para>
    /// </summary>
    private void AddFormationGuidance(List<HudPrim> prims, TelemetrySnapshot s, double cx, double cy,
                                      double pxDeg, double font, double fontSmall)
    {
        // 长机模式：只报状态，不给任何数字。位置和僚机模式的主行对齐，切换角色时视线不用移。
        if (_opt.LeadStatusMain.Length > 0)
        {
            prims.Add(new HudPrim
            {
                Kind = HudPrimKind.Text, Text = _opt.LeadStatusMain, Align = HudAlign.Center,
                FontSize = font * 1.45, Color = _opt.LeadStatusWarn ? _opt.WarnColor : _opt.Color,
                Points = { (cx, cy + 7.4 * pxDeg) },
            });

            if (_opt.LeadStatusSub.Length > 0)
            {
                prims.Add(new HudPrim
                {
                    Kind = HudPrimKind.Text, Text = _opt.LeadStatusSub, Align = HudAlign.Center,
                    FontSize = fontSmall * 1.3, Color = _opt.WarnColor,
                    Points = { (cx, cy + 8.7 * pxDeg) },
                });
            }

            // 僚机表：贴在速度带右侧、状态行下方。
            // ★ 不设行数上限：具名队形三槽位，但四人以上、外加「数据错误」行时截断会静默少报。
            //   超出屏幕的部分由渲染目标自然裁掉（Screen 上根本画不到那里）。
            if (_opt.LeadWingRows.Count > 0)
            {
                // 从主状态下方起：8.0° 是「大字下沿（8.2）- 半行」，再往下三行到 10.4°，
                // 仍在速度条那一行的左边，不会压上去。
                double rowY = cy + 8.0 * pxDeg;
                double labelX = cx - 10.6 * pxDeg;
                double stateX = cx - 10.2 * pxDeg;
                foreach (var row in _opt.LeadWingRows)
                {
                    prims.Add(new HudPrim
                    {
                        Kind = HudPrimKind.Text, Text = row.Label, Align = HudAlign.Right,
                        FontSize = fontSmall * 0.95, Color = Fade(_opt.Color, 0.85),
                        Points = { (labelX, rowY) },
                    });
                    prims.Add(new HudPrim
                    {
                        Kind = HudPrimKind.Text, Text = row.State, Align = HudAlign.Left,
                        // 到位用绿色，掉队/冲出/偏离用琥珀：一眼分出「谁不用管」
                        FontSize = fontSmall * 0.95,
                        Color = row.Warn ? _opt.WarnColor : Fade(_opt.Color, 0.9),
                        Points = { (stateX, rowY) },
                    });
                    rowY += 1.2 * pxDeg;
                }
            }

            // 长机也画速度条：一句 ADD POWER 说不清「差多少、到哪停」，
            // 目标就是设定马赫换算出的真空速，和僚机用同一套尺。
            if (_opt.LeadSpeedBar is (double curKmh, double tgtKmh, double dvKmh))
                AddSpeedBars(prims, cx, cy + 10.1 * pxDeg, pxDeg, fontSmall, curKmh, tgtKmh, dvKmh,
                             Math.Abs(dvKmh) >= 5);

            // 转向辅助挂在航向带下面：航向带 −12.5°、指针 +1.6°、航向数字 −10.2°，
            // 横带放 −7.6°，标签顶到 −8.95°，与数字留 1.2° 空隙，往下也不碰 FPM。
            // 「在航向指示下面」比放到屏幕下方更直观：两个都是「该往哪」的读数，视线不用来回找。
            if (_opt.TurnAssist is { } ta)
                AddTurnAssistBand(prims, cx, cy - 7.6 * pxDeg, pxDeg, fontSmall, ta);

            return;
        }

        // 数据链收到的长机目标坡度：一行小字，证明广播真的到了。
        // 位置与长机自己的坡度横带同高（−7.6°），两种角色切换时视线不用重新找。
        if (_opt.LeadTargetBankDeg is double leadBank)
        {
            string bankDir = leadBank >= 0 ? "R" : "L";
            prims.Add(new HudPrim
            {
                Kind = HudPrimKind.Text,
                Text = string.Format(L10n.Get(LangScope.Alerts, "guide.leadBank"), bankDir,
                                     Math.Abs(leadBank).ToString("0")),
                Align = HudAlign.Center, FontSize = fontSmall, Color = _opt.WarnColor,
                Points = { (cx, cy - 7.6 * pxDeg) },
            });
        }

        // ---- 垂直/爬升阶段 ----
        // 两阶段：先爬到高度层，横向引导才开始。所以这一段必须在 `Guide is null` 的
        // 早退**之前**画，否则爬升阶段什么都看不到。
        //   Priority（爬升阶段）：只给爬升率，不带米数 —— 米数是给「已经在编队里微调」用的；
        //   AtTarget：高度一致，一小行暗字，不占视觉。
        if (_opt.Vertical is { } v)
        {
            string text;
            if (v.Priority)
            {
                text = (v.ErrorM > 0 ? "\u2191 " : "\u2193 ")
                       + Math.Abs(v.RateMs).ToString("0.0") + " m/s";
            }
            else if (v.AtTarget)
            {
                text = L10n.Get(LangScope.Alerts, "guide.altMatch");
            }
            else
            {
                int altAbs = (int)Math.Round(Math.Abs(v.ErrorM) / 5.0) * 5;
                text = (v.ErrorM > 0 ? "\u2191 " : "\u2193 ") + altAbs.ToString() + " m   "
                       + Math.Abs(v.RateMs).ToString("0.0") + " m/s";
            }
            if (v.Stale) text += "   " + L10n.Get(LangScope.Alerts, "guide.vertStale");

            var vertColor = v.Priority ? _opt.WarnColor
                : v.AtTarget ? Fade(_opt.Color, 0.7) : _opt.FriendlyColor;
            if (v.Stale) vertColor = Fade(vertColor, 0.55);

            prims.Add(new HudPrim
            {
                Kind = HudPrimKind.Text, Text = text, Align = HudAlign.Center,
                // 爬升阶段没有别的元素，这条就是全场唯一读数，给大字号
                FontSize = v.Priority ? fontSmall * 1.9 : fontSmall * 1.1,
                Color = vertColor,
                // 爬升阶段放 9.2°：FPM 圈画在 cy + 迎角（3–6°），6.5° 时大字会和圈的下缘压上
                Points = { (cx, v.Priority ? cy + 9.2 * pxDeg : cy + 13.0 * pxDeg) },
            });

            // 远距例外：被甩开太远时，爬升阶段也放一行距离。
            // 只放距离 —— 圈/航向/油门仍然等高度到位，两阶段的分界不变。
            // 位置 11.5°：大字本体占 8.7–9.7°（字号 1.05°），这里留 1.4° 空隙。
            if (v.Priority && v.FarRangeM is double far)
            {
                double farGapPx = 0.55 * pxDeg;
                prims.Add(new HudPrim
                {
                    Kind = HudPrimKind.Text, Text = FormatRangeShort(far), Align = HudAlign.Right,
                    FontSize = fontSmall * 1.15, Color = Fade(_opt.WarnColor, 0.85),
                    Points = { (cx - farGapPx, cy + 11.5 * pxDeg) },
                });
                prims.Add(new HudPrim
                {
                    Kind = HudPrimKind.Text,
                    Text = "\u2192 " + ((int)Math.Round(v.FarTargetM ?? far)).ToString() + "m",
                    Align = HudAlign.Left, FontSize = fontSmall * 1.15,
                    Color = Fade(_opt.WarnColor, 0.6),
                    Points = { (cx + farGapPx, cy + 11.5 * pxDeg) },
                });
            }
        }

        // ---- 找不到长机：说明是哪一种，别让飞行员对着空屏猜 ----
        if (_opt.FormationHint.Length > 0)
        {
            prims.Add(new HudPrim
            {
                Kind = HudPrimKind.Text, Text = _opt.FormationHint, Align = HudAlign.Center,
                FontSize = font * 1.15, Color = _opt.WarnColor,
                // 7.6°：FPM 最高到 6.7°（迎角上限 6 + 半径 0.7），再往上会贴字
                Points = { (cx, cy + 7.6 * pxDeg) },
            });
        }

        if (_opt.Guide is not { } g) return;

        var col = _opt.Color;

        // ---- 引导圈：把飞行矢量标记（FPM）横移到圈里 ----
        // 纵向必须和 FPM 用同一个基准：FPM 画在 cy + (迎角 + 配平) 处，圈原来钉死在 cy，
        // 巡航迎角 3–5° 时两者天然差半屏，飞行员怎么修都套不上。
        // 上限 6°：大迎角（拉杆过猛）时让圈停住，免得压到下面的文字行。
        if (Math.Abs(g.TurnDeg) >= 0.6)
        {
            double ox = cx + Math.Clamp(g.TurnDeg, -60.0, 60.0) * pxDeg;
            double oy = cy + Math.Clamp(s.AoaDeg + _opt.FpmTrimDeg, -4.0, 6.0) * pxDeg;
            prims.Add(new HudPrim
            {
                Kind = HudPrimKind.Circle, Radius = 0.55 * pxDeg, Width = 1.8, Color = col,
                Points = { (ox, oy) },
            });
        }

        // ---- 编队完成：给长机的「可以开始机动了」信号 ----
        // 只在刚到位那 3 秒出现，之后靠引导继续维持，不常驻（常驻会变成噪音）。
        if (!string.IsNullOrEmpty(_opt.FormationNotice))
        {
            prims.Add(new HudPrim
            {
                Kind = HudPrimKind.Text,
                Text = _opt.FormationNotice,
                Align = HudAlign.Center,
                FontSize = fontSmall * 1.35,
                Color = col,
                Points = { (cx, cy + 2.85 * pxDeg) },
            });
        }

        // ---- 0) 长机转弯：为什么下面的角度比目视看到的大 ----
        // 提前量把机头指向长机的未来位置，转弯时这个数值会比"长机在哪"大十几到二十几度。
        // 不说一句，飞行员会以为引导跳了。只在真的在转弯时出现，平飞完全不占视觉。
        if (g.OverG)
        {
            // 超过阈值：不是"数值为什么大"，而是"跟不住了"—— 直接换成警告，别让飞行员以为在正常引导
            prims.Add(new HudPrim
            {
                Kind = HudPrimKind.Text,
                Text = string.Format(L10n.Get(LangScope.Alerts, "guide.turnGWarn"), g.TurnG),
                Align = HudAlign.Center,
                FontSize = fontSmall * 1.25,
                Color = _opt.WarnColor,
                Points = { (cx, cy + 3.45 * pxDeg) },
            });
        }
        else if (Math.Abs(g.LeadRateDeg) >= 1.5)
        {
            int rate = (int)Math.Round(Math.Abs(g.LeadRateDeg));
            string turnTpl = L10n.Get(LangScope.Alerts, g.LeadRateDeg > 0 ? "guide.leadTurnR" : "guide.leadTurnL");
            prims.Add(new HudPrim
            {
                Kind = HudPrimKind.Text, Text = string.Format(turnTpl, rate), Align = HudAlign.Center,
                FontSize = fontSmall * 1.1,
                // 数据链来的用正常色，本地估算的淡一档 —— 数值可信度不同，别让飞行员以为是同一个精度
                Color = Fade(col, g.LeadFromLink ? 0.85 : 0.55),
                Points = { (cx, cy + 3.45 * pxDeg) },
            });
        }

        // ---- 1) 往哪转：TurnDeg 是相对方位，+ = 友机在右侧 = 右转 ----
        double turn = g.TurnDeg;
        while (turn > 180) turn -= 360;
        while (turn < -180) turn += 360;
        int deg = (int)Math.Round(Math.Abs(turn));

        string dir;
        if (deg <= 3)
        {
            dir = L10n.Get(LangScope.Alerts, "guide.aligned");
        }
        else
        {
            string tpl = L10n.Get(LangScope.Alerts, turn > 0 ? "guide.right" : "guide.left");
            dir = string.Format(tpl, deg);
        }

        prims.Add(new HudPrim
        {
            Kind = HudPrimKind.Text, Text = dir, Align = HudAlign.Center,
            FontSize = font * 1.55, Color = col, Points = { (cx, cy + 7.2 * pxDeg) },
        });

        // ---- 目标速度：合并进速度带 ----
        // 原来这里画一条独立横条（轴线 + 绿点 + 琥珀三角 + 一串数字），占掉中心下方一整行。
        // 现在只把目标速度交给速度带：带子上直接一个琥珀三角，和「现在多快」在同一根尺上，
        // 不用在屏幕两个位置之间来回对。加速度条另有竖条（在带子里，加粗）。
        _opt.FormationTargetKmh = g.CurrentKmh + g.DeltaKmh;
        // ---- 2) 现在多远 → 目标多远 ----
        // 两块分开定位而不是拼一个字符串：连续空格在渲染里会被压缩，拼起来会糊成一坨。
        // 目标距离也画出来：只说"820m"飞行员不知道要追到多少，"820m → 400m"一眼就明白。
        string range = FormatRangeShort(g.RangeM);
        string target = ((int)Math.Round(g.TargetRangeM)).ToString() + "m";

        double gapPx = 0.55 * pxDeg;
        prims.Add(new HudPrim
        {
            Kind = HudPrimKind.Text, Text = range, Align = HudAlign.Right, FontSize = fontSmall * 1.25,
            Color = Fade(col, 0.85), Points = { (cx - gapPx, cy + 10.4 * pxDeg) },
        });
        prims.Add(new HudPrim
        {
            Kind = HudPrimKind.Text, Text = "\u2192 " + target, Align = HudAlign.Left, FontSize = fontSmall * 1.25,
            Color = Fade(col, 0.6), Points = { (cx + gapPx, cy + 10.4 * pxDeg) },
        });

        // ---- 3) 油门：加/减速给数值，稳定时给"保持" ----
        if (g.Action != FormationAction.None)
        {
            bool hold = g.Action == FormationAction.Hold;
            string act = L10n.Get(LangScope.Alerts, hold ? "guide.hold"
                : g.Action == FormationAction.Accelerate ? "guide.accel" : "guide.decel");

            // 差值数字不在这里画了：它跟着本机速度框走（在 AddSpeedTape 里），
            // 「我现在多少、还差多少」必须挨着同一个框，放到下方那一行又变成两处对数字。

            prims.Add(new HudPrim
            {
                Kind = HudPrimKind.Text, Text = act, Align = HudAlign.Center,
                FontSize = fontSmall * 1.15, Color = hold ? col : _opt.WarnColor,
                Points = { (cx, cy + 9.6 * pxDeg) },
            });
        }
    }

    /// <summary>
    /// 长机坡度横带：一条水平刻度带，绿标 = 当前坡度，琥珀标 = 目标坡度，下方一句该压多还是回。
    /// <para>左右各 60°，所以刻度带的「长度」就是坡度本身；不用圆环是因为圆环在长机模式下
    /// 与航向语义冲突（僚机的圈代表该飞的航向）。</para>
    /// </summary>
    private void AddTurnAssistBand(List<HudPrim> prims, double cx, double y, double pxDeg,
                                   double fontSmall, TurnAssistCue cue)
    {
        const double halfDeg = 60.0;
        double half = 9.0 * pxDeg;
        double perDeg = half / halfDeg;

        var dim = Fade(_opt.Color, 0.55);
        var amber = _opt.WarnColor;        // 与「该飞的航向/该到的速度」同一个琥珀
        var green = _opt.Color;            // 与速度条上的「我现在在哪」同一个绿

        // 基线
        prims.Add(new HudPrim
        {
            Kind = HudPrimKind.Line, Width = 1.6, Color = dim,
            Points = { (cx - half, y), (cx + half, y) },
        });

        // 刻度：每 15°，0/±30/±60 稍长
        for (var d = -60; d <= 60; d += 15)
        {
            bool major = d % 30 == 0;
            double h = (major ? 0.62 : 0.4) * pxDeg;
            double x = cx + d * perDeg;
            prims.Add(new HudPrim
            {
                Kind = HudPrimKind.Line, Width = 1.2, Color = Fade(_opt.Color, major ? 0.75 : 0.45),
                Points = { (x, y - h), (x, y + h) },
            });
        }

        // 中止位姿：目标坡度过大时钳到端头，用短箭头提示「不止这么多」
        double Clamp(double deg) => Math.Clamp(deg, -halfDeg, halfDeg);
        double xNow = cx + Clamp(cue.BankNowDeg) * perDeg;
        double xTgt = cx + Clamp(cue.TargetBankDeg) * perDeg;

        // 目标坡度：琥珀三角箭头，尖端指向基线。
        // ★ 原来是一根和「当前坡度」同形状的竖线，只靠颜色和高度区分 —— 扫一眼分不清
        //   哪个是「现在」哪个是「该去的」。换成三角形之后形状本身就是语义。
        double arrowTip = y + 0.9 * pxDeg;
        double arrowBase = y - 0.45 * pxDeg;
        double arrowHalf = 0.55 * pxDeg;
        prims.Add(new HudPrim
        {
            Kind = HudPrimKind.Polyline, Width = 3.2, Color = amber,
            Points =
            {
                (xTgt, arrowTip), (xTgt - arrowHalf, arrowBase),
                (xTgt + arrowHalf, arrowBase), (xTgt, arrowTip),
            },
        });

        // 当前坡度：绿色竖标（更高，一眼看到「我现在在哪」）
        prims.Add(new HudPrim
        {
            Kind = HudPrimKind.Line, Width = 3.6, Color = green,
            Points = { (xNow, y - 1.05 * pxDeg), (xNow, y + 1.05 * pxDeg) },
        });

        // 上图左：目标 G；图右：现在 → 目标
        prims.Add(new HudPrim
        {
            Kind = HudPrimKind.Text, Text = cue.TargetG.ToString("F1") + "G",
            Align = HudAlign.Left, FontSize = fontSmall,
            Color = amber, Points = { (cx - half, y - 1.35 * pxDeg) },
        });
        prims.Add(new HudPrim
        {
            Kind = HudPrimKind.Text,
            Text = Math.Abs(cue.BankNowDeg).ToString("0") + "° → " + Math.Abs(cue.TargetBankDeg).ToString("0") + "°",
            Align = HudAlign.Right, FontSize = fontSmall,
            Color = Fade(_opt.Color, 0.85), Points = { (cx + half, y - 1.35 * pxDeg) },
        });

        // 下：指令。持平时用绿色（说明到位了），否则用琥珀
        string key = cue.Arming ? "guide.turnArming"
            : cue.Action == TurnAssistAction.Hold ? "guide.turnHold"
            : cue.Action == TurnAssistAction.BankMore ? "guide.turnMore" : "guide.turnLess";
        var cueColor = !cue.Arming && cue.Action == TurnAssistAction.Hold ? green : amber;
        prims.Add(new HudPrim
        {
            Kind = HudPrimKind.Text, Text = L10n.Get(LangScope.Alerts, key),
            Align = HudAlign.Center, FontSize = fontSmall * 1.05,
            Color = cueColor, Points = { (cx, y + 1.55 * pxDeg) },
        });
    }

    /// <summary>
    /// 画「我在哪 / 该到哪」的速度条和「推多少油门」的加速度条。
    /// </summary>
    /// <param name="yTop">速度条轴线的纵坐标；数字在 +0.9°、加速度条在 +2.0°。</param>
    /// <param name="currentKmh">本机真空速。&lt;=1 视为未知，不画速度条（加速度条仍可画）。</param>
    /// <param name="targetKmh">目标真空速。轴心就是它。</param>
    /// <param name="deltaKmh">建议的加减速量，决定加速度条上点的位置。</param>
    /// <param name="showAccel">是否画加速度条。僚机传「有引导动作」，长机传「差值够大」——
    /// 由调用方决定，两条路径的判据本来就不一样。</param>
    private void AddSpeedBars(List<HudPrim> prims, double cx, double yTop, double pxDeg, double fontSmall,
                              double currentKmh, double targetKmh, double deltaKmh, bool showAccel)
    {
        var col = _opt.Color;

        // ---- 速度条：轴心 = 目标速度，实心点 = 本机真空速 ----
        // 只给一个「+80」飞行员还得自己心算现在多少、要到多少；给一对绝对标记之后，
        // 「我在哪、该到哪」直接可读，点往轴心靠就说明修对了。
        if (currentKmh > 1)
        {
            double by = yTop;
            // 半宽 2.2°：1.6° 在 1080p 下全长只有 50 px，缺口几十 km/h 时点几乎贴着轴心看不出来。
            double half = 2.2 * pxDeg;
            double frac = FormationBarScale.SpeedFrac(currentKmh, targetKmh);

            // 轴线 + 两端端点：没有端点的横线读不出"量程"，加两笔就成了一把尺
            prims.Add(new HudPrim
            {
                Kind = HudPrimKind.Line, Width = 1.4, Color = Fade(col, 0.55),
                Points = { (cx - half, by), (cx + half, by) },
            });
            foreach (double endX in new[] { cx - half, cx + half })
            {
                prims.Add(new HudPrim
                {
                    Kind = HudPrimKind.Line, Width = 1.4, Color = Fade(col, 0.75),
                    Points = { (endX, by - 0.20 * pxDeg), (endX, by + 0.20 * pxDeg) },
                });
            }
            // 轴心 = 目标速度：琥珀色三角扎在轴上，表示「系统希望到这里」。
            // 原来是根半透明细刻度，和当前点的形状太像，飞行员分不清哪个是"该到"。
            prims.Add(new HudPrim
            {
                Kind = HudPrimKind.Polyline, Width = 1.8, Color = _opt.WarnColor,
                Points =
                {
                    (cx - 0.34 * pxDeg, by - 0.74 * pxDeg),
                    (cx + 0.34 * pxDeg, by - 0.74 * pxDeg),
                    (cx, by - 0.16 * pxDeg),
                    (cx - 0.34 * pxDeg, by - 0.74 * pxDeg),
                },
            });
            // 本机速度：实心块（短粗竖线 —— 渲染器不消费 Filled，画圆只会得到空心环）。
            // 宽度 0.44° 对刻度线的 2 px，形状差异足够大，扫一眼就知道哪个是"现在"。
            prims.Add(new HudPrim
            {
                Kind = HudPrimKind.Line, Width = 0.44 * pxDeg, Color = col,
                Points = { (cx + frac * half, by - 0.30 * pxDeg), (cx + frac * half, by + 0.30 * pxDeg) },
            });

            // 数字：现在 → 目标。两块分开定位，连续空格在渲染里会被压掉。
            double gapV = 0.55 * pxDeg;
            prims.Add(new HudPrim
            {
                Kind = HudPrimKind.Text, Text = ((int)Math.Round(currentKmh)).ToString(),
                Align = HudAlign.Right, FontSize = fontSmall * 1.25,
                Color = Fade(col, 0.90), Points = { (cx - gapV, by + 0.9 * pxDeg) },
            });
            prims.Add(new HudPrim
            {
                Kind = HudPrimKind.Text,
                Text = "\u2192 " + ((int)Math.Round(targetKmh)).ToString() + " km/h",
                Align = HudAlign.Left, FontSize = fontSmall * 1.25,
                Color = Fade(col, 0.60), Points = { (cx + gapV, by + 0.9 * pxDeg) },
            });
        }

        // ---- 加速度条：中点 = 不用动油门，向右 = 该加速、向左 = 该减速 ----
        if (showAccel)
        {
            double frac = FormationBarScale.AccelFrac(deltaKmh);
            double ay = yTop + 2.0 * pxDeg;
            double half = 1.9 * pxDeg;
            // 轴线 + 两端端点（和速度条同一套尺的读法）
            prims.Add(new HudPrim
            {
                Kind = HudPrimKind.Line, Width = 1.4, Color = Fade(col, 0.55),
                Points = { (cx - half, ay), (cx + half, ay) },
            });
            foreach (double endX in new[] { cx - half, cx + half })
            {
                prims.Add(new HudPrim
                {
                    Kind = HudPrimKind.Line, Width = 1.4, Color = Fade(col, 0.75),
                    Points = { (endX, ay - 0.18 * pxDeg), (endX, ay + 0.18 * pxDeg) },
                });
            }
            // 中点刻度（0 位）
            prims.Add(new HudPrim
            {
                Kind = HudPrimKind.Line, Width = 2.0, Color = Fade(col, 0.70),
                Points = { (cx, ay - 0.22 * pxDeg), (cx, ay + 0.22 * pxDeg) },
            });
            // 指示块：琥珀三角，尖端指向轴线。
            // ★ 原来是短竖线，和速度条上的绿色实心块形状几乎一样，全靠颜色区分；
            //   换成三角之后「该去哪」和「现在在哪」形状不同，扫一眼就分得开。
            //   形状与速度条的目标三角一致（都是朝下），整套界面只有一种琥珀含义。
            double tx = cx + frac * half;
            prims.Add(new HudPrim
            {
                Kind = HudPrimKind.Polyline, Width = 1.8, Color = _opt.WarnColor,
                Points =
                {
                    (tx - 0.34 * pxDeg, ay - 0.74 * pxDeg),
                    (tx + 0.34 * pxDeg, ay - 0.74 * pxDeg),
                    (tx, ay - 0.10 * pxDeg),
                    (tx - 0.34 * pxDeg, ay - 0.74 * pxDeg),
                },
            });
        }
    }

    /// <summary>编队模式下加速度条的线宽倍数：比普通模式粗，因为周围没有别的参照物。</summary>
    private const double FormationAccelWidth = 2.4;

    /// <summary>
    /// 编队模式的读数：速度和高度用**和普通模式一样的竖带**，加速度条加粗。
    /// <para>原来是横排三个数字（TAS / ALT / HDG），省地方但不好用 —— 数字要读，带子扫一眼
    /// 就知道自己快慢高低，和长机比也更直观。航向不再单独给：航向带顶部本来就显示当前值。</para>
    /// <para>带子在外侧，中心区域仍然留给引导，和原来「视野中心不放东西」的原则不冲突。</para>
    /// </summary>
    private void AddFormationReadout(List<HudPrim> prims, TelemetrySnapshot s, double cx, double cy,
                                     double pxDeg, double line, double font, double fontSmall)
    {
        // useTas: 编队引导按真空速算，带子必须同一口径，否则目标三角和引导数字对不上
        AddSpeedTape(prims, s, cx, cy, pxDeg, line, font, fontSmall,
                     FormationAccelWidth, _opt.FormationTargetKmh, useTas: true, amberAccelZero: true);
        AddAltTape(prims, s, cx, cy, pxDeg, line, font, fontSmall);
    }

    // -------------------------------------------------------------------- tapes

    /// <param name="accelScale">
    /// 加速度竖条的线宽倍数。普通模式 1.0；编队模式传更大值 —— 那边视野里没有别的东西，
    /// 细线看不清，但带子本身的线宽不能跟着变粗，否则和普通模式长得不一样了。
    /// </param>
    /// <param name="targetKmh">
    /// 目标速度。给了就在带子上画一个琥珀三角（位置就在那个速度上）——
    /// 编队时「我该飞多快」和「我现在多快」必须落在同一根尺上才比得出来。
    /// </param>
    /// <param name="useTas">
    /// true 用真空速、false 用指示空速。编队引导按 TAS 算（长机按马赫飞），
    /// 所以编队模式必须用 TAS，否则目标三角和引导给的数字对不上。
    /// </param>
    private void AddSpeedTape(List<HudPrim> prims, TelemetrySnapshot s, double cx, double cy, double pxDeg,
        double line, double font, double fontSmall, double accelScale = 1.0,
        double? targetKmh = null, bool useTas = false, bool amberAccelZero = false)
    {
        double x = cx - _tapeOffsetDeg * pxDeg;
        double span = TapeHalfSpanDeg * pxDeg;
        double pxPerKmh = 1.5 * pxDeg / 50.0;   // 50 km/h per 1.5 deg
        var col = _opt.Color;
        double shownKmh = useTas ? s.TasKmh : s.IasKmh;

        prims.Add(new HudPrim { Kind = HudPrimKind.Line, Width = line, Color = col, Points = { (x, cy - span), (x, cy + span) } });

        double startV = Math.Floor((shownKmh - span / pxPerKmh) / 50) * 50;
        for (double v = startV; v <= shownKmh + span / pxPerKmh; v += 50)
        {
            if (v < 0) continue;
            double y = cy + (shownKmh - v) * pxPerKmh;
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
        AddBoxedText(prims, x - 0.6 * pxDeg, cy, shownKmh.ToString("0"), font, HudAlign.Right,
            overspeed ? _opt.DangerColor : col, line, pxDeg);

        // 差值贴在**本机速度框**正下方，按框的**左边缘**左对齐。
        // ★ 原来右对齐到框的右边缘，数字紧贴速度带，会和刻度短横线挤在一起，
        //   负号那一位也容易被带子压掉；左对齐后整行落在框下、远离带子。
        // 按 10 km/h 取整 —— 编队时「加 80」够用，没人照着个位数修油门。
        if (targetKmh is double tgt2 && tgt2 > 1)
        {
            int dvShow = (int)(Math.Round((tgt2 - shownKmh) / 10.0) * 10);
            if (dvShow != 0)
            {
                string curText = shownKmh.ToString("0");
                double boxLeft = (x - 0.6 * pxDeg) - BoxWidthDeg(curText, font);
                prims.Add(new HudPrim
                {
                    Kind = HudPrimKind.Text,
                    Text = (dvShow > 0 ? "+" : "-") + Math.Abs(dvShow).ToString(),
                    Align = HudAlign.Left, FontSize = fontSmall * 0.95,
                    Color = Fade(_opt.WarnColor, 0.9),
                    Points = { (boxLeft, cy + 1.55 * pxDeg) },
                });
            }
        }

        // 目标速度：带子左侧一个朝右的琥珀三角，尖端贴在带上。
        // 左边离刻度数字（-2.6°）还有余量，右边要让给加速度条。
        if (targetKmh is double tgt && tgt > 1)
        {
            double tY = cy + (shownKmh - tgt) * pxPerKmh;
            bool offTape = Math.Abs(tY - cy) > span;
            tY = Math.Clamp(tY, cy - span, cy + span);
            double tipX = x - 0.35 * pxDeg;
            double baseX = x - 1.15 * pxDeg;
            double halfH = 0.5 * pxDeg;
            prims.Add(new HudPrim
            {
                Kind = HudPrimKind.Polyline, Width = 2.0,
                // 出量程时**不变暗**：它是唯一的目标指示，钳在端头已经说明「还在更外面」，
                // 再压暗就等于看不见了。改由文字那边补一句具体差多少。
                Color = _opt.WarnColor,
                Points = { (tipX, tY), (baseX, tY - halfH), (baseX, tY + halfH), (tipX, tY) },
            });
        }

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
        double accW = line * accelScale;
        // 零位基准的颜色：编队模式用琥珀（绿色在那边统一表示「实际值」，基准得是另一个颜色），
        // 普通模式保持原来的淡绿 —— 那边绿色本来就同时扮演刻度和基准，改了反而多出一种颜色。
        prims.Add(new HudPrim
        {
            Kind = HudPrimKind.Line, Width = accW,
            Color = amberAccelZero ? Fade(_opt.WarnColor, 0.9) : Fade(col, 0.55),
            Points = { (accBarX - 0.25 * pxDeg, cy), (accBarX + 0.25 * pxDeg, cy) },
        });
        if (accLen > 0.15 * pxDeg)
        {
            double dir = _opt.AccelMs2 >= 0 ? -1 : 1;
            prims.Add(new HudPrim
            {
                Kind = HudPrimKind.Line, Width = accW, Color = Fade(col, 0.85),
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

        // ---- 目标航向：琥珀色三角（绿色三角 = 我现在朝哪，琥珀 = 系统希望我朝哪）----
        // 画在带上方、标签下方那一窄条：不压刻度数字，也不会和当前航向三角混在一起。
        if (_opt.TargetHeadingDeg is double tgtHdg)
        {
            double rel = tgtHdg - hdg;
            while (rel > 180) rel -= 360;
            while (rel < -180) rel += 360;
            double hx = cx + rel * pxPer10Deg / 10.0;
            if (Math.Abs(hx - cx) <= span)
            {
                prims.Add(new HudPrim
                {
                    Kind = HudPrimKind.Polyline, Width = 1.8, Color = _opt.WarnColor,
                    Points =
                    {
                        (hx - 0.42 * pxDeg, y - 0.80 * pxDeg),
                        (hx + 0.42 * pxDeg, y - 0.80 * pxDeg),
                        (hx, y - 0.12 * pxDeg),
                        (hx - 0.42 * pxDeg, y - 0.80 * pxDeg),
                    },
                });
            }
        }
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

    /// <summary>
    /// 带框读数的框宽（像素口径与渲染器一致）。抽出来是因为「本机速度框下方的差值」
    /// 必须知道框的左边缘在哪 —— 两处各写一份公式，改字号时必然错位。
    /// </summary>
    private double BoxWidthDeg(string text, double font)
        => Math.Max(font * 2.0, TextWidthEm(text) * font + font * 0.35);

    private void AddBoxedText(List<HudPrim> prims, double x, double y, string text, double font, HudAlign align,
        HudColor color, double line, double pxDeg)
    {
        double w = BoxWidthDeg(text, font);
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











































































