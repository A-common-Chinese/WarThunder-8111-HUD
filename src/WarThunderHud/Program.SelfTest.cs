// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 A-common-Chinese
// This file is part of War Thunder HUD (8111), licensed under the GNU AGPL v3 or later.

// 自检从 Program.cs 里拆出来：它是纯断言集合，不参与运行时，却占了主文件五分之一。
// 拆开之后 Program.cs 只剩「启动 / 命令行 / HUD 主循环」，改动时不用再滚过上千行断言。
// ★ 新增断言请写在这里；只增不改，别在它们中间删旧的（回归靠的就是这些名字）。

using System.Globalization;
using WtHud.Telemetry;
using WtHud.Hud;
using WtHud.Graphics;
using WtHud.Overlay;

namespace WtHud;

public static partial class Program
{
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
        // 没绑键盘键的动作也要留痕：只绑鼠标时要能和「文件里没有」区分开
        var wtMouse = WtHud.Input.WtControlsFile.Parse("ID_AIR_BRAKE { mouseKey:i=0; }");
        Check("blk keeps a mouse-only action as an empty entry",
            wtMouse.TryGetValue("ID_AIR_BRAKE", out var abMouse) && abMouse.Length == 0);

        // ---- 导入小结：三种结局必须说成三句不同的话 ----
        // 背景：以前「已经是这个键位」被并进「文件里没有键位」，重复导入同一份文件就会误报。
        var exported = new Dictionary<string, int[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["ID_AIR_BRAKE"] = new[] { 48 },                     // B
            ["ID_MPSTATSCREEN"] = new[] { 29, 49 },              // CTRL+N
        };
        var imp1 = new HudConfig { AirbrakeKey = "H", ScoreboardKey = "TAB" };
        var sum1 = WtHud.Input.WtControlsImport.Apply(imp1, exported, WtHud.UI.Lang.Zh);
        Check("import writes both bindings",
            imp1.AirbrakeKey == "B" && imp1.ScoreboardKey == "CTRL+N" && sum1.StartsWith("已导入"));

        var imp2 = new HudConfig { AirbrakeKey = "B", ScoreboardKey = "CTRL+N" };
        var sum2 = WtHud.Input.WtControlsImport.Apply(imp2, exported, WtHud.UI.Lang.Zh);
        Check("re-importing the same file says no change needed",
            sum2.StartsWith("无需修改") && !sum2.Contains("文件里没有"));

        var imp3 = new HudConfig { AirbrakeKey = "H", ScoreboardKey = "TAB" };
        var sum3 = WtHud.Input.WtControlsImport.Apply(imp3,
            new Dictionary<string, int[]>(), WtHud.UI.Lang.Zh);
        Check("import of a non-controls file reports zero actions",
            sum3.Contains("0 个动作") && imp3.AirbrakeKey == "H");

        var imp4 = new HudConfig { AirbrakeKey = "H", ScoreboardKey = "TAB" };
        var sum4 = WtHud.Input.WtControlsImport.Apply(imp4,
            new Dictionary<string, int[]>
            {
                ["ID_AIR_BRAKE"] = Array.Empty<int>(),
                ["ID_MPSTATSCREEN"] = new[] { 29, 49 },
            }, WtHud.UI.Lang.Zh);
        Check("import reports a mouse-only airbrake and still takes the scoreboard",
            sum4.Contains("鼠标") && imp4.AirbrakeKey == "H" && imp4.ScoreboardKey == "CTRL+N");

        // 飞机和直升机两条都写了：取飞机那条，不能被后写的直升机覆盖
        var imp5 = new HudConfig { AirbrakeKey = "H" };
        WtHud.Input.WtControlsImport.Apply(imp5,
            new Dictionary<string, int[]>
            {
                ["ID_AIR_BRAKE"] = new[] { 48 },                 // B
                ["ID_AIR_BRAKE_HELICOPTER"] = new[] { 46 },      // C
            }, WtHud.UI.Lang.Zh);
        Check("airbrake takes the fixed-wing action first", imp5.AirbrakeKey == "B");

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

        // 中文昵称 / 中文战队标签 / 昵称带括号：这三类以前都会让 actor 对不上设置里的名字
        const string zhLine = "^雄鹰^张三 (J-7E) 击落了 李四 (F-16)";
        Check("killfeed strips chinese squad tag",
            HudKillFeed.TryParse(zhLine, out var zhActor, out var zhType, out var zhTarget)
            && zhActor == "张三" && zhType == "J-7E" && zhTarget == "李四 (F-16)");
        Check("killfeed matches chinese player name", HudKillFeed.IsOwnKill(zhLine, "张三", out _));
        Check("killfeed chinese name is not split by zero-width",
            HudKillFeed.IsOwnKill("张\u200B三 (J-7E) 击落了 李四", "张三", out _));
        Check("killfeed nickname with parentheses",
            HudKillFeed.TryParse("张三(老阳) (J-7E) 击落了 李四 (F-16)", out var pnActor, out var pnType, out _)
            && pnActor == "张三(老阳)" && pnType == "J-7E");
        Check("killfeed english client",
            HudKillFeed.TryParse("Zhang San (J-7E) shot down Li Si (F-16)", out var enActor, out var enType, out var enTarget)
            && enActor == "Zhang San" && enType == "J-7E" && enTarget == "Li Si (F-16)");
        Check("killfeed still ignores ground destroy",
            !HudKillFeed.TryParse("张三 (J-7E) 击毁了 T-72", out _, out _, out _));

        // ---- 转弯提前量：asin(ω·R/V) 与守卫 ----
        // 400 m 间距、600 km/h、长机 10°/s 右转 → 提前 24.8°（这正是"±3° 算对准"盖不住的量）
        double lead = LeadPursuitDeg(10, 400, 600, 45);
        Check("lead pursuit 400m/600kmh/10degps", Math.Abs(lead - 24.77) < 0.1);
        Check("lead pursuit left turn negative", LeadPursuitDeg(-10, 400, 600, 45) < -24);
        Check("lead pursuit no turn", LeadPursuitDeg(0, 400, 600, 45) == 0);
        // 上限钳制：慢速 + 大转向率时 asin 会顶到 70° 以上，必须按配置砍到 45
        Check("lead pursuit clamped", Math.Abs(LeadPursuitDeg(30, 400, 180, 45) - 45) < 0.01);
        // 太慢时不给提前量：50 m/s 以下这个公式会被噪声主导
        Check("lead pursuit too slow", LeadPursuitDeg(10, 400, 170, 45) == 0);

        // ---- 固定站位：站位随长机航向转、左右镜像、居中时对准 ----
        Check("station aligned when centered", Math.Abs(WingAimDeg(0, 400, 0, 0, -400)) < 1e-9);
        Check("station responds to lead heading",
            Math.Abs(WingAimDeg(0, 400, 30, 150, -370) - WingAimDeg(0, 400, 0, 150, -370)) > 5);
        Check("station left/right mirror at zero delta",
            Math.Abs(WingAimDeg(0, 400, 0, -150, -370) + WingAimDeg(0, 400, 0, 150, -370)) < 1e-9);
        Check("station center shifts with rel",
            Math.Abs(WingAimDeg(30, 400, 0, 0, -400) - WingAimDeg(0, 400, 0, 0, -400)) > 5);
        // ---- 队形：预设只是「填数」，几何永远来自四个数值 ----
        var shapeCfg = new HudConfig();
        WtHud.Hud.FormationGeometry.ApplyPreset(shapeCfg, WtHud.Hud.FormationGeometry.ShapeEchelon, 0);
        var e1 = WtHud.Hud.FormationGeometry.Station(shapeCfg);
        Check("echelon preset slot1",
            e1.SideM == -250 && e1.BackM == -450 && Math.Abs(e1.RangeM - 514.78) < 0.1);
        WtHud.Hud.FormationGeometry.ApplyPreset(shapeCfg, WtHud.Hud.FormationGeometry.ShapeEchelon, 2);
        var e3 = WtHud.Hud.FormationGeometry.Station(shapeCfg);
        Check("echelon preset slot3",
            e3.SideM == -750 && e3.BackM == -1350 && Math.Abs(e3.RangeM - 1544.3) < 0.2);
        // 间距整体缩放：450 → 900，每个数都翻倍，形状不变
        shapeCfg.FormationEchelonSpacingM = 900;
        WtHud.Hud.FormationGeometry.ApplyPreset(shapeCfg, WtHud.Hud.FormationGeometry.ShapeEchelon, 0);
        var e1b = WtHud.Hud.FormationGeometry.Station(shapeCfg);
        Check("spacing scales the whole preset",
            e1b.SideM == -500 && e1b.BackM == -900 && Math.Abs(e1b.UpM - 50) < 1e-9);
        shapeCfg.FormationEchelonSpacingM = 450;
        WtHud.Hud.FormationGeometry.ApplyPreset(shapeCfg, WtHud.Hud.FormationGeometry.ShapeColumn, 1);
        var c2 = WtHud.Hud.FormationGeometry.Station(shapeCfg);
        Check("column preset slot2", c2.SideM == 0 && c2.BackM == -800 && Math.Abs(c2.RangeM - 800) < 0.01);
        WtHud.Hud.FormationGeometry.ApplyPreset(shapeCfg, WtHud.Hud.FormationGeometry.ShapeDiamond, 2);
        var d3 = WtHud.Hud.FormationGeometry.Station(shapeCfg);
        Check("diamond preset slot3", d3.SideM == 0 && d3.BackM == -600
            && Math.Abs(d3.RangeM - 600) < 0.01 && Math.Abs(d3.UpM - 30) < 1e-9);
        WtHud.Hud.FormationGeometry.ApplyPreset(shapeCfg, WtHud.Hud.FormationGeometry.ShapeDiamond, 0);
        Check("diamond preset slot1 is left",
            WtHud.Hud.FormationGeometry.Station(shapeCfg).SideM == -300);

        // 手工数值：侧位定左右，斜距按真实几何
        var manual = new HudConfig
        {
            FormationWingSide = -1, FormationWingOffsetM = 150, FormationStationBackM = 370,
        };
        var leg = WtHud.Hud.FormationGeometry.Station(manual);
        Check("manual side station uses true slant",
            leg.SideM == -150 && leg.BackM == -370 && Math.Abs(leg.RangeM - 399.25) < 0.01);
        manual.FormationWingOffsetM = 300;
        manual.FormationStationBackM = 400;
        Check("manual station follows the numbers given",
            Math.Abs(WtHud.Hud.FormationGeometry.Station(manual).RangeM - 500) < 0.001);
        Check("done band scales with the slant", Math.Abs(FormationRangeErrorPct(550, 500) - 10) < 1e-9);
        // 尾随位横偏乘 0：同一份配置换成正后，横向必须归零
        manual.FormationWingSide = 0;
        Check("trail side ignores the lateral offset",
            WtHud.Hud.FormationGeometry.Station(manual).SideM == 0);

        // 快照：套预设前存，切回自定义还原
        var snapCfg = new HudConfig { FormationWingSide = 0, FormationStationBackM = 820 };
        snapCfg.SnapshotCustom();
        WtHud.Hud.FormationGeometry.ApplyPreset(snapCfg, WtHud.Hud.FormationGeometry.ShapeColumn, 0);
        Check("preset overwrites the numbers", Math.Abs(snapCfg.FormationStationBackM - 400) < 0.01);
        snapCfg.RestoreCustom();
        Check("custom snapshot restores the numbers",
            snapCfg.FormationWingSide == 0 && Math.Abs(snapCfg.FormationStationBackM - 820) < 0.01);

