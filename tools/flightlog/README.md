# WtHud 飞行日志解析 / 可视化工具（flightlog）

独立的小工具：读取 WtHud 录制的 `wtlog-*.jsonl`（或 **War Thunder `.wrpl` 回放**），把
**能量、速度、高度、过载、姿态** 等随时间（以及死亡 / 复活 / 换机型事件）的变化画成图，
生成**一个自包含的 HTML 文件**（内联 CSS/JS/SVG，无 External 依赖，离线双击即看）。

报告是**翻页式**的：第 1 页是概览（汇总卡片 + 比能量缩略图），之后**每个数据主题一页**
（速度 / 高度 / Es / dEs/dt / Vy / 迎角与过载 / 姿态），用 `‹ 上一页` `下一页 ›`、
键盘 `←` `→` / `PageUp` `PageDown`、底部圆点翻页；每页固定铺满视口，**不用长滚动**。
多架次（多次 spawn）时页眉有架次选择器，切换后当前页的曲线换成该架次。
**速度页顶部显示平均 IAS / TAS / Mach**，可以手动输入起止时间、只统计所选区间
（默认整个当前架次），区间在图上用浅色底纹标出、平均值画成水平虚线。
可选再用 Pillow 出一张 PNG 预览。

输入除了 JSONL 还支持 **`.wrpl` 回放**：自动调用 `wrpl-acmi.exe` 转 ACMI → 列出玩家 →
选玩家（CLI 或 Tk 多选）→ 同一个报告（回放是**估算数据**，见第 3 节）。

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

回放只需要把位置参数换成 `.wrpl`（详见第 3 节）：

```bat
cd wt-hud\tools
python -m flightlog "D:\...\Replays\#2026.11.15 15.24.09.wrpl" --list
python -m flightlog "D:\...\Replays\#2026.11.15 15.24.09.wrpl" --player HCN_WT --out out\replay.html
```

### 独立可执行文件（不需要 Python）

`FlightLog.exe` 与 WtHud 完全分离，可以单独启动，**不需要安装 Python**：

- **双击** → 先弹出一个**来源选择小窗**：
  - `War Thunder 回放（.wrpl）` → 文件框直接打开 War Thunder 的 `Replays` 目录
  - `WtHud 飞行记录（.jsonl）` → 文件框直接打开 exe 旁边的 `recordings` 目录
  - 取消则退出
- 选文件后自动生成 HTML 并用默认浏览器打开；选到回放会再弹**玩家多选**对话框
- 也可以把日志/回放文件**拖到 exe 图标上**（跳过选择小窗，按扩展名自动识别）
- 报告写到 **FlightLog.exe 旁边的 `reports\`**（便携，不依赖用户名），文件名带时间戳，不会覆盖旧报告
- 重新打包：双击 `build_exe.bat`（需要本机 Python + `pip install pyinstaller`），产物在
  `flightlog\dist\FlightLog.exe`；脚本会把 `wrpl-acmi.exe` 一起复制到 `dist\`
- 自动化调用可加 `--mode jsonl|wrpl` 跳过启动选择窗（仍需给出文件路径）

### 自检

```bat
cd wt-hud\tools\flightlog
python selftest.py
```

纯标准库合成样本自检（解析、坏行跳过、缺字段、事件切分、能量公式、**分页 HTML 结构**、
**平均速度纯函数与速度页控件结构**、**ACMI 回放解析 / 玩家归组 / 回放模式标记 /
回放边界情况**、自包含检查、CLI、**MFD 导出与平均值窗口规则**），
当前 **338/338 项全部通过**（原 152 项 + 回放/ACMI 114 项 + MFD 72 项）。

---

## 2. 命令行参数

| 参数 | 说明 |
| --- | --- |
| `log`（位置参数） | `wtlog-*.jsonl` 日志、`.wrpl` 回放或 `.acmi` 文件 |
| `--out PATH` | HTML 输出路径；不传则 `out/<文件名>.html` |
| `--summary` | 只打印统计 JSON（若同时给 `--out` 也会写 HTML） |
| `--png PATH` | 额外生成 PNG 预览（Pillow 可选，缺库时跳过并提示） |
| `--title TEXT` | HTML 页面标题 |
| `--gap SEC` | 相邻样本间隔超过多少秒切新架次，默认 `30` |
| `--list` | **（回放）** 只列出玩家，不生成 HTML |
| `--player NAME` | **（回放）** 选玩家，可重复 / 逗号分隔 / 也可用 1 起的序号 |
| `--acmi PATH` | **（回放）** 跳过转换，直接解析已经转好的 ACMI |
| `--wrpl-acmi PATH` | **（回放）** 指定 `wrpl-acmi.exe` 路径（默认自动找） |
| `--keep-acmi N` | **（回放）** `cache\acmi`（exe 旁边）保留最近几个 ACMI，默认 `3`，`0` = 用完即删 |
| `--convert-timeout SEC` | **（回放）** 单次转换超时，默认 `900` |
| `--mfd-json` | **开关**（不带值）：把位置参数的日志导出成 HUD MFD 面板用的 JSON（schema=1）；配合 `--out` 写文件，不给 `--out` 时 stdout 只有纯 JSON（见第 12 节） |

`python -m flightlog --help` 可查看完整帮助。

---

## 3. 导入 War Thunder `.wrpl` 回放

WtHud 自己的 `wtlog-*.jsonl` 是"第一人称遥测"；本工具也能直接吃 **War Thunder 回放**：
先把 `.wrpl` 用第三方工具 `wrpl-acmi.exe` 转成 ACMI（Tacview 文本），再解析出**飞机类对象**，
按玩家归组，让用户选玩家，然后走**同一套翻页式 HTML 报告**。

```bat
:: 1) 列出回放里的玩家（.wrpl 会自动转换成临时 ACMI，18.7 MB 回放约 4 s）
cd wt-hud\tools
python -m flightlog "D:\...\Replays\#2026.11.15 15.24.09.wrpl" --list

