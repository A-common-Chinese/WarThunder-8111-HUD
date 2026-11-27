# 舵面 / 配平 / 指令轴 —— 页面交接说明

> 给负责做页面的代理。看完这一份就能开工，不需要再翻代码或重问背景。
> 生成日期 2026-11-27；对应 WtHud `v0.5.10`（未发布，改动还在工作区）。

## 0. 一句话

飞行记录里新增了 7 个操纵相关字段（舵面偏转、配平、指令轴）。现在只有一个实验性的单页预览
（`tools/flightlog/surface_preview.py`），要做的是**把这页做成正式、好看、能交互的版本**。
数据已经落在真实日志里，样例文件见第 8 节。

---

## 1. 数据从哪来（别搞错采集方）

| 环节 | 在谁身上 | 说明 |
|---|---|---|
| 读 8111 | **WtHud.exe** | `/state` 每轮取，`/indicators` 每两轮取一次并复用缓存 |
| 解析字段 | WtHud.exe | `TelemetryParser` → `TelemetrySnapshot` |
| 写 JSONL | WtHud.exe | `FlightRecorder`：有界队列 + 独立写线程，100 行/1 s 落盘 |
| 读 JSONL | FlightLog.exe | `parse.py` 解析；`surface_preview.py` 出预览页 |

**FlightLog 不采集任何数据**，它只是读者。页面只依赖 JSONL，不依赖游戏在线。

---

## 2. 字段表（页面要用的全部字段）

### 2.1 新增的 7 个（`schema` 2 起有）

| JSONL 键 | 中文 | 来源 | 量纲/范围 | 精度 | 语义 |
|---|---|---|---|---|---|
| `aileron_pct` | 副翼舵面 | `/state` `"aileron, %"` | −100…+100 | **1% 整数** | 舵面实际偏转 |
| `elevator_pct` | 升降舵面 | `/state` `"elevator, %"` | −100…+100 | 1% 整数 | 同上 |
| `rudder_pct` | 方向舵面 | `/state` `"rudder, %"` | −100…+100 | 1% 整数 | 同上 |
| `trimmer` | 俯仰配平量 | `/indicators` `trimmer` | −1…+1 | 0.001 级 | 配平设定（见 ★4） |
| `stick_ailerons` | 副翼指令轴 | `/indicators` | −1…+1 | 0.001 级 | 「杆+配平+增稳」后的最终指令 |
| `stick_elevator` | 升降指令轴 | `/indicators` | −1…+1 | 0.001 级 | 同上 |
| `pedals` | 方向指令轴 | `/indicators` | −1…+1 | 0.001 级 | 同上 |

### 2.2 页面同时会用到的基础字段

`t`（秒，日志起点为 0）、`alt_m`、`ias_kmh`、`tas_kmh`、`mach`、`ny_g`、`vy_ms`、
`pitch_deg`、`bank_deg`、`hdg_deg`、`gear_pct`、`flaps_pct`、`airbrake_pct`、`fuel_kg`、
`inflight`（布尔，地面/空中）、`valid`、`vehicle`。

---

## 3. JSONL 格式

一行一条记录，`type` 区分四种：`meta` / `sample` / `event` / `end`（另有 `marker`）。

真实样例（截自 2026-11-27 的 F-16C 试飞）：

```json
{"type":"meta","schema":2,"app":"0.5.10","utc":"2026-11-27T03:49:44.9569751Z","vehicle":"f_16c_block_52_aesa","rate":"per-sample"}
{"type":"sample","t":11.535,"utc":"2026-11-27T03:49:56.4316823Z","valid":true,"inflight":true,"vehicle":"f_16c_block_52_aesa","ias_kmh":64,"tas_kmh":64,"mach":0.05,"alt_m":34,"vy_ms":0,"aoa_deg":1,"aos_deg":-0.2,"ny_g":0.99,"roll_rate_dps":-0,"hdg_deg":267.3,"has_hdg":true,"pitch_deg":1,"bank_deg":0,"att_src":"Estimated","att_conf":1,"gear_pct":100,"flaps_pct":99,"airbrake_pct":0,"aileron_pct":3,"elevator_pct":-1,"rudder_pct":1,"trimmer":0,"stick_ailerons":0.039,"stick_elevator":-0.015,"pedals":0.01,"fuel_kg":3090,"fuel0_kg":6393}
{"type":"end","utc":"2026-11-27T03:57:15.2015579Z"}
```

