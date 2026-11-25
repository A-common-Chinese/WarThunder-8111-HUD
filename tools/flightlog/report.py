#!/usr/bin/env python3
# -*- coding: utf-8 -*-
# SPDX-License-Identifier: AGPL-3.0-or-later
"""把解析后的飞行日志渲染成一个**自包含单文件 HTML（翻页式）**。

特点：

* **翻页视图**：第 1 页是概览（汇总卡片 + 比能量缩略图），之后每个数据主题一页
  （速度 / 高度 / 比能量 Es / dEs/dt / Vy / 迎角与过载 / 姿态），多架次时最后追加
  "全局总览"页；每页固定铺满视口，不做长滚动。
* 翻页交互：底部 ``‹ 上一页`` / ``下一页 ›``、页码 ``第 3 / 8 页``、圆点导航、
  左右两侧大按钮，键盘 ←/→、PageUp/PageDown、Home/End 均可翻页；
* 多架次：页眉的架次选择器切换后，**当前页**的曲线换成该架次（事件竖线/标签保留）；
* 速度页顶部显示**平均 IAS / TAS / Mach**，起止时间可手动设置（默认整个架次），
  所选区间在图上用浅色底纹 + 边界虚线标出、平均值画成对应颜色的水平虚线；
* 图表全部用内联 SVG 在 Python 侧画好，**浏览器禁用脚本也能看到全部图**；
  曲线是**三次贝塞尔平滑**（Catmull-Rom 切线 + Fritsch–Carlson 限幅，零过冲），
  相邻样本间隔 > 1 s 的"数据中断"处实线断开、补一条虚线贝塞尔桥 + 淡灰竖带/斜纹，
  图下 legend 加 ``GAP 数据中断 2.66s · T+00:19``；概览页有「数据完整性」一行
  （样本 / 空洞 / 最大间隔 / >1 s 中断数）；
* 内联 JS 只负责"锦上添花"：翻页、架次切换、鼠标悬停十字线 + 数值浮窗；
* 事件（死亡 / 复活 / 换机型 / 击落）以竖线 + 标签画在每条时间轴上方，并贯穿全部图表；
  击落（``kill``，0.5.2 起）用青绿/蓝竖线，图下 legend 补一行 ``KILL 击落 <target> · T+mm:ss``；
  数据中断（``gap``，0.5.3 起日志才有）不画事件竖线，用断口虚线桥 + 竖带表示，
  legend 补 ``GAP 数据中断 <秒>s · T+mm:ss``（老日志没有 gap 事件也能按样本间隔自动检测）；
* **6 套美术主题**（``--theme blueprint|glass|manual|tactical|clean|mfd``，
  默认 ``blueprint`` = A 工程蓝图：深蓝图纸底 + 坐标纸网格 + 每页图框/图号
  （``FIG. 03 · 比能量高度``）+ 页脚 ``SHEET n/9`` + 右下角标题栏）；
  配色集中在 :data:`THEMES` 里，通过 CSS 变量输出，图表曲线/网格/事件线/统计卡
  全部取自同一份主题配置；
* **审阅批注**：按日志自动计算每页最多 2 条（速度页 = 最大 IAS + 限速预警；高度页 =
  最低气压高度；Es 页 = Es 最低点 + 2 s 滑动平均 dEs/dt 最负的断崖起点；迎角/过载页 =
  最大过载；姿态页不批注；死亡/坠毁竖线每页保留，只在概览/能量页加圈；
  击落竖线每页保留、legend 每页补 KILL 行，不进数据批注）。
  屏幕 = 红色十字/方框 + 浅色标注条；打印/PDF = 手绘双圈 + 打字机短注；
  时间相近（<10 s）的批注自动合并；
* **保存二级页**：页眉「保存」→「保存设置」模态：PDF 完整版 / PNG 单张复盘卡、
  保留批注开关。PNG 复盘卡 = 生成报告时 Python 画好的内嵌 SVG（档案袋风格），
  JS 序列化 SVG → canvas → PNG 下载，不引任何外部库；
* 不依赖任何外部资源（无 CDN / 图片 / 第三方 JS），离线双击即看。

坐标/面板配置集中在文件开头的常量里；配色与字体栈见 ``THEMES``。
"""

from __future__ import annotations

import html
import json
import math
import os
import re
import sys
from dataclasses import dataclass, field
from typing import Any, Dict, List, Optional, Sequence, Tuple

from .parse import EVENT_LABELS, Flight, LogModel, average_speed

# ---------------------------------------------------------------------------
# 版式与配色
# ---------------------------------------------------------------------------

#: SVG 内部坐标系宽度。页面用 preserveAspectRatio="none" 铺满容器，
#: 因此这个值只决定"文字/线宽在 1500 单位里多大"，不决定实际像素。
VIEW_W = 1500.0
MARGIN_L = 78.0
MARGIN_R = 96.0
PLOT_LEFT = MARGIN_L
PLOT_RIGHT = VIEW_W - MARGIN_R
PLOT_W = PLOT_RIGHT - PLOT_LEFT
PAD_TOP = 14.0
PAD_BOTTOM = 26.0

#: 单条曲线最多画多少个点（超过就等距抽稀；悬停数据仍是全量）
MAX_SVG_POINTS = 1800
#: 全局总览抽稀目标点数
MAX_OVERVIEW_POINTS = 2600
#: 序列在两条曲线之间断开的时间阈值（秒）：相邻样本间隔 > 1 s 视为一次**数据中断**，
#: 实线断开、改画虚线贝塞尔桥（8111 轮询卡顿时录制样本会出现这种空洞）。
PATH_GAP_S = 1.0
#: 概览「数据完整性」里"空洞"的判定阈值（秒）：相邻样本间隔 > 0.2 s 计一处。
#: 用户实测 220101 文件（104 s / 2088 样本）按这个口径正好 43 处。
GAP_HOLE_S = 0.2
#: 超过这个跨度的空洞不再当"轮询卡顿"：达到架次切分阈值（DEFAULT_GAP_SPLIT_S = 30 s）
#: 说明是换局 / 长时间暂停，画插值桥反而误导，保持断开（也不进 legend）。
GAP_BRIDGE_MAX_S = 30.0
#: gap 事件与自动检测空洞的时间容差（秒），用于合并去重。
GAP_MERGE_S = 1.5
#: 中断的表现参数：虚线贝塞尔桥 / 淡灰竖带 / 斜纹
GAP_BRIDGE_OPACITY = 0.42
GAP_BRIDGE_WIDTH = 1.35
GAP_BAND_OPACITY = 0.06
GAP_HATCH_OPACITY = 0.38

#: 单页图表的 viewBox 高度（页面铺满时按容器比例拉伸）
PAGE_CHART_H = 650.0
#: 概览页比能量缩略图的 viewBox 高度
THUMB_CHART_H = 470.0
#: 全局总览页每张堆叠图的高度
GLOBAL_CHART_H = 200.0

#: 默认主题 = A 工程蓝图（用户选定的形态）。
DEFAULT_THEME = "blueprint"


def _alpha(color: str, suffix: str) -> str:
    """给 ``#RRGGBB`` 追加 alpha（``#RRGGBBAA``）；其它写法原样返回。"""
    if len(color) == 7 and color.startswith("#"):
        return color + suffix
    return color


# ---------------------------------------------------------------------------
# 审阅批注参数
# ---------------------------------------------------------------------------

#: 同一页上两条批注的时间差小于该值（秒）时合并成一条。
ANNOTATION_MERGE_S = 10.0
#: 每页最多几条批注（合并后仍受此上限约束）。
ANNOTATION_MAX_PER_PAGE = 2
#: Es 断崖：dEs/dt 的滑动平均窗口（秒）。
ANNOTATION_SMOOTH_S = 2.0

#: 蓝图主题的结构样式（网格 / 图框 / 图号 / 页脚 / 标题栏 / 保存设置页）。
BLUEPRINT_CSS = """
/* ===== A 工程蓝图：深蓝图纸 / 坐标纸网格 / 图框 / 图号 / 标题栏 ===== */
body{
  background:
    radial-gradient(ellipse at 50% 38%,#0e365e 0%,#0B2A4A 46%,#061c33 100%) fixed;
}
header{
  background:linear-gradient(180deg,#0c3054,#092646);
  border-bottom:1.5px solid #8fc6e8b3;
  box-shadow:inset 0 -1px 0 #8fc6e82e;
  padding:10px 26px 8px;
}
header h1{font-family:var(--font-title);font-size:21px;font-weight:700;letter-spacing:1.8px}
header h1::before{
  content:"WT-FL · ";font-family:var(--font-num);font-size:11px;font-weight:400;
  letter-spacing:.2em;color:var(--muted);vertical-align:middle;margin-right:6px;
}
.subline{font-family:var(--font-num);letter-spacing:.05em;font-size:11.5px}
.subline code{background:#0e3a63;border:1px solid #8fc6e84d;letter-spacing:.04em}
.flights .flabel{font-family:var(--font-num);letter-spacing:.14em}
.fbtn,.fselect{border-radius:var(--radius-xs)}
.fbtn{font-family:var(--font-num);letter-spacing:.08em;background:#0e3557;
  border-color:#8fc6e873;color:#cfe6f8}
.fbtn:hover{border-color:#e6f4ff;color:#fff;background:#14476f}
.fbtn.active{background:#F2EBD8;border-color:#f2ebd8;color:#0a2540;font-weight:700}
.header-save{background:#F2EBD8;color:#0A2540;border-color:#F2EBD8;font-weight:700;
  letter-spacing:.18em;padding:4px 18px}
.header-save:hover{background:#fff;border-color:#fff;color:#0A2540}
.page{padding:32px 34px 46px}
.page::before{content:"";position:absolute;inset:10px;border:1.4px solid #8fc6e8b3;
  pointer-events:none;z-index:0}
.page::after{content:"";position:absolute;inset:15px;border:.6px solid #8fc6e880;
  pointer-events:none;z-index:0}
.page>*{position:relative;z-index:1}
.page-head{border-bottom:1px solid #8fc6e84d;padding-bottom:4px;align-items:baseline}
.page-title{font-family:var(--font-title);font-size:18px;font-weight:700;letter-spacing:1.2px}
.page-title::before{content:"FIG. " counter(fig,decimal-leading-zero) " · ";
  font-family:var(--font-num);font-weight:400;font-size:12.5px;letter-spacing:.1em;
  color:var(--muted)}
.page-sub{font-family:var(--font-num);font-size:11.5px;letter-spacing:.05em}
.page-right{font-family:var(--font-num);font-size:11.5px;letter-spacing:.08em}
body{counter-reset:fig}
section.page{counter-increment:fig}
.chart-wrap{border:1px solid #8fc6e84d;background:linear-gradient(180deg,#0a2744,#0b2a4a)}
.chart-svg text{letter-spacing:.07em}
.card,.avgbar{background:#0c2e50;border-color:#8fc6e859;border-radius:var(--radius-sm)}
.card .k,.card span{font-family:var(--font-num);letter-spacing:.08em}
.card b{font-family:var(--font-num)}
.badges span{background:#0d3357;border-color:#8fc6e859;font-family:var(--font-num);
  letter-spacing:.06em}
.legend{font-family:var(--font-num);letter-spacing:.05em}
.pager{background:#08213a;border-top:1px solid #8fc6e859}
.pbtn{font-family:var(--font-num);letter-spacing:.08em;background:#0d3357;
  border-color:#8fc6e873}
.pbtn:hover:not(:disabled){background:#14476f;border-color:#e6f4ff}
.dot.active{background:#e6f4ff;box-shadow:0 0 0 3px #8fc6e83d}
.hint,.side{font-family:var(--font-num)}
.side{background:#0a2744e6;border-color:#8fc6e859}
/* chrome：坐标纸网格 / 注册标记 / 页脚 / 右下角标题栏 */
.bp-chrome{position:absolute;inset:0;pointer-events:none;z-index:0}
.bp-grid{position:absolute;inset:0;
  background:
    repeating-linear-gradient(0deg,transparent 0 19px,#8fc6e80e 19px 20px),
    repeating-linear-gradient(90deg,transparent 0 19px,#8fc6e80e 19px 20px),
    repeating-linear-gradient(0deg,transparent 0 99px,#8fc6e81a 99px 100px),
    repeating-linear-gradient(90deg,transparent 0 99px,#8fc6e81a 99px 100px)}
.bp-reg{position:absolute;width:17px;height:17px;opacity:.85}
.bp-reg::before,.bp-reg::after{content:"";position:absolute;background:#8fc6e8}
.bp-reg::before{left:0;top:8px;width:17px;height:1px}
.bp-reg::after{left:8px;top:0;width:1px;height:17px}
.bp-reg.tl{left:24px;top:12px}.bp-reg.tr{right:24px;top:12px}
.bp-reg.bl{left:24px;bottom:24px}.bp-reg.br{right:24px;bottom:24px}
.bp-foot{position:absolute;left:34px;right:30px;bottom:14px;height:24px;
  display:flex;align-items:stretch;justify-content:space-between;gap:10px;
  font-family:var(--font-num);font-size:9px;letter-spacing:.16em;color:var(--muted)}
.bp-sheet{display:flex;align-items:center;border:1px solid #8fc6e859;padding:0 9px;
  color:#cfe6f8;white-space:nowrap}
.bp-tb{display:flex;border:1px solid #8fc6e859;background:#08213a8c}
.bp-tb span{display:flex;flex-direction:column;justify-content:center;gap:1px;padding:1px 9px;
  border-left:1px solid #8fc6e84d;min-width:0}
.bp-tb span:first-child{border-left:0}
.bp-tb em{font-style:normal;font-size:7px;letter-spacing:.18em;color:#7fa8c8}
.bp-tb b{font-weight:400;font-size:9.5px;letter-spacing:.1em;color:#e6f4ff;white-space:nowrap}
/* 保存设置（二级页 / 模态）——颜色由蓝图主题接管 */
.save-dialog{background:linear-gradient(180deg,#0e3358,#0a2744);border:1.4px solid #8fc6e8b3;
  box-shadow:0 0 0 6px #02101f66,0 30px 70px #000a;color:#e6f4ff}
.save-head{border-bottom-color:#8fc6e859}
.save-head b{font-family:var(--font-title);letter-spacing:2px}
.save-head span{color:#9cc8e6}
.save-group{border-color:#8fc6e84d;background:#0a274466}
.save-label{color:#9cc8e6}
.save-opt{border-color:#8fc6e84d}
.save-opt i,.save-check i{color:#9cc8e6}
.save-foot{border-top-color:#8fc6e859}
.save-dialog .save-btn.ghost{background:transparent;color:#cfe6f8;border-color:#8fc6e873}
.save-dialog .save-btn.ghost:hover{border-color:#e6f4ff;color:#fff;background:#14476f}
"""

#: 蓝图主题的打印样式：白/米底档案纸 + 图号/页脚 + 手绘红圈 + 逐页分页。
BLUEPRINT_PRINT_CSS = """
/* ===== 打印 / PDF：C 档案袋纸张风格（白/米底 + 图号 + 页脚 + 手绘圈注） ===== */
@page{size:1600px 1000px;margin:0}
html,body{height:auto!important;overflow:visible!important;background:#fff!important}
body{
  background:#fff!important;color:#2B2418!important;
  -webkit-print-color-adjust:exact;print-color-adjust:exact;
}
.app{height:auto;display:block}
header{display:none!important}
main{position:static;display:block;min-height:0}
.page{
  position:relative!important;inset:auto!important;display:flex!important;
  flex-direction:column;width:1600px;height:1000px;padding:32px 34px 42px;
  overflow:hidden;background:#F6F1E3!important;color:#2B2418!important;
  page-break-after:always;break-after:page;border:0!important;
}
.page:last-child{page-break-after:auto;break-after:auto}
.page::before{border-color:#8A734C!important}
.page::after{border-color:#B7A483!important}
.pager,.side,.hint,.header-save,.save-btn,.save-modal,#debrief-src,#tip{display:none!important}
.nav-hint{display:none!important}
html{
  --bg:#F6F1E3;--panel:#FBF7EC;--panel2:#F2EBD8;--line:#B7A483;--line-strong:#8A734C;
  --text:#2B2418;--muted:#6B5B41;--muted2:#6B5B41;--accent:#8E2A24;--accent2:#8A5A2B;
  --danger:#A8322B;--ok:#2F6B3A;--header-bg:#F6F1E3;--header-border:#8A734C;
  --subline-code-bg:#F2EBD8;--btn-bg:#F2EBD8;--btn-fg:#4A3D2A;--btn-border:#B7A483;
  --btn-active-bg:#E8DCC0;--btn-active-fg:#2B2418;--chip-bg:#F2EBD8;--chip-border:#B7A483;
  --chip-fg:#6B5B41;--chip-b:#2B2418;--card-bg:#FBF7EC;--card-border:#B7A483;
  --note-bg:#F2EBD8;--note-border:#B7A483;--note-fg:#3A3023;--note-b:#2B2418;
  --badge-bg:#FBF7EC;--badge-fg:#4A3D2A;--legend-fg:#4A3D2A;--chart-label-fg:#2B2418;
  --input-bg:#FBF7EC;--avg-btn-bg:#8E2A24;--avg-btn-border:#6B211D;--avg-btn-fg:#fff;
  --avg-btn-hover-bg:#6B211D;--ghost-fg:#4A3D2A;--avg-msg:#8E2A24;--avg-msg-warn:#A8322B;
  --warn-bg:#F7ECD6;--warn-border:#C8A86A;--warn-fg:#6B431C;--pager-bg:#F6F1E3;
  --dot:#C9BFa8;--dot-hover:#8A7A63;--dot-ring:#8E2A2433;--hint-fg:#8A8578;
  --side-bg:#FBF7EC;--side-fg:#3A3023;--side-hover-bg:#F2EBD8;
  --ann-box:#F2EBD8;--ann-ink:#8E2A24;--ann-mark:#B0362A;
}
.bp-grid{background:
  repeating-linear-gradient(0deg,transparent 0 19px,#8A734C1f 19px 20px),
  repeating-linear-gradient(90deg,transparent 0 19px,#8A734C1f 19px 20px),
  repeating-linear-gradient(0deg,transparent 0 99px,#8A734C2e 99px 100px),
  repeating-linear-gradient(90deg,transparent 0 99px,#8A734C2e 99px 100px)!important}
.bp-foot{color:#6B5B41!important}
.bp-sheet{border-color:#B7A483!important;color:#2B2418!important}
.bp-tb{border-color:#B7A483!important;background:#FBF7EC!important}
.bp-tb span{border-color:#B7A483!important}
.bp-tb em{color:#8A734C!important}
.bp-tb b{color:#2B2418!important}
.bp-reg::before,.bp-reg::after{background:#8A734C!important}
.page-head{border-bottom-color:#B7A483!important}
.page-title::before{color:#8A734C!important}
.chart-wrap{background:#FBF7EC!important;border-color:#B7A483!important}
.card,.avgbar{background:#FBF7EC!important;border-color:#B7A483!important}
.badges span{background:#FBF7EC!important;border-color:#B7A483!important;color:#4A3D2A!important}
.legend,.page-sub,.page-right{color:#5C4F3A!important}
.legend .lg i{filter:grayscale(1) brightness(.55)}
.lg i.dashed{background-image:linear-gradient(90deg,#2B2418 0 60%,transparent 60% 100%)!important}
.avgbar .avg-k,.avgbar .avg-meta,.avgbar .avg-title{color:#5C4F3A!important}
.avgbar .avg-title b{color:#2B2418!important}
.avg-ctl{display:none!important}
/* 批注：屏幕层藏起来，只留手绘圈 + 打字机短注；图下批注对照表（legend）保留 */
.ann-screen{display:none!important}
.ann-print{display:inline!important}
body.no-annot .ann-print{display:none!important}
body.no-annot .ann-legend{display:none!important}
.ann-legend{color:#5C4F3A!important;border-top-color:#B7A483!important;font-size:12px!important}
.ann-legend .ai-h{color:#8A734C!important}
"""


@dataclass(frozen=True)
class Theme:
    """一套美术主题的完整配置。

    字段分三组：

    * **基础色板**：背景 / 面板 / 正文 / 次要文字 / 主色 / 辅色 / 告警 / 成功 / 分隔线；
    * **字体与形状**：中文字体栈、标题字体栈、数字等宽栈、圆角；
    * **图表调色板**：``chart`` 里的每个键都会写进模块级 :data:`COLORS`
      （曲线、网格、事件线、平均速度区间都从这里取色，不再散落硬编码）。

    其余细碎的 UI 颜色放在 ``css``（键名就是 CSS 变量名，不带 ``--``），
    ``extra_css`` / ``print_css`` 追加主题专属的纹理与打印样式。
    """

    key: str
    label: str
    desc: str
    mode: str
    bg: str
    panel: str
    panel2: str
    text: str
    muted: str
    accent: str
    accent2: str
    danger: str
    ok: str
    line: str
    #: 图表调色板（键名会同步到模块级 ``COLORS``）
    chart: Dict[str, str]
    font_ui: str
    font_title: str
    font_num: str
    radius: str
    radius_sm: str
    radius_xs: str
    pill: str
    #: 额外 CSS 变量（键名不带 ``--``）
    css: Dict[str, str] = field(default_factory=dict)
    #: 主题专属结构样式（纸纹 / 噪点 / 阴影 / 衬线标题……）
    extra_css: str = ""
    #: 主题专属打印覆盖（浅色主题在这里把背景钉成白色）
    print_css: str = ""
    #: 打印时的 SVG 颜色替换（暗色主题 → 纸上的墨色；只作用于 ``stroke``/``fill``
    #: 属性值，屏幕样式不受影响）。键 = ``chart`` 的键，值 = 打印色。
    print_chart: Dict[str, str] = field(default_factory=dict)