:: 2) 给某个玩家出报告（--player 可重复 / 逗号分隔 / 也支持 1 起的序号）
python -m flightlog "xxx.wrpl" --player HCN_WT --out out\replay.html

:: 3) 多个玩家 → 一份报告，页眉有玩家切换按钮
python -m flightlog "xxx.wrpl" --player HCN_WT --player "送塟者"

:: 4) 已经转好 ACMI：跳过转换直接解析（调试 / 复用，秒开）
python -m flightlog "xxx.wrpl" --acmi "cache\acmi\xxx.acmi" --list
```

双击 `FlightLog.exe` 时：文件选择框同时接受 `.jsonl` / `.wrpl` / `.acmi`；选到回放会先转换，
然后弹出 **Tk 多选对话框**（列出 玩家 / 机型 / 阵营 / 对象数 / 样本数 / 时长，
「全选 / 清空 / 取消 / 生成报告」），确认后出报告，取消则直接退出。

### 依赖：`wrpl-acmi.exe`（第三方，AGPL-3.0）

* 来源：<https://github.com/3794432/wrpl-acmi/releases>
  （实测版本 `v3.0.0-20261101175226-9ed43b9f108b`，15,784,448 字节，
  SHA256 `C80D9C5C04531DADC27342788F9800A440A6F258076054DABFB6F9904E71890E`）；
* 放在 `FlightLog.exe` 旁边、或 `tools\flightlog\` 里都行（也认环境变量 `WRPL_ACMI`）；
* 没下载时运行脚本（**GitHub 直连会被本机 Steam++ 的加速规则拦**，脚本自动改走 gh-proxy 镜像）：

```powershell
cd wt-hud\tools\flightlog
powershell -ExecutionPolicy Bypass -File .\fetch_wrpl_acmi.ps1
# 可选：-Dest <目录> / -Force / -CheckOnly / -Url <自定义地址>
```

* 重新打包 `FlightLog.exe` 时 `build_exe.bat` 会把它一起复制到 `dist\`。

### 流程与临时文件

1. **转换**：`wrpl-acmi.exe -in x.wrpl -out x.acmi`。默认参数：
   `-events=none -radar-locks=false -radar-modes=false -countermeasures=false
   -missile-seeker=false -bookmarks=false -airfields=false -estimate-bomb-trajectories=false`；
   **必须保留** `-estimate-telemetry`（默认开），否则没有 IAS/TAS/Mach/AOA/G。
   注意：**不能**加 `-ground-units=false`——实测某些回放会连飞机位置一起丢掉。
2. **缓存目录**：`cache\acmi\`（FlightLog.exe 旁边；不可写时才退回 `%TEMP%\WtHud\flightlog`），按「回放路径 + 大小 + mtime + 参数版本」缓存，
   同一个回放换玩家看不必重复转换；默认**保留最近 3 个**（`--keep-acmi N`，`0` = 用完即删）。
   一个 18.7 MB 回放转出来约 **107 MB**，注意 tmp 磁盘占用。
3. **扫描**：流式读 ACMI，收集头部、对象声明（`Name/CallSign/Pilot/Type/Coalition`）、
   每个对象的出现时间与更新时间数。
4. **玩家归组**：只保留 `Type` 按 `+` 切词后含 `Air` 的对象（防空车 `AntiAircraft` 不算），
   按 `Pilot` 归组；没有 `Pilot` 的 AI/无人对象按对象单独列出，名字形如 `A-10A #33`。
