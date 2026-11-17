# WtHud 飞行日志解析 / 可视化工具（flightlog）

独立的小工具：读取 WtHud 录制的 `wtlog-*.jsonl`，把 **能量、速度、高度、过载、姿态** 等
随时间（以及死亡 / 复活 / 换机型事件）的变化画成图，生成**一个自包含的 HTML 文件**
（内联 CSS/JS/SVG，无 External 依赖，离线双击即看）。

报告是**翻页式**的：第 1 页是概览（汇总卡片 + 比能量缩略图），之后**每个数据主题一页**
（速度 / 高度 / Es / dEs/dt / Vy / 迎角与过载 / 姿态），用 `‹ 上一页` `下一页 ›`、
键盘 `←` `→` / `PageUp` `PageDown`、底部圆点翻页；每页固定铺满视口，**不用长滚动**。
多架次（多次 spawn）时页眉有架次选择器，切换后当前页的曲线换成该架次。
**速度页顶部显示平均 IAS / TAS / Mach**，可以手动输入起止时间、只统计所选区间
（默认整个当前架次），区间在图上用浅色底纹标出、平均值画成水平虚线。
可选再用 Pillow 出一张 PNG 预览。

> 该工具**不改动** `wt-hud/src` 下的任何产品代码，只是 `wt-hud/tools/` 里的独立脚本。
> 许可证与仓库一致：AGPL-3.0-or-later。

---

## 1. 快速开始

只需要 Python 3.8+（开发/验证环境为 3.13），**纯标准库**，不需要 pip install。

```bat
:: 方式 A：在 wt-hud\tools 目录下用模块方式运行
cd wt-hud\tools
python -m flightlog "C:\path\to\recordings\wtlog-20261117-131039-jh_7a.jsonl"

:: 方式 B：任意工作目录，直接用 analyze.py（脚本会自己处理 sys.path）
python "D:\...\wt-hud\tools\flightlog\analyze.py" "C:\path\to\wtlog-xxx.jsonl"

:: 只打印统计 JSON，不写 HTML
python -m flightlog "C:\path\to\wtlog-xxx.jsonl" --summary

:: 指定输出，并额外出 PNG 预览（需要 Pillow；没有就只出 HTML）
python -m flightlog "C:\path\to\wtlog-xxx.jsonl" --out out\demo.html --png out\preview.png
```

默认输出目录是**本工具自己的** `out/`（`wt-hud/tools/flightlog/out/`），与当前工作目录无关；
文件名默认为 `<日志名>.html`，例如 `wtlog-20261117-131039-jh_7a.html`。

### 独立可执行文件（不需要 Python）

`FlightLog.exe` 与 WtHud 完全分离，可以单独启动，**不需要安装 Python**：

- **双击** → 弹出文件选择框（默认目录 `桌面\WtHud-发布\recordings`）→ 选一个 `.jsonl` → 自动生成 HTML 并用默认浏览器打开
- 也可以把日志文件**拖到 exe 图标上**
- 报告写到 `%LOCALAPPDATA%\WtHud\flightlog\`，文件名带时间戳，不会覆盖旧报告
- 重新打包：双击 `build_exe.bat`（需要本机 Python + `pip install pyinstaller`），产物在 `flightlog\dist\FlightLog.exe`

### 自检

```bat
cd wt-hud\tools\flightlog
python selftest.py
```

纯标准库合成样本自检（解析、坏行跳过、缺字段、事件切分、能量公式、**分页 HTML 结构**、
**平均速度纯函数与速度页控件结构**、自包含检查、CLI），当前 **152/152 项全部通过**。

---

## 2. 命令行参数

| 参数 | 说明 |
| --- | --- |
| `log`（位置参数） | `wtlog-*.jsonl` 路径 |
| `--out PATH` | HTML 输出路径；不传则 `out/<日志名>.html` |
| `--summary` | 只打印统计 JSON（若同时给 `--out` 也会写 HTML） |
| `--png PATH` | 额外生成 PNG 预览（Pillow 可选，缺库时跳过并提示） |
| `--title TEXT` | HTML 页面标题 |
| `--gap SEC` | 相邻样本间隔超过多少秒切新架次，默认 `30` |

`python -m flightlog --help` 可查看完整帮助。

---

## 3. 输出：HTML 报告里有什么

报告是**翻页式**的：一共 N 页，**每页只放一个数据主题**，页面固定铺满视口（不出现长滚动）。

### 页序

| 页 | 内容 |
| --- | --- |
| 1 | **概览**：汇总卡片（总时长 / 采样率 / 最大 IAS / 最大 Mach / 最大高度 / 最大 G / 比能量范围 / 事件数）+ 解析提示 + 当前架次的统计徽标 + **比能量 Es 缩略图** |
| 2 | 速度：IAS、TAS（左轴 km/h）+ Mach（右轴）；顶部有**平均速度**（可手动设置统计范围） |
| 3 | 高度：气压高度 `alt_m` + 雷达高度 `ralt_m`（虚线，有数据才画） |
| 4 | 比能量高度 Es：`Es = alt + v²/2g`（米），带面积填充 |
| 5 | 能量变化率 dEs/dt：绿色 = 获得能量，红色 = 损失能量（m/s），正负分色填充 |
| 6 | 垂直速度 Vy（m/s） |
| 7 | 迎角与过载：AoA（左轴 °）+ Ny（右轴 g） |
| 8 | 姿态：Pitch（左轴 °）+ Bank（右轴 °，有数据才画） |
| 9 | **全局总览**（仅多架次时）：速度 / 高度 / Es 三张合并时间轴图，竖虚线为架次边界 |

> 若某类数据整份日志都没有（例如旧日志没有姿态），对应的页会**自动省略**，
> 页码与圆点数量随之变化（例如 `第 3 / 8 页`）。

### 翻页方式

* 底部：`‹ 上一页` / `页码 第 3 / 9 页` / 圆点导航（点圆点跳页）/ `下一页 ›`
* 页面左右两侧：半透明的大箭头按钮（窄屏自动隐藏）
* 键盘：`←` `→`、`PageUp` `PageDown` 上一页/下一页；`Home` `End` 首尾页
* 翻页只是 CSS 切换 `.page.active`，**不重新加载、不重画**；页码会同步到地址栏 hash
  （例如 `report.html#page-energy` 可直链到能量页）