★ **缺字段规则**：机型不报某个键时，**整条字段被省略**（不是写 0）。解析端得到 `None`/`null`。
页面必须把 `None` 当「没有这个数据」，绝不能画成 0 —— 0 表示「舵面在中立位」，是有效数据。

★ **向后兼容**：`schema` 1 的旧日志没有这 7 个字段，页面要给空态（文字说明），不要画平线。

---

## 4. ★ 五条实测结论（直接决定页面怎么画）

实测样本：F-16C Block 52，地面 + 一圈航线，约 2 万条样本。

1. **指令轴不是原始杆量**：`stick_*` 已含配平与增稳，恒等于 `舵面 ÷ 100`。
   实测比值中位 1.000（副翼/升降）、1.003（方向）。**不要做成「玩家输入 vs 飞机响应」的叙事。**
2. **两条线的关系是「同源、粗细两版」**：舵面 1% 量化，指令轴 0.001 级。页面价值在细腻度，不在对比。
3. **指令轴滞后约 1 个采样（≈24 ms）**：`/indicators` 隔轮取 + 复用缓存。
   互相关最佳对齐 −1 采样（相关 0.996–0.998）。把舵面前移 1 采样后，残差 p90 从 4–6% 降到 2–3%，
   剩下的基本是 1% 量化。★ 差值图上的尖峰**必须**标注为采样滞后，不能解读成操纵差动。
4. **`trimmer` 只在关掉「航电增稳 / 教练模式」时有值**：开着时恒 0。0 = 这局没有配平在生效。
   页面要写清这一点，否则用户会以为字段坏了。
5. **A-10 系列**：分裂副翼（deceleron）让 `airbrake, %` 跟随副翼，该机型的舵面数据需单独提示。

---

## 5. 现有实现（可以从这里改，也可以重写）

文件：`tools/flightlog/surface_preview.py`（约 430 行，纯标准库，无前端依赖）

| 函数 | 职责 |
|---|---|
| `build_preview(log_path, out_path, title, whole_log=False)` | 入口，返回写出的 HTML 路径 |
| `_collect(model)` | 把各架次样本拼成一条时间轴 |
| `_pick_window(rows)` | 挑最长一段连续 `inflight` 数据；地面段会让动作压扁，默认略去 |
| `_thin(values)` | 等距抽稀到 1400 点（首末必留） |
| `_axis_stats()` | 每轴：样本数、范围、相关、最大残差、**对齐 1 采样后 p90**、量化台阶数 |
| `_polyline()` | 值 → 折线；`step=True` 画阶梯（突出 1% 量化），超范围的值夹到边上 |
| `_chart()` | 一张轴图（1000×190 viewBox，绘图区 120 px 高） |
| `_trim_chart()` | 配平图；范围按 2%–98% 分位定，避免开局尖峰压塌全图 |
| `_diff_chart()` | 三轴残差叠一张 |
| `_stat_table()` | 统计表 |
| `_time_axis()` | 共享时间轴（mm:ss） |

命令行：

```bash
FlightLog --surface-preview "<log.jsonl>" --out "<out.html>"          # 默认只画空中段
FlightLog --surface-preview "<log.jsonl>" --out "<out.html>" --surface-preview-all   # 含地面段
```

---

## 6. 页面信息架构（当前版本，供改版参考）

自上而下：

1. **标题** + 一行元信息：机型 / app / schema / 样本数 / 时长 / 平均 Hz / 抽稀点数 / 窗口说明
2. **副翼** 图：舵面（灰带，阶梯） + 指令轴×100（细彩线） + 图例
3. **升降舵** 图（同上）
4. **方向舵** 图（同上）
5. **配平** 图：单线，0 基线；右上角注明全程范围与「按分位显示」
6. **差值** 图：三轴 `舵面 − 指令轴×100` 叠画，横向 0 线
7. **统计表**：轴 / 样本 / 舵面范围 / 指令轴范围 / 相关 / 最大残差 / 对齐后 p90 / 量化台阶
8. **说明块**：怎么读、差值来源、配平前提、非正式报告声明

配色（可沿用）：