5. **选择**：CLI `--player`（可多次/逗号/序号），或双击时的 Tk 多选；非交互环境不给
   `--player` 会**列出玩家并报错**（退出码 2），不会瞎猜。
6. **抽取**：第二遍只保留所选对象的样本（147 MB 的 ACMI 也不整体进内存）。
7. **出报告**：一个 ACMI 飞机对象 = 一个架次（玩家死亡重生会换对象 id），
   映射成与 JSONL 完全相同的报告模型。

### 回放报告和 JSONL 报告的区别

* 标题是「回放分析」，页眉固定一条橙色声明：
  **回放估算数据（IAS = EAS 近似，非座舱遥测）**；
* 概览页多一条回放说明条（源文件 / 场景 / 样本数 / 时长）+ 玩家摘要条；
* 多玩家时页眉有**玩家切换**按钮（点一下跳到该玩家第一个架次），此时不生成第 9 页
  "全局总览"（不同玩家时间轴重叠，拼在一起没意义），共 **8 页**；
* 回放里没有 HUD 的 spawn/death 事件，事件条为空；架次按"一个 ACMI 飞机对象"切分；
* 概览卡片的「事件数」换成「玩家」。

### 回放字段映射（重要：都是估算值）

| 报告字段 | ACMI 来源 | 说明 |
| --- | --- | --- |
| `t` | 帧头 `#<秒>` | 回放内相对秒（原生时间轴，不从 0 重排） |
| `ias_kmh` | `IAS` × 3.6 | ACMI 里是 m/s；导出器注明 **IAS = EAS 近似** |
| `tas_kmh` | `TAS` × 3.6 | m/s；零风 + ISA 标准大气估算 |
| `mach` | `Mach` | 导出器按当地声速反算 |
| `alt_m` | `T` 第 3 分量 | 游戏高度（米，相对 `ReferenceAltitude`） |
| `vy_ms` | 高度中心差分 | ACMI 里没有，本工具自己算（跨缺口/跳变留空） |
| `aoa_deg` | `AOA` | 体轴估计值 |
| `ny_g` | `VerticalGForce` | 体轴过载估计值（含重力补偿） |
| `pitch_deg` / `bank_deg` | `T` 第 5 / 第 4 分量 | Tacview 欧拉角 roll/pitch/yaw |
| `hdg_deg` | `HDG` | 度 |
| `es` / `des` | 同 JSONL | `Es = alt + (TAS/3.6)²/2g`，`dEs/dt` 用同一条能量导数管线 |

* 速度/过载/AOA 会出现**单帧跳变伪影**（反演估计的锅）：本工具把"相对相邻帧中位数跳得离谱"
  的孤立值置空，并在报告「解析提示」里写明剔除了多少个；持续多帧的估计偏差无法区分真假，予以保留。
* 回放样本一律按 `inflight=true` 处理（ACMI 没给起落架/停机状态），停机坪滑行也会算进"架次"。
* **采样率约 2–4 Hz**（以回放实际导出为准），比 HUD 自己的 10–20 Hz 稀疏，尖峰会被平滑掉。

---

## 4. 输出：HTML 报告里有什么

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
>
> 回放模式：概览卡片里的「事件数」换成「玩家」，概览页多一条回放声明与玩家摘要；
> **多玩家**时不生成第 9 页（共 8 页），页眉多一个玩家切换器。

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

## 5. 日志格式（输入）

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

## 6. 架次（flight）切分规则

一个架次 ≈ 一次"出航"：

1. `spawn` / `vehicle_change` 事件开启新架次（事件时间 ≤ 当前样本时间即生效）；
2. `death` 事件只做标记，之后的样本（坠机/地面状态）仍计入该架次，直到下一次
   `spawn` / `vehicle_change` 或文件结束；
3. 相邻样本间隔 > `--gap`（默认 30 s）→ 切新架次（原因 `gap`）；
4. 样本里的 `vehicle` 字段发生变化（没有事件的老日志）→ 切新架次（`vehicle_field`）；
5. 文件第一段没有 spawn 时，架次原因为 `log_start`。

每个架次的"原因"会显示在报告标题旁（`日志起点` / `复活生成` / `换机型` / `时间跳变`）。

回放（`.wrpl`）没有事件，切分规则是：**一个 ACMI 飞机对象 = 一个架次**（玩家死亡重生会
拿到新的对象 id），同一对象内再按 `--gap`（默认 30 s）补切；架次原因记为 `复活生成`。