### 多架次（多次 spawn / 换机型 / 时间跳变）

* 页眉右上角有**架次选择器**：≤6 架次用分段按钮（`架次 1 · jh_7a`），
  更多架次自动变成下拉框（14 架次也放得下）；
* 切换后**只替换当前页**的曲线为该架次，架次边界/事件竖线仍照常显示；
* 第 9 页的"全局总览"始终展示全部架次，不受选择器影响。

### 事件

`death` / `spawn` / `vehicle_change` 以彩色竖虚线 + 标签画在每条时间轴上方
（事件时间线色条），并**贯穿该页图表**；死亡后的地面段用浅红色底纹标出；
`valid=false` 的帧用浅灰底纹标出。**每一页都保留事件条与标签。**

### 悬停

鼠标在**当前页**图上移动 → 该页图表显示十字线与数据点，浮窗列出**该页**的数值
（例如速度页只列 IAS/TAS/Mach，不串到别的页）；切页/切架次会自动收起浮窗。
触屏按下滑动同样可用。

### 平均速度 / 手动统计范围（只在速度页）

速度页顶部有一条控件：`平均 IAS` `平均 TAS` `平均 Mach` 三个数值 + 起止时间输入框
`范围 [开始] s – [结束] s` + `应用` / `重置` 按钮。

* **默认范围 = 当前架次的整个时间轴**；改输入后点 `应用`（或在输入框里按 `回车`），
  只统计 `t` 落在 `[开始, 结束]`（闭区间）内的样本；
* 数值下方小字注明实际范围与样本数（如 `范围 120.0–180.0 s · 1814 样本`）；
* 图上同步标出所选区间：半透明浅蓝**底纹** + 两条**边界竖虚线**，并把平均 IAS、
  平均 TAS（左轴）和平均 Mach（右轴）画成对应颜色的**水平虚线**（图例里有说明）；
* 非法输入不会崩：`结束 < 开始` 自动交换；超出架次时间轴自动夹到边界；
  与当前架次完全不相交则退回全程——都会在数值下方给出黄色提示；
* 切换架次时**保留当前输入值**，但按新架次的时间轴重新夹取并重算平均；
  切页不影响已设置的范围；
* 平均 IAS/TAS/Mach 各自只统计"该字段有值"的样本（旧日志缺 Mach 时平均 Mach 显示 `—`），
  与悬停十字线、其它页完全兼容；
* 禁用 JS 时静态 HTML 仍显示**默认全程平均**与区间底纹/虚线，只是不能改范围。

纯函数 `parse.average_speed(samples, t1, t2)` 负责这段计算：返回
`{"ias","tas","mach","samples","duration_s","range_start","range_end",...}`，
区间内没有样本时返回 `None`（自检里直接断言它）。

### "自包含"的含义