        // 迁移：旧配置把「尾随距离」并进唯一的后距，只跑一次
        var oldCfg = new HudConfig
        {
            FormationModel = 0, FormationShape = "custom", FormationWingSide = 0,
            LegacyFormationRangeM = 620, FormationStationBackM = 370, FormationFarRangeM = 2000,
        };
        oldCfg.MigrateFormation();
        Check("legacy trail range becomes the back distance",
            Math.Abs(oldCfg.FormationStationBackM - 620) < 0.01 && oldCfg.FormationModel == 3
            && oldCfg.LegacyFormationRangeM is null);
        // 爬升阶段的远距提示：没动过的 2000 收到 0（严格两阶段），自己调过的不碰
        Check("untouched far range folds to zero",
            oldCfg.FormationFarRangeM == 0);
        var tunedFar = new HudConfig { FormationModel = 2, FormationFarRangeM = 2600 };
        tunedFar.MigrateFormation();
        Check("a hand-tuned far range survives the migration",
            Math.Abs(tunedFar.FormationFarRangeM - 2600) < 0.01 && tunedFar.FormationModel == 3);
        var oldNamed = new HudConfig { FormationModel = 0, FormationShape = "diamond", FormationSlot = 2 };
        oldNamed.MigrateFormation();
        Check("legacy named shape is rewritten from the preset",
            Math.Abs(oldNamed.FormationStationBackM - 600) < 0.01
            && Math.Abs(oldNamed.FormationUpM - 30) < 0.01);

        // ---- 长机转向辅助：目标坡度/转向率与「反向 1 s 才重锁」 ----
        var (bank3, rate3) = TurnAssistTargets(3.0, 800);
        Check("turn targets 3G bank", Math.Abs(bank3 - 70.53) < 0.05);
        Check("turn targets 3G rate at 800kmh", Math.Abs(rate3 - 7.15) < 0.05);
        var (bank6, _) = TurnAssistTargets(6.0, 800);
        Check("turn targets 6G bank", Math.Abs(bank6 - 80.41) < 0.05);
        Check("turn targets clamp low G", Math.Abs(TurnAssistTargets(0.5, 800).BankDeg - 33.56) < 0.05);

        var st = new TurnAssistState(TurnAssistStage.Off, 0, -1);
        // ---- 数据链目标坡度段：追加、可缺省、越界只丢自己 ----
        var pBank = WtHud.Telemetry.DataLink.BuildPayload(7, 3000, 0, 90, 0, 0.5, 0.5, -54.3);
        Check("datalink carries target bank", pBank.Split('|').Length == 9);
        Check("datalink parses target bank",
            WtHud.Telemetry.DataLink.TryParsePayload(pBank, out _, out _, out _, out _, out _,
                                                     out _, out _, out double? bankOut)
            && bankOut is double bb && Math.Abs(bb + 54.3) < 0.05);
        var pNoBank = WtHud.Telemetry.DataLink.BuildPayload(7, 3000, 0, 90, 0, 0.5, 0.5);
        Check("datalink 8-field has no bank",
            WtHud.Telemetry.DataLink.TryParsePayload(pNoBank, out _, out _, out _, out _, out _,
                                                     out _, out _, out double? bankNone)
            && bankNone is null);
        Check("datalink bad bank segment ignored",
            WtHud.Telemetry.DataLink.TryParsePayload(pNoBank + "|9999", out _, out _, out _, out _, out _,
                                                     out _, out _, out double? bankBad)
            && bankBad is null);

        // ---- 数据链角色（第 10 段）：只在坐标存在时追加，老读端照收 ----
        var pLead = WtHud.Telemetry.DataLink.BuildPayload(123456, 3000, 5, 90, 1.5, 0.5, 0.5, -30,
                                                          WtHud.Telemetry.DataLink.LinkRole.Lead);
        Check("role packet has 10 fields", pLead.Split('|').Length == 10);
        Check("role round-trips as lead",
            WtHud.Telemetry.DataLink.TryParsePayload(pLead, out _, out _, out _, out _, out _,
                                                     out _, out _, out _, out var roleOut)
            && roleOut == WtHud.Telemetry.DataLink.LinkRole.Lead);
        // 老签名（9 个 out）必须继续能用：漏了它等于把 selftest 自己搞崩
        Check("old 9-out parser still accepts role packet",
            WtHud.Telemetry.DataLink.TryParsePayload(pLead, out _, out _, out _, out _, out _,
                                                     out _, out _, out double? bankKeep)
            && bankKeep is double bk && Math.Abs(bk + 30) < 0.05);
        Check("no role keeps 9 fields",
            WtHud.Telemetry.DataLink.BuildPayload(1, 3000, 0, 90, 0, 0.5, 0.5, -30).Split('|').Length == 9);
        // 没有坡度但有角色：第 9 段留空占位，角色必须还在第 10 段
        var pWingNoBank = WtHud.Telemetry.DataLink.BuildPayload(1, 3000, 0, 90, 0, 0.5, 0.5, null,
                                                               WtHud.Telemetry.DataLink.LinkRole.Wing);
        var wParts = pWingNoBank.Split('|');
        Check("role keeps its slot when bank is absent",
            wParts.Length == 10 && wParts[8].Length == 0 && wParts[9] == "2");
        Check("role packet parses with null bank",
            WtHud.Telemetry.DataLink.TryParsePayload(pWingNoBank, out _, out _, out _, out _, out _,
                                                     out _, out _, out double? bankW, out var roleW)
            && bankW is null && roleW == WtHud.Telemetry.DataLink.LinkRole.Wing);

        // ---- 解析加固：ID 越界、空白、BOM ----
        Check("node id 0 rejected",
            !WtHud.Telemetry.DataLink.TryParsePayload("WTDL2|0|3000|0|90|0", out _, out _, out _, out _, out _));
        Check("node id 1000000 rejected",
            !WtHud.Telemetry.DataLink.TryParsePayload("WTDL2|1000000|3000|0|90|0", out _, out _, out _, out _, out _));
        Check("surrounding whitespace tolerated",
            WtHud.Telemetry.DataLink.TryParsePayload("  WTDL2|42|3000|0|90|0  \r\n", out _, out _, out _, out _, out _));
        Check("BOM tolerated",
            WtHud.Telemetry.DataLink.TryParsePayload("\uFEFFWTDL2|42|3000|0|90|0", out _, out _, out _, out _, out _));

        // ---- 长机选择：手填 ID → 唯一长机 → 单节点 → 最近活跃（歧义） ----
        long tn = 1_000_000;
        var nSolo = new (int Id, int Role, long TickAt)[] { (11, (int)WtHud.Telemetry.DataLink.LinkRole.Wing, tn - 100) };
        var sSolo = WtHud.Telemetry.DataLink.SelectLead(nSolo, 0, tn);
        Check("solo node is the lead", sSolo.Index == 0
            && sSolo.Source == WtHud.Telemetry.DataLink.LeadSource.SingleNode && !sSolo.Ambiguous);

        var nPair = new (int Id, int Role, long TickAt)[]
        {
            (11, (int)WtHud.Telemetry.DataLink.LinkRole.Wing, tn - 50),
            (22, (int)WtHud.Telemetry.DataLink.LinkRole.Lead, tn - 400),
        };
        var sPair = WtHud.Telemetry.DataLink.SelectLead(nPair, 0, tn);
        Check("unique lead wins over newer wing", sPair.Index == 1
            && sPair.Source == WtHud.Telemetry.DataLink.LeadSource.ByRole && !sPair.Ambiguous);

        var nBothLead = new (int Id, int Role, long TickAt)[]
        {
            (11, (int)WtHud.Telemetry.DataLink.LinkRole.Lead, tn - 500),
            (22, (int)WtHud.Telemetry.DataLink.LinkRole.Lead, tn - 50),
        };
        var sBoth = WtHud.Telemetry.DataLink.SelectLead(nBothLead, 0, tn);
        Check("two leads fall back to newest and flag it", sBoth.Index == 1 && sBoth.Ambiguous
            && sBoth.Source == WtHud.Telemetry.DataLink.LeadSource.Newest);

        var sById = WtHud.Telemetry.DataLink.SelectLead(nPair, 11, tn);
        Check("configured lead id wins", sById.Index == 0 && !sById.Ambiguous
            && sById.Source == WtHud.Telemetry.DataLink.LeadSource.ByConfig);
        var sBadId = WtHud.Telemetry.DataLink.SelectLead(nPair, 99, tn);
        Check("configured id that is absent means no lead", sBadId.Index == -1
            && sBadId.Source == WtHud.Telemetry.DataLink.LeadSource.None);

        var nStale = new (int Id, int Role, long TickAt)[]
        {
            (11, (int)WtHud.Telemetry.DataLink.LinkRole.Lead, tn - 5000),
        };
        Check("stale node is not a lead", WtHud.Telemetry.DataLink.SelectLead(nStale, 0, tn).Index == -1);
        Check("empty node list has no lead",
            WtHud.Telemetry.DataLink.SelectLead(Array.Empty<(int, int, long)>(), 0, tn).Index == -1);

        var nThree = new (int Id, int Role, long TickAt)[]
        {
            (11, (int)WtHud.Telemetry.DataLink.LinkRole.Wing, tn - 10),
            (22, (int)WtHud.Telemetry.DataLink.LinkRole.Lead, tn - 900),
            (33, (int)WtHud.Telemetry.DataLink.LinkRole.Wing, tn - 5),
        };
        Check("three-ship still finds the lead", WtHud.Telemetry.DataLink.SelectLead(nThree, 0, tn).Index == 1);

        // ---- 显示平滑：dt=0 不动、dt≫tau 到底、单调不过冲 ----
        Check("smooth snaps when dt is zero", Math.Abs(SmoothStep(10, 50, 0, 0.3) - 50) < 1e-9);
        Check("smooth reaches target after 10 tau", Math.Abs(SmoothStep(0, 100, 3.0, 0.3) - 100) < 0.1);
        double sm = 0;
        for (int i = 0; i < 20; i++) sm = SmoothStep(sm, 100, 0.05, 0.3);
        Check("smooth monotonic and no overshoot", sm > 0 && sm < 100);

        // ---- 垂直引导：增益、限幅、槽位高度的符号 ----
        Check("vert rate clamps at max", Math.Abs(VerticalRateCmd(300, 10) - 10) < 1e-9);
        Check("vert rate proportional", Math.Abs(VerticalRateCmd(20, 10) - 3.0) < 1e-9);
        Check("vert rate symmetric", Math.Abs(VerticalRateCmd(-20, 10) + 3.0) < 1e-9);
        Check("vert rate zero at target", VerticalRateCmd(0, 10) == 0);
        var vShape = new HudConfig();
        WtHud.Hud.FormationGeometry.ApplyPreset(vShape, WtHud.Hud.FormationGeometry.ShapeEchelon, 0);
        Check("echelon slot1 is 25 m up",
            WtHud.Hud.FormationGeometry.Station(vShape).UpM == 25
            && WtHud.Hud.FormationGeometry.Station(vShape).TargetDeltaHM == -25);
        WtHud.Hud.FormationGeometry.ApplyPreset(vShape, WtHud.Hud.FormationGeometry.ShapeDiamond, 2);
        Check("diamond rear slot is 30 m up", WtHud.Hud.FormationGeometry.Station(vShape).UpM == 30);
        vShape.FormationUpM = -40;
        Check("manual vertical offset flows into the station",
            WtHud.Hud.FormationGeometry.Station(vShape).UpM == -40
            && WtHud.Hud.FormationGeometry.Station(vShape).TargetDeltaHM == 40);