---

## 7. 能量定义与导数处理

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

## 8. `--summary` 统计摘要

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

## 9. 代码结构

```
wt-hud/tools/flightlog/
├─ __init__.py        # 包导出
├─ __main__.py        # CLI（python -m flightlog），JSONL / .wrpl / .acmi 都走这里
├─ analyze.py         # 任意 cwd 可用的便捷入口（内部转调 __main__）
├─ parse.py           # JSONL 解析 / 容错 / 架次切分 / 能量 / 统计 / 平均速度纯函数
├─ acmi.py            # ACMI（Tacview 文本）解析：对象/玩家归组/样本抽取/回放模型
├─ replay.py          # .wrpl 导入：找 wrpl-acmi.exe、转换、临时缓存、ReplaySession
├─ picker.py          # 玩家表格 / 命令行选择 / Tk 多选对话框
├─ mfd.py             # MFD 面板 JSON 导出（--mfd-json，schema=1；降采样 + 自动去噪平均值）
├─ report.py          # 自包含翻页 HTML：每页一张内联 SVG + 内联 CSS/JS
│                     # （翻页、架次、玩家切换、悬停、速度页平均速度与手动范围）
├─ preview_png.py     # 可选 Pillow PNG 预览（缺库优雅降级）
├─ launch.py          # FlightLog.exe 启动器（双击选文件 / 回放弹玩家多选）
├─ selftest.py        # 合成样本自检（纯标准库）
├─ build_exe.bat      # PyInstaller 打包（并把 wrpl-acmi.exe 复制到 dist\）
├─ fetch_wrpl_acmi.ps1# 下载 wrpl-acmi.exe（GitHub 直连不通时走 gh-proxy 镜像）
├─ wrpl-acmi.exe      # 第三方转换器（AGPL-3.0，可用上面的 ps1 重新下载）
├─ README.md          # 本文件
└─ out/               # 生成物（HTML / PNG / 截图 / mfd 样例），默认输出目录
   └─ mfd/            # --mfd-json 真实日志样例（sample-*.json，每份 <= 300 KB）
```

两条数据流：

* JSONL：`parse.analyze()` → `LogModel`（meta + flights + series + stats + summary）；
* 回放：`.wrpl` → `replay.open_replay()`（转换 + `acmi.scan()`）→ 玩家选择
  → `acmi.build_model()`（抽取 + 映射）→ 同样得到 `LogModel`。

之后统一进入 `report._build_payload()`（按页组装：每架次每主题一张 SVG + 悬停几何）
→ `report.render_report()`（页面 + CSS + JS + JSON 数据）→ 单文件 HTML。

---

## 10. 已验证

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

回放（`.wrpl`，全部真实跑过，2026-11-17）：

* **真实空战/直升机回放** `#2026.11.15 15.24.09.wrpl`（18.7 MB）：
  转换 **3.97 s** → ACMI **107.2 MB** → 扫描 0.63 s → 抽取 0.54 s；
  共 49631 帧、1270 个对象、**80 架飞机**，玩家列表 **56 条**（真人 8 名 +
  48 个无飞行员的 A-10A AI 对象），含中文名（送塟者、Furry_坦鹿_TanLu）与俄文名
  （KМС по ПМС）；
* 选 `送塟者`（Mi-28NM）→ `out/wrpl-air-送塟者.html`（**2.68 MB**、9 页、6 架次、
  8513 样本），页眉写明"回放估算数据（IAS = EAS 近似，非座舱遥测）"，
  无头 Edge 逐页截图 **0 溢出 / 0 JS 异常**；截图 `out/wrpl-overview.png`（概览）、
  `out/wrpl-speed.png`（速度页，平均 187.6 / 192.6 / 0.16），拼图 `out/preview-wrpl.png`；
* **多玩家**（HCN_WT + 送塟者 + MrWDD131）→ `out/wrpl-air-3players.html`（7.3 MB、
  **8 页**（无全局总览）、15 架次、25832 样本）；用 CDP 真实点击页眉玩家按钮验证
  **14/14 项通过**：切换后高亮/架次视图/下拉框同步、切回、翻页后保持所选架次、
  速度页平均值正常、无横向溢出、0 JS 异常；