* 没有 `<script src>`、`<link>`、`<img>`、`@import`，不访问网络；
* 图表是 Python 直接算好的内联 `<svg>`，**浏览器禁用 JS 也能看到全部图**
  （`<noscript>` 会退化成"纵向平铺所有页"，仍可阅读）；内联 JS 只负责翻页、
  架次切换、悬停浮窗；
* 文件大小：8k 样本的日志约 1.6 MB（大部分是悬停用的全量数据）；14 架次 / 26k 样本
  的日志约 5.6 MB。


---

## 4. 日志格式（输入）

JSONL，一行一个 JSON 对象，共四类记录：

```jsonc
{"type":"meta","schema":1,"app":"0.4.40","utc":"...","vehicle":"jh_7a","rate":"per-sample"}
{"type":"sample","t":0.342,"utc":"...","valid":true,"inflight":true,"vehicle":"jh_7a",
 "ias_kmh":887,"tas_kmh":984,"mach":0.82,"alt_m":2007,"ralt_m":579.3,"vy_ms":-11.9,
 "aoa_deg":5.1,"aos_deg":-1.7,"ny_g":2.99,"roll_rate_dps":9,"hdg_deg":163.3,"has_hdg":true,
 "pitch_deg":3.12,"bank_deg":-77.91,"att_src":"Game","att_conf":1,
 "gear_pct":0,"flaps_pct":0,"airbrake_pct":0,"fuel_kg":3115,"fuel0_kg":9912}
{"type":"event","kind":"death|spawn|vehicle_change","utc":"...","vehicle":"..."}
{"type":"end","utc":"..."}
```

工具实际用到：`t`、`utc`、`valid`、`inflight`、`vehicle`、`ias_kmh`、`tas_kmh`、`mach`、
`alt_m`、`ralt_m`、`vy_ms`、`aoa_deg`、`ny_g`、`pitch_deg`、`bank_deg`；其余字段解析但不上图。

### 容错约定

| 情况 | 行为 |
| --- | --- |
| 坏 JSON / 非对象行 / 缺 `t` / `t` 不是数字 | 计数跳过，继续解析 |
| 样本乱序 | 按 `t` 稳定排序后再切架次 |
| 缺少 `ralt_m` | 该点 `None`，雷达高度曲线断开 |
| 旧日志没有 `vehicle` / `pitch_deg` / `bank_deg` | 机型回退到 `meta.vehicle`；姿态图自动不画 |
| 事件只有 `utc` 没有 `t` | 用样本 `utc - t` 的中位数偏移换算成秒 |
| 没有 `spawn/death` 事件（旧日志） | 按样本 `vehicle` 字段变化或时间跳变切架次 |
| 未知 `type` / 未知 `kind` | 忽略未知 type；未知 kind 只画竖线不切分 |
| 文件里没有 `sample` | 抛出 `LogError`，CLI 退出码 2 |

---

## 5. 架次（flight）切分规则

一个架次 ≈ 一次"出航"：

1. `spawn` / `vehicle_change` 事件开启新架次（事件时间 ≤ 当前样本时间即生效）；
2. `death` 事件只做标记，之后的样本（坠机/地面状态）仍计入该架次，直到下一次
   `spawn` / `vehicle_change` 或文件结束；
3. 相邻样本间隔 > `--gap`（默认 30 s）→ 切新架次（原因 `gap`）；
4. 样本里的 `vehicle` 字段发生变化（没有事件的老日志）→ 切新架次（`vehicle_field`）；
5. 文件第一段没有 spawn 时，架次原因为 `log_start`。

每个架次的"原因"会显示在报告标题旁（`日志起点` / `复活生成` / `换机型` / `时间跳变`）。

---

## 6. 能量定义与导数处理

**比能量高度**（单位米，把动能折算成等效高度）：

```
Es = alt_m + (tas_kmh / 3.6)² / (2 × 9.80665)
```

**能量变化率** `dEs/dt`（m/s，**正 = 获得能量**：爬升或加速；负 = 损失）：

* 不直接用相邻差分——日志里高度是整数米、速度是整数 km/h，在 900 km/h 附近
  1 km/h 量化台阶 ≈ 7 m 的 Es 跳变，直接差分会全是锯齿；
* 采用**滑动窗口线性回归**（默认 ±1.5 s，至少 5 个点、跨度 ≥ 0.8 s）求导；
* 窗口内出现以下情况则该点置 `None`（图上一段空白，不制造假尖峰）：
  * `valid=false` 的帧；
  * 相邻样本间隔 > 2.5 s；
  * 单步比能量跳变 > 600 m（坠机/传送/死亡导致的状态重置）。