        // ---- 到位判定改百分比：同一套门限在 400 m 与 1545 m 的槽位上都成立 ----
        Check("range error pct at 10 percent", Math.Abs(FormationRangeErrorPct(440, 400) - 10.0) < 1e-9);
        Check("range error pct symmetric", Math.Abs(FormationRangeErrorPct(360, 400) - 10.0) < 1e-9);
        Check("range error pct scales with slot",
            Math.Abs(FormationRangeErrorPct(1545 * 1.1, 1545) - 10.0) < 1e-9);
        Check("range error pct guards zero target", FormationRangeErrorPct(500, 0) == 0);

        // ---- 主菜单侧键：注册名与按下时发出的名字必须是同一个（物理键 ↔ 格子两套编号）----
        bool menuSideOk = true;
        for (int cell = 0; cell < 10; cell++)
        {
            int key = WtHud.UI.Mfd.Controls.MfdPageLayout.SideKeyFromCell(cell);
            int back = WtHud.UI.Mfd.Controls.MfdPageLayout.CellFromSideKey(key);
            if (back != cell) menuSideOk = false;
            if (WtHud.UI.Mfd.MfdFrameForm.MenuSideSlotName(cell) != "SIDE" + back) menuSideOk = false;
        }
        Check("menu side key names round-trip", menuSideOk);
        // 格号 → 全局模块下标：菜单多页时不能把格号当模块号用
        Check("menu global index page 0", WtHud.UI.Mfd.MfdFrameForm.MenuGlobalIndex(0, 3) == 3);
        Check("menu global index page 1", WtHud.UI.Mfd.MfdFrameForm.MenuGlobalIndex(1, 3) == 13);
        Check("menu global index last cell of page 1",
            WtHud.UI.Mfd.MfdFrameForm.MenuGlobalIndex(2, 9) == 29);

        // ---- 面板分页：键漏了不会报错，屏幕上会直接印出键名（曾经出现过 val.roleWing）----
        var modules = WtHud.UI.Mfd.MfdPages.Build();
        Check("menu has 10 modules", modules.Count == 10);
        bool menuTitlesOk = true, rowLabelsOk = true;
        foreach (var pg in modules)
        {
            if (WtHud.UI.L10n.Get(WtHud.UI.LangScope.Screen, pg.TitleKey) == pg.TitleKey) menuTitlesOk = false;
            if (WtHud.UI.L10n.Get(WtHud.UI.LangScope.Screen, pg.MenuKey) == pg.MenuKey) menuTitlesOk = false;
            if (WtHud.UI.L10n.Get(WtHud.UI.LangScope.Screen, pg.MenuHintKey) == pg.MenuHintKey) menuTitlesOk = false;
            // 行标签走 Screen 表（Panel 表只有面板按键那几个词）。
            // 机型页的标签就是机型 id 本身，没有对应文案，跳过。
            if (pg.TitleKey == "page.airframe") continue;
            foreach (var it in pg.Items)
                if (WtHud.UI.L10n.Get(WtHud.UI.LangScope.Screen, it.LabelKey) == it.LabelKey) rowLabelsOk = false;
        }
        Check("every module title resolves", menuTitlesOk);
        Check("every row label resolves", rowLabelsOk);

        // ---- 第 11 段：僚机自报站位 ----
        var pWingRep = WtHud.Telemetry.DataLink.BuildPayload(
            654321, 3000, 0, 90, 0, 0.5, 0.5, 0,
            WtHud.Telemetry.DataLink.LinkRole.Wing, 1,
            WtHud.Telemetry.DataLink.WingState.Behind, 480);
        Check("wing report has 12 fields", pWingRep.Split('|').Length == 12);
        Check("wing report round-trips",
            WtHud.Telemetry.DataLink.TryParsePayload(pWingRep, out _, out _, out _, out _, out _,
                                                     out _, out _, out _, out _,
                                                     out int slotOut, out var wingOut, out int errOut)
            && slotOut == 1 && wingOut == WtHud.Telemetry.DataLink.WingState.Behind && errOut == 480);
        // 老格式：没有第 11 段 → 状态未知，其余照旧
        var pNoWing = WtHud.Telemetry.DataLink.BuildPayload(
            654321, 3000, 0, 90, 0, 0.5, 0.5, 0, WtHud.Telemetry.DataLink.LinkRole.Wing);
        Check("no status keeps 10 fields", pNoWing.Split('|').Length == 10);
        Check("missing status parses as unknown",
            WtHud.Telemetry.DataLink.TryParsePayload(pNoWing, out _, out _, out _, out _, out _,
                                                     out _, out _, out _, out _,
                                                     out int slotNone, out var wingNone, out int errNone)
            && slotNone == -1 && wingNone == WtHud.Telemetry.DataLink.WingState.Unknown && errNone == 0);
        // 脏子段：整段作废，但**包不能丢** —— 高度和角色仍然有效
        Check("dirty status segment keeps the packet",
            WtHud.Telemetry.DataLink.TryParsePayload(
                "WTDL2|654321|3000|0|90|0|50000|50000|0|2|1,9,abc",
                out _, out _, out _, out _, out _, out _, out _, out _, out _,
                out _, out var wingDirty, out _)
            && wingDirty == WtHud.Telemetry.DataLink.WingState.Unknown);
        Check("status slot out of range rejected",
            WtHud.Telemetry.DataLink.TryParsePayload(
                "WTDL2|654321|3000|0|90|0|50000|50000|0|2|9,1,480",
                out _, out _, out _, out _, out _, out _, out _, out _, out _,
                out int slotBad, out var wingBad, out _)
            && slotBad == -1 && wingBad == WtHud.Telemetry.DataLink.WingState.Unknown);

        // ---- 没有坐标：身份和站位必须照发，段位不挪 ----
        // ★ 这是「长机显示数据错误」的前提：旧实现整包提前截断，长机连这架是僚机都认不出。
        var pNoPos = WtHud.Telemetry.DataLink.BuildPayload(
            654321, 3000, 0, 90, 0, null, null, 0,
            WtHud.Telemetry.DataLink.LinkRole.Wing, 2,
            WtHud.Telemetry.DataLink.WingState.Unknown, 0);
        var pnp = pNoPos.Split('|');
        Check("no position keeps the field layout", pnp.Length == 12
            && pnp[6] == "" && pnp[7] == "" && pnp[9] == "2" && pnp[10] == "2,-1,0"
            && pnp[11] == "0");
        Check("no position still parses role and slot",
            WtHud.Telemetry.DataLink.TryParsePayload(pNoPos, out _, out _, out _, out _, out _,
                                                     out var xNp, out var yNp, out _, out var roleNp,
                                                     out int slotNp, out var wingNp, out _)
            && xNp is null && yNp is null
            && roleNp == WtHud.Telemetry.DataLink.LinkRole.Wing
            && slotNp == 2 && wingNp == WtHud.Telemetry.DataLink.WingState.Unknown);
        // 既没坐标又没身份：回到 6 段纯高度包，老读端看到的和以前一样
        Check("no position and no role stays 6 fields",
            WtHud.Telemetry.DataLink.BuildPayload(1, 100, 0, 0, 0, null, null).Split('|').Length == 6);

        // ---- 跟随长机（第 12 段）：两家长机同时在线时靠它把两张表分开 ----
        var pLeadId = WtHud.Telemetry.DataLink.BuildPayload(
            654321, 3000, 0, 90, 0, 0.5, 0.5, 0,
            WtHud.Telemetry.DataLink.LinkRole.Wing, 1,
            WtHud.Telemetry.DataLink.WingState.InPosition, 0, 555001);
        Check("lead id is the 12th field",
            pLeadId.Split('|').Length == 12 && pLeadId.EndsWith("|555001", StringComparison.Ordinal));
        Check("lead id round-trips",
            WtHud.Telemetry.DataLink.TryParsePayload(pLeadId, out _, out _, out _, out _, out _,
                                                     out _, out _, out _, out _,
                                                     out _, out _, out _, out int leadOut)
            && leadOut == 555001);
        Check("old packet has no lead id",
            WtHud.Telemetry.DataLink.TryParsePayload(pWingRep, out _, out _, out _, out _, out _,
                                                     out _, out _, out _, out _,
                                                     out _, out _, out _, out int leadOld)
            && leadOld == 0);
        Check("lead id without a station segment stays 10 fields",
            WtHud.Telemetry.DataLink.BuildPayload(
                1, 3000, 0, 90, 0, 0.5, 0.5, 0,
                WtHud.Telemetry.DataLink.LinkRole.Wing, -1, WtHud.Telemetry.DataLink.WingState.Unknown, 0,
                555001).Split('|').Length == 10);

        // ---- 队标锚点（第 13 段）：机场坐标 + 我方视角的友军标志 ----
        var pAnchor = WtHud.Telemetry.DataLink.BuildPayload(
            7, 3000, 0, 90, 0, 0.5, 0.5, null,
            WtHud.Telemetry.DataLink.LinkRole.Lead, -1,
            WtHud.Telemetry.DataLink.WingState.Unknown, 0, 0,
            0.25, 0.75, true);
        Check("anchor is always the 13th field", pAnchor.Split('|').Length == 13);
        Check("anchor round-trips",
            WtHud.Telemetry.DataLink.TryParsePayload(pAnchor, out _, out _, out _, out _, out _,
                                                     out _, out _, out _, out _,
                                                     out _, out _, out _, out _, out var anchorOut)
            && anchorOut is { } an
            && Math.Abs(an.Xn - 0.25) < 1e-4 && Math.Abs(an.Yn - 0.75) < 1e-4 && an.Friendly);
        Check("packet without an anchor leaves it null",
            WtHud.Telemetry.DataLink.TryParsePayload(pLeadId, out _, out _, out _, out _, out _,
                                                     out _, out _, out _, out _,
                                                     out _, out _, out _, out _, out var noAnchor)
            && noAnchor is null);

        // ---- 队标校验：同一座机场，两队看到相反的颜色 ----
        var airfields = new[]
        {
            new WtHud.Telemetry.MapPoint(0.20, 0.20, true, 0),
            new WtHud.Telemetry.MapPoint(0.80, 0.80, false, 0),
        };
        Check("team check accepts the same side",
            VerifyTeam(airfields, 0.20, 0.20, true) == WtHud.Telemetry.DataLink.TeamVerdict.Same);
        Check("team check rejects the other side",
            VerifyTeam(airfields, 0.20, 0.20, false) == WtHud.Telemetry.DataLink.TeamVerdict.Other);
        Check("team check blocks an airfield it cannot find",
            VerifyTeam(airfields, 0.50, 0.50, true) == WtHud.Telemetry.DataLink.TeamVerdict.Other);
        Check("team check with no airfields is unknown",
            VerifyTeam(Array.Empty<WtHud.Telemetry.MapPoint>(), 0.2, 0.2, true)
            == WtHud.Telemetry.DataLink.TeamVerdict.Unknown);
        // 选锚点时只认友军机场、且不限距离：僚机飞在敌方机场旁边也不会锚错
        Check("anchor selection prefers a friendly field anywhere",
            TryNearestAirfield(airfields, 0.79, 0.79, out var picked, friendlyOnly: true, tolerance: 2.0)
            && Math.Abs(picked.X - 0.20) < 1e-9 && picked.Friendly);