* **打包验证**：`build_exe.bat` 重新打包出 `dist\FlightLog.exe` 并把
  `wrpl-acmi.exe` 复制到 `dist\`；直接运行 `dist\FlightLog.exe "回放.wrpl" --player 1`
  （3.3 MB 地面局回放，真实玩家 ic1257 / MiG-29）**3.04 s** 出报告（489 KB），
  转换器在 exe 旁边被正确找到；
* **边界实跑**：不存在的回放 / 文本冒充的 `.wrpl` / 空回放（wrpl-acmi 报
  `no tracks found`）/ 纯地面 ACMI（无飞机）/ 玩家 0 样本 / 非交互不给 `--player`
  → 全部给出明确提示、退出码 2、不崩；
* `fetch_wrpl_acmi.ps1` 实跑：GitHub 直连超时 → 自动改走 gh-proxy 镜像下载成功，
  SHA256 与脚本内置期望值一致；
* **MFD 导出（`--mfd-json`）**：204002 / 131039 / 211515（4 架次）三份真实日志实测
  analyze+导出 **0.42–1.20 s**、JSON **39.5–121.8 KB**；输出样例放在 `out/mfd/`，
  已逐份校验 schema/字段集合/序列等长/≤900 点/`null`/`spike_window_s`/每份 ≤ 300 KB；
  重新打包的 `dist\FlightLog.exe --mfd-json <log> --out <tmp>` 实跑退出码 0、
  文件与 Python 输出一致（stdout 为空，约 2–3 s 含 onefile 自解压）；
* `python selftest.py`：**338/338 通过**（原 266 项 + MFD 导出 72 项）。

预览图：

* `out/preview.png` — 概览页 + 比能量页的纵向拼图（Pillow 生成），可直接查看翻页效果。
* `out/preview-avg.png` — **速度页平均速度**两张截图拼图：默认全程 vs 手动 `120–180 s`。
* `out/preview-wrpl.png` — **回放报告**概览页 + 速度页拼图（`out/wrpl-overview.png`、
  `out/wrpl-speed.png`）。
* `out/avg-default.png` / `out/avg-range.png` — 上述两张单图（1600×900）。
* `out/wrpl-air-送塟者.html` — 单玩家回放报告；`out/wrpl-air-3players.html` — 三玩家回放报告。
* `out/page-01.png` … `out/page-09.png` — 逐页无头截图（1600×900）。

---

## 11. 已知限制 / 后续建议

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

回放（`.wrpl`）相关：

14. **回放数据是估算值，不是座舱遥测**：`IAS` 是导出器的 EAS 近似，`TAS/姿态/过载/AOA`
    全部由回放位置反演（零风、ISA 标准大气）。绝对数值只能横向自比，不能当作真实仪表读数。
15. **ACMI 体积大**：18.7 MB 回放转出约 107 MB ACMI（导弹轨迹占大头，`wrpl-acmi` 没有
    "只导飞机"的开关），转换约 4 s、解析约 1–3 s；临时文件在 `%TEMP%\WtHud\flightlog`，
    默认保留 3 个（`--keep-acmi` 可调，`0` = 用完即删），注意磁盘占用。
16. **采样率只有 2–4 Hz**（HUD 自己是 10–20 Hz），短时尖峰会被抹平；`vy` 由高度中心差分
    得到，比 HUD 原生的 `vy_ms` 更糙。
17. **单帧跳变伪影**：反演估计偶尔出现一两帧物理上不可能的值（实测 AH-1W 蹦到 1400 km/h、
    68 g）；本工具会按相邻帧中位数把孤立跳变置空并在报告里注明数量，但**持续多帧**的估计
    偏差无法区分真假，予以保留（所以卡片上的"最大 IAS/最大过载"可能仍偏高）。
18. **回放没有事件**：击杀/复活/被击落都不在 ACMI 里，事件条为空，架次 = 一个 ACMI 飞机
    对象；被击落前后若对象仍在更新，那一段会留在同一架次里。
19. **一个 ACMI 玩家 ≠ 一个 War Thunder 玩家**：导出器按 `Pilot` 归组，观战者/无飞行员
    的 AI 对象会单独列成 `机型 #id`；同一玩家多架飞机时按对象 id 分组（而不是按真实出击编号）。
20. **多玩家合并成一份报告**时 HTML 体积随样本线性增长（每个玩家 8 页各一套图表），
    3 个玩家约 9 MB；人再多建议一人一份报告（分别 `--player` 生成）。

---

## 12. MFD 数据导出（`--mfd-json`）

WtHud 内置 MFD 面板要画"**左侧折线图 + 右侧统计值**"（最大速度、平均速度、最大 G 等）。
数据由本工具离线算好：这个接口**不生成 HTML/SVG**，只把一份日志压成 `schema=1` 的 JSON，
HUD 调一次 exe、读文件即可，不需要 Tk / 浏览器 / 联网。

### 12.1 用法

`--mfd-json` 是**开关**（不带路径值），输入仍然由**位置参数**给出：