```
背景 #f7f5ef   面板 #fffdf8   网格 #ded8c8   正文 #1f2937   次要 #6b7280
舵面带 #111827 @ 0.28 宽 3.2px（阶梯）
指令轴 副翼 #2563eb / 升降 #c2410c / 方向 #047857，宽 1.2px
配平   #b45309
```

可增强（当前没有，纯静态 SVG）：

- 鼠标悬停十字线 + 读数（t、舵面、指令轴、残差）
- 框选缩放、双击复位
- 轴开关（只看某一轴）
- 导出 PNG / 单页 PDF
- 事件标记（`event` 行：death / spawn / kill / gap）

---

## 7. 硬约束与坑（踩过的）

1. ★ **不要顺手改进正式 9 页报告**：那套页码是 10 页制（封面 + 9 页内容），硬编码散在 6 处
   （CSS 注释、B 路径进度 `1/10…10/10`、设置页文案、封面页脚、A 路径总页数、selftest 断言）。
   要并进去必须先拿到明确指令，并一起改这 6 处。当前这个页面刻意独立。
2. ★ **Python 模板里所有裸 `%` 要写成 `%%`**（例如「4–6%」「1% 量化」），否则
   `TypeError: not enough arguments for format string` —— 这个坑连踩三次。
3. **空态优先**：没有字段就写「这份日志没有舵面字段（0.5.10 之前录制）」，不要画 0 线。
4. **默认裁到空中段**：整段 450 s 里空中只有 82 s，全画会把动作压成一条细线。
5. **配平用分位定范围**：开局常有 0.9+ 的尖峰，按极值定比例会毁掉整张图。
6. **抽稀**：一条航线 1.8 万点，直接画会让 SVG 体积和渲染都失控；1400 点左右够看形态。
7. **`.wrpl` 回放（ACMI）没有这些字段**：该来源只能显示空态。

---

## 8. 样例数据与复现

现成的真实日志（F-16C，起飞绕场一圈降落，450 s / 18,149 样本 / 9.1 MB）：

```
wt-hud/src/WarThunderHud/bin/Debug/net8.0-windows/recordings/
    wtlog-20261127-114944-f_16c_block_52_aesa.jsonl
```

已生成的预览页（可直接打开对照）：

```
同目录/舵面预览-20261127-机场一圈.html   （219 KB，单文件，无外部依赖）
```

复现命令：

```bash
cd wt-hud/tools
python -m flightlog --surface-preview "…/wtlog-20261127-114944-f_16c_block_52_aesa.jsonl" \
    --out "…/preview.html"
```

这份日志的实测数字（改版后应能对上，可作为验收基线）：

| 轴 | 样本 | 舵面范围 | 指令轴范围 | 相关 | 最大残差 | 对齐 1 采样后 p90 |
|---|---|---|---|---|---|---|
| 副翼 | 3,479 | −100…+100 | −1.000…1.000 | 0.9827 | 33.90 | 2.80 |
| 升降 | 3,479 | −53…+94 | −0.495…0.927 | 0.9933 | 34.50 | 2.40 |
| 方向 | 3,479 | −99…+93 | −0.992…0.928 | 0.9951 | 38.20 | 2.20 |

配平全程 −0.110…+0.978，非零 3,116 条（地面打到接近满位，起飞后回落到 −0.01 附近）。

---

## 9. 验收口径

- 页面能在不联网、无外部 CDN 的情况下打开（现有实现是纯内联 SVG）
- 缺字段日志 → 空态文字，无 0 线、无 JS 报错
- 上述基线数字与统计表一致
- 改完后自检仍然通过：`tools/flightlog/selftest.py`（当前 **943 PASS**，
  其中 7 条专测预览页）；HUD 侧 `WtHud.exe selftest`（当前 **550 PASS**）

---

## 10. 联系点

- 数据字段/写盘口径：`src/WarThunderHud/Telemetry/{TelemetryParser,FlightRecorder,TelemetrySnapshot}.cs`
- 解析与字段清单：`tools/flightlog/parse.py`（`EXTRA_NUMERIC_FIELDS`）
- 页面生成：`tools/flightlog/surface_preview.py`
- 实测结论与已知限制：`CHANGELOG.md` 的 `v0.5.10` 段
