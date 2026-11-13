// SPDX-License-Identifier: AGPL-3.0-or-later



// Copyright (C) 2026 A-common-Chinese
// This file is part of War Thunder HUD (8111), licensed under the GNU AGPL v3.0 or later.

using System.Globalization;
using WtHud.Telemetry;
using WtHud.Hud;
using WtHud.Graphics;
using WtHud.Overlay;

namespace WtHud;

public static partial class Program
{
    /// <summary>
    /// 屏幕上显示的版本号：去掉 <c>+&lt;commit&gt;</c> 后缀。
    /// <see cref="Application.ProductVersion"/> 取的是 AssemblyInformationalVersion，SDK 会自动拼上
    /// 40 位 commit（0.4.0+cd56ea85a768…），在 1330 宽的题头里比标题还长，两个会贴在一起。
    /// 构建号留给日志和文件属性，题头与 DIAG 页只要 0.4.0。
    /// </summary>
    public static string ShortVersion =>
        Application.ProductVersion.Split('+')[0];

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
            case "dlprobe":
                return DataLinkProbe(args.Length > 1 ? args[1] : "",
                                     args.Length > 2 ? args[2] : "");
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
            case "ahrslog":
                return AhrsLog(args.Length > 1 ? double.Parse(args[1], CultureInfo.InvariantCulture) : 120.0);
            case "formationsim":
                return FormationSim(args);
            default:
                Console.WriteLine("usage: WtHud                       # start the HUD overlay (default)\n         WtHud overlay [seconds] [demo]\n         WtHud probe [seconds] | replay <file> [speed] | preview <out.png> [w] [h] [variant] | selftest | fusiontest | formationsim | dumpgeo");
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

    /// <summary>
    /// 姿态数据记录：按 20 Hz 把「原始量 + 估算结果」打成 CSV，用于离线比对算法。
    /// <para>为什么单独做一个模式：阵风这类没有罗盘的机型，坡度只能靠积分 Wx，
    /// 而地图位置差分（<c>CourseDeg</c>）其实也能反推航向。两条路的误差特性不同，
    /// 到底谁的权重该多大，必须有同一段飞行的两条曲线才能定 —— 靠肉眼看不出来。</para>
    /// <para>用法：进对局后 <c>WtHud.exe ahrslog 120 &gt; ahrs.csv</c>，飞完把 CSV 发回来。</para>
    /// </summary>
    private static int AhrsLog(double seconds)
    {
        var client = new TelemetryClient(25);
        TelemetrySnapshot? last = null;
        int frames = 0;
        client.SnapshotReceived += s => { last = s; frames++; };
        client.Start();

        var map = new WtHud.Telemetry.MapData();
        map.Start();

        var fusion = new WtHud.Telemetry.AttitudeFusion();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        int seen = 0;
        long warnedAt = -10_000;

        Console.WriteLine("t,tas_kmh,alt_m,vy_ms,aoa_deg,ny_g,wx_dps,has_hdg,hdg_deg,course_deg,pitch_native,bank_native,pitch_est,bank_est,conf,src");

        while (clock.Elapsed.TotalSeconds < seconds)
        {
            Thread.Sleep(40);
            // 按「收到几帧」去重而不是按时间戳：游戏时钟只有 0.1 s 分辨率，
            // 同一时刻会连出好几条，按时间戳去重会漏掉一半样本。
            if (last is null || frames == seen) continue;
            seen = frames;

            // 没有自机坐标就没有 course，整段数据白飞 —— 每 5 秒喊一次，别等落地才发现
            if (map.CourseDeg is null && clock.ElapsedMilliseconds - warnedAt > 5000)
            {
                warnedAt = clock.ElapsedMilliseconds;
                Console.Error.WriteLine("!! map_obj 里没有自机坐标（试飞/无对局/地图未加载），course 列会一直是空的");
            }

            var sol = fusion.Update(last);
            static string N(double? v, string fmt = "F1") => v is double d ? d.ToString(fmt, CultureInfo.InvariantCulture) : "";
            static string F(double v) => v.ToString("F1", CultureInfo.InvariantCulture);

            Console.WriteLine(string.Join(",",
                F(last.ElapsedSeconds),
                F(last.TasKmh),
                F(last.AltM),
                F(last.VyMs),
                F(last.AoaDeg),
                F(last.NyG),
                F(last.RollRateDegS),
                last.HasHeading ? "1" : "0",
                last.HasHeading ? F(last.HeadingDeg) : "",
                N(map.CourseDeg),
                N(last.PitchDeg),
                N(last.BankDeg),
                N(sol.PitchDeg),
                N(sol.BankDeg),
                sol.Confidence.ToString("F2", CultureInfo.InvariantCulture),
                sol.Source.ToString()));
        }

        map.Dispose();
        client.Dispose();
        Console.Error.WriteLine($"logged {seconds:F0}s, {seen} frames, last t={last?.ElapsedSeconds ?? double.NaN:F1}s, " +
                                $"course={(map.CourseDeg is null ? "none" : "ok")}");
        return last is null ? 1 : 0;
    }

    /// <summary>
    /// 从（已按机型过滤过的）友机里挑长机：取**最近**的那架。
    /// <para>机型只能区分类别（同为 Fighter 的两架分不开），所以同型多架时靠距离决胜负 ——
    /// 离我最近的那架才是我实际能编队的那架。抽成函数是为了能断言，
    /// 免得以后有人把「取列表第一个」改成别的顺序（列表默认是分析顺序，不是距离序）。</para>
    /// <para>返回 -1 表示没有可选目标（找不到长机，引导整段不画）。</para>
    /// </summary>
    /// <summary>
    /// 从配对结果里找出「长机是第几架友机」。<paramref name="leadNodeIndex"/> 是长机在节点快照里的下标，
    /// 配不上（长机不在本机地图友机里、或位置超出容差）就退回 <paramref name="fallback"/>（最近的友机）。
    /// <para>抽成纯函数是为了能断言：这一步错了，三机编队的圈会指向别人。</para>
    /// </summary>
    internal static int LeadFriendlyIndex(IReadOnlyList<int?> nodeForFriendly, int leadNodeIndex, int fallback)
    {
        if (leadNodeIndex >= 0)
            for (int i = 0; i < nodeForFriendly.Count; i++)
                if (nodeForFriendly[i] == leadNodeIndex) return i;
        return fallback;
    }

    internal static int NearestFriendlyIndex(System.Collections.Generic.IReadOnlyList<FriendlyContact> list)
    {
        int best = -1;
        for (int i = 0; i < list.Count; i++)
            if (best < 0 || list[i].RangeM < list[best].RangeM) best = i;
        return best;
    }