        // 丢包决策：只有明确判成异队才丢，老包/不可判一律放行
        var anchorSame = new WtHud.Telemetry.DataLink.TeamAnchor(0.2, 0.2, true);
        var anchorOther = new WtHud.Telemetry.DataLink.TeamAnchor(0.8, 0.8, true);
        Check("anchor for the same side is kept",
            !WtHud.Telemetry.DataLink.RejectsAnchor(anchorSame, (x, y, f) => VerifyTeam(airfields, x, y, f)));
        Check("anchor for the other side is dropped",
            WtHud.Telemetry.DataLink.RejectsAnchor(anchorOther, (x, y, f) => VerifyTeam(airfields, x, y, f)));
        Check("packet without an anchor is never dropped",
            !WtHud.Telemetry.DataLink.RejectsAnchor(null, (_, _, _) => WtHud.Telemetry.DataLink.TeamVerdict.Other));
        Check("no checker means no filtering",
            !WtHud.Telemetry.DataLink.RejectsAnchor(anchorOther, null));

        // ---- 站位状态判定：与状态机脱离 MAINTAIN 同一条线 ----
        var wc = new HudConfig { FormationDropPct = 30, FormationAlignDeg = 5 };
        Check("200 m over a 400 m slot is behind",
            WingStateFrom(200, 0, 400, wc) == WtHud.Telemetry.DataLink.WingState.Behind);
        Check("50 m over a 400 m slot is in position",
            WingStateFrom(50, 0, 400, wc) == WtHud.Telemetry.DataLink.WingState.InPosition);
        Check("200 m short is ahead",
            WingStateFrom(-200, 0, 400, wc) == WtHud.Telemetry.DataLink.WingState.Ahead);
        Check("close but 20 deg off is off-station",
            WingStateFrom(50, 20, 400, wc) == WtHud.Telemetry.DataLink.WingState.OffStation);
        Check("close and 5 deg off is in position",
            WingStateFrom(50, 5, 400, wc) == WtHud.Telemetry.DataLink.WingState.InPosition);
        Check("zero slot range does not divide by zero",
            WingStateFrom(50, 0, 0, wc) == WtHud.Telemetry.DataLink.WingState.InPosition);

        // ---- 长机侧：谁掉队，按偏离量从大到小 ----
        var nrOk = new WtHud.Telemetry.DataLink.LinkNode(11, 3000, 0, 0, 0, 0.5, 0.5, 0,
            0, WtHud.Telemetry.DataLink.LinkRole.Wing, 5,
            WtHud.Telemetry.DataLink.WingState.InPosition, 0, 0);
        var nrMid = new WtHud.Telemetry.DataLink.LinkNode(22, 3000, 0, 0, 0, 0.5, 0.5, 0,
            0, WtHud.Telemetry.DataLink.LinkRole.Wing, 5,
            WtHud.Telemetry.DataLink.WingState.Behind, 1, 320);
        var nrBad = new WtHud.Telemetry.DataLink.LinkNode(33, 3000, 0, 0, 0, 0.5, 0.5, 0,
            0, WtHud.Telemetry.DataLink.LinkRole.Wing, 5,
            WtHud.Telemetry.DataLink.WingState.Behind, 2, 880);
        var reports = WingReports(new[] { nrOk, nrMid, nrBad });
        Check("reports skip in-position wings", reports.Count == 2);
        Check("worst offender comes first", reports[0].Id == 33);
        Check("empty node list has no reports", WingReports(null).Count == 0);
        Check("wing label uses the slot", WingLabel(nrBad) == "W3");
        Check("wing label falls back to id",
            WingLabel(new WtHud.Telemetry.DataLink.LinkNode(424242, 0, 0, 0, 0, null, null, 0,
                0, WtHud.Telemetry.DataLink.LinkRole.Wing, 0,
                WtHud.Telemetry.DataLink.WingState.Behind, -1, 100)) == "#424242");

        // ---- 两条编队路径：几何算得一样，缺坐标时明确降级 ----
        var relEast = FormationResolver.Relative(0.5, 0.5, 0.6, 0.5, 100000, 100000, false, 90);
        Check("relative east: 10 km, zero offset",
            Math.Abs(relEast.RangeM - 10000) < 1 && Math.Abs(relEast.RelBearingDeg) < 0.01);
        var relNorth = FormationResolver.Relative(0.5, 0.5, 0.5, 0.6, 100000, 100000, false, 0);
        Check("relative north: 10 km, zero offset",
            Math.Abs(relNorth.RangeM - 10000) < 1 && Math.Abs(relNorth.RelBearingDeg) < 0.01);
        var relWest = FormationResolver.Relative(0.5, 0.5, 0.4, 0.5, 100000, 100000, false, 0);
        Check("relative west reads -90", Math.Abs(relWest.RelBearingDeg + 90) < 0.01);
        Check("normalize wraps 190 to -170", Math.Abs(FormationResolver.Normalize(190) + 170) < 1e-9);
        Check("normalize keeps 180", Math.Abs(FormationResolver.Normalize(180) - 180) < 1e-9);

        var linkWithPos = new WtHud.Telemetry.DataLink.LinkNode(77, 3000, 0, 90, 0, 0.6, 0.5, 0,
            0, WtHud.Telemetry.DataLink.LinkRole.Lead, 3);
        var tLink = FormationResolver.ByLink(linkWithPos, 0.5, 0.5, 100000, 100000, false, 90);
        Check("link target keeps horizontal when own position exists",
            tLink.Source == FormationSource.Link && tLink.HasHorizontal && tLink.RangeM > 9000);
        var tNoSelf = FormationResolver.ByLink(linkWithPos, null, null, 100000, 100000, false, 90);
        Check("link target degrades without own position",
            tNoSelf.Source == FormationSource.Link && !tNoSelf.HasHorizontal);
        var linkNoPos = new WtHud.Telemetry.DataLink.LinkNode(78, 3000, 0, 90, 0, null, null, 0,
            0, WtHud.Telemetry.DataLink.LinkRole.Lead, 3);
        Check("link target degrades when the lead sends no coordinates",
            !FormationResolver.ByLink(linkNoPos, 0.5, 0.5, 100000, 100000, false, 0).HasHorizontal);
        var tMap = FormationResolver.ByMap(400, 12);
        Check("map target passes values through",
            tMap.Source == FormationSource.Map && tMap.HasHorizontal
            && tMap.RangeM == 400 && tMap.RelBearingDeg == 12);

        // ---- 只跟地图可见：地图上没有这架友机就不给水平引导 ----
        var mapFr = new List<WtHud.Hud.FriendlyContact> { new(10, 400, "Fighter") };
        Check("map visibility matches range and bearing",
            WtHud.Hud.FormationResolver.VisibleOnMap(mapFr, 430, 12));
        Check("map visibility rejects an unshown lead",
            !WtHud.Hud.FormationResolver.VisibleOnMap(mapFr, 1200, 12));
        Check("map visibility rejects an empty list",
            !WtHud.Hud.FormationResolver.VisibleOnMap(
                Array.Empty<WtHud.Hud.FriendlyContact>(), 400, 10));
        Check("map visibility tolerance is bounded",
            !WtHud.Hud.FormationResolver.VisibleOnMap(mapFr, 400, 40));
        // ★ 实测反馈：两机贴在一起时提示一直闪。原因是判据只有固定角度（40 m 处 25° = 17 m），
        //   而 4 Hz 报文在 250 m/s 时就漂 60 m —— 近距离必然在边界上抖。
        Check("map visibility holds at close range even without a map icon",
            WtHud.Hud.FormationResolver.VisibleOnMap(
                Array.Empty<WtHud.Hud.FriendlyContact>(), 150, 10));
        var mapClose = new List<WtHud.Hud.FriendlyContact> { new(40, 60, "Fighter") };
        Check("map visibility tolerates close-range bearing jitter",
            WtHud.Hud.FormationResolver.VisibleOnMap(mapClose, 55, 5));
        var mapNear = new List<WtHud.Hud.FriendlyContact> { new(30, 210, "Fighter") };
        Check("map visibility uses metres, not a fixed 25 degrees",
            WtHud.Hud.FormationResolver.VisibleOnMap(mapNear, 210, 0));
        Check("map visibility still rejects a hidden lead when another wingman is near",
            !WtHud.Hud.FormationResolver.VisibleOnMap(
                new List<WtHud.Hud.FriendlyContact> { new(0, 400, "Fighter") }, 1200, 0));

        // 迟滞：命中后 0.8 s 内判不到也算可见，过了才放掉（压住边界闪烁）
        bool latch = false;
        double hitAt = double.NegativeInfinity;
        Check("map latch starts hidden",
            !WtHud.Hud.FormationResolver.LatchVisible(false, ref latch, ref hitAt, 0));
        Check("map latch holds through a miss",
            WtHud.Hud.FormationResolver.LatchVisible(true, ref latch, ref hitAt, 1.0)
            && WtHud.Hud.FormationResolver.LatchVisible(false, ref latch, ref hitAt, 1.5));
        Check("map latch releases after the hold window",
            !WtHud.Hud.FormationResolver.LatchVisible(false, ref latch, ref hitAt, 1.9));

        // ---- 延迟外推：链路一包比本机地图旧 250 ms，年龄差会直接变成距离误差 ----
        const double mapSize = 65000;
        double vEast = 500 / 3.6;                        // 500 km/h 向东
        double leadX = 0.5 + 400 / mapSize, leadY = 0.5;  // 长机在正东 400 m
        double staleLeadX = leadX - vEast * 0.25 / mapSize;   // 那一包是 250 ms 之前的

        var (rawRangeM, _) = WtHud.Hud.FormationResolver.Relative(
            0.5, 0.5, staleLeadX, leadY, mapSize, mapSize, false, 90);
        Check("one stale packet reads as a shorter range", Math.Abs(rawRangeM - 365) < 1.5);

        var (fixedX, fixedY) = WtHud.Hud.FormationResolver.Advance(
            staleLeadX, leadY, vEast / mapSize, 0, 0.25);
        var (fixedRangeM, _) = WtHud.Hud.FormationResolver.Relative(
            0.5, 0.5, fixedX, fixedY, mapSize, mapSize, false, 90);
        Check("advancing recovers the true range", Math.Abs(fixedRangeM - 400) < 0.5);

        // 没有年龄 / 没有速度（第一包）都不能动位置；外推封顶 1 s
        var (sameX, sameY) = WtHud.Hud.FormationResolver.Advance(0.5, 0.5, 1, 1, 0);
        Check("advance without age is a no-op", sameX == 0.5 && sameY == 0.5);
        var (noVelX, _) = WtHud.Hud.FormationResolver.Advance(0.5, 0.5, 0, 0, 0.25);
        Check("advance without velocity is a no-op", noVelX == 0.5);
        var (cappedX, _) = WtHud.Hud.FormationResolver.Advance(0.5, 0.5, 0.001, 0, 5.0);
        Check("advance caps at one second", Math.Abs(cappedX - 0.501) < 1e-9);
        var (nanX, _) = WtHud.Hud.FormationResolver.Advance(0.5, 0.5, double.NaN, 0, 0.25);
        Check("advance ignores a non-finite velocity", nanX == 0.5);