**能量获得 / 损失积分** = ∫max(0, dEs/dt)dt 与 ∫min(0, dEs/dt)dt，单位米。
**空中能量变化**只取 `inflight=true` 的首尾样本，避免坠机后地面 0 速度把首尾差拉偏。

---

## 7. `--summary` 统计摘要

输出结构（节选）：

```jsonc
{
  "file": "wtlog-20261117-131039-jh_7a.jsonl",
  "path": "C:\\...\\wtlog-20261117-131039-jh_7a.jsonl",
  "generated_utc": "2026-11-17T12:54:21Z",
  "meta": {"schema": 1, "app": "0.4.40", "vehicle": "jh_7a", "rate": "per-sample", ...},
  "counts": {"lines": 8360, "samples": 8356, "events": 2, "bad_json": 0, "flights": 2, ...},
  "time": {"t_min": 0.342, "t_max": 295.744, "span_s": 295.402},
  "totals": {
    "duration_s": 295.402, "samples": 8356, "flights": 2, "events": 2,
    "max_ias_kmh": 1242.0, "max_tas_kmh": 1349.0, "max_mach": 1.12,
    "max_alt_m": 3775.0, "max_ny_g": 13.68, "max_bank_deg": 120.17,
    "energy_min_m": 0.0, "energy_max_m": 10285.217,
    "energy_delta_m": -5784.2, "energy_delta_inflight_m": -165.0
  },
  "event_counts": {"death": 1, "spawn": 1},
  "events": [{"kind": "death", "label": "死亡", "t": 293.58, "vehicle": null}, ...],
  "flights": [ { "index": 1, "vehicle": "jh_7a", "duration_s": 293.56, "samples": 8304,
                 "ias": {...}, "tas": {...}, "mach": {...}, "alt": {...}, "ralt": {...},
                 "vy": {...}, "aoa": {...}, "ny": {...}, "pitch": {...}, "bank": {...},
                 "energy": {...}, "events": [...], ... } ],
  "warnings": ["..."]
}
```

---

## 8. 代码结构

```
wt-hud/tools/flightlog/
├─ __init__.py        # 包导出
├─ __main__.py        # CLI（python -m flightlog）
├─ analyze.py         # 任意 cwd 可用的便捷入口（内部转调 __main__）
├─ parse.py           # JSONL 解析 / 容错 / 架次切分 / 能量 / 统计 / 平均速度纯函数
├─ report.py          # 自包含翻页 HTML：每页一张内联 SVG + 内联 CSS/JS
│                     # （翻页、架次、悬停、速度页平均速度与手动范围）
├─ preview_png.py     # 可选 Pillow PNG 预览（缺库优雅降级）
├─ selftest.py        # 合成样本自检（纯标准库）
├─ README.md          # 本文件
└─ out/               # 生成物（HTML / PNG / 截图），默认输出目录
```

数据流：`parse.analyze()` → `LogModel`（meta + flights + series + stats + summary）
→ `report._build_payload()`（按页组装：每架次每主题一张 SVG + 悬停几何）
→ `report.render_report()`（页面 + CSS + JS + JSON 数据）→ 单文件 HTML。

---

## 9. 已验证

以下都用**真实日志**跑过（`out/` 里有生成物）：

* `wtlog-20261117-204002-jh_7a.jsonl`（11583 样本 / 390.7 s / 2 架次 / 4 事件）：
  → `out/wtlog-20261117-204002-jh_7a.html`（1888 KB，**9 页**：
  概览 + 7 个数据主题 + 全局总览）。无头 Edge（1600×900）逐页截图
  `out/page-01.png` … `out/page-09.png`，页面无溢出（0×0）、图表铺满、导航可见。
* `wtlog-20261117-131039-jh_7a.jsonl`（8356 样本 / 295.4 s / 2 架次 / death+spawn）：
  → `out/wtlog-20261117-131039-jh_7a.html`（1613 KB，9 页）。
* 多架次压力测试：当时正在录制的日志（14 架次 / 26141 样本 / 48 事件）→ 9 页、
  112 个架次视图、架次选择器自动变成 14 项下拉框，翻页/切架次均正常
  （该测试文件未保留在 `out/`）。
* 旧格式单架次日志 `wtlog-20261117-125220-f_15ex.jsonl`（无事件、无 `vehicle`、
  含 1 条坏行）：→ `out/wtlog-20261117-125220-f_15ex.html`（260 KB，8 页，
  无全局总览页、无架次选择器）；速度页平均控件正常、0 JS 异常（未回归）。
