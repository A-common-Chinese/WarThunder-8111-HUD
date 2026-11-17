# WtHud 飞行日志解析 / 可视化工具（flightlog）

独立的小工具：读取 WtHud 录制的 `wtlog-*.jsonl`，把 **能量、速度、高度、过载、姿态** 等
随时间（以及死亡 / 复活 / 换机型事件）的变化画成图，生成**一个自包含的 HTML 文件**
（内联 CSS/JS/SVG，无 External 依赖，离线双击即看）。可选再用 Pillow 出一张 PNG 预览。

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

### 自检

```bat
cd wt-hud\tools\flightlog
python selftest.py
```

纯标准库合成样本自检（解析、坏行跳过、缺字段、事件切分、能量公式、HTML 结构、CLI），
当前 **73/73 项全部通过**。

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

### 顶部

* 文件 / 机型 / WtHud 版本 / schema / 采样模式 / 生成时间；
* 汇总卡片：总时长、架次、最大 IAS、最大 Mach、最大过载、比能量范围；
* 解析提示（坏行、缺字段等，黄色告警条）；
* 架次跳转按钮（全局总览 / 架次 1 / 架次 2 …）。

### 全局总览（多架次时）

所有架次拼在一条时间轴上，速度 / 高度 / 比能量三张缩略图，竖虚线标出架次边界，
事件竖线贯穿全部图表，便于一眼看出"什么时候掉了多少能量"。

### 每个架次一节

| 图 | 内容 |
| --- | --- |
| 速度 | IAS、TAS（左轴 km/h）+ Mach（右轴） |
| 高度 | 气压高度 `alt_m` + 雷达高度 `ralt_m`（虚线，有数据才画） |
| 比能量高度 Es | `Es = alt + v²/2g`（米），带面积填充 |
| 能量变化率 dEs/dt | 绿色=获得能量，红色=损失能量（m/s），正负分色填充 |
| 垂直速度 Vy | m/s |
| 迎角与过载 | AoA（左轴 °）+ Ny（右轴 g） |
| 姿态 | Pitch（左轴 °）+ Bank（右轴 °，有数据才画） |

* **事件**：`death` / `spawn` / `vehicle_change` 以彩色竖虚线 + 标签画在每条时间轴上方
  （事件时间线色条），并贯穿该架次所有图表；死亡后的地面段用浅红色底纹标出；
  `valid=false` 的帧用浅灰底纹标出。
* **悬停**：鼠标在任意图上移动 → 该架次所有图同步显示十字线与数据点，浮窗列出
  该时刻全部数值（IAS/TAS/Mach/高度/Es/dEs/dt/Vy/AoA/Ny/姿态）。触屏按下滑动同样可用。
* **时间轴单位秒**：横轴数字即日志内的 `t`（秒），最后一张图右上角标注 `时间轴 秒 (s)`。

### "自包含"的含义

* 没有 `<script src>`、`<link>`、`<img>`、`@import`，不访问网络；
* 图表是 Python 直接算好的内联 `<svg>`，**浏览器禁用 JS 也能看到全部图**；
  内联 JS 只负责悬停浮窗和架次导航高亮；
* 文件大小：8k 样本的日志约 1.5 MB（大部分是悬停用的全量数据）。

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
├─ parse.py           # JSONL 解析 / 容错 / 架次切分 / 能量 / 统计
├─ report.py          # 自包含 HTML：内联 SVG 图表 + 内联 CSS/JS（悬停、导航）
├─ preview_png.py     # 可选 Pillow PNG 预览（缺库优雅降级）
├─ selftest.py        # 合成样本自检（纯标准库）
├─ README.md          # 本文件
└─ out/               # 生成物（HTML / PNG），默认输出目录
```

数据流：`parse.analyze()` → `LogModel`（meta + flights + series + stats + summary）
→ `report.render_report()`（SVG + JSON 数据）→ 单文件 HTML。

---

## 9. 已验证

* 真实日志 `wtlog-20261117-131039-jh_7a.jsonl`（8356 样本 / 295.4 s / 2 架次 /
  death+spawn 事件）：HTML 约 1.5 MB，能量范围 0–10285 m，最大 IAS 1242 km/h，
  最大过载 13.68 g；无头 Chromium 截图与 CDP 悬停测试通过（无 JS 异常）。
* 旧格式日志 `wtlog-20261117-125220-f_15ex.jsonl`（无事件、无 `vehicle`/姿态、
  含 1 条坏行）：可正常生成报告。
* 纯地面日志（IAS≈0）：正常出报告，并提示"没有空中样本"。
* `python selftest.py`：73/73 通过。

---

## 10. 已知限制 / 后续建议

1. **悬停数据是全量内联**：样本极多（>10 万）时 HTML 会偏大，可按需抽稀或改成分片加载。
2. **dEs/dt 是平滑值**：±1.5 s 窗口会钝化瞬时机动，短于窗口的高频能量尖峰看不出来；
   需要更灵敏可调 `parse.ENERGY_RATE_WINDOW_S`（图与统计会同步变化）。
3. **比能量只算了平动动能 + 位能**，不含燃油/武器质量变化；`tas_kmh` 缺失时退化为位能高度。
4. **事件时间来自 HUD 事件记录**，与游戏内真实死亡时刻可能有 1–2 s 延迟；死亡后的
   样本仍归入该架次（报告里用浅红底纹标出）。
5. **多架次按顺序拼在全局总览里**，架次之间有断档时不画时间间隔（横轴仍按真实 `t`，会留空白）。
6. **姿态角在 `att_conf=0`（Unavailable）时仍可能上报 0**，会被当成真实 0 画出来；
   需要更严格可过滤 `att_conf`。
7. **PNG 预览只用最长架次**，且只有能量/速度/高度三联图；完整交互请看 HTML。
8. 目前是"每次手动跑生成报告"，尚未接入 WtHud UI 的"打开最近日志"按钮——
   如果要集成，建议由 WtHud 调 `python -m flightlog <log> --out <临时 HTML>` 再启动浏览器。

---

## 11. 许可

与 WtHud 仓库一致：AGPL-3.0-or-later。本工具为独立脚本，不修改产品代码。
