// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 A-common-Chinese
// This file is part of War Thunder HUD (8111), licensed under the GNU AGPL v3.0 or later.

using System.Globalization;

namespace WtHud.UI;

/// <summary>Which surface a string belongs to. Each surface has its own language.</summary>
public enum LangScope
{
    Panel,      // 机框丝印：顶排/底排按键名
    Screen,     // MFD 屏幕内容：菜单、行标签、提示
    Alerts,     // HUD 告警文字
}

/// <summary>Two supported languages. Kept tiny on purpose - only CN/EN.</summary>
public enum Lang
{
    Zh,
    En,
}

/// <summary>
/// Per-surface language state plus the lookup used by the HUD and the MFD panel.
/// Panel rendering is done at runtime, so switching a language repaints immediately.
/// </summary>
public static class L10n
{
    public static Lang Panel { get; set; } = Lang.En;    // 默认：外板英文
    public static Lang Screen { get; set; } = Lang.Zh;   // 默认：屏幕中文
    public static Lang Alerts { get; set; } = Lang.En;   // 默认：告警英文

    public static bool FollowSystem { get; set; }

    public static Lang System => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName
        .Equals("zh", StringComparison.OrdinalIgnoreCase) ? Lang.Zh : Lang.En;

    public static void ApplyFollowSystem()
    {
        if (!FollowSystem) return;
        var lang = System;
        Panel = Screen = Alerts = lang;
    }

    public static Lang Get(LangScope scope) => scope switch
    {
        LangScope.Panel => Panel,
        LangScope.Screen => Screen,
        _ => Alerts,
    };

    public static void Set(LangScope scope, Lang lang)
    {
        switch (scope)
        {
            case LangScope.Panel: Panel = lang; break;
            case LangScope.Screen: Screen = lang; break;
            default: Alerts = lang; break;
        }
    }

    public static Lang Toggle(Lang lang) => lang == Lang.Zh ? Lang.En : Lang.Zh;

    /// <summary>Load the persisted selection into the live language state.</summary>
    public static void LoadFromConfig(Hud.LanguageConfig cfg)
    {
        FollowSystem = cfg.FollowSystem;
        Panel = Parse(cfg.Panel, Lang.En);
        Screen = Parse(cfg.Screen, Lang.Zh);
        Alerts = Parse(cfg.Alerts, Lang.En);
        ApplyFollowSystem();
    }

    /// <summary>Write the live language state back into the persisted config object.</summary>
    public static void StoreToConfig(Hud.LanguageConfig cfg)
    {
        cfg.FollowSystem = FollowSystem;
        cfg.Panel = Panel == Lang.Zh ? "zh" : "en";
        cfg.Screen = Screen == Lang.Zh ? "zh" : "en";
        cfg.Alerts = Alerts == Lang.Zh ? "zh" : "en";
    }

    private static Lang Parse(string? s, Lang fallback) =>
        string.Equals(s, "zh", StringComparison.OrdinalIgnoreCase) ? Lang.Zh
        : string.Equals(s, "en", StringComparison.OrdinalIgnoreCase) ? Lang.En
        : fallback;

    /// <summary>Display name of a language, written in the screen language.</summary>
    public static string Name(Lang lang, LangScope scope)
    {
        if (Screen == Lang.Zh) return lang == Lang.Zh ? "中文" : "英文";
        return lang == Lang.Zh ? "CHINESE" : "ENGLISH";
    }

    /// <summary>Text for a key on the given surface.</summary>
    public static string Get(LangScope scope, string key)
    {
        var table = scope switch
        {
            LangScope.Panel => PanelStrings,
            LangScope.Screen => ScreenStrings,
            _ => AlertStrings,
        };
        var lang = scope switch
        {
            LangScope.Panel => Panel,
            LangScope.Screen => Screen,
            _ => Alerts,
        };
        if (table.TryGetValue(key, out var pair))
            return lang == Lang.Zh ? pair.Zh : pair.En;
        return key;
    }

    private static readonly Dictionary<string, (string Zh, string En)> PanelStrings = new()
    {
        ["confirm"] = ("确认", "CONFIRM"),
        ["back"] = ("返回", "BACK"),
        ["up"] = ("上移", "UP"),
        ["down"] = ("下移", "DOWN"),
        ["help"] = ("帮助", "HELP"),
        ["reset"] = ("重置", "RESET"),
        ["pagePrev"] = ("上页", "PAGE◂"),
        ["pageNext"] = ("下页", "PAGE▸"),
        ["cancel"] = ("取消", "CANCEL"),
        ["save"] = ("保存", "SAVE"),
        ["brand"] = ("WTHUD  CFG", "WTHUD  CFG"),
    };

