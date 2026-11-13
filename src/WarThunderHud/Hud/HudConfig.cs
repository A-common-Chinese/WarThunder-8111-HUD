// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 A-common-Chinese
// This file is part of War Thunder HUD (8111), licensed under the GNU AGPL v3.0 or later.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace WtHud.Hud;

/// <summary>
/// User-tunable HUD calibration, persisted next to the executable.
/// Everything here is meant to be dialed in live with the hotkeys and then saved (F12),
/// because the game exposes no camera FOV and its attitude fields do not share our datum.
/// </summary>
public sealed class HudConfig
{
    [JsonPropertyName("verticalFovDeg")] public double VerticalFovDeg { get; set; } = 45.0;
    [JsonPropertyName("layoutOffsetYDeg")] public double LayoutOffsetYDeg { get; set; } = 1.8;

    /// <summary>Added to the displayed pitch; F7 sets it so the ladder reads 0 while level.</summary>
    [JsonPropertyName("pitchZeroDeg")] public double PitchZeroDeg { get; set; }

    /// <summary>
    /// Native pitch sign: 0 = auto detect from flight data, +1 / -1 = force it. Some aircraft
    /// (F/A-18E, MiG-19) report the horizon pitch inverted.
    /// </summary>
    [JsonPropertyName("pitchSign")] public int PitchSign { get; set; }

    /// <summary>Per-vehicle native pitch sign learned automatically (vehicle id -> +/-1).</summary>
    /// <summary>Per-airframe overrides: vehicle -> setting name -> value (survives restarts).</summary>
    [JsonPropertyName("perVehicleOverrides")]
    public Dictionary<string, Dictionary<string, double>> PerVehicleOverrides { get; set; } = new();


    /// <summary>+1 or -1; flips the bank direction if the ladder rotates the wrong way.</summary>
    [JsonPropertyName("bankSign")] public int BankSign { get; set; } = 1;

    /// <summary>Small bank trim in degrees.</summary>
    [JsonPropertyName("bankTrimDeg")] public double BankTrimDeg { get; set; }

    /// <summary>
    /// Extrapolate speed/altitude between samples to hide the 17-25 Hz update cap of the
    /// local API. Attitude is never extrapolated - prediction overshoot on the ladder was
    /// worse than the raw stepping.
    /// </summary>
    [JsonPropertyName("extrapolate")] public bool Extrapolate { get; set; }

    // ---- key bindings (passthrough polling, the game still gets every key) ----
    [JsonPropertyName("viewKey")] public string ViewKey { get; set; } = "V";
    [JsonPropertyName("zoomKey")] public string ZoomKey { get; set; } = "Z";
    [JsonPropertyName("mapKey")] public string MapKey { get; set; } = "M";
    [JsonPropertyName("scoreboardKey")] public string ScoreboardKey { get; set; } = "TAB";

    /// <summary>Settings dialog key.</summary>
    [JsonPropertyName("settingsKey")] public string SettingsKey { get; set; } = "F1";
    /// <summary>Pitch-zero calibration key.</summary>
    [JsonPropertyName("pitchZeroKey")] public string PitchZeroKey { get; set; } = "F7";
    /// <summary>Bank-flip key.</summary>
    [JsonPropertyName("bankFlipKey")] public string BankFlipKey { get; set; } = "F8";
    /// <summary>Body-fixed mode key.</summary>
    [JsonPropertyName("bodyFixedKey")] public string BodyFixedKey { get; set; } = "F9";
    /// <summary>Show / hide HUD key.</summary>
    [JsonPropertyName("showKey")] public string ShowKey { get; set; } = "F10";
    /// <summary>Exit key (press twice within 0.7 s).</summary>
    [JsonPropertyName("exitKey")] public string ExitKey { get; set; } = "F11";
    /// <summary>Save config key.</summary>
    [JsonPropertyName("saveKey")] public string SaveKey { get; set; } = "CTRL+F12";   // plain F12 is taken by Steam screenshots
    /// <summary>Cursor-hole cycle key.</summary>
    [JsonPropertyName("cursorHoleKey")] public string CursorHoleKey { get; set; } = "CTRL+ALT+H";

    /// <summary>Custom key: reset the internal view / map state (default F5).</summary>
    [JsonPropertyName("resetKey")] public string ResetKey { get; set; } = "F5";

    /// <summary>Long-press this key to hide the HUD (default U).</summary>
    [JsonPropertyName("holdHideKey")] public string HoldHideKey { get; set; } = "U";

    /// <summary>
    /// 编队解除 / 恢复键（按一下切换）。解除后不再画本轮引导，飞行员可以自由飞；
    /// 再按一下重新建立，之后重新判定到位。默认 CTRL+SHIFT+F。
    /// </summary>
    [JsonPropertyName("formationKey")] public string FormationKey { get; set; } = "CTRL+SHIFT+F";

    /// <summary>Height tape shows radar (above-ground) altitude instead of barometric.</summary>
    [JsonPropertyName("tapeRadarAltitude")] public bool TapeRadarAltitude { get; set; } = true;

    /// <summary>Flip the map Y axis when converting to bearings/grid (depends on the map).</summary>
    [JsonPropertyName("mapYFlip")] public bool MapYFlip { get; set; } = true;   // map image Y grows southwards

    /// <summary>Warn when closer than this fraction of the map size to the map edge.</summary>
    [JsonPropertyName("edgeMarginFraction")] public double EdgeMarginFraction { get; set; } = 0.06;

    /// <summary>Hide the whole HUD while the aircraft sits on the ground.</summary>
    [JsonPropertyName("hideOnGround")] public bool HideOnGround { get; set; } = true;