    /// <summary>
    /// 按机型设置的作用域自检：每台机型只读自己的覆盖值（不能串台）、优先级 覆盖 &gt; 档案 &gt; 全局。
    /// 曾经的问题：详情页用 Scoped.Get（跟着面板当前编辑的机型走）读值，导致 j16 的数据覆盖 f4j 的显示；
    /// 且运行时根本没读 perVehicleOverrides，这些「按机型」参数实际不生效。
    /// </summary>
    /// <summary>
    /// 双机编队仿真（真实级联 PID）：直线 / 右转 / 左转 × 侧位设定（0 / 左 / 右）。
    /// <para>方向引导复用 WingAimDeg / IsInsideTurn / FormationLeadAheadDeg；油门复用
    /// UpdateClosingRate / SmoothGuideDelta / ResetGuideState + HudConfig 默认增益，
    /// 与 UpdateNavigation 是同一套公式、增益和限幅。</para>
    /// <para>输出 2×2 俯视 XY 轨迹 PNG；数字摘要写同目录 .txt 供复核，不向控制台打表。</para>
    /// </summary>
    private static int FormationSim(string[] args)
    {
        const double vKmh = 600, dt = 0.25, maxTurnDegPerS = 20;
        // 仿真时长：默认 30 s。WT_SIM_SEC=45 可延长，用来判断「没编上」是真追不上还是窗口不够
        double totalS = 30;
        if (double.TryParse(Environment.GetEnvironmentVariable("WT_SIM_SEC"),
                            NumberStyles.Any, CultureInfo.InvariantCulture, out double secEnv))
            totalS = Math.Clamp(secEnv, 10, 120);
        double vMs = vKmh / 3.6;
        int steps = (int)(totalS / dt);
        // 三档转向率，默认直线 / 右转 10 / 左转 10（°/s）。
        // WT_SIM_RATES="0,20,-20" 可在不改代码的情况下压高转速，用来检查极限下会不会发散。
        double[] rates = { 0, 10, -10 };
        string? rateSpec = Environment.GetEnvironmentVariable("WT_SIM_RATES");
        if (!string.IsNullOrWhiteSpace(rateSpec))
        {
            var parsed = rateSpec.Split(',', StringSplitOptions.RemoveEmptyEntries)
                                 .Select(x => double.Parse(x.Trim(), CultureInfo.InvariantCulture))
                                 .ToArray();
            if (parsed.Length == 3 && parsed[0] == 0) rates = parsed;
            else Console.WriteLine("WT_SIM_RATES ignored: need 3 values, first must be 0");
        }
        string[] scenarios = { "STRAIGHT", $"TURN R {rates[1]:0} deg/s", $"TURN L {Math.Abs(rates[2]):0} deg/s" };
        var cfg = new HudConfig();
        // 遍历哪些站位：自定义队形 = 原来三个（尾随/左/右），具名队形 = 它的槽位表。
        // 这样 1G/2G 的历史数据和以前逐字可比，同时新队形也能跑同一套仿真。
        // 垂直通道：WT_SIM_VERT=0 关掉；WT_SIM_VYMAX 升降率限幅（默认 10 m/s）；
        // WT_SIM_ALT0 僚机起始高度差（默认 −120 m，即偏低 120 m）
        bool altLoop = Environment.GetEnvironmentVariable("WT_SIM_VERT") != "0";
        double vyMaxMs = 10;
        if (double.TryParse(Environment.GetEnvironmentVariable("WT_SIM_VYMAX"),
                            NumberStyles.Any, CultureInfo.InvariantCulture, out double vmaxEnv))
            vyMaxMs = Math.Abs(vmaxEnv);
        double altStartM = -120;
        if (double.TryParse(Environment.GetEnvironmentVariable("WT_SIM_ALT0"),
                            NumberStyles.Any, CultureInfo.InvariantCulture, out double alt0Env))
            altStartM = alt0Env;

        // WT_SIM_SHAPE=echelon|column|diamond 让仿真跑具名队形（默认仍是自定义三站位）
        if (Environment.GetEnvironmentVariable("WT_SIM_SHAPE") is { Length: > 0 } simShape)
            cfg.FormationShape = simShape;
        var simSlots = WtHud.Hud.FormationGeometry.SimSlots(cfg);
        // 控制器选择：默认站位点追踪（现有产品行为）；WT_SIM_MODE=vector 切到「速度矢量匹配」实验分支
        bool vectorMode = Environment.GetEnvironmentVariable("WT_SIM_MODE") == "vector";
        // WT_SIM_MODE=omega：角速度控制 —— ω_W = ω_L + k·(LOS 误差)，对应现实编队的「姿态跟随」
        bool omegaMode = Environment.GetEnvironmentVariable("WT_SIM_MODE") == "omega";
        // WT_SIM_MODE=staged：两阶段状态机 —— 先 CONVERGE（位置追踪）编好队，再 MAINTAIN（角速度）保持
        bool stagedMode = Environment.GetEnvironmentVariable("WT_SIM_MODE") == "staged";
        // 延迟模型：链路/地图都是 4 Hz，引导看到的是**上一拍**的位置，不是此刻真值。
        // 默认 0.25 s（=一个发包间隔）；WT_SIM_LAT=0 回到 0.4.34 的「瞬时知道」理想化。
        double simLatS = 0.25;
        if (double.TryParse(Environment.GetEnvironmentVariable("WT_SIM_LAT"),
                            NumberStyles.Any, CultureInfo.InvariantCulture, out double latEnv))
            simLatS = Math.Clamp(latEnv, 0, 1.0);
        // WT_SIM_NOCORR=1：只延迟、不修正 —— 用来量「不修的话到底差多少」
        bool simNoCorr = Environment.GetEnvironmentVariable("WT_SIM_NOCORR") == "1";
        // WT_SIM_NOLINK=<秒>：从该时刻起模拟「数据链断掉」——拿不到 ω_L、状态机强制回落 CONVERGE
        double linkLostAt = double.MaxValue;
        if (double.TryParse(Environment.GetEnvironmentVariable("WT_SIM_NOLINK"),
                            NumberStyles.Any, CultureInfo.InvariantCulture, out double llAt))
            linkLostAt = llAt;
        // 切换平滑的时间常数（秒）。0.5 s 实测把尾随的保持能力拖垮（1.5G 掉回 CONVERGE），
        // 0.15 s 只压掉切换那一帧的跳变，不影响稳态跟随。
        double tauSwitch = 0.15;
        // WT_SIM_SCATTER=1：三架僚机以**不同的初始位置 / 航向 / 速度**起步，
        // 用来考验 CONVERGE 阶段（原来的设定是三架同航向同速度，等于把最难的部分绕过去了）。
        bool scatter = Environment.GetEnvironmentVariable("WT_SIM_SCATTER") == "1";
        double straightS = scatter ? 20.0 : 10.0;   // 长机平飞时间：散开起步需要更久才编得起来
        // WT_SIM_MANEUVER=1：按时间表跑完整机动序列（只跑 1 个场景）：
        //   平飞 20 s 编队 → 0.8 G 右转 90° → 直飞 10 s → 1 G 左转 90° → 直飞 10 s
        //   → 5 G 右转 90°（远超 1.5 G 包线）→ 直飞 20 s，看编队能否自行重组
        bool maneuver = Environment.GetEnvironmentVariable("WT_SIM_MANEUVER") == "1";
        // 600 km/h：0.8G≈2.70°/s、1G≈3.37°/s、5G≈16.85°/s；每段按 90° 计算时长
        double[] mt = { 0, 20.0, 53.3, 63.3, 90.0, 100.0, 105.3, 300.0 };
        double[] mw = { 0, 2.70, 0, -3.37, 0, 16.85, 0 };
        int nScen = maneuver ? 1 : 3;
        // 角速度环增益（每秒每度偏差修正多少度/秒）。扫参结论：0.5 全面优于 1.0 ——
        // 2G 尾随 R 518→439、cross −114→23 且不再掉回 CONVERGE；1G/1.5G 的尾随也更准（−30→−17、−18→−8）。
        // 增益越大越容易过冲，然后被 120 m 阈值踢回 CONVERGE，反而更差。
        double kAng = 0.5;
        if (double.TryParse(Environment.GetEnvironmentVariable("WT_SIM_KANG"),
                            NumberStyles.Any, CultureInfo.InvariantCulture, out double kangEnv))
            kAng = kangEnv;

        string outPng = args.Length > 1 ? args[1] : "formation_sim.png";
        string outTxt = System.IO.Path.ChangeExtension(outPng, ".txt");
        string? dir = System.IO.Path.GetDirectoryName(outPng);
        if (!string.IsNullOrEmpty(dir)) System.IO.Directory.CreateDirectory(dir);

        var leaders = new List<(double x, double y)>[3];
        var tracks = new List<(double x, double y)>[3][];
        var summary = new System.Text.StringBuilder();
        summary.AppendLine($"# lag: " + (simLatS <= 0 ? "none (idealised)" 
            : $"{simLatS * 1000:F0} ms" + (simNoCorr ? ", NOT corrected" : ", extrapolated")));
        summary.AppendLine($"# mode: " + (stagedMode
            ? $"staged (CONVERGE=station, MAINTAIN=omega k={kAng:F2})"
            : omegaMode ? $"omega (w_L feedforward, k={kAng:F2})"
            : vectorMode ? "vector (speed matching)" : "station (position tracking)"));
        if (maneuver) summary.AppendLine(
            "# maneuver: straight 20s -> 0.8G R90 -> straight 10s -> 1G L90 -> straight 10s -> 5G R90 -> straight 20s"
            + (scatter ? " | scattered start" : ""));
        else if (stagedMode) summary.AppendLine($"# leader flies straight {straightS:F0} s, then turns"
            + (scatter ? " | scattered start: L(-600,200,h160,v500) R(400,-500,h40,v750) trail(0,-800,h0,v450)" : ""));

        for (int si = 0; si < nScen; si++)
        {
            tracks[si] = new List<(double, double)>[3];
            double omega = rates[si];
            for (int wi = 0; wi < simSlots.Count; wi++)
            {
                var (geo, wingSideSign, slotLabel) = simSlots[wi];
                int wing = wingSideSign;
                ResetGuideState();
                double psiL = 0, lx = 0, ly = 0;   // 长机：V 型顶点，在原点中央
                // V 型起点：左右僚机在斜后方（±150, −370 ≈ 400 m 间距），
                // 尾随僚机在正后方更远处 (0, −600)。三架各自追长机、彼此无交互。
                // 起点：默认从**站位**起步（编队辅助的实际用途是「跟住之后保持」，不是远距汇合）。
                // WT_SIM_START=far 恢复旧起点（尾随 −600），用于对比汇合阶段的表现。
                bool startFar = Environment.GetEnvironmentVariable("WT_SIM_START") == "far" || stagedMode;
                double wx, wy, psiW0, vW0Kmh;
                if (scatter)
                {
                    // 三架各自的烂摊子：左僚机在长机左前方且背向、右僚机在右后且快、尾随在正后且慢
                    if (wing < 0) { wx = -600; wy = 200; psiW0 = 160; vW0Kmh = 500; }
                    else if (wing > 0) { wx = 400; wy = -500; psiW0 = 40; vW0Kmh = 750; }
                    else { wx = 0; wy = -800; psiW0 = 0; vW0Kmh = 450; }
                }
                else
                {
                    wx = geo.SideM;
                    // ★ 远起点只对尾随位生效：老代码里 side≠0 一律用站位后距，
                    // 把 startFar 套到全部槽位会让左右僚机凭空多退 230 m（仿真实测 R 394→400 之外还会改 swAt）。
                    wy = startFar && wing == 0 && cfg.FormationShape == WtHud.Hud.FormationGeometry.ShapeCustom
                        ? -600 : geo.BackM;
                    psiW0 = 0; vW0Kmh = vKmh;
                }
                double psiW = psiW0;
                double omegaSmooth = 0;                // staged 模式的角速度指令（一阶低通后的状态）
                // ---- 独立高度通道（2026-11-12）----
                // 横向环已经调了很久，垂直环完全独立：一个标量误差、一个限幅的升降率、一阶滞后。
                // 爬升会掉速度，这一层先不耦合 —— 先把「能不能稳定停在目标高度」这件事测出来。
                double leadAltM = 3000;
                double wingAltM = leadAltM + altStartM;      // 起始高度差（负 = 僚机偏低）
                double vySmooth = 0;
                double maxAltErr = 0, finalAltErr = 0, altSettledAt = -1;
                double vW = vW0Kmh / 3.6;              // 僚机当前地速（矢量模式用它算速度误差）
                double maxAlong = double.MinValue;
                double ahead0 = 0, finalRel = 0, finalR = 0, finalAlong = 0, finalCross = 0;
                // 航向对齐子模式：航向差大时先摆正方向，再追站位点。
                // 直接指向站位点会追着一条不断移动的尾迹绕圈（5G 机动后实测 35 s 回不来）。
                bool aligning = false;
                bool inMaintain = false;
                double stageSince = 0;
                int stageSwitches = 0;
                double firstSwitchAt = -1;
                var leadTrack = new List<(double, double)>();
                var wingTrack = new List<(double, double)>();
                // 延迟模型：链路看到的是**上一拍**的采样。prev = 上一拍，prevPrev = 再上一拍（估速度用）
                double prevLeadX = 0, prevLeadY = 0, prevPrevLeadX = 0, prevPrevLeadY = 0;
                int latSamples = 0;

                for (int i = 0; i <= steps; i++)
                {
                    double nowS = i * dt;
                    // staged 模式让长机先平飞（把队伍编好），之后才开始转弯
                    // maneuver 模式改按时间表走完整机动序列
                    double omegaNow;
                    if (maneuver)
                    {
                        omegaNow = 0;
                        for (int k = 0; k < mw.Length; k++)
                            if (nowS >= mt[k] && nowS < mt[k + 1]) { omegaNow = mw[k]; break; }
                    }
                    else
                    {
                        omegaNow = (stagedMode && nowS < straightS) ? 0.0 : omega;
                    }
                    // 僚机「看得到」的 ω_L：断链后前馈失效，只能退回几何法
                    bool linkLost = nowS >= linkLostAt;
                    double omegaSeen = linkLost ? 0.0 : omegaNow;
                    // ★ 两个 4 Hz 源是**独立采样**的：链路那一包可能刚好旧 250 ms，
                    //   而本机地图刚好是刚刷新的 —— 取这个最坏相位来建模（两边同龄会互相抵消，
                    //   那是理想情况，不是现实）。
                    double seenLeadX = prevLeadX, seenLeadY = prevLeadY;
                    double seenVelX = latSamples >= 2 ? (prevLeadX - prevPrevLeadX) / dt : 0;
                    double seenVelY = latSamples >= 2 ? (prevLeadY - prevPrevLeadY) / dt : 0;
                    prevPrevLeadX = prevLeadX; prevPrevLeadY = prevLeadY;
                    prevLeadX = lx; prevLeadY = ly;
                    latSamples++;

                    double ex, ey;
                    if (simLatS > 0 && latSamples >= 2)
                    {
                        if (simNoCorr)
                        {
                            ex = seenLeadX - wx;
                            ey = seenLeadY - wy;
                        }
                        else
                        {
                            // 与机上一致：链路那位按两拍差分估速度外推；本机位置本来就是新的，不动
                            var (clx, cly) = WtHud.Hud.FormationResolver.Advance(
                                seenLeadX, seenLeadY, seenVelX, seenVelY, simLatS);
                            ex = clx - wx;
                            ey = cly - wy;
                        }
                    }
                    else
                    {
                        ex = lx - wx;
                        ey = ly - wy;
                    }
                    double R = Math.Sqrt(ex * ex + ey * ey);
                    double bearing = Math.Atan2(ex, ey) * 180.0 / Math.PI;
                    double rel = NormalizeDeg(bearing - psiW);

                    double guide;
                    double vecFeed = 0;
                    double omegaCmd = double.NaN;

                    // 两阶段状态机（仅 staged 模式）：距离误差进 50 m 且驻留 3 s → MAINTAIN；
                    // 掉到 120 m 以外 → 退回 CONVERGE。迟滞 50:120，避免在边界来回切。
                    if (stagedMode)
                    {
                        double rangeErrNow = Math.Abs(R - geo.RangeM);
                        if (linkLost && inMaintain)
                        {
                            // 断链：拿不到 ω_L，角速度前馈失效 → 立刻回落，别在保持模式里瞎飞
                            inMaintain = false; stageSince = nowS; stageSwitches++;
                        }
                        else if (!inMaintain && !linkLost && rangeErrNow < 60 && nowS - stageSince > 3.0)
                        {
                            // 断链时不许进 MAINTAIN：否则会「进→被踢→再进」反复抖（实测 10 次切换）
                            inMaintain = true; stageSince = nowS; stageSwitches++;
                            if (firstSwitchAt < 0) firstSwitchAt = nowS;
                        }
                        else if (inMaintain && rangeErrNow > 120)
                        {
                            // 试过给退出加 2 s 时间迟滞，结果更差（尾随 463 → 544 并掉出 MAINTAIN）：
                            // 拖着不退出会让它在 MAINTAIN 的角速度算法里多飞 2 s 错方向。
                            // 所以退出保持**瞬时**——那 3 次切换是系统在自适应，不是抖动。
                            inMaintain = false; stageSince = nowS; stageSwitches++;
                        }
                    }

                    // 外圈在保持阶段继续用「位置追踪」：
                    // 角速度前馈基于站位点几何，而外圈站位点在转弯时并非稳定参考，
                    // 实测外圈 cross 从 −155 掉到 −93；尾随/内圈反过来，角速度明显更准（38 → 3）。
                    bool outerInMaintain = stagedMode && inMaintain && wing != 0 && !IsInsideTurn(wing, omegaNow);

                    bool useOmega = ((stagedMode && inMaintain) || omegaMode) && !outerInMaintain;

                    if (useOmega)
                    {
                        // ===== 实验分支：角速度控制（对应现实编队的「姿态跟随」）=====
                        // 目标不是「指向某个点」，而是让 LOS 角稳定在站位对应的值上：
                        //   ω_W = ω_L + k·(rel − targetRel)
                        // 第一项是前馈（长机转多少我跟多少，即时），第二项才是偏差修正。
                        double sideW = geo.SideM;
                        double backW = geo.BackM;
                        double stBrg = Math.Atan2(sideW, backW) * 180.0 / Math.PI;      // 站位点相对长机（机体系）
                        double targetRel = NormalizeDeg(stBrg + 180.0 + (psiL - psiW)); // 长机相对我方的目标方位
                        double errAng = NormalizeDeg(rel - targetRel);
                        omegaCmd = omegaSeen + kAng * errAng;
                        guide = 0;                 // 本模式不走「角度→角速度」通道
                        ahead0 = 0;
                        // 角速度前馈只管方向；外圈要多飞的路程仍得靠速度前馈补，否则被甩在后面
                        vecFeed = FormationFeedKmh(omegaNow, R, vKmh, wing, rel, 0,
                            cfg.FormationWingMinRatio, cfg.FormationWingMaxRatio);
                    }
                    else if (vectorMode)
                    {
                        // ===== 实验分支：方向仍追站位点，只把「速度」换成矢量匹配 =====
                        // 理由：纯矢量会让机头对准 V_s，收敛阶段丢失「该往哪飞」的指向性
                        // （实测直线都漂到 421 m）；但速度这一路用站位点速度的模更准 ——
                        // 站位点自带切向分量 ω×r，外圈该加速、内圈该减速是几何直接给出的。
                        double aheadV = FormationLeadAheadDeg(omegaNow, R, vKmh, 45, wing);
                        guide = NormalizeDeg(WingAimDeg(rel, R,
                            NormalizeDeg(psiL - psiW), geo.SideM, geo.BackM) + aheadV);
                        ahead0 = aheadV;

                        double om = omegaNow * Math.PI / 180.0;                    // rad/s，正 = 右转
                        double sp = Math.Sin(psiL * Math.PI / 180.0);
                        double cp = Math.Cos(psiL * Math.PI / 180.0);
                        // ★ 切线速度按「僚机当前实际位置」算，不是站位点：
                        //   V_s = V_L + ω×(P_W − P_L)。站位点版本在收敛阶段会退化成前馈≈0
                        //   （内圈站位点几乎落在转弯圆心上），内圈反而更差。
                        //   用实际位置时 R 大 → |V_s| 大 → 自动加速追近，兼顾收敛与保持。
                        double rx = wx - lx, ry = wy - ly;                      // 僚机相对长机（世界系）
                        double vsx = vMs * sp + om * ry;                        // ω×r = ω·(r_y, −r_x)
                        double vsy = vMs * cp - om * rx;
                        double vsMag = Math.Sqrt(vsx * vsx + vsy * vsy);
                        vecFeed = (vsMag - vW) * 3.6;                           // 速度差 → 油门前馈
                    }
                    else
                    {
                        // 现有产品行为：指向随长机航向旋转的站位点 + 提前角
                        // ★ 但航向差大时先摆正方向：直接指向站位点会追着一条不断移动的尾迹绕圈
                        //   （5G 机动后实测 35 s 回不来，图上是绕大圈）。
                        //   迟滞 30° 进入 / 15° 退出，避免在边界反复切。
                        double hdgErr = NormalizeDeg(psiL - psiW);
                        if (!aligning && Math.Abs(hdgErr) > 30) aligning = true;
                        else if (aligning && Math.Abs(hdgErr) < 15) aligning = false;

                        if (aligning)
                        {
                            guide = hdgErr;              // 先跟长机航向，距离交给后面的追赶逻辑
                        }
                        else
                        {
                            double ahead = FormationLeadAheadDeg(omegaNow, R, vKmh, 45, wing);
                            guide = NormalizeDeg(WingAimDeg(rel, R, hdgErr, geo.SideM, geo.BackM) + ahead);
                            if (i == 0) ahead0 = ahead;
                        }
                    }

                    // ---- 真实级联 PID（与 UpdateNavigation 同公式 / 同增益 / 同限幅）----
                    double deltaKmh = 0;
                    double closing = UpdateClosingRate(R, nowS);
                    // |rel|<50 这道门是给「指向」模式设计的（偏角大时先转向、别急着修速度）。
                    // 角速度模式方向由 ω 环管，油门必须一直可用，否则一超出 50° 就没推力 → 发散。
                    // ★ 掉队追赶：背向切入时（rel 接近 180°）这道门会把油门彻底关死，
                    //   僚机只能一边转一边被长机拉远，形成「偏角大→不给油→追不上→偏角更大」的死循环。
                    //   实测散开起步时左僚机 30 s 都没编上队。所以**只看距离**：超过 500 m 就进追赶，
                    //   给满允许加速（maxRatio），先把距离拉回来再交回 PID。
                    // ★ 追赶：只把「远景增益」提上来，加速量仍走 (want − closing) 这条接近率闭环 ——
                    //   追近了 closing 变大、want−closing 自然减小，不会像开环加成那样冲过头。
                    //   同时解除 |rel|<50 的门（背向切入时偏角大，但那不是「该不该给油」的依据）。
                    // 对齐阶段也允许加速：只摆方向不补速度的话，平行飞追不上（尾随被甩到 566 m 就是这原因）
                    // 阈值 450：525 时会在 476 m 处「刚好掉出追赶」，闭环推力不足以继续收敛，卡住不动
                    // ★ 门槛按槽位斜距给，不能写死 450：梯队第 2 位斜距 1030 m，
                    //   写死值会让它一直判定「掉队」并加速冲到长机身边（实测 R 1030 → 305）。
                    //   自定义队形的 400 m 槽位：400+50=450，与原值逐字相同。
                    bool chasing = R > geo.RangeM + 50 || (!useOmega && aligning);
                    if (omegaMode || Math.Abs(rel) < 50 || chasing)
                    {
                        double err = R - geo.RangeM;
                        double pdt = guideIntegralLastT > 0
                            ? Math.Clamp(nowS - guideIntegralLastT, 0, 0.5) : 0;
                        guideIntegralLastT = nowS;
                        double blend = Math.Max(100.0, cfg.FormationBlendM);
                        double far = Math.Clamp(err / blend, 0.0, 1.0);
                        double kp = err > 0
                            ? cfg.FormationKp + (cfg.FormationKpNear - cfg.FormationKp) * (1.0 - far)
                            : cfg.FormationKpNear;
                        if (chasing) kp = Math.Max(kp, 0.15);   // 远距离把增益抬到能真追上
                        // 追赶时把期望接近率上限从 30 放到 50 m/s：30×3.6=108 km/h 追不上匀速长机，
                        // 而 maxRatio 允许 +180（=50 m/s）。闭环仍在 —— closing 上来后差值自动缩小。
                        double wantMax = chasing ? 50.0 : 30.0;
                        double want = Math.Clamp(kp * err + cfg.FormationKi * guideIntegral, -20, wantMax);
                        double cosOff = Math.Max(0.5, Math.Cos(rel * Math.PI / 180.0));
                        double dvRaw = (want - closing) * cfg.FormationKv / cosOff * 3.6;
                        // ★ 追赶阶段加**加速下限**：纯闭环在「closing 很大」时会判成「追太快」而刹车
                        //   （实测 t=115 R=666、closing=54 时给出 −22，然后距离又拉开，来回振荡卡在 566）。
                        //   下限按距离给，只做保底，不替代闭环。
                        if (chasing)
                            dvRaw = Math.Max(dvRaw, Math.Clamp((R - geo.RangeM - 20) * 0.8, 0,
                                vKmh * (cfg.FormationWingMaxRatio - 1)));
                        // 与产品 UpdateNavigation 同一套前馈 + 上下限
                        double feed = (vectorMode || omegaMode)
                            ? vecFeed
                            : FormationFeedKmh(omegaNow, R, vKmh, wing, rel, 0,
                                cfg.FormationWingMinRatio, cfg.FormationWingMaxRatio);
                        deltaKmh = Math.Clamp(SmoothGuideDelta(dvRaw + feed, nowS, cfg.FormationGuideTauS),
                            vKmh * (cfg.FormationWingMinRatio - 1),
                            vKmh * (cfg.FormationWingMaxRatio - 1));
                        if (pdt > 0 && Math.Abs(deltaKmh) < 114 && Math.Abs(want) < 29.4)
                            guideIntegral = Math.Clamp(guideIntegral + err * pdt,
                                -GuideIntegralLimit, GuideIntegralLimit);
                    }
                    // deltaKmh 是给飞行员看的油门建议（km/h），仿真里当成速度变化量直接应用
                    vW = (vKmh + deltaKmh) / 3.6;

                    double sL = Math.Sin(psiL * Math.PI / 180.0), cL = Math.Cos(psiL * Math.PI / 180.0);
                    double along = (wx - lx) * sL + (wy - ly) * cL;
                    double cross = (wx - lx) * cL - (wy - ly) * sL;
                    maxAlong = Math.Max(maxAlong, along);
                    finalRel = rel; finalR = R; finalAlong = along; finalCross = cross;

                    leadTrack.Add((lx, ly));
                    wingTrack.Add((wx, wy));

                    // 调试打点（仅尾随位、每 5 s）：定位「为什么给不足加速」
                    if (wi == 0 && i % (int)(5.0 / dt) == 0)
                        summary.AppendLine($"# dbg t={nowS:F0} R={R:F0} dKmh={deltaKmh:F0} "
                            + $"closing={closing:F1} vW={vW * 3.6:F0} want?={(Math.Abs(rel) < 50 ? "pid" : "gate")}");

                    // 角速度模式直接给 ω（已经含 ω_L 前馈）；其余（含外圈的保持阶段）还是「角度误差 → 每步 0.25 增益」
                    if (stagedMode)
                    {
                        // 只在 MAINTAIN 内部做低通：CONVERGE 本来就是稳定的角度环，
                        // 给它加滞后会让收敛变慢（实测外圈因此掉到 500 m 并退回）。
                        // 切出去时把 omegaSmooth 对齐到 guide，下次进 MAINTAIN 才不会从 0 爬。
                        if (useOmega)
                        {
                            omegaSmooth += (omegaCmd - omegaSmooth) * Math.Min(1.0, dt / tauSwitch);
                            psiW = NormalizeDeg(psiW + Math.Clamp(omegaSmooth, -maxTurnDegPerS, maxTurnDegPerS) * dt);
                        }
                        else
                        {
                            omegaSmooth = guide;
                            psiW = NormalizeDeg(psiW + Math.Clamp(guide * 0.25,
                                -maxTurnDegPerS * dt, maxTurnDegPerS * dt));
                        }
                    }
                    else
                    {
                        psiW = (omegaMode || (stagedMode && inMaintain)) && !outerInMaintain
                            ? NormalizeDeg(psiW + Math.Clamp(omegaCmd, -maxTurnDegPerS, maxTurnDegPerS) * dt)
                            : NormalizeDeg(psiW + Math.Clamp(guide * 0.25,
                                -maxTurnDegPerS * dt, maxTurnDegPerS * dt));
                    }
                    // 垂直环：目标 = 长机高度 + 槽位高度（UpM 正 = 站位更高）
                    if (altLoop)
                    {
                        double altErr = (leadAltM + geo.UpM) - wingAltM;
                        maxAltErr = Math.Max(maxAltErr, Math.Abs(altErr));
                        double vyCmd = Math.Clamp(altErr * VerticalGain, -vyMaxMs, vyMaxMs);
                        vySmooth += (vyCmd - vySmooth) * Math.Min(1.0, dt / 0.5);
                        wingAltM += vySmooth * dt;
                        finalAltErr = (leadAltM + geo.UpM) - wingAltM;
                        if (altSettledAt < 0 && Math.Abs(finalAltErr) < 10) altSettledAt = nowS;
                    }

                    psiL = NormalizeDeg(psiL + omegaNow * dt);
                    lx += vMs * Math.Sin(psiL * Math.PI / 180.0) * dt;
                    ly += vMs * Math.Cos(psiL * Math.PI / 180.0) * dt;
                    wx += vW * Math.Sin(psiW * Math.PI / 180.0) * dt;
                    wy += vW * Math.Cos(psiW * Math.PI / 180.0) * dt;
                }

                if (wi == 0) leaders[si] = leadTrack;
                tracks[si][wi] = wingTrack;
                string wname = slotLabel;
                summary.AppendLine($"{scenarios[si]} wing={wname} inside={(IsInsideTurn(wing, omega) ? "Y" : "N")} "
                    + $"ahead={ahead0:F1} finalRel={finalRel:F1} R={finalR:F0} "
                    + $"along={finalAlong:F0} cross={finalCross:F0} crossed={(maxAlong > 0 ? "YES" : "no")}"
                    + (stagedMode
                        ? $" stage={(inMaintain ? "MAINTAIN" : "CONVERGE")} sw={stageSwitches} swAt={firstSwitchAt:F1}"
                        : "")
                    + (altLoop
                        ? $" | dH={finalAltErr:F0}m up={geo.UpM:F0} maxErr={maxAltErr:F0} settle={(altSettledAt < 0 ? "no" : altSettledAt.ToString("F1"))}s"
                        : ""));
            }
        }
        System.IO.File.WriteAllText(outTxt, summary.ToString());

        // ---- 2×2 俯视轨迹图 ----
        const int cell = 620, pad = 12;
        int width = cell * 2 + pad * 3, height = cell * 2 + pad * 3;
        using var bmp = new System.Drawing.Bitmap(width, height);
        using var g = System.Drawing.Graphics.FromImage(bmp);
        g.Clear(System.Drawing.Color.FromArgb(14, 16, 22));
        var titleFont = new System.Drawing.Font("Arial", 17f, System.Drawing.FontStyle.Bold);
        var labelFont = new System.Drawing.Font("Arial", 13f);
        var leaderPen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(255, 95, 95), 3f);
        var wingPens = new[]
        {
            new System.Drawing.Pen(System.Drawing.Color.FromArgb(155, 160, 175), 2.2f),  // 0
            new System.Drawing.Pen(System.Drawing.Color.FromArgb(80, 210, 255), 2.2f),   // L
            new System.Drawing.Pen(System.Drawing.Color.FromArgb(255, 205, 75), 2.2f),   // R
        };
        var gridPen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(45, 50, 62), 1f);
        var panelBrush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(22, 25, 33));

        for (int si = 0; si < nScen; si++)
        {
            int ox = pad + (si % 2) * (cell + pad), oy = pad + (si / 2) * (cell + pad);
            g.FillRectangle(panelBrush, ox, oy, cell, cell);
            for (int k = 1; k < 4; k++)
            {
                int gx = ox + cell * k / 4, gy = oy + cell * k / 4;
                g.DrawLine(gridPen, gx, oy, gx, oy + cell);
                g.DrawLine(gridPen, ox, gy, ox + cell, gy);
            }

            double minX = double.MaxValue, maxX = double.MinValue;
            double minY = double.MaxValue, maxY = double.MinValue;
            void Include(double x, double y)
            {
                minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
                minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
            }
            foreach (var p in leaders[si]) Include(p.x, p.y);
            for (int wi = 0; wi < 3; wi++) foreach (var p in tracks[si][wi]) Include(p.x, p.y);
            double cx = (minX + maxX) / 2, cy = (minY + maxY) / 2;
            double span = Math.Max(maxX - minX, maxY - minY) * 1.15 + 1;
            (int, int) Map(double x, double y) =>
                (ox + (int)((x - cx) / span * cell + cell / 2),
                 oy + cell / 2 - (int)((y - cy) / span * cell));

            void DrawTrack(List<(double x, double y)> pts, System.Drawing.Pen pen)
            {
                for (int i = 1; i < pts.Count; i++)
                {
                    var a = Map(pts[i - 1].x, pts[i - 1].y);
                    var b = Map(pts[i].x, pts[i].y);
                    g.DrawLine(pen, a.Item1, a.Item2, b.Item1, b.Item2);
                }
                var s = Map(pts[0].x, pts[0].y);
                var e = Map(pts[^1].x, pts[^1].y);
                g.FillEllipse(System.Drawing.Brushes.White, s.Item1 - 3, s.Item2 - 3, 6, 6);
                g.FillEllipse(new System.Drawing.SolidBrush(pen.Color), e.Item1 - 5, e.Item2 - 5, 10, 10);
            }
            DrawTrack(leaders[si], leaderPen);
            for (int wi = 0; wi < 3; wi++) DrawTrack(tracks[si][wi], wingPens[wi]);
            g.DrawString(scenarios[si], titleFont, System.Drawing.Brushes.White, ox + 12, oy + 10);
        }

        // 第 4 格：图例
        {
            int ox = pad + (cell + pad), oy = pad + (cell + pad);
            g.FillRectangle(panelBrush, ox, oy, cell, cell);
            g.DrawString("LEGEND", titleFont, System.Drawing.Brushes.White, ox + 12, oy + 10);
            string[] names = { "LEADER", "TRAILER (center)", "WING L (back-left)", "WING R (back-right)" };
            var pens = new[] { leaderPen, wingPens[0], wingPens[1], wingPens[2] };
            for (int i = 0; i < 4; i++)
            {
                int yy = oy + 60 + i * 42;
                g.DrawLine(pens[i], ox + 20, yy, ox + 80, yy);
                g.DrawString(names[i], labelFont, System.Drawing.Brushes.White, ox + 95, yy - 10);
            }
            g.DrawString("white dot = start   colored dot = end", labelFont,
                System.Drawing.Brushes.LightGray, ox + 20, oy + 260);
            g.DrawString("INNER keeps lead / OUTER pure pursuit", labelFont,
                System.Drawing.Brushes.LightGray, ox + 20, oy + 290);
            g.DrawString("real cascade PID (formationKp/Ki/Kv)", labelFont,
                System.Drawing.Brushes.LightGray, ox + 20, oy + 320);
            g.DrawString("V=600km/h  dt=0.25s  T=30s", labelFont,
                System.Drawing.Brushes.LightGray, ox + 20, oy + 350);
            g.DrawString("V START: leader center, wings back,", labelFont,
                System.Drawing.Brushes.LightGray, ox + 20, oy + 380);
            g.DrawString("trailer further behind", labelFont,
                System.Drawing.Brushes.LightGray, ox + 20, oy + 405);
        }
        bmp.Save(outPng, System.Drawing.Imaging.ImageFormat.Png);
        Console.WriteLine("PNG: " + outPng);
        return 0;
    }

    /// <summary>
    /// 纯数据链诊断：不开 HUD、不读游戏、不碰 8111，只听 48017 端口，收到什么打什么。
    /// <para>跨机联调时这是最快的判断方式 —— 不用进游戏、不用看 HUD，一张嘴就能说清
    /// 「包到底有没有到」。退出码 0 = 收到过包，1 = 一个都没收到，2 = 端口绑不上。</para>
    /// </summary>
    private static int DataLinkProbe(string arg, string idArg = "")
    {
        int seconds = 30;
        if (int.TryParse(arg, NumberStyles.Integer, CultureInfo.InvariantCulture, out int s) && s > 0)
            seconds = s;

        // 第二个参数 = 本机节点 ID。填了才能验证「只列跟我的僚机」这条过滤：
        // 不填是 0 = 不过滤，所有人的僚机都会进表。
        int selfId = 0;
        if (int.TryParse(idArg, NumberStyles.Integer, CultureInfo.InvariantCulture, out int sid)
            && sid > 0 && sid <= 999_999)
            selfId = sid;

        using var link = new WtHud.Telemetry.DataLink(selfId);
        link.Start();
        if (!link.Listening)
        {
            Console.WriteLine($"datalink probe: cannot bind udp {WtHud.Telemetry.DataLink.Port} " +
                              "(another WtHud instance running, or the port is blocked)");
            return 2;
        }

        link.PacketCallback = (n, from) =>
        {
            string pos = n.Xn is double x && n.Yn is double y
                ? $"pos={x:F5},{y:F5}"
                : "pos=-";
            string hdg = n.HeadingDeg is double h ? h.ToString("F0") : "-";
            string bank = n.TargetBankDeg is double b ? b.ToString("F0") + "deg" : "-";
            // 角色：L = 自称长机、W = 自称僚机、- = 旧版没发第 10 段
            string role = n.Role switch
            {
                WtHud.Telemetry.DataLink.LinkRole.Lead => "L",
                WtHud.Telemetry.DataLink.LinkRole.Wing => "W",
                _ => "-",
            };
            // 站位自报：长机侧靠它知道谁掉队，联调时也必须能一眼看到
            string wing = n.Wing switch
            {
                WtHud.Telemetry.DataLink.WingState.InPosition => $"W{n.Slot + 1}=ok",
                WtHud.Telemetry.DataLink.WingState.Behind => $"W{n.Slot + 1}=behind{n.ErrM}m",
                WtHud.Telemetry.DataLink.WingState.Ahead => $"W{n.Slot + 1}=ahead{Math.Abs(n.ErrM)}m",
                WtHud.Telemetry.DataLink.WingState.OffStation => $"W{n.Slot + 1}=off",
                // 有站位段但状态是 -1：在编队里、这一帧算不出（长机那边显示「数据错误」）
                _ => n.Slot >= 0 ? $"W{n.Slot + 1}=err" : "-",
            };
            Console.WriteLine($"  {DateTime.Now:HH:mm:ss.fff}  from {from,-22} id={n.Id,-7} " +
                              $"role={role}  wing={wing,-14} lead={(n.LeadId > 0 ? "#" + n.LeadId : "-"),-8} " +
                              $"alt={n.AltM,5}m  vy={n.VyMs,6:F1}  hdg={hdg,4}  bank={bank,6}  {pos}");
        };

        Console.WriteLine($"datalink probe: listening on udp {WtHud.Telemetry.DataLink.Port} for {seconds}s"
                          + (selfId > 0 ? $"  (self id {selfId}: wing table filtered to my formation)" : ""));
        Console.WriteLine("  expecting WTDL2|id|alt|vy|hdg|turn[|x|y[|bank[|role[|slot,state,err[|leadId]]]]]  (Ctrl+C stops early)");

        long t0 = Environment.TickCount64;
        while (Environment.TickCount64 - t0 < seconds * 1000L) Thread.Sleep(200);

        // 长机是谁、依据是什么：三机以上编队时这一行比 rx 更能说明问题
        string leadText = "none";
        if (link.TryGetLeadNode(out var leadNode, out var leadSrc, out bool leadAmb))
            leadText = $"#{leadNode.Id}({LeadSourceText(leadSrc)}{(leadAmb ? "!" : "")})";

        Console.WriteLine();
        Console.WriteLine($"result: rx={link.RxCount}  bad={link.RxBad}  nodes={link.NodeCount}  " +
                          $"lead={leadText}  last={(link.LastPeer.Length == 0 ? "none" : link.LastPeer)}");

        // 长机 HUD 会画的那张表，原样打出来 —— 联调时不用进游戏就能确认「谁掉队、谁数据错误、谁掉线」。
        // ★ 传了本机 ID 就按「只列跟我的」过滤；不传（ID=0）是全量，看 lead= 那一列区分。
        var rows = WingTable(link.RecentNodes(WingOfflineKeepMs), link.SelfId);
        Console.WriteLine($"wing table: {rows.Count} row(s)");
        foreach (var n in rows)
            Console.WriteLine($"  {WingLabel(n),-6} {WingStateText(n)}");
        if (link.RxCount > 0) return 0;

        Console.WriteLine("no packet arrived. check, in this order:");
        Console.WriteLine("  1. firewall on BOTH ends (Windows asks once per network profile)");
        Console.WriteLine("  2. target ip:port - broadcast 255.255.255.255 never crosses a router");
        Console.WriteLine("  3. if the two machines are not on the same subnet, configure dataLinkPeers");
        Console.WriteLine("     with the peer's address and open the udp port on the way through");
        return 1;
    }

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

        // 无罗盘机型（阵风）：先滚 0.75 s 到 30°，然后稳住不改平。
        // 这条以前是空的 —— 所以「稳盘读出 0」这个 bug 一路没被发现。
        FusionScenario("no compass, hold 30 bank", bankExpected: 30, pitchExpected: 2.0,
            heading: _ => 90, tas: _ => 900, vy: _ => 1.0, aoa: _ => 2.0, ny: _ => 1.155,
            hasHeading: false, wx: t => t < 0.75 ? 40.0 : 0.0);

        // 同样的动作，但滚到位后立刻回平：应该回到 0，不能因为锚定而卡住
        FusionScenario("no compass, back to level", bankExpected: 0, pitchExpected: 2.0,
            heading: _ => 90, tas: _ => 900, vy: _ => 0.0, aoa: _ => 2.0, ny: _ => 1.0,
            hasHeading: false, wx: t => t < 0.5 ? 40.0 : (t < 1.0 ? -40.0 : 0.0));

        return Environment.ExitCode;
    }

    private static void FusionScenario(string name, double bankExpected, double pitchExpected,
        Func<double, double> heading, Func<double, double> tas, Func<double, double> vy,
        Func<double, double> aoa, Func<double, double> ny,
        bool hasHeading = true, Func<double, double>? wx = null)
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
                HeadingDeg = heading(t), HasHeading = hasHeading,
                TasKmh = tas(t), IasKmh = tas(t) * 0.9,
                VyMs = vy(t), AoaDeg = aoa(t), NyG = ny(t), AltM = 2000,
                RollRateDegS = wx?.Invoke(t) ?? 0,
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

        // 数据链：只交换气压高度（map_obj 没有 z，单机拿不到）。
        // 节点 ID 首次自动生成并写回配置，以后一直沿用，不需要人工配对。
        if (cfg.DataLinkEnabled)
        {
            if (cfg.DataLinkId == 0)
            {
                cfg.DataLinkId = Random.Shared.Next(100000, 999999);
                LogSave(cfg.Save(), "data link node id");
                Console.WriteLine($"data link: assigned node id {cfg.DataLinkId} (saved to hud-config.json)");
            }
            dataLink = new WtHud.Telemetry.DataLink(cfg.DataLinkId);
            // 收到包就拿本机地图的机场表比对队标；比不出来（刚进图）放行，不同队直接丢
            dataLink.TeamCheck = VerifyTeamAnchor;
            dataLink.Start();
            Console.WriteLine($"data link: on, node id {cfg.DataLinkId}, udp {WtHud.Telemetry.DataLink.Port}" +
                              (dataLink.Listening ? "" : "  ** BIND FAILED - port busy or blocked **"));

            // 单播目标：跨网段/内网穿透必须填，广播 255.255.255.255 出不了路由器。
            // 启动应用一次，面板里改完再应用一次（ApplyDataLinkPeers）。
            ApplyDataLinkPeers(cfg);
        }

        // 配置互传：独立端口 48018，**不看数据链开关**。
        // 新装机第一件事就是收别人的配置，那时候链路通常还没开 —— 这里要是也 follow 开关，
        // 「上线就能飞」的第一步就卡死了。
        configLink = new WtHud.Telemetry.ConfigLink();
        configLink.TeamCheck = VerifyTeamAnchor;
        configLink.Start();
        Console.WriteLine($"config link: udp {WtHud.Telemetry.ConfigPacket.Port}" +
                          (configLink.Listening ? "" : "  ** BIND FAILED - port busy or blocked **"));

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
            else if (id == 15)
            {
                // 长机转向辅助：开/关。方向不在这里定 —— 由第一次真实转向自动锁。
                ToggleTurnAssist();
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
                LogSave(cfg.Save(), "F12 save");
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
                    LogSave(cfg.Save(), "stall calibration");
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
                LogSave(cfg.Save(), "pitch sign");
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
            // 自己的转向率每帧更新：有数据链要广播给僚机，没数据链时"反推长机转向率"也要用它。
            // 没有罗盘就用航迹角兜底，和 UpdateNavigation 里的 navHeading 同一套规则。
            double ownHdg = snap.HasHeading ? snap.HeadingDeg : 0;
            bool ownHdgValid = snap.HasHeading;
            if (!ownHdgValid && mapData.CourseDeg is double cog)
            {
                ownHdg = cog;
                ownHdgValid = true;
            }
            UpdateOwnTurnRate(ownHdgValid, ownHdg, snap.ElapsedSeconds);
            // 坐标只在 map_obj 认得出自己时才有（刚进图、地图没加载时 Self 为 null）。
            // 那时发不带坐标的 6 段报文 —— 对方至少还能拿到高度，只是配不上对。
            double? dlXn = null, dlYn = null;
            if (mapData.Self is { } dlSelf) { dlXn = dlSelf.X; dlYn = dlSelf.Y; }
            // 接收线程做队标校验时要读这张表：写一个快照引用过去，读端不会看到半张表
            Volatile.Write(ref _linkMapSnapshot, mapData);

            // 队标锚点：离自己最近的机场在哪、在**我**眼里算不算友军。
            // 对方拿同一对坐标去自己的机场表里比颜色，就知道是不是同一队。
            double? anchorXn = null, anchorYn = null;
            bool? anchorFriendly = null;
            if (mapData.Self is { } anchorSelf
                && TryNearestAirfield(mapData.Airfields, anchorSelf.X, anchorSelf.Y, out var anchorField,
                                      friendlyOnly: true, tolerance: 2.0))
            {
                anchorXn = anchorField.X;
                anchorYn = anchorField.Y;
                anchorFriendly = anchorField.Friendly;
            }
            dataLink?.Send(snap.ElapsedSeconds, (int)Math.Round(snap.AltM), snap.VyMs, ownHdg, ownTurnRateDeg,
                           dlXn, dlYn, TurnAssistBroadcastBank(cfg), SelfLinkRole(cfg),
                           selfWingSlot, selfWingState, selfWingErrM,
                           EffectiveLeadId(cfg.LeadMode, cfg.DataLinkLeadId),
                           anchorXn, anchorYn, anchorFriendly);
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
                // 8111 半边服务检测：state 有、indicators 与 map 同时空 = 启动竞态，
                // 只有重启游戏能恢复。这一行是给现场排查用的，出现就该知道别再猜解析器。
                var health = UpdateApiHealth(latest, mapData.Self is not null);
                Console.WriteLine($"      api={ApiHealthText(health)}  state={latest?.Caps.StateValid} ind={latest?.Caps.IndicatorsValid} " +
                                  $"map={(mapData.Self is not null)}");
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
        configLink?.Dispose();
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
    /// 油门建议的一阶低通。时间常数由 <c>formationGuideTauS</c> 配置（默认 0.5 s）：
    /// 够快能跟上编队修正，够慢不会让数字来回跳。
    /// <para>首帧、断档（切图/暂停）或目标切换时直接取当前值，避免从 0 慢慢爬上去。</para>
    /// </summary>
    private static double SmoothGuideDelta(double raw, double nowS, double tauS = 1.2)
    {
        double dt = nowS - guideDeltaLastT;
        if (!guideDeltaValid || dt <= 0 || dt > 1.0)
        {
            guideDeltaSmooth = raw;
            guideDeltaValid = true;
            guideDeltaLastT = nowS;
            return guideDeltaSmooth;
        }

        double k = Math.Min(1.0, dt / Math.Max(0.2, tauS));
        guideDeltaSmooth += (raw - guideDeltaSmooth) * k;
        guideDeltaLastT = nowS;
        return guideDeltaSmooth;
    }

    /// <summary>切目标或关闭编队时把引导状态清干净，免得拿上一架的平滑值接着算。</summary>
    private static void ResetGuideState()
    {
        closingHistory.Clear();
        leadRelHistory.Clear();
        guideDeltaSmooth = 0;
        guideDeltaLastT = 0;
        guideDeltaValid = false;
        guideActionPrev = FormationAction.None;
        guideIntegral = 0;
        guideIntegralLastT = 0;
        leadHeadingLockValid = false;
        altitudeStageActive = false;      // 重新开始编队时回到「先爬升」的判定
        mapVisibleLatched = false;        // 地图判定也要忘掉上一次的锁存
        mapVisibleLastHitS = double.NegativeInfinity;
        vertSmoothT = -1;                 // 平滑器一起重置，否则会从上一段的旧值慢慢爬过来
    }

    private static void UpdateNavigation(HudOptions opt, WtHud.Telemetry.MapData map, TelemetrySnapshot snap, HudConfig cfg)
    {
        // 编队开关：关掉时既不解析友机（MapData.CollectFriendlies），也不做方位/距离计算。
        bool formationOn = cfg.FormationAssist || cfg.FormationOnly;
        map.CollectFriendlies = formationOn;
        // 「长机机型」只在数据链不生效时用来筛友机：
        //   数据链能收到节点 → 靠位置配对认长机（同机型多机也分得开）；
        //   纯 8111 单机 → 只能按机型过滤，否则最近的友机会被当成长机。
        CurrentTasKmh = snap.TasKmh;
        if (!string.IsNullOrEmpty(snap.VehicleType)) CurrentVehicleType = snap.VehicleType;

        bool linkUsable = cfg.DataLinkEnabled && dataLink != null && dataLink.NodeCount > 0;
        map.LeadIconFilter = EffectiveLeadIcon(linkUsable, cfg.LeadIcon);
        opt.FormationAssist = formationOn;
        opt.FormationOnly = cfg.FormationOnly;
        opt.Friendlies.Clear();
        friendPos.Clear();          // 与 opt.Friendlies 同序的归一化位置，配对用
        opt.Guide = null;
        opt.FormationLinkText = "";
        opt.LeadStatusMain = "";
        opt.LeadStatusSub = "";
        opt.LeadWingRows.Clear();
        opt.LeadSpeedBar = null;       // 长机的速度条数据，只有长机分支会填
        opt.TargetHeadingDeg = null;   // 航向带上的琥珀三角，只有确定目标航向时才画
        opt.FormationNotice = "";      // 每帧重置，只有状态机判定「刚到位」时才置上
        opt.FormationHint = "";        // 同上：只有「找不到长机」时才填

        // ---- 编队解除 / 恢复（按一下切换，边沿触发）----
        // 解除后不再画引导，飞行员可以自由飞；再按一下重新建立，之后重新判定到位。
        // 只对僚机生效：长机不画引导，按了只会把状态机打进 Disabled，
        // 连「编队完成」都收不到（长机唯一的编队提示）。
        bool fkDown = _formationKeyBinding.IsKeyDownIgnoringModifiers();
        if (!cfg.LeadMode && fkDown && !formationKeyPrev)
        {
            if (formationStage == FormationStage.Disabled)
            {
                formationStage = FormationStage.Converge;
                formationStageSince = snap.ElapsedSeconds;
                formationDoneSince = -1;
                formationNoticeUntil = -1;          // 恢复时不提示，等真到位再显示「编队完成」
            }
            else
            {
                formationStage = FormationStage.Disabled;
                formationNoticeUntil = snap.ElapsedSeconds + 3.0;
            }
        }
        formationKeyPrev = fkDown;
        // 提示放在这里（而不是编队分支里）：解除时可能旁边没有长机，提示仍要能看到
        if (snap.ElapsedSeconds < formationNoticeUntil)
            opt.FormationNotice = WtHud.UI.L10n.Get(WtHud.UI.LangScope.Alerts,
                formationStage == FormationStage.Disabled ? "guide.formationOff" : "guide.formationDone");
        if (!formationOn)
        {
            ResetGuideState();
            // 编队辅助关掉时不再有站位这回事：第 11 段整段不发，
            // 否则长机那边会一直挂着一句关掉之前的「到位」。
            ClearSelfWingReport();
        }

        opt.GridText = "";
        opt.EdgeWarning = false;
        opt.NavFriendly = "";
        opt.NavEnemy = "";

        // 没有自机坐标（8111 半初始化 / 不在对局）：站位判定无从谈起。
        // ★ 必须主动清掉上一帧的状态：否则本机一边报着「到位」、报文里却没有坐标，
        //   长机看到的就是一句假话。保留槽位 → 长机显示「数据错误」。
        if (map.Self is not { } self || !snap.Valid)
        {
            if (formationOn && !cfg.LeadMode) ClearSelfWingReport(cfg.FormationSlot);
            else ClearSelfWingReport();
            return;
        }

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
            // 位置和方位要一起排序、一起截断：数据链配对靠位置，一旦两张表错位，
            // 三机编队就会「圈指向 A、高度取 B」。所以这里先攒成元组再统一排序。
            var contacts = new List<(FriendlyContact C, (double X, double Y) P)>();
            foreach (var f in map.Friendlies)
            {
                // 与数据链那条路共用一个公式：以前这里是内联的同一段代码，
                // 两处只要有一边改了（比如 yFlip 的处理），地图和数据链的方位就会悄悄分家。
                var (range, rel) = WtHud.Hud.FormationResolver.Relative(
                    self.X, self.Y, f.X, f.Y, sizeX, sizeY, cfg.MapYFlip, navHeading);
                contacts.Add((new FriendlyContact(rel, range, f.Icon), (f.X, f.Y)));
            }
            contacts.Sort((a, b) => a.C.RangeM.CompareTo(b.C.RangeM));
            if (contacts.Count > 4) contacts.RemoveRange(4, contacts.Count - 4);
            foreach (var c in contacts)
            {
                opt.Friendlies.Add(c.C);
                friendPos.Add(c.P);
            }

            // ================= 长机模式 =================
            // 长机不追人：只看自己有没有在设定航向 / 设定马赫上，僚机只当"状态"报，不给数字。
            // 数据链上长机的目标坡度：僚机侧显示一行，用来确认广播生效。
            // 长机自己收不到（对端才有这个值），所以这里不需要区分角色。
            opt.LeadTargetBankDeg = dataLink is { } dl && dl.TryGetPartnerTargetBank(out double partnerBank)
                ? partnerBank
                : (double?)null;

            if (cfg.LeadMode)
            {
                opt.Guide = null;

                // 长机航向：有数据链就用长机自报值，没有就用本机航向近似（两者实测差别不大）
                double linkedHdg = 0;
                bool hasLinkedHdg = dataLink != null && dataLink.TryGetPartnerHeading(out linkedHdg);

                // 航向基准：
                //  - 全向跟随 ON：实时长机航向（长机可全向机动，不再被要求转回某个设定航向）
                //  - OFF：配置给了就用配置；-1 表示留空 → 锁存进入长机模式那一刻的航向
                double targetHdg;
                if (cfg.FormationOmniFollow)
                {
                    targetHdg = ResolveLeadHeading(hasLinkedHdg ? (double?)linkedHdg : null, navHeading);
                    leadHeadingLockValid = false;          // 切回 OFF 时重新锁存
                }
                else if (cfg.LeadHeadingDeg >= 0)
                {
                    targetHdg = cfg.LeadHeadingDeg;
                    leadHeadingLockValid = false;          // 以后改回留空时重新锁存
                }
                else
                {
                    if (!leadHeadingLockValid && hasHeading)
                    {
                        leadHeadingLocked = navHeading;
                        leadHeadingLockValid = true;
                    }
                    targetHdg = leadHeadingLocked;
                }

                // 僚机表先算出来：它同时决定主状态和「要不要跑自己的状态机」。
                // ★ 链路模式下长机不跑状态机 —— 僚机自报的到位/掉队是权威结果，
                //   长机再按自己的地图算一遍只会算出第二套结论。
                // ★ 用 RecentNodes 而不是 OnlineNodes：掉线的那架要留在表里说「掉线」，
                //   3 秒硬线会让整行凭空消失，长机只会以为"本来就没这架"。
                var wingRows = WingTable(dataLink?.RecentNodes(WingOfflineKeepMs), cfg.DataLinkId);

                // 主状态：僚机在不在位（长机唯一需要关心的事）
                string main;
                bool warn = false;
                if (opt.Friendlies.Count == 0)
                {
                    main = WtHud.UI.L10n.Get(WtHud.UI.LangScope.Alerts, "guide.lead.none");
                    warn = true;
                    ResetGuideState();
                }
                else
                {
                    var wing = opt.Friendlies[NearestFriendlyIndex(opt.Friendlies)];
                    double wingClosing = UpdateClosingRate(wing.RangeM, snap.ElapsedSeconds);
                    // 长机同样要走到位判定：FORMATION SET 是给长机的「可以开始机动了」信号，
                    // 只有僚机跑状态机的话，长机永远等不到这句。长机没有「站位」这回事，
                    // 方位传 0 = 永远对齐，期望距离用长机自己的配置。
                    // ★ 统一模型之后编号只是编号，推不出僚机的站位 —— 不再按僚机槽位取几何。
                    // 没有僚机自报时才跑长机自己的状态机（无链路模式保留现状）
                    if (wingRows.Count == 0)
                        UpdateFormationStage(wing.RangeM, 0, snap.ElapsedSeconds, cfg);

                    if (wing.RangeM < 200)
                    {
                        main = WtHud.UI.L10n.Get(WtHud.UI.LangScope.Alerts, "guide.lead.close");
                        warn = true;
                    }
                    else if (wing.RangeM > 1000 || (wing.RangeM > 600 && wingClosing < -10))
                    {
                        main = WtHud.UI.L10n.Get(WtHud.UI.LangScope.Alerts, "guide.lead.late");
                        warn = true;
                    }
                    else
                    {
                        main = WtHud.UI.L10n.Get(WtHud.UI.LangScope.Alerts, "guide.lead.formation");
                    }
                }

                // ★ 有僚机自报时以自报为准：主行只说整体，具体谁掉了看下面那张表。
                //   表是逐架的（到位 / 掉队 480m / 偏离站位 / 数据错误 / 掉线）。
                //   ★ 数据错误和掉线都不参与完整性：一个人出问题不该把整队判成不完整。
                //     但一架都没判定出来时不能说「编队完整」—— 有人掉线就说掉线，
                //     否则说数据错误。规则集中在 WingVerdict，别在这里再抄一份。
                if (wingRows.Count > 0)
                {
                    switch (WingVerdict(wingRows))
                    {
                        case LeadWingVerdict.Complete:
                            main = WtHud.UI.L10n.Get(WtHud.UI.LangScope.Alerts, "guide.lead.formation");
                            warn = false;
                            break;
                        case LeadWingVerdict.Incomplete:
                            main = WtHud.UI.L10n.Get(WtHud.UI.LangScope.Alerts, "guide.lead.late");
                            warn = true;
                            break;
                        case LeadWingVerdict.Offline:
                            main = WtHud.UI.L10n.Get(WtHud.UI.LangScope.Alerts, "guide.lead.wingOffline");
                            warn = true;
                            break;
                        default:
                            main = WtHud.UI.L10n.Get(WtHud.UI.LangScope.Alerts, "guide.wing.dataErr");
                            warn = true;
                            break;
                    }
                }
                foreach (var n in wingRows)
                    opt.LeadWingRows.Add((WingLabel(n), WingStateText(n),
                                          n.Wing != WtHud.Telemetry.DataLink.WingState.InPosition));

                // 副状态：姿态偏差（只报方向，不给数字）
                var subs = new System.Collections.Generic.List<string>();

                // 全向跟随时基准就是长机实时航向，不再提示「转回设定航向」；马赫检查照旧保留
                if (!cfg.FormationOmniFollow
                    && ((hasHeading && leadHeadingLockValid) || cfg.LeadHeadingDeg >= 0))
                {
                    double hdgErr = targetHdg - navHeading;
                    while (hdgErr > 180) hdgErr -= 360;
                    while (hdgErr < -180) hdgErr += 360;
                    opt.TargetHeadingDeg = targetHdg;      // 航向带上的琥珀三角
                    if (Math.Abs(hdgErr) > 5)
                        subs.Add(string.Format(WtHud.UI.L10n.Get(WtHud.UI.LangScope.Alerts,
                            hdgErr > 0 ? "guide.lead.turnR" : "guide.lead.turnL"), Math.Abs(hdgErr)));
                }

                // 马赫：目标马赫按当前音速换算成真空速再比，避免高度变化时误判
                if (cfg.LeadMach > 0.05 && snap.Mach > 0.05 && snap.TasKmh > 50)
                {
                    double soundKmh = snap.TasKmh / snap.Mach;
                    double targetTas = cfg.LeadMach * soundKmh;
                    double tasErr = targetTas - snap.TasKmh;
                    // 文字只说「加/减」，条才能说清「加多少、到哪停」
                    opt.LeadSpeedBar = (snap.TasKmh, targetTas, tasErr);
                    if (Math.Abs(tasErr) > 25)
                        subs.Add(WtHud.UI.L10n.Get(WtHud.UI.LangScope.Alerts, tasErr > 0 ? "guide.lead.add" : "guide.lead.reduce"));
                }

                // 链路状态：只在**没有**伙伴时提示 —— 有链路是常态，平时不该占视觉。
                // 僚机的转弯提前量靠这条链路，长机得知道自己的状态有没有送出去。
                if (dataLink == null || dataLink.NodeCount == 0)
                    subs.Add(WtHud.UI.L10n.Get(WtHud.UI.LangScope.Alerts, "guide.lead.nolink"));

                // 转向辅助：只在长机模式下有意义（僚机看的是长机怎么转，不是自己）。
                opt.TurnAssist = BuildTurnAssistCue(cfg, snap, snap.ElapsedSeconds);

                opt.LeadStatusMain = main;
                opt.LeadStatusSub = string.Join("  ·  ", subs);
                opt.LeadStatusWarn = warn;
                ClearSelfWingReport();     // 自己就是长机，不该再发僚机站位
                return;                    // 长机不再走僚机那套引导
            }
            opt.LeadStatusMain = "";
            opt.LeadStatusSub = "";
            opt.LeadWingRows.Clear();
            opt.TurnAssist = null;
            // 切回僚机就退出辅助：状态留着会在下次当长机时凭空亮起来
            if (turnAssist.Stage != TurnAssistStage.Off)
            {
                turnAssist = new TurnAssistState(TurnAssistStage.Off, 0, -1);
                turnAssistBankDeg = 0;
            }

            // 高度差：map_obj 没有 z，这一项只能来自数据链。正 = 对方比我高（我该爬）。
            opt.AltDiffM = null;
            opt.Vertical = null;
            opt.AltDiffStale = false;
            if (dataLink != null && snap.AltM > 0)
            {
                if (dataLink.TryGetPartnerAltitude(out int partnerAlt, out _, out double altAge)
                    && altAge < 3.0)
                {
                    lastAltDiffM = partnerAlt - snap.AltM;
                    lastAltDiffAtMs = Environment.TickCount64;
                    hasLastAltDiff = true;
                }

                double sinceS = (Environment.TickCount64 - lastAltDiffAtMs) / 1000.0;
                if (hasLastAltDiff && sinceS <= AltHoldStaleS)
                {
                    opt.AltDiffM = lastAltDiffM;
                    // 还没到 3 s 的超时线就是新鲜值；超过就标「旧」（最多再显示 2 s）
                    opt.AltDiffStale = sinceS > 3.0;
                }
                else if (!hasLastAltDiff)
                {
                    opt.AltDiffM = null;
                }
            }

            // --- 目标解析：数据链优先，取不到才用地图像素 ---
            // 两条路径的差别只在这一步：有链路就直接认节点（不依赖 map_obj 的友机列表，
            // 也就不会把「最近的僚机」当成长机）；无链路才退回「最近的友机」。
            // 两者都需要自机坐标才能给水平量 —— 拿不到就只留垂直引导。
            // 本机速度（归一化单位/秒）：把地图给的位置外推到「现在」用。
            // 方向取 navHeading（无罗盘时已退化为航迹角），与 Relative() 的方位定义一致。
            double hdgRad = navHeading * Math.PI / 180.0;
            double selfVxPerS = snap.TasKmh / 3.6 * Math.Sin(hdgRad) / sizeX;
            double selfVyPerS = snap.TasKmh / 3.6 * Math.Cos(hdgRad) / (sizeY * (cfg.MapYFlip ? -1 : 1));

            var target = ResolveFormationTarget(cfg, map, dataLink, opt, sizeX, sizeY, navHeading,
                                                snap.ElapsedSeconds, selfVxPerS, selfVyPerS);
            if (target.Source != FormationSource.None)
            {
                double leadRange = target.RangeM;
                double leadBearing = target.RelBearingDeg;
                UpdateFormationLinkLine(cfg, opt, dataLink, target, snap.ElapsedSeconds);

                // 长机航向 / 转向率：油门前馈和转弯提前量共用，放在 PID 之前算好。
                // 有数据链就用长机自报值，没有就用本机航向近似 / 本地估算（实测差别不大）。
                double leadHdgDelta = 0;
                if (dataLink != null && dataLink.TryGetPartnerHeading(out double partnerHdg))
                    leadHdgDelta = NormalizeDeg(partnerHdg - navHeading);

                double leadRate = 0;
                bool leadFromLink = false;
                if (cfg.FormationLeadFeed)
                {
                    // 长机认准了才信它的转向率。判断已经收进 DataLink：手填 ID / 唯一 role=1 /
                    // 场上只有一个节点 → 可信；多候选退回「最近活跃」时它自己返回 false。
                    // ★ 以前这里是 `NodeCount == 1`，三方编队时直接失效 —— 认长机的能力就体现在这。
                    if (dataLink != null && dataLink.TryGetPartnerTurnRate(out double linkedRate))
                    {
                        leadRate = linkedRate;
                        leadFromLink = true;
                    }
                    else
                    {
                        leadRate = EstimateLeadTurnRate(leadBearing, ownTurnRateDeg, snap.ElapsedSeconds);
                    }
                }

                double closing = UpdateClosingRate(leadRange, snap.ElapsedSeconds);

                // 本机要飞的站位（形状 + 槽位）。后面所有站位几何、到位判定、速度前馈都读它，
                // 避免出现「引导按新队形算、油门还按旧距离算」的分叉。
                var slot = WtHud.Hud.FormationGeometry.Station(cfg);

                // ---- 垂直引导 ----
                // 目标高度差 = 长机高度 + 槽位高度（UpM 正 = 站位比长机高），所以目标 ΔH = −UpM。
                // 误差 = 当前 ΔH − 目标 ΔH，正 = 我偏低、要爬。
                if (opt.AltDiffM is double currentDiff)
                {
                    double altErr = currentDiff - slot.TargetDeltaHM;
                    // 显示/指令用平滑值，阶段判定用原始值：4 Hz 的跳变不该让横带和数字抖
                    double altShown = SmoothVertError(altErr, snap.ElapsedSeconds);
                    double vertGate = Math.Max(5, cfg.FormationVertGateM);

                    // ---- 垂直两阶段（2026-11-12；0.4.24 起默认关）----
                    // 开「只显示爬升率」时：高度没到位就只给爬升率，横向引导完全不画
                    // （圈、航向、距离、油门都不出），爬进门槛后才开始横向。
                    // ★ 默认关：飞行员反馈「只给距离、不知道往哪转」更难受，所以默认全套一起显示。
                    // 迟滞：进入用门槛，退出用 60% 门槛，否则在门槛附近会一小时里反复切换。
                    bool altStage = formationStage == FormationStage.Converge
                                    && (altitudeStageActive
                                        ? Math.Abs(altErr) > vertGate * 0.6
                                        : Math.Abs(altErr) > vertGate);
                    altitudeStageActive = altStage;
                    bool vertFirst = VerticalFirstActive(cfg, altStage);

                    // 「爬升显示距离」只在只显示爬升率时才有意义：横向全关时补一行距离。
                    // 默认模式本来就有距离行，这一项不参与。
                    double? farRange = vertFirst && RevealRangeWhileClimbing(leadRange, cfg.FormationFarRangeM)
                        ? leadRange
                        : null;

                    opt.Vertical = new VerticalCue(
                        altShown,
                        VerticalRateCmd(altShown, cfg.FormationVertMaxMs),
                        opt.AltDiffStale,
                        vertFirst,
                        Math.Abs(altErr) <= vertGate,
                        farRange,
                        farRange is null ? null : slot.RangeM);
                }

                // 没有自机坐标：认得长机，但算不出它在哪一侧。
                // ★ 这是错误状态（8111 半初始化 / 不在对局），不是"没有僚机"——
                //   必须明说，否则飞行员只会看到"圈不见了"。
                if (!target.HasHorizontal)
                {
                    opt.Guide = null;
                    opt.TargetHeadingDeg = null;
                    ClearSelfWingReport(cfg.FormationSlot);   // 没有水平量就没有站位误差，别发上一帧的残留
                    opt.FormationHint = WtHud.UI.L10n.Get(WtHud.UI.LangScope.Alerts,
                        target.LeadHidden ? "formation.leadHidden" : "formation.noSelfPos");
                }
                else
                {
                    FormationAction action = FormationAction.None;
                    double deltaKmh = 0;

                    // 站位偏差：从当前视线指向理想槽位的角度，站准时为 0。
                    // ★ 只判距离会出现「正后方 500 m」和「左后 45° 500 m」都算到位 ——
                    //   后者压根不在槽位上。这个角就是判「位置」的那一维，油门和转向共用同一个数。
                    double aimDeg = WingAimDeg(leadBearing, leadRange, leadHdgDelta,
                                               slot.SideM, slot.BackM);

                    // 距离误差（米，正 = 比槽位远）。原来只在油门那段里算，站位自报也要用同一个数，
                    // 提到这里避免两处各算一份、符号还不一样。
                    double err = leadRange - slot.RangeM;

                    // 偏角大时先转向，不给油门建议：斜着几十度去修速度没有意义。
                    // 爬升阶段整段跳过：横向引导还没开始，PID 积分不该在场外偷偷累积。
                    if (opt.Vertical is not { Priority: true } && Math.Abs(leadBearing) < 50)
                    {
                        // 级联 PID：外环 PI 把距离误差换成"期望接近率"，内环 P 把接近率之差换成油门。
                        // 三个增益都在 hud-config.json 里（formationKp / formationKi / formationKv）。
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
                        double cosOff = Math.Max(0.5, Math.Cos(leadBearing * Math.PI / 180));
                        double dvRaw = (wantClosing - closing) * cfg.FormationKv / cosOff * 3.6;

                        // 僚机速度前馈：外圈路径更长，按 |ω|·(r_w−ρ) 提前补速度差；
                        // 纯反馈 PID 要等距离拉大才动作，天然滞后。前馈与反馈一起过低通避免突变。
                        double tasRef = Math.Max(50, snap.TasKmh);
                        double feed = FormationFeedKmh(leadRate, leadRange, snap.TasKmh,
                            slot.SideSign, leadBearing, leadHdgDelta,
                            cfg.FormationWingMinRatio, cfg.FormationWingMaxRatio);
                        // 总增量按长机真空速倍数上下限收窄：外圈不许超速、内圈不许失速
                        deltaKmh = Math.Clamp(SmoothGuideDelta(dvRaw + feed, nowS, cfg.FormationGuideTauS),
                            tasRef * (cfg.FormationWingMinRatio - 1),
                            tasRef * (cfg.FormationWingMaxRatio - 1));

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
                            double holdAim = Math.Max(0.5, cfg.FormationAlignDeg) * (wasHold ? 1.6 : 1.0);
                            if (Math.Abs(err) <= holdErr && Math.Abs(closing) < holdClosing
                                && Math.Abs(aimDeg) <= holdAim)
                                action = FormationAction.Hold;
                        }
                    }

                    // 滞回状态每帧都更新（偏角大到没给建议时也要清掉，否则残留到下一段）
                    guideActionPrev = action;

                    // 自报站位：长机不看地图也能知道谁掉队了。
                    // 槽位号直接用配置里的那个 —— 三人编队时它同时也是「我是第几号僚机」。
                    selfWingSlot = Math.Clamp(cfg.FormationSlot, 0, 8);
                    selfWingErrM = (int)Math.Round(Math.Clamp(err, -9999, 9999));
                    selfWingState = WingStateFrom(err, aimDeg, slot.RangeM, cfg);

                    // ---- 转弯提前量：把机头指向长机的未来位置 ----
                    // 优先用数据链里长机自报的转向率（准、无滞后）；没有就退化为相对方位变化率反推。
                    // 油门那一侧不动：提前量只改"往哪转"，距离仍由 PID 管。
                    // 设定了侧位后按内外圈分治：提前量按转弯半径重算、外圈反向，避免切进长机航线。
                    // leadHdgDelta / leadRate 已在上方（PID 之前）算好，油门前馈与转弯提前量共用

                    // 侧位设定：瞄准点从「长机本体」移到「长机侧后方」；0 = 正后方，保持旧行为
                    double guideTurn = aimDeg;
                    // 提前量只在开关打开且转向率过门槛时叠加；但 leadRate 本身保留 ——
                    // 「超载提示」要独立判断：关了提前量不等于就不该提醒「转弯过急」。
                    double leadRateShown = 0;
                    if (cfg.FormationLeadFeed && Math.Abs(leadRate) >= Math.Max(0.2, cfg.FormationLeadMinRate))
                    {
                        guideTurn += FormationLeadAheadDeg(leadRate, leadRange, snap.TasKmh,
                                                           Math.Max(5, cfg.FormationLeadMaxDeg),
                                                           slot.SideSign);
                        guideTurn = NormalizeDeg(guideTurn);
                        leadRateShown = leadRate;   // 只有真叠了提前量，才需要解释"数值为什么比目视大"
                    }

                    double turnG = TurnLoadG(leadRate, snap.TasKmh);
                    bool overG = cfg.FormationWarnG > 0.05 && turnG >= cfg.FormationWarnG;

                    // ---- 两阶段：收敛阶段给明确指令，保持阶段只在小偏差时才开口 ----
                    // HUD 是显示层，sim 里那套「角速度前馈」没法直接搬（飞行员不是自动驾驶），
                    // 能落地的是**指令的松紧**：已经在 40 m 内的时候，指针还一直在动反而看不清，
                    // 所以保持阶段把小偏差直接归零，让界面安静下来；真偏了照样报。
                    if (formationStage == FormationStage.Maintain)
                    {
                        // 死区随站位一起调：站位改大时同样的角度对应更大的横向偏差
                        if (Math.Abs(guideTurn) < cfg.FormationSteadyDeg) guideTurn = 0;
                        if (Math.Abs(deltaKmh) < cfg.FormationSteadyKmh)
                        {
                            deltaKmh = 0;
                            action = FormationAction.Hold;
                        }
                    }

                    // 爬升阶段：横向引导完全不出现（圈/航向/距离/油门），只留那条爬升率。
                    // 这不是「压掉油门建议」，而是「还没轮到横向」——两阶段的分界。
                    if (opt.Vertical is { Priority: true })
                        action = FormationAction.None;

                    // 编队状态机：判定到位（40 m 持续 2 s）→ 提示「编队完成」
                    UpdateFormationStage(leadRange, aimDeg, snap.ElapsedSeconds, cfg);

                    // 解除后不画本轮引导（友机标记仍在航向带上，那是另一条线）
                    // ★ 这里必须带大括号：不带的写法会让下面的 Guide 赋值跑到 if/else 之外，
                    //   解除后每帧又把 Guide 设回来，解除键等于失效。（曾经这样错过一次。）
                    if (formationStage == FormationStage.Disabled)
                    {
                        opt.Guide = null;
                        // ★ 解除后不再判定：还发「到位」就是拿上一帧的结论骗长机。
                        //   槽位保留 → 长机那一行变成「数据错误」，而不是凭空消失。
                        ClearSelfWingReport(cfg.FormationSlot);
                    }
                    else if (opt.Vertical is { Priority: true })
                    {
                        // 阶段 1：只画爬升率。连目标航向三角也收掉，免得两个阶段抢同一块注意力。
                        opt.Guide = null;
                        opt.TargetHeadingDeg = null;
                    }
                    else
                    {
                        opt.TargetHeadingDeg = NormalizeDeg(navHeading + guideTurn);
                        opt.Guide = new FormationLead(guideTurn, leadRange,
                                                 slot.RangeM, action, deltaKmh,
                                                 leadRateShown, leadFromLink, turnG, overG,
                                                 snap.TasKmh);
                    }
                }
            }
            else
            {
                opt.Guide = null;
                ResetGuideState();
                // 没认到长机：同样发不出判定。保留槽位，长机显示「数据错误」而不是当这架不存在。
                ClearSelfWingReport(cfg.FormationSlot);

                // 找不到长机：分两种情况说清。长机模式有自己的状态行（guide.lead.none），不重复。
                if (formationOn && !cfg.LeadMode)
                {
                    string filter = (cfg.LeadIcon ?? "").Trim();
                    opt.FormationHint = filter.Length > 0 && map.FriendlyTotal > 0
                        ? string.Format(WtHud.UI.L10n.Get(WtHud.UI.LangScope.Alerts, "formation.noMatch"), filter)
                        : WtHud.UI.L10n.Get(WtHud.UI.LangScope.Alerts, "formation.noFriendly");
                }
            }

            // ★ 两个 role=1 且没手填长机 ID：SelectLead 会退到「最近活跃」，
            //   圈和航向会在两架之间来回跳。这不等于"找不到长机"，但同样是飞行员
            //   必须知道的事 —— 只放在面板里等于没说。
            //   不覆盖上面更具体的提示（没自机坐标 / 没认到长机）。
            if (!cfg.LeadMode && cfg.DataLinkLeadId <= 0 && opt.FormationHint.Length == 0
                && dataLink != null && dataLink.TryGetLeadNode(out _, out _, out bool leadAmb) && leadAmb)
                opt.FormationHint = WtHud.UI.L10n.Get(WtHud.UI.LangScope.Alerts, "formation.leadAmbiguous");
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
    private static WtHud.Input.KeyBinding _formationKeyBinding = WtHud.Input.KeyBinding.Parse("CTRL+SHIFT+F", 0x46, "CTRL+SHIFT+F");
    private static bool formationKeyPrev;   // 边沿检测：按一下切换，不是按住生效

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
        _formationKeyBinding = WtHud.Input.KeyBinding.Parse(
            string.IsNullOrWhiteSpace(c.FormationKey) ? "CTRL+SHIFT+F" : c.FormationKey, 0x46, "CTRL+SHIFT+F");
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

    // 油门建议的平滑状态。closing 本身有噪声，不滤波的话数字会在几百 km/h 之间跳。
    private static double guideDeltaSmooth;
    private static double guideDeltaLastT;
    private static bool guideDeltaValid;
    private static FormationAction guideActionPrev;

    // 级联 PID 的积分项。只有外环没顶到限幅、油门也没饱和时才累积（抗积分饱和）。
    private static double guideIntegral;
    private static double guideIntegralLastT;
    private const double GuideIntegralLimit = 400.0;   // m·s，够大但不至于失控

    // 长机模式：航向留空（leadHeadingDeg = -1）时锁存进入那一刻的航向，偏离了才好提醒回来。
    private static double leadHeadingLocked;
    private static bool leadHeadingLockValid;

    // ---- 转弯提前量（lead pursuit）----
    // 转向率是角度的一阶导数，只能用窗口内的首尾差估：单帧差分在 4 Hz 上全是噪声。
    // 1.0 秒窗口 = 罗盘 ±0.5° 抖动折成 ±0.5°/s，而转弯信号是 5~15°/s，够用且不迟钝。
    private const double TurnRateWindowS = 1.0;
    private const double TurnRateMinSpanS = 0.4;

    /// <summary>自己航向的历史（算自己的转向率，用于广播和「相对方位变化率」的反推）。</summary>
    private static readonly System.Collections.Generic.List<(double T, double Hdg)> ownHeadingHistory = new();

    /// <summary>最近一次算出的自己的转向率（°/s，正 = 右转）。</summary>
    private static double ownTurnRateDeg;

    // ---- 长机转向辅助（2026-11-12）----
    // 只提示不控杆：把「该压多少度」数字化，平滑仍然取决于人的输入。
    private const double TurnAssistRateGate = 1.5;      // °/s：低于它视为「没在转」，不锁方向
    private const double TurnAssistReverseHoldS = 1.0;  // 反向持续多久才自动重锁

    private static TurnAssistState turnAssist = new(TurnAssistStage.Off, 0, -1);

    /// <summary>最近一次算出的目标坡度（带符号）。数据链按它广播，每帧发出的是上一帧的值，100 ms 误差可忽略。</summary>
    private static double turnAssistBankDeg;

    // ---- 垂直引导（2026-11-12）----
    // 链路 3 s 无包时 TryGetPartnerAltitude 会失败。直接把高度差清空会让「高度一致」那行凭空消失，
    // 飞行员会以为自己掉高度了。所以缓存**差值**（不是对方绝对高度：保留绝对高度、自己一爬就错），
    // 过期后继续显示最多 AltHoldStaleS 秒并标注旧数据。
    private const double AltHoldStaleS = 5.0;
    private static double lastAltDiffM;
    private static long lastAltDiffAtMs;
    private static bool hasLastAltDiff;

    /// <summary>是否处在「先爬升」阶段（带迟滞）。编队重编时由状态机重置。</summary>
    private static bool altitudeStageActive;

    /// <summary>地图判定（只跟地图可见）的锁存状态与上次命中时刻，用来压住边界抖动。</summary>
    private static bool mapVisibleLatched;
    private static double mapVisibleLastHitS = double.NegativeInfinity;

    /// <summary>与 <see cref="HudOptions.Friendlies"/> 同序的归一化位置，供数据链按位置配对。</summary>
    private static readonly List<(double X, double Y)> friendPos = new();

    // ---- 本机自报的站位（写进报文第 11 段，让长机知道谁掉队了）----
    private static int selfWingSlot = -1;
    private static int selfWingErrM;
    private static WtHud.Telemetry.DataLink.WingState selfWingState = WtHud.Telemetry.DataLink.WingState.Unknown;

    /// <summary>
    /// 清掉站位状态。
    /// <para><paramref name="slot"/> &gt;= 0 时**保留槽位**、只把状态置为 Unknown —— 那是
    /// 「我在编队里，但这一帧算不出自己在哪」。长机收到 <c>槽位,-1,0</c> 会显示「数据错误」，
    /// 而不是以为这架没参加编队（那条路是整段不发）。</para>
    /// </summary>
    private static void ClearSelfWingReport(int slot = -1)
    {
        selfWingSlot = slot >= 0 ? Math.Clamp(slot, 0, 8) : -1;
        selfWingErrM = 0;
        selfWingState = WtHud.Telemetry.DataLink.WingState.Unknown;
    }

    /// <summary>
    /// 僚机自报的站位状态。判据与状态机「脱离 MAINTAIN」用同一条线，
    /// 否则会出现「僚机屏幕上还在编队里、长机那边已经标掉队」。
    /// <para><paramref name="errM"/> 正 = 比槽位远（掉队），负 = 冲到前面。</para>
    /// </summary>
    internal static WtHud.Telemetry.DataLink.WingState WingStateFrom(
        double errM, double aimDeg, double slotRangeM, HudConfig cfg)
    {
        double pct = FormationRangeErrorPct(slotRangeM + errM, slotRangeM);
        double dropPct = Math.Clamp(cfg.FormationDropPct, 1, 500);
        if (pct > dropPct) return errM >= 0
            ? WtHud.Telemetry.DataLink.WingState.Behind
            : WtHud.Telemetry.DataLink.WingState.Ahead;

        if (Math.Abs(aimDeg) > Math.Max(0.5, cfg.FormationAlignDeg) * 2.0)
            return WtHud.Telemetry.DataLink.WingState.OffStation;

        return WtHud.Telemetry.DataLink.WingState.InPosition;
    }

    /// <summary>
    /// 长机侧：从在线节点里挑出「不在槽位上」的僚机，按偏离量从大到小。
    /// <para>只认僚机自报的状态，长机不自己判 —— 槽位和阈值都只有僚机知道。</para>
    /// </summary>
    internal static List<WtHud.Telemetry.DataLink.LinkNode> WingReports(
        IReadOnlyList<WtHud.Telemetry.DataLink.LinkNode>? nodes)
    {
        var list = new List<WtHud.Telemetry.DataLink.LinkNode>();
        if (nodes is null) return list;
        foreach (var n in nodes)
            if (n.Wing is WtHud.Telemetry.DataLink.WingState.Behind
                      or WtHud.Telemetry.DataLink.WingState.Ahead
                      or WtHud.Telemetry.DataLink.WingState.OffStation)
                list.Add(n);
        list.Sort((a, b) => Math.Abs(b.ErrM).CompareTo(Math.Abs(a.ErrM)));
        return list;
    }

    /// <summary>
    /// 僚机侧的「连的是哪个长机」。收敛阶段常驻，进入保持 3 s 后淡出，掉队立刻回来。
    /// <para>只有在**真的认出长机**时才画；认不出来由 <see cref="HudOptions.FormationHint"/> 说原因，
    /// 两者同一格，不会再叠一行字。</para>
    /// </summary>
    private static void UpdateFormationLinkLine(HudConfig cfg, HudOptions opt,
                                                WtHud.Telemetry.DataLink? link,
                                                FormationTarget target, double nowS)
    {
        if (cfg.LeadMode || target.Source == FormationSource.None) return;

        // 收敛/掉队阶段：每帧把隐藏时刻往后推，等于常驻；进入保持后停止推，3 s 后自己消失
        if (formationStage != FormationStage.Maintain) linkLineHideAt = nowS + 3.0;
        if (nowS >= linkLineHideAt) return;

        if (link != null && link.TryGetLeadNode(out var lead))
        {
            double age = lead.AgeMs / 1000.0;
            opt.FormationLinkText = string.Format(
                WtHud.UI.L10n.Get(WtHud.UI.LangScope.Screen, "formation.linkLead"),
                lead.Id, age.ToString("0.0"));
            // 超过一秒的包说明对方刚卡过/快掉线了，先变黄，别等彻底断了才说
            opt.FormationLinkOk = lead.AgeMs < 1000;
            return;
        }

        // 没有链路但地图上有友军：明确写清「现在跟的是地图上最近的那架」，
        // 否则飞行员会以为链路在起作用，回头查为什么距离数不对。
        if (target.Source == FormationSource.Map)
        {
            opt.FormationLinkText = WtHud.UI.L10n.Get(WtHud.UI.LangScope.Screen, "formation.linkMapOnly");
            opt.FormationLinkOk = false;
        }
    }

    /// <summary>
    /// 选编队目标。**这是两条路径唯一的分叉点**：
    /// 有链路就按节点认（不依赖 map_obj 的友机列表），否则退回最近的友机。
    /// 之后 PID、两阶段、垂直引导、绘制全部共用一份。
    /// <para>★ 时间基准有两条，别混：<paramref name="nowS"/> 是 <c>snap.ElapsedSeconds</c>
    /// （遥测时基，与状态机同一根），而包龄 / 地图年龄来自 <c>Environment.TickCount64</c>
    /// （墙钟，与采样线程同一根）。两者都不与绝对时间挂钩，所以各自内部一致就行；
    /// 混着相减会得到几十万秒的「年龄」。</para>
    /// </summary>
    private static FormationTarget ResolveFormationTarget(HudConfig cfg, WtHud.Telemetry.MapData map,
        WtHud.Telemetry.DataLink? link, HudOptions opt, double sizeX, double sizeY, double navHeading,
        double nowS = 0, double selfVxPerS = 0, double selfVyPerS = 0)
    {
        var byLink = default(FormationTarget);
        var leadNode = default(WtHud.Telemetry.DataLink.LinkNode);
        bool haveLink = link != null && link.TryGetLeadNode(out leadNode);
        if (haveLink)
        {
            // ★ 两边都外推到「现在」：链路位置旧 AgeMs，本机地图位置旧 (now − UpdatedAtMs)。
            //   只补一边等于把误差从一边挪到另一边 —— 要补的是两者**年龄之差**。
            var correctedLead = leadNode;
            if (leadNode.Xn is double lx0 && leadNode.Yn is double ly0)
            {
                var (lx, ly) = FormationResolver.Advance(lx0, ly0, leadNode.VelXn, leadNode.VelYn,
                                                         leadNode.AgeMs / 1000.0);
                correctedLead = leadNode with { Xn = lx, Yn = ly };
            }

            double? selfX = map.Self?.X, selfY = map.Self?.Y;
            if (selfX is double sx && selfY is double sy)
            {
                // 地图年龄：本机位置只能从 map_obj 拿，所以它天然旧 0~250 ms。
                // ★ 上限 0.3 s，而且超过 0.6 s 就**完全不外推**：地图停更时（换局、8111 抽风）
                //   Self 会停在旧值、年龄却一直涨，按 0.5 s 外推等于凭 TAS 把飞机推出去几十米，
                //   恢复那一帧又跳回来。数据不可信时用原值，比瞎推强。
                double mapAgeS = (Environment.TickCount64 - map.UpdatedAtMs) / 1000.0;
                double selfAgeS = mapAgeS is > 0 and <= 0.6 ? Math.Min(mapAgeS, 0.3) : 0;
                var (nx, ny) = FormationResolver.Advance(sx, sy, selfVxPerS, selfVyPerS, selfAgeS);
                selfX = nx;
                selfY = ny;
            }

            byLink = FormationResolver.ByLink(correctedLead, selfX, selfY,
                                              sizeX, sizeY, cfg.MapYFlip, navHeading);
            if (byLink.HasHorizontal)
            {
                // 「只跟地图可见」（默认开）：地图上没显示这架友机，就当没看见。
                // 全真 1 km 外不给友机图标，这一条会把超距的链路位置挡回去 ——
                // 关掉之后才允许纯报文位置，可以超过 1 km。
                // ★ 锁存 0.8 s：判据在边界附近会逐帧翻转，不锁存的话提示一闪一闪
                //   （近距离角度抖动、长机刚好压在 1 km 上，都会这样）。
                if (cfg.FormationMapOnly
                    && !FormationResolver.LatchVisible(
                        FormationResolver.VisibleOnMap(opt.Friendlies, byLink.RangeM, byLink.RelBearingDeg),
                        ref mapVisibleLatched, ref mapVisibleLastHitS, nowS))
                    return byLink with { HasHorizontal = false, LeadHidden = true };
                return byLink;
            }
        }

        // 链路认得长机、但对方报文没带坐标（它自己 map_obj 空）——这时退回地图友机：
        // 至少本机地图知道它在哪，和旧行为一致；配对不上时锁最近的，也是旧行为。
        if (opt.Friendlies.Count > 0)
        {
            var f = opt.Friendlies[NearestFriendlyIndex(opt.Friendlies)];
            return FormationResolver.ByMap(f.RangeM, f.RelBearingDeg);
        }

        // 两条路都给不出水平量：返回链路结果（Source=Link, HasHorizontal=false），
        // 调用方据此只留垂直引导并给出「无自机坐标」的警告。
        return byLink;
    }

    /// <summary>长机主状态该怎么写。数据来自僚机表逐行判定，规则集中在这里。</summary>
    internal enum LeadWingVerdict
    {
        /// <summary>有判定的僚机全部到位。</summary>
        Complete,
        /// <summary>至少一架有判定的僚机不在位。</summary>
        Incomplete,
        /// <summary>没有一架给出判定，而且没人掉线 —— 全是「数据错误」。</summary>
        DataError,
        /// <summary>没有一架给出判定，且有人已经掉线（3 s 收不到包）。</summary>
        Offline,
    }

    /// <summary>
    /// 长机主状态的判定：只看**有判定**的僚机。
    /// <para>★ 数据错误和掉线都不参与完整性 —— 一个人出问题不该把整队判成不完整；
    /// 但一架都没判定出来时不能说「编队完整」，那同样是一句假话：有人掉线就说掉线，
    /// 否则说数据错误。</para>
    /// </summary>
    internal static LeadWingVerdict WingVerdict(
        IReadOnlyList<WtHud.Telemetry.DataLink.LinkNode>? rows)
    {
        int judged = 0;
        bool all = true;
        bool offline = false;
        if (rows is null) return LeadWingVerdict.DataError;
        foreach (var n in rows)
        {
            if (n.AgeMs >= WtHud.Telemetry.DataLink.StaleMs) { offline = true; continue; }
            if (n.Wing == WtHud.Telemetry.DataLink.WingState.Unknown) continue;
            judged++;
            if (n.Wing != WtHud.Telemetry.DataLink.WingState.InPosition) all = false;
        }
        if (judged > 0) return all ? LeadWingVerdict.Complete : LeadWingVerdict.Incomplete;
        return offline ? LeadWingVerdict.Offline : LeadWingVerdict.DataError;
    }

    /// <summary>僚机掉线后还留在表里的时长。超过就整行撤掉 —— 那已经不是"编队里少了一架"，
    /// 而是这架已经不在这一局了，留着只会越堆越多。</summary>
    internal const int WingOfflineKeepMs = 60_000;

    /// <summary>
    /// 这架僚机算不算「我这队的」。
    /// <para>报文第 12 段是它自报的跟随长机 ID：等于我的 ID = 我的；等于 0 = 没指定。
    /// ★ 0 必须算进来：面板里那一项默认就是 0，如果把它排除，单长机场景整张表会空掉。
    /// 代价是两家长机同时在线时，没填 ID 的僚机会同时出现在两张表里 —— 想分开就填 ID。</para>
    /// <para><paramref name="myId"/> &lt;= 0 表示不做过滤（自检/无链路时用）。</para>
    /// </summary>
    internal static bool IsMyWing(WtHud.Telemetry.DataLink.LinkNode n, int myId)
        => myId <= 0 || n.LeadId == 0 || n.LeadId == myId;

    /// <summary>
    /// 长机 HUD 上的僚机表：只要**发过第 11 段**的节点（槽位 &gt;= 0），按槽位排序。
    /// <para>★ 入表条件不是「状态已知」：状态 Unknown 恰恰是「数据错误」那一档，必须列出来。
    /// 掉线的（包龄 &gt;= 3 s）也留着，状态列写「掉线」——见 <see cref="WingOfflineKeepMs"/>。
    /// 没发第 11 段的节点是"没开编队辅助/老版本"，不属于这支编队，不进表。</para>
    /// </summary>
    internal static List<WtHud.Telemetry.DataLink.LinkNode> WingTable(
        IReadOnlyList<WtHud.Telemetry.DataLink.LinkNode>? nodes, int myId = -1)
    {
        var list = new List<WtHud.Telemetry.DataLink.LinkNode>();
        if (nodes is null) return list;
        foreach (var n in nodes)
            if (n.Slot >= 0 && n.Role == WtHud.Telemetry.DataLink.LinkRole.Wing && IsMyWing(n, myId))
                list.Add(n);
        list.Sort((a, b) =>
        {
            int sa = a.Slot >= 0 ? a.Slot : int.MaxValue;
            int sb = b.Slot >= 0 ? b.Slot : int.MaxValue;
            return sa != sb ? sa.CompareTo(sb) : a.Id.CompareTo(b.Id);
        });
        return list;
    }

    /// <summary>
    /// 表里那一格的状态词：到位 / 掉队 480m / 冲出 320m / 偏离站位 / 数据错误 / 掉线 12s。
    /// <para>数据错误 = 僚机在编队里，但这一帧给不出判定（没自机坐标 / 没认出长机）。
    /// 掉线 = 已经 3 s 没收到它的包，上一帧的「到位」不再算数。</para>
    /// </summary>
    internal static string WingStateText(WtHud.Telemetry.DataLink.LinkNode n)
    {
        var alerts = WtHud.UI.LangScope.Alerts;
        // 掉线优先于一切自报：包一停，最后那句"我在位"就只是历史。
        if (n.AgeMs >= WtHud.Telemetry.DataLink.StaleMs)
            return string.Format(WtHud.UI.L10n.Get(alerts, "guide.wing.offline"),
                                 Math.Max(1, n.AgeMs / 1000));
        return n.Wing switch
        {
            WtHud.Telemetry.DataLink.WingState.InPosition => WtHud.UI.L10n.Get(alerts, "guide.wing.ok"),
            WtHud.Telemetry.DataLink.WingState.Behind => string.Format(
                WtHud.UI.L10n.Get(alerts, "guide.wing.behind"), Math.Abs(n.ErrM)),
            WtHud.Telemetry.DataLink.WingState.Ahead => string.Format(
                WtHud.UI.L10n.Get(alerts, "guide.wing.ahead"), Math.Abs(n.ErrM)),
            WtHud.Telemetry.DataLink.WingState.OffStation => WtHud.UI.L10n.Get(alerts, "guide.wing.off"),
            _ => WtHud.UI.L10n.Get(alerts, "guide.wing.dataErr"),
        };
    }

    // ---- 队标校验（机场锚点）----
    // 双方在同一条数据链上广播，谁都可能收到另一队的包。机场是固定的、两队看到的颜色相反，
    // 所以「你离哪座机场最近」这个问题在两边各有唯一答案：答案不同 = 不是一队。

    /// <summary>接收线程读的地图快照；主循环每帧写一次。</summary>
    private static WtHud.Telemetry.MapData? _linkMapSnapshot;

    /// <summary>数据链的队标校验入口（挂给 <c>DataLink.TeamCheck</c>）。</summary>
    internal static WtHud.Telemetry.DataLink.TeamVerdict VerifyTeamAnchor(
        double anchorXn, double anchorYn, bool senderFriendly)
    {
        var map = Volatile.Read(ref _linkMapSnapshot);
        return VerifyTeam(map?.Airfields, anchorXn, anchorYn, senderFriendly);
    }

    /// <summary>
    /// 拿本机机场表比对对方自报的锚点：同一座机场，两队看到的颜色相反。
    /// <para>★ 对方锚点只取**友军机场**，而友军机场两边都永远可见 —— 所以「我这边找不到这座机场」
    /// 基本等于「它不是我的友军机场」= 另一队，判 Other。
    /// 这正是全真模式的补丁：敌方机场没点亮时不在我的表里，以前判不出来就放行了。</para>
    /// <para>唯一例外是我的机场表还是空的（刚进图、map_obj 没读到）：这时谁都判不了，
    /// 一律 Unknown 放行，否则进图头几秒会把所有人断掉。</para>
    /// </summary>
    internal static WtHud.Telemetry.DataLink.TeamVerdict VerifyTeam(
        IReadOnlyList<WtHud.Telemetry.MapPoint>? fields,
        double anchorXn, double anchorYn, bool senderFriendly)
    {
        if (fields is null || fields.Count == 0)
            return WtHud.Telemetry.DataLink.TeamVerdict.Unknown;
        if (!TryNearestAirfield(fields, anchorXn, anchorYn, out var field))
            return WtHud.Telemetry.DataLink.TeamVerdict.Other;
        return field.Friendly == senderFriendly
            ? WtHud.Telemetry.DataLink.TeamVerdict.Same
            : WtHud.Telemetry.DataLink.TeamVerdict.Other;
    }

    /// <summary>机场表里离给定归一化坐标最近的一座；超出容差算「对不上」。</summary>
    /// <param name="tolerance">允许的距离（归一化）。选自己的锚点时传 2.0（覆盖全图）：
    /// 最近的友军机场可能在几十公里外，那也得用。</param>
    internal static bool TryNearestAirfield(IReadOnlyList<WtHud.Telemetry.MapPoint> fields,
                                            double xn, double yn,
                                            out WtHud.Telemetry.MapPoint best,
                                            bool friendlyOnly = false,
                                            double tolerance = 0.005)
    {
        best = default;
        double bestSq = double.MaxValue;
        foreach (var f in fields)
        {
            // 发送端只锚友军机场：友军机场两边都永远可见，对方一定认得出来；
            // 锚敌方机场会在对方没点亮时被判成「找不到」。
            if (friendlyOnly && !f.Friendly) continue;
            double dx = f.X - xn, dy = f.Y - yn;
            double d = dx * dx + dy * dy;
            if (d < bestSq) { bestSq = d; best = f; }
        }
        // 容差 0.5% 地图边长：两边读同一张图、同一套归一化坐标，实际只差取整误差 1e-5，
        // 留余量只为防不同帧的刷新抖动。
        return bestSq <= tolerance * tolerance;
    }

    /// <summary>僚机的显示名：有编号就叫 W1/W2，没有就用节点号。</summary>
    internal static string WingLabel(WtHud.Telemetry.DataLink.LinkNode n)
        => n.Slot >= 0 ? "W" + (n.Slot + 1).ToString() : "#" + n.Id.ToString();

    /// <summary>垂直环增益（1/s）。0.15 对应约 6.7 s 时间常数：120 m 用 18 s 左右收掉。</summary>
    internal const double VerticalGain = 0.15;

    /// <summary>
    /// 垂直引导的升降率建议：误差 × 增益，再按机型限幅。
    /// <para>不限幅的话差 300 m 会给出 45 m/s，飞机做不到，指令等于永远失败。
    /// 增益也别再往上抬：仿真里 0.05 时 120 m 走 60 s 还剩 26 m（时间常数 20 s），
    /// 0.15 时 18 s 收敛到 10 m 内且没有过冲。</para>
    /// </summary>
    internal static double VerticalRateCmd(double errorM, double maxMs)
        => Math.Clamp(errorM * VerticalGain, -Math.Abs(maxMs), Math.Abs(maxMs));

    /// <summary>高度读数的一阶平滑时间常数（秒）。4 Hz 的数据直接上屏会每 250 ms 跳一次。</summary>
    internal const double VerticalDisplayTauS = 0.30;

    /// <summary>一阶低通：把 <paramref name="prev"/> 拉向 <paramref name="target"/>。
    /// <para>抽成纯函数是为了能在 selftest 里断言（dt=0 不动、dt≫tau 到底、单调不过冲）。</para></summary>
    internal static double SmoothStep(double prev, double target, double dtS, double tauS)
    {
        if (tauS <= 0 || dtS <= 0) return target;
        double a = 1.0 - Math.Exp(-dtS / tauS);
        return prev + (target - prev) * a;
    }

    // 显示用平滑状态：只在「有高度差」时推进，数据断了就重置，避免拿旧值继续拉。
    private static double vertSmooth;
    private static double vertSmoothT = -1;

    /// <summary>
    /// 把 4 Hz 跳变的高度误差平滑成连续读数。只用于显示与升降率指令，
    /// **阶段判定仍然用原始值** —— 判定要靠真值，不能靠惯性。
    /// <para>断流超过 0.5 s 或时间倒流时直接重置成当前值，不做「追」的动作：</para>
    /// </summary>
    private static double SmoothVertError(double raw, double nowS)
    {
        if (vertSmoothT < 0 || nowS < vertSmoothT || nowS - vertSmoothT > 0.5)
        {
            vertSmooth = raw;
            vertSmoothT = nowS;
            return raw;
        }
        double dt = nowS - vertSmoothT;
        vertSmoothT = nowS;
        vertSmooth = SmoothStep(vertSmooth, raw, dt, VerticalDisplayTauS);
        return vertSmooth;
    }

    /// <summary>
    /// 这次是不是要「只显示爬升率」：开关打开**且**确实在爬升阶段。
    /// <para>抽出来是为了让「默认关」这条能被自检盯住 —— 它决定爬升阶段给不给横向引导，
    /// 改错了不会报错，只会让飞行员觉得「怎么什么都没了」。</para>
    /// </summary>
    internal static bool VerticalFirstActive(HudConfig cfg, bool altitudeStage)
        => cfg.FormationVerticalFirst && altitudeStage;

    /// <summary>
    /// 爬升阶段是否因为「离得太远」而破例放出距离行。
    /// <para>两阶段的分界是高度，但长机一跑起来距离是按秒变的：先爬 20 s 高度，
    /// 编队可能已经甩开 2 km。这时候屏幕上只剩一条爬升率，飞行员不知道自己正在被甩掉 ——
    /// 破例给一行距离，比严守「只给爬升率」更有用。</para>
    /// <para>★ 判定用**负数**表示关闭：<c>-1</c> = 什么都不显示；<c>0</c> = 任何距离都显示
    /// （距离恒 ≥ 0，所以 0 这个门槛等于没有门槛）；正数 = 超过它才显示。
    /// 旧写法把 0 当关闭，结果「0 = 任何距离」这个直觉读法反而失效了。</para>
    /// </summary>
    internal static bool RevealRangeWhileClimbing(double rangeM, double gateM)
        => gateM >= 0 && rangeM >= gateM;

    /// <summary>状态机位置：Off / 等方向（Arming）/ 已锁方向（Active）。</summary>
    internal enum TurnAssistStage { Off, Arming, Active }

    /// <summary>方向锁存状态。<paramref name="ReverseSince"/> 是反向开始的时刻，-1 = 当前没有反向。</summary>
    internal readonly record struct TurnAssistState(TurnAssistStage Stage, int Dir, double ReverseSince);

    /// <summary>
    /// 目标坡度与目标转向率。
    /// <para>坡度 φ = acos(1/n) 只跟 G 有关；转向率 ω = g·√(n²−1)/V 还跟速度有关，
    /// 所以主提示给坡度，转向率只作副行 —— 同是 3G，500 km/h 时 13°/s、900 km/h 时 7°/s。</para>
    /// </summary>
    internal static (double BankDeg, double RateDeg) TurnAssistTargets(double targetG, double tasKmh)
    {
        var n = Math.Clamp(targetG, 1.2, 9.0);
        var bank = Math.Acos(1.0 / n) * 180.0 / Math.PI;
        var v = Math.Max(50.0, tasKmh / 3.6);
        var rate = Math.Sqrt(Math.Max(0, n * n - 1)) * 9.80665 / v * 180.0 / Math.PI;
        return (bank, rate);
    }

    /// <summary>
    /// 推进一步转向辅助状态机。抽成纯函数是为了能断言「反向多久才重锁」这种边界。
    /// <para>方向由转向率的符号决定：长机往哪边压，系统就跟哪边，不用人告诉它左右。</para>
    /// </summary>
    internal static TurnAssistState AdvanceTurnAssist(TurnAssistState s, double rateDeg, double nowS,
                                                      double gateDeg = TurnAssistRateGate,
                                                      double holdS = TurnAssistReverseHoldS)
    {
        if (s.Stage == TurnAssistStage.Off) return s;

        var mag = Math.Abs(rateDeg);
        var sign = Math.Sign(rateDeg);

        if (s.Stage == TurnAssistStage.Arming)
            return mag >= gateDeg && sign != 0 ? new TurnAssistState(TurnAssistStage.Active, sign, -1) : s;

        // Active：反向超过门槛并**持续** holdS 才重锁，滚转过零那一瞬不算反悔
        if (sign != 0 && sign != s.Dir && mag >= gateDeg)
        {
            var since = s.ReverseSince < 0 ? nowS : s.ReverseSince;
            return nowS - since >= holdS
                ? new TurnAssistState(TurnAssistStage.Active, sign, -1)
                : s with { ReverseSince = since };
        }
        return s.ReverseSince < 0 ? s : s with { ReverseSince = -1 };
    }

    /// <summary>按键：关 → 待锁；待锁/已锁 → 关。</summary>
    internal static void ToggleTurnAssist()
    {
        turnAssist = turnAssist.Stage == TurnAssistStage.Off
            ? new TurnAssistState(TurnAssistStage.Arming, 0, -1)
            : new TurnAssistState(TurnAssistStage.Off, 0, -1);
        Console.WriteLine($"turn assist -> {turnAssist.Stage}");
    }

    /// <summary>
    /// 要广播的目标坡度。只在**已锁方向**时广播：待锁阶段那个值是占位（按右侧画的），
    /// 发出去会让僚机以为长机要右转。
    /// </summary>
    private static double? TurnAssistBroadcastBank(HudConfig cfg)
        => cfg.FormationTurnBroadcast && turnAssist.Stage == TurnAssistStage.Active
            ? turnAssistBankDeg
            : (double?)null;

    /// <summary>面板只读行：数据链上收到的目标坡度（僚机侧看这一行判断广播到没到）。</summary>
    public static string PartnerTurnBankText()
    {
        var link = dataLink;
        if (link == null) return "-";
        return link.TryGetPartnerTargetBank(out double bank) ? bank.ToString("0") + "°" : "-";
    }

    /// <summary>面板只读行：当前状态。</summary>
    public static string TurnAssistStatusText() => turnAssist.Stage switch
    {
        TurnAssistStage.Active => turnAssist.Dir > 0 ? "R" : "L",
        TurnAssistStage.Arming => "ARM",
        _ => "OFF",
    };

    /// <summary>友机相对方位的历史（没有数据链时用它反推长机转向率）。</summary>
    private static readonly System.Collections.Generic.List<(double T, double Rel)> leadRelHistory = new();

    /// <summary>把角度归一化到 -180..180（罗盘差、相对方位差都要过一遍）。</summary>
    private static double NormalizeDeg(double deg)
    {
        while (deg > 180) deg -= 360;
        while (deg < -180) deg += 360;
        return deg;
    }

    /// <summary>
    /// 窗口内首尾差算自己的转向率（°/s）。没有航向时清空历史并返回 0 ——
    /// 不能拿 0 当航向喂进来，否则转弯一开始会先算出一次巨大的假跳变。
    /// </summary>
    private static double UpdateOwnTurnRate(bool hasHeading, double hdgDeg, double nowS)
    {
        if (!hasHeading)
        {
            ownHeadingHistory.Clear();
            ownTurnRateDeg = 0;
            return 0;
        }

        // 切图/重生/罗盘重同步会让航向瞬间跳到别处，历史必须作废
        if (ownHeadingHistory.Count > 0
            && Math.Abs(NormalizeDeg(hdgDeg - ownHeadingHistory[^1].Hdg)) > 60)
            ownHeadingHistory.Clear();

        ownHeadingHistory.Add((nowS, hdgDeg));
        while (ownHeadingHistory.Count > 2 && nowS - ownHeadingHistory[0].T > TurnRateWindowS)
            ownHeadingHistory.RemoveAt(0);

        if (ownHeadingHistory.Count < 2) { ownTurnRateDeg = 0; return 0; }

        double span = ownHeadingHistory[^1].T - ownHeadingHistory[0].T;
        if (span < TurnRateMinSpanS) { ownTurnRateDeg = 0; return 0; }

        ownTurnRateDeg = NormalizeDeg(ownHeadingHistory[^1].Hdg - ownHeadingHistory[0].Hdg) / span;
        return ownTurnRateDeg;
    }

    /// <summary>
    /// 没有数据链时从「相对方位的变化率」反推长机的转向率。
    /// <para>距离稳定时有 <c>d(rel)/dt = ω_长机 − ω_我</c>，所以把测到的变化率加回自己的转向率
    /// 就是长机的。比拿位置序列算航迹角简单，也正好复用同一个窗口。</para>
    /// </summary>
    private static double EstimateLeadTurnRate(double relDeg, double ownTurnRate, double nowS)
    {
        if (leadRelHistory.Count > 0
            && Math.Abs(NormalizeDeg(relDeg - leadRelHistory[^1].Rel)) > 60)
            leadRelHistory.Clear();      // 换目标了

        leadRelHistory.Add((nowS, relDeg));
        while (leadRelHistory.Count > 2 && nowS - leadRelHistory[0].T > TurnRateWindowS)
            leadRelHistory.RemoveAt(0);

        if (leadRelHistory.Count < 2) return 0;

        double span = leadRelHistory[^1].T - leadRelHistory[0].T;
        if (span < TurnRateMinSpanS) return 0;

        double dRel = NormalizeDeg(leadRelHistory[^1].Rel - leadRelHistory[0].Rel) / span;
        return dRel + ownTurnRate;
    }

    /// <summary>
    /// 转弯提前角：<c>asin(ω·R/V)</c> —— 指向长机的未来位置，而不是它现在的位置。
    /// <para>物理含义：以 V 飞行时，要跟上以 ω 绕自己转、距离 R 的长机，机头必须多压这么多度。
    /// 400 m / 600 km/h / 10°/s 时约 25°，而引导把 ±3° 就当"对准"了 —— 这就是转弯时队形被甩的原因。</para>
    /// </summary>
    /// <summary>编队引导阶段：未到位 → 保持中 → 手动解除。</summary>
    private enum FormationStage { Converge, Maintain, Disabled }

    private static FormationStage formationStage = FormationStage.Converge;
    /// <summary>连接行隐藏时刻。收敛阶段每帧往后推 3 s（等于常驻），进保持后就不再推、3 s 后消失。</summary>
    private static double linkLineHideAt = double.NegativeInfinity;
    private static double formationStageSince;
    private static double formationDoneSince = -1;   // 进入 40 m 的起始时刻（要持续 2 s 才算完成）
    private static double formationNoticeUntil = -1; // 「编队完成 / 已解除」提示显示到什么时候

    /// <summary>
    /// 长机转向辅助的显示数据。状态机先走一步（方向可能在这一帧被锁上），再算偏差与指令。
    /// <para>坡度取 <c>BankDeg * BankSign</c>：和俯仰梯上看到的符号一致，
    /// 否则用户会看到「带子说左、梯子说右」。</para>
    /// </summary>
    private static TurnAssistCue? BuildTurnAssistCue(HudConfig cfg, TelemetrySnapshot snap, double nowS)
    {
        turnAssist = AdvanceTurnAssist(turnAssist, ownTurnRateDeg, nowS);
        if (turnAssist.Stage == TurnAssistStage.Off) { turnAssistBankDeg = 0; return null; }

        var (targetBank, targetRate) = TurnAssistTargets(cfg.FormationTurnTargetG, snap.TasKmh);
        var bankNow = (snap.BankDeg ?? 0) * cfg.BankSign;

        // 还没锁方向时先按右侧画目标标，靠下面的「转向以锁定」文案说明它只是个占位
        var dir = turnAssist.Dir == 0 ? 1 : turnAssist.Dir;
        var signedTarget = dir * targetBank;

        // 容差按 G 换算成坡度差：φ(n±tol) − φ(n)，这样「容差 0.3G」在 3G 和 6G 下都合理
        var (bankHigh, _) = TurnAssistTargets(cfg.FormationTurnTargetG + Math.Max(0.05, cfg.FormationTurnTolG),
                                              snap.TasKmh);
        var tolBank = Math.Max(0.5, bankHigh - targetBank);

        var absBank = Math.Abs(bankNow);
        var action = absBank < targetBank - tolBank ? TurnAssistAction.BankMore
            : absBank > targetBank + tolBank ? TurnAssistAction.BankLess
            : TurnAssistAction.Hold;
        // 压反了：不管大小，先回正再说
        if (Math.Sign(bankNow) == -dir && absBank > tolBank) action = TurnAssistAction.BankMore;

        turnAssistBankDeg = signedTarget;
        return new TurnAssistCue(dir, bankNow, signedTarget, targetRate,
                                 Math.Clamp(cfg.FormationTurnTargetG, 1.2, 9.0), action,
                                 turnAssist.Stage == TurnAssistStage.Arming);
    }

    /// <summary>
    /// 编队状态机（产品层）：只做「到位判定 + 提示」，不参与控制 —— HUD 是建议，飞行员才是控制器。
    /// <para>到位线 40 m、需持续 2 s：40 m 比 sim 里的 60 m 更严，因为「编队完成」是给长机的承诺，
    /// 一旦显示他就会开始机动。</para>
    /// </summary>
    /// <summary>
    /// 距离误差，按槽位斜距的百分比。抽成纯函数为了能断言百分比换算。
    /// <para>目标距离 ≤ 1 m 时返回 0：地面/静止时站位没有意义，不要因为除零跳到 100%。</para>
    /// </summary>
    internal static double FormationRangeErrorPct(double rangeM, double targetM)
        => targetM <= 1.0 ? 0.0 : Math.Abs(rangeM - targetM) / targetM * 100.0;

    /// <param name="slotIndex">
    /// 期望距离用哪个槽位算。-1 = 用本机配置的槽位（僚机侧就是自己的）；
    /// 长机侧要传**僚机自报的槽位** —— 否则三机时按长机自己的槽位算，永远对不上。
    /// </param>
    private static void UpdateFormationStage(double rangeM, double aimDeg, double nowS, HudConfig cfg)
    {
        if (formationStage == FormationStage.Disabled) return;

        // 按本机站位的斜距百分比判定：同一套松紧在 400 m 和 1545 m 的站位上都成立。
        // ★ 统一模型之后几何只来自本机配置 —— 僚机的编号推不出它自己的站位。
        double slotRange = WtHud.Hud.FormationGeometry.Station(cfg).RangeM;
        double err = FormationRangeErrorPct(rangeM, slotRange);

        // 站位：距离对上但没站在槽位上，不算「编队完成」。阈值和 Hold 用同一个配置，
        // 脱离时放宽一倍 —— 否则在门槛上会反复切换「完成 / 收敛」。
        double alignDeg = Math.Max(0.5, cfg.FormationAlignDeg);
        bool aligned = Math.Abs(aimDeg) <= alignDeg;
        bool badlyAligned = Math.Abs(aimDeg) > alignDeg * 2.0;

        if (formationStage == FormationStage.Converge)
        {
            if (err <= cfg.FormationDonePct && aligned)
            {
                if (formationDoneSince < 0) formationDoneSince = nowS;
                else if (nowS - formationDoneSince >= Math.Max(0.1, cfg.FormationDoneHoldS))
                {
                    formationStage = FormationStage.Maintain;
                    formationStageSince = nowS;
                    formationNoticeUntil = nowS + 3.0;
                    formationDoneSince = -1;
                }
            }
            else formationDoneSince = -1;
        }
        else if (formationStage == FormationStage.Maintain
                 && (err > cfg.FormationDropPct || badlyAligned))
        {
            formationStage = FormationStage.Converge;
            formationStageSince = nowS;
            formationDoneSince = -1;
        }
    }

    /// <summary>
    /// 长机转弯的横向过载（G）：<c>n = V·ω / g</c>。
    /// <para>用它当阈值而不是 °/s，因为它不随速度漂移：600 km/h 下 3 G ≈ 10 °/s，
    /// 400 km/h 下 3 G 只需要一半的转弯率，但两种情况队形保持的难度是相当的。</para>
    /// </summary>
    private static double TurnLoadG(double turnRateDeg, double tasKmh)
    {
        double vMs = tasKmh / 3.6;
        double omega = Math.Abs(turnRateDeg) * Math.PI / 180.0;
        return vMs * omega / 9.80665;
    }

    private static double LeadPursuitDeg(double turnRateDeg, double rangeM, double tasKmh, double maxDeg)
    {
        double vMs = tasKmh / 3.6;
        if (vMs < 50 || rangeM < 30) return 0;

        double omega = turnRateDeg * Math.PI / 180.0;             // rad/s
        double s = Math.Clamp(omega * rangeM / vMs, -0.98, 0.98);  // asin 的定义域
        return Math.Clamp(Math.Asin(s) * 180.0 / Math.PI, -maxDeg, maxDeg);
    }

    /// <summary>固定站位的横向偏移（m）：左右僚机在长机左右各 150 m。</summary>
    /// <summary>左右僚机站位：长机后方 370 m（与 150 m 横向合起来斜距 ≈ 400 m）。</summary>
    /// <summary>站位瞄准的最小前向距离（m）：见 WingAimDeg 里的原点病态说明。</summary>
    private const double AimForwardMinM = 200;

    /// <summary>
    /// 固定站位瞄准：站位钉在**长机机体坐标系**里（横向 sideM、后距 backM，由队形槽位给出），
    /// 用 leadHeadingDeltaDeg（长机航向 − 本机航向）把站位转到本机航向系，取 atan2 作为该转的方位。
    /// <para>站位随长机航向一起转，长机转弯后 V 队形跟着转回来；delta = 0（无链路 / 航向一致）
    /// 退化为本机航向系。★ 斜距不再假设相等：梯队/纵队/菱形的槽位斜距各不相同，
    /// 到位判定读的是 <see cref="WtHud.Hud.FormationSlotSpec.RangeM"/>。</para>
    /// </summary>
    private static double WingAimDeg(double relBearingDeg, double rangeM,
                                     double leadHeadingDeltaDeg, double sideM, double backM)
    {
        double rad = relBearingDeg * Math.PI / 180.0;
        double delta = leadHeadingDeltaDeg * Math.PI / 180.0;
        // 长机在本机航向系的位置
        double lx = rangeM * Math.Sin(rad), ly = rangeM * Math.Cos(rad);
        // 长机机体系的「右」「前」在本机航向系的方向
        double rx = Math.Cos(delta), ry = -Math.Sin(delta);
        double fx = Math.Sin(delta), fy = Math.Cos(delta);
        double side = sideM;
        double back = backM;
        double sx = lx + side * rx + back * fx;
        double sy = ly + side * ry + back * fy;
        // 僚机接近站位时 sy→0，atan2(sx, sy) 在原点附近是病态的：t=0 实测 sx=sy=0
        // 却因浮点残差给出 ±90°，随后 sy 在 0 附近来回穿越会让引导角在 0~180° 乱跳，
        // 内圈叠加正提前角后被打成倒飞。用最小前向距离限住分母，引导角随横向偏差平滑变化。
        return Math.Atan2(sx, Math.Max(sy, AimForwardMinM)) * 180.0 / Math.PI;
    }

    /// <summary>
    /// 长机转弯时僚机是否在内圈。右转圆心在长机右侧、左转圆心在左侧，
    /// 所以「侧位方向与转向方向同侧」就是内圈（路径更短），异侧是外圈。
    /// </summary>
    private static bool IsInsideTurn(int wingSide, double turnRateDeg)
    {
        if (wingSide == 0 || Math.Abs(turnRateDeg) < 1e-9) return false;
        return Math.Sign(wingSide) == Math.Sign(turnRateDeg);
    }

    /// <summary>
    /// 转弯提前角。wingSide == 0（未设定侧位）保持旧 <c>asin(ω·R/V)</c>。
    /// <para>wingSide != 0（固定站位系统）恒为 0：站位点随长机航向一起转，天然包含转弯预测，
    /// 再叠加提前角会让内圈切过头（仿真实测 cross 从 +150 收到 +36）。外圈该多飞的距离
    /// 交给速度前馈 + 油门 PID，方向引导不再叠提前量。</para>
    /// </summary>
    private static double FormationLeadAheadDeg(double turnRateDeg, double rangeM, double tasKmh,
                                                double maxDeg, int wingSide)
    {
        // 侧位僚机不做提前量：站位点本身随长机航向旋转，已经含了转弯预测，
        // 再叠一次 asin(ω·R/V) 会切过头（sim 实测内圈 cross +71 → +36）。
        if (wingSide != 0) return 0;
        return LeadPursuitDeg(turnRateDeg, rangeM, tasKmh, maxDeg);
    }

    /// <summary>
    /// 长机转弯的共享几何：ρ = V/|ω|、僚机到圆心距离 r_w、相对半径差 Δr = r_w − ρ。
    /// <para>C 提前角用它算幅度缩放，僚机速度前馈用它算 |ω|·Δr。两处必须同一套几何，
    /// 否则一个按这个圆心、另一个按那个圆心，会互相打架。</para>
    /// </summary>
    private static (double RhoM, double RwM, double DeltaRM) TurnGeometry(
        double turnRateDeg, double rangeM, double tasKmh,
        double relBearingDeg, double leadHeadingDeltaDeg)
    {
        double vMs = tasKmh / 3.6;
        double omega = Math.Abs(turnRateDeg) * Math.PI / 180.0;
        if (omega < 1e-9 || vMs < 50) return (0, 0, 0);
        double rho = vMs / omega;
        double rad = relBearingDeg * Math.PI / 180.0;
        double delta = leadHeadingDeltaDeg * Math.PI / 180.0;
        double s = Math.Sign(turnRateDeg);
        double cx = rangeM * Math.Sin(rad) + s * rho * Math.Cos(delta);
        double cy = rangeM * Math.Cos(rad) - s * rho * Math.Sin(delta);
        double rW = Math.Sqrt(cx * cx + cy * cy);
        return (rho, rW, rW - rho);
    }

    /// <summary>
    /// 僚机速度前馈：绕同一圆心以 ω 转弯时，外圈半径更大、需要更快；内圈更慢。
    /// <para>ΔV = |ω|·(r_w − ρ)，速率是标量所以与转弯方向无关；外圈为正、内圈为负。
    /// 钳在长机真空速的 [minRatio, maxRatio] 倍所对应的增量内。</para>
    /// <para>wingSide == 0（正后方 / 未设定侧位）返回 0，保持默认编队行为不变。</para>
    /// </summary>
    private static double FormationFeedKmh(double turnRateDeg, double rangeM, double tasKmh,
                                           int wingSide, double relBearingDeg,
                                           double leadHeadingDeltaDeg, double minRatio, double maxRatio)
    {
        if (wingSide == 0 || Math.Abs(turnRateDeg) < 1e-9 || tasKmh < 180) return 0;
        var (_, _, deltaR) = TurnGeometry(turnRateDeg, rangeM, tasKmh,
                                          relBearingDeg, leadHeadingDeltaDeg);
        double omega = Math.Abs(turnRateDeg) * Math.PI / 180.0;
        double need = omega * deltaR * 3.6;               // km/h
        return Math.Clamp(need, tasKmh * (minRatio - 1), tasKmh * (maxRatio - 1));
    }

    // 数据链（交换高度 + 航向/转向率）。null = 没开。
    private static WtHud.Telemetry.DataLink? dataLink;

    // 配置互传（48018）。null = 没起来（端口被占或被防火墙挡）。
    private static WtHud.Telemetry.ConfigLink? configLink;

    /// <summary>最近一帧的 TAS（面板「判定余量」用：延迟 = 速度 × 一个发包间隔）。</summary>
    internal static double CurrentTasKmh { get; private set; }

    /// <summary>当前机型（配置包 type 段 + 确认页显示）。</summary>
    internal static string CurrentVehicleType { get; private set; } = "";

    /// <summary>
    /// 链路一拍能飞多远 —— 这就是链路位置的平均滞后量，也是「判定余量」要对比的东西。
    /// <para>500 km/h 时 34.7 m；122 m 的站位上，这已经超过 12 m 的完成门槛。</para>
    /// </summary>
    internal static double FormationLatencyM
        => CurrentTasKmh / 3.6 * WtHud.Telemetry.DataLink.SendIntervalS;

    /// <summary>上一次配置动作的提示（面板 toast 用）。</summary>
    internal static string? ConfigActionText { get; private set; }

    /// <summary>
    /// 命令行/后台路径的保存：**失败必须打出来**。
    /// <para>这些地方以前是 catch{} 静默失败 —— 数据链节点 ID 没写进去，下次启动会换一个，
    /// 队友按 ID 配对就永远对不上，而屏幕上什么提示都没有。</para>
    /// </summary>
    private static void LogSave(HudConfig.SaveResult result, string what)
    {
        if (result.Ok) return;
        Console.WriteLine($"{what}: ** SAVE FAILED ({result.Error}) - {result.Detail} **");
    }

    private static string CfgText(string key)
        => WtHud.UI.L10n.Get(WtHud.UI.LangScope.Screen, key);

    internal static WtHud.Telemetry.ConfigOffer[] ConfigOffers()
        => configLink?.Offers ?? Array.Empty<WtHud.Telemetry.ConfigOffer>();

    internal static string ConfigInboxSummary()
    {
        int n = ConfigOffers().Length;
        return n == 0 ? CfgText("cfg.none") : string.Format(CfgText("cfg.have"), n);
    }

    /// <summary>广播本机编队配置：连发 3 包，对方在 48018 上收。</summary>
    internal static void BroadcastFormationConfig(HudConfig cfg)
    {
        if (configLink is null)
        {
            ConfigActionText = CfgText("cfg.noChannel");
            return;
        }

        bool hasAnchor = CurrentAnchor(out double ax, out double ay, out bool af);
        var profile = WtHud.Hud.FormationProfile.From(cfg);
        int targets = configLink.Send(profile,
            cfg.DataLinkId > 0 ? cfg.DataLinkId.ToString(System.Globalization.CultureInfo.InvariantCulture) : "",
            cfg.PlayerName ?? "", CurrentVehicleType,
            hasAnchor ? ax : null, hasAnchor ? ay : null, af,
            cfg.DataLinkPeers);

        ConfigActionText = targets == 0
            ? CfgText("cfg.noChannel")
            : string.Format(CfgText("cfg.broadcast"), targets);
    }

    /// <summary>导出到文件：**不带机场锚点** —— 换一局/换张图之后锚点必然对不上，带上只会互相挡。</summary>
    internal static void ExportFormationConfig(HudConfig cfg)
    {
        using var dlg = new SaveFileDialog
        {
            Title = CfgText("cfg.exportTitle"),
            Filter = "WtHud formation (*.wtcfg)|*.wtcfg|All files (*.*)|*.*",
            FileName = "wt-hud-formation.wtcfg",
        };
        if (dlg.ShowDialog(Form.ActiveForm) != DialogResult.OK) return;

        try
        {
            var profile = WtHud.Hud.FormationProfile.From(cfg);
            string line = WtHud.Telemetry.ConfigPacket.Build(profile,
                cfg.DataLinkId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                cfg.PlayerName ?? "", CurrentVehicleType, null, null, false);
            File.WriteAllText(dlg.FileName, line, new System.Text.UTF8Encoding(false));
            ConfigActionText = string.Format(CfgText("cfg.exported"), Path.GetFileName(dlg.FileName));
        }
        catch (Exception ex)
        {
            ConfigActionText = string.Format(CfgText("cfg.failed"), ex.Message);
        }
    }

    /// <summary>从文件导入：用户挑了文件本身就是确认，直接写进草稿（仍需 SAVE）。</summary>
    internal static void ImportFormationConfigFile(HudConfig cfg)
    {
        using var dlg = new OpenFileDialog
        {
            Title = CfgText("cfg.importTitle"),
            Filter = "WtHud formation (*.wtcfg)|*.wtcfg|All files (*.*)|*.*",
            CheckFileExists = true,
        };
        if (dlg.ShowDialog(Form.ActiveForm) != DialogResult.OK) return;

        try
        {
            if (!WtHud.Telemetry.ConfigPacket.TryParse(File.ReadAllText(dlg.FileName), out var offer))
            {
                ConfigActionText = CfgText("cfg.badPacket");
                return;
            }
            ApplyConfigOffer(cfg, offer);
        }
        catch (Exception ex)
        {
            ConfigActionText = string.Format(CfgText("cfg.failed"), ex.Message);
        }
    }

    /// <summary>把收到的配置写进草稿。★ 只是草稿：磁盘仍要 SAVE，否则别人一发就改了你在飞的设置。</summary>
    internal static void ApplyConfigOffer(HudConfig cfg, WtHud.Telemetry.ConfigOffer offer)
    {
        try
        {
            offer.Profile.ApplyTo(cfg);
            ConfigActionText = CfgText("cfg.imported");
        }
        catch (Exception ex)
        {
            ConfigActionText = string.Format(CfgText("cfg.failed"), ex.Message);
        }
    }

    /// <summary>本机机场锚点：与状态包同一套（最近的**友军**机场，两边永远可见）。</summary>
    internal static bool CurrentAnchor(out double xn, out double yn, out bool friendly)
    {
        xn = yn = 0;
        friendly = false;
        var map = Volatile.Read(ref _linkMapSnapshot);
        if (map?.Self is not { } self) return false;
        if (!TryNearestAirfield(map.Airfields, self.X, self.Y, out var field,
                                friendlyOnly: true, tolerance: 2.0))
            return false;
        xn = field.X;
        yn = field.Y;
        friendly = field.Friendly;
        return true;
    }
    private static double lastFlaps = -1;
    private static long lastFlapsChangeTicks;
    private static double lastAirbrake = -1;
    private static long lastAirbrakeChangeTicks;
    private static volatile bool scoreboardHeld;

    /// <summary>数据链状态（面板"数据链设置"页显示）：off / 无伙伴 / 已连接 N 个。</summary>
    /// <summary>
    /// 长机航向来源：有数据链就用长机自报航向，没有就用本机航向近似。
    /// <para>编队两机航向近似平行，实测两者差别不大；高度不在其列 —— map_obj 没有 z，
    /// 高度只能走数据链（<c>TryGetPartnerAltitude</c>）。</para>
    /// </summary>
    private static double ResolveLeadHeading(double? linkedHeading, double ownHeading)
        => linkedHeading ?? ownHeading;

    /// <summary>8111 那半边服务到底给了什么。面板与日志共用这一个判断。</summary>
    internal enum ApiHealth
    {
        /// <summary>还没采到第一帧。</summary>
        Unknown,
        /// <summary>该有的都有。</summary>
        Ok,
        /// <summary>不在对局里（机库、主菜单、地面停机）。</summary>
        NotInBattle,
        /// <summary>★ 8111 只起来一半：state 正常，indicators 与 map_obj 同时空。重启游戏才恢复。</summary>
        HalfReady,
        /// <summary>地图正常、仪表为空 —— 该机型本来就不提供航空电子数据（2.59 的部分新机）。</summary>
        NoIndicatorsOnly,
        /// <summary>仪表正常、地图为空 —— 看得到姿态但看不到友机。</summary>
        NoMapOnly,
    }

    /// <summary>
    /// 判断 8111 的健康度。抽成纯函数是为了能在 selftest 里把四种组合都断言一遍。
    /// <para>★ 只在**飞行中**判断：机库里 state 也是 valid，但 indicators/map_obj 本来就空，
    /// 那不是故障。用 <paramref name="inFlight"/> 而不是「有没有数据」来分界。</para>
    /// </summary>
    internal static ApiHealth ClassifyApiHealth(bool stateValid, bool inFlight,
                                                bool indicatorsValid, bool hasSelf)
    {
        if (!stateValid) return ApiHealth.NotInBattle;
        if (!inFlight) return ApiHealth.NotInBattle;
        if (!indicatorsValid && !hasSelf) return ApiHealth.HalfReady;
        if (!indicatorsValid) return ApiHealth.NoIndicatorsOnly;
        if (!hasSelf) return ApiHealth.NoMapOnly;
        return ApiHealth.Ok;
    }

    /// <summary>健康度的一句话说明。给日志用英文、给面板用中文。</summary>
    internal static string ApiHealthText(ApiHealth h) => h switch
    {
        ApiHealth.Ok => "ok",
        ApiHealth.NotInBattle => "not in battle",
        ApiHealth.HalfReady => "HALF-READY (8111 gave state but no indicators/map - restart the game)",
        ApiHealth.NoIndicatorsOnly => "no indicators (this airframe has no avionics data)",
        ApiHealth.NoMapOnly => "no map objects (attitude ok, no friendly positions)",
        _ => "-",
    };

    /// <summary>面板上那一行中文。端用户看的是这个，不要塞英文错误码。</summary>
    internal static string ApiHealthZh(ApiHealth h) => h switch
    {
        ApiHealth.Ok => "正常",
        ApiHealth.NotInBattle => "不在对局",
        ApiHealth.HalfReady => "未就绪·请重启游戏",
        ApiHealth.NoIndicatorsOnly => "本机型无仪表",
        ApiHealth.NoMapOnly => "无地图对象",
        _ => "-",
    };

    /// <summary>
    /// 「长机节点 ID」的生效值。长机模式下强制 0（自动）—— 自己就是长机，
    /// 再去认别人当长机会让僚机端看到两个 role=1，谁都分不清。
    /// </summary>
    internal static int EffectiveLeadId(bool leadMode, int configured) => leadMode ? 0 : configured;

    /// <summary>
    /// 「长机机型」的生效值。数据链能收到节点时一律留空：位置配对比机型精确，
    /// 而且同机型多机时机型过滤帮不上忙。只有纯 8111 单机才需要按机型筛友机。
    /// </summary>
    internal static string EffectiveLeadIcon(bool linkUsable, string? icon) => linkUsable ? "" : (icon ?? "");

    private static ApiHealth lastApiHealth = ApiHealth.Unknown;

    /// <summary>面板读这个，不用自己再算一遍。</summary>
    internal static ApiHealth ApiHealthNow => lastApiHealth;

    /// <summary>
    /// 每帧调一次：只在**状态发生变化**时打日志，不在稳态刷屏。
    /// 返回当前健康度，面板直接读。
    /// </summary>
    internal static ApiHealth UpdateApiHealth(TelemetrySnapshot? snap, bool hasSelf)
    {
        var h = snap is null
            ? ApiHealth.Unknown
            : ClassifyApiHealth(snap.Caps.StateValid, snap.InFlight,
                                snap.Caps.IndicatorsValid, hasSelf);

        if (h != lastApiHealth)
        {
            lastApiHealth = h;
            // 只记录有意义的转变：Unknown->Ok 这种正常启动也记一行，方便对时间
            Console.WriteLine($"api: {ApiHealthText(h)}");
        }
        return h;
    }

    /// <summary>
    /// 本机在编队里的角色：面板上的「长机模式」开着就是长机，否则是僚机。
    /// <para>★ 角色只描述「我在这局里干什么」，和有没有开数据链无关 —— 所以写进每一包，
    /// 让僚机端不用猜。两台都开长机模式属于配置错误，接收端会同时看到两个 role=1，
    /// 那时选择逻辑退回最近活跃并在面板上标「歧义」。</para>
    /// </summary>
    internal static WtHud.Telemetry.DataLink.LinkRole SelfLinkRole(HudConfig cfg)
        => cfg.LeadMode
            ? WtHud.Telemetry.DataLink.LinkRole.Lead
            : WtHud.Telemetry.DataLink.LinkRole.Wing;

    /// <summary>
    /// 面板一行：**我这一队**的僚机逐架一条 —— `W1 到位 · W2 掉队 480m · W3 数据错误`。
    /// <para>长机在飞的时候看 HUD 那张表就够，这一行是给落地后复盘用的。
    /// ★ 和 HUD 用同一份数据、同一个过滤（<see cref="WingTable"/>）：两家长机同时在线时，
    /// 面板不会把对方的人算进来，掉线的也照样能看见。</para>
    /// </summary>
    public static string DataLinkWingText()
    {
        var link = dataLink;
        if (link == null) return "-";

        var rows = WingTable(link.RecentNodes(WingOfflineKeepMs), link.SelfId);
        if (rows.Count == 0) return "-";
        var parts = new List<string>(rows.Count);
        foreach (var n in rows) parts.Add(WingLabel(n) + " " + WingStateText(n));
        return string.Join(" · ", parts);
    }

    /// <summary>
    /// 面板一行：填了的长机 ID 现在在线还是离线。
    /// <para>填错了或者对端还没起来，当场就能看出来 —— 否则飞行员只会看到"找不到长机"，
    /// 不知道是 ID 错了还是对方没开。</para>
    /// </summary>
    public static string DataLinkLeadStateText(HudConfig cfg)
    {
        if (cfg.LeadMode) return "-";                       // 长机自己不需要跟谁
        string zh(string key) => WtHud.UI.L10n.Get(WtHud.UI.LangScope.Screen, key);

        var link = dataLink;
        if (link is null) return zh("cfg.noChannel");

        // 自动模式以前只说「未指定」，等于什么都没说；现在直接报认出的是谁、多久前的一包。
        if (link.TryGetLeadNode(out var lead, out var source, out bool ambiguous))
        {
            string age = (lead.AgeMs / 1000.0).ToString("0.0");
            string src = source switch
            {
                WtHud.Telemetry.DataLink.LeadSource.ByConfig => zh("val.srcConfig"),
                WtHud.Telemetry.DataLink.LeadSource.ByRole => zh("val.srcRole"),
                WtHud.Telemetry.DataLink.LeadSource.SingleNode => zh("val.srcSingle"),
                _ => zh("val.srcRecent"),
            };
            return $"#{lead.Id} · {src} · {age}s" + (ambiguous ? " ?" : "");
        }

        return cfg.DataLinkLeadId > 0 ? zh("val.leadOffline") : zh("val.leadAuto");
    }

    /// <summary>长机是怎么认出来的，面板上一行字。</summary>
    internal static string LeadSourceText(WtHud.Telemetry.DataLink.LeadSource src) => src switch
    {
        WtHud.Telemetry.DataLink.LeadSource.ByConfig => "by id",
        WtHud.Telemetry.DataLink.LeadSource.ByRole => "by role",
        WtHud.Telemetry.DataLink.LeadSource.SingleNode => "solo",
        WtHud.Telemetry.DataLink.LeadSource.Newest => "ambiguous",
        _ => "-",
    };

    /// <summary>
    /// 数据链状态（面板显示）：off / no partner / linked。
    /// 后面追「长机 #ID（怎么认出来的）」和节点数，认不出来时能一眼看出该手填 ID。
    /// </summary>
    public static string DataLinkStatusText()
    {
        var link = dataLink;
        if (link == null) return "off";
        int n = link.NodeCount;
        if (!link.TryGetLeadNode(out var lead, out var src, out bool ambiguous))
            return n > 1 ? $"x{n} / no lead" : "no partner";

        string how = LeadSourceText(src);
        if (ambiguous) how += "!";
        return n > 1 ? $"linked x{n} · #{lead.Id} ({how})" : $"linked #{lead.Id} ({how})";
    }

    /// <summary>
    /// 数据链开关的值列文本。**运行中的实例和配置不一致时要说出来。**
    /// <para>收发线程只在启动时建一次，运行中改这个开关不会立刻生效 —— 面板上只写「开」，
    /// 而实际没有在监听，是最难查的那种假象（队员说「我开了」但对面收不到）。</para>
    /// </summary>
    internal static string DataLinkStateText(HudConfig cfg)
    {
        string state = WtHud.UI.L10n.Get(WtHud.UI.LangScope.Screen, cfg.DataLinkEnabled ? "on" : "off");
        if (cfg.DataLinkEnabled == (dataLink != null)) return state;
        return state + " · " + WtHud.UI.L10n.Get(WtHud.UI.LangScope.Screen, "val.needRestart");
    }

    /// <summary>
    /// 在线节点一览（面板一行）：`#123456 L 2846m 0.2s`，多个用逗号隔开。
    /// <para>联调时这行最有用：谁的包龄在涨、谁报的是长机，都写在这。</para>
    /// </summary>
    public static string DataLinkNodeText()
    {
        var link = dataLink;
        if (link == null) return "-";
        var nodes = link.OnlineNodes();
        if (nodes.Length == 0) return "-";

        var parts = new List<string>(nodes.Length);
        foreach (var n in nodes)
        {
            char role = n.Role switch
            {
                WtHud.Telemetry.DataLink.LinkRole.Lead => 'L',
                WtHud.Telemetry.DataLink.LinkRole.Wing => 'W',
                _ => '?',
            };
            parts.Add($"#{n.Id}{role} {n.AltM}m {n.AgeMs / 1000.0:0.0}s");
        }
        return string.Join(", ", parts);
    }

    /// <summary>
    /// 异队过滤的现状：因为机场队标不符被丢掉多少包、最后一次是什么时候。
    /// <para>一直是「无」说明网段里只有自己人；出现数字说明收到了另一队的包 ——
    /// 这时候才需要确认是不是有人误进了同一个 VPN。</para>
    /// </summary>
    public static string DataLinkTeamText()
    {
        var link = dataLink;
        if (link == null) return "-";
        long n = link.OtherTeamDropped;
        if (n == 0) return WtHud.UI.L10n.Get(WtHud.UI.LangScope.Screen, "val.teamNone");
        int ago = (int)Math.Round((Environment.TickCount64 - link.LastOtherTeamTick) / 1000.0);
        return string.Format(WtHud.UI.L10n.Get(WtHud.UI.LangScope.Screen, "val.teamFiltered"),
                             n, Math.Max(0, ago));
    }

    /// <summary>对端地址的现状（面板上单独一行显示）：broadcast / 2 target(s) / 1 ok / 1 bad。</summary>
    private static string dataLinkPeerNote = "";
    private static int dataLinkPeersConfigured;

    /// <summary>给面板用的对端现状文本。</summary>
    public static string DataLinkPeerText()
    {
        var link = dataLink;
        if (link == null) return "off";
        // 配置本身有问题时先报配置，别让用户以为自己填的对了
        if (dataLinkPeerNote.Contains("bad")) return dataLinkPeerNote;
        if (dataLinkPeerNote == "broadcast") return "broadcast";

        int actual = link.TargetCount;
        int reply = Math.Max(0, actual - dataLinkPeersConfigured);
        // 「N+M reply」= 手填 N 个 + 收到的 N 个节点自动回包。
        // 三人编队里长机只填一个地址也会显示 1+1，一眼看得出另一个是自动补上的。
        return reply > 0 ? $"{dataLinkPeersConfigured}+{reply} reply" : $"{actual} target(s)";
    }

    /// <summary>
    /// 把配置里的对端地址应用到正在跑的链路上。启动时调一次、面板改完再调一次。
    /// <para>★ 这是这行设置能放进面板的唯一理由：改完立即生效，不用重启 HUD ——
    /// 联调时 IP 会变好几次，每次都要重开程序的话这行设置就没有意义了。</para>
    /// </summary>
    /// <summary>
    /// 对端现状那一行的文字。抽成纯函数是为了能在 selftest 里断言 ——
    /// 真实状态下这行走的是 UDP 链路，不太可能为了看文字去开两台机器。
    /// </summary>
    /// <param name="requested">配置里写了几个目标。</param>
    /// <param name="bad">其中格式不对、被跳过的个数。</param>
    internal static string PeerNote(int requested, int bad)
    {
        if (requested <= 0) return "broadcast";
        int ok = requested - bad;
        return bad <= 0 ? $"{ok} target(s)" : $"{ok} ok / {bad} bad";
    }

    internal static void ApplyDataLinkPeers(HudConfig cfg)
    {
        var link = dataLink;
        if (link == null) { dataLinkPeerNote = "off"; return; }

        var bad = link.SetPeers(cfg.DataLinkPeers);
        dataLinkPeersConfigured = cfg.DataLinkPeers.Count;
        dataLinkPeerNote = PeerNote(cfg.DataLinkPeers.Count, bad.Count);
        // 手填的长机 ID 也在这里落地：面板改完立即生效，不用重开 HUD。
        // ★ 长机模式下强制回到自动（0）：自己就是长机，再去认一个「长机 ID」只会
        //   造成「本机角色=长机、长机节点=别人」的矛盾配置，僚机端看到两个 role=1 也分不清。
        link.LeadId = EffectiveLeadId(cfg.LeadMode, cfg.DataLinkLeadId);

        foreach (var b in bad)
            Console.WriteLine($"data link: bad peer \"{b}\" ignored (want ip:port)");
        Console.WriteLine($"data link: targets -> {dataLinkPeerNote}");
    }

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

    private static TelemetrySnapshot DemoSnapshot() => new()
    {
        Valid = true, InFlight = true, VehicleType = "demo",
        IasKmh = 912, TasKmh = 1044, Mach = 0.87, AltM = 3520, VyMs = 12.4,
        AoaDeg = 5.2, AosDeg = 1.1, NyG = 2.4, HeadingDeg = 275, FuelKg = 4100, Fuel0Kg = 9400,
    };

    /// <summary>
    /// 预览样张专用：把本机真空速换成引导样张里给的值。
    /// <para>不然读数行还挂着写死的 1044、速度条却写 620，同一张图里两个速度互相矛盾，
    /// 看图判断「直观不直观」的人会先被这处不一致绊住。</para>
    /// </summary>
    private static TelemetrySnapshot WithTas(TelemetrySnapshot s, double tasKmh) => new()
    {
        TimestampUtc = s.TimestampUtc, ElapsedSeconds = s.ElapsedSeconds, Valid = s.Valid, InFlight = s.InFlight,
        VehicleType = s.VehicleType, IasKmh = tasKmh * 0.86, TasKmh = tasKmh, Mach = s.Mach, AltM = s.AltM,
        VyMs = s.VyMs, AoaDeg = s.AoaDeg, AosDeg = s.AosDeg, NyG = s.NyG, RollRateDegS = s.RollRateDegS,
        HeadingDeg = s.HeadingDeg, FuelKg = s.FuelKg, Fuel0Kg = s.Fuel0Kg, PitchDeg = s.PitchDeg,
        BankDeg = s.BankDeg, TurnRateDegS = s.TurnRateDegS, Ammo = s.Ammo, Caps = s.Caps,
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
            // 引导样张：WT_PREVIEW_GUIDE="转角,距离,目标距离,动作(0=无 1=加速 2=减速 3=保持),油门km/h[,转向率°/s[,1=来自数据链]]"
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
                    double gLeadRate = g.Length >= 6
                        && double.TryParse(g[5], NumberStyles.Any, CultureInfo.InvariantCulture, out double glr) ? glr : 0;
                    bool gFromLink = g.Length >= 7 && g[6].Trim() == "1";
                    // 第 8 段：长机转弯过载（G）；≥1.5 自动进「转弯过急」警告（与 formationWarnG 默认一致）
                    double gTurnG = g.Length >= 8
                        && double.TryParse(g[7], NumberStyles.Any, CultureInfo.InvariantCulture, out double gtg) ? gtg : 0;
                    // 第 10 段：本机真空速（km/h），速度条要用；不写就不画速度条
                    double gCur = g.Length >= 10
                        && double.TryParse(g[9], NumberStyles.Any, CultureInfo.InvariantCulture, out double gc) ? gc : 0;
                    if (gCur > 1) snap = WithTas(snap, gCur);
                    opt.Guide = new FormationLead(gTurn, gRange, gTarget, gAct, gDelta,
                                                  gLeadRate, gFromLink, gTurnG, gTurnG >= 1.5, gCur);
                    // 样张也画目标航向三角：当前航向 + 引导转角
                    opt.TargetHeadingDeg = ((snap.HeadingDeg + gTurn) % 360 + 360) % 360;
                    // 「找不到长机」提示样张：WT_PREVIEW_HINT="无 Fighter 型友军 · 检查长机机型"
                    if (Environment.GetEnvironmentVariable("WT_PREVIEW_HINT") is { Length: > 0 } hintSpec)
                        opt.FormationHint = hintSpec;
                    // 第 9 段：预览「编队完成」提示（"set"）
                    if (g.Length >= 9 && g[8].Trim() == "set")
                        opt.FormationNotice = WtHud.UI.L10n.Get(WtHud.UI.LangScope.Alerts, "guide.formationDone");
                }
            }
            else
            {
                opt.Guide = new FormationLead(12, 820, WtHud.Hud.FormationGeometry.Station(pcfg).RangeM, FormationAction.Decelerate, -90,
                                              0, false, 0, false, 640);
            }

            // 纵向加速度样张：WT_PREVIEW_ACCEL="6.5"（m/s²，正 = 加速）。
            // 编队模式的加速度竖条只在非零时才画，没有这个变量就永远出不了样张。
            if (double.TryParse(Environment.GetEnvironmentVariable("WT_PREVIEW_ACCEL"),
                                NumberStyles.Any, CultureInfo.InvariantCulture, out double previewAccel))
                opt.AccelMs2 = previewAccel;

            // 数据链目标坡度样张：WT_PREVIEW_LEADBANK="54"（僚机侧那行「长机 R54°」）
            if (double.TryParse(Environment.GetEnvironmentVariable("WT_PREVIEW_LEADBANK"),
                                NumberStyles.Any, CultureInfo.InvariantCulture, out double previewBank))
                opt.LeadTargetBankDeg = previewBank;

            // 高度差样张：WT_PREVIEW_ALTD="120"（正 = 对方高）
            var altSpec = Environment.GetEnvironmentVariable("WT_PREVIEW_ALTD");
            if (!string.IsNullOrWhiteSpace(altSpec)
                && double.TryParse(altSpec, NumberStyles.Any, CultureInfo.InvariantCulture, out double pAlt))
            {
                opt.AltDiffM = pAlt;
                opt.Vertical = new VerticalCue(pAlt, VerticalRateCmd(pAlt, 10), false, false, Math.Abs(pAlt) <= 30);
            }

            // 垂直引导样张：WT_PREVIEW_VERT="误差m,升降率,过期(0/1),优先(0/1)[,远距m,目标m]"
            var vertSpec = Environment.GetEnvironmentVariable("WT_PREVIEW_VERT");
            if (!string.IsNullOrWhiteSpace(vertSpec))
            {
                var vp = vertSpec.Split(',');
                double V(int i, double dflt) => vp.Length > i
                    && double.TryParse(vp[i], NumberStyles.Any, CultureInfo.InvariantCulture, out double x) ? x : dflt;
                double? farRange = vp.Length > 4 ? V(4, 0) : null;
                opt.Vertical = new VerticalCue(V(0, 120), V(1, 4.5), V(2, 0) > 0.5, V(3, 0) > 0.5, V(0, 120) == 0,
                                               farRange, farRange is null ? null : V(5, 0));
                // 爬升阶段横向引导不出现：样张也照这个规则，否则出图会和真机不一致
                if (V(3, 0) > 0.5) { opt.Guide = null; opt.TargetHeadingDeg = null; }
            }

            // 长机样张：WT_PREVIEW_LEAD="主状态|副状态|是否警告(0/1)[|当前速度,目标速度]"
            var leadSpec = Environment.GetEnvironmentVariable("WT_PREVIEW_LEAD");
            if (!string.IsNullOrWhiteSpace(leadSpec))
            {
                var lp = leadSpec.Split('|');
                opt.Guide = null;
                opt.LeadStatusMain = lp.Length > 0 ? lp[0] : "";
                opt.LeadStatusSub = lp.Length > 1 ? lp[1] : "";
                opt.LeadStatusWarn = lp.Length > 2 && lp[2] == "1";
                if (lp.Length > 3)
                {
                    var sp = lp[3].Split(',');
                    if (sp.Length >= 2
                        && double.TryParse(sp[0], NumberStyles.Any, CultureInfo.InvariantCulture, out double lc)
                        && double.TryParse(sp[1], NumberStyles.Any, CultureInfo.InvariantCulture, out double lt))
                    {
                        opt.LeadSpeedBar = (lc, lt, lt - lc);
                        snap = WithTas(snap, lc);
                    }
                }
            }

            // 僚机表样张：WT_PREVIEW_WINGS="W1:到位:0|W2:掉队 480m:1|W3:偏离站位:1"
            var wingsSpec = Environment.GetEnvironmentVariable("WT_PREVIEW_WINGS");
            if (!string.IsNullOrWhiteSpace(wingsSpec))
            {
                foreach (var part in wingsSpec.Split('|'))
                {
                    var cells = part.Split(':');
                    if (cells.Length < 2) continue;
                    opt.LeadWingRows.Add((cells[0], cells[1], cells.Length > 2 && cells[2] == "1"));
                }
            }
        }
        if (variant == "turnassist")
        {
            // 长机转向辅助样张：WT_PREVIEW_TURN="方向,当前坡度,目标坡度,G,指令(0多压1回杆2正好),待锁(0/1),目标转向率"
            // ★ FormationAssist 必须开：编队块（含转向辅助）只在它开着时绘制
            opt.FormationOnly = true;
            opt.FormationAssist = true;
            if (opt.LeadStatusMain.Length == 0)
            {
                opt.LeadStatusMain = WtHud.UI.L10n.Get(WtHud.UI.LangScope.Alerts, "guide.lead.formation");
                opt.LeadStatusSub = WtHud.UI.L10n.Get(WtHud.UI.LangScope.Alerts, "guide.lead.setMach") + " 0.70";
                opt.LeadSpeedBar = (snap.TasKmh, snap.TasKmh + 55, 55);
            }
            var spec = Environment.GetEnvironmentVariable("WT_PREVIEW_TURN") ?? "1,28,54,3.0,0,0,7.2";
            var q = spec.Split(',');
            double Q(int i, double dflt) => q.Length > i &&
                double.TryParse(q[i], NumberStyles.Any, CultureInfo.InvariantCulture, out double v) ? v : dflt;
            opt.TurnAssist = new TurnAssistCue(
                Dir: (int)Q(0, 1), BankNowDeg: Q(1, 28), TargetBankDeg: Q(2, 54),
                TargetRateDeg: Q(6, 7.2), TargetG: Q(3, 3.0),
                Action: (TurnAssistAction)(int)Q(4, 0), Arming: Q(5, 0) > 0.5);
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
            else if (view == "shape") { form.PreviewOpen(1, 2); form.PreviewSub(2); }   // 编队 → 队形（三级页）
            else if (view == "turn") { form.PreviewOpen(1, 7); form.PreviewSub(7); }    // 编队 → 长机 → 转向辅助
            else if (view == "wing") { form.PreviewOpen(1, 6); form.PreviewSub(6); }    // 编队 → 僚机参数
            else if (view == "lead") { form.PreviewOpen(1, 7); form.PreviewSub(7); }    // 编队 → 长机参数
            else if (view == "dl") { form.PreviewOpen(2, 0); }                          // 数据链模块
            // 数据链页从 0.4.29 起第 1 行插了「地图判定」，链路状态顺延到 5；配置传递在 6
            else if (view == "dlstatus") { form.PreviewOpen(2, 5); form.PreviewSub(5); } // 数据链 → 链路状态
            else if (view == "cfg") { form.PreviewOpen(2, 6); form.PreviewSub(6); }       // 数据链 → 配置传递
            else if (view == "cfginbox") { form.PreviewOpen(2, 6); form.PreviewSub(6); form.PreviewSub(1); }
            else if (view == "diag") { form.PreviewOpen(9, 0); }                        // 诊断（顶部数据链入口）
            else if (view == "diagstatus") { form.PreviewOpen(9, 0); form.PreviewSub(0); }  // 诊断 → 链路状态（同一个页）
            else if (view == "dlfromform") { form.PreviewOpen(1, 8); form.PreviewSub(8); }  // 编队 → 数据链（跳板）
            else if (view == "sidekey") { form.PreviewSideKey(Math.Clamp(item, 0, 9)); }   // 主菜单按第 N 个侧键
            else if (view == "preset") { form.PreviewOpen(4, 0); form.PreviewPreset(); }
            else if (view == "edit") { form.PreviewEdit(0, 7); }   // FOV 行（数值行）
            else if (view == "alerts") { form.PreviewOpen(2, 0); }
            else if (view == "display") { form.PreviewOpen(0, 0); }
            else if (view == "display2") { form.PreviewOpen(0, 0); form.PreviewItemPage(1); }
            else if (view == "menu2") { form.PreviewMenuPage(1); }
            else if (view == "menunext") { form.PreviewBottomKey(2); }            // 按「下页」
            else if (view == "menuedge") { form.PreviewMenuPage(1); form.PreviewBottomKey(2); }  // 末页再按
            else if (view == "menusingle") { form.PreviewBottomKey(2); }          // 只有一页时按
            // 二级页面侧键：走 SidePressed → page router，用来核对按钮槽位与命令是否接上
            else if (view == "confirmkey") { form.PreviewConfirm(5, 9); form.PreviewSideKey(3); }
            else if (view == "editkey") { form.PreviewEdit(0, 7); form.PreviewSideKey(3); }
            else if (view == "editkey7") { form.PreviewEdit(0, 7); form.PreviewSideKey(7); }
            else if (view == "presetkey") { form.PreviewOpen(4, 0); form.PreviewPreset(); form.PreviewSideKey(2); }
            else if (view == "pickkey") { form.PreviewPickVehicle(); form.PreviewSideKey(5); }
            else if (view == "presetkey0") { form.PreviewOpen(4, 0); form.PreviewPreset(); form.PreviewSideKey(0); }
            else if (view == "presetkey1") { form.PreviewOpen(4, 0); form.PreviewPreset(); form.PreviewSideKey(1); }
            else if (view == "presetkey5") { form.PreviewOpen(4, 0); form.PreviewPreset(); form.PreviewSideKey(5); }
            // 文本输入页：只打开、不按键（用于核对输入框/提示/确认行的排版）。
            // textedifull 额外灌满 40 字符，用来核对长昵称会不会顶出玻璃。
            else if (view == "textedit") { form.PreviewOpenItem(0, 22); form.PreviewPreset(); }
            else if (view == "textedifull")
            {
                form.PreviewOpenItem(0, 22); form.PreviewPreset();
                form.PreviewTextBuffer("ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_-+=~");
            }
            else if (view.StartsWith("sidekey:"))
            {
                // 形如 sidekey:edit:3 / sidekey:preset:2 / sidekey:confirm:3
                var parts = view.Split(':');
                switch (parts[1])
                {
                    case "confirm": form.PreviewConfirm(5, 9); break;
                    case "edit": form.PreviewEdit(0, 7); break;
                    case "preset": form.PreviewOpen(4, 0); form.PreviewPreset(); break;
                    case "pick": form.PreviewPickVehicle(); break;
                    case "text": form.PreviewOpenItem(0, 22); form.PreviewPreset(); break;   // 第 22 行 = 玩家昵称（文本行）
                    case "snap": form.PreviewSnapshots(); break;
                }
                form.PreviewSideKey(int.Parse(parts[2]));
                Console.WriteLine($"SEL {parts[1]} side{parts[2]} -> {form.PreviewActiveSelection}");
            }
            else if (view.StartsWith("itemsidekey:"))
            {
                // 形如 itemsidekey:0:7[:1] —— 一级模块页按侧键，核对「侧键槽位 → 格号 → 选中」；
                // 可选的第四段先翻到模块内第 N 页，验证换页后按钮也跟着换。
                var parts = view.Split(':');
                form.PreviewOpen(int.Parse(parts[1]), 0);
                if (parts.Length > 3) form.PreviewItemPage(int.Parse(parts[3]));
                form.PreviewSideKey(int.Parse(parts[2]));
                Console.WriteLine($"SEL items p{parts[1]} page{form.PreviewActivePageIndex} side{parts[2]} -> {form.PreviewActiveSelection}");
            }
            else if (view.StartsWith("itemnextpage:"))
            {
                // 形如 itemnextpage:0 —— 走真实按钮路径翻模块内下一页
                var p = int.Parse(view.Split(':')[1]);
                form.PreviewOpen(p, 0);
                form.PreviewBottomKey(2);
                Console.WriteLine($"PAGE items p{p} after PAGE+ -> page{form.PreviewActivePageIndex} sel{form.PreviewActiveSelection}");
            }
            else if (view == "diag") { form.PreviewOpen(8, 0); }   // 模块顺序：… Airframes(7) → Diag(8)，此前写的 7 出的是机型页
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
            else if (view == "detailreset") { form.PreviewDetailReset(); }
            using var bmp = new System.Drawing.Bitmap(form.Width, form.Height);
            using (var g = System.Drawing.Graphics.FromImage(bmp))
            {
                form.RenderTo(g);
            }
            // 相对路径按当前目录解析：以前回退到 %TEMP%，写出来的图不在身边，
            // 而且旧文件不会被覆盖 —— 出图核对时会一直看到上一张（踩过一次）。
            var path = Path.IsPathRooted(outPath) ? outPath : Path.Combine(Environment.CurrentDirectory, outPath);
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