```bat
:: 写文件：stdout 保持干净（提示只走 stderr）
FlightLog.exe --mfd-json "wtlog-20261117-204002-jh_7a.jsonl" --out "%TEMP%\mfd.json"

:: 不给 --out：stdout 只有纯 JSON（可以直接被父进程管道读走）
FlightLog.exe --mfd-json "wtlog-20261117-204002-jh_7a.jsonl"
```

`python -m flightlog` 用法完全相同（在 `wt-hud\tools` 目录下运行）：

```bat
cd wt-hud\tools
python -m flightlog --mfd-json "recordings\wtlog-20261117-131039-jh_7a.jsonl" --out out\mfd\sample-131039.json
```

* 不带 `--mfd-json` 时**行为完全不变**（HTML 报告 / `--summary` / 回放流程都不受影响）；
* `--out` 在 MFD 模式下**只写 JSON**，不会把日志混进文件；提示信息只走 stderr；
* 支持 `.jsonl` 日志；`.acmi` 走同一套回放解析（需含飞机对象）；`.wrpl` 回放会明确报错
  （提示先转 ACMI 或直接用 HUD 自己的日志）；
* `--summary` / `--png` / `--list` 与 `--mfd-json` 互斥（明确报错，不猜用户意图）；
* 缺位置参数、坏文件、写文件失败都返回**退出码 2**，不会崩栈。

### 12.2 JSON 结构（schema = 1）

字段只增不减；单位写进字段名；**缺失值一律 `null`**（不是 0，也不省略列）；同一架次的所有
序列数组长度都等于 `series.t` 的长度。HUD 只认 `schema == 1`，不兼容时按"版本不符"报错。

真实样例（`out/mfd/sample-204002.json` 裁剪）：

```json
{
  "schema": 1,
  "tool": "FlightLog",
  "tool_version": "0.4.40",
  "source": "wtlog-20261117-204002-jh_7a.jsonl",
  "generated_utc": "2026-11-18T12:33:57Z",
  "meta": {"vehicle": "jh_7a", "duration_s": 390.7, "rate_hz": 29.6, "samples": 11583},
  "flights": [
    {
      "index": 1,
      "start_s": 6.805,
      "end_s": 395.752,
      "samples": 11533,
      "vehicle": "jh_7a",
      "stats": {
        "max_ias_kmh": 1215.0, "max_tas_kmh": 1280.0, "max_mach": 1.07,
        "max_g": 13.79, "max_alt_m": 3968.0,
        "avg_ias_kmh": 1007.8, "avg_tas_kmh": 1120.2, "avg_mach": 0.929, "avg_alt_m": 1987.1,
        "es_range_m": [0.0, 9873.7], "es_delta_m": -5377.9,
        "max_ias_filtered_kmh": 1215.0, "max_tas_filtered_kmh": 1280.0,
        "avg_filter": {
          "death_window_s": 10.0, "spike_window_s": 30.0, "spike_sigma": 3.0,
          "excluded_samples": 215,
          "death_excluded_samples": 215, "spike_excluded_samples": 0,
          "kept_samples": 11318, "fallback": false
        }
      },
      "series": {
        "t": [6.805, 7.245, "..."], "ias_kmh": [742.0, "..."], "tas_kmh": ["..."],
        "mach": ["..."], "alt_m": ["..."], "ralt_m": ["..."], "es_m": ["..."],
        "des_dt_ms": ["..."], "vy_ms": ["..."], "aoa_deg": ["..."],
        "ny_g": ["..."], "pitch_deg": ["..."], "bank_deg": ["..."]
      }
    }
  ],
  "events": [{"kind": "death", "t": 395.398, "vehicle": null}]
}
```

顶层字段：

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `schema` | int | 固定 `1`；HUD 用它判兼容 |
| `tool` | string | 固定 `"FlightLog"` |
| `tool_version` | string | 与 WtHud 发布版本对齐（当前 `0.4.40`） |
| `source` | string | 输入文件名（不含目录） |
| `generated_utc` | string | 生成时间，`YYYY-MM-DDTHH:MM:SSZ` |
| `meta` | object | 整份记录概要（下表） |
| `flights` | array | 架次数组，按时间顺序；每架次自带独立 `series` |
| `events` | array | `[{kind, t, vehicle}]`，按 `t` 升序；`end` 事件不导出 |

`meta`：`vehicle`（机型）、`duration_s`（整份日志首尾秒数）、`rate_hz`（平均采样率）、
`samples`（有效样本总数）。

`flights[i]`：`index`（**1 起**）、`start_s` / `end_s`（日志内绝对秒）、`samples`（原始样本数）、
`vehicle`、`stats`、`series`。