    /// <summary>Delay (ms) before the HUD hides after the aircraft stops on the ground.</summary>
    [JsonPropertyName("groundHideDelayMs")] public double GroundHideDelayMs { get; set; } = 500;

    /// <summary>Below this IAS (km/h) the aircraft counts as stopped for the ground hide.</summary>
    [JsonPropertyName("groundHideSpeedKmh")] public double GroundHideSpeedKmh { get; set; } = 5;

    /// <summary>Blink the enemy-airfield readout when closer than this many km.</summary>
    [JsonPropertyName("enemyAirfieldAlertKm")] public double EnemyAirfieldAlertKm { get; set; } = 12;

    /// <summary>
    /// Airbrake key. Used by the A-10 family only: their split-aileron (deceleron) brakes make the
    /// reported percentage follow the ailerons, so the box follows the key instead.
    /// </summary>
    [JsonPropertyName("airbrakeKey")] public string AirbrakeKey { get; set; } = "H";

    /// <summary>Stall warning key: records the current AOA as this vehicle''s stall AOA.</summary>
    [JsonPropertyName("stallKey")] public string StallKey { get; set; } = "CTRL+ALT+S";

    /// <summary>Generic stall AOA used before a per-vehicle calibration exists (deg).</summary>
    [JsonPropertyName("stallAoaDeg")] public double StallAoaDeg { get; set; } = 22;

    /// <summary>Per-vehicle calibrated stall AOA, keyed by the API vehicle id.</summary>
    [JsonPropertyName("stallAoaByVehicle")] public System.Collections.Generic.Dictionary<string, double> StallAoaByVehicle { get; set; } = new();

    /// <summary>AOA above this (deg) blinks the AOA warning.</summary>
    [JsonPropertyName("aoaWarnDeg")] public double AoaWarnDeg { get; set; } = 18;

    /// <summary>Distance limit (km) for the nearest friendly SPAA readout. 0 = hidden.</summary>
    [JsonPropertyName("friendlySpaaKm")] public double FriendlySpaaKm { get; set; } = 60;

    /// <summary>Endurance below this (seconds) blinks the BINGO fuel warning.</summary>
    [JsonPropertyName("bingoFuelSeconds")] public double BingoFuelSeconds { get; set; } = 180;

    /// <summary>Sign of the lateral flight-path offset: -1 mirrors the AoS-based marker.</summary>
    [JsonPropertyName("fpmAosSign")] public int FpmAosSign { get; set; } = -1;

    /// <summary>Show the pitch ladder.</summary>
    [JsonPropertyName("showPitchLadder")] public bool ShowPitchLadder { get; set; } = true;

    /// <summary>Show the flight path marker (velocity vector).</summary>
    [JsonPropertyName("showFpm")] public bool ShowFpm { get; set; } = true;

    /// <summary>编队辅助：在航向带上标出友机方位与距离（只友军，按 color 过滤）。</summary>
    [JsonPropertyName("formationAssist")] public bool FormationAssist { get; set; }

    /// <summary>编队模式：隐藏原 HUD 其余元素，只留友机信息 + 速度/高度/航向三个读数。</summary>
    [JsonPropertyName("formationOnly")] public bool FormationOnly { get; set; }

    // ---- 编队引导的级联 PID（外环 PI → 期望接近率，内环 P → 油门） ----
    // 三个参数可以在这里直接改，改完重启生效。默认值是仿真扫参的结果：
    // 1.6 km 追尾约 32 秒到位（纯 P 的旧逻辑要 80~95 秒），且交叉接近场景不劣化。
    //
    // 外环的比例增益是**分段**的（增益调度）：
    //   距离误差大 → formationKp（小，慢慢追，不给自己挖坑）
    //   逼近 400 m → formationKpNear（大，快速收尾，不然末段磨叽）
    // 中间按 formationBlendM 线性过渡，是连续函数，没有切换点突变。
    //
    // ★ 外环输出的"期望接近率"始终被夹在 -20…+30 m/s，这是安全底线：
    //   放开它会在远距离索要 100 m/s 以上的接近率，物理上刹不住（实测交叉场景需要 10 km）。

    /// <summary>外环比例（远景）：距离误差大时用的增益。越大追得越急。</summary>
    [JsonPropertyName("formationKp")] public double FormationKp { get; set; } = 0.04;

    /// <summary>外环比例（近景）：逼近目标距离时用的增益。加大到 0.2 能把末段收尾快一倍。</summary>
    [JsonPropertyName("formationKpNear")] public double FormationKpNear { get; set; } = 0.20;

    /// <summary>增益过渡的距离尺度（m）：误差超过它就完全用远景增益，到 0 时完全用近景增益。</summary>
    [JsonPropertyName("formationBlendM")] public double FormationBlendM { get; set; } = 800;

    /// <summary>外环积分：消除残余偏差。过大容易冲过头。</summary>
    [JsonPropertyName("formationKi")] public double FormationKi { get; set; } = 0.006;

    /// <summary>内环比例：期望接近率与实际接近率之差放大成油门量。想更跟手就加大。</summary>
    [JsonPropertyName("formationKv")] public double FormationKv { get; set; } = 1.0;

    // ---- 转弯提前量（lead pursuit）----
    // 纯追位（指向长机"当前位置"）在转弯时注定追错：长机 10°/s 右转、间距 400 m、600 km/h，
    // 需要的提前角是 asin(ω·R/V) ≈ 25°，而引导门槛只把 ±3° 当"对准"。
    // 打开后转向建议指向长机的**未来位置**，队形不会在转弯中被甩到外圈。
    // 转向率优先取数据链（长机自报，准且无滞后），没有就退化为相对方位的变化率反推。