        // ---- 角色行：读推导值、只在角色变化时套队形 ----
        var roleCfg = new HudConfig { LeadMode = false, FormationSlot = 0 };
        roleCfg.FormationAssist = false;
        roleCfg.DataLinkEnabled = false;
        WtHud.UI.Mfd.MfdPages.SetRole(roleCfg, 2);                 // 僚机#2
        Check("role writes wing identity",
            !roleCfg.LeadMode && roleCfg.FormationSlot == 1);
        Check("role arms assist and the link",
            roleCfg.FormationAssist && roleCfg.DataLinkEnabled);
        Check("role applies the echelon preset for that number",
            roleCfg.FormationShape == WtHud.Hud.FormationGeometry.ShapeEchelon
            && Math.Abs(roleCfg.FormationWingOffsetM - 500) < 0.1
            && Math.Abs(roleCfg.FormationStationBackM - 900) < 0.1
            && Math.Abs(roleCfg.FormationUpM - 50) < 0.1);

        // 手改过之后重复点同一个角色：不能把手改重置成标准队形
        roleCfg.FormationWingOffsetM = 640;
        roleCfg.FormationShape = WtHud.Hud.FormationGeometry.ShapeCustom;
        WtHud.UI.Mfd.MfdPages.SetRole(roleCfg, 2);
        Check("re-selecting the same role keeps hand edits",
            Math.Abs(roleCfg.FormationWingOffsetM - 640) < 0.1);

        // 切长机：身份变了，但队形一个数都不许动
        double backBefore = roleCfg.FormationStationBackM;
        WtHud.UI.Mfd.MfdPages.SetRole(roleCfg, 0);
        Check("lead role leaves the formation alone",
            roleCfg.LeadMode && Math.Abs(roleCfg.FormationStationBackM - backBefore) < 0.1
            && Math.Abs(roleCfg.FormationWingOffsetM - 640) < 0.1);
        Check("role index reads back from the two fields",
            WtHud.UI.Mfd.MfdPages.RoleIndex(roleCfg) == 0);

        // ---- 判定余量：门槛 vs 链路一拍 ----
        var marginCfg = new HudConfig
        {
            FormationShape = WtHud.Hud.FormationGeometry.ShapeCustom,
            FormationStationBackM = 400, FormationDonePct = 10,
        };
        string marginOk = WtHud.UI.Mfd.MfdPages.MarginText(marginCfg, 35);
        Check("margin shows the band and the latency",
            marginOk.Contains("40 m") && marginOk.Contains("35 m") && !marginOk.EndsWith("✗"));
        Check("margin flags latency beyond the band",
            WtHud.UI.Mfd.MfdPages.MarginText(marginCfg, 55).EndsWith("✗"));

        // ---- 配置包：往返、清洗、应用范围 ----
        var profCfg = new HudConfig
        {
            LeadMode = false, FormationSlot = 1,
            FormationShape = WtHud.Hud.FormationGeometry.ShapeEchelon,
            FormationWingSide = -1, FormationWingOffsetM = 500, FormationStationBackM = 900,
            FormationUpM = 50, FormationDonePct = 8, FormationDoneHoldS = 2.5, FormationDropPct = 25,
            FormationMapOnly = false, FormationAssist = true, LeadIcon = "Fighter",
        };
        var prof = WtHud.Hud.FormationProfile.From(profCfg);
        string packet = WtHud.Telemetry.ConfigPacket.Build(prof, "48213", "老|阳", "f-4s", 0.31, 0.42, true);
        Check("config packet round-trips",
            WtHud.Telemetry.ConfigPacket.TryParse(packet, out var parsed)
            && parsed.NodeId == "48213" && parsed.Name == "老阳"      // 分隔符被清洗
            && parsed.Type == "f-4s"
            && parsed.Profile.Slot == 1 && parsed.Profile.Side == -1
            && Math.Abs(parsed.Profile.BackM - 900) < 0.001
            && Math.Abs(parsed.Profile.DonePct - 8) < 0.001
            && parsed.Profile.MapOnly == false && parsed.Profile.Assist
            && parsed.Profile.LeadIcon == "Fighter"
            && parsed.HasAnchor && Math.Abs(parsed.AnchorXn - 0.31) < 1e-6 && parsed.AnchorFriendly);
        Check("config packet rejects junk",
            !WtHud.Telemetry.ConfigPacket.TryParse("hello", out _)
            && !WtHud.Telemetry.ConfigPacket.TryParse("CFG0|1|2", out _));

        var applyTo = new HudConfig
        {
            FormationShape = WtHud.Hud.FormationGeometry.ShapeCustom,
            FormationWingOffsetM = 150, FormationStationBackM = 400,
        };
        parsed.Profile.ApplyTo(applyTo);
        Check("config apply writes the numbers",
            Math.Abs(applyTo.FormationWingOffsetM - 500) < 0.001
            && Math.Abs(applyTo.FormationStationBackM - 900) < 0.001
            && applyTo.FormationShape == WtHud.Hud.FormationGeometry.ShapeEchelon
            && applyTo.FormationSlot == 1);
        Check("config apply snapshots the old custom values",
            applyTo.FormationCustomSet && Math.Abs(applyTo.FormationCustomWingOffsetM - 150) < 0.001);
        Check("config diff is empty right after applying",
            WtHud.UI.Mfd.MfdPages.DiffKeys(applyTo, parsed.Profile).Count == 0);

        // 长机发来的配置不能把僚机的编号顶掉
        var leadProf = parsed.Profile with { SenderLead = true, Slot = 0 };
        var wingCfg = new HudConfig
        {
            FormationShape = WtHud.Hud.FormationGeometry.ShapeCustom, FormationSlot = 2,
        };
        leadProf.ApplyTo(wingCfg);
        Check("a lead profile does not overwrite the wing number", wingCfg.FormationSlot == 2);

        // 配置包走独立端口：对端地址只说明机器，端口固定 48018
        var cfgTargets = WtHud.Telemetry.ConfigLink.ResolveTargets(new[] { "192.168.1.7:48017" });
        Check("config targets switch to the config port",
            cfgTargets.Length == 1 && cfgTargets[0].Port == WtHud.Telemetry.ConfigPacket.Port);
        var cfgBroadcast = WtHud.Telemetry.ConfigLink.ResolveTargets(Array.Empty<string>());
        Check("config targets fall back to broadcast",
            cfgBroadcast.Length == 1 && cfgBroadcast[0].Address.Equals(System.Net.IPAddress.Broadcast));

        // ---- 动态页不进缓存（0.4.36）：摘要实时算、内容被冻住，两者会互相矛盾 ----
        var dlModule = WtHud.UI.Mfd.MfdPages.Build().First(p => p.MenuKey == "page.datalink");
        var cfgMenuPage = dlModule.Items.First(i => i.LabelKey == "row.cfgMenu").SubPage!();
        Check("config menu is a normal cached page", !cfgMenuPage.NoCache);
        var inboxPage = cfgMenuPage.Items.First(i => i.LabelKey == "row.cfgInbox").SubPage!();
        Check("config inbox rebuilds on every entry", inboxPage.NoCache);
        var confirmOffer = new WtHud.Telemetry.ConfigOffer
        {
            Address = "192.168.1.7", NodeId = "48213", Name = "test", Type = "F-4S",
            Profile = prof, HasAnchor = true, TeamVerified = true,
        };
        Check("config confirm rebuilds on every entry",
            WtHud.UI.Mfd.MfdPages.ConfigConfirmPage(confirmOffer).NoCache);

        // ---- 角色不再顺手把界面切成极简：两个概念一个开关是 0.4.35 之前的事 ----
        var roleLeadCfg = new HudConfig { LeadMode = true, FormationOnly = false };
        WtHud.UI.Mfd.MfdPages.SetRole(roleLeadCfg, 1);
        Check("role arms the assist but leaves the layout alone",
            roleLeadCfg.FormationAssist && !roleLeadCfg.FormationOnly);

        // ---- 间距也要进差异表：两个预设的四个数值碰巧相同时，只有间距不同 ----
        var spaceCfg = new HudConfig
        {
            FormationShape = WtHud.Hud.FormationGeometry.ShapeEchelon,
            FormationWingOffsetM = 500, FormationStationBackM = 900, FormationUpM = 50,
        };
        WtHud.Hud.FormationGeometry.SetSpacing(spaceCfg, WtHud.Hud.FormationGeometry.ShapeEchelon, 450);
        var spaceProf = WtHud.Hud.FormationProfile.From(spaceCfg);
        Check("same spacing reports no change",
            WtHud.UI.Mfd.MfdPages.DiffKeys(spaceCfg, spaceProf).Count == 0);
        Check("a different spacing is reported",
            WtHud.UI.Mfd.MfdPages.DiffKeys(spaceCfg, spaceProf with { SpacingM = 700 })
                .SequenceEqual(new[] { "row.fSpacing" }));