    private static readonly Dictionary<string, (string Zh, string En)> ScreenStrings = new()
    {
        // 页面标题
        ["menu"] = ("主菜单", "MAIN MENU"),
        ["page.display"] = ("显示", "DISPLAY"),
        ["page.datalink"] = ("数据链", "DATA LINK"),
        ["page.camera"] = ("姿态与校准", "ATTITUDE"),
        ["page.alerts"] = ("告警", "ALERTS"),
        ["page.keys"] = ("按键", "KEYS"),
        ["page.language"] = ("语言", "LANGUAGE"),
        ["page.diag"] = ("诊断", "DIAG"),
        // 语言页
        ["lang.panel"] = ("外板语言", "PANEL LANGUAGE"),
        ["lang.screen"] = ("屏幕语言", "SCREEN LANGUAGE"),
        ["lang.alerts"] = ("告警语言", "ALERT LANGUAGE"),
        ["lang.follow"] = ("跟随系统", "FOLLOW SYSTEM"),
        ["lang.reset"] = ("恢复默认语言", "RESET LANGUAGES"),
        // 通用
        ["on"] = ("开", "ON"),
        ["off"] = ("关", "OFF"),
        ["hint.nav"] = ("上移/下移 选择 · 确认 进入", "UP/DOWN SELECT · CONFIRM ENTER"),
        ["hint.lang"] = ("上移/下移 选择 · 确认 切换", "UP/DOWN SELECT · CONFIRM TOGGLE"),
        ["hint.help"] = ("任意键返回", "PRESS ANY KEY"),
        ["help.title"] = ("说明", "HELP"),
        // 菜单提示
        ["menu.hint.display"] = ("显示 / 缩放 / 光标洞", "DISPLAY · SCALE · CURSOR HOLE"),
        ["menu.hint.datalink"] = ("连接", "LINK"),
        ["menu.hint.camera"] = ("零点 / 符号源 / 校准键", "ZERO · SOURCE · CAL KEYS"),
        ["menu.hint.alerts"] = ("各类告警阈值", "ALERT THRESHOLDS"),
        ["menu.hint.keys"] = ("热键一览", "KEY BINDINGS"),
        ["menu.hint.language"] = ("外板 / 屏幕 / 告警", "PANEL · SCREEN · ALERTS"),
        ["menu.hint.diag"] = ("数据源与诊断", "SOURCES · DIAGNOSTICS"),
        ["hint.menu"] = ("上移/下移 选择 · 确认 进入", "UP/DOWN SELECT · CONFIRM ENTER"),
        ["hint.item"] = ("确认 切换 · 右键 反向 · 返回 上一级", "CONFIRM TOGGLE · RMB REVERSE · BACK"),
        // 行标签
        ["row.showHud"] = ("显示 HUD", "SHOW HUD"),
        ["row.fov"] = ("FOV 视场角", "FIELD OF VIEW"),
        ["row.yoff"] = ("垂直偏移", "VERTICAL OFFSET"),
        ["row.pitchzero"] = ("俯仰零点", "PITCH ZERO"),
        ["row.holeX"] = ("光标洞 X", "CURSOR HOLE X"),
        ["row.holeY"] = ("光标洞 Y", "CURSOR HOLE Y"),
        ["row.hole"] = ("光标洞半径", "CURSOR HOLE"),
        ["row.ladder"] = ("俯仰梯", "PITCH LADDER"),
        ["row.fpm"] = ("速度矢量", "FLIGHT MARKER"),
        ["row.formation"] = ("编队辅助", "FORMATION"),
        ["row.formationOnly"] = ("编队模式", "FORMATION ONLY"),
        ["row.formationLeadFeed"] = ("转弯提前量", "TURN LEAD"),
        ["row.leadMode"] = ("长机模式", "LEAD MODE"),
        ["row.leadIcon"] = ("长机机型", "LEAD ICON"),
        ["row.formationOmni"] = ("全向跟随", "OMNI FOLLOW"),
        ["row.leadHeading"] = ("长机航向", "LEAD HDG"),
        ["row.leadMach"] = ("长机马赫", "LEAD MACH"),
        ["row.dataLinkOn"] = ("使用数据互联", "USE DATA LINK"),
        ["row.dataLinkPeer"] = ("对端地址", "PEER ADDR"),
        ["row.dataLinkPeerNow"] = ("发送目标", "TARGETS"),
        ["hint.unavailable"] = ("当前不可用", "UNAVAILABLE"),
        ["row.dataLinkId"] = ("节点 ID", "NODE ID"),
        ["row.dataLinkRole"] = ("本机角色", "MY ROLE"),
        ["row.dataLinkLeadId"] = ("长机节点 ID", "LEAD NODE ID"),
        ["row.fMapOnly"] = ("地图判定", "MAP CHECK"),
        // 角色：长机 / 僚机#N。取代原来的「长机模式」开关（同一个状态，多了编号和队形）。
        ["row.role"] = ("角色", "ROLE"),
        ["opt.roleLead"] = ("长机", "LEAD"),
        ["opt.roleWing1"] = ("僚机#1", "WING #1"),
        ["opt.roleWing2"] = ("僚机#2", "WING #2"),
        ["opt.roleWing3"] = ("僚机#3", "WING #3"),
        ["opt.roleWingN"] = ("僚机#{0}", "WING #{0}"),
        ["row.fMargin"] = ("判定余量", "MARGIN"),
        // 填了 ID 之后紧跟着的一行只读状态：填错了/对方没开，当场能看出来
        ["row.dataLinkLeadState"] = ("当前长机", "LEAD"),
        ["val.leadAuto"] = ("未指定（自动）", "AUTO"),
        ["val.leadOnline"] = ("在线", "ONLINE"),
        ["val.leadOffline"] = ("离线", "OFFLINE"),
        ["row.apiHealth"] = ("接口状态", "API STATE"),
        ["row.dataLinkNodes"] = ("在线节点", "NODES"),
        ["row.dataLinkTeam"] = ("异队过滤", "TEAM FILTER"),
        ["val.teamNone"] = ("无", "NONE"),
        ["val.teamFiltered"] = ("已丢 {0} 包 · {1}s", "{0} dropped · {1}s"),
        ["row.dataLinkWings"] = ("僚机站位", "WING POS"),
        ["row.fWingMenu"] = ("僚机参数", "WINGMAN"),
        ["row.fLeadMenu"] = ("长机参数", "LEAD"),
        ["row.dlConnMenu"] = ("数据链", "DATA LINK"),
        ["row.dlStatusMenu"] = ("链路状态", "LINK STATUS"),
        ["row.dataLinkState"] = ("链路状态", "LINK STATE"),
        ["row.dataLinkPort"] = ("端口", "PORT"),
        ["row.dataLinkDesc"] = ("说明", "INFO"),
        ["val.dataLinkDesc"] = ("关=只用平面数据", "Off=planar only"),
        ["val.slotShort"] = ("槽位 {0}", "SLOT {0}"),
        ["val.tolShort"] = ("容差 {0}°", "TOL {0}°"),
        ["val.machShort"] = ("马赫 {0}", "MACH {0}"),
        ["val.noMach"] = ("未设马赫", "NO MACH"),
        ["val.linkOn"] = ("开", "ON"),
        ["val.linkOff"] = ("关", "OFF"),
        ["val.roleLead"] = ("长机", "LEAD"),
        ["val.roleWing"] = ("僚机", "WING"),
        ["row.extrapolate"] = ("插值平滑", "EXTRAPOLATE"),
        ["row.version"] = ("版本", "VERSION"),
        ["row.hideCockpit"] = ("座舱隐藏", "HIDE IN COCKPIT"),
        ["row.hideNose"] = ("机头视角隐藏", "HIDE IN NOSE VIEW"),
        ["row.rollCoupleMode"] = ("坡度耦合方式", "ROLL COUPLING MODE"),
        ["row.rollCouple"] = ("相机随滚转", "ROLL COUPLING"),
        ["row.bankSign"] = ("坡度方向", "BANK DIRECTION"),
        ["scope.resetVehicle"] = ("已清除该机型覆盖，回落到全局", "AIRFRAME OVERRIDES CLEARED"),
        ["edit.pick"] = ("手动选择机型", "PICK AIRFRAME"),
        ["pick.title"] = ("选择机型", "SELECT AIRFRAME"),
        ["scope.global"] = ("全局数据", "GLOBAL"),
        ["scope.vehicle"] = ("机型数据：", "AIRFRAME: "),
        ["row.pitchSign"] = ("俯仰符号", "PITCH SIGN"),
        ["row.bankTrim"] = ("坡度修正", "BANK TRIM"),
        ["row.fpmTrim"] = ("矢量修正", "MARKER TRIM"),
        ["row.tapeRadar"] = ("高度带雷达", "RADAR ALTITUDE"),
        ["row.aimRmb"] = ("右键瞄准", "AIM ON RMB"),
        ["row.source"] = ("当前符号源", "ACTIVE SIGN"),
        ["row.stallAoa"] = ("失速迎角", "STALL AOA"),
        ["row.spaaRange"] = ("友军防空显示距离", "FRIENDLY SPAA RANGE"),
        ["row.aoaWarn"] = ("迎角告警", "AOA WARNING"),
        ["row.overspeedMach"] = ("极速 Mach", "OVERSPEED MACH"),
        ["row.overspeedIas"] = ("极速 IAS", "OVERSPEED IAS"),
        ["row.sinkRate"] = ("下降率告警", "SINK RATE ALERT"),
        ["row.pullUp"] = ("PULL UP 提前量", "PULL UP LEAD"),
        ["row.bingo"] = ("低油量剩余", "BINGO TIME"),
        ["row.afAlert"] = ("机场告警距离", "AIRFIELD ALERT"),
        ["row.edgeMargin"] = ("边界告警余量", "EDGE MARGIN"),
        ["row.groundSpeed"] = ("地面隐藏速度", "GROUND HIDE SPEED"),
        ["row.hideGround"] = ("地面隐藏", "HIDE ON GROUND"),
        ["row.keyShow"] = ("显示 / 隐藏", "SHOW / HIDE"),
        ["row.keyZoom"] = ("瞄准模式", "AIMING LAYOUT"),
        ["row.keyView"] = ("视角循环", "CYCLE VIEW"),
        ["row.keyMap"] = ("战术地图", "TACTICAL MAP"),
        ["row.keyScore"] = ("记分板", "SCOREBOARD"),
        ["row.keyBrake"] = ("减速板", "AIRBRAKE"),
        ["row.keySave"] = ("保存配置", "SAVE CONFIG"),
        ["row.keyHole"] = ("光标洞开关", "CURSOR HOLE KEY"),
        ["row.keySettings"] = ("设置面板", "SETTINGS"),
        ["row.keyFormation"] = ("编队辅助", "FORMATION"),
        ["row.keyExit"] = ("退出", "EXIT"),
        ["row.keyPitchZero"] = ("俯仰零位校准", "PITCH ZERO CAL"),
        ["row.keyBankFlip"] = ("坡度方向翻转", "BANK FLIP"),
        ["row.keyBodyFixed"] = ("纯机体固连", "BODY FIXED"),
        ["row.keyReset"] = ("状态复位", "STATE RESET"),
        ["row.keyHoldHide"] = ("长按隐藏 HUD", "HOLD HIDE HUD"),
        ["row.keyStall"] = ("失速迎角校准", "STALL AOA CAL"),
        ["row.keyFix1"] = ("固定：上下偏移", "FIXED: VERT OFFSET"),
        ["row.keyFix2"] = ("固定：FOV", "FIXED: FOV"),
        ["row.keyFix3"] = ("固定：滚转耦合", "FIXED: ROLL COUPLE"),
        ["row.importWt"] = ("从战雷 (.blk) 导入按键", "IMPORT WT .BLK KEYS"),
        ["row.splashEnabled"] = ("击落播报", "SPLASH ALERTS"),
        ["row.playerName"] = ("玩家昵称", "PLAYER NAME"),
        ["tedit.title"] = ("文本输入", "TEXT INPUT"),
        ["tedit.hint"] = ("直接打字 · Ctrl+V 粘贴 · 确认 保存 · 返回 取消", "TYPE · CTRL+V PASTE · CONFIRM SAVE · BACK CANCEL"),
        ["splash.needName"] = ("先在下面填「玩家昵称」，击落播报才会生效", "SET PLAYER NAME FIRST FOR SPLASH"),
        ["row.endurance"] = ("续航系数", "ENDURANCE SCALE"),
        ["row.groundDelay"] = ("地面延迟", "GROUND DELAY"),
        ["row.mapFlip"] = ("地图 Y 翻转", "MAP Y FLIP"),
        ["row.configDebug"] = ("构型调试块", "CONFIG DEBUG"),
        ["row.stallMem"] = ("失速记忆", "STALL MEMORY"),
        ["row.pitchMem"] = ("俯仰记忆", "PITCH MEMORY"),
        ["row.dupKeys"] = ("重复热键", "DUPLICATE KEYS"),
        ["row.copyDiag"] = ("复制诊断信息", "COPY DIAGNOSTICS"),
        ["row.diagState"] = ("/state 状态", "/STATE"),
        ["row.diagInd"] = ("/indicators", "/INDICATORS"),
        ["row.diagMap"] = ("/map_obj", "/MAP_OBJ"),
        ["row.diagInfo"] = ("/map_info", "/MAP_INFO"),
        ["row.update"] = ("更新状态", "UPDATE"),
        ["row.checkUpdate"] = ("检查更新", "CHECK UPDATE"),
        // 选项文字
        ["opt.normal"] = ("正常", "NORMAL"),
        ["opt.invert"] = ("反转", "INVERT"),
        ["opt.wingLeft"] = ("左僚机", "LEFT WING"),
        ["opt.wingTrail"] = ("尾随", "TRAIL"),
        ["opt.wingRight"] = ("右僚机", "RIGHT WING"),
        ["row.formationShapeMenu"] = ("队形", "FORMATION SHAPE"),
        ["row.turnAssistMenu"] = ("长机转向辅助", "TURN ASSIST"),
        ["page.turnAssist"] = ("转向辅助", "TURN ASSIST"),
        ["menu.hint.turnAssist"] = ("目标 G / 容差 / 按键", "TARGET G · TOL · KEY"),
        ["row.turnKey"] = ("开关按键", "ASSIST KEY"),
        ["row.turnG"] = ("目标过载", "TARGET G"),
        ["row.turnTol"] = ("容差", "TOLERANCE"),
        ["row.turnBcast"] = ("广播目标坡度", "BROADCAST BANK"),
        ["row.turnBank"] = ("目标坡度", "TARGET BANK"),
        ["row.turnStatus"] = ("当前状态", "STATE"),
        ["row.turnWing"] = ("收到的目标坡度", "RECEIVED"),
        ["page.formationShape"] = ("队形", "FORMATION"),
        ["menu.hint.formationShape"] = ("预设 / 编号 / 站位数值", "PRESET · NUMBER · STATION"),
        ["row.fShape"] = ("预设队形", "PRESET"),
        ["row.fSlot"] = ("编号", "NUMBER"),
        ["row.fSpacing"] = ("间距", "SPACING"),
        ["row.fSlant"] = ("斜距", "SLANT"),
        ["row.fSideM"] = ("横向", "LATERAL"),
        ["row.fBackM"] = ("纵向", "LONGITUDINAL"),
        ["row.fUpM"] = ("站位高度", "ALTITUDE"),
        ["row.fVertFirst"] = ("引导模式", "GUIDANCE"),
        ["opt.oneStage"] = ("一段式", "ONE-STAGE"),
        ["opt.twoStage"] = ("两段式", "TWO-STAGE"),
        ["row.fVertGate"] = ("高度到位门槛", "ALT GATE"),
        ["row.fVertMax"] = ("升降率上限", "VERT RATE MAX"),
        ["row.fFarRange"] = ("爬升显示距离", "CLIMB RANGE"),
        ["opt.shapeCustom"] = ("自定义", "CUSTOM"),
        ["opt.shapeEchelon"] = ("梯队", "ECHELON"),
        ["opt.shapeColumn"] = ("纵队", "COLUMN"),
        ["opt.shapeDiamond"] = ("菱形", "DIAMOND"),
        ["slot.1"] = ("1", "1"),
        ["slot.2"] = ("2", "2"),
        ["slot.3"] = ("3", "3"),
        ["hint.presetOnly"] = ("仅预设队形可改", "PRESET ONLY"),
        ["hint.vertFirstOnly"] = ("仅两段式", "TWO-STAGE ONLY"),
        ["hint.wingOnly"] = ("仅僚机", "WING ONLY"),
        ["row.wingSide"] = ("侧位", "SIDE"),

        // ---- 编队调参页（僚机制导增益，平时不用动） ----
        ["page.formationTune"] = ("编队", "FORMATION"),
        ["menu.hint.formationTune"] = ("开关 / 判定 / 角色入口", "SWITCH · GATES · ROLE PAGES"),
        ["page.formationWing"] = ("僚机参数", "WINGMAN"),
        ["menu.hint.formationWing"] = ("识别 / 判定 / 增益", "ID · GATES · GAINS"),
        ["page.formationLead"] = ("长机参数", "LEAD"),
        ["menu.hint.formationLead"] = ("航向 / 马赫 / 转向辅助", "HDG · MACH · TURN ASSIST"),
        ["page.dataLinkStatus"] = ("链路状态", "LINK STATUS"),
        ["menu.hint.dataLinkStatus"] = ("节点 / 角色 / 接口", "NODES · ROLE · API"),
        ["row.fWingOffset"] = ("侧位横偏", "WING OFFSET"),
        ["row.fStationBack"] = ("后距", "STATION BACK"),
        ["row.fDonePct"] = ("完成误差 %", "DONE BAND %"),
        ["row.fAlignDeg"] = ("站位容差", "ALIGN TOL"),
        ["row.fDoneHold"] = ("完成保持", "DONE HOLD"),
        ["row.fDropPct"] = ("掉出门槛 %", "DROP BAND %"),
        ["row.fSteadyDeg"] = ("保持死区角", "STEADY DEG"),
        ["row.fSteadyKmh"] = ("保持死区速", "STEADY SPEED"),
        ["row.fKp"] = ("外环比例", "OUTER KP"),
        ["row.fKpNear"] = ("近距比例", "NEAR KP"),
        ["row.fKi"] = ("积分(‰)", "INTEGRAL (‰)"),
        ["row.fKv"] = ("接近率增益", "CLOSING KV"),
        ["row.fBlendM"] = ("远近融合距离", "BLEND DIST"),
        ["row.fTau"] = ("油门平滑", "THROTTLE TAU"),
        ["row.fLeadFeed"] = ("转弯提前量", "LEAD TURN FEED"),
        ["row.fLeadMin"] = ("提前量门槛", "LEAD MIN RATE"),
        ["row.fLeadMax"] = ("提前量上限", "LEAD MAX ANG"),
        ["row.fMaxRatio"] = ("速度上限倍率", "SPEED MAX RATIO"),
        ["row.fMinRatio"] = ("速度下限倍率", "SPEED MIN RATIO"),
        ["row.formationWarnG"] = ("转弯告警阈值", "TURN WARN G"),

        ["opt.on"] = ("开", "ON"),
        ["opt.off"] = ("关", "OFF"),
        ["opt.auto"] = ("自动", "AUTO"),
        ["opt.positive"] = ("正", "POSITIVE"),
        ["opt.negative"] = ("负", "NEGATIVE"),
        ["opt.ias"] = ("IAS", "IAS"),
        ["opt.mach"] = ("MACH", "MACH"),
        ["opt.en"] = ("英文", "ENGLISH"),
        ["opt.zh"] = ("中文", "CHINESE"),        ["pick.hint"] = ("确认 指定 · 返回 取消", "CONFIRM PICK · BACK CANCEL"),
        ["help.pagerHint"] = ("PAGE 翻页 · BACK 返回", "PAGE FLIPS · BACK RETURNS"),
        ["page.single"] = ("本页已全部显示", "SINGLE PAGE"),
        ["page.edge"] = ("已到最后一页", "LAST PAGE"),
        ["help.close"] = ("任意键 / 点击 关闭", "ANY KEY / CLICK TO CLOSE"),
        ["help.body"] = ("侧键：选择    确认：执行     上下：移动     返回：上一级\n数值行：确认 进入输入页，直接键入数字\n键位行：确认 进入捕获页，按住组合键实时显示\n执行类：确认 后进入二次确认，再确认才执行\n底排：重置 / 翻页 / 取消 / 保存",
                         "SIDE KEY: SELECT   CONFIRM: APPLY   UP/DOWN: MOVE   BACK: UP ONE LEVEL\nNUMBER ROW: CONFIRM OPENS NUMERIC ENTRY\nKEY ROW: CONFIRM OPENS LIVE CAPTURE\nACTION ROW: CONFIRM SHOWS A SECOND CONFIRMATION\nBOTTOM: RESET / PAGE / CANCEL / SAVE"),        ["page.airframe"] = ("机型", "AIRFRAMES"),
        ["menu.hint.airframe"] = ("按机型异化的能力", "PER-AIRFRAME DATA"),
        ["airframe.ra"] = ("雷达高度", "RADAR ALT"),
        ["airframe.att"] = ("姿态来源", "ATTITUDE"),
        ["airframe.ab"] = ("减速板", "AIRBRAKE"),
        ["airframe.pitchSign"] = ("俯仰符号", "PITCH SIGN"),
        ["airframe.stall"] = ("失速迎角", "STALL AOA"),
        ["airframe.aoa"] = ("迎角告警", "AOA WARN"),
        ["airframe.sink"] = ("下降率告警", "SINK RATE"),
        ["airframe.note"] = ("备注", "NOTE"),
        ["page.panel"] = ("面板 / 语言", "PANEL · LANGUAGE"),
        ["menu.hint.panel"] = ("颜色 / 拟真按键 / 语言", "COLOURS · KEYS · LANGUAGE"),
        ["menu.panel"] = ("面板", "PANEL"),
        ["menu.panel.hint"] = ("配色 / 操作方式", "COLORS / INPUT"),
        ["row.inkColor"] = ("文本颜色", "TEXT COLOR"),
        ["row.accentColor"] = ("高亮颜色", "ACCENT COLOR"),
        ["row.realisticKeys"] = ("拟真按键操作", "KEY-ONLY MODE"),
        ["preset.title"] = ("面板配色", "PANEL COLOURS"),
        ["selectPreset"] = ("选择颜色 · 确认 应用 · 返回 取消", "PICK COLOR · CONFIRM APPLY · BACK CANCEL"),
        ["color.white"] = ("白色", "WHITE"),
        ["color.green"] = ("绿色", "GREEN"),
        ["color.purple"] = ("紫色", "PURPLE"),
        ["color.blue"] = ("蓝色", "BLUE"),
        ["color.red"] = ("红色", "RED"),
        ["edit.title"] = ("数值输入", "NUMERIC ENTRY"),
        ["capture.title"] = ("键位捕获", "KEY CAPTURE"),
        ["edit.prompt"] = ("输入数值 · 确认 提交 · 返回 取消", "TYPE VALUE · CONFIRM APPLY · BACK CANCEL"),
        ["capture.prompt"] = ("按下要绑定的按键", "PRESS A KEY TO BIND"),
        ["capture.hint"] = ("确认 保存 · 返回 取消", "CONFIRM SAVE · BACK CANCEL"),
        // ---- 配置互传（48018）----
        ["page.cfgMenu"] = ("配置传递", "CONFIG SHARE"),
        ["page.cfgInbox"] = ("收到的配置", "RECEIVED"),
        ["page.cfgConfirm"] = ("确认导入", "CONFIRM"),
        ["menu.hint.cfgMenu"] = ("广播 · 接收 · 文件", "SEND · RECEIVE · FILE"),
        ["menu.hint.cfgInbox"] = ("谁发来的", "FROM WHOM"),
        ["menu.hint.cfgConfirm"] = ("看差异再导入", "CHECK THEN APPLY"),
        ["row.cfgMenu"] = ("配置传递", "CONFIG SHARE"),
        ["row.cfgBroadcast"] = ("广播本机配置", "BROADCAST"),
        ["row.cfgInbox"] = ("接收配置", "RECEIVE"),
        ["row.cfgExport"] = ("导出到文件", "EXPORT FILE"),
        ["row.cfgImport"] = ("从文件导入", "IMPORT FILE"),
        ["row.cfgNone"] = ("暂无配置包", "NONE YET"),
        ["row.cfgOffer"] = ("配置包", "OFFER"),
        ["row.cfgFrom"] = ("来自", "FROM"),
        ["row.cfgNode"] = ("节点", "NODE"),
        ["row.cfgRole"] = ("对方角色", "THEIR ROLE"),
        ["row.cfgForm"] = ("队形", "FORMATION"),
        ["row.cfgDiff"] = ("将改动", "CHANGES"),
        ["row.cfgApply"] = ("导入这份配置", "APPLY"),
        ["cfg.none"] = ("无", "NONE"),
        ["cfg.have"] = ("● {0} 个配置", "● {0} OFFER(S)"),
        ["cfg.broadcast"] = ("已广播 · {0} 个目标", "BROADCAST · {0} TARGET(S)"),
        ["cfg.noChannel"] = ("配置通道未启动", "CONFIG CHANNEL DOWN"),
        ["cfg.exported"] = ("已导出 · {0}", "EXPORTED · {0}"),
        ["cfg.imported"] = ("已导入 · 按 SAVE 保存", "IMPORTED · SAVE TO KEEP"),
        ["cfg.badPacket"] = ("不是配置包或已损坏", "NOT A CONFIG PACKET"),
        ["cfg.failed"] = ("失败：{0}", "FAILED: {0}"),
        ["cfg.exportTitle"] = ("导出编队配置", "Export formation config"),
        ["cfg.importTitle"] = ("选择编队配置文件", "Pick a formation config file"),
        ["cfg.diff.none"] = ("与当前一致", "SAME AS NOW"),
        // 差异项用短名：MFD 一格放不下行标签那么长的词，长了会压到隔壁那一格
        ["cfg.short.row.fSlot"] = ("编号", "NO."),
        ["cfg.short.row.fShape"] = ("队形", "SHAPE"),
        ["cfg.short.row.fWingOffset"] = ("横偏", "LAT"),
        ["cfg.short.row.fStationBack"] = ("后距", "BACK"),
        ["cfg.short.row.fUpM"] = ("高度", "ALT"),
        ["cfg.short.row.fDonePct"] = ("完成", "DONE"),
        ["cfg.short.row.fDoneHold"] = ("保持", "HOLD"),
        ["cfg.short.row.fDropPct"] = ("掉队", "DROP"),
        ["cfg.short.row.fMapOnly"] = ("地图", "MAP"),
        ["cfg.short.row.formation"] = ("辅助", "ASSIST"),
        ["cfg.short.row.leadIcon"] = ("机型", "TYPE"),
        ["cfg.diff.more"] = (" 等{0}项", " +{0}"),
        // 僚机侧：连的是哪个长机
        ["formation.linkLead"] = ("长机 #{0} · {1}s", "LEAD #{0} · {1}s"),
        ["formation.linkMapOnly"] = ("地图最近友机 · 无链路", "NEAREST ON MAP · NO LINK"),
        ["val.srcConfig"] = ("手填", "BY ID"),
        ["val.srcRole"] = ("角色", "BY ROLE"),
        ["val.srcSingle"] = ("唯一", "ONLY NODE"),
        ["val.srcRecent"] = ("最近", "RECENT"),
        ["hint.readonly"] = ("只读项，无法修改", "READ-ONLY ITEM"),        ["confirm.resetAirframe"] = ("重置该机型到默认？", "RESET THIS AIRFRAME?"),
        ["confirm.askReset"] = ("确认 重置 · 取消 返回", "CONFIRM: RESET · CANCEL: BACK"),
        ["toast.resetAirframe"] = ("该机型已恢复默认", "AIRFRAME RESET"),
        ["confirm.discard"] = ("放弃未保存的修改？", "DISCARD UNSAVED CHANGES?"),
        ["confirm.askDiscard"] = ("确认 放弃并关闭 · 取消 返回面板", "CONFIRM: DISCARD & CLOSE · CANCEL: BACK"),        ["confirm.title"] = ("确认", "CONFIRM"),
        ["confirm.ask"] = ("是否执行该操作？", "EXECUTE THIS ACTION?"),
        ["confirm.ok"] = ("确认", "CONFIRM"),
        ["confirm.cancel"] = ("取消", "CANCEL"),
        ["hint.confirm"] = ("左移/右移 选择 · 确认 执行 · 返回 取消", "LEFT/RIGHT SELECT · CONFIRM RUN · BACK CANCEL"),        ["hint.root"] = ("已在主菜单 · 取消 关闭面板", "AT ROOT · CANCEL TO CLOSE"),
        ["help.lang"] = ("分别设置机框、屏幕、告警的语言；跟随系统开启后三项自动跟随系统语言。",
                         "Set the language of the bezel, the screen and the HUD alerts. FOLLOW SYSTEM mirrors the OS language."),
        // 回归快照
        ["page.snapshots"] = ("回归快照", "SNAPSHOTS"),
        ["menu.hint.snapshots"] = ("恢复到历史保存点", "RESTORE SAVED STATES"),
        ["snap.title"] = ("回归快照", "SNAPSHOTS"),
        ["snap.hint"] = ("确认 恢复 · 返回 上一级", "CONFIRM RESTORE · BACK"),
        ["snap.empty"] = ("暂无保存快照，先 SAVE 一次", "NO SAVED SNAPSHOTS YET - SAVE FIRST"),
        ["snap.confirmAsk"] = ("恢复到 {0} 的 save 快照？", "RESTORE THE {0} SAVE SNAPSHOT?"),
        ["snap.confirmSub"] = ("确认 恢复 · 取消 返回", "CONFIRM: RESTORE · CANCEL: BACK"),
        ["snap.toast.restored"] = ("已恢复到 {0} 的快照", "RESTORED TO {0}"),
        ["snap.toast.failed"] = ("恢复失败：快照不可读取", "RESTORE FAILED - SNAPSHOT UNREADABLE"),
    };