    /// <summary>转弯提前量开关。关掉就回到"指向长机当前位置"的纯追位。</summary>
    [JsonPropertyName("formationLeadFeed")] public bool FormationLeadFeed { get; set; } = true;

    /// <summary>启用提前量所需的最小转向率（°/s）。低于它提前角不到 4°，加了只会让数字乱跳。</summary>
    [JsonPropertyName("formationLeadMinRate")] public double FormationLeadMinRate { get; set; } = 1.5;

    /// <summary>提前角上限（度）。防止低速大转向率时 asin 顶到 90° 把机头指到侧面去。</summary>
    [JsonPropertyName("formationLeadMaxDeg")] public double FormationLeadMaxDeg { get; set; } = 45;

    /// <summary>
    /// 超载告警阈值（G）。长机转弯的横向过载超过它，就在引导上方提示「转弯过急」。
    /// <para>用 G 而不是 °/s，因为它不随速度漂移：600 km/h 下 1.5 G ≈ 5.1 °/s，
    /// 400 km/h 下同样 1.5 G 只需要六成的转弯率，但两种情况队形保持的难度相当。</para>
    /// <para>依据（sim 实测，600 km/h）：3 °/s（0.87 G）外圈/内圈与理想站位只差 **6–10 m**；
    /// 6.7 °/s（2.0 G）内圈横位已丢 **144 m**（切到长机正后方）；10 °/s（2.97 G）差 55–79 m；
    /// 15 °/s（5.9 G）外圈直接脱离。退化不是线性的 —— 2 G 之后断崖。
    /// 所以取 **1.5 G（≈5.1 °/s）**：在明显跑偏**之前**提醒，而不是等队形散了才说。</para>
    /// </summary>
    [JsonPropertyName("formationWarnG")] public double FormationWarnG { get; set; } = 1.5;

    // ---- 站位数值（2026-11-13 统一模型）----
    // 四个数值是唯一真相：侧位定左右，横偏/后距/高度定位置。
    // 「预设队形」只是把这四个值一次填好，不再是一套独立的几何表。

    /// <summary>僚机侧位：-1 = 长机左侧，0 = 正后方（默认），+1 = 长机右侧。**永远生效。**</summary>
    [JsonPropertyName("formationWingSide")] public int FormationWingSide { get; set; } = 0;

    /// <summary>
    /// 横向偏移（米）。默认 150。左僚机 −150、右僚机 +150；侧位为正后时会乘 0，这一项不起作用。
    /// </summary>
    [JsonPropertyName("formationWingOffsetM")] public double FormationWingOffsetM { get; set; } = 150;

    /// <summary>
    /// 纵向后距（米）。默认 400。**所有侧位共用这一个后距。**
    /// <para>★ 0.4.20 及以前尾随位另有一个「尾随距离」，两套后距只有一套生效 ——
    /// 与「槽位 / 侧位」是同一类毛病，统一模型里合并掉了。</para>
    /// </summary>
    [JsonPropertyName("formationStationBackM")] public double FormationStationBackM { get; set; } = 400;

    /// <summary>站位高度偏移（米，正 = 站位比长机高）。默认 0。任何形状都读这一项。</summary>
    [JsonPropertyName("formationUpM")] public double FormationUpM { get; set; }

    // ---- 预设队形（只是「填数按钮」）----

    /// <summary>
    /// 队形标签：custom / echelon / column / diamond。
    /// <para>★ 0.4.21 起它只是**标签**，不再决定几何 —— 几何永远来自上面四个数。
    /// 手工改任一数值会立刻把标签改回 custom。</para>
    /// </summary>
    [JsonPropertyName("formationShape")] public string FormationShape { get; set; } = "custom";

    /// <summary>
    /// 僚机编号（0 起）。**只是编号**：报文第 11 段带它，长机表据此显示 W1/W2/W3，不参与几何。
    /// </summary>
    [JsonPropertyName("formationSlot")] public int FormationSlot { get; set; } = 0;

    /// <summary>梯队基准间距（米）。默认 450：每架再后 450、再左 250、再高 25，按比例整体缩放。</summary>
    [JsonPropertyName("formationEchelonSpacingM")] public double FormationEchelonSpacingM { get; set; } = 450;

    /// <summary>纵队基准间距（米）。默认 400：正后方每架间隔 400。</summary>
    [JsonPropertyName("formationColumnSpacingM")] public double FormationColumnSpacingM { get; set; } = 400;

    /// <summary>菱形基准间距（米）。默认 400：左右后各 300/400，后位 0/600 并抬高 30。</summary>
    [JsonPropertyName("formationDiamondSpacingM")] public double FormationDiamondSpacingM { get; set; } = 400;

    // ---- 自定义快照：套预设前存一份，切回自定义时原样还原 ----

    [JsonPropertyName("formationCustomSet")] public bool FormationCustomSet { get; set; }
    [JsonPropertyName("formationCustomWingSide")] public int FormationCustomWingSide { get; set; }
    [JsonPropertyName("formationCustomWingOffsetM")] public double FormationCustomWingOffsetM { get; set; } = 150;
    [JsonPropertyName("formationCustomStationBackM")] public double FormationCustomStationBackM { get; set; } = 400;
    [JsonPropertyName("formationCustomUpM")] public double FormationCustomUpM { get; set; }