THEMES: Dict[str, Theme] = {
    # ------------------------------------------------------------------
    # 1) mfd：保留原有的深色 MFD 绿色荧光风格（对照组，默认主题）
    # ------------------------------------------------------------------
    "mfd": Theme(
        key="mfd",
        label="MFD 荧光",
        desc="深色座舱 MFD：冷蓝底 + 荧光绿/琥珀曲线，保留旧版观感。",
        mode="dark",
        bg="#0a0e13",
        panel="#0f151d",
        panel2="#121a24",
        text="#dde6f0",
        muted="#8ea1b4",
        accent="#4aa3ff",
        accent2="#ffb347",
        danger="#ff5c5c",
        ok="#4ade80",
        line="#1e2a36",
        chart={
            "ias": "#4aa3ff",
            "tas": "#ffb347",
            "mach": "#d97bff",
            "avg_speed": "#93a4b8",
            "alt": "#46d39a",
            "ralt": "#2f9e75",
            "es": "#ffd166",
            "des_pos": "#48d597",
            "des_neg": "#ff6b6b",
            "vy": "#7dd3fc",
            "aoa": "#f472b6",
            "ny": "#fbbf24",
            "pitch": "#60a5fa",
            "bank": "#ffb347",
            "roll": "#ff5c5c",
            "death": "#ff5c5c",
            "spawn": "#4ade80",
            "vehicle_change": "#60a5fa",
            "kill": "#2fd4c0",
            "other": "#93a4b8",
            "grid": "#ffffff12",
            "grid_zero": "#ffffff2a",
            "axis": "#8fa1b3",
            "border": "#ffffff14",
            "avg_band": "#6ea8ff",
            "avg_edge": "#9dc4ff",
            "invalid": "#9aa7b8",
            "death_region": "#ff5c5c",
            "vline": "#cbd5e1",
            "gap": "#94a7bd",
            "crosshair": "#e6eef7",
            "crosshair_stroke": "#0a0e13",
        },
        font_ui=(
            '"Segoe UI","Microsoft YaHei","PingFang SC","Hiragino Sans GB",'
            '"Noto Sans CJK SC",sans-serif'
        ),
        font_title=(
            '"Segoe UI","Microsoft YaHei","PingFang SC","Hiragino Sans GB",'
            '"Noto Sans CJK SC",sans-serif'
        ),
        font_num=(
            '"Segoe UI","Microsoft YaHei","PingFang SC","Hiragino Sans GB",'
            '"Noto Sans CJK SC",sans-serif'
        ),
        radius="10px",
        radius_sm="8px",
        radius_xs="6px",
        pill="999px",
        css={
            "header-bg": "linear-gradient(180deg,#0d141c,#0a0e13)",
            "subline-code-bg": "#16202b",
            "btn-fg": "#c3d2e1",
            "btn-hover-fg": "#ffffff",
            "btn-active-bg": "#12324f",
            "btn-active-fg": "#ffffff",
            "replay-fg": "#ffd79b",
            "replay-b": "#ffcf7a",
            "note-bg": "#1b2735",
            "note-border": "#2f4a66",
            "note-fg": "#bcd6ee",
            "note-b": "#e6f1ff",
            "pchip-bg": "#131c26",
            "pchip-fg": "#9fb4c8",
            "pchip-b": "#dbe7f3",
            "badge-bg": "#141d27",
            "badge-fg": "#bfcedd",
            "legend-fg": "#b8c7d6",
            "chart-label-fg": "#cfdcea",
            "input-bg": "#121a24",
            "avg-btn-bg": "#16406b",
            "avg-btn-border": "#2b6aa8",
            "avg-btn-fg": "#ffffff",
            "avg-btn-hover-bg": "#1b4f84",
            "ghost-fg": "#c3d2e1",
            "avg-msg": "#ffd79b",
            "avg-msg-warn": "#ffb4a8",
            "warn-bg": "#2c2113",
            "warn-border": "#6a4a1c",
            "warn-fg": "#ffd79b",
            "pager-bg": "#0b1118",
            "dot": "#2b3c4f",
            "dot-hover": "#5b7a99",
            "dot-ring": "#4aa3ff2e",
            "hint-fg": "#6d7f92",
            "side-bg": "#0d151ef2",
            "side-fg": "#bcd0e4",
            "side-hover-bg": "#12324f",
            "tip-bg": "#080c11f2",
            "tip-border": "#2e4054",
            "tip-shadow": "0 10px 30px #00000066",
            "dash-fg": "#ffffff",
            "avgdash-fg": "#cbd5e1",
            "avg-dash": "#6d7f92",
            "color-scheme": "dark",
            "print-bg": "#0a0e13",
            "print-fg": "#dde6f0",
            "print-header-bg": "#0a0e13",
        },
    ),
    # ------------------------------------------------------------------
    # 2) glass：玻璃座舱 EFIS（深底青蓝 + 琥珀辅色 + 细青网格 + 等宽数字）
    # ------------------------------------------------------------------
    "glass": Theme(
        key="glass",
        label="玻璃座舱 EFIS",
        desc="深空底 + 青蓝主色 + 琥珀告警色，细青网格、6px 圆角、等宽数字。",
        mode="dark",
        bg="#0b0f14",
        panel="#121a22",
        panel2="#0e1620",
        text="#e6edf3",
        muted="#9fb4c4",
        accent="#4cc9f0",
        accent2="#ffb703",
        danger="#ef476f",
        ok="#43d9ad",
        line="#1d3440",
        chart={
            "ias": "#4cc9f0",
            "tas": "#ffb703",
            "mach": "#c77dff",
            "avg_speed": "#8aa4b2",
            "alt": "#43d9ad",
            "ralt": "#2a9d8f",
            "es": "#ffd166",
            "des_pos": "#57e389",
            "des_neg": "#ef476f",
            "vy": "#72efdd",
            "aoa": "#ff9ebb",
            "ny": "#f9c74f",
            "pitch": "#8ecae6",
            "bank": "#ffb703",
            "roll": "#ef476f",
            "death": "#ef476f",
            "spawn": "#70e4a0",
            "vehicle_change": "#4cc9f0",
            "kill": "#22d3ee",
            "other": "#8aa4b2",
            "grid": "#4cc9f01a",
            "grid_zero": "#4cc9f040",
            "axis": "#7fa6b5",
            "border": "#4cc9f02e",
            "avg_band": "#4cc9f0",
            "avg_edge": "#a8e6ff",
            "invalid": "#6c8ea0",
            "death_region": "#ef476f",
            "vline": "#a7c6d4",
            "gap": "#8fa8ba",
            "crosshair": "#e6edf3",
            "crosshair_stroke": "#0b0f14",
        },
        font_ui=(
            '"Segoe UI","Microsoft YaHei UI","Bahnschrift","DIN Alternate",'
            '"Noto Sans CJK SC",sans-serif'
        ),
        font_title='"Bahnschrift","Segoe UI","Microsoft YaHei UI","Noto Sans CJK SC",sans-serif',
        font_num='Consolas,"Cascadia Mono","SF Mono","Roboto Mono",monospace',
        radius="6px",
        radius_sm="4px",
        radius_xs="4px",
        pill="999px",
        css={
            "header-bg": "linear-gradient(180deg,#101a22,#0b0f14)",
            "subline-code-bg": "#0e1620",
            "btn-fg": "#cfe3ec",
            "btn-hover-fg": "#ffffff",
            "btn-active-bg": "#0e3a4a",
            "btn-active-fg": "#e6f9ff",
            "replay-fg": "#ffcf6b",
            "replay-b": "#ffd98c",
            "note-bg": "#0e1a24",
            "note-border": "#2a4c5c",
            "note-fg": "#cfe3ec",
            "note-b": "#e6edf3",
            "pchip-bg": "#0f1a23",
            "pchip-fg": "#9fb4c4",
            "pchip-b": "#e6edf3",
            "badge-bg": "#0f1a23",
            "badge-fg": "#b7c9d6",
            "legend-fg": "#b7c9d6",
            "chart-label-fg": "#e6edf3",
            "input-bg": "#0e1620",
            "avg-btn-bg": "#0e3a4a",
            "avg-btn-border": "#2a6f86",
            "avg-btn-fg": "#dff6ff",
            "avg-btn-hover-bg": "#12506a",
            "ghost-fg": "#cfe3ec",
            "avg-msg": "#ffcf6b",
            "avg-msg-warn": "#ff9aa5",
            "warn-bg": "#1a1206",
            "warn-border": "#7a5a00",
            "warn-fg": "#ffcf6b",
            "pager-bg": "#080c10",
            "dot": "#1e3c48",
            "dot-hover": "#4cc9f0",
            "dot-ring": "#4cc9f03d",
            "hint-fg": "#6f8794",
            "side-bg": "#0b1218f2",
            "side-fg": "#cfe3ec",
            "side-hover-bg": "#0e3a4a",
            "tip-bg": "#050a0ef2",
            "tip-border": "#2a4c5c",
            "tip-shadow": "0 10px 30px #00000080",
            "dash-fg": "#e6edf3",
            "avgdash-fg": "#9fb4c4",
            "avg-dash": "#6f8794",
            "color-scheme": "dark",
            "print-bg": "#0b0f14",
            "print-fg": "#e6edf3",
            "print-header-bg": "#0b0f14",
            "chart-bg": "radial-gradient(120% 90% at 50% 0%,#4cc9f00d,transparent 62%)",
        },
        extra_css="""
header{box-shadow:inset 0 -1px 0 #4cc9f03d}
.page-head{border-bottom:1px solid #4cc9f024;padding-bottom:3px}
header h1,.page-title{letter-spacing:1.2px;font-weight:600}
.card{border-left:2px solid var(--accent);box-shadow:inset 0 1px 0 #ffffff0a}
.avgbar{box-shadow:inset 0 1px 0 #ffffff0a}
.legend .lg i{box-shadow:0 0 6px #4cc9f033}
.badges span{border-color:#4cc9f02e}
.pager{box-shadow:0 -1px 0 #4cc9f01a}
""",
    ),
    # ------------------------------------------------------------------
    # 3) manual：军机维护手册（暖纸底 + 墨色 + 橙强调 + 衬线标题 + 纸纹）
    # ------------------------------------------------------------------
    "manual": Theme(
        key="manual",
        label="军机维护手册",
        desc="暖纸底、墨色正文、衬线标题、橙色强调，细横线与图号页脚，可打印。",
        mode="light",
        bg="#f4f1e8",
        panel="#faf7ee",
        panel2="#efeade",
        text="#1c1c1c",
        muted="#5c574b",
        accent="#c8551a",
        accent2="#8a5a2b",
        danger="#a31d1d",
        ok="#2f6b3a",
        line="#d9d2c0",
        chart={
            "ias": "#1c1c1c",
            "tas": "#c8551a",
            "mach": "#6a3fa0",
            "avg_speed": "#8a8578",
            "alt": "#2f4a3a",
            "ralt": "#6b7f63",
            "es": "#c8551a",
            "des_pos": "#3c6e47",
            "des_neg": "#a31d1d",
            "vy": "#2b4e66",
            "aoa": "#7a3e5c",
            "ny": "#8a5a2b",
            "pitch": "#1f4fa8",
            "bank": "#b5651d",
            "roll": "#a3193f",
            "death": "#a31d1d",
            "spawn": "#2f6b3a",
            "vehicle_change": "#2b4e66",
            "kill": "#1f6f7a",
            "other": "#5c574b",
            "grid": "#1c1c1c1f",
            "grid_zero": "#1c1c1c33",
            "axis": "#4a453a",
            "border": "#1c1c1c26",
            "avg_band": "#c8551a",
            "avg_edge": "#e08a50",
            "invalid": "#8a8578",
            "death_region": "#a31d1d",
            "vline": "#8a7a63",
            "gap": "#9a9488",
            "crosshair": "#7a3b12",
            "crosshair_stroke": "#f4f1e8",
        },
        font_ui=(
            '"Microsoft YaHei UI","Microsoft YaHei",SimHei,"Segoe UI",'
            '"Noto Sans CJK SC",sans-serif'
        ),
        font_title=(
            '"Source Han Serif SC","Source Han Serif","Noto Serif CJK SC",'
            'SimSun,"Songti SC",Georgia,serif'
        ),
        font_num='Consolas,"Courier New","SimSun",monospace',
        radius="2px",
        radius_sm="2px",
        radius_xs="2px",
        pill="2px",
        css={
            "header-bg": "linear-gradient(180deg,#faf7ee,#f4f1e8)",
            "subline-code-bg": "#efeade",
            "btn-fg": "#3a352b",
            "btn-hover-fg": "#1c1c1c",
            "btn-active-bg": "#f0dccb",
            "btn-active-fg": "#8f3a10",
            "replay-fg": "#8a5a2b",
            "replay-b": "#6b431c",
            "note-bg": "#efeade",
            "note-border": "#c9bfa8",
            "note-fg": "#3a352b",
            "note-b": "#1c1c1c",
            "pchip-bg": "#efeade",
            "pchip-fg": "#5c574b",
            "pchip-b": "#1c1c1c",
            "badge-bg": "#efeade",
            "badge-fg": "#3a352b",
            "legend-fg": "#3a352b",
            "chart-label-fg": "#1c1c1c",
            "input-bg": "#faf7ee",
            "avg-btn-bg": "#c8551a",
            "avg-btn-border": "#a8440f",
            "avg-btn-fg": "#ffffff",
            "avg-btn-hover-bg": "#a8440f",
            "ghost-fg": "#3a352b",
            "avg-msg": "#a8440f",
            "avg-msg-warn": "#a31d1d",
            "warn-bg": "#f7ecd6",
            "warn-border": "#c8a86a",
            "warn-fg": "#6b431c",
            "pager-bg": "#f4f1e8",
            "dot": "#c9bfa8",
            "dot-hover": "#8a7a63",
            "dot-ring": "#c8551a33",
            "hint-fg": "#8a8578",
            "side-bg": "#faf7eef2",
            "side-fg": "#3a352b",
            "side-hover-bg": "#efeade",
            "tip-bg": "#fffdf6f7",
            "tip-border": "#c9bfa8",
            "tip-shadow": "0 8px 20px #0000001f",
            "dash-fg": "#1c1c1c",
            "avgdash-fg": "#5c574b",
            "avg-dash": "#8a8578",
            "color-scheme": "light",
            "print-bg": "#ffffff",
            "print-fg": "#111111",
            "print-header-bg": "#ffffff",
        },
        extra_css="""
body{
  background-color:#f4f1e8;
  background-image:repeating-linear-gradient(0deg,#00000004 0 1px,transparent 1px 29px),
    radial-gradient(120% 80% at 50% 0%,#ffffff66,transparent 55%);
}
header{border-bottom:3px double #1c1c1c}
header h1{font-family:var(--font-title);font-weight:600;letter-spacing:.6px}
.page-head{border-bottom:1px solid #1c1c1c;padding-bottom:3px;align-items:flex-end}
.page-title{font-family:var(--font-title);font-weight:600;letter-spacing:.4px;font-size:17px}
body{counter-reset:fig}
section.page{counter-increment:fig}
.page-title::before{content:"图 " counter(fig) " · ";font-family:var(--font-num);font-weight:400;
  color:var(--muted);font-size:13px}
.card{box-shadow:none;border-color:#cfc7b4;background:#faf7ee}
.card b{font-family:var(--font-num)}
.avgbar{border-color:#cfc7b4;background:#faf7ee;border-radius:2px}
.legend{color:#3a352b}
.badges span{background:#efeade;border-color:#cfc7b4}
.pager{border-top:1px solid #1c1c1c}
.hint{display:none}
.pager::before{content:"WtHud 飞行日志分析 · 维护手册版";position:absolute;left:22px;
  font-size:10.5px;color:#8a8578;letter-spacing:.6px}
.pager::after{content:"仅供技术复盘参考";position:absolute;right:16px;
  font-size:10.5px;color:#8a8578}
.tip{border-radius:2px}
.chart-wrap{border-bottom:1px solid #00000012}
""",
        print_css="""
body{background:#fff!important;background-image:none!important;color:#111!important}
header{background:#fff!important;border-bottom:2px solid #111!important}
.page{border-top:1px solid #ccc!important}
.card,.badges span,.avgbar{background:#fff!important;border-color:#bbb!important}
.pager::before,.pager::after{content:none!important}
""",
    ),
    # ------------------------------------------------------------------
    # 4) tactical：战术复盘（橄榄绿 + 卡其 + 沙色文字 + 胶片噪点 + 等宽标题）
    # ------------------------------------------------------------------
    "tactical": Theme(
        key="tactical",
        label="战术复盘",
        desc="橄榄绿底、卡其线条、沙色沙盘文字，半透明胶片图表面板 + 弱噪点，事件红调标注。",
        mode="dark",
        bg="#2f3a2a",
        panel="#37432f",
        panel2="#404d36",
        text="#e6dcc0",
        muted="#c2b797",
        accent="#c9bc9a",
        accent2="#d9a441",
        danger="#e06a5a",
        ok="#9fbf6a",
        line="#5a6748",
        chart={
            "ias": "#cfe8f2",
            "tas": "#d9a441",
            "mach": "#b9d18a",
            "avg_speed": "#a9a48a",
            "alt": "#9fbf6a",
            "ralt": "#6e8b54",
            "es": "#e0c060",
            "des_pos": "#a8c86a",
            "des_neg": "#e06a5a",
            "vy": "#a9c7c1",
            "aoa": "#d98f7a",
            "ny": "#e8b04a",
            "pitch": "#9fc0d8",
            "bank": "#e8b04a",
            "roll": "#e06a5a",
            "death": "#e06a5a",
            "spawn": "#d98f7a",
            "vehicle_change": "#d9a441",
            "kill": "#63b7a6",
            "other": "#c9bc9a",
            "grid": "#c9bc9a1f",
            "grid_zero": "#c9bc9a3d",
            "axis": "#c9bc9a",
            "border": "#c9bc9a2e",
            "avg_band": "#c9bc9a",
            "avg_edge": "#e6dcc0",
            "invalid": "#8a8a6f",
            "death_region": "#e06a5a",
            "vline": "#d8cba6",
            "gap": "#a9a48a",
            "crosshair": "#f0e6c8",
            "crosshair_stroke": "#2f3a2a",
        },
        font_ui='"Microsoft YaHei UI","Segoe UI","Noto Sans CJK SC",sans-serif',
        font_title='Consolas,"Cascadia Mono","Courier New","Microsoft YaHei UI",monospace',
        font_num='Consolas,"Cascadia Mono","Courier New",monospace',
        radius="3px",
        radius_sm="2px",
        radius_xs="2px",
        pill="3px",
        css={
            "header-bg": "linear-gradient(180deg,#37432f,#2f3a2a)",
            "subline-code-bg": "#404d36",
            "btn-fg": "#d8cba6",
            "btn-hover-fg": "#f2e8c8",
            "btn-active-bg": "#4a4530",
            "btn-active-fg": "#f2e8c8",
            "replay-fg": "#e8b04a",
            "replay-b": "#f0c060",
            "note-bg": "#3d4933",
            "note-border": "#6e7a54",
            "note-fg": "#e6dcc0",
            "note-b": "#f2e8c8",
            "pchip-bg": "#3a4631",
            "pchip-fg": "#b3a887",
            "pchip-b": "#f2e8c8",
            "badge-bg": "#3a4631",
            "badge-fg": "#d8cba6",
            "legend-fg": "#d8cba6",
            "chart-label-fg": "#f2e8c8",
            "input-bg": "#3a4631",
            "avg-btn-bg": "#4a4530",
            "avg-btn-border": "#8a7c4e",
            "avg-btn-fg": "#f2e8c8",
            "avg-btn-hover-bg": "#5a5338",
            "ghost-fg": "#d8cba6",
            "avg-msg": "#e8b04a",
            "avg-msg-warn": "#e06a5a",
            "warn-bg": "#463a26",
            "warn-border": "#8a6a34",
            "warn-fg": "#e8b04a",
            "pager-bg": "#2a3426",
            "dot": "#5a6748",
            "dot-hover": "#c9bc9a",
            "dot-ring": "#c9bc9a40",
            "hint-fg": "#8f8a6f",
            "side-bg": "#2f3a2af0",
            "side-fg": "#e6dcc0",
            "side-hover-bg": "#4a4530",
            "tip-bg": "#20281cef",
            "tip-border": "#6e7a54",
            "tip-shadow": "0 10px 26px #00000099",
            "dash-fg": "#f2e8c8",
            "avgdash-fg": "#d8cba6",
            "avg-dash": "#8f8a6f",
            "color-scheme": "dark",
            "print-bg": "#2f3a2a",
            "print-fg": "#e6dcc0",
            "print-header-bg": "#2f3a2a",
            "chart-bg": "rgba(233,223,190,0.045)",
            "chart-ring": "inset 0 0 0 1px #c9bc9a33",
        },
        extra_css="""
body{background-image:url("data:image/svg+xml;utf8,<svg xmlns='http://www.w3.org/2000/svg' width='140' height='140'><filter id='n'><feTurbulence type='fractalNoise' baseFrequency='0.9' numOctaves='2' stitchTiles='stitch'/><feColorMatrix type='saturate' values='0'/></filter><rect width='140' height='140' filter='url(%23n)' opacity='0.055'/></svg>")}
header{border-bottom:2px solid #c9bc9a4d;box-shadow:inset 0 -1px 0 #00000033}
header h1,.page-title{font-family:var(--font-title);letter-spacing:2px;font-weight:600}
.page-title{font-size:15px}
.page-head{border-bottom:1px dashed #c9bc9a33;padding-bottom:3px}
.card{background:rgba(201,188,154,.07);border:1px solid #c9bc9a40;border-left:2px solid #c9bc9a66}
.card b{font-family:var(--font-num);color:#f2e8c8}
.avgbar{background:rgba(201,188,154,.06)}
.badges span{background:rgba(201,188,154,.08);border-color:#c9bc9a33}
.chart-wrap{padding:2px}
.tip{border-radius:2px}
""",
        print_css="""
body{background:#2f3a2a!important;background-image:none!important}
header{background:#2f3a2a!important}
.card{background:rgba(201,188,154,.09)!important}
""",
    ),
    # ------------------------------------------------------------------
    # 5) clean：浅色分析仪表盘（白卡片 + 蓝色强调 + 阴影，可读性优先）
    # ------------------------------------------------------------------
    "clean": Theme(
        key="clean",
        label="浅色分析仪表盘",
        desc="白/浅灰卡片、蓝色强调、圆角阴影，白天看最省眼，可打印。",
        mode="light",
        bg="#f5f7fa",
        panel="#ffffff",
        panel2="#f1f4f9",
        text="#1f2937",
        muted="#6b7280",
        accent="#2563eb",
        accent2="#f59e0b",
        danger="#dc2626",
        ok="#059669",
        line="#e5e7eb",
        chart={
            "ias": "#2563eb",
            "tas": "#b45309",
            "mach": "#7c3aed",
            "avg_speed": "#6b7280",
            "alt": "#059669",
            "ralt": "#0d9488",
            "es": "#2563eb",
            "des_pos": "#16a34a",
            "des_neg": "#dc2626",
            "vy": "#0891b2",
            "aoa": "#db2777",
            "ny": "#a16207",
            "pitch": "#1d4ed8",
            "bank": "#d97706",
            "roll": "#dc2626",
            "death": "#dc2626",
            "spawn": "#16a34a",
            "vehicle_change": "#3b82f6",
            "kill": "#0d9488",
            "other": "#6b7280",
            "grid": "#1f293714",
            "grid_zero": "#1f293733",
            "axis": "#6b7280",
            "border": "#1f29371f",
            "avg_band": "#2563eb",
            "avg_edge": "#60a5fa",
            "invalid": "#6b7280",
            "death_region": "#dc2626",
            "vline": "#64748b",
            "gap": "#9ca3af",
            "crosshair": "#1f2937",
            "crosshair_stroke": "#ffffff",
        },
        font_ui=(
            '"Segoe UI","Microsoft YaHei UI","Microsoft YaHei","PingFang SC",'
            '"Noto Sans CJK SC",sans-serif'
        ),
        font_title=(
            '"Segoe UI","Microsoft YaHei UI","Microsoft YaHei","PingFang SC",'
            '"Noto Sans CJK SC",sans-serif'
        ),
        font_num='"Segoe UI","Microsoft YaHei UI",sans-serif',
        radius="12px",
        radius_sm="10px",
        radius_xs="8px",
        pill="999px",
        css={
            "header-bg": "#ffffff",
            "subline-code-bg": "#f1f4f9",
            "btn-fg": "#374151",
            "btn-hover-fg": "#111827",
            "btn-active-bg": "#dbeafe",
            "btn-active-fg": "#1d4ed8",
            "replay-fg": "#b45309",
            "replay-b": "#92400e",
            "note-bg": "#f1f5ff",
            "note-border": "#bfdbfe",
            "note-fg": "#1e3a8a",
            "note-b": "#1e3a8a",
            "pchip-bg": "#f3f4f6",
            "pchip-fg": "#4b5563",
            "pchip-b": "#111827",
            "badge-bg": "#f3f4f6",
            "badge-fg": "#4b5563",
            "legend-fg": "#4b5563",
            "chart-label-fg": "#111827",
            "input-bg": "#ffffff",
            "avg-btn-bg": "#2563eb",
            "avg-btn-border": "#1d4ed8",
            "avg-btn-fg": "#ffffff",
            "avg-btn-hover-bg": "#1d4ed8",
            "ghost-fg": "#374151",
            "avg-msg": "#b45309",
            "avg-msg-warn": "#b91c1c",
            "warn-bg": "#fffbeb",
            "warn-border": "#fcd34d",
            "warn-fg": "#92400e",
            "card-bg": "#ffffff",
            "card-border": "#e5e7eb",
            "card-shadow": "0 1px 2px #0f172a0f",
            "pager-bg": "#ffffff",
            "dot": "#cbd5e1",
            "dot-hover": "#64748b",
            "dot-ring": "#2563eb26",
            "hint-fg": "#94a3b8",
            "side-bg": "#fffffff2",
            "side-fg": "#374151",
            "side-hover-bg": "#eff6ff",
            "tip-bg": "#fffffff7",
            "tip-border": "#e5e7eb",
            "tip-shadow": "0 12px 28px #0f172a26",
            "dash-fg": "#1f2937",
            "avgdash-fg": "#6b7280",
            "avg-dash": "#94a3b8",
            "color-scheme": "light",
            "print-bg": "#ffffff",
            "print-fg": "#111111",
            "print-header-bg": "#ffffff",
        },
        extra_css="""
header{box-shadow:0 1px 3px #0f172a0f}
.page-head{border-bottom:1px solid #eef2f7;padding-bottom:3px}
.card{box-shadow:var(--card-shadow)}
.avgbar{box-shadow:0 1px 2px #0f172a0d}
.pager{box-shadow:0 -1px 2px #0f172a0a}
""",
        print_css="""
body{background:#fff!important}
header{background:#fff!important}
.card,.avgbar{box-shadow:none!important}
""",
    ),
    # ------------------------------------------------------------------
    # 6) blueprint：A 工程蓝图（默认）——深蓝图纸 / 坐标纸网格 / 图框 / 图号 /
    #    页脚 SHEET n/9 / 右下角标题栏；红色审阅标记，打印为白/米底档案纸。
    # ------------------------------------------------------------------
    "blueprint": Theme(
        key="blueprint",
        label="工程蓝图",
        desc="深蓝图纸底 + 坐标纸网格 + 图框/图号/标题栏，红色审阅标记；打印为白/米底档案纸。",
        mode="dark",
        bg="#0B2A4A",
        panel="#0D3157",
        panel2="#0E3A63",
        text="#E6F4FF",
        muted="#9CC8E6",
        accent="#8FC6E8",
        accent2="#FFD166",
        danger="#FF5A3C",
        ok="#7EE0B8",
        line="#8FC6E8",
        chart={
            "ias": "#5FE3FF",
            "tas": "#FFC24A",
            "mach": "#E39BFF",
            "avg_speed": "#A9B8C4",
            "alt": "#E6F4FF",
            "ralt": "#9CC8E6",
            "es": "#8FD3FF",
            "des_pos": "#7EE0B8",
            "des_neg": "#FF8A70",
            "vy": "#A8DAFF",
            "aoa": "#FFD166",
            "ny": "#CFE9FC",
            "pitch": "#7CC7FF",
            "bank": "#FFB347",
            "roll": "#FF7AA2",
            "death": "#FF5A3C",
            "spawn": "#7EE0B8",
            "vehicle_change": "#8FD3FF",
            "kill": "#38D9C9",
            "other": "#9CC8E6",
            "grid": "#8FC6E81A",
            "grid_zero": "#8FC6E840",
            "axis": "#9CC8E6",
            "border": "#8FC6E866",
            "avg_band": "#8FD3FF",
            "avg_edge": "#CFE9FC",
            "invalid": "#6E93B5",
            "death_region": "#FF5A3C",
            "vline": "#CFE9FC",
            "gap": "#8FB6D4",
            "crosshair": "#E6F4FF",
            "crosshair_stroke": "#0B2A4A",
        },
        font_ui=(
            '"Microsoft YaHei UI","Microsoft YaHei","Segoe UI",'
            '"Noto Sans CJK SC",sans-serif'
        ),
        font_title=(
            '"Bahnschrift SemiCondensed","Bahnschrift","Arial Narrow",'
            '"Microsoft YaHei UI","Microsoft YaHei",sans-serif'
        ),
        font_num='Consolas,"Cascadia Mono","Courier New",monospace',
        radius="0px",
        radius_sm="0px",
        radius_xs="0px",
        pill="0px",
        css={
            "header-bg": "linear-gradient(180deg,#0c3054,#092646)",
            "header-border": "#8fc6e8b3",
            "subline-code-bg": "#0e3a63",
            "btn-fg": "#cfe6f8",
            "btn-hover-fg": "#ffffff",
            "btn-active-bg": "#F2EBD8",
            "btn-active-fg": "#0A2540",
            "replay-fg": "#FFD166",
            "replay-b": "#FFE3A3",
            "note-bg": "#0e3a63",
            "note-border": "#8fc6e859",
            "note-fg": "#d8ecfd",
            "note-b": "#ffffff",
            "pchip-bg": "#0d3357",
            "pchip-fg": "#9CC8E6",
            "pchip-b": "#E6F4FF",
            "badge-bg": "#0d3357",
            "badge-fg": "#cfe6f8",
            "legend-fg": "#cfe6f8",
            "chart-label-fg": "#E6F4FF",
            "input-bg": "#0e3a63",
            "avg-btn-bg": "#F2EBD8",
            "avg-btn-border": "#F2EBD8",
            "avg-btn-fg": "#0A2540",
            "avg-btn-hover-bg": "#ffffff",
            "ghost-fg": "#cfe6f8",
            "avg-msg": "#FFD166",
            "avg-msg-warn": "#FF8A70",
            "warn-bg": "#123a53",
            "warn-border": "#FFD166",
            "warn-fg": "#FFE3A3",
            "card-bg": "#0c2e50",
            "card-border": "#8fc6e859",
            "pager-bg": "#08213a",
            "pager-border": "#8fc6e859",
            "dot": "#25517a",
            "dot-hover": "#8fc6e8",
            "dot-ring": "#8fc6e83d",
            "hint-fg": "#7fa8c8",
            "side-bg": "#0a2744e6",
            "side-fg": "#cfe6f8",
            "side-hover-bg": "#14476f",
            "tip-bg": "#08213af7",
            "tip-border": "#8fc6e88c",
            "tip-fg": "#E6F4FF",
            "tip-strong": "#ffffff",
            "tip-shadow": "0 10px 30px #00000099",
            "dash-fg": "#E6F4FF",
            "avgdash-fg": "#9CC8E6",
            "avg-dash": "#7fa8c8",
            "color-scheme": "dark",
            "print-bg": "#F6F1E3",
            "print-fg": "#2B2418",
            "print-header-bg": "#F6F1E3",
            "ann-box": "#DCEBF5",
            "ann-ink": "#0A2540",
            "ann-mark": "#FF5A3C",
        },
        extra_css=BLUEPRINT_CSS,
        print_css=BLUEPRINT_PRINT_CSS,
        print_chart={
            "ias": "#2B2418",
            "tas": "#5C4F3A",
            "mach": "#4A3D2A",
            "avg_speed": "#8A8578",
            "alt": "#2B2418",
            "ralt": "#6B5B41",
            "es": "#3A3226",
            "des_pos": "#2F6B3A",
            "des_neg": "#A8322B",
            "vy": "#3A4A5A",
            "aoa": "#7A3E5C",
            "ny": "#3A3226",
            "pitch": "#2B4E66",
            "bank": "#7A5A2B",
            "roll": "#A8322B",
            "death": "#A8322B",
            "spawn": "#2F6B3A",
            "vehicle_change": "#2B4E66",
            "kill": "#1F6F7A",
            "other": "#6B5B41",
            "grid": "#DCD0B6",
            "grid_zero": "#C3B294",
            "axis": "#6B5B41",
            "border": "#B7A483",
            "avg_band": "#B0362A",
            "avg_edge": "#B0362A",
            "invalid": "#B7A483",
            "death_region": "#A8322B",
            "vline": "#A8967A",
            "gap": "#B7A483",
            "crosshair": "#2B2418",
        },
    ),
}

#: 主题顺序（CLI ``--theme`` 的 choices 与对比预览的顺序都按这个来）。
THEME_KEYS: Tuple[str, ...] = ("blueprint", "glass", "manual", "tactical", "clean", "mfd")

#: 当前生效主题（``render_report`` 会在渲染前应用；主题只影响颜色配置，不影响数据结构）。
ACTIVE_THEME: Theme = THEMES[DEFAULT_THEME]

#: 图表调色板（可变字典：渲染每个主题前由 :func:`_apply_theme` 原地更新）。
#: 面板配置 / SVG 在**调用时**读它，所以不需要把主题参数一路传进每个画图函数。
COLORS: Dict[str, str] = dict(THEMES[DEFAULT_THEME].chart)

EVENT_COLORS: Dict[str, str] = {
    "death": COLORS["death"],
    "spawn": COLORS["spawn"],
    "vehicle_change": COLORS["vehicle_change"],
    "kill": COLORS["kill"],
    "end": COLORS["other"],
}

#: **曲线线型（dash / width）集中定义**。这是「语义」而不是「主题」：同一页上的每条线
#: 都必须「颜色 + 线型」双编码，所以 dash/线宽对所有主题固定，颜色才在各主题的
#: ``Theme.chart`` 里分别定义（``ias`` / ``tas`` / ``mach`` / ``avg_speed`` /
#: ``pitch`` / ``bank`` / ``roll``）。
#:
#: * ``dash=None`` = 实线；其余值是 SVG ``stroke-dasharray``（单位 px）；
#: * 速度页：IAS 实线 / TAS 长虚线 / Mach 点线 / 平均速度 短虚线 + 中性灰；
#: * 姿态页：Pitch 实线（左轴）/ Bank 长虚线 / Roll rate 点线（右轴，与 Bank 共轴）；
#: * 打印态（blueprint 打印）颜色换成墨色，靠这里的 dash 继续区分。
LINE_STYLES: Dict[str, Dict[str, Any]] = {
    "ias": {"dash": None, "width": 2.2},
    "tas": {"dash": "8 4", "width": 2.0},
    "mach": {"dash": "2 3", "width": 1.8},
    "avg_speed": {"dash": "4 4", "width": 1.4},
    "pitch": {"dash": None, "width": 2.2},
    "bank": {"dash": "8 4", "width": 2.0},
    "roll": {"dash": "2 3", "width": 1.8},
}

#: ``_series()`` 里 ``dash=True`` 的旧写法等价于这个 dash（雷达高度等次要曲线）。
DEFAULT_DASH = "5 4"


def line_style(key: str) -> Dict[str, Any]:
    """取某条曲线的线型配置（dash / width）；未登记 = 实线 1.7 px。"""
    style = LINE_STYLES.get(key)
    if style is None:
        return {"dash": None, "width": 1.7}
    return {"dash": style.get("dash"), "width": style.get("width", 1.7)}


def get_theme(name: Optional[str] = None) -> Theme:
    """按名字取主题；``None`` / 空串取默认主题；未知主题抛 ``ValueError``。"""
    key = (name or "").strip().lower() or DEFAULT_THEME
    theme = THEMES.get(key)
    if theme is None:
        raise ValueError(
            "未知主题 %r；可选：%s" % (name, " / ".join(THEME_KEYS))
        )
    return theme


def _apply_theme(theme: Theme) -> Theme:
    """把主题的图表调色板写进模块级 ``COLORS`` / ``EVENT_COLORS``。

    ``report.py`` 里所有 SVG 片段都在调用时读 ``COLORS``，因此渲染入口
    （:func:`render_report`）先应用主题即可；不需要把主题对象层层传参。
    这是单进程渲染，不做并发保护。
    """
    global ACTIVE_THEME
    ACTIVE_THEME = theme
    COLORS.clear()
    COLORS.update(theme.chart)
    EVENT_COLORS.update(
        {
            "death": COLORS["death"],
            "spawn": COLORS["spawn"],
            "vehicle_change": COLORS["vehicle_change"],
            "kill": COLORS["kill"],
            "end": COLORS["other"],
        }
    )
    return theme


# ---------------------------------------------------------------------------
# 限速（Vne）读取：与 HUD 告警同一套语义
# ---------------------------------------------------------------------------
#: 解析优先级（镜像 ``wt-hud/src/WarThunderHud/Hud/EffectiveSettings.cs`` + ``SettingScopes``）：
#:
#: 1. ``hud-config.json`` 的 ``perVehicleOverrides[vehicle]["row.overspeedIas"]``
#: 2. ``airframes.json`` 的 ``[vehicle].overspeedIas``
#: 3. ``hud-config.json`` 的全局 ``overspeedKmh``
#:
#: 值 <= 0 视为"没有记录"，继续往下找；两个文件都读不到时返回空
#: :class:`SpeedLimits`（渲染时跳过限速批注，不报错）。


def _read_json_object(path: str) -> Optional[Dict[str, Any]]:
    """读一个 JSON 对象；文件缺失 / 坏 JSON / 顶层不是对象时返回 ``None``。"""
    try:
        with open(path, "r", encoding="utf-8-sig") as fh:
            data = json.load(fh)
    except (OSError, ValueError, UnicodeError):
        return None
    return data if isinstance(data, dict) else None


def _positive_float(value: Any) -> Optional[float]:
    """``>0`` 的有限数字；其余（None / 0 / 负数 / 布尔 / 字符串）返回 ``None``。"""
    if isinstance(value, bool):
        return None
    try:
        number = float(value)
    except (TypeError, ValueError):
        return None
    if not math.isfinite(number) or number <= 0:
        return None
    return number


@dataclass(frozen=True)
class SpeedLimits:
    """按机型解析出的限速（Vne IAS，km/h）与来源说明。"""

    #: 小写机型 → (限速 km/h, 来源说明)
    by_vehicle: Dict[str, Tuple[float, str]] = field(default_factory=dict)
    #: ``hud-config.json`` 的全局 ``overspeedKmh``（没有就是 ``None``）
    global_vne_kmh: Optional[float] = None
    #: 读到的配置目录（诊断用）
    hud_dir: str = ""
    #: ``hud-config.json`` 里的玩家昵称（复盘卡「PILOT 飞行员」用，空则为空）
    player_name: str = ""
    #: 是否由 ``--vne-kmh`` 手动指定
    manual: bool = False
    #: 读取过程中的提示（缺文件 / 坏 JSON）
    notes: Tuple[str, ...] = ()

    def vne_for(self, vehicle: Optional[str]) -> Tuple[Optional[float], str]:
        """取某机型的限速；返回 ``(km/h, 来源)``，没有记录时 ``(None, "")``。"""
        key = (vehicle or "").strip().lower()
        if key and key in self.by_vehicle:
            return self.by_vehicle[key]
        if self.global_vne_kmh is not None:
            return self.global_vne_kmh, "hud-config.json 全局 overspeedKmh"
        return None, ""

    def is_empty(self) -> bool:
        return not self.by_vehicle and self.global_vne_kmh is None


def manual_speed_limits(vne_kmh: float) -> SpeedLimits:
    """``--vne-kmh`` 手动指定：所有机型都用这个值。"""
    value = _positive_float(vne_kmh)
    if value is None:
        return SpeedLimits(notes=("--vne-kmh 不是正数，已忽略",))
    return SpeedLimits(
        global_vne_kmh=value,
        manual=True,
        notes=("--vne-kmh 手动指定 %.0f km/h" % value,),
    )


def _hud_config_candidates(hud_dir: Optional[str] = None) -> List[str]:
    """候选配置目录：参数指定 → exe 目录（冻结时）→ 本工具目录 → 其父目录。"""
    dirs: List[str] = []
    if hud_dir:
        dirs.append(hud_dir)
    if getattr(sys, "frozen", False):
        dirs.append(os.path.dirname(os.path.abspath(sys.executable)))
    here = os.path.dirname(os.path.abspath(__file__))
    dirs.append(here)
    dirs.append(os.path.dirname(here))
    out: List[str] = []
    seen = set()
    for directory in dirs:
        directory = os.path.abspath(directory)
        if directory in seen or not os.path.isdir(directory):
            continue
        seen.add(directory)
        out.append(directory)
    return out


def load_speed_limits(hud_dir: Optional[str] = None) -> SpeedLimits:
    """读与 FlightLog.exe 同目录的 ``hud-config.json`` / ``airframes.json``。

    * 先把 ``airframes.json`` 的 per-aircraft ``overspeedIas`` 当基础表；
    * 再用 ``hud-config.json`` 的 ``perVehicleOverrides`` 覆盖（优先级更高）；
    * 全局 ``overspeedKmh`` 作为兜底（机型没记录时按 HUD 语义使用它）。

    任何一步读不到都只是跳过（写进 ``notes``），不会抛异常。
    """
    notes: List[str] = []
    chosen = ""
    for directory in _hud_config_candidates(hud_dir):
        if os.path.isfile(os.path.join(directory, "hud-config.json")):
            chosen = directory
            break
    if not chosen:
        return SpeedLimits(notes=("未找到 hud-config.json（限速批注跳过）",))

    by_vehicle: Dict[str, Tuple[float, str]] = {}

    airframes = _read_json_object(os.path.join(chosen, "airframes.json"))
    if airframes is None:
        notes.append("airframes.json 缺失或无法解析（只用 hud-config.json）")
    else:
        for vehicle, profile in airframes.items():
            if not isinstance(profile, dict):
                continue
            value = _positive_float(profile.get("overspeedIas"))
            if value is not None:
                by_vehicle[str(vehicle).strip().lower()] = (
                    value,
                    "airframes.json[%s].overspeedIas" % vehicle,
                )

    config = _read_json_object(os.path.join(chosen, "hud-config.json"))
    global_vne: Optional[float] = None
    player_name = ""
    if config is None:
        notes.append("hud-config.json 无法解析（只用 airframes.json）")
    else:
        player_name = str(config.get("playerName") or "").strip()
        global_vne = _positive_float(config.get("overspeedKmh"))
        if global_vne is None:
            global_vne = _positive_float(config.get("overspeedIas"))
        overrides = config.get("perVehicleOverrides")
        if isinstance(overrides, dict):
            for vehicle, mapping in overrides.items():
                if not isinstance(mapping, dict):
                    continue
                value = _positive_float(mapping.get("row.overspeedIas"))
                if value is None:
                    value = _positive_float(mapping.get("overspeedIas"))
                if value is None:
                    continue
                by_vehicle[str(vehicle).strip().lower()] = (
                    value,
                    "hud-config.json perVehicleOverrides[%s].row.overspeedIas" % vehicle,
                )
    return SpeedLimits(
        by_vehicle=by_vehicle,
        global_vne_kmh=global_vne,
        hud_dir=chosen,
        player_name=player_name,
        notes=tuple(notes),
    )


# ---------------------------------------------------------------------------
# 审阅批注：自动计算每页要圈的点（屏幕 = 十字/方框；打印 = 手绘双圈）
# ---------------------------------------------------------------------------