    private static readonly Dictionary<string, (string Zh, string En)> AlertStrings = new()
    {
        ["stall"] = ("失速", "STALL"),
        ["aoa"] = ("迎角", "AOA"),
        ["pullup"] = ("拉起", "PULL UP"),
        ["guide.left"] = ("◀ 左转 {0}°", "◀ TURN L {0}"),
        ["guide.right"] = ("右转 {0}° ▶", "TURN R {0} ▶"),
        ["guide.aligned"] = ("对准", "ON"),
        ["guide.accel"] = ("加速", "SPEED UP"),
        ["guide.decel"] = ("减速", "SLOW DOWN"),
        ["guide.hold"] = ("保持", "HOLD"),
        ["guide.turnMore"] = ("多压一点", "BANK MORE"),
        ["guide.turnLess"] = ("回一点杆", "BANK LESS"),
        ["guide.turnHold"] = ("坡度正好", "ON TARGET"),
        ["guide.turnArming"] = ("转向以锁定", "TURN TO ARM"),
        ["guide.leadBank"] = ("长机 {0}{1}°", "LEAD {0}{1}°"),
        ["guide.vertStale"] = ("旧数据", "STALE"),
        ["guide.altMatch"] = ("高度一致", "LEVEL"),
        ["guide.lead.formation"] = ("编队完整", "FORMATION"),
        ["guide.lead.late"] = ("僚机掉队", "WINGMAN LATE"),
        // 长机看僚机自报（数据链第 11 段）：带槽位号，三人编队能直接点名
        // 长机 HUD 的僚机表：只有状态词，标签单独一列
        ["guide.wing.ok"] = ("到位", "ON STATION"),
        ["guide.wing.behind"] = ("掉队 {0}m", "BEHIND {0}m"),
        ["guide.wing.ahead"] = ("冲出 {0}m", "AHEAD {0}m"),
        ["guide.wing.off"] = ("偏离站位", "OFF STATION"),
        // 僚机在编队里、但这一帧给不出判定（没自机坐标 / 没认出长机）。
        // ★ 不能写成「未报」：那是"这架没开编队"；也不能算「掉队」：那是凭空定罪。
        ["guide.wing.dataErr"] = ("数据错误", "DATA ERR"),
        // 掉线：3 秒收不到包。带着秒数，长机才知道是刚断还是断了很久。
        ["guide.wing.offline"] = ("掉线 {0}s", "OFFLINE {0}s"),
        ["guide.lead.wingOffline"] = ("僚机掉线", "WINGMAN OFFLINE"),
        ["guide.lead.wingBehind"] = ("{0} 掉队 {1}m", "{0} BEHIND {1}m"),
        ["guide.lead.wingAhead"] = ("{0} 冲出 {1}m", "{0} AHEAD {1}m"),
        ["guide.lead.wingOff"] = ("{0} 偏离站位", "{0} OFF STATION"),
        ["guide.lead.close"] = ("距离过近", "TOO CLOSE"),
        ["guide.lead.none"] = ("无僚机", "NO CONTACT"),
        ["guide.lead.setMach"] = ("设定马赫", "SET MACH"),
        ["guide.lead.turnL"] = ("左转 {0:0}°", "TURN L {0:0}°"),
        ["guide.lead.turnR"] = ("右转 {0:0}°", "TURN R {0:0}°"),
        ["guide.lead.add"] = ("加速", "ADD POWER"),
        ["guide.lead.reduce"] = ("减速", "REDUCE POWER"),
        // 转弯提前量：长机正在以多少度/秒转弯，僚机的转向建议已按这个值前移
        ["guide.leadTurnR"] = ("长机右转 {0}°/s", "LEAD TURN R {0}°/s"),
        ["guide.leadTurnL"] = ("长机左转 {0}°/s", "LEAD TURN L {0}°/s"),
        // 长机转弯过载超过阈值：队形跟不住，直接说清楚，别让飞行员以为引导失灵
        ["guide.turnGWarn"] = ("{0:0.0}G 转弯过急", "{0:0.0}G TOO TIGHT"),
        // 编队到位 / 解除：给长机看的「可以开始机动了」信号
        ["guide.formationDone"] = ("编队完成", "FORMATION SET"),
        // 找不到长机的两种原因：说清是哪一种，否则空屏无从判断
        ["formation.noFriendly"] = ("附近没有友军", "NO FRIENDLY NEARBY"),
        // 认得长机但拿不到自机坐标：8111 半初始化 / 不在对局。这是错误状态，要明说
        ["formation.noSelfPos"] = ("无自机坐标 · 8111 未就绪", "NO OWN POSITION · 8111 NOT READY"),
        ["formation.leadHidden"] = ("长机不在图上 · 超出显示范围", "LEAD NOT ON MAP · OUT OF RANGE"),
        ["formation.noMatch"] = ("无 {0} 型友军 · 检查长机机型", "NO {0} CONTACT · CHECK LEAD TYPE"),
        // 两个 role=1 且没填长机 ID：引导会在两架之间跳，必须说清该怎么办
        ["formation.leadAmbiguous"] = ("长机不唯一 · 填节点 ID", "MULTIPLE LEADS · SET NODE ID"),
        ["guide.formationOff"] = ("编队已解除", "FORMATION OFF"),
        ["guide.lead.nolink"] = ("无数据链", "NO LINK"),
        ["splash"] = ("击落", "SPLASH"),
        ["sinkrate"] = ("下降率", "SINK RATE"),
        ["reducespeed"] = ("减速", "REDUCE SPEED"),
        ["return"] = ("返回战区", "RETURN"),
        ["gear.down"] = ("起落架放下", "GEAR DOWN"),
        ["gear.up"] = ("起落架收起", "GEAR UP"),
        ["flaps.down"] = ("襟翼放下", "FLAPS DOWN"),
        ["flaps.up"] = ("襟翼收起", "FLAPS UP"),
        ["brake.out"] = ("减速板展开", "AIRBRAKE OUT"),
        ["brake.in"] = ("减速板收起", "AIRBRAKE IN"),
        ["brake.steady"] = ("减速板", "AIRBRAKE"),
        ["label.fuel"] = ("燃油", "FUEL"),
        ["label.alt"] = ("高度", "ALT"),
        ["label.grid"] = ("网格", "GRID"),
        ["label.flaps"] = ("襟翼", "FLAPS"),
        ["af"] = ("机场", "AF"),
        ["afenemy"] = ("！机场", "AF!"),
        ["bingo"] = ("低油量", "BINGO"),
        ["overspeed"] = ("超速", "OVERSPEED"),
        ["aoaAlert"] = ("迎角超限", "AOA"),
        ["stallAlert"] = ("接近失速", "STALL"),
    };
}











