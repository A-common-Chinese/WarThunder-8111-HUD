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

    // ---- 僚机侧位（编队位置设定）----
    // 僚机主动声明飞在长机左侧还是右侧，引导就把机头指向「长机侧后方的虚拟点」而不是长机本体。
    // 有了这个稳定设定，长机转弯时才能确定僚机在内圈还是外圈：
    // 右转圆心在长机右侧 → 右侧 = 内圈、左侧 = 外圈；左转反之。
    // 0 = 正后方（旧行为，不做内外圈区分），默认编队不受影响。

    /// <summary>僚机侧位：-1 = 长机左侧，0 = 正后方（默认），+1 = 长机右侧。</summary>
    [JsonPropertyName("formationWingSide")] public int FormationWingSide { get; set; } = 0;

    // ---- 站位间距（2026-11-11 从代码常量改成配置项） ----

    /// <summary>
    /// 尾随站位：长机正后方的距离（米）。默认 400。
    /// <para>★ 改大之后「编队完成」的判定（40 m 绝对误差）会相对变严，因为它是绝对米数，
    /// 不是百分比 —— 站位放到 800 m 时建议把判定一起放宽。</para>
    /// </summary>
    [JsonPropertyName("formationRangeM")] public double FormationRangeM { get; set; } = 400;

    /// <summary>
    /// 侧位僚机的横向偏移（米）。默认 150。左僚机 −150、右僚机 +150。
    /// </summary>
    [JsonPropertyName("formationWingOffsetM")] public double FormationWingOffsetM { get; set; } = 150;

    /// <summary>
    /// 侧位僚机的纵向后距（米）。默认 370。
    /// <para>与 <see cref="FormationWingOffsetM"/> 一起决定斜距：默认 150² + 370² ≈ 400²，
    /// 三个站位到长机的距离都是 400 m —— 这样切换侧位时速度前馈不用重新标定。
    /// 改了这两个数就不再相等，属于有意为之的话没问题。</para>
    /// </summary>
    [JsonPropertyName("formationStationBackM")] public double FormationStationBackM { get; set; } = 370;

    // ---- 状态机与保持死区（2026-11-11 从代码常量改成配置项） ----

    /// <summary>
    /// 「编队完成」的距离误差门槛（米）。默认 40。★ 这是**绝对米数**，不是百分比：
    /// 站位放到 800 m 时 40 m 只相当于 5%，比 400 m 时的 10% 严一倍，改站位记得同步放宽这里。
    /// </summary>
    [JsonPropertyName("formationDoneM")] public double FormationDoneM { get; set; } = 40;

    /// <summary>进入门槛需要持续多久才算完成（秒）。默认 2.0，防止掠过站位就报完成。</summary>
    [JsonPropertyName("formationDoneHoldS")] public double FormationDoneHoldS { get; set; } = 2.0;

    /// <summary>
    /// 掉出「保持」的误差门槛（米）。默认 120。
    /// <para>必须明显大于 <see cref="FormationDoneM"/>，否则会在门槛附近反复切换状态。</para>
    /// </summary>
    [JsonPropertyName("formationDropM")] public double FormationDropM { get; set; } = 120;

    /// <summary>
    /// 保持阶段的转向死区（度）：偏差小于它就按「已对准」显示，指针不再动。默认 5。
    /// <para>400 m 上 5° ≈ 35 m 横向。</para>
    /// </summary>
    [JsonPropertyName("formationSteadyDeg")] public double FormationSteadyDeg { get; set; } = 5;

    /// <summary>保持阶段的速度死区（km/h）：偏差小于它就显示「保持」。默认 20。</summary>
    [JsonPropertyName("formationSteadyKmh")] public double FormationSteadyKmh { get; set; } = 20;

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
        try
        {
            var path = PathFor();
            if (File.Exists(path))
            {
                var cfg = JsonSerializer.Deserialize<HudConfig>(File.ReadAllText(path));
                if (cfg != null) return cfg;
            }
        }
        catch { }
        return new HudConfig();
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

    public HudConfig Clone() => (HudConfig)MemberwiseClone();

    /// <summary>Copies every writable property from <paramref name="other"/> (used by CANCEL).</summary>
    public void CopyFrom(HudConfig other)
    {
        foreach (var p in typeof(HudConfig).GetProperties())
        {
            if (!p.CanRead || !p.CanWrite) continue;
            p.SetValue(this, p.GetValue(other));
        }
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
}