def _tc_text(seconds: float, tenths: bool = True) -> str:
    """任务时间码 ``T+mm:ss.d``。"""
    value = max(0.0, float(seconds))
    minutes = int(value // 60)
    rest = value - minutes * 60
    if tenths:
        if rest >= 59.95:
            minutes += 1
            rest = 0.0
        text = "%04.1f" % rest
    else:
        whole = int(round(rest))
        if whole >= 60:
            minutes += 1
            whole -= 60
        text = "%02d" % whole
    return "T+%02d:%s" % (minutes, text)


def _tc_text_floor(seconds: float) -> str:
    """任务时间码 ``T+mm:ss``（**向零截断**，不四舍五入）。

    中断 legend 用截断：空洞从 19.6 s 开始就写 ``T+00:19``（和用户实测对齐），
    写成 ``T+00:20`` 会让人以为空洞发生在第 20 秒整。
    """
    value = max(0.0, float(seconds))
    minutes = int(value // 60)
    rest = int(value - minutes * 60)
    if rest >= 60:
        minutes += 1
        rest -= 60
    return "T+%02d:%02d" % (minutes, rest)


def _series_max(values: Sequence[Optional[float]]) -> Tuple[int, float]:
    """``(argmax, max)``；没有有效值时 ``(-1, 0.0)``。"""
    best = -1
    best_value = 0.0
    for index, value in enumerate(values):
        if value is None:
            continue
        if best < 0 or value > best_value:
            best = index
            best_value = float(value)
    return best, best_value


def _series_min(values: Sequence[Optional[float]]) -> Tuple[int, float]:
    """``(argmin, min)``；没有有效值时 ``(-1, 0.0)``。"""
    best = -1
    best_value = 0.0
    for index, value in enumerate(values):
        if value is None:
            continue
        if best < 0 or value < best_value:
            best = index
            best_value = float(value)
    return best, best_value


def _median_step(times: Sequence[float]) -> float:
    diffs = [times[i + 1] - times[i] for i in range(len(times) - 1) if times[i + 1] > times[i]]
    if not diffs:
        return 0.0
    diffs.sort()
    return diffs[len(diffs) // 2]


def _moving_average(values: Sequence[Optional[float]], half: int) -> List[Optional[float]]:
    """居中滑动平均（窗口 ``2*half+1``），``None`` 不参与。"""
    if not values:
        return []
    half = max(1, int(half))
    prefix_sum = [0.0]
    prefix_cnt = [0]
    for value in values:
        if value is None:
            prefix_sum.append(prefix_sum[-1])
            prefix_cnt.append(prefix_cnt[-1])
        else:
            prefix_sum.append(prefix_sum[-1] + float(value))
            prefix_cnt.append(prefix_cnt[-1] + 1)
    out: List[Optional[float]] = []
    total = len(values)
    for index in range(total):
        lo = max(0, index - half)
        hi = min(total, index + half + 1)
        count = prefix_cnt[hi] - prefix_cnt[lo]
        out.append((prefix_sum[hi] - prefix_sum[lo]) / count if count else None)
    return out


def _es_cliff(times: Sequence[float], des: Sequence[Optional[float]],
              es: Sequence[Optional[float]]) -> Optional[Dict[str, Any]]:
    """Es 断崖：2 秒滑动平均的 dEs/dt 最负值，返回该段的起点。

    起点 = 从最负点往回走、直到滑动平均不再单调下降的那个局部高点
    （即"开始掉能量"的时刻）。找不到下降段时返回 ``None``。
    """
    if len(times) < 3 or not des:
        return None
    step = _median_step(times) or 1.0
    half = max(1, int(round(ANNOTATION_SMOOTH_S / (2.0 * step))))
    smoothed = _moving_average(des, half)
    best_index = -1
    best_rate = 0.0
    for index, value in enumerate(smoothed):
        if value is None:
            continue
        if best_index < 0 or value < best_rate:
            best_index = index
            best_rate = float(value)
    if best_index < 0 or best_rate >= 0:
        return None
    # 「该段起点」：从最负点往回走，直到 2 s 平滑后的 dEs/dt 不再陡
    # （绝对值跌到最负值的 8% 以内）——即断崖开始的位置；最多回看 30 s。
    threshold = abs(best_rate) * 0.08
    max_gap = max(4.0 * step, 0.2)  # 容忍个别丢样本，不因小缺口中断回看
    index = best_index
    while index > 0:
        prev = smoothed[index - 1]
        if prev is None or abs(prev) < threshold:
            break
        if times[index] - times[index - 1] > max_gap:
            break
        if times[best_index] - times[index] > 30.0:
            break
        index -= 1
    start_index = index
    start_value = es[start_index] if start_index < len(es) else None
    return {
        "t": times[start_index],
        "value": start_value,
        "rate": best_rate,
        "bottom_t": times[best_index],
    }


#: 图上「缩写 code」：图面只写编号 + code（``① IAS_MAX``），全称 / 数值 / 时间
#: 放到图下方的对照表（legend）里，图面不再又长又挤。
#:
#: 最大过载在批注数据里的 ``kind`` 一直是 ``max_ny``（计算规则不动），
#: 这里与 ``max_g`` 一起映射到同一个 ``G_MAX``。
ANNOTATION_CODES: Dict[str, str] = {
    "max_ias": "IAS_MAX",
    "vne": "VNE",
    "min_alt": "ALT_MIN",
    "es_min": "ES_MIN",
    "es_cliff": "ES_DROP",
    "max_g": "G_MAX",
    "max_ny": "G_MAX",
    "death": "DEATH",
    "kill": "KILL",
}

#: code → 完整中文名（legend 每行「缩写 + 全称」用）
ANNOTATION_NAMES: Dict[str, str] = {
    "IAS_MAX": "最大指示空速",
    "VNE": "限速预警",
    "ALT_MIN": "最低气压高度",
    "ES_MIN": "Es 最低点",
    "ES_DROP": "Es 断崖",
    "G_MAX": "最大过载",
    "DEATH": "死亡",
    "KILL": "击落",
}

#: 图上编号：①…⑩，第 11 条起退化成 ``(11)``。
ANNOTATION_CIRCLED = "①②③④⑤⑥⑦⑧⑨⑩"


def annotation_code(kind: Optional[str]) -> str:
    """批注 ``kind`` → 图上缩写 code；未知 kind 用大写 kind 兜底。"""
    text = str(kind or "")
    return ANNOTATION_CODES.get(text, text.upper() or "ANNOT")


def annotation_name(code: str) -> str:
    """缩写 code → 完整中文名（未知 code 返回空串，legend 直接用 code）。"""
    return ANNOTATION_NAMES.get(code, "")


def _annotation_number(index: int) -> str:
    """上图编号：①②③…（索引从 0 开始；>10 条用 ``(11)``）。"""
    if 0 <= index < len(ANNOTATION_CIRCLED):
        return ANNOTATION_CIRCLED[index]
    return "(%d)" % (int(index) + 1)


def _annotation_member(annotation: Dict[str, Any]) -> Dict[str, Any]:
    """批注的一条「成员」：合并后一条批注可能对应多个 kind（每个各占 legend 一段）。"""
    return {
        "kind": annotation.get("kind"),
        "code": annotation.get("code") or annotation_code(annotation.get("kind")),
        "value": annotation.get("value"),
        "ref": annotation.get("ref"),
        "rate": annotation.get("rate"),
        "unit": annotation.get("unit") or "",
        "decimals": int(annotation.get("decimals", 0) or 0),
        "t": float(annotation["t"]),
    }


def _annotation_part(annotation: Dict[str, Any]) -> str:
    """一条批注的完整短文字（CLI 摘要 / 数据导出用；图面改用 :func:`_annotation_mark_text`）。"""
    kind = annotation.get("kind")
    if kind == "es_cliff":
        return "Es 断崖 %+.0f m/s" % float(annotation.get("rate") or 0.0)
    if kind == "vne":
        return "限速预警 >%s km/h" % _fmt_number(annotation.get("ref"), 0)
    digits = int(annotation.get("decimals", 0))
    unit = annotation.get("unit") or ""
    unit_text = (" " + unit) if unit else ""
    return "%s %s%s" % (
        annotation.get("title") or annotation.get("kind") or "",
        _fmt_number(annotation.get("value"), digits),
        unit_text,
    )


def _merge_page_annotations(
    annotations: Sequence[Dict[str, Any]],
) -> List[Dict[str, Any]]:
    """同一页：时间相近（<10 s）的批注合并，最后只留前 N 条。"""
    ordered = sorted(
        annotations, key=lambda item: (int(item.get("priority", 9)), float(item["t"]))
    )
    kept: List[Dict[str, Any]] = []
    for annotation in ordered:
        target = None
        for existing in kept:
            if abs(float(existing["t"]) - float(annotation["t"])) < ANNOTATION_MERGE_S:
                target = existing
                break
        if target is None:
            new = dict(annotation)
            new["parts"] = [_annotation_part(annotation)]
            new["members"] = [_annotation_member(annotation)]
            new["merged"] = False
            kept.append(new)
            continue
        target["merged"] = True
        if float(annotation["t"]) < float(target["t"]):
            target.update(
                t=annotation["t"],
                value=annotation.get("value"),
                series=annotation.get("series"),
                kind=annotation.get("kind"),
            )
        target["parts"].append(_annotation_part(annotation))
        target["members"].append(_annotation_member(annotation))
    # 合并批注的 legend 成员按时间排（legend 的时间取最早那条，读起来才一致）
    for item in kept:
        item["members"].sort(key=lambda member: float(member["t"]))
    return kept[:ANNOTATION_MAX_PER_PAGE]


def _annotation_members(annotation: Dict[str, Any]) -> List[Dict[str, Any]]:
    members = annotation.get("members") or []
    return list(members) if members else [_annotation_member(annotation)]


def _annotation_codes(annotation: Dict[str, Any]) -> List[str]:
    """一条批注用到的 code（合并批注按顺序去重，如 ``["IAS_MAX", "VNE"]``）。"""
    codes: List[str] = []
    for member in _annotation_members(annotation):
        code = member.get("code") or annotation_code(member.get("kind"))
        if code and code not in codes:
            codes.append(code)
    return codes or [annotation_code(annotation.get("kind"))]


def _annotation_code_label(annotation: Dict[str, Any]) -> str:
    """图面缩写标签：``IAS_MAX``，合并批注是 ``IAS_MAX+VNE``。"""
    return "+".join(_annotation_codes(annotation))


def _annotation_mark_text(annotation: Dict[str, Any]) -> str:
    """图上标记里只写缩写 code（编号由渲染器补 ``①``），不带数值 / 时间。"""
    return _annotation_code_label(annotation)


def _annotation_measure(member: Dict[str, Any]) -> str:
    """legend 里的数值段：``1215 km/h`` / ``>1345 km/h`` / ``-699 m/s``。"""
    kind = member.get("kind")
    if kind == "vne":
        return ">%s km/h" % _fmt_number(member.get("ref"), 0)
    if kind == "es_cliff":
        return "%+.0f m/s" % float(member.get("rate") or 0.0)
    digits = int(member.get("decimals", 0) or 0)
    text = _fmt_number(member.get("value"), digits)
    unit = (member.get("unit") or "").strip()
    return ("%s %s" % (text, unit)) if unit else text


def _annotation_name_value_text(annotation: Dict[str, Any], name_sep: str = " ") -> str:
    """legend 的「全称 + 数值」段；合并批注的多个成员用 `` / `` 连接。

    ``name_sep=" "`` → ``最大指示空速 1215 km/h``（复盘卡）
    ``name_sep=" · "`` → ``最大指示空速 · 1215 km/h``（屏幕 / PDF）
    """
    bits: List[str] = []
    for member in _annotation_members(annotation):
        code = member.get("code") or annotation_code(member.get("kind"))
        name = annotation_name(code) or code
        bits.append("%s%s%s" % (name, name_sep, _annotation_measure(member)))
    return " / ".join(bits)


def _annotation_legend_line(annotation: Dict[str, Any], index: int) -> str:
    """屏幕 / PDF 的 legend 一行：``① IAS_MAX 最大指示空速 · 1215 km/h · T+04:32``。"""
    return "%s %s %s · %s" % (
        _annotation_number(index),
        _annotation_code_label(annotation),
        _annotation_name_value_text(annotation, name_sep=" · "),
        _tc_text(float(annotation["t"]), tenths=False),
    )


def _annotation_card_line(annotation: Dict[str, Any], index: int) -> str:
    """PNG 复盘卡的 legend 一行：``① IAS_MAX — 最大指示空速 1215 km/h · T+04:32``。"""
    return "%s %s — %s · %s" % (
        _annotation_number(index),
        _annotation_code_label(annotation),
        _annotation_name_value_text(annotation, name_sep=" "),
        _tc_text(float(annotation["t"]), tenths=False),
    )


def _death_legend_line(mark: Dict[str, Any], index: int) -> str:
    """屏幕 / PDF 的 DEATH legend 行：``③ DEATH 死亡 · 395.4 s · T+06:35（数据终止）``。"""
    moment = float(mark["t"])
    note = mark.get("note") or "数据终止"
    return "%s DEATH 死亡 · %s s · %s（%s）" % (
        _annotation_number(index),
        _fmt_number(moment, 1),
        _tc_text(moment, tenths=False),
        note,
    )


def _death_card_line(mark: Dict[str, Any], index: int) -> str:
    """PNG 复盘卡的 DEATH legend 行：``③ DEATH — 死亡 395.4 s · T+06:35（数据终止）``。"""
    moment = float(mark["t"])
    note = mark.get("note") or "数据终止"
    return "%s DEATH — 死亡 %s s · %s（%s）" % (
        _annotation_number(index),
        _fmt_number(moment, 1),
        _tc_text(moment, tenths=False),
        note,
    )


def _kill_target(mark: Dict[str, Any]) -> str:
    """击落事件的目标名；缺目标时用占位，保证 legend 一行永远能读。"""
    return str(mark.get("target") or "").strip() or "未知目标"


def _kill_legend_line(mark: Dict[str, Any], index: int) -> str:
    """屏幕 / PDF 的 KILL legend 行：``④ KILL 击落 =FdX= Target · T+05:12``。

    编号沿用事件编号规则（排在数据批注 / DEATH 之后，不和数据批注冲突）；
    竖线本身每页都画，这一行只是图下 legend 的说明。
    """
    moment = float(mark["t"])
    return "%s KILL 击落 %s · %s" % (
        _annotation_number(index),
        _kill_target(mark),
        _tc_text(moment, tenths=False),
    )


def _kill_card_line(mark: Dict[str, Any], index: int) -> str:
    """PNG 复盘卡的 KILL legend 行：``④ KILL — 击落 =FdX= Target · T+05:12``。"""
    moment = float(mark["t"])
    return "%s KILL — 击落 %s · %s" % (
        _annotation_number(index),
        _kill_target(mark),
        _tc_text(moment, tenths=False),
    )


def _event_legend_line(mark: Dict[str, Any], index: int) -> str:
    """事件 legend 行分发：``kill`` → KILL 行，其余沿用 DEATH 行（向后兼容）。"""
    if str(mark.get("kind") or "") == "kill":
        return _kill_legend_line(mark, index)
    return _death_legend_line(mark, index)


def _gap_legend_line(item: Dict[str, Any], index: int) -> str:
    """屏幕 / PDF 的 GAP legend 行：``③ GAP 数据中断 2.66s · T+00:19``。

    编号沿用事件编号规则（排在数据批注 / DEATH / KILL 之后，不占数据批注的 2 条上限）；
    时间取空洞**起点**并截断到秒，``2.66s`` 与事件条 / 概览「数据完整性」口径一致。
    """
    gap = float(item.get("gap") or 0.0)
    return "%s GAP 数据中断 %ss · %s" % (
        _annotation_number(index),
        _fmt_number(gap, 2),
        _tc_text_floor(float(item["t0"])),
    )


def _annotation_text(annotation: Dict[str, Any]) -> str:
    """屏幕标注条上的完整文字：``值 · T+mm:ss``。"""
    body = " / ".join(annotation.get("parts") or [_annotation_part(annotation)])
    return "%s · %s" % (body, _tc_text(float(annotation["t"])))


def _annotation_print_text(annotation: Dict[str, Any]) -> str:
    """打印/复盘卡上的打字机短注（一行，尽量短）。"""
    parts = annotation.get("parts") or [_annotation_part(annotation)]
    body = parts[0]
    if len(parts) > 1:
        body += " +%d" % (len(parts) - 1)
    return "%s · %s" % (body, _tc_text(float(annotation["t"]), tenths=False))


def _first_over(values: Sequence[Optional[float]], limit: float) -> int:
    """首次 ``value > limit`` 的下标；没有返回 ``-1``。"""
    for index, value in enumerate(values):
        if value is not None and value > limit:
            return index
    return -1


def compute_annotations(
    flight: Any, vne_kmh: Optional[float] = None
) -> Dict[str, List[Dict[str, Any]]]:
    """按"批注规则表"计算一个架次每页的批注（页面键与 ``_flight_panel_specs`` 一致）。

    规则：

    * ``speed``：最大 IAS；首次 IAS 超过该机限速（限速预警）
    * ``altitude``：最低气压高度
    * ``energy``：Es 最低点；Es 断崖（2 s 滑动平均 dEs/dt 最负值 → 标该段起点）
    * ``aoa_ny``：最大过载（Ny 正值峰值）
    * ``attitude``：不批注
    * 死亡/坠毁只在概览 / 能量页加圈（由渲染侧处理，不在这里）
    """
    series = getattr(flight, "series", None) or {}
    times = [float(t) for t in (series.get("t") or []) if t is not None]
    if len(times) < 2:
        return {}
    t0 = float(getattr(flight, "start_t", times[0]))
    t1 = float(getattr(flight, "end_t", times[-1]))
    if t1 <= t0:
        t1 = t0 + 0.5

    def values(key: str) -> List[Optional[float]]:
        raw = series.get(key) or []
        if len(raw) != len(times):
            return [None] * len(times)
        return [None if value is None else float(value) for value in raw]

    annotations: Dict[str, List[Dict[str, Any]]] = {}

    def add(page: str, **fields: Any) -> None:
        index = int(fields.pop("index"))
        if not (0 <= index < len(times)) or not (t0 - 0.05 <= times[index] <= t1 + 0.05):
            return
        annotation = {
            "page": page,
            "t": float(times[index]),
            "value": fields.pop("value", None),
        }
        annotation.update(fields)
        # 图上用的缩写 code + 中文全称（图面只写 ① CODE，全称进 legend）
        annotation["code"] = annotation_code(annotation.get("kind"))
        annotation["name"] = annotation_name(annotation["code"])
        annotations.setdefault(page, []).append(annotation)

    ias = values("ias")
    index, value = _series_max(ias)
    if index >= 0:
        add("speed", index=index, value=value, kind="max_ias", title="MAX IAS",
            series="ias", unit="km/h", decimals=0, priority=0)
    if vne_kmh is not None and vne_kmh > 0:
        crossing = _first_over(ias, float(vne_kmh))
        if crossing >= 0:
            add("speed", index=crossing, value=ias[crossing], kind="vne",
                title="限速预警", series="ias", unit="km/h", decimals=0,
                ref=float(vne_kmh), priority=1)

    alt = values("alt")
    index, value = _series_min(alt)
    if index >= 0:
        add("altitude", index=index, value=value, kind="min_alt",
            title="最低气压高度", series="alt", unit="m", decimals=0, priority=0,
            dimension=True)

    es = values("es")
    index, value = _series_min(es)
    if index >= 0:
        add("energy", index=index, value=value, kind="es_min", title="Es 最低点",
            series="es", unit="m", decimals=0, priority=0, dimension=True)
    cliff = _es_cliff(times, values("des"), es)
    if cliff is not None and cliff["value"] is not None:
        nearest = min(range(len(times)), key=lambda i: abs(times[i] - cliff["t"]))
        add("energy", index=nearest, value=cliff["value"], kind="es_cliff",
            title="Es 断崖", series="es", unit="m", decimals=0,
            rate=cliff["rate"], priority=1)

    ny = values("ny")
    index, value = _series_max(ny)
    if index >= 0:
        add("aoa_ny", index=index, value=value, kind="max_ny", title="最大过载",
            series="ny", unit="g", decimals=2, priority=0)

    return {page: _merge_page_annotations(items) for page, items in annotations.items()}

SERIES_ROUND = {
    "t": 3,
    "mach": 2,
    "valid": 0,
    "inflight": 0,
    "es": 1,
    "des": 1,
}


def _series_digits(key: str) -> int:
    return SERIES_ROUND.get(key, 1)


def _round_series(values: Sequence[Optional[float]], digits: int) -> List[Optional[float]]:
    out: List[Optional[float]] = []
    for value in values:
        if value is None:
            out.append(None)
        else:
            out.append(round(float(value), digits))
    return out


# ---------------------------------------------------------------------------
# 数值 / 坐标工具
# ---------------------------------------------------------------------------


def _fmt_number(value: Optional[float], digits: int = 1) -> str:
    if value is None:
        return "—"
    text = ("%." + str(digits) + "f") % value
    if "." in text:
        text = text.rstrip("0").rstrip(".")
    return text


def _fmt_coord(value: float) -> str:
    return "%.1f" % value


def _nice_ticks(lo: float, hi: float, target: int = 5) -> Tuple[float, float, float, List[float]]:
    """给出一个"好看"的坐标范围与刻度。"""
    if lo is None or hi is None or not math.isfinite(lo) or not math.isfinite(hi):
        lo, hi = 0.0, 1.0
    if hi - lo < 1e-9:
        # 常数序列：围绕数值展开成以 0 为基准的可读范围
        if abs(hi) < 1e-9:
            lo, hi = -1.0, 1.0
        else:
            pad = max(abs(hi) * 0.1, 1.0)
            lo, hi = hi - pad, hi + pad
    raw = (hi - lo) / max(1, target)
    if raw <= 0:
        raw = 1.0
    mag = 10.0 ** math.floor(math.log10(raw))
    norm = raw / mag
    if norm <= 1.0:
        step = 1.0 * mag
    elif norm <= 2.0:
        step = 2.0 * mag
    elif norm <= 2.5:
        step = 2.5 * mag
    elif norm <= 5.0:
        step = 5.0 * mag
    else:
        step = 10.0 * mag
    nlo = math.floor(lo / step) * step
    nhi = math.ceil(hi / step) * step
    ticks: List[float] = []
    v = nlo
    guard = 0
    while v <= nhi + step * 1e-6 and guard < 200:
        ticks.append(round(v, 10))
        v += step
        guard += 1
    return nlo, nhi, step, ticks


def _tick_digits(step: float) -> int:
    if step >= 10:
        return 0
    if step >= 1:
        return 0
    if step >= 0.1:
        return 1
    return 2


def _time_ticks(t0: float, t1: float, target: int = 8) -> Tuple[float, List[float]]:
    """时间轴刻度：只给"整"步长（1/2/5/10/15/30/60/120... 秒）。"""
    span = max(t1 - t0, 1e-6)
    raw = span / max(2, target)
    candidates = (0.5, 1, 2, 5, 10, 15, 30, 60, 120, 300, 600)
    step = candidates[-1]
    for cand in candidates:
        if raw <= cand:
            step = cand
            break
    start = math.ceil(t0 / step) * step
    ticks: List[float] = []
    v = start
    while v <= t1 + 1e-9:
        ticks.append(round(v, 6))
        v += step
    return step, ticks


def _fmt_time(value: float, step: float) -> str:
    if step >= 1:
        return "%d" % round(value)
    return ("%." + str(_tick_digits(step)) + "f") % value


def _iter_segments_with_gaps(
    times: Sequence[float], values: Sequence[Optional[float]], max_gap_s: float = PATH_GAP_S
) -> Tuple[List[List[Tuple[float, float]]], List[Dict[str, float]]]:
    """把序列切成若干连续段，并返回段与段之间的**时间跳变**。

    返回 ``(segments, gaps)``：

    * ``segments`` —— ``[[(t, v), ...], ...]``，``None`` 或相邻间隔 > ``max_gap_s``
      处断开；
    * ``gaps`` —— ``[{"t0", "t1", "gap", "v0", "v1"}, ...]``，只记**时间跳变**造成的
      断口，``v0`` / ``v1`` 是断口两侧的值（画虚线贝塞尔桥用）。

    ``None`` 造成的断开不算 gap：那是坏样本 / 无效数据，用 ``invalid`` 底纹表示，
    不做插值桥（否则等于凭空编数据）。
    """
    segments: List[List[Tuple[float, float]]] = []
    gaps: List[Dict[str, float]] = []
    current: List[Tuple[float, float]] = []
    last_t: Optional[float] = None
    for t, v in zip(times, values):
        if t is None or v is None:
            if current:
                segments.append(current)
                current = []
            last_t = None
            continue
        if last_t is not None and (t - last_t) > max_gap_s:
            if current:
                prev = current[-1]
                segments.append(current)
                gaps.append(
                    {
                        "t0": float(prev[0]),
                        "t1": float(t),
                        "gap": float(t) - float(prev[0]),
                        "v0": float(prev[1]),
                        "v1": float(v),
                    }
                )
                current = []
        current.append((t, v))
        last_t = t
    if current:
        segments.append(current)
    return segments, gaps


def _iter_segments(
    times: Sequence[float], values: Sequence[Optional[float]], max_gap_s: float = PATH_GAP_S
) -> List[List[Tuple[float, float]]]:
    """兼容入口：只要连续段（内部改用 :func:`_iter_segments_with_gaps`）。"""
    return _iter_segments_with_gaps(times, values, max_gap_s)[0]


def _decimate(points: List[Tuple[float, float]], limit: int) -> List[Tuple[float, float]]:
    if len(points) <= limit:
        return points
    step = len(points) / float(limit)
    out = [points[0]]
    pos = step
    last_index = 0
    while pos < len(points) - 1:
        idx = int(round(pos))
        if idx != last_index:
            out.append(points[idx])
            last_index = idx
        pos += step
    out.append(points[-1])
    return out


# ---------------------------------------------------------------------------
# 平滑路径：Catmull-Rom 切线 + Fritsch–Carlson 限幅 → 三次贝塞尔
# ---------------------------------------------------------------------------
#
# 旧版曲线是 ``M...L...`` 折线，样本密时转折生硬、还容易看出量化锯齿。
# 现在所有"连续段"都画成穿过每个点的三次贝塞尔：
#
# 1. 切线取三点差分（= Catmull-Rom 在函数形式下的非均匀版本）：
#    ``m_i = (h_next*delta_prev + h_prev*delta_next) / (h_prev + h_next)``；
# 2. 再用 Fritsch–Carlson 单调性限幅（PCHIP 的圆约束 ``a^2 + b^2 <= 9``，
#    局部极值处切线置 0）。这一步保证**每段 y 都在两端点之间、x 单调**，
#    即曲线不会过冲（不会在尖峰外鼓包、不会回头打圈），也不改变端点值。
#
# 控制点仍是贝塞尔标准形式 ``c1 = p0 + h/3 * (1, m0)``、``c2 = p1 - h/3 * (1, m1)``，
# 所以"穿过各点"和 Catmull-Rom 一致，只是把危险的切线压回去了。


def _bezier_segments(
    points: Sequence[Tuple[float, float]],
) -> List[Tuple[Tuple[float, float], Tuple[float, float], Tuple[float, float], Tuple[float, float]]]:
    """像素点列 → ``[(p0, c1, c2, p1), ...]`` 三次贝塞尔段（无过冲，见上）。"""
    clean: List[Tuple[float, float]] = []
    for raw_x, raw_y in points:
        x = float(raw_x)
        y = float(raw_y)
        if not (math.isfinite(x) and math.isfinite(y)):
            continue
        if clean and abs(x - clean[-1][0]) < 1e-9:
            # 同一像素列（重复时间戳 / 抽稀撞车）：保留后一个值，避免 h = 0
            clean[-1] = (x, y)
        else:
            clean.append((x, y))
    n = len(clean)
    if n < 2:
        return []

    h = [clean[i + 1][0] - clean[i][0] for i in range(n - 1)]
    delta = [(clean[i + 1][1] - clean[i][1]) / h[i] for i in range(n - 1)]

    # 1) Catmull-Rom（三点差分）切线 + 局部极值处置 0（单调性要求）
    tan = [0.0] * n
    tan[0] = delta[0]
    tan[-1] = delta[-1]
    for i in range(1, n - 1):
        if delta[i - 1] * delta[i] <= 0.0:
            tan[i] = 0.0
        else:
            tan[i] = (h[i] * delta[i - 1] + h[i - 1] * delta[i]) / (h[i - 1] + h[i])

    # 2) Fritsch–Carlson 圆约束限幅（只减小切线，因此前一段的单调性不会被破坏）
    for i in range(n - 1):
        if abs(delta[i]) < 1e-12:
            tan[i] = 0.0
            tan[i + 1] = 0.0
            continue
        a = max(0.0, tan[i] / delta[i])
        b = max(0.0, tan[i + 1] / delta[i])
        norm = a * a + b * b
        if norm > 9.0:
            scale = 3.0 / math.sqrt(norm)
            tan[i] = scale * a * delta[i]
            tan[i + 1] = scale * b * delta[i]

    segments = []
    for i in range(n - 1):
        step = h[i] / 3.0
        p0 = clean[i]
        p1 = clean[i + 1]
        c1 = (p0[0] + step, p0[1] + tan[i] * step)
        c2 = (p1[0] - step, p1[1] - tan[i + 1] * step)
        segments.append((p0, c1, c2, p1))
    return segments


def _bezier_commands(
    segments: Sequence[
        Tuple[Tuple[float, float], Tuple[float, float], Tuple[float, float], Tuple[float, float]]
    ],
) -> str:
    """控制点段 → ``C x1,y1 x2,y2 x,y`` 命令串（不含起始 ``M``）。"""
    parts: List[str] = []
    for _p0, c1, c2, p1 in segments:
        parts.append(
            "C%s,%s %s,%s %s,%s"
            % (
                _fmt_coord(c1[0]),
                _fmt_coord(c1[1]),
                _fmt_coord(c2[0]),
                _fmt_coord(c2[1]),
                _fmt_coord(p1[0]),
                _fmt_coord(p1[1]),
            )
        )
    return "".join(parts)


def _bezier_path(points: Sequence[Tuple[float, float]]) -> str:
    """像素点列 → ``M...C...`` 平滑路径（局部极值/尖峰不过冲）。"""
    segments = _bezier_segments(points)
    if not segments:
        return ""
    first = segments[0][0]
    return "M%s,%s%s" % (_fmt_coord(first[0]), _fmt_coord(first[1]), _bezier_commands(segments))


def _edge_slope_px(
    segment: Sequence[Tuple[float, float]],
    side: str,
    xaxis: Any,
    yaxis: Any,
    window_s: float = 1.0,
) -> float:
    """断口一侧的像素斜率 ``dy/dx``（用一个时间窗里的首尾点估计，抗单点噪声）。"""
    if len(segment) < 2:
        return 0.0
    if side == "end":
        t_anchor, v_anchor = segment[-1]
        t_other, v_other = segment[0]
        for t, v in segment:
            if t >= t_anchor - window_s:
                t_other, v_other = t, v
                break
    else:  # "start"
        t_anchor, v_anchor = segment[0]
        t_other, v_other = segment[-1]
        for t, v in reversed(segment):
            if t <= t_anchor + window_s:
                t_other, v_other = t, v
                break
    if abs(t_anchor - t_other) < 1e-9:
        return 0.0
    py_anchor = yaxis(v_anchor)
    py_other = yaxis(v_other)
    px_anchor = xaxis(t_anchor)
    px_other = xaxis(t_other)
    if py_anchor is None or py_other is None or abs(px_anchor - px_other) < 1e-9:
        return 0.0
    slope = (py_anchor - py_other) / (px_anchor - px_other)
    # 极端斜率（单样本跳变）没有意义，限幅到 ±8（约 83°），控制点再按端点点 y 夹紧
    return max(-8.0, min(8.0, slope))


def _gap_bridge_path(
    p0: Tuple[float, float], slope0: float, p1: Tuple[float, float], slope1: float
) -> str:
    """断口两侧点 → 一段平滑的三次贝塞尔"桥"（虚线画，表示插值 / 中断）。"""
    dx = p1[0] - p0[0]
    if dx <= 0.0:
        return ""
    step = dx / 3.0
    c1y = p0[1] + slope0 * step
    c2y = p1[1] - slope1 * step
    lo, hi = (p0[1], p1[1]) if p0[1] <= p1[1] else (p1[1], p0[1])
    # 控制点 y 夹在两端点之间 → 桥本身也不会过冲（凸包性质）
    c1y = min(max(c1y, lo), hi)
    c2y = min(max(c2y, lo), hi)
    return "M%s,%s C%s,%s %s,%s %s,%s" % (
        _fmt_coord(p0[0]),
        _fmt_coord(p0[1]),
        _fmt_coord(p0[0] + step),
        _fmt_coord(c1y),
        _fmt_coord(p1[0] - step),
        _fmt_coord(c2y),
        _fmt_coord(p1[0]),
        _fmt_coord(p1[1]),
    )


class _Axis:
    """一条 y 轴：数据范围 → 画布坐标。刻度在创建时算好，保证刻度和坐标一致。"""

    def __init__(
        self,
        lo: float,
        hi: float,
        cmin: float,
        cmax: float,
        step: float = 0.0,
        ticks: Optional[Sequence[float]] = None,
    ) -> None:
        self.lo = lo
        self.hi = hi
        self.cmin = cmin
        self.cmax = cmax
        self.step = step
        self.ticks = list(ticks) if ticks is not None else []

    def __call__(self, value: Optional[float]) -> Optional[float]:
        if value is None:
            return None
        frac = (value - self.lo) / (self.hi - self.lo)
        # lo 在底部（cmin = y1），hi 在顶部（cmax = y0）
        return self.cmin - frac * (self.cmin - self.cmax)

    def as_dict(self) -> Dict[str, float]:
        return {"min": self.lo, "max": self.hi}


def _make_axis(values: Sequence[float], cmin: float, cmax: float) -> _Axis:
    lo, hi, step, ticks = _nice_ticks(min(values), max(values), target=5)
    return _Axis(lo, hi, cmin, cmax, step, ticks)


class _XAxis:
    def __init__(self, t0: float, t1: float, cmin: float, cmax: float) -> None:
        self.t0 = t0
        self.t1 = t1
        self.cmin = cmin
        self.cmax = cmax

    def __call__(self, t: float) -> float:
        frac = (t - self.t0) / (self.t1 - self.t0)
        return self.cmin + frac * (self.cmax - self.cmin)


# ---------------------------------------------------------------------------
# 面板配置（每个 key 对应报告里的一页）
# ---------------------------------------------------------------------------


def _series(key: str, label: str, color: str, unit: str, axis: str = "left",
            decimals: int = 1, dash: Optional[Union[bool, str]] = None,
            width: Optional[float] = None,
            clip: Optional[str] = None, fill: Optional[str] = None,
            tooltip: bool = True, signed: bool = False,
            tooltip_label: Optional[str] = None) -> Dict[str, Any]:
    """一条曲线的定义。

    ``dash`` / ``width`` 省略时取 :data:`LINE_STYLES` 里按 ``key`` 登记的线型
    （速度页 IAS/TAS/Mach/平均速度、姿态页 Pitch/Bank/Roll 已集中登记）；
    ``dash=True`` 兼容旧写法（= :data:`DEFAULT_DASH`），字符串则按 SVG
    ``stroke-dasharray`` 原样使用。
    """
    style = line_style(key)
    if dash is None:
        pattern = style["dash"]
    elif dash is True:
        pattern = DEFAULT_DASH
    elif dash is False:
        pattern = None
    else:
        pattern = str(dash)
    return {
        "key": key,
        "label": label,
        "color": color,
        "unit": unit,
        "axis": axis,
        "decimals": decimals,
        "dash": pattern,
        "width": style["width"] if width is None else width,
        "clip": clip,
        "fill": fill,
        "tooltip": tooltip,
        "signed": signed,
        "tooltip_label": tooltip_label,
    }


def _flight_panel_specs() -> List[Dict[str, Any]]:
    """单架次的 7 个数据主题（顺序 = 报告里的页面顺序）。"""
    height = PAGE_CHART_H
    return [
        {
            "key": "speed",
            "title": "速度",
            "subtitle": "IAS / TAS（左轴 km/h）与 Mach（右轴）",
            "height": height,
            "left_unit": "km/h",
            "right_unit": "Mach",
            "series": [
                _series("ias", "IAS 指示空速", COLORS["ias"], "km/h", decimals=0),
                _series("tas", "TAS 真空速", COLORS["tas"], "km/h", decimals=0),
                _series("mach", "Mach", COLORS["mach"], "", axis="right", decimals=2),
            ],
        },
        {
            "key": "altitude",
            "title": "高度",
            "subtitle": "气压高度 （实线）+ 雷达高度（虚线）",
            "height": height,
            "left_unit": "m",
            "series": [
                _series("alt", "气压高度", COLORS["alt"], "m", decimals=0, width=1.8),
                _series("ralt", "雷达高度", COLORS["ralt"], "m", decimals=1, dash=True, width=1.3),
            ],
        },
        {
            "key": "energy",
            "title": "比能量高度 Es",
            "subtitle": "Es = 高度 + 速度² / 2g（米）",
            "height": height,
            "left_unit": "m",
            "series": [
                _series("es", "比能量高度 Es", COLORS["es"], "m", decimals=0, width=2.0, fill="min"),
            ],
        },
        {
            "key": "energy_rate",
            "title": "能量变化率 dEs/dt",
            "subtitle": "绿色 = 获得能量，红色 = 损失能量（m/s）",
            "height": height,
            "left_unit": "m/s",
            "series": [
                _series("des", "获得能量", COLORS["des_pos"], "m/s", decimals=1, width=1.4,
                        clip="pos", fill="zero", tooltip=True, signed=True,
                        tooltip_label="dEs/dt"),
                _series("des", "损失能量", COLORS["des_neg"], "m/s", decimals=1, width=1.4,
                        clip="neg", fill="zero", tooltip=False, signed=True,
                        tooltip_label="dEs/dt"),
            ],
        },
        {
            "key": "vy",
            "title": "垂直速度 Vy",
            "subtitle": "爬升率为正、下降率为负（m/s）",
            "height": height,
            "left_unit": "m/s",
            "series": [
                _series("vy", "Vy 垂直速度", COLORS["vy"], "m/s", decimals=1, width=1.6,
                        fill="zero"),
            ],
        },
        {
            "key": "aoa_ny",
            "title": "迎角与过载",
            "subtitle": "AoA 迎角（左轴 °）+ Ny 过载（右轴 g）",
            "height": height,
            "left_unit": "°",
            "right_unit": "g",
            "series": [
                _series("aoa", "AoA 迎角", COLORS["aoa"], "°", decimals=1, width=1.6),
                _series("ny", "Ny 过载", COLORS["ny"], "g", axis="right", decimals=2),
            ],
        },
        {
            "key": "attitude",
            "title": "姿态",
            "subtitle": "Pitch 俯仰（左轴 °）+ Bank 滚转 / Roll rate 滚转率（右轴）",
            "height": height,
            "left_unit": "°",
            "right_unit": "° / °/s",
            "series": [
                _series("pitch", "Pitch 俯仰", COLORS["pitch"], "°", decimals=1),
                _series("bank", "Bank 滚转", COLORS["bank"], "°", axis="right", decimals=1),
                _series("roll", "Roll rate 滚转率", COLORS["roll"], "°/s",
                        axis="right", decimals=1),
            ],
        },
    ]


def _thumb_panel_spec() -> Dict[str, Any]:
    """概览页的比能量缩略图。"""
    return {
        "key": "es_thumb",
        "title": "比能量高度 Es",
        "subtitle": "Es = 高度 + 速度² / 2g（米）",
        "height": THUMB_CHART_H,
        "left_unit": "m",
        "series": [
            _series("es", "比能量高度 Es", COLORS["es"], "m", decimals=0, width=2.0, fill="min"),
        ],
    }


def _overview_panel_specs() -> List[Dict[str, Any]]:
    """多架次"全局总览"页：三张堆叠的合并时间轴图。"""
    height = GLOBAL_CHART_H
    return [
        {
            "key": "speed",
            "title": "速度（全局）",
            "height": height,
            "left_unit": "km/h",
            "right_unit": "Mach",
            "series": [
                _series("ias", "IAS", COLORS["ias"], "km/h", decimals=0, width=1.3),
                _series("tas", "TAS", COLORS["tas"], "km/h", decimals=0, width=1.3),
                _series("mach", "Mach", COLORS["mach"], "", axis="right", decimals=2, width=1.0),
            ],
        },
        {
            "key": "altitude",
            "title": "高度（全局）",
            "height": height,
            "left_unit": "m",
            "series": [
                _series("alt", "气压高度", COLORS["alt"], "m", decimals=0, width=1.3),
                _series("ralt", "雷达高度", COLORS["ralt"], "m", decimals=1, dash=True, width=1.0),
            ],
        },
        {
            "key": "energy",
            "title": "比能量高度 Es（全局）",
            "height": height,
            "left_unit": "m",
            "series": [
                _series("es", "Es", COLORS["es"], "m", decimals=0, width=1.5, fill="min"),
            ],
        },
    ]


# ---------------------------------------------------------------------------
# SVG 片段
# ---------------------------------------------------------------------------


def _svg_series(
    defn: Dict[str, Any],
    times: Sequence[float],
    values: Sequence[Optional[float]],
    xaxis: _XAxis,
    yaxis: _Axis,
) -> str:
    """渲染一条曲线：三次贝塞尔平滑（含正值/负值裁剪与面积填充）。

    * 非中断段（相邻样本间隔 ≤ ``PATH_GAP_S``）不再画折线，改用
      :func:`_bezier_path`（Catmull-Rom 切线 + 限幅）平滑穿过各点；
    * 每个 > 1 s 的时间空洞：实线断开，在断口两侧点之间补一条**虚线、
      低透明度、同色**的三次贝塞尔桥（表示插值 / 中断），不画实线直连；
    * 面积图的**底边保持直线**（``M baseline → L 首点 → 贝塞尔 → L baseline``）。
    """
    clip = defn.get("clip")
    fill = defn.get("fill")
    if clip == "pos":
        series = [None if v is None else max(0.0, v) for v in values]
    elif clip == "neg":
        series = [None if v is None else min(0.0, v) for v in values]
    else:
        series = list(values)

    segments, gaps = _iter_segments_with_gaps(times, series)
    if not segments:
        return ""

    # 断口桥：只画 <= GAP_BRIDGE_MAX_S 的（更大 = 换局/长暂停，保持断开）
    bridges: Dict[Tuple[float, float], Dict[str, float]] = {}
    for gap in gaps:
        if gap["gap"] <= GAP_BRIDGE_MAX_S:
            bridges[(gap["t0"], gap["t1"])] = gap

    color = defn["color"]
    width = defn.get("width", 1.7)
    dash_pattern = defn.get("dash")
    dash = ' stroke-dasharray="%s"' % dash_pattern if dash_pattern else ""
    parts: List[str] = []

    for index, segment in enumerate(segments):
        pixels: List[Tuple[float, float]] = []
        for t, v in segment:
            py = yaxis(v)
            if py is None:
                continue
            pixels.append((xaxis(t), py))
        if not pixels:
            continue
        if len(pixels) > 1:
            # 像素空间抽稀（保持 MAX_SVG_POINTS 口径；抽稀后仍按顺序穿过这些点）
            pixels = _decimate(pixels, MAX_SVG_POINTS)
        if len(pixels) == 1:
            parts.append(
                '<circle cx="%s" cy="%s" r="2.2" fill="%s"/>'
                % (_fmt_coord(pixels[0][0]), _fmt_coord(pixels[0][1]), color)
            )
        else:
            if fill:
                if fill == "zero":
                    baseline = yaxis(0.0)
                else:
                    baseline = yaxis(yaxis.lo)
                if baseline is not None:
                    first, last = pixels[0], pixels[-1]
                    area = (
                        "M%s,%s L%s,%s %s L%s,%s Z"
                        % (
                            _fmt_coord(first[0]),
                            _fmt_coord(baseline),
                            _fmt_coord(first[0]),
                            _fmt_coord(first[1]),
                            _bezier_commands(_bezier_segments(pixels)),
                            _fmt_coord(last[0]),
                            _fmt_coord(baseline),
                        )
                    )
                    opacity = "0.16" if fill == "min" else "0.22"
                    parts.append(
                        '<path d="%s" fill="%s" fill-opacity="%s" stroke="none"/>'
                        % (area, color, opacity)
                    )
            parts.append(
                '<path d="%s" fill="none" stroke="%s" stroke-width="%s" stroke-linejoin="round"'
                ' stroke-linecap="round"%s vector-effect="non-scaling-stroke"/>'
                % (_bezier_path(pixels), color, width, dash)
            )

        # 到下一段的断口：虚线贝塞尔桥（表示插值 / 中断，不画实线直连）
        if index + 1 < len(segments):
            gap = bridges.get((segment[-1][0], segments[index + 1][0][0]))
            if gap is None:
                continue
            py0 = yaxis(gap["v0"])
            py1 = yaxis(gap["v1"])
            if py0 is None or py1 is None:
                continue
            p0 = (xaxis(gap["t0"]), py0)
            p1 = (xaxis(gap["t1"]), py1)
            slope0 = _edge_slope_px(segment, "end", xaxis, yaxis)
            slope1 = _edge_slope_px(segments[index + 1], "start", xaxis, yaxis)
            bridge = _gap_bridge_path(p0, slope0, p1, slope1)
            if not bridge:
                continue
            parts.append(
                '<path class="gap-bridge" d="%s" fill="none" stroke="%s"'
                ' stroke-width="%s" stroke-dasharray="4 4" stroke-linecap="round"'
                ' opacity="%s" vector-effect="non-scaling-stroke"/>'
                % (bridge, color, GAP_BRIDGE_WIDTH, GAP_BRIDGE_OPACITY)
            )
    return "".join(parts)


def _svg_axes(
    left: Optional[_Axis],
    right: Optional[_Axis],
    xstep: float,
    xticks: Sequence[float],
    xaxis: _XAxis,
    t_label: bool,
    height: float,
    y0: float,
    y1: float,
) -> str:
    parts: List[str] = []
    # 水平网格 + 左轴刻度
    if left is not None:
        step = left.step
        digits = _tick_digits(step)
        for tick in left.ticks:
            py = left(tick)
            if py is None:
                continue
            is_zero = abs(tick) < 1e-9
            parts.append(
                '<line x1="%s" y1="%s" x2="%s" y2="%s" stroke="%s" stroke-width="%s"/>'
                % (
                    _fmt_coord(PLOT_LEFT),
                    _fmt_coord(py),
                    _fmt_coord(PLOT_RIGHT),
                    _fmt_coord(py),
                    COLORS.get("grid_zero", COLORS["grid"]) if is_zero else COLORS["grid"],
                    1.0 if is_zero else 1.0,
                )
            )
            parts.append(
                '<text x="%s" y="%s" text-anchor="end" font-size="11" fill="%s">%s</text>'
                % (
                    _fmt_coord(PLOT_LEFT - 8),
                    _fmt_coord(py + 3.6),
                    COLORS["axis"],
                    _fmt_number(tick, digits),
                )
            )
    # 右轴刻度
    if right is not None:
        step = right.step
        digits = _tick_digits(step)
        for tick in right.ticks:
            py = right(tick)
            if py is None:
                continue
            parts.append(
                '<text x="%s" y="%s" text-anchor="start" font-size="11" fill="%s">%s</text>'
                % (
                    _fmt_coord(PLOT_RIGHT + 8),
                    _fmt_coord(py + 3.6),
                    COLORS["axis"],
                    _fmt_number(tick, digits),
                )
            )
    # 竖向网格 + 底部时间刻度
    for tick in xticks:
        px = xaxis(tick)
        parts.append(
            '<line x1="%s" y1="%s" x2="%s" y2="%s" stroke="%s" stroke-width="1"/>'
            % (
                _fmt_coord(px),
                _fmt_coord(y0),
                _fmt_coord(px),
                _fmt_coord(y1),
                COLORS["grid"],
            )
        )
        if t_label:
            parts.append(
                '<text x="%s" y="%s" text-anchor="middle" font-size="11" fill="%s">%s</text>'
                % (
                    _fmt_coord(px),
                    _fmt_coord(height - 8),
                    COLORS["axis"],
                    _fmt_time(tick, xstep),
                )
            )
    # 边框
    parts.append(
        '<rect x="%s" y="%s" width="%s" height="%s" fill="none" stroke="%s"'
        ' stroke-width="1"/>'
        % (
            _fmt_coord(PLOT_LEFT),
            _fmt_coord(y0),
            _fmt_coord(PLOT_W),
            _fmt_coord(y1 - y0),
            COLORS["border"],
        )
    )
    return "".join(parts)


def _svg_events(
    events: Sequence[Dict[str, Any]],
    xaxis: _XAxis,
    t0: float,
    t1: float,
    y0: float,
    y1: float,
) -> str:
    parts: List[str] = []
    for event in events:
        t = event.get("t")
        if t is None or t < t0 - 0.05 or t > t1 + 0.05:
            continue
        px = min(max(xaxis(t), PLOT_LEFT), PLOT_RIGHT)
        color = EVENT_COLORS.get(event.get("kind", ""), COLORS["other"])
        parts.append(
            '<line x1="%s" y1="%s" x2="%s" y2="%s" stroke="%s" stroke-width="1.4"'
            ' stroke-dasharray="5 4" opacity="0.85"/>'
            % (_fmt_coord(px), _fmt_coord(y0), _fmt_coord(px), _fmt_coord(y1), color)
        )
    return "".join(parts)


def _svg_regions(
    regions: Sequence[Dict[str, Any]],
    xaxis: _XAxis,
    t0: float,
    t1: float,
    y0: float,
    y1: float,
) -> str:
    parts: List[str] = []
    for region in regions:
        start = max(region["t0"], t0)
        end = min(region["t1"], t1)
        if end - start <= 1e-9:
            continue
        x = xaxis(start)
        width = xaxis(end) - x
        parts.append(
            '<rect x="%s" y="%s" width="%s" height="%s" fill="%s" fill-opacity="%s"/>'
            % (
                _fmt_coord(x),
                _fmt_coord(y0),
                _fmt_coord(width),
                _fmt_coord(y1 - y0),
                region.get("color", COLORS["death_region"]),
                region.get("opacity", 0.08),
            )
        )
    return "".join(parts)


def _svg_gap_bands(
    gaps: Sequence[Dict[str, Any]],
    xaxis: _XAxis,
    t0: float,
    t1: float,
    y0: float,
    y1: float,
    chart_id: str,
) -> str:
    """数据中断的底纹：淡灰竖带 + 45° 浅斜纹（很淡，压在曲线下面）。

    每个图用自己唯一的 ``gap-hatch-<chart_id>`` 图案 id，避免单文件里多张 SVG
    的 ``<pattern>`` id 撞车。带宽按时间区间真实宽度画；极窄时至少留 0.8 单位，
    保证"有中断"这件事看得见。
    """
    if not gaps:
        return ""
    gap_color = COLORS.get("gap") or COLORS["other"]
    pattern_id = "gap-hatch-%s" % re.sub(r"[^A-Za-z0-9_.-]", "-", str(chart_id))
    parts: List[str] = [
        '<defs><pattern id="%s" width="7" height="7" patternUnits="userSpaceOnUse"'
        ' patternTransform="rotate(45)"><line x1="0" y1="0" x2="0" y2="7" stroke="%s"'
        ' stroke-width="1.5" opacity="0.6"/></pattern></defs>' % (pattern_id, gap_color)
    ]
    for item in gaps:
        start = max(float(item["t0"]), float(t0))
        end = min(float(item["t1"]), float(t1))
        if end < start:
            continue
        x = xaxis(start)
        width = max(xaxis(end) - x, 0.8)
        parts.append(
            '<rect class="gap-band" x="%s" y="%s" width="%s" height="%s" fill="%s"'
            ' fill-opacity="%s"/>'
            % (
                _fmt_coord(x),
                _fmt_coord(y0),
                _fmt_coord(width),
                _fmt_coord(y1 - y0),
                gap_color,
                GAP_BAND_OPACITY,
            )
        )
        parts.append(
            '<rect class="gap-hatch" x="%s" y="%s" width="%s" height="%s"'
            ' fill="url(#%s)" fill-opacity="%s"/>'
            % (
                _fmt_coord(x),
                _fmt_coord(y0),
                _fmt_coord(width),
                _fmt_coord(y1 - y0),
                pattern_id,
                GAP_HATCH_OPACITY,
            )
        )
    return "".join(parts)


def _svg_vlines(
    lines: Sequence[Dict[str, Any]],
    xaxis: _XAxis,
    t0: float,
    t1: float,
    y0: float,
    y1: float,
) -> str:
    parts: List[str] = []
    for line in lines:
        t = line.get("t")
        if t is None or t < t0 or t > t1:
            continue
        px = xaxis(t)
        parts.append(
            '<line x1="%s" y1="%s" x2="%s" y2="%s" stroke="%s" stroke-width="1"'
            ' stroke-dasharray="2 4" opacity="0.65"/>'
            % (
                _fmt_coord(px),
                _fmt_coord(y0),
                _fmt_coord(px),
                _fmt_coord(y1),
                line.get("color", COLORS["vline"]),
            )
        )
    return "".join(parts)


def _svg_avg_overlay(
    avg: Dict[str, Any],
    chart_id: str,
    xaxis: _XAxis,
    left: Optional[_Axis],
    right: Optional[_Axis],
    y0: float,
    y1: float,
) -> str:
    """速度页的"平均速度统计区间"图层：半透明竖条 + 两条边界线 + 平均速度虚线。

    id 规则固定为 ``avg-<kind>-<chart_id>``（chart_id 形如 ``f1-speed``），
    内联 JS 在用户改范围后按同样的 id 原地更新几何，不需要重画整张 SVG。
    """
    start = max(float(avg["range_start"]), xaxis.t0)
    end = min(float(avg["range_end"]), xaxis.t1)
    if end < start:
        return ""
    parts: List[str] = []
    x1 = xaxis(start)
    x2 = xaxis(end)
    band_w = max(0.0, x2 - x1)
    # 统计区间浅色底纹（在曲线之上、但只有 7% 不透明度，不影响看曲线）
    parts.append(
        '<rect id="avg-band-%s" class="avg-band" x="%s" y="%s" width="%s" height="%s"'
        ' fill="%s" fill-opacity="0.09"/>'
        % (
            chart_id,
            _fmt_coord(x1),
            _fmt_coord(y0),
            _fmt_coord(band_w),
            _fmt_coord(y1 - y0),
            COLORS["avg_band"],
        )
    )
    # 区间两条边界：竖向细虚线
    for slot, px in (("edge0", x1), ("edge1", x2)):
        parts.append(
            '<line id="avg-%s-%s" class="avg-edge" x1="%s" y1="%s" x2="%s" y2="%s"'
            ' stroke="%s" stroke-width="1" stroke-dasharray="3 4" opacity="0.6"/>'
            % (
                slot,
                chart_id,
                _fmt_coord(px),
                _fmt_coord(y0),
                _fmt_coord(px),
                _fmt_coord(y1),
                COLORS["avg_edge"],
            )
        )
    # 平均 IAS / 平均 TAS（左轴）与平均 Mach（右轴）的水平虚线。
    # 三条平均线统一用「中性灰 + 短虚线」的 avg_speed 线型：它们不是数据曲线，
    # 只表示统计区间的均值，靠位置 + 上方读数区分，不再和三条实测曲线抢颜色。
    avg_style = line_style("avg_speed")
    avg_dash = ' stroke-dasharray="%s"' % avg_style["dash"] if avg_style["dash"] else ""
    for key, axis in (("ias", left), ("tas", left), ("mach", right)):
        value = avg.get(key)
        if value is None or axis is None:
            continue
        py = axis(value)
        if py is None:
            continue
        parts.append(
            '<line id="avg-%s-%s" class="avg-line" data-avg-line="%s" x1="%s" y1="%s"'
            ' x2="%s" y2="%s" stroke="%s" stroke-width="%s"%s opacity="0.9"/>'
            % (
                key,
                chart_id,
                key,
                _fmt_coord(PLOT_LEFT),
                _fmt_coord(py),
                _fmt_coord(PLOT_RIGHT),
                _fmt_coord(py),
                COLORS["avg_speed"],
                avg_style["width"],
                avg_dash,
            )
        )
    return "".join(parts)


def _line_swatch(color: str, style: Dict[str, Any], css_class: str = "lg-line") -> str:
    """图例里的小线段：内联 SVG，颜色 / 线宽 / dash 与图上曲线**完全一致**。

    以前用 ``<i style="background:...">`` 只能画实色块（虚线靠 CSS 渐变近似），
    图例看不出虚实；这里直接复用 SVG 的 ``stroke-dasharray``，屏幕与打印都一致。
    """
    dash = style.get("dash")
    dash_attr = ' stroke-dasharray="%s"' % dash if dash else ""
    return (
        '<svg class="%s" width="24" height="8" viewBox="0 0 24 8" aria-hidden="true"'
        ' focusable="false"><line x1="1" y1="4" x2="23" y2="4" stroke="%s"'
        ' stroke-width="%s"%s stroke-linecap="round"/></svg>'
        % (css_class, color, style.get("width", 1.7), dash_attr)
    )


def _series_swatch(defn: Dict[str, Any]) -> str:
    """按曲线定义画图例线段（颜色 + 线宽 + dash）。"""
    return _line_swatch(
        defn["color"],
        {"dash": defn.get("dash"), "width": defn.get("width", 1.7)},
    )


def _wobble_ring(cx: float, cy: float, rx: float, ry: float, phase: float = 0.0,
                 amp: float = 0.055, n: int = 40) -> str:
    """手绘圈：一笔画出来的椭圆/圆。

    以前是 40 个正弦抖动点连成折线，转折太多、像机绘锯齿；真人用笔画的圈
    只有几个大的起伏。这里改成 **8 个锚点 + Catmull-Rom 转三次贝塞尔**，
    半径只做低频起伏（2/3 次谐波），轮廓平滑、封闭处不出现尖角。
    ``n`` 仅为兼容旧调用保留，不再使用。
    """
    anchors = 8
    points: List[Tuple[float, float]] = []
    for index in range(anchors):
        angle = 2.0 * math.pi * index / anchors
        wobble = (
            1.0
            + amp * 0.55 * math.sin(2.0 * angle + phase)
            + amp * 0.35 * math.sin(3.0 * angle + phase * 1.7)
        )
        points.append(
            (
                cx + rx * wobble * math.cos(angle),
                cy + ry * wobble * math.sin(angle),
            )
        )

    parts = ["M%.1f,%.1f" % points[0]]
    for index in range(anchors):
        p0 = points[(index - 1) % anchors]
        p1 = points[index]
        p2 = points[(index + 1) % anchors]
        p3 = points[(index + 2) % anchors]
        c1 = (p1[0] + (p2[0] - p0[0]) / 6.0, p1[1] + (p2[1] - p0[1]) / 6.0)
        c2 = (p2[0] - (p3[0] - p1[0]) / 6.0, p2[1] - (p3[1] - p1[1]) / 6.0)
        parts.append(
            "C%.1f,%.1f %.1f,%.1f %.1f,%.1f"
            % (c1[0], c1[1], c2[0], c2[1], p2[0], p2[1])
        )
    parts.append("Z")
    return "".join(parts)


def _text_width(text: str, size: float = 10.5) -> float:
    """粗略估算文字宽度（CJK ≈ 1.05em，ASCII ≈ 0.66em + 字距），用于标注条排布。"""
    width = 0.0
    for char in text:
        width += size * (1.05 if ord(char) > 0x2E80 else 0.66)
    return width + 2.0


def _box_hits(box: Tuple[float, float, float, float],
              boxes: Sequence[Tuple[float, float, float, float]]) -> bool:
    x, y, w, h = box
    for bx, by, bw, bh in boxes:
        if x < bx + bw + 4 and bx < x + w + 4 and y < by + bh + 3 and by < y + h + 3:
            return True
    return False


def _place_label(
    px: float, py: float, text: str,
    boxes: List[Tuple[float, float, float, float]],
    y0: float, y1: float,
) -> Tuple[float, float, float, float]:
    """给标注条找一个不压曲线、不出图框的位置；返回 ``(x, y, w, h)``。"""
    width = _text_width(text) + 22.0
    height = 19.0
    left_limit, right_limit = PLOT_LEFT + 2.0, PLOT_RIGHT - 2.0
    candidates = [
        (px + 14.0, py - height - 10.0),
        (px + 14.0, py + 10.0),
        (px - width - 14.0, py - height - 10.0),
        (px - width - 14.0, py + 10.0),
        (px + 14.0, py - height / 2.0),
        (px - width - 14.0, py - height / 2.0),
    ]
    chosen: Optional[Tuple[float, float, float, float]] = None
    for cx, cy in candidates:
        box = (cx, cy, width, height)
        if cx < left_limit or cx + width > right_limit:
            continue
        if cy < y0 + 1 or cy + height > y1 - 1:
            continue
        if not _box_hits(box, boxes):
            chosen = box
            break
    if chosen is None:
        x = min(max(candidates[0][0], left_limit), right_limit - width)
        y = min(max(candidates[0][1], y0 + 1), y1 - height - 1)
        chosen = (x, y, width, height)
    boxes.append(chosen)
    return chosen


def _svg_annotations(
    annotations: Sequence[Dict[str, Any]],
    y0: float,
    y1: float,
) -> str:
    """画审阅批注：屏幕层（红十字/方框 + 浅色标注条）与打印层（手绘双圈 + 打字机注）。

    图上只写「编号 + 缩写 code」（``① IAS_MAX``），完整中文名 / 数值 / 时间在
    图下方的对照表（``.ann-legend``）里。两层都在 DOM 里，用 ``@media print``
    切换；``body.no-annot`` 关掉打印层（图上标记与 legend 一起隐藏）。

    ``annotations`` 由 :func:`_render_chart` 预处理：每项已带 ``px`` / ``py`` / ``num``。
    """
    if not annotations:
        return ""
    parts: List[str] = []
    boxes: List[Tuple[float, float, float, float]] = []
    ring_slots: List[Tuple[float, float]] = []
    for order, annotation in enumerate(annotations):
        px = float(annotation["px"])
        py = float(annotation["py"])
        text = "%s %s" % (
            annotation.get("num") or _annotation_number(order),
            _annotation_mark_text(annotation),
        )
        bx, by, bw, bh = _place_label(px, py, text, boxes, y0, y1)
        anchor = "start" if bx >= px else "end"
        tx = bx + 8.0 if bx >= px else bx + bw - 8.0
        ty = by + 13.0
        # 引线：从数据点连到标注条最近的边
        edge_x = bx if bx >= px else bx + bw
        edge_y = by + bh / 2.0
        group: List[str] = ['<g class="ann ann-screen">']
        group.append(
            '<line class="ann-mark" x1="%.1f" y1="%.1f" x2="%.1f" y2="%.1f"'
            ' opacity="0.9"/>' % (px, py, edge_x, edge_y)
        )
        if annotation.get("dimension"):
            group.append(
                '<line class="ann-mark" x1="%.1f" y1="%.1f" x2="%.1f" y2="%.1f"'
                ' stroke-dasharray="4 4" opacity="0.55"/>' % (px, py, px, y1)
            )
            group.append(
                '<line class="ann-mark" x1="%.1f" y1="%.1f" x2="%.1f" y2="%.1f"/>'
                % (px - 5, y1, px + 5, y1)
            )
        group.append(
            '<path class="ann-mark" d="M%.1f,%.1f L%.1f,%.1f M%.1f,%.1f L%.1f,%.1f"/>'
            % (px - 6, py, px + 6, py, px, py - 6, px, py + 6)
        )
        group.append(
            '<rect class="ann-mark" x="%.1f" y="%.1f" width="14" height="14"'
            ' stroke-dasharray="3 2"/>' % (px - 7, py - 7)
        )
        group.append(
            '<rect class="ann-box" x="%.1f" y="%.1f" width="%.1f" height="%.1f"/>'
            % (bx, by, bw, bh)
        )
        group.append(
            '<text class="ann-ink" x="%.1f" y="%.1f" text-anchor="%s"'
            ' font-size="10.5" font-family="Consolas,\'Courier New\',monospace">%s</text>'
            % (tx, ty, anchor, html.escape(text))
        )
        group.append("</g>")
        parts.append("".join(group))

        # ---- 打印层：手绘双圈 + 打字机短注（同样只写编号 + code） ----
        ring_rx, ring_ry = 30.0, 15.0
        ring_cy = py
        if ring_cy - ring_ry < y0 + 1:
            ring_cy = y0 + ring_ry + 1
        if ring_cy + ring_ry > y1 - 1:
            ring_cy = y1 - ring_ry - 1
        print_text = text
        ptw = _text_width(print_text, 11.0)
        ptx = px + ring_rx + 10.0
        if ptx + ptw > PLOT_RIGHT - 4:
            ptx = px - ring_rx - 10.0 - ptw
        pty = ring_cy + 4.0
        for slot in ring_slots:
            if abs(slot[1] - ring_cy) < 26 and abs(slot[0] - px) < 70:
                pty = ring_cy + 20.0
                break
        ring_slots.append((px, ring_cy))
        pgroup: List[str] = ['<g class="ann ann-print">']
        pgroup.append(
            '<path class="ann-crayon" d="%s" stroke-width="3.4" opacity="0.8"/>'
            % _wobble_ring(px, ring_cy, ring_rx, ring_ry, order * 1.7)
        )
        pgroup.append(
            '<path class="ann-crayon" d="%s" stroke-width="1.7" opacity="0.55"/>'
            % _wobble_ring(px, ring_cy, ring_rx * 0.9, ring_ry * 0.86, order * 1.7 + 2.2)
        )
        pgroup.append(
            '<path class="ann-crayon-arrow" d="M%.1f,%.1f L%.1f,%.1f"/>'
            % (ptx - 6.0 if ptx > px else ptx + ptw + 6.0, pty - 4.0,
               px + (ring_rx - 2.0) if ptx > px else px - (ring_rx - 2.0), ring_cy - 2.0)
        )
        pgroup.append(
            '<text class="ann-type" x="%.1f" y="%.1f" text-anchor="%s" font-size="11">%s</text>'
            % (ptx, pty, "start" if ptx > px else "end", html.escape(print_text))
        )
        pgroup.append("</g>")
        parts.append("".join(pgroup))
    return "".join(parts)


def _svg_event_marks(
    marks: Sequence[Dict[str, Any]],
    y0: float,
    y1: float,
) -> str:
    """死亡/坠毁等事件的"审阅圈"（只用于概览 / 能量页；竖线由事件层统一画）。

    图上只写「编号 + 缩写 code」（``③ DEATH``）；数值 / 时间 / 说明进 legend。
    ``marks`` 由 :func:`_render_chart` 预处理：已带 ``px`` / ``num``。
    """
    if not marks:
        return ""
    parts: List[str] = []
    boxes: List[Tuple[float, float, float, float]] = []
    for order, mark in enumerate(marks):
        px = float(mark["px"])
        py = y0 + 26.0
        text = "%s %s" % (
            mark.get("num") or _annotation_number(order),
            annotation_code(mark.get("kind")),
        )
        bx, by, bw, bh = _place_label(px, py, text, boxes, y0, y1)
        parts.append(
            '<g class="ann ann-screen">'
            '<path class="ann-mark" d="M%.1f,%.1f L%.1f,%.1f M%.1f,%.1f L%.1f,%.1f"/>'
            '<rect class="ann-mark" x="%.1f" y="%.1f" width="15" height="15"'
            ' stroke-dasharray="3 2"/>'
            '<line class="ann-mark" x1="%.1f" y1="%.1f" x2="%.1f" y2="%.1f"/>'
            '<rect class="ann-box" x="%.1f" y="%.1f" width="%.1f" height="%.1f"/>'
            '<text class="ann-ink" x="%.1f" y="%.1f" text-anchor="start" font-size="10.5"'
            ' font-family="Consolas,\'Courier New\',monospace">%s</text>'
            '</g>'
            % (
                px - 8, py - 8, px + 8, py + 8, px + 8, py - 8, px - 8, py + 8,
                px - 8, py - 8,
                bx, by + bh / 2.0,
                px + (8.0 if bx >= px else -8.0), py,
                bx, by, bw, bh, bx + 8.0, by + 13.0, html.escape(text),
            )
        )
        ring_cy = max(y0 + 16.0, py)
        death_text = text
        death_w = _text_width(death_text, 11.0)
        death_x = px + 40.0
        death_anchor = "start"
        if death_x + death_w > PLOT_RIGHT - 4:
            death_x = px - 40.0
            death_anchor = "end"
        parts.append(
            '<g class="ann ann-print">'
            '<path class="ann-crayon" d="%s" stroke-width="3.6" opacity="0.82"/>'
            '<path class="ann-crayon" d="%s" stroke-width="1.8" opacity="0.55"/>'
            '<text class="ann-type" x="%.1f" y="%.1f" text-anchor="%s" font-size="11">%s</text>'
            '</g>'
            % (
                _wobble_ring(px, ring_cy, 34.0, 16.0, 1.1 + order),
                _wobble_ring(px, ring_cy, 30.0, 13.5, 3.3 + order),
                death_x, ring_cy + 4.0, death_anchor,
                html.escape(death_text),
            )
        )
    return "".join(parts)


def _svg_blueprint_chrome(
    left: Optional[_Axis],
    right: Optional[_Axis],
    xaxis: _XAxis,
    y0: float,
    y1: float,
    xticks: Sequence[float],
) -> str:
    """蓝图专属图面元素：图框角标、左尺寸线、底部比例尺（打印时隐藏）。

    屏幕上是"工程图"语汇（尺寸线 / 比例尺）；打印成档案纸时不出现，
    让纸面更像 C 档案袋（曲线 + 红圈）。
    """
    parts: List[str] = ['<g class="bp-dwg">']
    # 绘图区角标（L 形）
    for cx, cy, sx, sy in (
        (PLOT_LEFT, y0, 1, 1), (PLOT_RIGHT, y0, -1, 1),
        (PLOT_LEFT, y1, 1, -1), (PLOT_RIGHT, y1, -1, -1),
    ):
        parts.append(
            '<path d="M%.1f,%.1f L%.1f,%.1f M%.1f,%.1f L%.1f,%.1f"'
            ' fill="none" stroke="%s" stroke-width="1.2"/>'
            % (cx, cy, cx + 14 * sx, cy, cx, cy, cx, cy + 14 * sy, COLORS["axis"])
        )
    axis = right if right is not None and left is None else left
    if axis is not None:
        lo, hi = axis.lo, axis.hi
        label = "%s … %s" % (_fmt_number(lo, 1), _fmt_number(hi, 1))
        dim_x = PLOT_LEFT - 40.0
        parts.append(
            '<line x1="%.1f" y1="%.1f" x2="%.1f" y2="%.1f" stroke="%s" stroke-width="0.8"'
            ' stroke-dasharray="6 4" opacity="0.7"/>'
            % (PLOT_LEFT, y0, dim_x, y0, COLORS["axis"])
        )
        parts.append(
            '<line x1="%.1f" y1="%.1f" x2="%.1f" y2="%.1f" stroke="%s" stroke-width="0.8"'
            ' stroke-dasharray="6 4" opacity="0.7"/>'
            % (PLOT_LEFT, y1, dim_x, y1, COLORS["axis"])
        )
        parts.append(
            '<line x1="%.1f" y1="%.1f" x2="%.1f" y2="%.1f" stroke="%s" stroke-width="0.9"/>'
            % (dim_x, y0, dim_x, y1, COLORS["axis"])
        )
        parts.append(
            '<line x1="%.1f" y1="%.1f" x2="%.1f" y2="%.1f" stroke="%s" stroke-width="0.9"/>'
            % (dim_x - 4, y0, dim_x + 4, y0, COLORS["axis"])
        )
        parts.append(
            '<line x1="%.1f" y1="%.1f" x2="%.1f" y2="%.1f" stroke="%s" stroke-width="0.9"/>'
            % (dim_x - 4, y1, dim_x + 4, y1, COLORS["axis"])
        )
        mid = (y0 + y1) / 2.0
        parts.append(
            '<text x="%.1f" y="%.1f" text-anchor="middle" font-size="9" fill="%s"'
            ' transform="rotate(-90 %.1f %.1f)" letter-spacing="1">%s</text>'
            % (dim_x - 6, mid, COLORS["axis"], dim_x - 6, mid,
               html.escape("RANGE " + label))
        )
    # 底部比例尺：60 s 对应的像素长度（贴在图线内侧，避开时间刻度）
    if len(xticks) >= 2:
        span = xticks[-1] - xticks[0]
        if span > 0:
            seg = min(60.0, span)
            start_t = xticks[0]
            bar_x0 = xaxis(start_t)
            bar_x1 = xaxis(start_t + seg)
            bar_y = y1 - 8.0
            if bar_x1 - bar_x0 > 8:
                parts.append(
                    '<line x1="%.1f" y1="%.1f" x2="%.1f" y2="%.1f" stroke="%s"'
                    ' stroke-width="1" opacity="0.8"/>'
                    % (bar_x0, bar_y, bar_x1, bar_y, COLORS["axis"])
                )
                for frac in (0.0, 0.5, 1.0):
                    tick_x = bar_x0 + (bar_x1 - bar_x0) * frac
                    parts.append(
                        '<line x1="%.1f" y1="%.1f" x2="%.1f" y2="%.1f" stroke="%s"'
                        ' stroke-width="1" opacity="0.8"/>'
                        % (tick_x, bar_y - 3, tick_x, bar_y + 3, COLORS["axis"])
                    )
                parts.append(
                    '<text x="%.1f" y="%.1f" text-anchor="start" font-size="9" fill="%s"'
                    ' letter-spacing="1" opacity="0.9">%s</text>'
                    % (bar_x1 + 8.0, bar_y + 3.0, COLORS["axis"],
                       html.escape("SCALE %d s" % round(seg)))
                )
    parts.append("</g>")
    return "".join(parts)


def _render_chart(
    panel: Dict[str, Any],
    times: Sequence[float],
    series: Dict[str, Sequence[Optional[float]]],
    t0: float,
    t1: float,
    chart_id: str,
    t_label: bool,
    events: Sequence[Dict[str, Any]] = (),
    regions: Sequence[Dict[str, Any]] = (),
    vlines: Sequence[Dict[str, Any]] = (),
    avg: Optional[Dict[str, Any]] = None,
    annotations: Sequence[Dict[str, Any]] = (),
    event_marks: Sequence[Dict[str, Any]] = (),
    event_legend: Sequence[Dict[str, Any]] = (),
    gap_events: Sequence[Dict[str, Any]] = (),
    gap_times: Optional[Sequence[float]] = None,
) -> Optional[Dict[str, Any]]:
    """画一张图，返回 (svg, 图例, 悬停几何)。没有数据时返回 None。

    ``avg`` 非空时（只有速度页用）额外画"平均速度统计区间"图层：半透明竖条 +
    边界虚线 + 平均 IAS/TAS/Mach 的水平虚线；默认范围 = 整个架次。

    ``annotations`` / ``event_marks`` 是审阅批注（屏幕十字/方框 + 打印手绘双圈）
    与事件圈（死亡/坠毁只在概览、能量页传）。批注按“值 → 轴”解析到左/右轴。

    ``event_legend`` 是"只进图下 legend、不在图面加圈"的事件（0.5.2 的 KILL）：
    竖线由 ``events`` 统一画，这里只补一行 ``④ KILL 击落 <target> · T+mm:ss``，
    编号顺延在数据批注 / 事件圈之后，不和数据批注编号冲突。

    数据中断（相邻样本 > 1 s，或 gap 事件）在这里统一处理：曲线层（
    :func:`_svg_series`）画虚线贝塞尔桥，本函数画淡灰竖带/斜纹并把
    ``GAP 数据中断 2.66s · T+00:19`` 追加到图下 legend（编号继续顺延，
    不占数据批注的 2 条上限）。
    """
    height = float(panel["height"])
    y0, y1 = PAD_TOP, height - PAD_BOTTOM
    xaxis = _XAxis(t0, t1, PLOT_LEFT, PLOT_RIGHT)

    defs: List[Dict[str, Any]] = []
    for defn in panel["series"]:
        values = series.get(defn["key"])
        if values is None:
            continue
        if not any(v is not None for v in values):
            continue
        defs.append(defn)
    if not defs:
        return None

    left_values: List[float] = []
    right_values: List[float] = []
    for defn in defs:
        values = series.get(defn["key"], [])
        bucket = right_values if defn["axis"] == "right" else left_values
        bucket.extend(v for v in values if v is not None)
    left_axis: Optional[_Axis] = None
    right_axis: Optional[_Axis] = None
    if left_values:
        left_axis = _make_axis(left_values, y1, y0)
    if right_values:
        right_axis = _make_axis(right_values, y1, y0)

    xstep, xticks = _time_ticks(t0, t1, target=8)

    # 数据中断：自动检测（样本间隔 > 1 s）为主，gap 事件合并去重（老日志没有事件也可用）。
    # 全局总览页的曲线是抽稀后的 times，自动检测要用**原始**时间轴（gap_times），
    # 否则抽稀间隔会被误判成空洞。
    gap_source: Sequence[float] = times if gap_times is None else gap_times
    gap_list = _gap_items_in_range(_gap_items(gap_source, gap_events), t0, t1)
    # 竖带/斜纹只画"样本里真有空洞"的中断；只有事件、样本连续的情况只进 legend
    # （没有可标记的空洞区间，画带会误导成"这里没数据"）。
    band_items = [item for item in gap_list if item.get("source") != "event"]

    parts: List[str] = [
        '<svg id="svg-%s" viewBox="0 0 %.0f %.1f" class="chart-svg"'
        ' preserveAspectRatio="none" role="img" aria-label="%s">'
        % (chart_id, VIEW_W, height, html.escape(panel["title"]))
    ]
    parts.append(_svg_gap_bands(band_items, xaxis, t0, t1, y0, y1, chart_id))
    parts.append(_svg_regions(regions, xaxis, t0, t1, y0, y1))
    parts.append(_svg_vlines(vlines, xaxis, t0, t1, y0, y1))
    parts.append(_svg_axes(left_axis, right_axis, xstep, xticks, xaxis, t_label, height, y0, y1))
    parts.append(_svg_events(events, xaxis, t0, t1, y0, y1))
    # 悬停层放在曲线下面，曲线再覆盖上去；十字线由 JS 追加在最上层
    hover_group: List[str] = []
    for defn in defs:
        axis = right_axis if defn["axis"] == "right" else left_axis
        if axis is None:
            continue
        values = series.get(defn["key"], [])
        if not any(v is not None for v in values):
            continue
        hover_group.append(_svg_series(defn, times, values, xaxis, axis))
    parts.append("".join(hover_group))
    if avg is not None:
        parts.append(_svg_avg_overlay(avg, chart_id, xaxis, left_axis, right_axis, y0, y1))

    # ---- 审阅批注：先按「值 → 轴」解析出像素点并编号，图面与 legend 共用同一套编号 ----
    ann_items: List[Dict[str, Any]] = []
    if annotations:
        axis_for = {defn["key"]: defn["axis"] for defn in defs}
        for annotation in annotations:
            item = dict(annotation)
            item["axis"] = axis_for.get(item.get("series") or "", "left")
            axis = right_axis if item["axis"] == "right" else left_axis
            value = item.get("value")
            if axis is None or value is None:
                continue
            try:
                px = xaxis(float(item["t"]))
                py = axis(float(value))
            except (TypeError, ValueError):
                continue
            if py is None or not math.isfinite(py):
                continue
            if not (PLOT_LEFT - 1.0 <= px <= PLOT_RIGHT + 1.0):
                continue
            item["px"] = px
            item["py"] = min(max(py, y0 + 2.0), y1 - 2.0)
            ann_items.append(item)
    for index, item in enumerate(ann_items):
        item["num"] = _annotation_number(index)

    mark_items: List[Dict[str, Any]] = []
    for mark in event_marks:
        try:
            t = float(mark["t"])
        except (KeyError, TypeError, ValueError):
            continue
        if t < t0 - 5.0 or t > t1 + 5.0:
            continue
        item = dict(mark)
        item["px"] = min(max(xaxis(t), PLOT_LEFT), PLOT_RIGHT)
        item["num"] = _annotation_number(len(ann_items) + len(mark_items))
        mark_items.append(item)

    # 只进 legend 的事件（KILL）：竖线已在 events 层画好，这里按事件编号顺延。
    legend_items: List[Dict[str, Any]] = []
    legend_base = len(ann_items) + len(mark_items)
    for event in event_legend:
        try:
            t = float(event["t"])
        except (KeyError, TypeError, ValueError):
            continue
        if t < t0 - 0.05 or t > t1 + 0.05:
            continue
        item = dict(event)
        item["index"] = legend_base + len(legend_items)
        legend_items.append(item)

    if ann_items:
        parts.append(_svg_annotations(ann_items, y0, y1))
    if mark_items:
        parts.append(_svg_event_marks(mark_items, y0, y1))

    if ACTIVE_THEME.key == "blueprint":
        parts.append(
            _svg_blueprint_chrome(left_axis, right_axis, xaxis, y0, y1, xticks)
        )
    parts.append("</svg>")

    legend_parts: List[str] = []
    for defn in defs:
        legend_parts.append(
            '<span class="lg">%s<span class="lg-t">%s</span></span>'
            % (_series_swatch(defn), html.escape(defn["label"]))
        )
    if avg is not None:
        legend_parts.append(
            '<span class="lg avg-legend">%s'
            '<span class="lg-t">平均速度（水平虚线，数值见上方；底纹 = 统计区间）</span></span>'
            % _line_swatch(COLORS["avg_speed"], line_style("avg_speed"))
        )

    # 批注对照表：图上只有 ① IAS_MAX，这里给出「缩写 + 全称 + 值 + 时间」。
    ann_legend_parts: List[str] = []
    for index, item in enumerate(ann_items):
        ann_legend_parts.append(
            '<span class="ai">%s</span>'
            % html.escape(_annotation_legend_line(item, index))
        )
    for index, item in enumerate(mark_items):
        ann_legend_parts.append(
            '<span class="ai">%s</span>'
            % html.escape(_death_legend_line(item, len(ann_items) + index))
        )
    # 击落（KILL）legend：每页都出（竖线每页都画），关闭「保留批注」时随整块隐藏。
    for item in legend_items:
        ann_legend_parts.append(
            '<span class="ai">%s</span>'
            % html.escape(_event_legend_line(item, int(item["index"])))
        )
    # 数据中断（GAP）legend：编号继续顺延（不占数据批注上限）；竖带/斜纹 + 虚线桥
    # 在每张图上都画，legend 只说明"这处空洞多大、发生在哪"。
    gap_legend: List[Dict[str, Any]] = []
    gap_base = legend_base + len(legend_items)
    for item in gap_list:
        entry = dict(item)
        entry["index"] = gap_base + len(gap_legend)
        gap_legend.append(entry)
    for item in gap_legend:
        ann_legend_parts.append(
            '<span class="ai gap-ai">%s</span>'
            % html.escape(_gap_legend_line(item, int(item["index"])))
        )

    geometry = {
        "id": chart_id,
        "title": panel["title"],
        "x0": PLOT_LEFT,
        "x1": PLOT_RIGHT,
        "y0": y0,
        "y1": y1,
        "left": left_axis.as_dict() if left_axis is not None else None,
        "right": right_axis.as_dict() if right_axis is not None else None,
        "series": [
            {
                "key": defn["key"],
                "label": defn["label"],
                "color": defn["color"],
                "axis": defn["axis"],
                "clip": defn.get("clip"),
                "decimals": defn.get("decimals", 1),
                "unit": defn.get("unit", ""),
                "tooltip": defn.get("tooltip", True),
                "signed": bool(defn.get("signed")),
                "tooltipLabel": defn.get("tooltip_label"),
            }
            for defn in defs
        ],
    }
    return {
        "svg": "".join(parts),
        "legend": "".join(legend_parts),
        "ann_legend": "".join(ann_legend_parts),
        "geometry": geometry,
        "left_unit": panel.get("left_unit"),
        "right_unit": panel.get("right_unit"),
    }


def _render_event_strip(
    events: Sequence[Dict[str, Any]], t0: float, t1: float, strip_id: str
) -> Optional[str]:
    visible = [
        e
        for e in events
        if e.get("t") is not None and t0 - 0.05 <= e["t"] <= t1 + 0.05
    ]
    if not visible:
        return None
    height = 38.0
    xaxis = _XAxis(t0, t1, PLOT_LEFT, PLOT_RIGHT)
    parts: List[str] = [
        '<svg id="%s" viewBox="0 0 %.0f %.1f" class="strip-svg"'
        ' preserveAspectRatio="none" role="img" aria-label="事件时间线">'
        % (strip_id, VIEW_W, height)
    ]
    row_last_x = [-1e9, -1e9]
    for event in visible:
        t = event["t"]
        px = min(max(xaxis(t), PLOT_LEFT), PLOT_RIGHT)
        kind = event.get("kind", "")
        color = EVENT_COLORS.get(kind, COLORS["other"])
        label = event.get("label") or EVENT_LABELS.get(kind, kind)
        text = "%s %s" % (label, _fmt_number(t, 1) + "s")
        # 击落事件顺带标出目标：色条上直接写「击落 =FdX= Target 312.4s」。
        if kind == "kill" and event.get("target"):
            text = "%s %s %s" % (label, event["target"], _fmt_number(t, 1) + "s")
        parts.append(
            '<line x1="%s" y1="10" x2="%s" y2="%.0f" stroke="%s" stroke-width="1.4"'
            ' opacity="0.9"/>'
            % (_fmt_coord(px), _fmt_coord(px), height, color)
        )
        parts.append(
            '<circle cx="%s" cy="10" r="2.6" fill="%s"/>' % (_fmt_coord(px), color)
        )
        row = 0
        if px - row_last_x[0] < 88 and px - row_last_x[1] >= 88:
            row = 1
        row_last_x[row] = px
        text_y = 12.0 if row == 0 else 25.0
        anchor = "middle"
        text_x = px
        if px < PLOT_LEFT + 40:
            anchor = "start"
            text_x = px + 3
        elif px > PLOT_RIGHT - 40:
            anchor = "end"
            text_x = px - 3
        parts.append(
            '<text x="%s" y="%s" text-anchor="%s" font-size="11" fill="%s">%s</text>'
            % (
                _fmt_coord(text_x),
                _fmt_coord(text_y),
                anchor,
                color,
                html.escape(text),
            )
        )
    parts.append("</svg>")
    return "".join(parts)


# ---------------------------------------------------------------------------
# 数据准备
# ---------------------------------------------------------------------------


def _events_dicts(flight: Flight) -> List[Dict[str, Any]]:
    items: List[Dict[str, Any]] = []
    for event in flight.events:
        if event.t is None:
            continue
        item = {
            "kind": event.kind,
            "label": EVENT_LABELS.get(event.kind, event.kind),
            "t": event.t,
            "target": event.target,
        }
        if event.gap is not None:
            item["gap"] = float(event.gap)
        items.append(item)
    return items


def _visible_events(events: Sequence[Dict[str, Any]], t0: float, t1: float) -> List[Dict[str, Any]]:
    return [e for e in events if e["t"] is not None and t0 - 0.05 <= e["t"] <= t1 + 0.05]


def _gap_items(
    times: Sequence[float], gap_events: Sequence[Dict[str, Any]] = ()
) -> List[Dict[str, Any]]:
    """收集图表要标出来的**数据中断**（空洞）列表。

    口径（与另一路 C# 的 ``event kind:"gap"`` 兼容、老日志也能工作）：

    * 自动检测：同一架次内**相邻样本间隔 >** :data:`PATH_GAP_S`（1 s）即一处中断，
      这是主口径（用户实测 220101：8 处 > 1 s，最长 2.66 s）；
    * gap 事件：新日志会带 ``{"kind":"gap","t":...,"gap":<秒>}``，按时间容差
      :data:`GAP_MERGE_S` 与自动检测**合并去重**；只有事件、没有样本空洞的
      （例如样本被补插过）也保留，用于 legend / 竖带，但没有桥可画；
    * 跨度 > :data:`GAP_BRIDGE_MAX_S`（30 s，= 架次切分阈值）不标：那是换局 /
      长暂停，不是 8111 轮询卡顿。

    返回 ``[{"t0","t1","gap","source"[,"event_t","event_gap"]}, ...]``，按 t0 排序。
    """
    items: List[Dict[str, Any]] = []
    for index in range(1, len(times)):
        try:
            prev_t = float(times[index - 1])
            cur_t = float(times[index])
        except (TypeError, ValueError):
            continue
        dt = cur_t - prev_t
        if PATH_GAP_S < dt <= GAP_BRIDGE_MAX_S:
            items.append(
                {"t0": prev_t, "t1": cur_t, "gap": dt, "source": "samples"}
            )
    for event in gap_events or ():
        if str(event.get("kind") or "") != "gap":
            continue  # 只认 gap 事件；调用方偷懒传全部事件也不会被误判
        raw_t = event.get("t")
        if raw_t is None:
            continue
        try:
            moment = float(raw_t)
        except (TypeError, ValueError):
            continue
        size = event.get("gap")
        try:
            size = float(size) if size is not None else None
        except (TypeError, ValueError):
            size = None
        matched: Optional[Dict[str, Any]] = None
        for item in items:
            if "event_t" in item:
                continue
            if item["t0"] - GAP_MERGE_S <= moment <= item["t1"] + GAP_MERGE_S:
                matched = item
                break
        if matched is not None:
            matched["event_t"] = moment
            if size is not None:
                matched["event_gap"] = size
            continue
        if size is not None and PATH_GAP_S < size <= GAP_BRIDGE_MAX_S:
            items.append(
                {
                    "t0": moment - size,
                    "t1": moment,
                    "gap": size,
                    "source": "event",
                    "event_t": moment,
                }
            )
    items.sort(key=lambda item: float(item["t0"]))
    return items


def _gap_items_in_range(
    gaps: Sequence[Dict[str, Any]], t0: float, t1: float
) -> List[Dict[str, Any]]:
    """只保留和图表时间范围有交集的空洞（多架次 / 全局总览用）。"""
    return [
        item
        for item in gaps
        if float(item["t1"]) >= t0 - 1e-6 and float(item["t0"]) <= t1 + 1e-6
    ]


def _integrity_stats(model: LogModel) -> Dict[str, Any]:
    """概览「数据完整性」统计（只在**架次内部**算相邻样本间隔）。

    * ``samples`` —— 样本总数；
    * ``holes`` —— 相邻样本间隔 > :data:`GAP_HOLE_S`（0.2 s）的处数；
    * ``interrupts`` —— 相邻样本间隔 > :data:`PATH_GAP_S`（1 s）的处数
      （= 报告里断线 / 画虚线贝塞尔桥的那些中断）；
    * ``longest_s`` —— 最长相邻样本间隔（秒）。

    架次之间本来就不连续（换局 / 长暂停 / 死亡后的空白），跨架次的时间差不算空洞。
    """
    samples = 0
    holes = 0
    interrupts = 0
    longest = 0.0
    for flight in model.flights:
        times = (flight.series or {}).get("t") or [sample["t"] for sample in flight.samples]
        samples += len(times)
        for index in range(1, len(times)):
            try:
                dt = float(times[index]) - float(times[index - 1])
            except (TypeError, ValueError):
                continue
            if dt <= 0.0:
                continue
            if dt > GAP_HOLE_S:
                holes += 1
            if dt > PATH_GAP_S:
                interrupts += 1
            if dt > longest:
                longest = dt
    return {
        "samples": samples,
        "holes": holes,
        "interrupts": interrupts,
        "longest_s": longest,
    }


def _integrity_html(model: LogModel) -> str:
    """概览页 KPI 下面的「数据完整性」一行（老日志同样适用）。"""
    stats = _integrity_stats(model)
    if not stats["samples"]:
        return ""
    if stats["longest_s"] <= 0.0:
        value_text = "—"
    else:
        value_text = _fmt_number(stats["longest_s"], 2) + " s"
    text = "数据完整性：%d 样本 / %d 空洞 / 最大 %s（>1 s 中断 %d 处）" % (
        stats["samples"],
        stats["holes"],
        value_text,
        stats["interrupts"],
    )
    hint = "空洞 = 相邻样本间隔 > 0.2 s（8111 轮询抖动）；中断 = > 1 s（断线 / 贝塞尔桥阈值）"
    return '<div class="integrity" title="%s">%s</div>' % (
        _html_escape(hint),
        _html_escape(text),
    )


def _invalid_regions(series: Dict[str, Sequence[Optional[float]]]) -> List[Dict[str, Any]]:
    """valid=false 的连续区间（>=0.25 s 才画）。"""
    times = series["t"]
    valid = series["valid"]
    regions: List[Dict[str, Any]] = []
    start: Optional[float] = None
    for t, v in zip(times, valid):
        if v:
            if start is not None:
                regions.append({"t0": start, "t1": t, "color": COLORS["invalid"], "opacity": 0.10})
                start = None
        else:
            if start is None:
                start = t
    if start is not None and times:
        regions.append({"t0": start, "t1": times[-1], "color": COLORS["invalid"], "opacity": 0.10})
    return [r for r in regions if r["t1"] - r["t0"] >= 0.25]


def _post_death_regions(flight: Flight) -> List[Dict[str, Any]]:
    if flight.death_t is None or not flight.samples:
        return []
    end_t = flight.samples[-1]["t"]
    if end_t <= flight.death_t + 0.1:
        return []
    return [{"t0": flight.death_t, "t1": end_t, "color": COLORS["death_region"], "opacity": 0.10}]


def _render_all_panels(
    specs: Sequence[Dict[str, Any]],
    times: Sequence[float],
    series: Dict[str, Sequence[Optional[float]]],
    t0: float,
    t1: float,
    id_prefix: str,
    events: Sequence[Dict[str, Any]] = (),
    regions: Sequence[Dict[str, Any]] = (),
    vlines: Sequence[Dict[str, Any]] = (),
    event_legend: Sequence[Dict[str, Any]] = (),
    gap_events: Sequence[Dict[str, Any]] = (),
    gap_times: Optional[Sequence[float]] = None,
) -> List[Tuple[Dict[str, Any], Dict[str, Any]]]:
    """渲染一组面板；丢弃没有数据的面板，并让最后一个面板显示时间刻度。"""
    rendered: List[Tuple[Dict[str, Any], Dict[str, Any]]] = []
    for spec in specs:
        chart_id = "%s-%s" % (id_prefix, spec["key"])
        block = _render_chart(
            spec, times, series, t0, t1, chart_id, False, events, regions, vlines,
            event_legend=event_legend, gap_events=gap_events, gap_times=gap_times,
        )
        if block is not None:
            rendered.append((spec, block))
    if not rendered:
        return []
    spec, _block = rendered[-1]
    chart_id = "%s-%s" % (id_prefix, spec["key"])
    last = _render_chart(
        spec, times, series, t0, t1, chart_id, True, events, regions, vlines,
        event_legend=event_legend, gap_events=gap_events, gap_times=gap_times,
    )
    if last is not None:
        rendered[-1] = (spec, last)
    return rendered


def _build_flight_payload(
    flight: Flight, limits: Optional[SpeedLimits] = None
) -> Dict[str, Any]:
    series = flight.series
    times = series["t"]
    t0, t1 = flight.start_t, flight.end_t
    if t1 - t0 < 0.5:
        t1 = t0 + 0.5

    payload_series: Dict[str, List[Optional[float]]] = {}
    for key, values in series.items():
        payload_series[key] = _round_series(values, _series_digits(key))

    all_events = _events_dicts(flight)
    # gap（数据中断）不画竖线/不进事件条：曲线断口 + 淡灰竖带 + legend 已经说明；
    # 这里单独拆出来给图表做"自动检测 + gap 事件合并"（老日志没有 gap 事件也能用）。
    gap_events: List[Dict[str, Any]] = [e for e in all_events if e["kind"] == "gap"]
    events = _visible_events([e for e in all_events if e["kind"] != "gap"], t0, t1)
    regions = _invalid_regions(series) + _post_death_regions(flight)
    vne_kmh, vne_source = (None, "")
    if limits is not None:
        vne_kmh, vne_source = limits.vne_for(flight.vehicle)
    annotations = compute_annotations(flight, vne_kmh)
    event_marks: List[Dict[str, Any]] = []
    if flight.death_t is not None:
        event_marks = [
            {"kind": "death", "t": flight.death_t, "label": "DEATH", "note": "数据终止"}
        ]
    # 击落事件（0.5.2）：竖线每页都画（已经在 events 里），图下 legend 每页补一行。
    kill_events: List[Dict[str, Any]] = [
        {"kind": "kill", "t": event.t, "label": "KILL", "target": event.target}
        for event in flight.events
        if event.kind == "kill" and event.t is not None
    ]

    # 每个数据主题渲染一张独立的 SVG（每页一张，都带时间刻度）
    blocks: Dict[str, Dict[str, Any]] = {}
    panels: List[Dict[str, Any]] = []
    default_avg: Optional[Dict[str, Any]] = None
    for spec in _flight_panel_specs():
        chart_id = "f%d-%s" % (flight.index, spec["key"])
        chart_avg: Optional[Dict[str, Any]] = None
        if spec["key"] == "speed":
            # 默认统计范围 = 整个当前架次（用户可在页面上手动改）
            chart_avg = average_speed(flight.samples, t0, t1)
            default_avg = chart_avg
        chart_marks = event_marks if spec["key"] == "energy" else ()
        block = _render_chart(
            spec, times, series, t0, t1, chart_id, True,
            events=events, regions=regions, avg=chart_avg,
            annotations=annotations.get(spec["key"], ()),
            event_marks=chart_marks,
            event_legend=kill_events,
            gap_events=gap_events,
        )
        if block is None:
            continue
        blocks[spec["key"]] = block
        panels.append(block["geometry"])

    thumb_spec = _thumb_panel_spec()
    thumb = _render_chart(
        thumb_spec,
        times,
        series,
        t0,
        t1,
        "ovf%d-es" % flight.index,
        True,
        events=events,
        regions=regions,
        event_marks=event_marks,
        event_legend=kill_events,
        gap_events=gap_events,
    )
    if thumb is not None:
        panels.append(thumb["geometry"])

    player = getattr(flight, "player", None)
    title = "架次 %d · %s · %s – %s" % (
        flight.index,
        flight.vehicle or "未知机型",
        _fmt_number(t0, 1) + "s",
        _fmt_number(flight.end_t, 1) + "s",
    )
    if player:
        title = "架次 %d · 玩家 %s · %s · %s – %s" % (
            flight.index,
            player,
            flight.vehicle or "未知机型",
            _fmt_number(t0, 1) + "s",
            _fmt_number(flight.end_t, 1) + "s",
        )
    return {
        "index": flight.index,
        "title": title,
        "vehicle": flight.vehicle or "未知机型",
        "player": player,
        "reason_label": flight.stats.get("reason_label"),
        "t0": t0,
        "t1": t1,
        "times": payload_series["t"],
        "series": payload_series,
        "panels": panels,
        "blocks": blocks,
        "thumb": thumb,
        "avg_default": default_avg,
        "events": events,
        "badges": _flight_badges(flight),
        "annotations": annotations,
        "event_marks": event_marks,
        "kill_events": kill_events,
        "gap_events": gap_events,
        "vne_kmh": vne_kmh,
        "vne_source": vne_source,
        "death_t": flight.death_t,
        "stats": flight.stats,
    }


def _overview_payload(model: LogModel) -> Optional[Dict[str, Any]]:
    flights = [f for f in model.flights if f.samples]
    if len(flights) < 2:
        return None
    times: List[float] = []
    merged: Dict[str, List[Optional[float]]] = {
        key: [] for key in ("ias", "tas", "mach", "alt", "ralt", "es", "valid", "inflight")
    }
    for flight in flights:
        times.extend(flight.series["t"])
        for key in merged:
            merged[key].extend(flight.series.get(key, []))
    if not times:
        return None
    # 抽稀：总览只需要形状，悬停用抽稀点也够用
    keep: List[int] = []
    if len(times) > MAX_OVERVIEW_POINTS:
        step = len(times) / float(MAX_OVERVIEW_POINTS)
        pos = 0.0
        seen = -1
        while pos < len(times):
            idx = min(len(times) - 1, int(round(pos)))
            if idx != seen:
                keep.append(idx)
                seen = idx
            pos += step
        if not keep or keep[-1] != len(times) - 1:
            keep.append(len(times) - 1)
    else:
        keep = list(range(len(times)))
    times_f = [times[i] for i in keep]
    series_f = {key: [vals[i] for i in keep] for key, vals in merged.items()}
    series_f["t"] = times_f

    t0 = min(times_f)
    t1 = max(times_f)
    if t1 - t0 < 0.5:
        t1 = t0 + 0.5
    all_events = [
        {
            "kind": e.kind,
            "label": EVENT_LABELS.get(e.kind, e.kind),
            "t": e.t,
            "target": e.target,
            **({"gap": float(e.gap)} if e.gap is not None else {}),
        }
        for e in model.events
        if e.t is not None and t0 - 0.05 <= e.t <= t1 + 0.05
    ]
    # gap 事件单独给曲线层做"中断"标注（不画竖线、不进事件条）
    gap_events = [event for event in all_events if event["kind"] == "gap"]
    events = [event for event in all_events if event["kind"] != "gap"]
    vlines: List[Dict[str, Any]] = []
    for flight in flights[1:]:
        vlines.append(
            {
                "t": flight.start_t,
                "label": "架次 %d" % flight.index,
                "color": COLORS["vline"],
            }
        )
    regions = _invalid_regions(series_f)
    for flight in flights:
        regions.extend(_post_death_regions(flight))

    kill_events = [event for event in events if event["kind"] == "kill"]
    kept = _render_all_panels(
        _overview_panel_specs(),
        times_f,
        series_f,
        t0,
        t1,
        "ov",
        events=events,
        regions=regions,
        vlines=vlines,
        event_legend=kill_events,
        gap_events=gap_events,
        # 这里必须用**抽稀前**的原始时间轴做中断检测
        gap_times=times,
    )
    if not kept:
        return None
    rounded_times = _round_series(times_f, 3)
    return {
        "t0": t0,
        "t1": t1,
        "times": rounded_times,
        "series": {key: _round_series(vals, _series_digits(key)) for key, vals in series_f.items()},
        "panels": [block["geometry"] for _spec, block in kept],
        "html": {
            "blocks": kept,
            "strip": _render_event_strip(events, t0, t1, "ev-overview"),
            "events": events,
        },
    }


# ---------------------------------------------------------------------------
# 卡片 / 徽标
# ---------------------------------------------------------------------------


def _cards(model: LogModel) -> List[Dict[str, str]]:
    totals = model.summary["totals"]
    counts = model.summary["counts"]
    time = model.summary.get("time") or {}
    span = time.get("span_s") or 0.0
    samples = counts.get("samples") or 0
    rate = (samples - 1) / span if span > 0 and samples > 1 else None

    event_counts = model.summary.get("event_counts") or {}
    ev_bits = []
    for kind, label in (
        ("death", "死亡"),
        ("kill", "击落"),
        ("spawn", "复活"),
        ("vehicle_change", "换机型"),
    ):
        if event_counts.get(kind):
            ev_bits.append("%s %d" % (label, event_counts[kind]))
    if event_counts.get("gap"):
        # 数据中断是录制诊断事件，单独说明；不计入"事件数"
        ev_bits.append("数据中断 %d 处" % event_counts["gap"])
    gameplay_events = sum(
        count for kind, count in event_counts.items() if kind not in ("end", "gap")
    )

    ralt_max: Optional[float] = None
    for flight in model.flights:
        value = (flight.stats.get("ralt") or {}).get("max")
        if value is not None and (ralt_max is None or value > ralt_max):
            ralt_max = value

    replay = (model.meta or {}).get("replay") or {}
    is_replay = bool(replay)

    cards = [
        {
            "label": "总时长",
            "value": _fmt_number(totals["duration_s"], 1) + " s",
            "sub": "%s 个样本" % samples,
        },
        {
            "label": "采样率",
            "value": (_fmt_number(rate, 1) + " Hz") if rate else "—",
            "sub": ("ACMI 回放 · %s" % (model.meta.get("rate") or "?"))
            if is_replay
            else ("采样 %s" % (model.meta.get("rate") or "?")),
        },
        {
            "label": "最大 IAS",
            "value": _fmt_number(totals["max_ias_kmh"], 0) + " km/h",
            "sub": "TAS %s km/h" % _fmt_number(totals["max_tas_kmh"], 0),
        },
        {
            "label": "最大 Mach",
            "value": _fmt_number(totals["max_mach"], 2),
            "sub": "最大高度 %s m" % _fmt_number(totals["max_alt_m"], 0),
        },
        {
            "label": "最大高度",
            "value": _fmt_number(totals["max_alt_m"], 0) + " m",
            "sub": "雷达 %s m" % _fmt_number(ralt_max, 0),
        },
        {
            "label": "最大过载",
            "value": _fmt_number(totals["max_ny_g"], 2) + " g",
            "sub": "最大滚转 %s°" % _fmt_number(totals["max_bank_deg"], 1),
        },
        {
            "label": "比能量范围",
            "value": "%s – %s m"
            % (
                _fmt_number(totals["energy_min_m"], 0),
                _fmt_number(totals["energy_max_m"], 0),
            ),
            "sub": "空中能量变化 %s m"
            % _fmt_number(totals.get("energy_delta_inflight_m"), 0),
        },
    ]
    if is_replay:
        selected = replay.get("selected_players") or "—"
        cards.append(
            {
                "label": "玩家",
                "value": str(len(replay.get("players") or [])),
                "sub": "回放估算 · %s" % selected,
            }
        )
    else:
        cards.append(
            {
                "label": "事件数",
                "value": str(gameplay_events),
                "sub": " · ".join(ev_bits) if ev_bits else "无事件",
            }
        )
    return cards


def _flight_badges(flight: Flight) -> List[str]:
    stats = flight.stats
    energy = stats["energy"]
    badges = []
    if getattr(flight, "player", None):
        badges.append("玩家 %s" % flight.player)
    badges.extend(
        [
            "机型 %s" % (flight.vehicle or "未知"),
            "时长 %s s" % _fmt_number(stats["duration_s"], 1),
            "样本 %d" % stats["samples"],
            "最大 IAS %s km/h" % _fmt_number(stats["ias"]["max"], 0),
            "最大 TAS %s km/h" % _fmt_number(stats["tas"]["max"], 0),
            "最大过载 %s g" % _fmt_number(stats["ny"]["abs_max"], 2),
            "Es %s – %s m"
            % (
                _fmt_number(energy["min"], 0),
                _fmt_number(energy["max"], 0),
            ),
            "能量获得 %s / 损失 %s m"
            % (
                _fmt_number(energy["gain"], 0),
                _fmt_number(energy["loss"], 0),
            ),
            "空中能量变化 %s m" % _fmt_number(energy.get("inflight_delta"), 0),
        ]
    )
    if stats.get("death_t") is not None:
        badges.append("死亡于 %s s" % _fmt_number(stats["death_t"], 1))
    return badges


def _html_escape(value: Any) -> str:
    return html.escape("" if value is None else str(value))


# ---------------------------------------------------------------------------
# 页面组装
# ---------------------------------------------------------------------------


def _strip_html(flight_payload: Dict[str, Any], page_key: str) -> str:
    """一页一份事件时间线（id 按 架次+页 唯一，避免重复 id）。"""
    strip = _render_event_strip(
        flight_payload["events"],
        flight_payload["t0"],
        flight_payload["t1"],
        "ev-f%d-%s" % (flight_payload["index"], page_key),
    )
    return strip or ""


def _flight_view_html(
    flight_payload: Dict[str, Any],
    page_key: str,
    block: Optional[Dict[str, Any]],
    is_active: bool,
    badges_html: str = "",
) -> str:
    """一页里"某个架次"的可切换内容：事件条 + 图例 + 图。"""
    parts = [
        '<div class="flight-view%s" data-flight="%d">'
        % (" active" if is_active else "", flight_payload["index"])
    ]
    if badges_html:
        parts.append(badges_html)
    strip = _strip_html(flight_payload, page_key)
    if strip:
        parts.append('<div class="strip">%s</div>' % strip)
    if block is None:
        parts.append('<div class="empty">该架次没有此项数据</div>')
    else:
        parts.append('<div class="legend">%s</div>' % block["legend"])
        parts.append('<div class="chart-wrap">%s</div>' % block["svg"])
        if block.get("ann_legend"):
            parts.append(
                '<div class="ann-legend"><span class="ai-h">批注对照</span>%s</div>'
                % block["ann_legend"]
            )
    parts.append("</div>")
    return "".join(parts)


def _fmt_fixed(value: Any, digits: int) -> str:
    """固定小数位（不做去尾零），保证静态数值和 JS 更新后的格式一致。"""
    if value is None:
        return "—"
    return ("%." + str(digits) + "f") % float(value)


def _fmt_seconds_input(value: float) -> str:
    """秒数输入框的值：最多 3 位小数、去掉尾零（0.342 / 30 / 293.58）。"""
    text = "%.3f" % float(value)
    text = text.rstrip("0").rstrip(".")
    return text or "0"


def _avg_bar_html(flight_payload: Dict[str, Any]) -> str:
    """速度页顶部的"平均速度 + 可手动设置计算范围"控件条。

    静态 HTML 里放的是**默认范围（整个当前架次）**的平均值；内联 JS 加载后
    会用同样的数据重算一遍，并在用户点"应用"时原地更新数值和 SVG 图层。
    """
    avg = flight_payload.get("avg_default") or {}
    t0 = float(flight_payload["t0"])
    t1 = float(flight_payload["t1"])
    flight_index = int(flight_payload["index"])
    channels = (("ias", "IAS", "km/h", 1), ("tas", "TAS", "km/h", 1), ("mach", "Mach", "", 2))
    avg_swatch = _line_swatch(COLORS["avg_speed"], line_style("avg_speed"))
    value_parts: List[str] = []
    for key, label, unit, digits in channels:
        unit_text = (" · " + unit) if unit else ""
        value_parts.append(
            '<div class="avg-val"><span class="avg-k">%s平均 %s%s</span>'
            '<b data-avg-val="%s">%s</b></div>'
            % (avg_swatch, label, unit_text, key, _fmt_fixed(avg.get(key), digits))
        )
    range_start = avg.get("range_start", t0) if avg else t0
    range_end = avg.get("range_end", t1) if avg else t1
    sample_count = avg.get("samples") if avg else None
    if avg:
        meta_text = "范围 %s–%s s · %s 样本 · 底纹 = 统计区间，虚线 = 平均值（回车 = 应用）" % (
            _fmt_fixed(range_start, 1),
            _fmt_fixed(range_end, 1),
            sample_count,
        )
    else:
        meta_text = "该架次没有速度数据"
    disabled = "" if avg else " disabled"
    return (
        '<div class="avgbar" id="avgbar-speed" data-avg-flight="%d"'
        ' data-avg-t0="%s" data-avg-t1="%s">'
        '<div class="avg-title">平均速度<b>可手动设置计算范围</b></div>'
        '<div class="avg-vals">%s</div>'
        '<div class="avg-ctl">'
        '<label class="avg-range">范围'
        '<input id="avg-start-speed" class="avg-input" type="number" data-avg-start'
        ' step="0.1" min="%s" max="%s" value="%s" inputmode="decimal"'
        ' aria-label="平均速度统计开始时间（秒）"%s>'
        '<span>s</span><span class="avg-dash">–</span>'
        '<input id="avg-end-speed" class="avg-input" type="number" data-avg-end'
        ' step="0.1" min="%s" max="%s" value="%s" inputmode="decimal"'
        ' aria-label="平均速度统计结束时间（秒）"%s>'
        "<span>s</span></label>"
        '<button type="button" class="avg-btn" data-avg-apply>应用</button>'
        '<button type="button" class="avg-btn ghost" data-avg-reset>重置</button>'
        "</div>"
        '<div class="avg-meta"><span data-avg-meta>%s</span>'
        '<span class="avg-msg" data-avg-msg role="status" aria-live="polite"></span></div>'
        "</div>"
        % (
            flight_index,
            _fmt_seconds_input(t0),
            _fmt_seconds_input(t1),
            "".join(value_parts),
            _fmt_seconds_input(t0),
            _fmt_seconds_input(t1),
            _fmt_seconds_input(range_start),
            disabled,
            _fmt_seconds_input(t0),
            _fmt_seconds_input(t1),
            _fmt_seconds_input(range_end),
            disabled,
            _html_escape(meta_text),
        )
    )


def _overview_page_html(
    flight_payloads: Sequence[Dict[str, Any]],
    default_index: int,
    cards_html: str,
    warnings_html: str,
    note_html: str = "",
    players_html: str = "",
    integrity_html: str = "",
) -> str:
    views: List[str] = []
    for flight_payload in flight_payloads:
        badges = "".join("<span>%s</span>" % _html_escape(b) for b in flight_payload["badges"])
        views.append(
            _flight_view_html(
                flight_payload,
                "overview",
                flight_payload["thumb"],
                flight_payload["index"] == default_index,
                badges_html='<div class="badges">%s</div>' % badges,
            )
        )
    return (
        '<section class="page active" id="page-overview" data-page="overview">'
        '<div class="page-head"><div class="page-title">概览</div>'
        '<div class="page-sub">汇总指标 + 比能量缩略图；多架次时用右上角切换</div>'
        '<div class="page-right nav-hint">按 ← → 或 PageUp/PageDown 翻页</div></div>'
        '<div class="page-body"><div class="ovwrap">'
        "%s"
        '<div class="cards">%s</div>'
        "%s"
        "%s"
        "%s"
        '<div class="ovchart">%s</div>'
        "</div></div></section>"
        % (
            note_html,
            cards_html,
            integrity_html,
            players_html,
            warnings_html,
            "".join(views),
        )
    )


def _data_page_html(spec: Dict[str, Any], views_html: str) -> str:
    key = spec["key"]
    units = []
    if spec.get("left_unit"):
        units.append("左轴 %s" % spec["left_unit"])
    if spec.get("right_unit"):
        units.append("右轴 %s" % spec["right_unit"])
    units.append("时间轴 秒 (s)")
    return (
        '<section class="page" id="page-%s" data-page="%s">'
        '<div class="page-head"><div class="page-title">%s</div>'
        '<div class="page-sub">%s</div>'
        '<div class="page-right">%s</div></div>'
        '<div class="page-body single">%s</div>'
        "</section>"
        % (
            key,
            key,
            _html_escape(spec["title"]),
            _html_escape(spec.get("subtitle") or ""),
            _html_escape(" · ".join(units)),
            views_html,
        )
    )


def _speed_page_html(
    spec: Dict[str, Any], views_html: str, default_payload: Dict[str, Any]
) -> str:
    """速度页：顶部多一条"平均速度 / 手动范围"控件条，其余与普通数据页一致。"""
    key = spec["key"]
    units = []
    if spec.get("left_unit"):
        units.append("左轴 %s" % spec["left_unit"])
    if spec.get("right_unit"):
        units.append("右轴 %s" % spec["right_unit"])
    units.append("时间轴 秒 (s)")
    return (
        '<section class="page" id="page-%s" data-page="%s">'
        '<div class="page-head"><div class="page-title">%s</div>'
        '<div class="page-sub">%s</div>'
        '<div class="page-right">%s</div></div>'
        '<div class="page-body speed-body">%s'
        '<div class="speed-views">%s</div>'
        "</div></section>"
        % (
            key,
            key,
            _html_escape(spec["title"]),
            _html_escape(spec.get("subtitle") or ""),
            _html_escape(" · ".join(units)),
            _avg_bar_html(default_payload),
            views_html,
        )
    )


def _global_page_html(overview: Dict[str, Any]) -> str:
    items: List[str] = []
    for spec, block in overview["html"]["blocks"]:
        units = []
        if block.get("left_unit"):
            units.append("左轴 %s" % block["left_unit"])
        if block.get("right_unit"):
            units.append("右轴 %s" % block["right_unit"])
        items.append(
            '<div class="stack-item">'
            '<div class="chart-label">%s<span>%s</span></div>'
            '<div class="legend">%s</div>'
            '<div class="chart-wrap">%s</div>'
            "</div>"
            % (
                _html_escape(spec["title"]),
                _html_escape(" · ".join(units)),
                block["legend"],
                block["svg"],
            )
        )
    strip = overview["html"].get("strip")
    return (
        '<section class="page" id="page-global" data-page="global">'
        '<div class="page-head"><div class="page-title">全局总览</div>'
        '<div class="page-sub">全部架次拼在同一条时间轴上，竖虚线为架次边界</div>'
        '<div class="page-right">时间轴 秒 (s)</div></div>'
        '<div class="page-body"><div class="stackwrap">'
        "%s%s"
        "</div></div></section>"
        % (
            ('<div class="strip">%s</div>' % strip) if strip else "",
            "".join(items),
        )
    )


def _flight_label(flight_payload: Dict[str, Any]) -> str:
    """架次选择器上的短标签：多玩家时带玩家名。"""
    player = flight_payload.get("player")
    vehicle = _html_escape(flight_payload["vehicle"])
    if player:
        return "%s · %s" % (_html_escape(player), vehicle)
    return vehicle


def _flight_selector_html(
    flight_payloads: Sequence[Dict[str, Any]], default_index: int
) -> str:
    """架次选择器：<=6 架次用分段按钮，更多用下拉框（避免页眉被撑爆）。"""
    if len(flight_payloads) < 2:
        return ""
    if len(flight_payloads) <= 6:
        buttons: List[str] = []
        for flight_payload in flight_payloads:
            active = " active" if flight_payload["index"] == default_index else ""
            buttons.append(
                '<button type="button" class="fbtn%s" data-flight-btn="%d" title="%s">'
                "架次 %d · %s</button>"
                % (
                    active,
                    flight_payload["index"],
                    _html_escape(flight_payload["title"]),
                    flight_payload["index"],
                    _flight_label(flight_payload),
                )
            )
        return '<div class="flights"><span class="flabel">架次</span>%s</div>' % "".join(buttons)
    options: List[str] = []
    for flight_payload in flight_payloads:
        selected = " selected" if flight_payload["index"] == default_index else ""
        options.append(
            '<option value="%d"%s>架次 %d · %s</option>'
            % (
                flight_payload["index"],
                selected,
                flight_payload["index"],
                _flight_label(flight_payload),
            )
        )
    return (
        '<div class="flights"><label class="flabel" for="flight-select">架次</label>'
        '<select id="flight-select" class="fselect">%s</select></div>' % "".join(options)
    )


def _player_switch_html(player_names: Sequence[str], default_player: Optional[str]) -> str:
    """页眉的玩家切换按钮（只有多玩家回放才会出现）。"""
    if len(player_names) < 2:
        return ""
    buttons: List[str] = []
    for name in player_names:
        active = " active" if name == default_player else ""
        buttons.append(
            '<button type="button" class="fbtn%s" data-player-btn="%s" title="跳到该玩家的架次">'
            "%s</button>" % (active, _html_escape(name), _html_escape(name))
        )
    return (
        '<div class="players-bar" id="player-bar"><span class="flabel">玩家</span>%s</div>'
        % "".join(buttons)
    )


def _players_strip_html(players: Sequence[Dict[str, Any]], flight_counts: Dict[str, int]) -> str:
    """概览页的玩家摘要条（玩家 / 机型 / 阵营 / 架次 / 时长）。"""
    if not players:
        return ""
    items: List[str] = []
    for player in players:
        name = player.get("name") or "—"
        vehicles = player.get("vehicles") or []
        vehicle_text = " / ".join(vehicles[:2]) if vehicles else "未知机型"
        if len(vehicles) > 2:
            vehicle_text += " 等"
        duration = player.get("duration_s")
        bits = [
            "机型 %s" % vehicle_text,
            "阵营 %s" % (player.get("coalition_label") or "未知"),
            "架次 %d" % flight_counts.get(name, 0),
            "时长 %s s" % _fmt_number(duration, 0),
            "样本 %s" % player.get("updates", 0),
        ]
        items.append(
            '<span class="pchip"><b>%s</b>%s</span>'
            % (_html_escape(name), _html_escape(" · ".join(bits)))
        )
    return '<div class="players-strip">%s</div>' % "".join(items)


def _replay_note_html(replay: Dict[str, Any]) -> str:
    """概览页顶部的「回放估算数据」声明条。"""
    if not replay:
        return ""
    bits = ["回放估算数据（IAS = EAS 近似，非座舱遥测）"]
    header = replay.get("header") or {}
    source_bits = []
    if replay.get("wrpl_file"):
        source_bits.append("源文件 %s" % replay["wrpl_file"])
    if header.get("title"):
        source_bits.append(str(header["title"]))
    if replay.get("samples"):
        source_bits.append("%s 个样本" % replay["samples"])
    if replay.get("duration_s") is not None:
        source_bits.append("时长 %s s" % _fmt_number(replay.get("duration_s"), 1))
    if source_bits:
        bits.append(" · ".join(source_bits))
    bits.append("姿态 / 过载 / AOA 由回放位置反演，速度与高度为估算值")
    return '<div class="replay-note"><b>%s</b>%s</div>' % (
        _html_escape(bits[0]),
        _html_escape(" · " + " · ".join(bits[1:])),
    )


def _blueprint_chrome_meta(model: LogModel) -> Dict[str, str]:
    """蓝图标题栏/图号用的元数据（项目 / 机型 / 日期 / REV）。"""
    meta = model.meta or {}
    generated = str((model.summary or {}).get("generated_utc") or "")
    log_utc = str(meta.get("utc") or "")
    date = (log_utc or generated or "")[:10] or "—"
    vehicle = str(meta.get("vehicle") or "")
    if not vehicle and model.flights:
        vehicle = model.flights[0].vehicle or ""
    stem = os.path.splitext(os.path.basename(model.path))[0]
    tokens = re.split(r"[-_]", stem)
    drawing = "LOG"
    for token in tokens:
        if re.fullmatch(r"\d{6}", token):
            drawing = token
            break
    return {
        "project": "WTHUD FLIGHT LOG",
        "vehicle": vehicle or "—",
        "date": date,
        "rev": "C",
        "dwg": "WT-FL-%s" % drawing,
    }


def _blueprint_chrome_html(page_no: int, page_total: int,
                           bp: Dict[str, str]) -> str:
    """每页的图框元素：坐标纸网格 + 注册标记 + 页脚 SHEET n/9 + 右下角标题栏。"""
    return (
        '<div class="bp-chrome" aria-hidden="true">'
        '<div class="bp-grid"></div>'
        '<i class="bp-reg tl"></i><i class="bp-reg tr"></i>'
        '<i class="bp-reg bl"></i><i class="bp-reg br"></i>'
        '<div class="bp-foot">'
        '<span class="bp-sheet">%s</span>'
        '<span class="bp-tb">'
        "<span><em>PROJECT</em><b>%s</b></span>"
        "<span><em>ACFT</em><b>%s</b></span>"
        "<span><em>DATE</em><b>%s</b></span>"
        "<span><em>DWG NO.</em><b>%s</b></span>"
        "<span><em>REV</em><b>%s</b></span>"
        "</span></div></div>"
        % (
            _html_escape("SHEET %02d/%02d" % (page_no, page_total)),
            _html_escape(bp["project"]),
            _html_escape(bp["vehicle"]),
            _html_escape(bp["date"]),
            _html_escape(bp["dwg"]),
            _html_escape(bp["rev"]),
        )
    )


def _inject_page_chrome(page: str, chrome: str) -> str:
    """把 chrome 插到 ``<section ...>`` 开标签之后（页面内容保持原样）。"""
    end = page.find(">")
    if end < 0:
        return page
    return page[: end + 1] + chrome + page[end + 1 :]


def _build_payload(
    model: LogModel, limits: Optional[SpeedLimits] = None
) -> Dict[str, Any]:
    flight_payloads: List[Dict[str, Any]] = [
        _build_flight_payload(flight, limits) for flight in model.flights
    ]
    default_index = flight_payloads[0]["index"] if flight_payloads else 1

    # 回放相关：玩家切换、概览声明条、玩家摘要条
    replay = (model.meta or {}).get("replay") or {}
    player_names: List[str] = []
    flight_counts: Dict[str, int] = {}
    for flight_payload in flight_payloads:
        player = flight_payload.get("player")
        if not player:
            continue
        if player not in player_names:
            player_names.append(player)
        flight_counts[player] = flight_counts.get(player, 0) + 1
    multi_player = len(player_names) > 1
    default_player = flight_payloads[0].get("player") if flight_payloads else None
    note_html = _replay_note_html(replay) if replay else ""
    players_html = ""
    if replay and player_names:
        players_html = _players_strip_html(replay.get("players") or [], flight_counts)
    player_switch = _player_switch_html(player_names, default_player)
    players_data = [
        {
            "name": name,
            "flight": next(
                (fp["index"] for fp in flight_payloads if fp.get("player") == name),
                default_index,
            ),
        }
        for name in player_names
    ]

    cards_html = "".join(
        '<div class="card"><div class="k">%s</div><b>%s</b><span>%s</span></div>'
        % (_html_escape(card["label"]), _html_escape(card["value"]), _html_escape(card["sub"]))
        for card in _cards(model)
    )

    warnings = model.warnings or []
    warnings_html = ""
    if warnings:
        items = "".join("<li>%s</li>" % _html_escape(w) for w in warnings)
        warnings_html = '<div class="warn"><b>解析提示</b><ul>%s</ul></div>' % items

    pages: List[str] = [
        _overview_page_html(
            flight_payloads,
            default_index,
            cards_html,
            warnings_html,
            note_html=note_html,
            players_html=players_html,
            integrity_html=_integrity_html(model),
        )
    ]
    for spec in _flight_panel_specs():
        views: List[str] = []
        any_data = False
        for flight_payload in flight_payloads:
            block = flight_payload["blocks"].get(spec["key"])
            if block is not None:
                any_data = True
            views.append(
                _flight_view_html(
                    flight_payload,
                    spec["key"],
                    block,
                    flight_payload["index"] == default_index,
                )
            )
        if not any_data:
            continue
        if spec["key"] == "speed":
            pages.append(_speed_page_html(spec, "".join(views), flight_payloads[0]))
        else:
            pages.append(_data_page_html(spec, "".join(views)))

    overview = _overview_payload(model)
    # 多玩家时不拼"全局总览"：不同玩家的时间轴互相重叠，拼在一起没有意义
    if overview is not None and not multi_player:
        pages.append(_global_page_html(overview))

    # A 工程蓝图：每页注入图框 / 图号 / 页脚 SHEET n/9 / 右下角标题栏
    blueprint_meta: Optional[Dict[str, str]] = None
    if ACTIVE_THEME.key == "blueprint" and pages:
        blueprint_meta = _blueprint_chrome_meta(model)
        total = len(pages)
        pages = [
            _inject_page_chrome(
                page, _blueprint_chrome_html(index + 1, total, blueprint_meta)
            )
            for index, page in enumerate(pages)
        ]

    limits_info = None
    if limits is not None:
        limits_info = {
            "manual": limits.manual,
            "hud_dir": limits.hud_dir,
            "global_vne_kmh": limits.global_vne_kmh,
            "by_vehicle": len(limits.by_vehicle),
            "notes": list(limits.notes),
        }

    payload_flights = [
        {
            "index": flight_payload["index"],
            "title": flight_payload["title"],
            "player": flight_payload.get("player"),
            "t0": flight_payload["t0"],
            "t1": flight_payload["t1"],
            "times": flight_payload["times"],
            "series": flight_payload["series"],
            "panels": flight_payload["panels"],
        }
        for flight_payload in flight_payloads
    ]

    overview_data = None
    if overview is not None and not multi_player:
        overview_data = {key: value for key, value in overview.items() if key != "html"}

    title_prefix = "回放分析" if replay else "飞行日志分析"
    vne_items: List[Dict[str, Any]] = []
    for flight_payload in flight_payloads:
        if flight_payload.get("vne_kmh") is None:
            continue
        item = {
            "vehicle": flight_payload["vehicle"],
            "vne": flight_payload["vne_kmh"],
            "source": flight_payload.get("vne_source") or "",
        }
        if item not in vne_items:
            vne_items.append(item)
    debrief_html = _debrief_host_html(
        model, flight_payloads, blueprint_meta,
        player_name=(limits.player_name if limits is not None else ""),
    )
    return {
        "title": "%s · %s" % (title_prefix, os.path.basename(model.path)),
        "file": os.path.basename(model.path),
        "meta": model.meta,
        "cards": _cards(model),
        "warnings": warnings,
        "overview": overview_data,
        "flights": payload_flights,
        "players": players_data,
        "limits": limits_info,
        "vne": vne_items,
        "blueprint": blueprint_meta,
        "html": {
            "pages": pages,
            "page_count": len(pages),
            "flight_selector": _flight_selector_html(flight_payloads, default_index),
            "player_switch": player_switch,
            "debrief": debrief_html,
        },
    }


# ---------------------------------------------------------------------------
# PNG 单张复盘卡（C 档案袋风格）：生成报告时用 Python 画成内嵌 SVG
# ---------------------------------------------------------------------------


def _card_polyline(
    times: Sequence[float],
    values: Sequence[Optional[float]],
    xf: Any,
    yf: Any,
    limit: int = 460,
) -> str:
    """复盘卡曲线：分段三次贝塞尔（同报告页，不再画折线），抽稀到 ``limit`` 点以内。"""
    out: List[str] = []
    for segment in _iter_segments(times, values):
        segment = _decimate(segment, limit)
        if len(segment) == 1:
            x, y = xf(segment[0][0]), yf(segment[0][1])
            if y is not None:
                out.append("M%.1f,%.1f L%.1f,%.1f" % (x, y, x + 0.1, y))
            continue
        points: List[Tuple[float, float]] = []
        for t, v in segment:
            y = yf(v)
            if y is None:
                continue
            points.append((xf(t), y))
        if points:
            out.append(_bezier_path(points))
    return "".join(out)


def _card_axis(values: Sequence[Optional[float]], target: int = 4,
               pad: float = 0.0) -> Tuple[float, float, List[float]]:
    """给复盘卡算一条轴：返回 ``(lo, hi, ticks)``。"""
    clean = [float(v) for v in values if v is not None]
    if not clean:
        return 0.0, 1.0, [0.0, 1.0]
    lo, hi, _step, ticks = _nice_ticks(min(clean), max(clean), target=target)
    if hi - lo < 1e-9:
        hi = lo + 1.0
        ticks = [lo, hi]
    return lo, hi, ticks


def _debrief_card_svg(
    model: LogModel,
    flight_payload: Dict[str, Any],
    clean: bool,
    meta: Dict[str, str],
) -> str:
    """一张 1600×1000 的复盘卡（C 档案袋风格）：纸底 + 印章 + 关键数据 + 标注图 + 打字机注。

    ``clean=True`` 时去掉所有手绘圈 / 编号 / 分析员注记（保留曲线、坐标、事件线）。
    """
    times = [float(t) for t in (flight_payload.get("times") or []) if t is not None]
    series = flight_payload.get("series") or {}
    if not times:
        times = [0.0, 1.0]
    t0 = float(flight_payload.get("t0") or times[0])
    t1 = float(flight_payload.get("t1") or times[-1])
    if t1 <= t0:
        t1 = t0 + 1.0
    stats = flight_payload.get("stats") or {}
    vehicle = flight_payload.get("vehicle") or meta.get("vehicle") or "未知机型"
    duration = (t1 - t0)
    samples = len(flight_payload.get("times") or [])
    death_t = flight_payload.get("death_t")
    annotations = flight_payload.get("annotations") or {}
    flat: List[Dict[str, Any]] = []
    for page in ("speed", "altitude", "energy", "aoa_ny"):
        for annotation in annotations.get(page) or []:
            flat.append(annotation)
    flat.sort(key=lambda item: float(item["t"]))

    # ---------- 画布与纸张 ----------
    W, H = 1600, 1000
    parts: List[str] = []
    svg_id = "debrief-svg-clean" if clean else "debrief-svg-annotated"
    parts.append(
        '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 %d %d" width="%d" height="%d"'
        ' id="%s" class="debrief-svg" role="img" aria-label="%s">'
        % (W, H, W, H, svg_id,
           html.escape("飞行复盘卡（无批注）" if clean else "飞行复盘卡（带批注）"))
    )
    parts.append(
        '<defs>'
        '<pattern id="pcard-lines-%s" width="7" height="7" patternUnits="userSpaceOnUse">'
        '<rect width="7" height="7" fill="#C9B28A"/>'
        '<path d="M0,0 L7,0 M0,3 L7,3" stroke="#B79E74" stroke-width="0.7" opacity="0.5"/>'
        "</pattern>"
        '<pattern id="pcard-paper-%s" width="4" height="4" patternUnits="userSpaceOnUse">'
        '<rect width="4" height="4" fill="#F2EBD8"/>'
        '<path d="M0,0 L4,0" stroke="#E2D6BB" stroke-width="0.6" opacity="0.6"/>'
        "</pattern>"
        "</defs>"
        % ("a" if clean else "b", "a" if clean else "b")
    )
    parts.append('<rect width="%d" height="%d" fill="url(#pcard-lines-%s)"/>'
                 % (W, H, "a" if clean else "b"))
    parts.append('<rect x="52" y="42" width="%d" height="%d" fill="#00000018"/>'
                 % (W - 96, H - 76))
    parts.append(
        '<rect x="42" y="32" width="%d" height="%d" fill="url(#pcard-paper-%s)"'
        ' stroke="#8A734C" stroke-width="1.6"/>'
        % (W - 84, H - 64, "a" if clean else "b")
    )
    parts.append(
        '<rect x="54" y="44" width="%d" height="%d" fill="none" stroke="#B7A483"'
        ' stroke-width="0.8" stroke-dasharray="5 4"/>' % (W - 108, H - 88)
    )
    # 胶带
    for tx, ty, rot in ((96, 34, -7), (1360, 30, 5), (104, 918, 3), (1330, 924, -4)):
        parts.append(
            '<rect x="%d" y="%d" width="150" height="30" fill="#E8DCC0" opacity="0.72"'
            ' transform="rotate(%d %d %d)"/>' % (tx, ty, rot, tx + 75, ty + 15)
        )

    # ---------- 页眉 ----------
    parts.append(
        '<text x="84" y="86" font-family="Courier New,Consolas,monospace" font-size="11"'
        ' fill="#6B5B41" letter-spacing="3">WAR THUNDER · FLIGHT LOG DIVISION · DEBRIEF SECTION</text>'
    )
    parts.append(
        '<text x="84" y="134" font-family="Courier New,Consolas,monospace" font-size="40"'
        ' font-weight="700" fill="#2B2418" letter-spacing="1.5">COMBAT FLIGHT DEBRIEF</text>'
    )
    parts.append(
        '<text x="84" y="160" font-family="Microsoft YaHei UI,Microsoft YaHei,sans-serif"'
        ' font-size="14" fill="#6B5B41" letter-spacing="5">飞 行 复 盘 卡 · DEBRIEF CARD</text>'
    )
    parts.append(
        '<text x="1516" y="96" text-anchor="end" font-family="Courier New,monospace"'
        ' font-size="15" font-weight="700" fill="#8E2A24" letter-spacing="1">NO. %s</text>'
        % _html_escape(meta.get("dwg") or "WT-FL-LOG")
    )
    parts.append(
        '<text x="1516" y="116" text-anchor="end" font-family="Courier New,monospace"'
        ' font-size="9.5" fill="#6B5B41" letter-spacing="2">CLASSIFIED · DEBRIEF COPY · DO NOT CIRCULATE</text>'
    )
    parts.append(
        '<text x="1516" y="150" text-anchor="end" font-family="Courier New,monospace"'
        ' font-size="10" fill="#6B5B41" letter-spacing="1">REV %s · SHEET 01/01 · %s</text>'
        % (_html_escape(meta.get("rev") or "C"), _html_escape(meta.get("date") or "—"))
    )
    parts.append('<line x1="84" y1="178" x2="1516" y2="178" stroke="#8A734C" stroke-width="2"/>')

    # ---------- 左列：任务字段 ----------
    mission = "FLT-%s" % (meta.get("dwg") or "LOG").replace("WT-FL-", "")
    outcome = "LOST · DEATH %s s" % _fmt_number(death_t, 1) if death_t is not None else "SURVIVED"
    pilot = (
        flight_payload.get("player")
        or meta.get("player")
        or "UNKNOWN"
    )
    pilot = str(pilot).strip() or "UNKNOWN"
    if len(pilot) > 24:
        pilot = pilot[:23] + "…"
    rows = [
        ("MISSION 任务", mission + "   · " + vehicle),
        ("DATE 日期", "%s   LOCAL" % (meta.get("date") or "—")),
        ("DURATION 时长", "%.1f s   · %d samples" % (duration, samples)),
        ("ACFT 机型", vehicle),
        ("PILOT 飞行员", pilot),
        ("OUTCOME 结果", outcome),
    ]
    row_y = 208.0
    for label, value in rows:
        parts.append(
            '<text x="84" y="%.1f" font-family="Courier New,monospace" font-size="10"'
            ' fill="#6B5B41" letter-spacing="1.5">%s</text>' % (row_y, _html_escape(label))
        )
        parts.append(
            '<text x="84" y="%.1f" font-family="Courier New,monospace" font-size="14.5"'
            ' fill="#2B2418">%s</text>' % (row_y + 20, _html_escape(value))
        )
        if "OUTCOME" in label and not clean:
            parts.append(
                '<text x="84" y="%.1f" font-family="Courier New,monospace" font-size="9"'
                ' fill="#8E2A24" letter-spacing="1.2">RED = REVIEW MARK</text>'
                % (row_y + 36)
            )
        parts.append(
            '<line x1="84" y1="%.1f" x2="452" y2="%.1f" stroke="#C3B294"'
            ' stroke-width="0.8" stroke-dasharray="2 3"/>' % (row_y + 29, row_y + 29)
        )
        row_y += 50.0

    # ---------- 左列下部：关键数据 ----------
    def kpi(label: str, value: str, unit: str, x: float, y: float) -> None:
        parts.append(
            '<text x="%.1f" y="%.1f" font-family="Courier New,monospace" font-size="9.5"'
            ' fill="#6B5B41" letter-spacing="1.6">%s</text>' % (x, y, _html_escape(label))
        )
        parts.append(
            '<text x="%.1f" y="%.1f" font-family="Courier New,monospace" font-size="25"'
            ' font-weight="700" fill="#2B2418">%s<tspan font-size="11" font-weight="400"'
            ' fill="#52708A"> %s</tspan></text>'
            % (x, y + 27, _html_escape(value), _html_escape(unit))
        )

    stats_i = stats.get("ias") or {}
    stats_ny = stats.get("ny") or {}
    stats_alt = stats.get("alt") or {}
    energy = stats.get("energy") or {}
    kpi("MAX IAS 最大指示空速", _fmt_number(stats_i.get("max"), 0), "km/h", 84, 546)
    kpi("MAX G 最大过载", _fmt_number(stats_ny.get("abs_max"), 2), "g", 268, 546)
    kpi("ES 比能量范围", "%s–%s" % (_fmt_number(energy.get("min"), 0),
                                  _fmt_number(energy.get("max"), 0)), "m", 84, 630)
    kpi("MAX ALT 最大气压高度", _fmt_number(stats_alt.get("max"), 0), "m", 268, 630)
    parts.append(
        '<text x="84" y="708" font-family="Courier New,monospace" font-size="9.5"'
        ' fill="#6B5B41" letter-spacing="1.4">SOURCE %s</text>'
        % _html_escape(os.path.basename(model.path))
    )
    parts.append(
        '<text x="84" y="724" font-family="Courier New,monospace" font-size="9.5"'
        ' fill="#6B5B41" letter-spacing="1.4">SCHEMA %s · %s %s</text>'
        % (
            _html_escape((model.meta or {}).get("schema") or "1"),
            _html_escape((model.meta or {}).get("app") or "WtHud"),
            _html_escape((model.meta or {}).get("rate") or ""),
        )
    )

    # ---------- 右侧：飞行剖面图 ----------
    X0, X1 = 500.0, 1516.0
    PA = {"top": 246.0, "bot": 352.0, "lo": 0.0, "hi": 0.0, "ticks": []}
    PB = {"top": 372.0, "bot": 464.0, "lo": 0.0, "hi": 0.0, "ticks": []}
    PC = {"top": 484.0, "bot": 574.0, "lo": 0.0, "hi": 0.0, "ticks": []}
    ias_vals = series.get("ias") or []
    alt_vals = series.get("alt") or []
    es_vals = series.get("es") or []
    ny_vals = series.get("ny") or []
    PA["lo"], PA["hi"], PA["ticks"] = _card_axis(ias_vals, target=4)
    alt_lo, alt_hi, alt_ticks = _card_axis(alt_vals, target=3)
    PB["lo"], PB["hi"], PB["ticks"] = _card_axis(es_vals, target=3)
    PC["lo"], PC["hi"], PC["ticks"] = _card_axis(ny_vals, target=3)

    def xf(t: float) -> float:
        return X0 + (t - t0) / (t1 - t0) * (X1 - X0)

    def yf(panel: Dict[str, Any], value: float) -> float:
        return panel["top"] + (panel["hi"] - value) / (panel["hi"] - panel["lo"]) * (
            panel["bot"] - panel["top"]
        )

    parts.append(
        '<text x="%.1f" y="226" font-family="Courier New,monospace" font-size="11"'
        ' fill="#2B2418" letter-spacing="1.6">FIG. 2 — FLIGHT PROFILE'
        '   ·   IAS / ALT / Es / Ny vs MISSION TIME</text>' % X0
    )
    for panel, caption, ticks, right_ticks in (
        (PA, "2A  IAS（实线） / ALT（虚线） [km/h · m]", PA["ticks"], alt_ticks),
        (PB, "2B  SPECIFIC ENERGY ALTITUDE  Es [m]", PB["ticks"], []),
        (PC, "2C  NORMAL LOAD  Ny [g]", PC["ticks"], []),
    ):
        parts.append(
            '<rect x="%.1f" y="%.1f" width="%.1f" height="%.1f" fill="#FBF7EC"'
            ' stroke="#C0AE8C" stroke-width="1"/>'
            % (X0, panel["top"], X1 - X0, panel["bot"] - panel["top"])
        )
        parts.append(
            '<text x="%.1f" y="%.1f" font-family="Courier New,monospace" font-size="9.5"'
            ' fill="#4A3D2A" letter-spacing="0.5">%s</text>'
            % (X0 + 5, panel["top"] + 13, _html_escape(caption))
        )
        for tick in ticks:
            y = yf(panel, tick)
            parts.append(
                '<line x1="%.1f" y1="%.1f" x2="%.1f" y2="%.1f" stroke="#DCD0B6"'
                ' stroke-width="0.8"/>' % (X0, y, X1, y)
            )
            parts.append(
                '<text x="%.1f" y="%.1f" text-anchor="end" font-family="Courier New,monospace"'
                ' font-size="9" fill="#6B5B41">%s</text>'
                % (X0 - 6, y + 3, _fmt_number(tick, 0))
            )
        if right_ticks:
            for tick in right_ticks:
                y = panel["top"] + (alt_hi - tick) / (alt_hi - alt_lo) * (
                    panel["bot"] - panel["top"]
                )
                parts.append(
                    '<text x="%.1f" y="%.1f" text-anchor="start" font-family="Courier New"'
                    ' font-size="9" fill="#8A734C">%s</text>'
                    % (X1 + 5, y + 3, _fmt_number(tick, 0))
                )
    # 时间网格 + 时间刻度
    for tick in range(0, int(t1 - t0) + 1, 60):
        x = xf(t0 + tick)
        if x > X1:
            break
        for panel in (PA, PB, PC):
            parts.append(
                '<line x1="%.1f" y1="%.1f" x2="%.1f" y2="%.1f" stroke="#E4D8BE"'
                ' stroke-width="0.8"/>' % (x, panel["top"], x, panel["bot"])
            )
        parts.append(
            '<line x1="%.1f" y1="%.1f" x2="%.1f" y2="%.1f" stroke="#C0AE8C"'
            ' stroke-width="0.8"/>' % (x, PC["bot"], x, PC["bot"] + 4)
        )
        parts.append(
            '<text x="%.1f" y="%.1f" text-anchor="middle" font-family="Courier New"'
            ' font-size="9" fill="#6B5B41">%s</text>'
            % (x, PC["bot"] + 17, _html_escape(_tc_text(t0 + tick, tenths=False)))
        )

    def alt_yf(value: float) -> float:
        return PA["top"] + (alt_hi - value) / (alt_hi - alt_lo) * (
            PA["bot"] - PA["top"]
        )

    alt_path = _card_polyline(times, alt_vals, xf, alt_yf)
    if alt_path:
        parts.append(
            '<path d="%s" fill="none" stroke="#8A734C" stroke-width="1"'
            ' stroke-dasharray="4 4"/>' % alt_path
        )
    ias_path = _card_polyline(times, ias_vals, xf, lambda v: yf(PA, v))
    if ias_path:
        parts.append(
            '<path d="%s" fill="none" stroke="#2B2418" stroke-width="1.8"/>' % ias_path
        )
    es_path = _card_polyline(times, es_vals, xf, lambda v: yf(PB, v))
    if es_path:
        parts.append(
            '<path d="%s" fill="none" stroke="#3A3226" stroke-width="1.3"/>' % es_path
        )
    ny_path = _card_polyline(times, ny_vals, xf, lambda v: yf(PC, v))
    if ny_path:
        parts.append(
            '<path d="%s" fill="none" stroke="#3A3226" stroke-width="1.1"/>' % ny_path
        )
    if PC["lo"] < 0 < PC["hi"]:
        zero_y = yf(PC, 0.0)
        parts.append(
            '<line x1="%.1f" y1="%.1f" x2="%.1f" y2="%.1f" stroke="#C3B294"'
            ' stroke-width="0.9" stroke-dasharray="6 4"/>' % (X0, zero_y, X1, zero_y)
        )

    # 死亡线（每页/每卡保留；只有带批注时才加编号文字）
    death_index = len(flat)  # DEATH 在 legend 里排最后一条
    if death_t is not None and t0 <= float(death_t) <= t1:
        dx = xf(float(death_t))
        parts.append(
            '<line x1="%.1f" y1="%.1f" x2="%.1f" y2="%.1f" stroke="#A8322B"'
            ' stroke-width="1.1" stroke-dasharray="6 5" opacity="0.8"/>'
            % (dx, PA["top"], dx, PC["bot"])
        )
        if not clean:
            death_label = "%s DEATH" % _annotation_number(death_index)
            dl_w = _text_width(death_label, 10.5)
            dl_x = dx + 6.0
            dl_anchor = "start"
            if dl_x + dl_w > X1 - 4:
                dl_x = dx - 6.0
                dl_anchor = "end"
            parts.append(
                '<text class="ann-type" x="%.1f" y="%.1f" text-anchor="%s"'
                ' font-family="Courier New,monospace" font-size="10.5" fill="#9E2F26"'
                ' letter-spacing="0.3">%s</text>'
                % (dl_x, PA["top"] - 5.0, dl_anchor, _html_escape(death_label))
            )

    # 击落线（0.5.2）：青绿虚线，每卡保留；带批注时在线上方标「编号 KILL」。
    kills_visible = [
        kill
        for kill in (flight_payload.get("kill_events") or [])
        if t0 <= float(kill["t"]) <= t1
    ]
    kill_index_base = len(flat) + (1 if death_t is not None else 0)
    for offset, kill in enumerate(kills_visible):
        kx = xf(float(kill["t"]))
        parts.append(
            '<line x1="%.1f" y1="%.1f" x2="%.1f" y2="%.1f" stroke="#1F6F7A"'
            ' stroke-width="1.1" stroke-dasharray="6 5" opacity="0.85"/>'
            % (kx, PA["top"], kx, PC["bot"])
        )
        if not clean:
            kill_label = "%s KILL" % _annotation_number(kill_index_base + offset)
            kl_w = _text_width(kill_label, 10.5)
            kl_x = kx + 6.0
            kl_anchor = "start"
            if kl_x + kl_w > X1 - 4:
                kl_x = kx - 6.0
                kl_anchor = "end"
            parts.append(
                '<text class="ann-type" x="%.1f" y="%.1f" text-anchor="%s"'
                ' font-family="Courier New,monospace" font-size="10.5" fill="#1F6F7A"'
                ' letter-spacing="0.3">%s</text>'
                % (kl_x, PA["bot"] + 13.0, kl_anchor, _html_escape(kill_label))
            )

    # ---------- 批注：手绘圈 + 编号缩写（完整说明进下方 legend） ----------
    if not clean:
        panel_for = {
            "ias": PA, "alt": PA, "es": PB, "ny": PC,
        }
        for index, annotation in enumerate(flat):
            key = annotation.get("series") or ""
            panel = panel_for.get(key)
            value = annotation.get("value")
            if panel is None or value is None:
                continue
            try:
                value_f = float(value)
            except (TypeError, ValueError):
                continue
            if key == "alt":
                y = alt_yf(value_f)
            else:
                y = yf(panel, value_f)
            y = min(max(y, panel["top"] + 6), panel["bot"] - 6)
            x = xf(float(annotation["t"]))
            rx, ry = 26.0, 12.0
            parts.append(
                '<path class="ann-crayon" d="%s" fill="none" stroke="#B0362A"'
                ' stroke-width="3.2" stroke-linecap="round" opacity="0.78"/>'
                % _wobble_ring(x, y, rx, ry, index * 1.6, n=36)
            )
            parts.append(
                '<path class="ann-crayon" d="%s" fill="none" stroke="#B0362A"'
                ' stroke-width="1.5" stroke-linecap="round" opacity="0.5"/>'
                % _wobble_ring(x, y, rx * 0.88, ry * 0.84, index * 1.6 + 2.4, n=36)
            )
            # 图上只写「编号 + 缩写」：① IAS_MAX（值 / 时间在下方 legend）
            note = "%s %s" % (_annotation_number(index), _annotation_mark_text(annotation))
            note_x = x + rx + 8
            if note_x + _text_width(note, 10.5) > X1 - 4:
                note_x = x - rx - 8 - _text_width(note, 10.5)
            note_y = y - ry - 6
            if note_y < panel["top"] + 12:
                note_y = y + ry + 14
            parts.append(
                '<text class="ann-type" x="%.1f" y="%.1f" font-family="Courier New,monospace"'
                ' font-size="10.5" fill="#9E2F26" letter-spacing="0.3">%s</text>'
                % (max(X0 + 2, note_x), note_y, _html_escape(note))
            )

    # ---------- 批注对照表（legend）/ 印章 / 签字 ----------
    parts.append(
        '<line x1="84" y1="770" x2="1516" y2="770" stroke="#B7A483" stroke-width="1"/>'
    )
    notes_top = 788.0
    if clean:
        parts.append(
            '<text x="84" y="%.1f" font-family="Courier New,monospace" font-size="13"'
            ' fill="#6B5B41" letter-spacing="0.4">CLEAN COPY — 本卡未启用批注：'
            '只保留曲线 / 坐标 / 事件竖线（无圈注、无批注对照表）</text>' % notes_top
        )
    else:
        legend_lines: List[str] = [
            _annotation_card_line(annotation, index)
            for index, annotation in enumerate(flat)
        ]
        if death_t is not None:
            legend_lines.append(_death_card_line({"t": death_t}, death_index))
        # 击落（KILL）legend：编号顺延在 DEATH 之后，不进数据批注的 5 条上限。
        for offset, kill in enumerate(kills_visible):
            legend_lines.append(_kill_card_line(kill, kill_index_base + offset))
        parts.append(
            '<text x="84" y="%.1f" font-family="Courier New,monospace" font-size="10"'
            ' fill="#6B5B41" letter-spacing="2">ANALYST REMARKS · 分析员注记 — '
            '红圈编号对照 LEGEND</text>' % notes_top
        )
        max_rows = 5
        col_count = 1 if len(legend_lines) <= max_rows else 2
        per_col = (
            max_rows
            if col_count == 1
            else max(1, (len(legend_lines) + col_count - 1) // col_count)
        )
        shown = legend_lines[: per_col * col_count]
        for index, line in enumerate(shown):
            column = index // per_col
            row = index % per_col
            size = 12.0 if _text_width(line, 12.0) <= 556.0 else 10.0
            parts.append(
                '<text x="%.1f" y="%.1f" font-family="Courier New,monospace"'
                ' font-size="%.1f" fill="#3A3023">%s</text>'
                % (84 + column * 592, notes_top + 22 + row * 17, size,
                   _html_escape(line))
            )
        extra = len(legend_lines) - len(shown)
        note_y = notes_top + 22 + per_col * 17 + 4
        if extra > 0:
            parts.append(
                '<text x="84" y="%.1f" font-family="Courier New,monospace" font-size="9.5"'
                ' fill="#6B5B41">… 另有 %d 条批注（见 PDF 各页）</text>'
                % (note_y, extra)
            )
        else:
            parts.append(
                '<text x="84" y="%.1f" font-family="Courier New,monospace" font-size="9.5"'
                ' fill="#6B5B41" letter-spacing="1">红圈 / 编号为自动审阅批注，'
                '时间相近（&lt;10 s）已合并；数值取自全速率样本。</text>'
                % note_y
            )
    # 印章
    stamp_x, stamp_y = 1216.0, 700.0
    parts.append(
        '<g transform="rotate(-10 %.1f %.1f)" opacity="0.82">'
        '<rect x="%.1f" y="%.1f" width="272" height="78" fill="none" stroke="#A8322B"'
        ' stroke-width="4"/>'
        '<rect x="%.1f" y="%.1f" width="264" height="70" fill="none" stroke="#A8322B"'
        ' stroke-width="1.6"/>'
        '<text x="%.1f" y="%.1f" text-anchor="middle" font-family="Courier New,monospace"'
        ' font-size="31" font-weight="700" fill="#A8322B" letter-spacing="4">DEBRIEF</text>'
        '<text x="%.1f" y="%.1f" text-anchor="middle" font-family="Courier New,monospace"'
        ' font-size="10" fill="#A8322B" letter-spacing="2.6">REVIEWED · WTHUD ANALYST</text>'
        "</g>"
        % (
            stamp_x + 136, stamp_y + 39,
            stamp_x + 4, stamp_y + 4,
            stamp_x + 8, stamp_y + 8,
            stamp_x + 136, stamp_y + 40,
            stamp_x + 136, stamp_y + 58,
        )
    )
    # 签字
    parts.append(
        '<path d="M1194,876 C1210,850 1218,884 1230,866 C1240,852 1246,882 1258,872'
        ' C1272,860 1276,878 1292,870 C1306,862 1310,876 1326,868"'
        ' fill="none" stroke="#2B2418" stroke-width="1.8" stroke-linecap="round"/>'
    )
    parts.append(
        '<line x1="1180" y1="884" x2="1360" y2="884" stroke="#4A3D2A" stroke-width="1.2"/>'
    )
    parts.append(
        '<text x="1500" y="900" text-anchor="end" font-family="Courier New,monospace"'
        ' font-size="9" fill="#6B5B41" letter-spacing="1.4">ANALYST SIGNATURE · 分析员签字'
        '<tspan fill="#8E2A24" letter-spacing="0.8">   ■■■■■ . Bulannikov</tspan></text>'
    )
    # 页脚
    parts.append(
        '<line x1="84" y1="918" x2="1120" y2="918" stroke="#B7A483" stroke-width="0.9"/>'
    )
    parts.append(
        '<text x="84" y="936" font-family="Courier New,monospace" font-size="9.5"'
        ' fill="#6B5B41" letter-spacing="1.2">ARCHIVED %s · %s · PNG 1600×1000 · %s</text>'
        % (
            _html_escape(meta.get("date") or "—"),
            _html_escape(meta.get("dwg") or "WT-FL-LOG"),
            _html_escape("CLEAN" if clean else "ANNOTATED"),
        )
    )
    parts.append(
        '<text x="1516" y="936" text-anchor="end" font-family="Courier New,monospace"'
        ' font-size="9.5" fill="#6B5B41" letter-spacing="1.2">%s · REV %s</text>'
        % (_html_escape(meta.get("project") or "WTHUD FLIGHT LOG"),
           _html_escape(meta.get("rev") or "C"))
    )
    parts.append("</svg>")
    return "".join(parts)


def _debrief_host_html(
    model: LogModel,
    flight_payloads: Sequence[Dict[str, Any]],
    bp: Optional[Dict[str, str]],
    player_name: str = "",
) -> str:
    """隐藏容器：内嵌两份复盘卡 SVG（带批注 / 无批注）+ 下载文件名前缀。"""
    usable = [payload for payload in flight_payloads if payload.get("times")]
    if not usable:
        return ""
    main = max(usable, key=lambda item: len(item.get("times") or []))
    meta = dict(bp) if bp else {
        "project": "WTHUD FLIGHT LOG",
        "vehicle": main.get("vehicle") or "—",
        "date": "",
        "rev": "C",
        "dwg": "WT-FL-LOG",
    }
    if player_name:
        meta["player"] = player_name
    stem = os.path.splitext(os.path.basename(model.path))[0]
    prefix = "debrief-%s-" % stem
    return (
        '<div id="debrief-src" aria-hidden="true" data-prefix="%s">'
        "%s%s"
        "</div>"
        % (
            _html_escape(prefix),
            _debrief_card_svg(model, main, False, meta),
            _debrief_card_svg(model, main, True, meta),
        )
    )


# ---------------------------------------------------------------------------
# CSS / JS（内联，无外链）
# ---------------------------------------------------------------------------

def _root_vars(theme: Theme) -> Dict[str, str]:
    """主题 -> ``:root`` CSS 变量表（键名不带 ``--``）。

    基础变量从 :class:`Theme` 的字段派生，细节（按钮/提示框/徽标/打印……）
    由 ``theme.css`` 覆盖；这样 ``CSS_BASE`` 里不出现任何硬编码颜色。
    """
    values: Dict[str, str] = {
        "bg": theme.bg,
        "panel": theme.panel,
        "panel2": theme.panel2,
        "line": theme.line,
        "line-strong": theme.line,
        "text": theme.text,
        "muted": theme.muted,
        "muted2": theme.muted,
        "accent": theme.accent,
        "accent2": theme.accent2,
        "danger": theme.danger,
        "ok": theme.ok,
        "radius": theme.radius,
        "radius-sm": theme.radius_sm,
        "radius-xs": theme.radius_xs,
        "pill": theme.pill,
        "font-ui": theme.font_ui,
        "font-title": theme.font_title,
        "font-num": theme.font_num,
        "body-size": "14px",
        "header-bg": "linear-gradient(180deg,%s,%s)" % (theme.panel, theme.bg),
        "header-border": theme.line,
        "subline-code-bg": theme.panel2,
        "chip-bg": theme.panel,
        "chip-border": theme.line,
        "chip-fg": theme.muted,
        "chip-b": theme.text,
        "btn-bg": theme.panel2,
        "btn-fg": theme.muted,
        "btn-border": theme.line,
        "btn-hover-fg": theme.text,
        "btn-active-bg": _alpha(theme.accent, "2e"),
        "btn-active-fg": theme.text,
        "link": theme.accent,
        "replay-fg": theme.accent2,
        "replay-b": theme.accent2,
        "note-bg": theme.panel2,
        "note-border": theme.line,
        "note-fg": theme.text,
        "note-b": theme.text,
        "pchip-bg": theme.panel,
        "pchip-fg": theme.muted,
        "pchip-b": theme.text,
        "badge-bg": theme.panel,
        "badge-fg": theme.muted,
        "legend-fg": theme.muted,
        "chart-label-fg": theme.text,
        "input-bg": theme.panel2,
        "avg-btn-bg": theme.accent,
        "avg-btn-border": theme.accent,
        "avg-btn-fg": theme.bg,
        "avg-btn-hover-bg": theme.accent2,
        "ghost-fg": theme.muted,
        "avg-msg": theme.accent2,
        "avg-msg-warn": theme.danger,
        "warn-bg": theme.panel2,
        "warn-border": theme.accent2,
        "warn-fg": theme.accent2,
        "card-bg": theme.panel,
        "card-border": theme.line,
        "card-shadow": "none",
        "pager-bg": theme.bg,
        "pager-border": theme.line,
        "dot": theme.line,
        "dot-hover": theme.muted,
        "dot-ring": _alpha(theme.accent, "2e"),
        "hint-fg": theme.muted,
        "side-bg": theme.panel,
        "side-fg": theme.text,
        "side-hover-bg": theme.panel2,
        "tip-bg": theme.bg,
        "tip-border": theme.line,
        "tip-fg": theme.text,
        "tip-strong": theme.text,
        "tip-shadow": "0 10px 30px #00000066",
        "dash-fg": theme.text,
        "avgdash-fg": theme.muted,
        "avg-dash": theme.muted,
        "chart-bg": "transparent",
        "chart-ring": "none",
        "chart-radius": "0",
        "color-scheme": theme.mode,
        "print-bg": theme.bg,
        "print-fg": theme.text,
        "print-header-bg": theme.bg,
        # 审阅批注：屏幕标注条的底色/字色与标记色（蓝图主题在 theme.css 里覆盖）
        "ann-box": theme.panel2,
        "ann-ink": theme.text,
        "ann-mark": theme.danger,
    }
    values.update(theme.css)
    return values


CSS_BASE = """
*{box-sizing:border-box}
html{color-scheme:var(--color-scheme)}
html,body{height:100%;margin:0;padding:0;overflow:hidden}
body{
  background:var(--bg); color:var(--text);
  font-family:var(--font-ui);
  font-size:var(--body-size); line-height:1.5;
  -webkit-font-smoothing:antialiased;
}
.app{height:100%;display:flex;flex-direction:column;min-height:0}
header{
  flex:0 0 auto;display:flex;flex-direction:column;
  padding:11px 22px 8px;border-bottom:1px solid var(--header-border);
  background:var(--header-bg);
}
.hrow{display:flex;align-items:baseline;gap:14px;min-width:0;flex-wrap:wrap}
header h1{
  font-family:var(--font-title);font-size:19px;margin:0;font-weight:650;
  letter-spacing:.3px;white-space:nowrap;
}
.subline{
  color:var(--muted);font-size:12px;margin-top:3px;
  white-space:nowrap;overflow:hidden;text-overflow:ellipsis;
}
.subline code{
  background:var(--subline-code-bg);border:1px solid var(--line);padding:1px 5px;
  border-radius:var(--radius-xs);
}
.flights{margin-left:auto;display:flex;flex-wrap:wrap;gap:6px;align-items:center}
.flights .flabel{font-size:12px;color:var(--muted);margin-right:2px;white-space:nowrap}
.fbtn{
  font-family:inherit;font-size:12px;color:var(--btn-fg);background:var(--btn-bg);
  border:1px solid var(--btn-border);border-radius:var(--pill);padding:3px 11px;cursor:pointer;
  transition:.15s;white-space:nowrap;
}
.fbtn:hover{border-color:var(--accent);color:var(--btn-hover-fg)}
.fbtn.active{background:var(--btn-active-bg);border-color:var(--accent);color:var(--btn-active-fg)}
.fselect{
  font-family:inherit;font-size:12px;color:var(--btn-fg);background:var(--btn-bg);
  border:1px solid var(--btn-border);border-radius:var(--radius-sm);padding:3px 8px;
  cursor:pointer;max-width:240px;
}
.fselect:hover{border-color:var(--accent)}
/* 回放：玩家切换条 / 估算声明 / 概览玩家摘要 */
.players-bar{
  display:flex;flex-wrap:wrap;gap:6px;align-items:center;
  margin-top:6px;font-size:12px;color:var(--muted);
}
.players-bar .flabel{font-size:12px;color:var(--muted);margin-right:2px;white-space:nowrap}
.replay-banner{
  color:var(--replay-fg)!important;font-size:11.5px;white-space:normal!important;
  overflow:visible!important;text-overflow:clip!important;
}
.replay-banner b{color:var(--replay-b)}
.replay-note{
  flex:0 0 auto;background:var(--note-bg);border:1px solid var(--note-border);
  color:var(--note-fg);border-radius:var(--radius-sm);padding:5px 12px;font-size:12px;
}
.replay-note b{color:var(--note-b);margin-right:2px}
.players-strip{flex:0 0 auto;display:flex;flex-wrap:wrap;gap:6px;max-height:58px;overflow:auto}
.pchip{
  font-size:11.5px;color:var(--pchip-fg);background:var(--pchip-bg);
  border:1px solid var(--line);border-radius:var(--radius-xs);padding:2px 8px;
  white-space:nowrap;font-variant-numeric:tabular-nums;
}
.pchip b{color:var(--pchip-b);font-weight:600;margin-right:6px}
main{position:relative;flex:1 1 auto;min-height:0}
.page{position:absolute;inset:0;display:none;flex-direction:column;padding:9px 26px 4px;min-height:0}
.page.active{display:flex}
.page-head{flex:0 0 auto;display:flex;align-items:baseline;gap:12px;flex-wrap:wrap;margin-bottom:4px}
.page-title{font-family:var(--font-title);font-size:16px;font-weight:650}
.page-sub{font-size:12px;color:var(--muted)}
.page-right{
  margin-left:auto;font-size:12px;color:var(--muted);
  font-variant-numeric:tabular-nums;white-space:nowrap;
}
.page-body{flex:1 1 auto;min-height:0;position:relative}
.page-body.single>.flight-view{position:absolute;inset:0}
.flight-view{display:none;flex-direction:column;min-height:0}
.flight-view.active{display:flex}
.badges{
  flex:0 0 auto;display:flex;flex-wrap:wrap;gap:6px;margin-bottom:5px;
  overflow:hidden;max-height:56px;
}
.badges span{
  font-size:11.5px;color:var(--badge-fg);background:var(--badge-bg);border:1px solid var(--line);
  border-radius:var(--radius-xs);padding:2px 8px;font-variant-numeric:tabular-nums;
}
.strip{flex:0 0 auto;margin-bottom:1px}
.strip svg{width:100%;height:34px;display:block;overflow:hidden}
.legend{
  flex:0 0 auto;display:flex;flex-wrap:wrap;gap:13px;font-size:12px;color:var(--legend-fg);
  margin:2px 0 3px;
}
.lg{display:inline-flex;align-items:center;gap:5px;white-space:nowrap}
.lg svg.lg-line{display:inline-block;width:24px;height:8px;flex:0 0 auto;overflow:visible;vertical-align:middle}
.lg i{display:inline-block;width:15px;height:3px;border-radius:2px}
.lg i.dashed{
  background-image:linear-gradient(90deg,var(--dash-fg) 0 60%,transparent 60% 100%)!important;
  background-size:7px 3px;background-repeat:repeat-x;
}
.chart-wrap{
  flex:1 1 auto;min-height:0;position:relative;background:var(--chart-bg);
  box-shadow:var(--chart-ring);border-radius:var(--chart-radius);
}
.chart-svg{width:100%;height:100%;display:block;overflow:hidden}
.chart-svg text{font-family:var(--font-num)}
.empty{
  flex:1 1 auto;display:flex;align-items:center;justify-content:center;
  color:var(--muted);font-size:13px;
}
/* ---------- 批注对照表（legend）：图上只有 ① IAS_MAX，图下列全称 / 值 / 时间 ---------- */
.ann-legend{
  flex:0 0 auto;display:flex;flex-wrap:wrap;align-items:baseline;gap:1px 16px;
  margin:3px 0 0;padding-top:3px;border-top:1px dotted var(--line);
  font-family:var(--font-num);font-size:11px;line-height:1.5;
  color:var(--muted);letter-spacing:.02em;
}
.ann-legend .ai{white-space:nowrap}
.ann-legend .ai-h{
  font-size:9.5px;letter-spacing:.22em;color:var(--muted);
  text-transform:uppercase;opacity:.75;margin-right:2px;
}
body.no-annot .ann-legend{display:none!important}
/* 速度页：平均速度 / 手动统计范围（只在第 2 页出现） */
.speed-body{display:flex;flex-direction:column;min-height:0}
.speed-views{position:relative;flex:1 1 auto;min-height:0}
.speed-views>.flight-view{position:absolute;inset:0}
.avgbar{
  flex:0 0 auto;display:flex;align-items:center;gap:20px;flex-wrap:wrap;
  background:var(--panel);border:1px solid var(--line);border-radius:var(--radius);
  padding:6px 13px;margin-bottom:5px;font-variant-numeric:tabular-nums;
}
.avgbar .avg-title{font-size:12px;color:var(--muted);line-height:1.35;white-space:nowrap}
.avgbar .avg-title b{display:block;font-size:13px;color:var(--text);font-weight:600}
.avg-vals{display:flex;gap:18px;flex-wrap:wrap}
.avg-val{display:flex;flex-direction:column;line-height:1.15}
.avg-val b{font-size:19px;font-weight:650;letter-spacing:.2px;white-space:nowrap}
.avg-k{display:inline-flex;align-items:center;gap:5px;font-size:10.5px;color:var(--muted);white-space:nowrap}
.avgdash{
  display:inline-block;width:13px;height:3px;border-radius:2px;
  background-image:linear-gradient(90deg,currentColor 0 60%,transparent 60% 100%);
  background-size:7px 3px;background-repeat:repeat-x;
}
.avg-ctl{display:flex;align-items:center;gap:8px;margin-left:auto;flex-wrap:wrap}
.avg-range{display:flex;align-items:center;gap:5px;font-size:12px;color:var(--muted);white-space:nowrap}
.avg-dash{color:var(--avg-dash)}
.avg-input{
  width:82px;font-family:inherit;font-size:12.5px;color:var(--text);background:var(--input-bg);
  border:1px solid var(--line);border-radius:var(--radius-xs);padding:3px 6px;
  font-variant-numeric:tabular-nums;
}
.avg-input:focus{outline:none;border-color:var(--accent)}
.avg-input:disabled{opacity:.45}
.avg-btn{
  font-family:inherit;font-size:12.5px;color:var(--avg-btn-fg);background:var(--avg-btn-bg);
  border:1px solid var(--avg-btn-border);border-radius:var(--radius-xs);
  padding:4px 12px;cursor:pointer;transition:.15s;
}
.avg-btn:hover{background:var(--avg-btn-hover-bg);border-color:var(--accent)}
.avg-btn.ghost{background:var(--panel2);border-color:var(--line);color:var(--ghost-fg)}
.avg-btn.ghost:hover{border-color:var(--accent);color:var(--text)}
.avg-meta{
  flex-basis:100%;display:flex;gap:12px;align-items:baseline;flex-wrap:wrap;
  font-size:11.5px;color:var(--muted);margin-top:1px;min-height:14px;
}
.avg-msg{color:var(--avg-msg)}
.avg-msg.warn{color:var(--avg-msg-warn)}
/* SVG 里的平均速度图层不拦截悬停 */
.avg-band,.avg-edge,.avg-line{pointer-events:none}
/* 数据中断图层（淡灰竖带 / 斜纹 / 虚线贝塞尔桥）同样不拦截悬停 */
.gap-band,.gap-hatch,.gap-bridge{pointer-events:none}
.lg i.avgdash{
  background-image:linear-gradient(90deg,var(--avgdash-fg) 0 60%,transparent 60% 100%);
  background-size:7px 3px;background-repeat:repeat-x;
}
/* 概览页 */
.ovwrap{position:absolute;inset:0;display:flex;flex-direction:column;gap:9px;min-height:0}
.cards{flex:0 0 auto;display:grid;grid-template-columns:repeat(auto-fit,minmax(168px,1fr));gap:8px}
.card{
  background:var(--card-bg);border:1px solid var(--card-border);border-radius:var(--radius);
  padding:7px 11px;box-shadow:var(--card-shadow);
}
.card .k{font-size:12px;color:var(--muted)}
.card b{
  display:block;font-size:17px;font-weight:650;letter-spacing:.2px;
  font-variant-numeric:tabular-nums;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;
}
.card span{
  display:block;font-size:11.5px;color:var(--muted);margin-top:1px;
  white-space:nowrap;overflow:hidden;text-overflow:ellipsis;
}
/* 概览「数据完整性」：样本 / 空洞 / 最大间隔 / >1 s 中断（口径见 title 提示） */
.integrity{
  flex:0 0 auto;display:flex;align-items:baseline;gap:6px;flex-wrap:wrap;
  font-family:var(--font-num);font-size:11.5px;color:var(--muted);
  font-variant-numeric:tabular-nums;letter-spacing:.02em;margin-top:-2px;
}
.warn{
  flex:0 0 auto;background:var(--warn-bg);border:1px solid var(--warn-border);color:var(--warn-fg);
  border-radius:var(--radius-sm);padding:6px 12px;font-size:12.5px;max-height:64px;overflow:auto;
}
.warn ul{margin:3px 0 0;padding-left:18px}
.ovchart{flex:1 1 auto;min-height:0;position:relative}
.ovchart>.flight-view{position:absolute;inset:0}
/* 全局总览页（多图堆叠） */
.stackwrap{position:absolute;inset:0;display:flex;flex-direction:column;gap:5px;min-height:0}
.stack-item{flex:1 1 0;min-height:0;display:flex;flex-direction:column}
.chart-label{flex:0 0 auto;font-size:12.5px;font-weight:650;color:var(--chart-label-fg)}
.chart-label span{font-weight:400;color:var(--muted);margin-left:8px;font-size:11.5px}
.stack-item .legend{margin:0 0 2px;font-size:11.5px}
/* 翻页控件 */
.pager{
  flex:0 0 auto;display:flex;align-items:center;justify-content:center;gap:14px;
  padding:7px 22px 9px;border-top:1px solid var(--pager-border);background:var(--pager-bg);
  position:relative;
}
.pbtn{
  font-family:inherit;font-size:13px;color:var(--text);background:var(--btn-bg);
  border:1px solid var(--btn-border);border-radius:var(--pill);padding:5px 15px;
  cursor:pointer;transition:.15s;
}
.pbtn:hover:not(:disabled){border-color:var(--accent);color:var(--btn-hover-fg)}
.pbtn:disabled{opacity:.35;cursor:default}
.page-label{
  font-size:13px;color:var(--text);font-variant-numeric:tabular-nums;
  min-width:104px;text-align:center;
}
.dots{display:flex;gap:7px;align-items:center}
.dot{
  width:9px;height:9px;padding:0;border:0;border-radius:50%;background:var(--dot);
  cursor:pointer;transition:.15s;
}
.dot:hover{background:var(--dot-hover)}
.dot.active{background:var(--accent);box-shadow:0 0 0 3px var(--dot-ring)}
.hint{
  position:absolute;right:16px;bottom:12px;font-size:11.5px;color:var(--hint-fg);
  white-space:nowrap;
}
.side{
  position:absolute;top:50%;transform:translateY(-50%);z-index:6;width:32px;height:76px;
  display:flex;align-items:center;justify-content:center;font-size:22px;line-height:1;
  color:var(--side-fg);background:var(--side-bg);border:1px solid var(--line);cursor:pointer;
  font-family:inherit;border-radius:var(--radius-sm);transition:.15s;
}
.side:hover:not(:disabled){border-color:var(--accent);color:var(--text);background:var(--side-hover-bg)}
.side:disabled{opacity:.22;cursor:default}
.side.prev{left:3px}
.side.next{right:3px}
.tip{
  position:fixed;z-index:999;display:none;pointer-events:none;
  background:var(--tip-bg);border:1px solid var(--tip-border);border-radius:var(--radius-sm);
  padding:8px 11px;font-size:12px;color:var(--tip-fg);
  box-shadow:var(--tip-shadow);min-width:180px;
}
.tip .tip-t{
  font-weight:650;margin-bottom:5px;color:var(--tip-strong);
  font-variant-numeric:tabular-nums;
}
.tip table{border-collapse:collapse;width:100%}
.tip td{padding:1px 0;vertical-align:top}
.tip td.v{
  text-align:right;padding-left:14px;font-variant-numeric:tabular-nums;white-space:nowrap;
}
.tip td i{display:inline-block;width:8px;height:8px;border-radius:50%;margin-right:6px}
a{color:var(--link)}
/* 等宽数字：座舱/手册/战术主题下所有读数统一用 --font-num */
.card b,.avg-val b,.page-label,.page-right,.badges span,.pchip,.avg-input,.avg-meta,
.tip .tip-t,.tip td.v{font-family:var(--font-num)}
@media (max-width:1000px){
  .hint{display:none}
  .side{width:26px;height:60px;font-size:18px}
}
@media (max-width:760px){
  .side{display:none}
  header{padding:9px 12px 6px}
  .page{padding:7px 12px 3px}
  .cards{grid-template-columns:repeat(auto-fit,minmax(140px,1fr))}
}
@media (max-height:660px){
  .strip svg{height:26px}
  .legend{font-size:11px;margin:0 0 2px}
  .badges{max-height:28px}
  .cards .card b{font-size:15px}
}
/* ---------- 审阅批注：屏幕层 / 打印层 ---------- */
/* 打印层默认藏起来；@media print 里反过来（见 CSS_PRINT），body.no-annot 再关掉。 */
.ann-print{display:none}
.ann-box{fill:var(--ann-box)}
.ann-ink{fill:var(--ann-ink)}
.ann-mark{stroke:var(--ann-mark);fill:none;stroke-width:1.4}
.ann-mark-fill{fill:var(--ann-mark)}
.ann-crayon{fill:none;stroke:#B0362A;stroke-linecap:round;stroke-linejoin:round}
.ann-crayon-arrow{fill:none;stroke:#B0362A;stroke-width:2;stroke-linecap:round}
.ann-type{font-family:"Courier New",Consolas,monospace;fill:#9E2F26;letter-spacing:.02em}
.ann-num{font-family:"Courier New",Consolas,monospace;fill:#9E2F26}
/* ---------- 页眉「保存」按钮 + 保存设置二级页（模态） ---------- */
.header-save{
  margin-left:10px;font-family:var(--font-num);font-size:12.5px;letter-spacing:.1em;
  color:var(--btn-active-fg);background:var(--btn-active-bg);border:1px solid var(--accent);
  border-radius:var(--radius-xs);padding:4px 16px;cursor:pointer;white-space:nowrap;
}
.header-save:hover{background:var(--accent);color:var(--bg)}
.save-modal{position:fixed;inset:0;z-index:200;display:flex;align-items:center;
  justify-content:center}
.save-modal[hidden]{display:none!important}
.save-backdrop{position:absolute;inset:0;background:#02101fd9}
.save-dialog{position:relative;width:520px;max-width:calc(100vw - 40px);
  background:var(--panel);border:1.4px solid var(--accent);border-radius:var(--radius);
  box-shadow:0 30px 80px #00000099;color:var(--text);font-family:var(--font-ui);
  padding:0;outline:none}
.save-head{display:flex;align-items:baseline;justify-content:space-between;gap:12px;
  padding:14px 22px 11px;border-bottom:1px solid var(--line)}
.save-head b{font-family:var(--font-title);font-size:19px;letter-spacing:1.4px}
.save-head span{font-family:var(--font-num);font-size:9.5px;letter-spacing:.26em;
  color:var(--muted)}
.save-body{padding:16px 22px 8px;display:flex;flex-direction:column;gap:14px}
.save-group{border:1px solid var(--line);padding:10px 13px;border-radius:var(--radius-xs);
  background:var(--panel2)}
.save-label{font-family:var(--font-num);font-size:9.5px;letter-spacing:.24em;
  color:var(--muted);margin-bottom:8px}
.save-opt{display:flex;align-items:flex-start;gap:10px;padding:7px 2px;cursor:pointer}
.save-opt+.save-opt{border-top:1px dotted var(--line)}
.save-opt input,.save-check input{margin-top:3px;accent-color:var(--accent)}
.save-opt b{display:block;font-size:13.5px;letter-spacing:.3px}
.save-opt i,.save-check i{display:block;font-style:normal;font-family:var(--font-num);
  font-size:10.5px;color:var(--muted);letter-spacing:.06em;margin-top:1px}
.save-check{display:flex;align-items:flex-start;gap:10px;padding:2px;cursor:pointer}
.save-check b{display:block;font-size:13.5px}
.save-foot{display:flex;justify-content:flex-end;gap:10px;padding:12px 22px 16px;
  border-top:1px solid var(--line)}
.save-foot .save-btn{font-family:var(--font-num);font-size:12.5px;letter-spacing:.1em;
  padding:7px 20px;cursor:pointer}
.save-btn{background:var(--btn-active-bg);color:var(--btn-active-fg);
  border:1px solid var(--accent);border-radius:var(--radius-xs)}
.save-btn.ghost{background:transparent;color:var(--ghost-fg)}
body.modal-open{overflow:hidden}
#debrief-src{position:absolute;left:-10000px;top:0;width:1px;height:1px;overflow:hidden}
"""

CSS_PRINT = """
@media print{
  html,body{height:auto;overflow:visible}
  body{
    background:var(--print-bg);color:var(--print-fg);
    -webkit-print-color-adjust:exact;print-color-adjust:exact;
  }
  .app{height:auto}
  header{background:var(--print-header-bg);border-color:var(--line)}
  main{position:static}
  .page{
    position:relative;inset:auto;display:flex!important;height:auto;min-height:72vh;
    page-break-after:always;border-top:1px solid var(--line);
  }
  .page:first-of-type{border-top:0}
  .page-body{min-height:420px}
  .flight-view{display:none!important}
  .flight-view.active{display:flex!important}
  .pager,.side,.hint,.header-save,.save-modal,#debrief-src,#tip{display:none}
  .card{box-shadow:none}
  /* 批注：打印层显示手绘圈 + 打字机短注，屏幕层隐藏；关闭批注时整层隐藏 */
  .ann-screen{display:none!important}
  .ann-print{display:inline}
  body.no-annot .ann-print{display:none!important}
}
"""


def _css(theme: Theme) -> str:
    """把主题配置渲染成完整内联 CSS（变量 + 结构样式 + 主题纹理 + 打印）。"""
    root = ":root{" + "".join(
        "--%s:%s;" % (key, value) for key, value in _root_vars(theme).items()
    ) + "}"
    # 主题打印覆盖必须包在 @media print 里，否则会污染屏幕样式
    print_extra = ("@media print{" + theme.print_css + "}") if theme.print_css else ""
    # 打印时把 SVG 里的深色曲线/网格按主题配置换成纸上的墨色（属性选择器只影响
    # 内联 SVG 的 stroke/fill 属性值，屏幕样式完全不变）。
    chart_print = ""
    if theme.print_chart:
        pairs: Dict[str, str] = {}
        # 曲线/网格颜色可能重复（ias / alt / pitch 都是正文色），只保留第一次映射，
        # 否则后出现的装饰色（avg_band 等）会把主曲线改成红色。
        for key, new_color in theme.print_chart.items():
            old_color = theme.chart.get(key)
            if old_color and old_color != new_color and old_color not in pairs:
                pairs[old_color] = new_color
        rules = "".join(
            '[stroke="%s"]{stroke:%s!important}[fill="%s"]{fill:%s!important}'
            % (old, new, old, new)
            for old, new in pairs.items()
        )
        if rules:
            chart_print = "@media print{" + rules + "}"
    return root + "\n" + CSS_BASE + theme.extra_css + CSS_PRINT + chart_print + print_extra

JS = r"""
(function(){
  "use strict";
  var DATA = window.__FLIGHTLOG__ || {};
  var THEME = DATA.theme || {};
  var NS = "http://www.w3.org/2000/svg";
  var tip = document.getElementById("tip");
  var hiders = [];

  function qsa(sel, root){
    return Array.prototype.slice.call((root || document).querySelectorAll(sel));
  }

  function nearest(times, t){
    var lo = 0, hi = times.length - 1;
    if (hi < 0) return -1;
    if (t <= times[0]) return 0;
    if (t >= times[hi]) return hi;
    while (lo < hi - 1){
      var mid = (lo + hi) >> 1;
      if (times[mid] <= t) lo = mid; else hi = mid;
    }
    return (t - times[lo] <= times[hi] - t) ? lo : hi;
  }

  function fmt(v, decimals){
    if (v === null || v === undefined || !isFinite(v)) return "—";
    if (decimals === undefined || decimals === null){
      var a = Math.abs(v);
      decimals = a >= 100 ? 0 : (a >= 10 ? 1 : 2);
    }
    return v.toFixed(decimals);
  }

  function signed(v, decimals){
    var s = fmt(Math.abs(v), decimals);
    if (v > 0) return "+" + s;
    if (v < 0) return "-" + s;
    return s;
  }

  // 只对"当前页 + 当前架次"里可见的图表做十字线联动
  function isShown(el){
    if (!el || !el.closest) return true;
    var page = el.closest(".page");
    if (page && !page.classList.contains("active")) return false;
    var view = el.closest(".flight-view");
    if (view && !view.classList.contains("active")) return false;
    return true;
  }

  function hideAll(){
    for (var i = 0; i < hiders.length; i++) hiders[i]();
  }

  // 屏幕坐标 -> SVG 用户坐标（兼容 preserveAspectRatio="none" 的拉伸）
  function localPoint(svg, clientX, clientY){
    if (svg.getScreenCTM){
      var m = svg.getScreenCTM();
      if (m && m.inverse){
        var pt = svg.createSVGPoint();
        pt.x = clientX;
        pt.y = clientY;
        return pt.matrixTransform(m.inverse());
      }
    }
    var rect = svg.getBoundingClientRect();
    var vb = (svg.viewBox && svg.viewBox.baseVal && svg.viewBox.baseVal.width) || 1500;
    return {x: rect.width ? (clientX - rect.left) / rect.width * vb : 0, y: 0};
  }

  function setup(desc){
    if (!desc || !desc.panels || !desc.panels.length) return;
    var panels = [];
    desc.panels.forEach(function(p){
      var svg = document.getElementById("svg-" + p.id);
      if (!svg || !svg.viewBox) return;
      var dots = [];
      (p.series || []).forEach(function(s){
        var c = document.createElementNS(NS, "circle");
        c.setAttribute("r", "3.2");
        c.setAttribute("fill", s.color);
        c.setAttribute("stroke", THEME.crosshairStroke || "#0a0e13");
        c.setAttribute("stroke-width", "0.9");
        c.setAttribute("opacity", "0");
        svg.appendChild(c);
        dots.push({el: c, s: s});
      });
      var line = document.createElementNS(NS, "line");
      line.setAttribute("stroke", THEME.crosshair || "#e6eef7");
      line.setAttribute("stroke-width", "0.9");
      line.setAttribute("stroke-dasharray", "3 3");
      line.setAttribute("opacity", "0");
      svg.appendChild(line);
      panels.push({p: p, svg: svg, dots: dots, line: line});
    });
    if (!panels.length) return;

    function xFor(p, t){
      return p.x0 + (t - desc.t0) / (desc.t1 - desc.t0) * (p.x1 - p.x0);
    }
    function yFor(p, axisName, v){
      var b = axisName === "right" ? p.right : p.left;
      if (!b) return null;
      var frac = (v - b.min) / (b.max - b.min);
      return p.y1 - frac * (p.y1 - p.y0);
    }
    function hide(){
      tip.style.display = "none";
      panels.forEach(function(P){
        P.line.setAttribute("opacity", "0");
        P.dots.forEach(function(D){ D.el.setAttribute("opacity", "0"); });
      });
    }
    function show(index, clientX, clientY){
      var t = desc.times[index];
      var rows = [];
      panels.forEach(function(P){
        if (!isShown(P.svg)) return;
        var px = xFor(P.p, t);
        P.line.setAttribute("x1", px);
        P.line.setAttribute("x2", px);
        P.line.setAttribute("y1", P.p.y0);
        P.line.setAttribute("y2", P.p.y1);
        P.line.setAttribute("opacity", "0.7");
        P.dots.forEach(function(D){
          var raw = desc.series[D.s.key] ? desc.series[D.s.key][index] : null;
          if (raw === null || raw === undefined){
            D.el.setAttribute("opacity", "0");
            return;
          }
          var v = raw;
          if (D.s.clip === "pos") v = Math.max(0, raw);
          else if (D.s.clip === "neg") v = Math.min(0, raw);
          if (D.s.clip && Math.abs(v) < 1e-9 && Math.abs(raw) > 1e-9){
            D.el.setAttribute("opacity", "0");
          } else {
            var cy = yFor(P.p, D.s.axis, v);
            if (cy === null){
              D.el.setAttribute("opacity", "0");
            } else {
              D.el.setAttribute("cx", px);
              D.el.setAttribute("cy", cy);
              D.el.setAttribute("opacity", "1");
            }
          }
          if (D.s.tooltip !== false && D.s.clip !== "neg"){
            var text = D.s.signed ? signed(raw, D.s.decimals) : fmt(raw, D.s.decimals);
            var unit = D.s.unit ? (" " + D.s.unit) : "";
            rows.push({label: D.s.tooltipLabel || D.s.label, color: D.s.color, text: text + unit});
          }
        });
      });
      if (!rows.length){ hide(); return; }
      var html = '<div class="tip-t">t = ' + t.toFixed(2) + " s</div><table>";
      rows.forEach(function(r){
        html += '<tr><td><i style="background:' + r.color + '"></i>' +
          r.label + '</td><td class="v">' + r.text + "</td></tr>";
      });
      html += "</table>";
      tip.innerHTML = html;
      tip.style.display = "block";
      var x = clientX + 16, y = clientY + 16;
      var w = tip.offsetWidth, h = tip.offsetHeight;
      if (x + w > window.innerWidth - 8) x = clientX - w - 16;
      if (y + h > window.innerHeight - 8) y = clientY - h - 16;
      tip.style.left = Math.max(4, x) + "px";
      tip.style.top = Math.max(4, y) + "px";
    }
    function hover(ev){
      var clientX = ev.clientX, clientY = ev.clientY;
      var q = localPoint(ev.currentTarget, clientX, clientY);
      if (!isFinite(q.x)) return;
      var t = desc.t0 + (q.x - panels[0].p.x0) / (panels[0].p.x1 - panels[0].p.x0) *
        (desc.t1 - desc.t0);
      var idx = nearest(desc.times, t);
      if (idx >= 0) show(idx, clientX, clientY);
    }
    function touchHover(ev){
      var touch = ev.touches && ev.touches[0];
      if (!touch) return;
      var q = localPoint(ev.currentTarget, touch.clientX, touch.clientY);
      if (!isFinite(q.x)) return;
      var t = desc.t0 + (q.x - panels[0].p.x0) / (panels[0].p.x1 - panels[0].p.x0) *
        (desc.t1 - desc.t0);
      var idx = nearest(desc.times, t);
      if (idx >= 0) show(idx, touch.clientX, touch.clientY);
    }
    panels.forEach(function(P){
      P.svg.addEventListener("mousemove", hover);
      P.svg.addEventListener("mouseleave", hide);
      P.svg.addEventListener("touchstart", touchHover, {passive: true});
      P.svg.addEventListener("touchmove", touchHover, {passive: true});
    });
    hiders.push(hide);
  }

  if (DATA.overview) setup(DATA.overview);
  (DATA.flights || []).forEach(setup);

  // ---------------- 翻页 ----------------
  var pages = qsa(".page");
  var labelEl = document.getElementById("page-label");
  var dotsBox = document.getElementById("dots");
  var prevBtns = qsa('[data-nav="prev"]');
  var nextBtns = qsa('[data-nav="next"]');
  var current = 0;
  var dots = [];

  pages.forEach(function(page, i){
    if (!dotsBox) return;
    var dot = document.createElement("button");
    dot.type = "button";
    dot.className = "dot";
    dot.title = "第 " + (i + 1) + " 页 · " + (page.querySelector(".page-title") || {}).textContent;
    dot.setAttribute("aria-label", "第 " + (i + 1) + " 页");
    dot.addEventListener("click", function(){ showPage(i, true); });
    dotsBox.appendChild(dot);
    dots.push(dot);
  });

  function showPage(index, updateHash){
    if (!pages.length) return;
    index = Math.max(0, Math.min(pages.length - 1, index));
    current = index;
    pages.forEach(function(page, i){
      page.classList.toggle("active", i === index);
    });
    if (labelEl) labelEl.textContent = "第 " + (index + 1) + " / " + pages.length + " 页";
    dots.forEach(function(dot, i){
      dot.classList.toggle("active", i === index);
    });
    prevBtns.forEach(function(btn){ btn.disabled = index === 0; });
    nextBtns.forEach(function(btn){ btn.disabled = index === pages.length - 1; });
    hideAll();
    if (updateHash){
      try { history.replaceState(null, "", "#" + pages[index].id); } catch (err) { /* file:// */ }
    }
  }

  prevBtns.forEach(function(btn){
    btn.addEventListener("click", function(){ showPage(current - 1, true); });
  });
  nextBtns.forEach(function(btn){
    btn.addEventListener("click", function(){ showPage(current + 1, true); });
  });

  document.addEventListener("keydown", function(ev){
    if (ev.defaultPrevented || ev.ctrlKey || ev.metaKey || ev.altKey) return;
    var tag = (ev.target && ev.target.tagName) || "";
    if (tag === "INPUT" || tag === "SELECT" || tag === "TEXTAREA") return;
    var key = ev.key;
    if (key === "ArrowLeft" || key === "PageUp"){
      ev.preventDefault();
      showPage(current - 1, true);
    } else if (key === "ArrowRight" || key === "PageDown"){
      ev.preventDefault();
      showPage(current + 1, true);
    } else if (key === "Home"){
      ev.preventDefault();
      showPage(0, true);
    } else if (key === "End"){
      ev.preventDefault();
      showPage(pages.length - 1, true);
    }
  });

  // ---------------- 平均速度 / 手动统计范围（速度页） ----------------
  // 静态 HTML 里已经画好默认（全程）的平均值和 SVG 图层；这里加载后按同一份
  // 数据重算一遍，用户改起止时间点"应用"时只原地更新数值和图层几何。
  var AVG = null;

  function initAverage(){
    var bar = document.getElementById("avgbar-speed");
    if (!bar) return null;
    var startEl = bar.querySelector("[data-avg-start]");
    var endEl = bar.querySelector("[data-avg-end]");
    var applyEl = bar.querySelector("[data-avg-apply]");
    var resetEl = bar.querySelector("[data-avg-reset]");
    var metaEl = bar.querySelector("[data-avg-meta]");
    var msgEl = bar.querySelector("[data-avg-msg]");
    var viewEls = {
      ias: bar.querySelector('[data-avg-val="ias"]'),
      tas: bar.querySelector('[data-avg-val="tas"]'),
      mach: bar.querySelector('[data-avg-val="mach"]')
    };
    var byIndex = {};
    (DATA.flights || []).forEach(function(fl){ byIndex[fl.index] = fl; });
    var state = {a: null, b: null};

    function toNum(v, fallback){
      var n = parseFloat(v);
      return isFinite(n) ? n : fallback;
    }
    function f1(t){ return (Math.round(t * 10) / 10).toFixed(1); }
    function fsec(t){
      var s = t.toFixed(3);
      s = s.replace(/0+$/, "").replace(/\.$/, "");
      return s || "0";
    }
    function fixed(v, d){
      if (v === null || v === undefined || !isFinite(v)) return "—";
      return v.toFixed(d);
    }
    function setMsg(text, warn){
      if (!msgEl) return;
      msgEl.textContent = text || "";
      msgEl.className = "avg-msg" + (warn ? " warn" : "");
    }
    function currentFlight(){
      var el = document.querySelector(".flight-view.active[data-flight]");
      if (!el) return null;
      var idx = parseInt(el.getAttribute("data-flight"), 10);
      return byIndex[idx] || null;
    }
    // 夹取到 [t0, t1]：写反交换、超界夹边界、完全不相交则退回全程
    function clampRange(fl, rawA, rawB){
      var t0 = fl.t0, t1 = fl.t1;
      var a = toNum(rawA, t0);
      var b = toNum(rawB, t1);
      var notes = [];
      if (a > b){ var tmp = a; a = b; b = tmp; notes.push("起止时间写反，已交换"); }
      var ca = a, cb = b;
      if (ca < t0) ca = t0;
      if (cb > t1) cb = t1;
      if (ca !== a || cb !== b) notes.push("超出当前架次时间轴，已夹到边界");
      if (ca > cb){ ca = t0; cb = t1; notes.push("范围与当前架次不相交，已重置为全程"); }
      return {a: ca, b: cb, notes: notes};
    }
    function slice(fl, a, b){
      var times = fl.times, s = fl.series;
      var n = 0, si = 0, st = 0, sm = 0, ci = 0, ct = 0, cm = 0, v;
      for (var i = 0; i < times.length; i++){
        var t = times[i];
        if (t < a) continue;
        if (t > b) break;
        n++;
        v = s.ias ? s.ias[i] : null;
        if (v !== null && v !== undefined){ si += v; ci++; }
        v = s.tas ? s.tas[i] : null;
        if (v !== null && v !== undefined){ st += v; ct++; }
        v = s.mach ? s.mach[i] : null;
        if (v !== null && v !== undefined){ sm += v; cm++; }
      }
      return {
        samples: n,
        ias: ci ? si / ci : null,
        tas: ct ? st / ct : null,
        mach: cm ? sm / cm : null
      };
    }
    function panelFor(fl){
      var panels = fl.panels || [];
      for (var i = 0; i < panels.length; i++){
        if (panels[i].id === "f" + fl.index + "-speed") return panels[i];
      }
      return null;
    }
    function yFor(p, axis, v){
      if (!axis) return null;
      var span = axis.max - axis.min;
      if (!isFinite(span) || span === 0) return null;
      return p.y1 - (v - axis.min) / span * (p.y1 - p.y0);
    }
    function find(id){ return document.getElementById(id); }
    function updateOverlay(fl, a, b, avg){
      var p = panelFor(fl);
      if (!p) return;
      var suffix = "f" + fl.index + "-speed";
      var span = (fl.t1 - fl.t0) || 1;
      var xa = p.x0 + (a - fl.t0) / span * (p.x1 - p.x0);
      var xb = p.x0 + (b - fl.t0) / span * (p.x1 - p.x0);
      xa = Math.max(p.x0, Math.min(p.x1, xa));
      xb = Math.max(p.x0, Math.min(p.x1, xb));
      if (xb < xa){ var tmp = xa; xa = xb; xb = tmp; }
      var band = find("avg-band-" + suffix);
      if (band){
        band.setAttribute("x", xa.toFixed(1));
        band.setAttribute("width", (xb - xa).toFixed(1));
      }
      var edge0 = find("avg-edge0-" + suffix);
      if (edge0){ edge0.setAttribute("x1", xa.toFixed(1)); edge0.setAttribute("x2", xa.toFixed(1)); }
      var edge1 = find("avg-edge1-" + suffix);
      if (edge1){ edge1.setAttribute("x1", xb.toFixed(1)); edge1.setAttribute("x2", xb.toFixed(1)); }
      [["ias", p.left], ["tas", p.left], ["mach", p.right]].forEach(function(pair){
        var line = find("avg-" + pair[0] + "-" + suffix);
        if (!line) return;
        var y = yFor(p, pair[1], avg[pair[0]]);
        if (y === null){
          line.setAttribute("opacity", "0");
        } else {
          line.setAttribute("y1", y.toFixed(1));
          line.setAttribute("y2", y.toFixed(1));
          line.setAttribute("opacity", "0.85");
        }
      });
    }
    function render(fl){
      if (!fl) return;
      var result = slice(fl, state.a, state.b);
      if (viewEls.ias) viewEls.ias.textContent = fixed(result.ias, 1);
      if (viewEls.tas) viewEls.tas.textContent = fixed(result.tas, 1);
      if (viewEls.mach) viewEls.mach.textContent = fixed(result.mach, 2);
      if (metaEl){
        metaEl.textContent = "范围 " + f1(state.a) + "–" + f1(state.b) + " s · " +
          result.samples + " 样本 · 底纹 = 统计区间，虚线 = 平均值（回车 = 应用）";
      }
      updateOverlay(fl, state.a, state.b, result);
    }
    function syncInputs(fl){
      var lo = fsec(fl.t0), hi = fsec(fl.t1);
      if (startEl){
        startEl.min = lo; startEl.max = hi;
        startEl.value = fsec(state.a);
      }
      if (endEl){
        endEl.min = lo; endEl.max = hi;
        endEl.value = fsec(state.b);
      }
    }
    function apply(){
      var fl = currentFlight();
      if (!fl) return;
      var out = clampRange(fl, startEl ? startEl.value : state.a,
                           endEl ? endEl.value : state.b);
      state.a = out.a;
      state.b = out.b;
      syncInputs(fl);
      render(fl);
      setMsg(out.notes.join("；"), out.notes.length > 0);
    }
    function reset(){
      var fl = currentFlight();
      if (!fl) return;
      state.a = fl.t0;
      state.b = fl.t1;
      syncInputs(fl);
      render(fl);
      setMsg("已重置为当前架次全程", false);
    }
    function onFlight(value){
      var fl = byIndex[parseInt(value, 10)];
      if (!fl) return;
      var out = clampRange(fl, state.a === null ? fl.t0 : state.a,
                           state.b === null ? fl.t1 : state.b);
      state.a = out.a;
      state.b = out.b;
      syncInputs(fl);
      render(fl);
      if (out.notes.length) setMsg("切换架次后：" + out.notes.join("；"), true);
      else setMsg("", false);
    }

    if (applyEl) applyEl.addEventListener("click", apply);
    if (resetEl) resetEl.addEventListener("click", reset);
    if (startEl) startEl.addEventListener("keydown", function(ev){
      if (ev.key === "Enter"){ ev.preventDefault(); apply(); }
    });
    if (endEl) endEl.addEventListener("keydown", function(ev){
      if (ev.key === "Enter"){ ev.preventDefault(); apply(); }
    });

    var first = currentFlight();
    if (first){
      state.a = first.t0;
      state.b = first.t1;
      syncInputs(first);
      render(first);
    }
    return {apply: apply, reset: reset, onFlight: onFlight};
  }

  AVG = initAverage();

  // ---------------- 架次切换（只换当前页的曲线） ----------------
  var flightBtns = qsa("[data-flight-btn]");
  var flightViews = qsa(".flight-view[data-flight]");
  var flightSelect = document.getElementById("flight-select");
  var flightPlayer = {};
  (DATA.flights || []).forEach(function(fl){ flightPlayer[fl.index] = fl.player || ""; });
  function selectFlight(value){
    flightViews.forEach(function(view){
      view.classList.toggle("active", view.getAttribute("data-flight") === value);
    });
    flightBtns.forEach(function(btn){
      btn.classList.toggle("active", btn.getAttribute("data-flight-btn") === value);
    });
    if (flightSelect && flightSelect.value !== value) flightSelect.value = value;
    if (AVG) AVG.onFlight(value);
    syncPlayer(value);
    hideAll();
  }
  flightBtns.forEach(function(btn){
    btn.addEventListener("click", function(){
      selectFlight(btn.getAttribute("data-flight-btn"));
    });
  });
  if (flightSelect){
    flightSelect.addEventListener("change", function(){
      selectFlight(flightSelect.value);
    });
  }

  // ---------------- 玩家切换（多玩家回放；跳到该玩家第一个架次） ----------------
  var playerBtns = qsa("[data-player-btn]");
  function syncPlayer(value){
    if (!playerBtns.length) return;
    var name = flightPlayer[parseInt(value, 10)] || "";
    playerBtns.forEach(function(btn){
      btn.classList.toggle("active", btn.getAttribute("data-player-btn") === name);
    });
  }
  playerBtns.forEach(function(btn){
    btn.addEventListener("click", function(){
      var name = btn.getAttribute("data-player-btn");
      var target = null;
      (DATA.players || []).forEach(function(player){
        if (player.name === name) target = player.flight;
      });
      if (target !== null) selectFlight(String(target));
    });
  });
  (function initPlayer(){
    var view = document.querySelector(".flight-view.active[data-flight]");
    if (view) syncPlayer(view.getAttribute("data-flight"));
  })();

  // ---------------- 初始页（支持 #page-speed 直链） ----------------
  var startPage = 0;
  var hash = (window.location.hash || "").replace(/^#/, "");
  if (hash){
    for (var i = 0; i < pages.length; i++){
      if (pages[i].id === hash){ startPage = i; break; }
    }
  }
  showPage(startPage, false);

  // ---------------- 保存：二级页（PDF 完整版 / PNG 复盘卡 + 批注开关） ----------------
  var saveModal = document.getElementById("save-modal");
  var saveBtn = document.getElementById("btn-save");
  var saveLastFocus = null;

  function saveIsOpen(){ return !!(saveModal && !saveModal.hidden); }
  function saveOpen(){
    if (!saveModal) return;
    saveLastFocus = document.activeElement;
    saveModal.hidden = false;
    saveModal.setAttribute("aria-hidden", "false");
    document.body.classList.add("modal-open");
    var focusEl = saveModal.querySelector('input[name="save-kind"]:checked') ||
      saveModal.querySelector("input");
    if (focusEl && focusEl.focus) focusEl.focus();
  }
  function saveClose(){
    if (!saveModal) return;
    saveModal.hidden = true;
    saveModal.setAttribute("aria-hidden", "true");
    document.body.classList.remove("modal-open");
    if (saveLastFocus && saveLastFocus.focus) saveLastFocus.focus();
  }
  function saveKind(){
    var el = saveModal && saveModal.querySelector('input[name="save-kind"]:checked');
    return el ? el.value : "pdf";
  }
  function saveAnnot(){
    var el = document.getElementById("save-annot");
    return el ? !!el.checked : true;
  }
  function debriefPrefix(){
    var host = document.getElementById("debrief-src");
    return (DATA.debrief && DATA.debrief.prefix) ||
      (host && host.getAttribute("data-prefix")) || "debrief-";
  }
  function debriefTimestamp(){
    var d = new Date();
    function p(n){ return (n < 10 ? "0" : "") + n; }
    return d.getFullYear() + p(d.getMonth() + 1) + p(d.getDate()) + "-" +
      p(d.getHours()) + p(d.getMinutes()) + p(d.getSeconds());
  }
  // 内嵌 SVG -> canvas -> PNG（不引外部库；SVG 里没有外链，canvas 不会被污染）
  function renderDebriefCanvas(annotated, done, fail){
    var svg = document.getElementById(
      annotated ? "debrief-svg-annotated" : "debrief-svg-clean");
    if (!svg){ if (fail) fail("缺少复盘卡 SVG"); return; }
    var xml = new XMLSerializer().serializeToString(svg);
    if (xml.indexOf('xmlns="http://www.w3.org/2000/svg"') < 0){
      xml = xml.replace("<svg ", '<svg xmlns="http://www.w3.org/2000/svg" ');
    }
    var blob = new Blob(['<?xml version="1.0" encoding="UTF-8"?>' + xml],
      {type: "image/svg+xml;charset=utf-8"});
    var url = URL.createObjectURL(blob);
    var img = new Image();
    img.onload = function(){
      try {
        var canvas = document.createElement("canvas");
        canvas.width = 1600; canvas.height = 1000;
        var ctx = canvas.getContext("2d");
        ctx.fillStyle = "#C9B28A";
        ctx.fillRect(0, 0, 1600, 1000);
        ctx.drawImage(img, 0, 0, 1600, 1000);
        URL.revokeObjectURL(url);
        done(canvas);
      } catch (err) {
        URL.revokeObjectURL(url);
        if (fail) fail("canvas 失败: " + (err && err.message ? err.message : err));
      }
    };
    img.onerror = function(){
      URL.revokeObjectURL(url);
      if (fail) fail("SVG 载入失败");
    };
    img.src = url;
  }
  function exportDebriefPNG(annotated, done, fail){
    renderDebriefCanvas(annotated, function(canvas){
      var name = debriefPrefix() + debriefTimestamp() + ".png";
      var link = document.createElement("a");
      link.href = canvas.toDataURL("image/png");
      link.download = name;
      document.body.appendChild(link);
      link.click();
      document.body.removeChild(link);
      if (done) done({name: name, width: canvas.width, height: canvas.height});
    }, fail);
  }
  // 自动化验证钩子：返回 PNG dataURL（headless 通过 CDP 取回写成样张）
  window.__exportDebrief = function(annotated){
    return new Promise(function(resolve, reject){
      renderDebriefCanvas(annotated !== false, function(canvas){
        resolve(canvas.toDataURL("image/png"));
      }, reject);
    });
  };
  window.__debriefInfo = function(){
    return {prefix: debriefPrefix(), filename: debriefPrefix() + debriefTimestamp() + ".png"};
  };
  window.__saveState = function(){
    return {open: saveIsOpen(), kind: saveKind(), annot: saveAnnot()};
  };
  if (saveBtn) saveBtn.addEventListener("click", function(ev){
    ev.preventDefault();
    saveOpen();
  });
  if (saveModal){
    saveModal.querySelectorAll("[data-save-cancel]").forEach(function(el){
      el.addEventListener("click", function(ev){ ev.preventDefault(); saveClose(); });
    });
    var generateBtn = document.getElementById("save-generate");
    if (generateBtn) generateBtn.addEventListener("click", function(ev){
      ev.preventDefault();
      var kind = saveKind();
      var annot = saveAnnot();
      var body = document.body;
      var state = {kind: kind, annot: annot, result: ""};
      body.classList.toggle("no-annot", !annot);
      saveClose();
      if (kind === "png"){
        exportDebriefPNG(annot, function(info){
          state.result = info.name;
          body.setAttribute("data-last-save",
            "png:" + info.name + ":" + info.width + "x" + info.height);
        }, function(message){
          state.result = "error:" + message;
          body.setAttribute("data-last-save", "png-error:" + message);
        });
      } else {
        body.setAttribute("data-last-save", "pdf:" + (annot ? "annotated" : "clean"));
        window.setTimeout(function(){
          // 打印期间保持 body.no-annot；打印结束后由 afterprint 统一移除
          try {
            window.print();
          } catch (err) {
            body.classList.remove("no-annot");
          }
        }, 40);
      }
    });
    var cancelBtn = document.getElementById("save-cancel");
    if (cancelBtn) cancelBtn.addEventListener("click", function(ev){
      ev.preventDefault();
      saveClose();
    });
  }
  document.addEventListener("keydown", function(ev){
    if (!saveIsOpen()) return;
    if (ev.key === "Escape"){
      ev.preventDefault();
      saveClose();
    } else if (ev.key === "Enter" && ev.target && ev.target.tagName === "INPUT"){
      ev.preventDefault();
      var gen = document.getElementById("save-generate");
      if (gen) gen.click();
    }
  });
  window.addEventListener("afterprint", function(){
    document.body.classList.remove("no-annot");
  });
})();
"""

NOSCRIPT = """<noscript><style>
html,body{height:auto;overflow:auto}
.app{height:auto}
main{position:static}
.page{position:relative;inset:auto;display:flex!important;flex-direction:column;
  height:auto;min-height:70vh;border-top:1px solid var(--line);margin-top:8px}
.page-body{min-height:420px}
.pager,.side,.hint,.flights{display:none}
</style><p style="color:var(--muted);font-size:12px;padding:0 22px">
本报告用脚本实现翻页；当前浏览器禁用了脚本，已改为纵向平铺显示全部页面。
</p></noscript>"""

#: 页眉「保存」打开的二级页（模态）：输出类型 + 批注开关。
SAVE_MODAL = """<div class="save-modal" id="save-modal" role="dialog" aria-modal="true"
     aria-labelledby="save-title" aria-hidden="true" hidden>
  <div class="save-backdrop" data-save-cancel></div>
  <div class="save-dialog" role="document">
    <div class="save-head"><b id="save-title">保存设置</b><span>SAVE / EXPORT</span></div>
    <div class="save-body">
      <div class="save-group">
        <div class="save-label">输出类型 / OUTPUT</div>
        <label class="save-opt"><input type="radio" name="save-kind" value="pdf" checked>
          <span><b>PDF 完整版</b><i>9 页 · 档案纸 · 每页独立分页（打印 / 另存为 PDF）</i></span></label>
        <label class="save-opt"><input type="radio" name="save-kind" value="png">
          <span><b>PNG 单张复盘卡</b><i>1600 × 1000 · 档案袋风格 · 下载单张 PNG</i></span></label>
      </div>
      <div class="save-group">
        <label class="save-check"><input type="checkbox" id="save-annot" checked>
          <span><b>保留批注</b><i>红色审阅标记 / 手绘双圈 / 打字机短注；关闭后只留曲线、坐标、事件竖线</i></span></label>
      </div>
    </div>
    <div class="save-foot">
      <button type="button" class="save-btn ghost" id="save-cancel">取消</button>
      <button type="button" class="save-btn" id="save-generate">生成</button>
    </div>
  </div>
</div>"""


def render_report(
    model: LogModel,
    title: Optional[str] = None,
    theme: Optional[str] = None,
    limits: Optional[SpeedLimits] = None,
) -> str:
    """把模型渲染成完整 HTML 字符串（翻页式、单文件自包含）。

    ``theme`` 取 :data:`THEMES` 里的键（``blueprint`` / ``glass`` / ``manual`` /
    ``tactical`` / ``clean`` / ``mfd``）；省略时用 :data:`DEFAULT_THEME`
    （= ``blueprint`` A 工程蓝图）。

    ``limits`` 是预先读好的限速表（:func:`load_speed_limits`）；省略时跳过
    限速批注（不影响其它批注）。
    """
    theme_obj = _apply_theme(get_theme(theme))
    payload = _build_payload(model, limits)
    page_title = title or payload["title"]
    page_count = payload["html"]["page_count"]
    pages_html = "\n".join(payload["html"]["pages"])

    meta = model.meta or {}
    replay = meta.get("replay") or {}
    meta_bits = ["文件 <code>%s</code>" % _html_escape(payload["file"])]
    if replay:
        header = replay.get("header") or {}
        if header.get("title"):
            meta_bits.append("场景 %s" % _html_escape(header["title"]))
        meta_bits.append("玩家 %s" % _html_escape(replay.get("selected_players") or "—"))
        if header.get("file_version"):
            meta_bits.append("ACMI %s" % _html_escape(header["file_version"]))
        meta_bits.append("导出器 %s" % _html_escape(header.get("exporter") or "wrpl-acmi"))
    else:
        meta_bits.append("机型 %s" % _html_escape(meta.get("vehicle") or "未知"))
        meta_bits.append("WtHud %s" % _html_escape(meta.get("app") or "未知"))
        meta_bits.append(
            "schema %s" % _html_escape(meta.get("schema") if meta.get("schema") is not None else "?")
        )
    meta_bits.append("采样 %s" % _html_escape(meta.get("rate") or "?"))
    meta_bits.append("生成于 %s" % _html_escape(model.summary.get("generated_utc", "")))
    for item in payload.get("vne") or []:
        meta_bits.append(
            "限速 %s %s km/h" % (_html_escape(item["vehicle"]), _html_escape(_fmt_number(item["vne"], 0)))
        )
    subline = " · ".join(meta_bits)
    replay_banner = ""
    if replay:
        replay_banner = (
            '<div class="subline replay-banner">'
            "<b>回放估算数据</b>（IAS = EAS 近似，非座舱遥测）"
            " · 姿态 / 过载 / AOA 由回放位置反演估算，速度与高度为估算值，"
            "仅供参考，不等同于游戏内遥测。</div>"
        )

    pager = (
        '<nav class="pager" aria-label="翻页">'
        '<button type="button" class="pbtn" data-nav="prev" id="nav-prev"'
        ' aria-label="上一页" disabled>&#8249; 上一页</button>'
        '<span class="page-label" id="page-label">第 1 / %d 页</span>'
        '<span class="dots" id="dots"></span>'
        '<button type="button" class="pbtn" data-nav="next" id="nav-next"'
        ' aria-label="下一页">下一页 &#8250;</button>'
        '<span class="hint">&#8592; &#8594; / PageUp PageDown 翻页 · 鼠标悬停看数值</span>'
        "</nav>" % page_count
    )
    save_button = (
        '<button type="button" class="header-save" id="btn-save"'
        ' title="导出 PDF 完整版 / PNG 复盘卡">保存</button>'
    )
    debrief_host = payload["html"].get("debrief") or ""
    stem = os.path.splitext(os.path.basename(model.path))[0]

    data_json = json.dumps(
        {
            "title": page_title,
            "overview": payload["overview"],
            "flights": payload["flights"],
            "players": payload.get("players") or [],
            "debrief": {"prefix": "debrief-%s-" % stem},
            "limits": payload.get("limits"),
            "theme": {
                "key": theme_obj.key,
                "label": theme_obj.label,
                "crosshair": COLORS["crosshair"],
                "crosshairStroke": COLORS["crosshair_stroke"],
            },
        },
        ensure_ascii=False,
        allow_nan=False,
        separators=(",", ":"),
    )
    # 避免数据里出现 </script> 提前闭合
    data_json = data_json.replace("</", "<\\/")

    return (
        "<!DOCTYPE html>\n"
        '<html lang="zh-CN" data-theme="%s">\n<head>\n<meta charset="utf-8">\n'
        '<meta name="viewport" content="width=device-width, initial-scale=1">\n'
        "<title>%s</title>\n<style>%s</style>\n</head>\n<body>\n"
        '<div class="app">\n'
        "<header>\n"
        '<div class="hrow"><h1>%s</h1>%s%s</div>\n'
        "%s"
        "%s"
        '<div class="subline">%s</div>\n'
        "</header>\n"
        "%s\n"
        '<main id="pages">\n%s\n'
        '<button type="button" class="side prev" data-nav="prev" aria-label="上一页" disabled>'
        "&#8249;</button>"
        '<button type="button" class="side next" data-nav="next" aria-label="下一页">'
        "&#8250;</button>\n"
        "</main>\n"
        "%s\n"
        "</div>\n"
        "%s\n"
        '<div id="tip" class="tip"></div>\n'
        "%s\n"
        "<script>window.__FLIGHTLOG__=%s;</script>\n"
        "<script>%s</script>\n"
        "</body>\n</html>\n"
        % (
            theme_obj.key,
            _html_escape(page_title),
            _css(theme_obj),
            "回放分析" if replay else "飞行日志分析",
            payload["html"]["flight_selector"],
            save_button,
            payload["html"].get("player_switch") or "",
            replay_banner,
            subline,
            NOSCRIPT,
            pages_html,
            pager,
            debrief_host,
            SAVE_MODAL,
            data_json,
            JS,
        )
    )


def write_report(
    model: LogModel,
    out_path: str,
    title: Optional[str] = None,
    theme: Optional[str] = None,
    limits: Optional[SpeedLimits] = None,
) -> str:
    """渲染并写盘（UTF-8，自动建目录），返回绝对路径。"""
    out_path = os.path.abspath(out_path)
    directory = os.path.dirname(out_path)
    if directory:
        os.makedirs(directory, exist_ok=True)
    content = render_report(model, title=title, theme=theme, limits=limits)
    with open(out_path, "w", encoding="utf-8", newline="\n") as fh:
        fh.write(content)
    return out_path