`series`（每架次独立降采样：约 2 Hz、单架次 **≤ 900 点**，多架次切换直接换 `flights[i].series`）：

| 字段 | 单位 | 说明 |
| --- | --- | --- |
| `t` | s | 日志内绝对秒（降采样后） |
| `ias_kmh` / `tas_kmh` | km/h | 指示空速 / 真空速 |
| `mach` | — | 马赫数 |
| `alt_m` / `ralt_m` | m | 气压高度 / 无线电高度 |
| `es_m` | m | 比能量 `Es = alt + (TAS/3.6)²/(2g)` |
| `des_dt_ms` | m/s | 比能量变化率 `dEs/dt`（±1.5 s 平滑，状态跳变处置 `null`） |
| `vy_ms` | m/s | 垂直速度 |
| `aoa_deg` | ° | 迎角 |
| `ny_g` | g | 法向过载 |
| `pitch_deg` / `bank_deg` | ° | 俯仰 / 滚转 |

`stats`：

| 字段 | 说明 |
| --- | --- |
| `max_ias_kmh` / `max_tas_kmh` / `max_mach` / `max_g` / `max_alt_m` | **原始最大值**（整架次，与 HTML 报告一致，不过滤） |
| `avg_ias_kmh` / `avg_tas_kmh` / `avg_mach` / `avg_alt_m` | **自动去噪后**的平均值（规则见 12.3） |
| `max_ias_filtered_kmh` / `max_tas_filtered_kmh` | 去噪后的最大值（"去掉死亡俯冲/尖峰"后的极值） |
| `es_range_m` | `[min, max]` 比能量范围 |
| `es_delta_m` | 空中样本首尾比能量差（没有空中样本时退回全样本首尾差） |
| `avg_filter` | 过滤参数与排除样本数（下表） |

`avg_filter`（HUD 至少读前 4 个字段，其余是给调试/自检的细分，可忽略）：

| 字段 | 说明 |
| --- | --- |
| `death_window_s` | 死亡排除窗口长度（固定 `10.0`） |
| `spike_window_s` | MAD 尖峰过滤生效窗口（固定 `30.0`，即 `[death−30, death]`） |
| `spike_sigma` | MAD 尖峰阈值倍数（固定 `3.0`） |
| `excluded_samples` | 被排除样本总数 = 死亡窗口 ∪ 尖峰（并集，不重复计数） |
| `death_excluded_samples` | 其中死亡窗口排除数 |
| `spike_excluded_samples` | 其中 MAD 尖峰排除数（只可能来自死亡前窗口内） |
| `kept_samples` | 参与平均的样本数 |
| `fallback` | 是否因窗口内剩余 < 30 样本而退回"只排除死亡窗口" |

`events` 每项：`kind`（`spawn` / `death` / `vehicle_change` / `match_start` …，与日志事件同名）、
`t`（秒）、`vehicle`（可能为 `null`）。

### 12.3 平均值规则（不可手动选范围）

MFD 面板不给用户拖范围，平均值由 FlightLog 固定规则自动算。用户口径是「**只去除死亡之前的
超大波动**来平均」，所以 MAD 只在死亡前窗口内生效，窗口外（起飞 / 巡航 / 盘旋）不做任何
离群过滤 —— 平均值因此**接近原始算术均值**：

1. **死亡窗口**：排除每次 `death` 事件前 **10 秒**内的样本（闭区间 `[death−10, death]`），
   这部分不参与任何平均；
2. **MAD 尖峰（只在死亡前窗口内）**：检测范围 = `[death−30, death]` 与"未被死亡窗口排除"的
   交集（即 `[death−30, death−10)`）。在此范围内对 IAS / TAS **各自**过滤
   `|x − median| > 3 × 1.4826 × MAD` 的样本（`median` / `MAD` 只用该范围内的样本算；
   系数 1.4826 把 MAD 换算成正态 σ）。**窗口外一律不做过离群过滤**；
3. **没有 `death` 事件 → 不做 MAD 过滤**：平均值就是原始样本的算术平均（没有死亡窗口可排除）；
4. **样本太少退让**：某通道过滤后窗口内剩余 < **30** 个样本时，该通道退回"只排除死亡窗口"
   （`avg_filter.fallback = true`，不会把架次平均变成一两个样本的噪声）；
5. Mach / 高度等其它统计用 IAS/TAS 尖峰集合的**并集**掩码；
6. `avg_filter.excluded_samples` 记录"死亡窗口 + 尖峰"的并集样本数，方便 HUD 显示口径。

也就是说：**正常飞行段一个样本都不动，只有死亡前那 30 秒里的超大波动会被剔除**（其中死亡前
10 秒本来就整段排除）。