        // ---- 保存：原子写、失败要能报出来 ----
        string saveDir = Path.Combine(Path.GetTempPath(), "wthud-save-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(saveDir);
        string savePath = Path.Combine(saveDir, "hud-config.json");
        var saveCfg = new HudConfig { FormationStationBackM = 425 };
        var firstSave = saveCfg.SaveTo(savePath);
        Check("config save succeeds and leaves no temp file",
            firstSave.Ok && File.Exists(savePath) && !File.Exists(savePath + ".tmp"));
        saveCfg.FormationStationBackM = 640;
        var secondSave = saveCfg.SaveTo(savePath);
        Check("a second save replaces the file",
            secondSave.Ok && File.ReadAllText(savePath).Contains("640")
            && !File.Exists(savePath + ".tmp"));
        var badSave = saveCfg.SaveTo(Path.Combine(saveDir, "no-such-dir", "hud-config.json"));
        Check("a failed save reports instead of throwing",
            !badSave.Ok && badSave.Error != HudConfig.SaveError.None);

        // ---- 数据链开关：运行中改不了，值列必须说出来 ----
        Check("a matching link switch reads plain",
            WtHud.Program.DataLinkStateText(new HudConfig { DataLinkEnabled = false })
            == WtHud.UI.L10n.Get(WtHud.UI.LangScope.Screen, "off"));
        Check("a pending link switch says restart is needed",
            WtHud.Program.DataLinkStateText(new HudConfig { DataLinkEnabled = true })
                .Contains(WtHud.UI.L10n.Get(WtHud.UI.LangScope.Screen, "val.needRestart")));
        try { Directory.Delete(saveDir, true); } catch { }

        // ---- 长机僚机表：按槽位排序、含「数据错误」行、状态词带数字 ----
        var wt1 = new WtHud.Telemetry.DataLink.LinkNode(11, 3000, 0, 0, 0, null, null, 0,
            0, WtHud.Telemetry.DataLink.LinkRole.Wing, 5,
            WtHud.Telemetry.DataLink.WingState.Behind, 2, 480);
        var wt2 = new WtHud.Telemetry.DataLink.LinkNode(22, 3000, 0, 0, 0, null, null, 0,
            0, WtHud.Telemetry.DataLink.LinkRole.Wing, 5,
            WtHud.Telemetry.DataLink.WingState.InPosition, 0, 0);
        var wt3 = new WtHud.Telemetry.DataLink.LinkNode(33, 3000, 0, 0, 0, null, null, 0,
            0, WtHud.Telemetry.DataLink.LinkRole.Lead, 5,
            WtHud.Telemetry.DataLink.WingState.Unknown, -1, 0);
        // 在编队里、但没有坐标/判定：这一行必须还在，状态写「数据错误」
        var wt4 = new WtHud.Telemetry.DataLink.LinkNode(44, 3000, 0, 0, 0, null, null, 0,
            0, WtHud.Telemetry.DataLink.LinkRole.Wing, 5,
            WtHud.Telemetry.DataLink.WingState.Unknown, 3, 0);
        var tableRows = WingTable(new[] { wt1, wt2, wt3 });
        Check("wing table keeps only reporting wings", tableRows.Count == 2);
        Check("wing table sorts by slot", tableRows[0].Id == 22 && tableRows[1].Id == 11);
        Check("wing table tolerates null", WingTable(null).Count == 0);
        Check("data-error wing stays in the table",
            WingTable(new[] { wt1, wt2, wt3, wt4 }).Count == 3);
        Check("state text says behind with metres", WingStateText(wt1).Contains("480"));
        Check("state text says on station", WingStateText(wt2).Length > 0
            && WingStateText(wt2) != "guide.wing.ok");
        Check("unknown state is spelled out as a data error",
            WingStateText(wt3).Length > 0
            && WingStateText(wt3) == WtHud.UI.L10n.Get(WtHud.UI.LangScope.Alerts, "guide.wing.dataErr"));

        // ---- 主状态判定：数据错误/掉线不参与完整性，但没人判定时要说真话 ----
        Check("data-error wing does not break completeness",
            WingVerdict(new[] { wt2, wt4 }) == LeadWingVerdict.Complete);
        Check("all data-error is reported as data error",
            WingVerdict(new[] { wt4 }) == LeadWingVerdict.DataError);
        Check("a lagging wing still breaks completeness",
            WingVerdict(new[] { wt1, wt2 }) == LeadWingVerdict.Incomplete);
        Check("empty table cannot claim completeness",
            WingVerdict(null) == LeadWingVerdict.DataError);

        // 掉线：3 s 收不到包的那架要留在表里，状态从「到位」变成「掉线 12s」
        var wtOff = new WtHud.Telemetry.DataLink.LinkNode(55, 3000, 0, 0, 0, null, null, 12_000,
            0, WtHud.Telemetry.DataLink.LinkRole.Wing, 9,
            WtHud.Telemetry.DataLink.WingState.InPosition, 1, 0);
        Check("offline wing keeps its slot", WingTable(new[] { wt2, wtOff }).Count == 2);
        Check("offline state wins over the last report", WingStateText(wtOff).Contains("12"));
        Check("offline does not break completeness",
            WingVerdict(new[] { wt2, wtOff }) == LeadWingVerdict.Complete);
        Check("nobody judged and one offline says offline",
            WingVerdict(new[] { wtOff }) == LeadWingVerdict.Offline);
        Check("offline outranks data error when nobody judged",
            WingVerdict(new[] { wtOff, wt4 }) == LeadWingVerdict.Offline);

        // ---- 两家长机：只列跟我的那架（0 = 没指定，当"可能是我的人"留下）----
        var wtMine = wt4 with { LeadId = 555 };
        var wtOther = wt4 with { LeadId = 999 };
        Check("wing that follows another lead is filtered out", !IsMyWing(wtOther, 555));
        Check("wing that names me is kept", IsMyWing(wtMine, 555));
        Check("wing with no lead id is kept", IsMyWing(wt4, 555));
        Check("wing table drops the other formation",
            WingTable(new[] { wtMine, wtOther }, 555).Count == 1);
        Check("wing table keeps both when my id is unknown",
            WingTable(new[] { wtMine, wtOther }).Count == 2);

        // ---- 表不设行数上限：截断等于静默少报（四人以上 / 带数据错误行时正好踩到）----
        var leadOpt = new HudOptions { LeadStatusMain = "X", FormationOnly = true };
        for (int i = 0; i < 6; i++) leadOpt.LeadWingRows.Add(("W" + (i + 1), "到位", false));
        var leadPrims = new HudGeometry(leadOpt, new HudCalibration { VerticalFovDeg = 45 })
            .Build(new TelemetrySnapshot { Valid = true, InFlight = true }, default, 1920, 1080);
        int drawnLabels = leadPrims.Count(p => p.Text is "W1" or "W2" or "W3" or "W4" or "W5" or "W6");
        int drawnStates = leadPrims.Count(p => p.Text == "到位");
        Check("wing table draws every row", drawnLabels == 6 && drawnStates == 6);

        // ---- 站位偏差：站在槽位上指向角为 0，横向偏出去必须明显大于容差 ----
        Check("aim zero when parked in the slot", Math.Abs(WingAimDeg(180, 400, 0, 0, 400)) < 1.0);
        Check("aim grows when offset sideways", Math.Abs(WingAimDeg(180, 400, 0, 50, 400)) > 5.0);

        // ---- 远距例外：爬升阶段的距离行只看「够不够远」，门槛 0 = 关掉 ----
        Check("far range hidden just under gate", !RevealRangeWhileClimbing(1999.9, 2000));
        Check("far range shown at gate", RevealRangeWhileClimbing(2000, 2000));
        Check("far range shown beyond gate", RevealRangeWhileClimbing(5200, 2000));
        // ★ 0 = 任何距离都显示（距离恒 ≥ 0，门槛等于没有）；负数才是关闭
        Check("zero gate shows the range at any distance",
            RevealRangeWhileClimbing(1, 0) && RevealRangeWhileClimbing(99999, 0));
        Check("negative gate hides the range", !RevealRangeWhileClimbing(99999, -1));
        // 「只显示爬升率」默认关：爬升阶段照常给全套横向引导
        Check("climb-rate-only is off by default", !new HudConfig().FormationVerticalFirst);
        Check("climb-rate-only needs the switch",
            !VerticalFirstActive(new HudConfig(), true)
            && VerticalFirstActive(new HudConfig { FormationVerticalFirst = true }, true)
            && !VerticalFirstActive(new HudConfig { FormationVerticalFirst = true }, false));

        Check("turn assist off stays off",
            AdvanceTurnAssist(st, 9.0, 1.0).Stage == TurnAssistStage.Off);
        st = new TurnAssistState(TurnAssistStage.Arming, 0, -1);
        Check("arming needs a real turn", AdvanceTurnAssist(st, 0.8, 1.0).Stage == TurnAssistStage.Arming);
        var locked = AdvanceTurnAssist(st, 2.0, 1.0);
        Check("arming latches direction", locked.Stage == TurnAssistStage.Active && locked.Dir == 1);
        // 反向 0.5 s 不重锁，1.1 s 才重锁
        var rev1 = AdvanceTurnAssist(locked, -3.0, 10.0);
        Check("reversal records start", rev1.Dir == 1 && Math.Abs(rev1.ReverseSince - 10.0) < 1e-9);
        var rev2 = AdvanceTurnAssist(rev1, -3.0, 10.5);
        Check("reversal under 1s keeps direction", rev2.Dir == 1);
        var rev3 = AdvanceTurnAssist(rev2, -3.0, 11.1);
        Check("reversal over 1s relatches", rev3.Dir == -1 && rev3.ReverseSince < 0);
        // 反向中断就清零，不累计
        var cancel = AdvanceTurnAssist(rev1, 0.2, 10.4);
        Check("reversal cancelled resets timer", cancel.ReverseSince < 0 && cancel.Dir == 1);

        Check("inside turn right/right", IsInsideTurn(+1, +10));
        Check("outside turn right/left", !IsInsideTurn(-1, +10));
        Check("inside turn left/left", IsInsideTurn(-1, -10));
        Check("outside turn left/right", !IsInsideTurn(+1, -10));
        Check("C unset side keeps legacy lead",
            Math.Abs(FormationLeadAheadDeg(10, 400, 600, 45, 0) - 24.77) < 0.1);
        Check("C legacy lead mirrors turn direction",
            Math.Abs(FormationLeadAheadDeg(-10, 400, 600, 45, 0)
                     + FormationLeadAheadDeg(10, 400, 600, 45, 0)) < 0.1);
        Check("C station drops lead inner", FormationLeadAheadDeg(10, 400, 600, 45, +1) == 0);
        Check("C station drops lead outer", FormationLeadAheadDeg(10, 400, 600, 45, -1) == 0);
        Check("C station drops lead both turns", FormationLeadAheadDeg(-10, 400, 600, 45, +1) == 0);

        // ---- 僚机速度前馈：|ω|·(r_w−ρ)，外圈加速 / 内圈减速 / 上下限 ----
        Check("feed straight is zero", FormationFeedKmh(0, 400, 600, +1, -30, 0, 0.8, 1.3) == 0);
        Check("feed center wing is zero", FormationFeedKmh(10, 400, 600, 0, -30, 0, 0.8, 1.3) == 0);
        Check("feed outer wing accelerates", FormationFeedKmh(10, 400, 600, -1, 30, 0, 0.8, 1.3) > 0);
        Check("feed inner wing decelerates", FormationFeedKmh(10, 400, 600, +1, -30, 0, 0.8, 1.3) < 0);
        Check("feed mirrors turn direction",
            Math.Abs(FormationFeedKmh(-10, 400, 600, +1, -30, 0, 0.8, 1.3)
                     - FormationFeedKmh(10, 400, 600, -1, 30, 0, 0.8, 1.3)) < 1e-9);
        Check("feed clamped by max ratio",
            FormationFeedKmh(20, 1500, 600, -1, 30, 0, 0.8, 1.3) <= 600 * 0.3 + 1e-9);

        // ---- 数据链报文：新格式回环 + 老格式兼容 ----
        string wire = WtHud.Telemetry.DataLink.BuildPayload(123456, 1000, -2.5, 359.6, -12.34);
        Check("datalink payload shape", wire == "WTDL2|123456|1000|-25|0|-123");
        Check("datalink roundtrip",
            WtHud.Telemetry.DataLink.TryParsePayload(wire, out var dlId, out var dlAlt, out var dlVy,
                                                     out var dlTurn, out var dlHdg)
            && dlId == 123456 && dlAlt == 1000
            && Math.Abs(dlVy - (-2.5)) < 0.01
            && dlTurn is double t && Math.Abs(t - (-12.3)) < 0.01
            && dlHdg is double h && Math.Abs(h - 0) < 0.01);
        // 老版本只发 4 段：高度照收，航向/转向率都给 null（上层退化为本地近似）
        Check("datalink legacy 4 fields",
            WtHud.Telemetry.DataLink.TryParsePayload("WTDL2|999999|5000|-10",
                out _, out var oldAlt, out _, out var oldTurn, out var oldHdg)
            && oldAlt == 5000 && oldTurn == null && oldHdg == null);
        Check("datalink heading parsed",
            WtHud.Telemetry.DataLink.TryParsePayload("WTDL2|1|1000|0|275|-123",
                out _, out _, out _, out _, out var hdg275)
            && hdg275 is double hp && Math.Abs(hp - 275) < 0.01);
        Check("datalink rejects wrong magic",
            !WtHud.Telemetry.DataLink.TryParsePayload("WTDL3|1|2|3", out _, out _, out _, out _, out _));
        Check("datalink rejects garbage",
            !WtHud.Telemetry.DataLink.TryParsePayload("WTDL2|abc|1|2", out _, out _, out _, out _, out _));

        // ---- 坐标段（2026-11-12 追加）：唯一用途是把数据链节点和 map_obj 里的友机认成同一架 ----
        string wirePos = WtHud.Telemetry.DataLink.BuildPayload(123456, 1000, -2.5, 359.6, -12.34, 0.48213, 0.51234);
        Check("datalink payload carries position", wirePos == "WTDL2|123456|1000|-25|0|-123|48213|51234");
        Check("datalink position roundtrip",
            WtHud.Telemetry.DataLink.TryParsePayload(wirePos, out _, out _, out _, out _, out _,
                                                     out var dlX, out var dlY)
            && dlX is double px2 && Math.Abs(px2 - 0.48213) < 1e-6
            && dlY is double py2 && Math.Abs(py2 - 0.51234) < 1e-6);
        // 不带坐标时仍是 6 段：老读端只取前 6 段，所以这次扩展不需要两边同时升级
        Check("datalink without position stays 6 fields",
            WtHud.Telemetry.DataLink.BuildPayload(1, 100, 0, 0, 0, null, null).Split('|').Length == 6);
        // 坐标段坏了只丢坐标，高度照收
        Check("datalink bad position keeps altitude",
            WtHud.Telemetry.DataLink.TryParsePayload("WTDL2|7|1234|0|90|0|abc|5",
                out _, out var badPosAlt, out _, out _, out _, out var badX, out _)
            && badPosAlt == 1234 && badX == null);
        // 越界坐标当无效：一个手滑的 999 不该把配对带到地图外面
        Check("datalink out-of-range position dropped",
            WtHud.Telemetry.DataLink.TryParsePayload("WTDL2|7|1234|0|90|0|999999|5",
                out _, out _, out _, out _, out _, out var oorX, out _)
            && oorX == null);

        // ---- 单播目标：跨网段/内网穿透必须填，广播出不了路由器 ----
        Check("peer ip:port",
            WtHud.Telemetry.DataLink.TryParsePeer("192.168.16.7:48017", out var epA)
            && epA.Address.ToString() == "192.168.16.7" && epA.Port == 48017);
        Check("peer defaults to datalink port",
            WtHud.Telemetry.DataLink.TryParsePeer("10.0.0.5", out var epB)
            && epB.Port == WtHud.Telemetry.DataLink.Port);
        Check("peer rejects hostname", !WtHud.Telemetry.DataLink.TryParsePeer("example.com:48017", out _));
        Check("peer rejects bad port", !WtHud.Telemetry.DataLink.TryParsePeer("10.0.0.5:70000", out _));

        // ---- IPv6 目标：方括号带端口、裸地址、坏输入 ----
        Check("peer bracketed ipv6 with port",
            WtHud.Telemetry.DataLink.TryParsePeer("[2001:db8::1]:48017", out var ep6)
            && ep6.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6
            && ep6.Port == 48017 && ep6.Address.ToString() == "2001:db8::1");
        Check("peer bare ipv6 keeps default port",
            WtHud.Telemetry.DataLink.TryParsePeer("::1", out var ep6b)
            && ep6b.Address.ToString() == "::1" && ep6b.Port == WtHud.Telemetry.DataLink.Port);
        Check("peer bare ipv6 with many colons",
            WtHud.Telemetry.DataLink.TryParsePeer("fd00:1234::55", out var ep6c)
            && ep6c.Port == WtHud.Telemetry.DataLink.Port);
        Check("peer rejects unclosed bracket",
            !WtHud.Telemetry.DataLink.TryParsePeer("[::1:48017", out _));
        Check("peer rejects bracket without colon",
            !WtHud.Telemetry.DataLink.TryParsePeer("[::1]48017", out _));

        // ---- 按键名解析：OEM 符号键（` ~ [ ] 等）以前落进默认分支，静默退回默认键 ----
        Check("vk tilde by symbol", WtHud.Input.KeyBinding.NameToVk("~") == 0xC0);
        Check("vk tilde by wpf name", WtHud.Input.KeyBinding.NameToVk("OEM3") == 0xC0);
        Check("vk tilde by winforms name", WtHud.Input.KeyBinding.NameToVk("OEMTILDE") == 0xC0);
        Check("vk backtick alias", WtHud.Input.KeyBinding.NameToVk("`") == 0xC0);
        Check("vk oem symbols",
            WtHud.Input.KeyBinding.NameToVk("[") == 0xDB
            && WtHud.Input.KeyBinding.NameToVk("]") == 0xDD
            && WtHud.Input.KeyBinding.NameToVk("/") == 0xBF
            && WtHud.Input.KeyBinding.NameToVk(";") == 0xBA
            && WtHud.Input.KeyBinding.NameToVk("'") == 0xDE
            && WtHud.Input.KeyBinding.NameToVk(",") == 0xBC
            && WtHud.Input.KeyBinding.NameToVk(".") == 0xBE
            && WtHud.Input.KeyBinding.NameToVk("-") == 0xBD
            && WtHud.Input.KeyBinding.NameToVk("=") == 0xBB
            && WtHud.Input.KeyBinding.NameToVk("\\") == 0xDC);
        var kbTilde = WtHud.Input.KeyBinding.Parse("~", 0x70, "F1");
        Check("parse tilde keeps vk",
            kbTilde.VirtualKey == 0xC0 && kbTilde.Text == "~" && !kbTilde.Shift);
        var kbShiftTilde = WtHud.Input.KeyBinding.Parse("SHIFT+~", 0x70, "F1");
        Check("parse shift+tilde", kbShiftTilde.VirtualKey == 0xC0 && kbShiftTilde.Shift);
        Check("old saved oem name still works",
            WtHud.Input.KeyBinding.Parse("OEMTILDE", 0x70, "F1").VirtualKey == 0xC0);
        Check("unknown key falls back",
            WtHud.Input.KeyBinding.Parse("NOPE", 0x70, "F1").VirtualKey == 0x70);

        // ---- 面板那一行文本 ↔ 配置里的列表（F1 数据链模块里直接改地址） ----
        string peerText = HudConfig.PeersToText(new[] { "172.16.5.23:48017", "10.0.0.5" });
        Check("peers to text", peerText == "172.16.5.23:48017, 10.0.0.5");
        Check("peers text roundtrip",
            HudConfig.TextToPeers(peerText) is var backPeers && backPeers.Count == 2
            && backPeers[0] == "172.16.5.23:48017" && backPeers[1] == "10.0.0.5");
        Check("peers text accepts comma semicolon space",
            HudConfig.TextToPeers("1.1.1.1:48017; 2.2.2.2  3.3.3.3").Count == 3);
        Check("peers text drops duplicates",
            HudConfig.TextToPeers("1.1.1.1, 1.1.1.1 ,  2.2.2.2").Count == 2);
        Check("empty peer text means broadcast", HudConfig.TextToPeers("   ").Count == 0);

        // 面板上「发送目标」那行文字：三种状态各一条，免得只有真机才看得出对不对
        Check("peer note broadcast", PeerNote(0, 0) == "broadcast");
        Check("peer note single target", PeerNote(1, 0) == "1 target(s)");
        Check("peer note mixed", PeerNote(2, 1) == "1 ok / 1 bad");

        // ---- 面板草稿必须是深拷贝 ----
        // _work = _cfg.Clone() 之后两者若共用集合，SettingsRouter.Set 的就地写会绕过 SAVE，
        // 表现为「没点保存也生效了」，而 CANCEL 只能靠快照兜。
        var cloneSrc = new HudConfig();
        cloneSrc.PerVehicleOverrides["f16"] = new Dictionary<string, double> { ["AoaWarnDeg"] = 18 };
        cloneSrc.DataLinkPeers.Add("1.1.1.1:48017");
        var cloneCopy = cloneSrc.Clone();
        cloneCopy.PerVehicleOverrides["f16"]["AoaWarnDeg"] = 99;
        cloneCopy.DataLinkPeers.Add("2.2.2.2:48017");
        Check("clone deep-copies overrides", cloneSrc.PerVehicleOverrides["f16"]["AoaWarnDeg"] == 18);
        Check("clone deep-copies peers", cloneSrc.DataLinkPeers.Count == 1);

        // 兜底：任何引用类型属性都不能与源共用实例 —— 将来加集合忘了处理，这条会先红
        var refProps = typeof(HudConfig).GetProperties()
            .Where(p => p.CanRead && p.PropertyType != typeof(string) && !p.PropertyType.IsValueType)
            .ToArray();
        Check("clone shares no reference property",
            refProps.Length > 0
            && refProps.All(p => !ReferenceEquals(p.GetValue(cloneSrc), p.GetValue(cloneCopy))));

        // ---- 双击判定：区域 + 索引一起比，跨区域不算连击 ----
        var hitMenu3 = ("menu", 3);
        Check("repeat ignores other zone",
            !WtHud.UI.Mfd.MfdFrameForm.IsRepeat(hitMenu3, "confirm", 3, 100, 400));
        Check("repeat needs same index",
            !WtHud.UI.Mfd.MfdFrameForm.IsRepeat(hitMenu3, "menu", 4, 100, 400));
        Check("repeat inside window",
            WtHud.UI.Mfd.MfdFrameForm.IsRepeat(hitMenu3, "menu", 3, 100, 400));
        Check("repeat outside window",
            !WtHud.UI.Mfd.MfdFrameForm.IsRepeat(hitMenu3, "menu", 3, 500, 400));

        // ---- 侧键槽位名：只认 SIDE0..SIDE9（以前会抛异常） ----
        Check("side slot parses SIDE0",
            WtHud.UI.Mfd.Controls.MfdInputRouter.TryParseSideSlot("SIDE0", out var slot0) && slot0 == 0);
        Check("side slot parses SIDE9",
            WtHud.UI.Mfd.Controls.MfdInputRouter.TryParseSideSlot("SIDE9", out var slot9) && slot9 == 9);
        Check("side slot rejects SIDE", !WtHud.UI.Mfd.Controls.MfdInputRouter.TryParseSideSlot("SIDE", out _));
        Check("side slot rejects SIDE10", !WtHud.UI.Mfd.Controls.MfdInputRouter.TryParseSideSlot("SIDE10", out _));
        Check("side slot rejects SIDEX", !WtHud.UI.Mfd.Controls.MfdInputRouter.TryParseSideSlot("SIDEX", out _));
        Check("side slot rejects null", !WtHud.UI.Mfd.Controls.MfdInputRouter.TryParseSideSlot(null, out _));

        // ---- 行级联动：EnabledWhen 每次求值，不可用时激活路径全部无效 ----
        var gateCfg = new HudConfig { DataLinkEnabled = false };
        var gatedRow = new WtHud.UI.Mfd.MfdItem
        {
            LabelKey = "gated",
            Kind = WtHud.UI.Mfd.ItemKind.Toggle,
            GetBool = c => c.DataLinkEnabled,
            SetBool = (c, v) => c.DataLinkEnabled = v,
            EnabledWhen = c => c.DataLinkEnabled,
        };
        Check("gated row off while feature off", !gatedRow.IsEnabled(gateCfg));
        gateCfg.DataLinkEnabled = true;
        Check("gated row live once feature on", gatedRow.IsEnabled(gateCfg));

        var blockedRow = new WtHud.UI.Mfd.MfdItem
        {
            LabelKey = "blocked",
            Kind = WtHud.UI.Mfd.ItemKind.Toggle,
            GetBool = c => c.DataLinkEnabled,
            SetBool = (c, v) => c.DataLinkEnabled = v,
            EnabledWhen = _ => false,
        };
        blockedRow.Activate(gateCfg);
        Check("disabled row ignores activate", gateCfg.DataLinkEnabled);
        Check("row without gate stays enabled",
            new WtHud.UI.Mfd.MfdItem { LabelKey = "plain" }.IsEnabled(gateCfg));

        // ---- 节点 ↔ 地图友机配对：给每架友机各自的高度 ----
        var twoNodes = new[]
        {
            new WtHud.Telemetry.DataLink.LinkNode(111111, 3000, 0, 90, 0, 0.5000, 0.5000, 0),
            new WtHud.Telemetry.DataLink.LinkNode(222222, 5000, 0, 90, 0, 0.5010, 0.5010, 0),
        };
        var matchedAlt = WtHud.Telemetry.DataLink.MatchAltitudes(
            new (double X, double Y)[] { (0.50005, 0.50005), (0.50105, 0.50105) }, twoNodes, 8e-4);
        Check("pair gives each friendly its own altitude", matchedAlt[0] == 3000 && matchedAlt[1] == 5000);
        Check("pair leaves unmatched friendly null",
            WtHud.Telemetry.DataLink.MatchAltitudes(
                new (double X, double Y)[] { (0.9, 0.9) }, twoNodes, 8e-4)[0] == null);
        // 一个节点只能配一次：两架挨得近的友机不能共用同一个高度
        Check("pair never reuses a node",
            WtHud.Telemetry.DataLink.MatchAltitudes(
                new (double X, double Y)[] { (0.5000, 0.5000), (0.50001, 0.50001) },
                new[] { twoNodes[0] }, 8e-4) is var oneNode && oneNode[0] == 3000 && oneNode[1] == null);

        // ---- 多机：引导要锁「认出来的长机」，不是「最近的那架」 ----
        // 场景：三机编队。友机列表按距离排过序，僚机 2 号比长机近 —— 旧逻辑会锁错人。
        var pairIdx = WtHud.Telemetry.DataLink.MatchNodeIndex(
            new (double X, double Y)[] { (0.50005, 0.50005), (0.50105, 0.50105) }, twoNodes, 8e-4);
        Check("pair returns node indexes", pairIdx[0] == 0 && pairIdx[1] == 1);
        Check("lead friendly found by pair",
            LeadFriendlyIndex(pairIdx, 1, 9) == 1);      // 长机是节点 1 → 友机 1
        Check("lead friendly falls back when unmatched",
            LeadFriendlyIndex(pairIdx, 7, 0) == 0);      // 长机不在配对表里 → 退回最近
        Check("lead friendly falls back with no lead",
            LeadFriendlyIndex(pairIdx, -1, 0) == 0);
        Check("lead friendly survives empty pair",
            LeadFriendlyIndex(Array.Empty<int?>(), 0, 2) == 2);

        // ---- 8111 健康度：四种组合必须分得开，否则「请重启游戏」会误报 ----
        Check("api ok when everything present",
            ClassifyApiHealth(true, true, true, true) == ApiHealth.Ok);
        Check("api not-in-battle when state dead",
            ClassifyApiHealth(false, false, false, false) == ApiHealth.NotInBattle);
        // 机库里 state 是 valid、indicators 与 map 都空 —— 这是正常状态，不能喊重启
        Check("api hangar is not a fault",
            ClassifyApiHealth(true, false, false, false) == ApiHealth.NotInBattle);
        Check("api half-ready when flying with both empty",
            ClassifyApiHealth(true, true, false, false) == ApiHealth.HalfReady);
        Check("api airframe without avionics is not half-ready",
            ClassifyApiHealth(true, true, false, true) == ApiHealth.NoIndicatorsOnly);
        Check("api missing map only",
            ClassifyApiHealth(true, true, true, false) == ApiHealth.NoMapOnly);
        Check("api text mentions restart",
            ApiHealthText(ApiHealth.HalfReady).Contains("restart"));
        Check("api zh is readable",
            ApiHealthZh(ApiHealth.HalfReady).Contains("重启"));

        // ---- 回包：手填目标 + 来包来源合并（三人编队里长机只填一个地址也不会漏人） ----
        var rtA = new System.Net.IPEndPoint(System.Net.IPAddress.Parse("26.1.1.1"), 48017);
        var rtB = new System.Net.IPEndPoint(System.Net.IPAddress.Parse("26.1.1.2"), 48017);
        var rtC = new System.Net.IPEndPoint(System.Net.IPAddress.Parse("26.1.1.3"), 48017);
        var rtNone = new List<System.Net.IPEndPoint?>();
        var rtStale = new List<System.Net.IPEndPoint?> { null, null };
        Check("reply adds unknown source",
            WtHud.Telemetry.DataLink.MergeTargets(new[] { rtA }, new List<System.Net.IPEndPoint?> { rtB }).Length == 2);
        Check("reply dedupes a known peer",
            WtHud.Telemetry.DataLink.MergeTargets(new[] { rtA }, new List<System.Net.IPEndPoint?> { rtA }).Length == 1);
        Check("reply keeps configured order and appends sources",
            WtHud.Telemetry.DataLink.MergeTargets(new[] { rtA, rtB }, new List<System.Net.IPEndPoint?> { rtC }).Length == 3);
        Check("reply skips stale (null) sources",
            WtHud.Telemetry.DataLink.MergeTargets(new[] { rtA }, rtStale).Length == 1);
        var rtFallback = WtHud.Telemetry.DataLink.MergeTargets(Array.Empty<System.Net.IPEndPoint>(), rtNone);
        Check("reply falls back to broadcast with nothing at all",
            rtFallback.Length == 1 && rtFallback[0].Address.Equals(System.Net.IPAddress.Broadcast));

        // ---- 长机专用项在长机模式 / 数据链生效时必须自动让位 ----
        Check("lead id forced to auto under lead mode", EffectiveLeadId(true, 872768) == 0);
        Check("lead id honoured for a wingman", EffectiveLeadId(false, 872768) == 872768);
        Check("lead icon dropped once the link is up", EffectiveLeadIcon(true, "Fighter").Length == 0);
        Check("lead icon kept without a link", EffectiveLeadIcon(false, "Fighter") == "Fighter");
        Check("lead icon null safe", EffectiveLeadIcon(false, null).Length == 0);

        // ---- 长机航向来源：有链路用自报值，没有用本机航向近似 ----
        Check("lead heading prefers link", ResolveLeadHeading(275, 10) == 275);
        Check("lead heading falls back to own", ResolveLeadHeading(null, 10) == 10);

        // ---- 转向率估计器：窗口首尾差 ----
        // 每 250 ms 涨 5° = 20°/s；窗口 1.0 s，所以稳态应读出 20
        ownHeadingHistory.Clear();
        double rateSeen = 0;
        for (int i = 0; i <= 5; i++) rateSeen = UpdateOwnTurnRate(true, i * 5.0, i * 0.25);
        Check("own turn rate 20degps", Math.Abs(rateSeen - 20) < 0.5);
        Check("own turn rate needs heading", UpdateOwnTurnRate(false, 0, 2.0) == 0);
        // 切图/重生：航向从 10 跳到 200，历史作废，这一帧不能算出 760°/s 那种鬼值
        ownHeadingHistory.Clear();
        UpdateOwnTurnRate(true, 10, 0);
        UpdateOwnTurnRate(true, 10, 0.25);
        Check("own turn rate ignores compass jump", Math.Abs(UpdateOwnTurnRate(true, 200, 0.5)) < 1e-9);

        // 无数据链时的反推：长机 10°/s、我不转 → 相对方位每秒涨 10° → 估出 10°/s
        leadRelHistory.Clear();
        double estRate = 0;
        for (int i = 0; i <= 4; i++) estRate = EstimateLeadTurnRate(i * 2.5, 0, i * 0.25);
        Check("lead turn rate from bearing", Math.Abs(estRate - 10) < 0.5);
        // 我也在同样右转 10°/s 时相对方位不变，靠 ownTurnRate 补回来，结果仍是 10
        leadRelHistory.Clear();
        for (int i = 0; i <= 4; i++) estRate = EstimateLeadTurnRate(0, 10, i * 0.25);
        Check("lead turn rate adds own rate", Math.Abs(estRate - 10) < 0.5);

        // ---- 超载提示：n = V·ω/g，600 km/h 下 10°/s ≈ 2.97 G（阈值默认 2.5 G）----
        Check("turn load 10degps at 600kmh is ~3G", Math.Abs(TurnLoadG(10, 600) - 2.97) < 0.05);
        Check("turn load scales with speed",
            Math.Abs(TurnLoadG(10, 400) - TurnLoadG(10, 600) * 400.0 / 600.0) < 0.01);
        Check("turn load zero when straight", TurnLoadG(0, 600) == 0);
        Check("turn load ignores turn direction",
            Math.Abs(TurnLoadG(-10, 600) - TurnLoadG(10, 600)) < 1e-9);
        // 阈值 1.5 G ≈ 5.1°/s @ 600 km/h：5.2°/s 越线，5°/s 还没到
        Check("turn load 5.2degps exceeds 1.5G warn", TurnLoadG(5.2, 600) > 1.5);
        Check("turn load 5degps below 1.5G warn", TurnLoadG(5, 600) < 1.5);
        // 1 G 对应的转弯率（600 km/h 下 3.37°/s），sim 用它做「标准转弯」基准
        Check("1G at 600kmh is ~3.37degps", Math.Abs(TurnLoadG(3.37, 600) - 1.0) < 0.02);

        // 速度条：轴心是目标速度，块偏在哪边 = 差在哪边
        Check("speed bar centred when on target",
            FormationBarScale.SpeedFrac(600, 600) == 0);
        Check("speed bar 100 slow is 5/6 left",
            Math.Abs(FormationBarScale.SpeedFrac(600, 700) + 100.0 / 120.0) < 1e-9);
        Check("speed bar right when fast",
            FormationBarScale.SpeedFrac(760, 700) > 0);
        Check("speed bar clamps past full scale",
            FormationBarScale.SpeedFrac(1200, 700) == 1.0
            && FormationBarScale.SpeedFrac(100, 700) == -1.0);
        // 加速度条：中点 = 不用动油门
        Check("accel bar centred at zero", FormationBarScale.AccelFrac(0) == 0);
        Check("accel bar half-right at half scale",
            Math.Abs(FormationBarScale.AccelFrac(60) - 0.5) < 1e-9);
        Check("accel bar clamps both ways",
            FormationBarScale.AccelFrac(999) == 1.0 && FormationBarScale.AccelFrac(-999) == -1.0);
        // 两条条共用同一个满量程（120 km/h）：这样"油门打满"和"速度条走到头"是同一个位置，
        // 飞行员不用记两套刻度
        Check("both bars share one full scale",
            FormationBarScale.SpeedHalfKmh == FormationBarScale.AccelHalfKmh);
        Check("speed bar hits the end at accel full scale",
            Math.Abs(FormationBarScale.SpeedFrac(600, 720) + 1.0) < 1e-9);

        // 长机挑选：同型多架时取最近的，而不是列表里第一个（列表顺序是分析顺序，不可依赖）
        var cand = new System.Collections.Generic.List<FriendlyContact>
        {
            new(0, 2400, "Fighter"), new(0, 900, "Fighter"), new(0, 1500, "Fighter"),
        };
        Check("nearest matching contact is the lead", NearestFriendlyIndex(cand) == 1);
        Check("nearest ignores bearing", NearestFriendlyIndex(
            new System.Collections.Generic.List<FriendlyContact>
            {
                new(-170, 500, "Fighter"), new(10, 700, "Fighter"),
            }) == 0);
        Check("no friendly means no lead",
            NearestFriendlyIndex(new System.Collections.Generic.List<FriendlyContact>()) == -1);

        Console.WriteLine("selftest done");
        return Environment.ExitCode;
    }
}