    /// <summary>配置模型版本：0 = 0.4.20 及以前，2 = 统一模型。用来只迁移一次。</summary>
    [JsonPropertyName("formationModel")] public int FormationModel { get; set; }

    /// <summary>
    /// 0.4.20 及以前的「尾随距离」，只在迁移时读一次；迁移完置空，不再落盘。
    /// </summary>
    [JsonPropertyName("formationRangeM")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? LegacyFormationRangeM { get; set; }

    /// <summary>
    /// 垂直优先（两阶段）：高度没到位时**只给爬升率**，圈 / 航向 / 距离 / 油门全关。
    /// **默认关**（0.4.24 起）。
    /// <para>关着的时候爬升阶段照样给全套横向引导 —— 只有距离、不知道往哪转，飞行员反馈更难受。
    /// 打开它回到「先爬高度、别分心」的老行为，适合大高度差同时还要修位置的时候。</para>
    /// </summary>
    [JsonPropertyName("formationVerticalFirst")] public bool FormationVerticalFirst { get; set; }

    /// <summary>
    /// 垂直引导的「算到位」门槛（米）。默认 30：不到这个误差就按「高度一致」显示，不再给升降建议。
    /// </summary>
    [JsonPropertyName("formationVertGateM")] public double FormationVertGateM { get; set; } = 30;

    /// <summary>
    /// 垂直引导的升降率上限（m/s）。默认 10。
    /// <para>不限幅的话，差 300 m 时会给出飞机做不到的爬升率，指令永远「做不到」。</para>
    /// </summary>
    [JsonPropertyName("formationVertMaxMs")] public double FormationVertMaxMs { get; set; } = 10;

    /// <summary>
    /// 爬升阶段（高度未到位）要不要显示距离行，单位米。**默认 0 = 任何距离都显示。**
    /// <para>0.4.22 之前这是「远距例外」，默认 2000：只有被甩开 2 km 才破例显示。
    /// 现在默认常显 —— 爬升时看不见距离，飞行员根本不知道自己正在被甩开。</para>
    /// <para>★ **-1 = 什么都不显示**（严格两阶段）；0 = 任何距离都显示；正数 = 超过它才显示。</para>
    /// </summary>
    [JsonPropertyName("formationFarRangeM")] public double FormationFarRangeM { get; set; }

    // ---- 状态机与保持死区（2026-11-11 从代码常量改成配置项） ----

    /// <summary>
    /// 「编队完成」的距离误差门槛，**按槽位斜距的百分比**。默认 10。
    /// <para>★ 2026-11-12 从绝对米数改成百分比：原来 40 m 在 400 m 槽位是 10%，
    /// 到了梯队第 3 位（1545 m）只相当于 2.6%，越往外越难判定。改成百分比后每个槽位松紧一致。</para>
    /// </summary>
    [JsonPropertyName("formationDonePct")] public double FormationDonePct { get; set; } = 10;

    /// <summary>进入门槛需要持续多久才算完成（秒）。默认 2.0，防止掠过站位就报完成。</summary>
    [JsonPropertyName("formationDoneHoldS")] public double FormationDoneHoldS { get; set; } = 2.0;

    /// <summary>
    /// 掉出「保持」的误差门槛，同样是槽位斜距的百分比。默认 30。
    /// <para>必须明显大于 <see cref="FormationDonePct"/>，否则会在门槛附近反复切换状态。</para>
    /// </summary>
    [JsonPropertyName("formationDropPct")] public double FormationDropPct { get; set; } = 30;

    /// <summary>
    /// 站位方位容差（度）：距离对上、速度也对，但站歪了同样不算到位。
    /// <para>★ 只看距离会出现「正后方 500 m」和「左后 45° 500 m」都判到位 ——
    /// 前者压根没在槽位上。这个角是「当前视线」到「理想站位」的夹角，
    /// 站准时为 0，所以它同时是完成判定和「保持」判定的第二个维度。</para>
    /// </summary>
    [JsonPropertyName("formationAlignDeg")] public double FormationAlignDeg { get; set; } = 5;

    /// <summary>
    /// 保持阶段的转向死区（度）：偏差小于它就按「已对准」显示，指针不再动。默认 5。
    /// <para>400 m 上 5° ≈ 35 m 横向。</para>
    /// </summary>
    [JsonPropertyName("formationSteadyDeg")] public double FormationSteadyDeg { get; set; } = 5;

    /// <summary>保持阶段的速度死区（km/h）：偏差小于它就显示「保持」。默认 20。</summary>
    [JsonPropertyName("formationSteadyKmh")] public double FormationSteadyKmh { get; set; } = 20;

    // ---- 长机转向辅助（2026-11-12）----
    // 长机按一下键，HUD 按「预定 G」给出目标坡度；再按一下关掉。
    // 只提示不控杆：它把「该压多少度」数字化，平滑仍然取决于人的输入。

    /// <summary>转向辅助开关的按键。默认 CTRL+SHIFT+T（与解除引导的 CTRL+SHIFT+F 分开）。</summary>
    [JsonPropertyName("formationTurnAssistKey")] public string FormationTurnAssistKey { get; set; } = "CTRL+SHIFT+T";

    /// <summary>
    /// 目标过载（G）。默认 3.0，范围 1.2–9.0。
    /// <para>换算成目标坡度 φ = acos(1/n) 与目标转向率 ω = g·√(n²−1)/V：
    /// 坡度只跟 G 有关，转向率还跟速度有关。所以主提示给坡度，转向率只作副行。</para>
    /// </summary>
    [JsonPropertyName("formationTurnTargetG")] public double FormationTurnTargetG { get; set; } = 3.0;

    /// <summary>容差（G）：实际过载与目标的差在这个范围内就显示「保持」。默认 0.3。</summary>
    [JsonPropertyName("formationTurnTolG")] public double FormationTurnTolG { get; set; } = 0.3;

    /// <summary>把目标坡度通过数据链广播给僚机（僚机可据此提前预判长机要转多少）。默认开。</summary>
    [JsonPropertyName("formationTurnBroadcast")] public bool FormationTurnBroadcast { get; set; } = true;

    /// <summary>
    /// 僚机速度前馈的上限（相对长机真空速的倍数）。外圈路径更长，按 |ω|·(r_w−ρ) 补速。
    /// <para>默认 **1.30**。2026-11-11 试过放宽到 1.60：15°/s 外圈确实从 R=730 收到 486，
    /// 但 10°/s 内圈的横位从 +71 翻成 −77（越过站位切到内侧，误差 79 m → 227 m），
    /// 而且 10°/s 是本系统最常用的档位 —— 所以放上限这一刀最后没有采纳，保持 1.30。</para>
    /// <para>要真正改善大转弯率的外圈，得先改前馈公式（按站位点半径而不是僚机当前位置算），
    /// 单靠放宽倍数不行。详见 <c>.ai/formation-redesign.md</c>。</para>
    /// </summary>
    [JsonPropertyName("formationWingMaxRatio")] public double FormationWingMaxRatio { get; set; } = 1.30;

    /// <summary>
    /// 僚机速度前馈的下限（相对长机真空速的倍数）。内圈要减速。
    /// <para>**保持 0.80**（同日试过 0.50，10°/s 内圈从 cross +71 翻成 −78，减过头切到站位内侧，
    /// 所以下限不能跟着上限一起放）。要覆盖 20°/s 内圈（需 0.69×）得先把速度前馈改成按
    /// **站位点半径**算 —— 现在用的是当前距离，放大包线会同时放大这个近似误差。</para>
    /// <para>★ 这是相对长机的倍数，不是绝对速度：长机飞得慢时 0.8× 也可能贴近失速。</para>
    /// </summary>
    [JsonPropertyName("formationWingMinRatio")] public double FormationWingMinRatio { get; set; } = 0.80;

    /// <summary>
    /// 油门建议的一阶低通时间常数（秒）。越小加速/减速响应越快。
    /// <para>复评后收回 1.2 s：外圈改纯追踪后不再需要激进响应，放宽反而平白超速。
    /// 太小会让 HUD 上的油门数字来回跳，别低于 0.2。</para>
    /// </summary>
    [JsonPropertyName("formationGuideTauS")] public double FormationGuideTauS { get; set; } = 1.2;

    // ---- 长机 / 僚机 ----

    /// <summary>
    /// 长机图标（map_obj 的 <c>icon</c>：Fighter / Bomber / Assault / …）。留空 = 认「离我最近的友军」。
    /// <para>僚机靠最近友军认长机，战场上多一架友军就会被带偏。填了图标就只认该类别的飞机。</para>
    /// <para>★ 这是**类别**不是具体机型：两架都填 Fighter 区分不了；测试时让长机换个大类（例如轰炸机）。</para>
    /// </summary>
    [JsonPropertyName("leadIcon")] public string LeadIcon { get; set; } = "";

    /// <summary>
    /// 长机模式。关 = 僚机（默认，追到长机后方 400 m）；开 = 长机，保持设定航向与马赫数，
    /// 只报状态不给数字 —— 长机不该被引导去追僚机。
    /// <para>打开 <c>formationOmniFollow</c> 后航向不再锁定，改为跟随长机实时航向。</para>
    /// </summary>
    [JsonPropertyName("leadMode")] public bool LeadMode { get; set; }

    /// <summary>
    /// 全向跟随模式（仅长机模式有效）。关 = 按 <c>leadHeadingDeg</c> 指定航向直飞、偏离就提示转回；
    /// 开 = 不锁航向，长机可全向机动，始终以长机实时航向为准
    /// （有数据链用长机自报值，没有就用本机航向近似）。
    /// 速度不受影响，两种模式都按 <c>leadMach</c> 设定。
    /// </summary>
    [JsonPropertyName("formationOmniFollow")] public bool FormationOmniFollow { get; set; }

    /// <summary>
    /// 长机的设定航向（度）。**-1 表示留空**：在打开编队模式那一刻锁存当时的航向，
    /// 之后偏离就提醒回来。
    /// </summary>
    [JsonPropertyName("leadHeadingDeg")] public double LeadHeadingDeg { get; set; } = -1;

    /// <summary>长机的设定马赫数。用马赫而不是空速，是因为它不随高度变（喷气机习惯）。</summary>
    [JsonPropertyName("leadMach")] public double LeadMach { get; set; } = 0.7;

    // ---- 数据链（高度 / 航向 / 转向率） ----

    /// <summary>
    /// 数据链开关。位置不需要共享（单机拿到的友机位置已经准到 1–2 m），
    /// 但 `map_obj` 没有 z，**高度只能靠这条链路**。
    /// 航向 / 转向率有链路时直接用长机自报值，没有就退化为本地近似（实测差别不大）。默认关。
    /// </summary>
    [JsonPropertyName("dataLinkEnabled")] public bool DataLinkEnabled { get; set; }

    /// <summary>
    /// 本机的数据链节点 ID。**0 表示还没分配** —— 首次启用数据链时自动生成一个 6 位数并存进
    /// 配置文件，以后一直沿用。两台机器撞号概率极低，撞了会收到"自己"的包而被检测到。
    /// </summary>
    [JsonPropertyName("dataLinkId")] public int DataLinkId { get; set; }

    /// <summary>
    /// 手动指定「谁是长机」：填对方的节点 ID。默认 0 = 自动。
    /// <para>自动规则是「声称自己是长机（role=1）的那个节点」；三方以上编队、或者两台都开了
    /// 长机模式时，自动会分不清，这一行就是强制手段。</para>
    /// <para>填了就必须能收到这个 ID 的包，否则僚机侧会一直显示「没认出长机」——
    /// 这是有意的：宁可明说认不出，也不要拿别人的高度顶替。</para>
    /// </summary>
    [JsonPropertyName("dataLinkLeadId")] public int DataLinkLeadId { get; set; }

    /// <summary>
    /// 数据链单播目标，形如 <c>["192.168.16.7:48017"]</c>。为空 = 子网广播。
    /// <para>★ 广播地址 255.255.255.255 **出不了路由器**：两台机器不在同一网段时
    /// （内网穿透 / VPN 组网 / 公网中转）必须在这里填对方的地址，并在链路上放通 UDP 48017。</para>
    /// <para>可以填多个，每个目标都会发一份；某个目标不可达不影响其它目标。</para>
    /// </summary>
    [JsonPropertyName("dataLinkPeers")] public List<string> DataLinkPeers { get; set; } = new();

    /// <summary>
    /// 面板上的「对端地址」只有一行文本，配置里是列表 —— 这两个方法就是那层转换。
    /// <para>分隔符接受逗号、分号、空格，重复项自动去掉；<c>null</c>/空串 = 清空 = 回到广播。
    /// 抽成静态纯函数是为了能在 selftest 里断言往返一致，不用真的开面板。</para>
    /// </summary>
    internal static string PeersToText(IReadOnlyList<string> peers) => string.Join(", ", peers);

    internal static List<string> TextToPeers(string? text)
    {
        var list = new List<string>();
        if (string.IsNullOrWhiteSpace(text)) return list;
        foreach (var part in text.Split(new[] { ',', ';', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
        {
            string s = part.Trim();
            if (s.Length == 0 || list.Contains(s)) continue;
            list.Add(s);
        }
        return list;
    }

    /// <summary>Hold the right mouse button to enter the aiming layout.</summary>
    [JsonPropertyName("aimOnRightMouse")] public bool AimOnRightMouse { get; set; } = true;

    /// <summary>
    /// Multiplies the measured fuel flow before computing endurance. The game shows its own
    /// FM-table estimate, which runs ~1.25-1.3x higher than the measured mass flow at altitude;
    /// set this to match the in-game readout.
    /// </summary>
    [JsonPropertyName("enduranceScale")] public double EnduranceScale { get; set; } = 1.00;

    /// <summary>
    /// Half-size (px) of the transparent hole at screen centre that lets the game draw its cursor.
    /// Smaller = less HUD occlusion near the boresight; 0 disables the hole.
    /// </summary>
    /// <summary>Cursor-hole offset from the screen centre (pixels, manual placement).</summary>
    [JsonPropertyName("cursorHoleOffsetX")] public int CursorHoleOffsetX { get; set; }
    [JsonPropertyName("cursorHoleOffsetY")] public int CursorHoleOffsetY { get; set; }

    [JsonPropertyName("cursorHoleHalfPx")] public int CursorHoleHalfPx { get; set; } = 16;   // 32 px hole: hides the game cursor, keeps the FPM ring visible

    /// <summary>Mach number above which the speed readout turns red (manual mode).</summary>
    [JsonPropertyName("overspeedMach")] public double OverspeedMach { get; set; } = 1.80;

    /// <summary>IAS above which the speed readout turns red (km/h). No Vne in the API.</summary>
    [JsonPropertyName("overspeedKmh")] public double OverspeedKmh { get; set; } = 1400;

    /// <summary>下降率告警阈值（m/s，正值；0 = 用内置默认 40）。机型可单独覆盖。</summary>
    [JsonPropertyName("sinkRateMs")] public double SinkRateMs { get; set; } = 40;

    /// <summary>PULL UP 提前量（秒；TTI 小于该值触发；默认 5，0 = 用内置默认）。机型可覆盖。</summary>
    [JsonPropertyName("pullUpTtiS")] public double PullUpTtiS { get; set; } = 5.0;

    /// <summary>击落播报（SPLASH）：自己击落敌机时在 HUD 上播报。</summary>
    [JsonPropertyName("splashEnabled")] public bool SplashEnabled { get; set; } = true;

    /// <summary>自己的游戏昵称（用于从 /hudmsg 判断「自己击落」；8111 没有可靠的自我标识）。</summary>
    [JsonPropertyName("playerName")] public string PlayerName { get; set; } = "";

    /// <summary>Hide the HUD while the cockpit view is active.</summary>
    [JsonPropertyName("hideInCockpit")] public bool HideInCockpit { get; set; } = false;

    /// <summary>Hide the HUD in the nose / gunner view (V twice). Off by default.</summary>
    [JsonPropertyName("hideInNose")] public bool HideInNose { get; set; }

    /// <summary>Hide the HUD without quitting (same as F10).</summary>
    [JsonPropertyName("hudVisible")] public bool HudVisible { get; set; } = true;

    // 2026-11-10 移除 hideWhenInactive：以前"后台是否隐藏叠加层"是可选项，关掉后覆盖层会一直留在
    // 游戏那块显示器上，而顶层窗口在部分合成状态下会把整片区域的点击吃掉（见 OverlayWindow.
    // SetVisible 的注释），实测表现是"游戏那块屏什么都点不了"。现在隐藏是无条件行为
    // （Program.cs 的 wantVisible），这个开关没有合法用法，字段一起删掉。
    // 老配置里残留的 "hideWhenInactive" 会被 System.Text.Json 当未知字段忽略，不报错，下次 SAVE 自动消失。

    /// <summary>Show raw gear/flaps/airbrake values and flags for diagnosis.</summary>
    [JsonPropertyName("configDebug")] public bool ConfigDebug { get; set; }

    /// <summary>How much of the aircraft roll the camera performs (0 = horizon-stabilised chase cam).</summary>
    /// <summary>Camera roll coupling mode: "auto" (follow the current view), "off", "on".</summary>
    [JsonPropertyName("rollCouplingMode")] public string RollCouplingMode { get; set; } = "auto";

    [JsonPropertyName("rollCoupling")] public double RollCoupling { get; set; }

    /// <summary>Extra vertical trim for the flight path marker, degrees.</summary>
    [JsonPropertyName("fpmTrimDeg")] public double FpmTrimDeg { get; set; }

    private static string PathFor() =>
        System.IO.Path.Combine(AppContext.BaseDirectory, "hud-config.json");

    public static HudConfig Load()
    {
        var path = PathFor();
        if (!File.Exists(path))
        {
            // 首次运行：没有文件就是默认，正常。也走一遍迁移，把快照与模型版本初始化好。
            var fresh = new HudConfig();
            fresh.MigrateFormation();
            return fresh;
        }

        try
        {
            var cfg = JsonSerializer.Deserialize<HudConfig>(File.ReadAllText(path));
            if (cfg != null)
            {
                // 旧语义（0.4.20 及以前）只迁移一次；迁移前把原文件留一份 .bak，
                // 因为这一步会重写四个站位数值，用户没法从面板上看出改过什么。
                if (cfg.FormationModel < 3)
                {
                    // 只有改几何的那一次（<2）需要留 .bak；只是改默认值不需要
                    if (cfg.FormationModel < 2)
                    {
                        try { File.Copy(path, path + ".bak", overwrite: true); } catch { }
                    }
                    cfg.MigrateFormation();
                    // 迁移结果立刻落盘：否则每次启动都会重跑一遍
                    cfg.SaveTo(path);
                }
                return cfg;
            }
            // 解出 null 不等于「没有配置」，是文件本身有问题，不能当成默认继续跑
            throw new System.IO.InvalidDataException("deserialised to null");
        }
        catch (Exception ex)
        {
            // ★ 这里原来是 catch { }：解析失败就静默返回默认值。上层紧接着会因为
            //   DataLinkId == 0 去分配节点 ID 并 Save()，于是**把用户整份配置写回默认** ——
            //   现象就是「重启之后设置全变回去了」，而且没有任何提示，查都没法查。
            //   现在：原文件先留一份 .bad，再把失败原因喊出来。
            try { File.Copy(path, path + ".bad", overwrite: true); } catch { }
            Console.WriteLine($"config load FAILED ({ex.GetType().Name}: {ex.Message})");
            Console.WriteLine("  original kept as hud-config.json.bad; this session runs on DEFAULTS");
            Console.WriteLine("  -> saving from the panel will overwrite hud-config.json");
            return new HudConfig();
        }
    }

    /// <summary>
    /// 把 0.4.20 及以前的配置迁到统一模型。**只跑一次**（跑完写 <c>formationModel = 2</c>）。
    /// <para>旧语义下具名队形的三个距离是死数据（真正生效的是槽位表），尾随位用的还是另一个
    /// 「尾随距离」。迁移后：具名队形按预设重写四个值（看到的站位不变）；自定义按当前侧位
    /// 挑一个后距（尾随位取尾随距离，侧位取侧位后距），也不改变当前站位。</para>
    /// </summary>
    public void MigrateFormation()
    {
        if (FormationModel < 2)
        {
            if (!WtHud.Hud.FormationGeometry.IsKnownShape(FormationShape))
                FormationShape = WtHud.Hud.FormationGeometry.ShapeCustom;

            if (FormationShape != WtHud.Hud.FormationGeometry.ShapeCustom)
            {
                // 具名队形：旧版几何来自槽位表，直接按预设写回四个值
                WtHud.Hud.FormationGeometry.ApplyPreset(this, FormationShape, FormationSlot);
                SnapshotCustomDefaults();
            }
            else
            {
                // 自定义：把旧的「尾随距离」并进唯一的后距，保持当前侧位看到的距离不变
                if (FormationWingSide == 0 && LegacyFormationRangeM is double r && r > 50)
                    FormationStationBackM = r;
                SnapshotCustom();
            }

            LegacyFormationRangeM = null;
            FormationModel = 2;
        }

        // 3：爬升阶段的「远距例外」默认从 2000 改成 0（严格两阶段）。
        // 只改没动过的 2000 —— 自己调过别的值说明是有意为之，不碰。
        if (FormationModel < 3)
        {
            if (Math.Abs(FormationFarRangeM - 2000.0) < 1e-9) FormationFarRangeM = 0;
            FormationModel = 3;
        }
    }

    /// <summary>把当前四个值存成「自定义快照」。</summary>
    public void SnapshotCustom()
    {
        FormationCustomSet = true;
        FormationCustomWingSide = FormationWingSide;
        FormationCustomWingOffsetM = FormationWingOffsetM;
        FormationCustomStationBackM = FormationStationBackM;
        FormationCustomUpM = FormationUpM;
    }

    /// <summary>
    /// 首次迁移用具名队形时，手上没有「用户原来的自定义值」，快照写真后 400 这一套默认，
    /// 免得切回自定义时还原出一堆 0。
    /// </summary>
    private void SnapshotCustomDefaults()
    {
        FormationCustomSet = true;
        FormationCustomWingSide = 0;
        FormationCustomWingOffsetM = 150;
        FormationCustomStationBackM = 400;
        FormationCustomUpM = 0;
    }

    /// <summary>把「自定义快照」写回四个值；没有快照时保持原样。</summary>
    public void RestoreCustom()
    {
        if (!FormationCustomSet) return;
        FormationWingSide = FormationCustomWingSide;
        FormationWingOffsetM = FormationCustomWingOffsetM;
        FormationStationBackM = FormationCustomStationBackM;
        FormationUpM = FormationCustomUpM;
    }

    public void Save() => SaveTo(PathFor());

    /// <summary>Writes the config to an explicit path (used by the standalone scratch panel).</summary>
    public void SaveTo(string path)
    {
        try
        {
            File.WriteAllText(path,
                JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    /// <summary>
    /// 面板用的草稿副本。
    /// <para>★ <c>MemberwiseClone</c> 只复制引用，所以集合属性必须单独深拷贝 —— 否则
    /// <c>_work.PerVehicleOverrides</c> 和 <c>_cfg.PerVehicleOverrides</c> 是**同一个字典**，
    /// 而 <see cref="SettingsRouter.Set"/> 是就地写 <c>map[name] = value</c>，于是
    /// 面板里改任何一个「按机型」的设置都会直接写进正在生效的配置，绕过 SAVE。
    /// 这类问题不会报错，只会表现为「没点保存也生效了」。</para>
    /// </summary>
    public HudConfig Clone()
    {
        var clone = (HudConfig)MemberwiseClone();
        clone.CopyReferenceTypesFrom(this);
        return clone;
    }

    /// <summary>Copies every writable property from <paramref name="other"/> (used by CANCEL).</summary>
    public void CopyFrom(HudConfig other)
    {
        foreach (var p in typeof(HudConfig).GetProperties())
        {
            if (!p.CanRead || !p.CanWrite) continue;
            // 单个属性赋值失败不该让整次 CANCEL 停在半路：那种情况下 _work 会变成
            // 一半新一半旧的配置，比恢复失败更难查。
            try { p.SetValue(this, p.GetValue(other)); }
            catch { }
        }
        // 反射只搬引用，集合要单独深拷贝，否则源和目的从此共用一份数据
        CopyReferenceTypesFrom(other);
    }

    /// <summary>
    /// 深拷贝所有引用类型属性。新增引用类型属性时必须在这里补一行 ——
    /// selftest 的 "clone shares no reference property" 会在漏掉时变红。
    /// <para>★ 这里只处理 HudConfig 的直接属性。嵌套对象（<see cref="LanguageConfig"/>、
    /// <see cref="MfdSettings"/>）目前只有 string / bool 字段，浅拷贝等价于深拷贝；
    /// 一旦给它们加集合，它们的 <c>Clone()</c> 也要一起改成深拷贝。</para>
    /// </summary>
    private void CopyReferenceTypesFrom(HudConfig src)
    {
        PerVehicleOverrides = new Dictionary<string, Dictionary<string, double>>(src.PerVehicleOverrides.Count);
        foreach (var kv in src.PerVehicleOverrides)
            PerVehicleOverrides[kv.Key] = new Dictionary<string, double>(kv.Value);

        StallAoaByVehicle = new Dictionary<string, double>(src.StallAoaByVehicle);
        DataLinkPeers = new List<string>(src.DataLinkPeers);
        Language = src.Language.Clone();
        Mfd = src.Mfd.Clone();
    }

    // ---------------------------------------------------------------- language

    /// <summary>
    /// Language of the three surfaces: panel = silk-screen on the bezel, screen = MFD text,
    /// alerts = texts drawn on the HUD. Defaults: panel/alerts English, screen Chinese.
    /// </summary>
    [JsonPropertyName("language")] public LanguageConfig Language { get; set; } = new();

    /// <summary>MFD panel appearance and input behaviour.</summary>
    [JsonPropertyName("mfd")] public MfdSettings Mfd { get; set; } = new();
}

/// <summary>Persisted language selection (one language per surface).</summary>
public sealed class LanguageConfig
{
    [JsonPropertyName("followSystem")] public bool FollowSystem { get; set; }
    [JsonPropertyName("panel")] public string Panel { get; set; } = "en";
    [JsonPropertyName("screen")] public string Screen { get; set; } = "zh";
    [JsonPropertyName("alerts")] public string Alerts { get; set; } = "en";

    /// <summary>字段全是 string / bool，MemberwiseClone 即深拷贝。加集合时要一起改。</summary>
    public LanguageConfig Clone() => (LanguageConfig)MemberwiseClone();

}

/// <summary>MFD panel appearance/behaviour settings.</summary>
public sealed class MfdSettings
{
    /// <summary>Normal text color preset: white / green / purple / blue / red.</summary>
    public string InkColor { get; set; } = "white";

    /// <summary>Highlight (accent) color preset: white / green / purple / blue / red.</summary>
    public string AccentColor { get; set; } = "green";

    /// <summary>Realistic mode: screen rows can only be driven by the physical keys.</summary>
    public bool RealisticKeys { get; set; }

    /// <summary>字段全是 string / bool，MemberwiseClone 即深拷贝。加集合时要一起改。</summary>
    public MfdSettings Clone() => (MfdSettings)MemberwiseClone();
}