### 12.4 真实样例与性能

三份真实日志（`C:\Users\noapi\Desktop\WtHud-发布\recordings\`）实测（analyze + 导出，不含
Python/PyInstaller 启动）：

| 日志 | 样本 | 架次 | 耗时 | JSON | 样例文件 |
| --- | --- | --- | --- | --- | --- |
| `wtlog-20261117-204002-jh_7a.jsonl` | 11583 | 2 | 0.56 s | 53.6 KB | `out/mfd/sample-204002.json` |
| `wtlog-20261117-131039-jh_7a.jsonl` | 8356 | 2 | 0.42 s | 39.5 KB | `out/mfd/sample-131039.json` |
| `wtlog-20261117-211515-jh_7a.jsonl`（多架次） | 25084 | 4 | 1.20 s | 121.8 KB | `out/mfd/sample-multiflight.json` |

三份样例都 ≤ 300 KB，可直接给 HUD 侧做预览 / 自检。抽样数值（不含地面/机库短架次）：

* 204002 架次 1：`avg_ias=1007.8`（**原始样本均值 994.0**，差异只来自排除死亡前 10 s 的
  215 个低速样本；MAD 在死亡前窗口内没有检出尖峰）、`max_ias=1215.0`、`max_g=13.79`；
* 131039 架次 1：`avg_ias=1051.7`（**原始均值 1051.5**，几乎一致）、`max_ias=1242.0`、`max_g=13.68`；
* 211515 架次 1/3/4：`avg_ias=1059.6 / 1001.1 / 1009.7`，对应原始均值 `1058.4 / 999.7 / 1008.9`
  （窗口外不过滤，所以平均基本等于原始均值）；
* 每个记录末尾的地面/机库尾巴会单独成为很短的一架次（如 50 样本 → 4 个点），HUD 可按
  `samples` / 点数决定是否跳过。

### 12.5 已知限制（MFD 导出）

1. **曲线是 2 Hz 降采样**：短于约 0.5 s 的尖峰不会出现在 `series` 里（`stats` 仍用全量样本算，
   所以"最大 G"可能是曲线上看不到的瞬时值）。
2. **900 点上限优先于 1–2 Hz**：超过约 450 s 的单架次会自动加粗步长（可能低于 1 Hz），
   保证 MFD 小屏不卡；多架次日志每个架次单独降采样，互不影响。
3. **MAD 只在死亡前 30 s 窗口内生效**：窗口内若正好有持续低速段（例如死亡前正在降落/盘旋），
   它离中位数很远时仍可能被判成"超大波动"剔除；窗口外（起飞/巡航等）**完全不滤**，所以
   `avg_ias` 接近原始算术均值，两者差异主要来自"排除死亡前 10 s 样本"
   （例：204002 架次 1 原始均值 994.0 → 去噪后 1007.8，差 13.8 全部来自死亡窗口的 215 个低速样本）。
4. **没有 `death` 事件就没有任何过滤**：窗口内检测范围为空 → 不做 MAD，平均值 = 原始样本均值
   （例：131039 架次 1 原始 1051.5 → 1051.7，只因死亡窗口排除 212 个样本）。
   另外事件时间本身有 1–2 s 延迟（见第 11 节），窗口位置会随之整体偏移。
5. **`meta.rate_hz` / `duration_s` 用整份日志首尾算**：多架次之间的空档会被计入时长，
   所以 `rate_hz` 是整场平均采样率（如 211515：26.8 Hz，单架次内约 27 Hz），不是"每架次采样率"。
6. **`.wrpl` 不支持**：MFD 只吃 HUD 自己录的 `.jsonl`（或已转好的 `.acmi`），不直接开回放。
7. **HUD 调的是 `dist\FlightLog.exe`**：本分支已重新打包 `dist\FlightLog.exe`（2026-11-18，
   30.6 MB，PyInstaller onefile+noconsole）；改源码/换机器后要重新执行 `build_exe.bat`。
   实测 `FlightLog.exe --mfd-json "<log>" --out "<tmp>"` 退出码 0、stdout 为空、耗时约 0.5–0.7 s
   （另有约 2–3 s 的 onefile 自解压冷启动，仍远低于 HUD 的 15 s 超时）。
   HUD 侧调用约定见 `src/WarThunderHud/UI/Mfd/FlightLogBridge.cs`
   （`--mfd-json <log> --out <tmp>`，`schema != 1` 视为版本不兼容）。

---

## 13. 许可

与 WtHud 仓库一致：AGPL-3.0-or-later。本工具为独立脚本，不修改产品代码。