* 空文件 / 只有坏行：抛出 `LogError`，CLI 退出码 **2**；纯地面日志（IAS≈0）正常出报告。
* 交互验证（无头 Edge + 本地 HTTP + 内联 iframe 测试页，**40/40 通过、0 JS 异常**）：
  初始页 / 页码文字、`下一页`按钮、`←` `→` `PageUp` `PageDown` `Home` `End` 键盘、
  圆点跳转、左右大按钮、9 页都有事件条、悬停浮窗只列当前页 3 行且不串页、
  切架次后停留当前页且曲线换成 `svg-fN-*`、14 架次下拉切换、无横向/纵向滚动。
* **平均速度 / 手动范围**（无头 Edge 通过 CDP 真实点击，**44/44 断言通过、0 JS 异常**）：
  默认全程平均（994.0 / 1104.5 / 0.92）、改 `120–180 s` 点应用后数值变为
  993.7 / 1180.2 / 0.99 且底纹/边界/平均虚线同步移动、重置回全程、
  结束<开始自动交换、超范围夹取到 `6.8–395.8 s`、空/非数字输入退回全程、
  输入框回车即应用、切架次后按新时间轴夹取并重算（架次 2：1.0 / 1.0 / 0.00）、
  悬停十字线与翻页无回归。截图 `out/avg-default.png`、`out/avg-range.png`，
  拼图 `out/preview-avg.png`。
* `python selftest.py`：**152/152 通过**（原 105 项 + 平均速度函数 21 项 + 速度页平均控件结构 26 项）。

预览图：

* `out/preview.png` — 概览页 + 比能量页的纵向拼图（Pillow 生成），可直接查看翻页效果。
* `out/preview-avg.png` — **速度页平均速度**两张截图拼图：默认全程 vs 手动 `120–180 s`。
* `out/avg-default.png` / `out/avg-range.png` — 上述两张单图（1600×900）。
* `out/page-01.png` … `out/page-09.png` — 逐页无头截图（1600×900）。

---

## 10. 已知限制 / 后续建议

1. **悬停数据是全量内联**：样本极多（>10 万）时 HTML 会偏大，可按需抽稀或改成分片加载。
   多架次日志的每个架次都会独立渲染一套页面图表（14 架次 / 26k 样本约 5.6 MB）。
2. **dEs/dt 是平滑值**：±1.5 s 窗口会钝化瞬时机动，短于窗口的高频能量尖峰看不出来；
   需要更灵敏可调 `parse.ENERGY_RATE_WINDOW_S`（图与统计会同步变化）。
3. **比能量只算了平动动能 + 位能**，不含燃油/武器质量变化；`tas_kmh` 缺失时退化为位能高度。
4. **事件时间来自 HUD 事件记录**，与游戏内真实死亡时刻可能有 1–2 s 延迟；死亡后的
   样本仍归入该架次（报告里用浅红底纹标出）。
5. **多架次按顺序拼在全局总览里**，架次之间有断档时不画时间间隔（横轴仍按真实 `t`，会留空白）。
6. **姿态角在 `att_conf=0`（Unavailable）时仍可能上报 0**，会被当成真实 0 画出来；
   需要更严格可过滤 `att_conf`。
7. **PNG 预览只用最长架次**，且只有能量/速度/高度三联图；完整交互请看 HTML。
8. **SVG 用 `preserveAspectRatio="none"` 铺满视口**：窗口比例接近 16:9 时基本无变形，
   极端窄高窗口（如竖屏）里坐标文字会被纵向拉长；这是"每页固定铺满、不滚动"的取舍。
9. **翻页依赖内联 JS**：禁用脚本时 `<noscript>` 会退化成"全部页面纵向平铺"，
   能看到所有图但没有翻页/悬停。
10. 目前是"每次手动跑生成报告"，尚未接入 WtHud UI 的"打开最近日志"按钮——
    如果要集成，建议由 WtHud 调 `python -m flightlog <log> --out <临时 HTML>` 再启动浏览器。
11. **平均速度是样本等权的算术平均**，不是按时间加权；采样率基本均匀时两者几乎一致，
    但如果日志有明显的采样稀疏段（断档、掉帧合并），平均值会偏向样本密集的一侧。
12. **平均范围只作用于速度页的平均值/图层**，不会改变其它页的统计（最大速度、最大过载、
    能量积分等仍按整架次算）；范围输入也不会写回日志或持久化，关掉页面即复位为全程。
13. **切换架次时的范围夹取**：范围保留但会夹到新架次时间轴内；若两者完全不相交则自动
    退回新架次全程，并在数值下方用黄字提示（避免"空区间"看起来像程序坏了）。

---

## 11. 许可

与 WtHud 仓库一致：AGPL-3.0-or-later。本工具为独立脚本，不修改产品代码。
