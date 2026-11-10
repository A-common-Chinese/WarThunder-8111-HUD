# War Thunder HUD (8111)

**简体中文** | [English](#english)

用官方 `localhost:8111` 接口驱动的**战争雷霆平视显示器（HUD）叠加层**，反作弊安全：**不注入、不读内存、不改文件、不拦截网络、不自动操作**。

A military-style HUD overlay for **War Thunder**, driven entirely by the official local API on
`http://127.0.0.1:8111`. Read-only: no injection, no memory reads, no file modification,
no packet interception, no automation.

当前版本 **v0.3.7** · [更新日志](CHANGELOG.md) · [AGPL-3.0](LICENSE)

---

## 效果截图

第三人称（HUD 全要素）：

![第三人称 HUD](docs/screenshots/hud-01-third-person.jpg)

近距离视角：

![近距离视角](docs/screenshots/hud-02-close-view.jpg)

全部要素示意（模拟渲染，非游戏截图）：

![HUD 全部要素](docs/screenshots/hud-all-elements.jpg)

## 特性

### MFD 设置面板（F1）

- 机框与 25 个实体按键为 Blender 建模渲染，按键与屏幕 10 行一一对应：**侧键 = 选择，CONFIRM = 执行**
- 主菜单 → 模块页（显示 / 视角与姿态 / 告警 / 按键 / 面板 / 语言 / 机型 / 诊断 / 回归快照）→
  二级页（数值输入 / 键位捕获 / 文本输入 / 二次确认）
- 数值可直接键入；键位**按住组合键实时显示**（Ctrl → `CTRL`，再按 Q → `CTRL+Q`）
- 外板 / 屏幕 / 告警可分别设中英文；告警框宽度按当前语言的文本实测宽度自动重算
- 诊断页显示 `/state`、`/indicators`、`/map_obj`、`/map_info` 的实时延迟与状态，并可检查 GitHub 最新版本
- F1 可开关面板；面板按屏幕尺寸自适应缩放；机框轮廓做了窗口裁剪，无黑底


### 姿态与基准符号

- **俯仰梯 + 地平线 + 坡度尺**（第三人称相机不滚转，梯子保持水平；相机俯仰耦合已补偿）
- **坡度指针**：沿圆弧指示坡度，指针内显示**滚转角度数**与 `+`（机腹朝下）/ `-`（机腹朝上）
- **飞行矢量标记（FPM）**：实时显示航迹方向，可微调偏置

### 两侧数据带

| 位置 | 内容 |
|---|---|
| 左侧（速度带） | IAS 数字框、`M` 马赫数、`ACC` 纵向加速度 + 趋势条 |
| 右侧（高度带） | 气压高度（`H`）、`V/S` 升降率 + 趋势条 |
| 顶部 | 航向带，读数随航向滚动 |

- 两条趋势条**零点对齐屏幕中心线**，起点各有一段短横线基线，量程：爬升率 0–100 m/s → 5°，加速度 0–10 m/s² → 5°
- 走带刻度每 50 km/h / 100 m 一格，主刻度标数值

### 左下数值区

| 行 | 内容 | 颜色规则 |
|---|---|---|
| `G` | 法向过载 | ≥9 黄，≥12 红；≤−2 黄，≤−4 红 |
| `AOA` | 迎角 | 绿 |
| `FUEL` | **剩余续航时间**（`mm:ss`） | <5 min 黄，<2 min 红；无油耗数据时 `--:--` |
| `ALT` | 气压高度 | 绿 |
| `GRID` | 地图网格坐标（如 F2） | 绿 |

> `FUEL` 是"按当前油耗还能飞多久"，用 12 秒滑动窗口最小二乘估计油耗，可在设置里用标定系数对齐游戏内显示。

### 构型提示（中偏左）

| 提示 | 运动中 | 到位后 |
|---|---|---|
| 起落架 | 琥珀**闪烁** `GEAR DOWN` / `GEAR UP` | 绿色常亮 `STB` 0.8 秒 |
| 襟翼 | 琥珀**闪烁** `FLAPS DOWN` / `FLAPS UP` | 常亮 `FLAPS <档位>`（收回 0 后消失） |
| 减速板 | 琥珀**闪烁** `AIRBRAKE OUT` / `AIRBRAKE IN` | 常亮 `AIRBRAKE`（提示仍处于展开状态） |

三者共用同一列、随 `Z` 瞄准布局一起下沉。

### 击杀播报（SPLASH）

- 自己击落**玩家**时，屏幕中心上方弹出**实心绿框**：告警语言中文显示「击落」，英文显示 `SPLASH`；
  框下面用无框绿字显示被击落目标名；**无动画**，1.2 秒后直接消失（连续击杀排队）
- 数据来自 8111 `/hudmsg`（4 Hz 增量轮询、按 id 去重、解析失败完全静默）
- **只认「击落了」**：AI / 地面目标的「击毁了」、自己被击落、其它玩家的击杀都不播报
- 用法：`F1` → 显示页第 2 页 → `玩家昵称`（与游戏内一致；ASCII 直接打字、中文用 `Ctrl+V` 粘贴）→ 打开 `击落播报`

### 按机型参数（作用域）

- 10 项参数支持**按机型单独覆盖**：俯仰零点 / 俯仰符号 / 坡度修正 / 矢量修正 / 失速迎角 / 迎角告警 /
  极速 IAS / 极速 Mach / 下降率 / PULL UP 提前量
- 生效顺序：**机型覆盖 > 机型档案（`airframes.json`）> 全局配置**
- 编辑页顶部显示**写入层**（机型 / 全局，未识别机型时琥珀色提示）；机型详情页显示数值**来源**（机型 / 档案 / 全局）

### 告警（原有）

- **超速**：IAS 或 Mach **双路同时判断**（任一超限即告警），阈值可调；速度数字变红 + 闪烁 `REDUCE SPEED`
- **过载**：见上表 G 值配色
- **油量**：续航低于 2 分钟变红

### 导航与告警

- **导航**：`GRID` 网格坐标；最近友方 / 敌方机场的相对方位与距离（`AF ↗ 4.2km`）；距地图边缘过近时闪烁红色 `RETURN ↑`；
  距**敌方机场**小于设定距离（默认 12 km，可调）时 `AF!` 变为闪烁框
- **地形告警**：`SINK RATE`（低空 + 大下降率，阈值按机型/全局可调）；有机型雷达高度表时升级为 `PULL UP`
  （`PULL UP 提前量` 可配置，默认 5 s：距撞地时间小于该值即告警，按机型覆盖）
- **超速**：速度数字变红 + 左右各一个闪烁红框 `REDUCE SPEED`；**IAS / Mach 双路同时判断**（任一超限即告警），
  机型有 `vne` 时自动用 vne
- **失速**：闪烁 `STALL`（琥珀=预失速，红=失速）。判定优先用**按机型校准值**（飞行中按 `Ctrl+Alt+S` 记录临界迎角），
  其次用座舱迎角指示灯（200 ms 防抖 + 0.85 回差，避免拉杆瞬间误闪），最后用通用阈值；且要求机翼卸载（Ny < 2.5）才告警
- **A-10 家族减速板**：A-10 使用副翼分裂式（deceleron）减速板，遥测值随滚转抖动，
  因此改为**按减速板键切换**常亮 `AIR BRAKE`（键位可在 F1 设置，或从战雷 `.blk` 导入）
- **其它告警**：`AOA` 过大、`BINGO` 油量见底、`RETURN` 边界警示
- **只读且只取自身/机场数据**：不读取、不绘制任何敌方单位（见合规说明）

### 布局与交互

- **Z 瞄准布局**：数据带外推、俯仰梯下移、数值区下沉、航向带置顶、坡度圆弧放大，**0.28 s 平滑过渡**
- **视角感知**：座舱内自动隐藏 HUD（机头视角可选），`V` 键计数视图切换
- **地图 / 计分板按住时隐藏 HUD**
- **地面也显示数据类符号**（姿态类自动淡出，避免地面 AoA/AoS 噪声）
- **光标洞可调**：`Ctrl+Alt+H` 循环 0/32/48/64/80/112/140 px（默认 32 px），
  越小越不会遮挡飞行矢量；配合光标看门狗自动清理残留箭头
- **后台行为**：游戏不在前台时叠加层整窗隐藏（不遮挡其它窗口的点击），F1 面板打开期间保持显示。
  v0.3.7 起「显示 > 后台隐藏」开关已删除——隐藏是无条件的
- **单实例**：同一时间只允许一个 WtHud 运行。重复启动会弹提示并退出（两个实例会互相抢热键，
  F10/F1 会失灵、HUD 会叠在一起）
- **点击回跳**：切到别的窗口再点回游戏时，万一叠加层吃掉了那一下点击，会自动把游戏激活到前台

### 工程

- **双机型适配**：
  - 有原生地平仪（`aviahorizon_pitch` / `aviahorizon_roll`）→ **原生数据直通**，标注 `AHRS GAME`
  - 顶级喷气无姿态数据 → 合成姿态（`俯仰 = 航迹角 + AoA`，坡度由转弯率给出），标注 `AHRS EST`
- 面向 17–25 Hz 的接口做了**变化才提交**渲染（4K 下约 15 次/秒合成，CPU ≈ 15% 单核）
- 全部参数可现场热键标定并保存到 `hud-config.json`（exe 同目录）

## 运行要求

1. **Windows 10/11**，DX11 显卡
2. 游戏**显示模式设为「无边框窗口」（Fullscreen Window）** —— 独占全屏无法叠加
3. 游戏运行中（8111 服务随游戏进程启动）
4. 自定义战斗 / 自定义难度（试飞）需在自定义设置里勾选 **「允许使用网页界面」（Allow web UI）**

## 安装

从 [Releases](../../releases) 下载 `WtHud.exe`（或 zip），解压到**任意独立目录**
（不要放在游戏根目录 —— 启动器会删除第三方 exe），**双击运行**即可，无需安装 .NET。

- 首次运行会在同目录生成 `hud-config.json`
- 运行时长不限，**连按两次 `F11`** 退出（单次按不会退出，防误触）
- 想改参数按 `F1` 打开设置面板

## 快捷键

除下面三个固定组合外，**其余键位都可以在 F1 设置面板里逐项自定义**（支持 `CTRL+N`、`SHIFT+TAB`、`ALT+F1` 这类组合键）。

### 固定（不可改）

| 热键 | 功能 |
|---|---|
| `Ctrl+Alt+↑ / ↓` | HUD 整体上移 / 下移 0.5° |
| `Ctrl+Alt+PgUp / PgDn` | 垂直视场角（角度尺度）∓1° |
| `Ctrl+Alt+R` | 相机滚转耦合 0/1 |

### 可自定义（括号内为默认值）

| 功能 | 默认键 |
|---|---|
| 显示 / 隐藏 HUD | `F10` |
| 纯机体固连模式 | `F9` |
| 退出（连按两次） | `F11` |
| 设置面板 | `F1` |
| 俯仰零位校准 | `F7` |
| 坡度方向翻转 | `F8` |
| 保存配置 | `CTRL+F12`（F12 被 Steam 截图占用） |
| 光标洞尺寸循环 | `CTRL+ALT+H` |
| 视角切换计数 | `V` |
| 瞄准布局切换 | `Z` |
| 地图（按住隐藏 HUD） | `M` |
| 计分板（单击切换隐藏） | `CTRL+N` |
| 长按隐藏 HUD | `U` |
| 状态复位（视角/地图/计分板） | `F5` |

所有按键要么是被动轮询、要么是系统热键注册；轮询的键**不会吞掉游戏输入**，热键被其他程序占用时会在日志里提示。

> 按键页第 2 页底部可「从战雷 (.blk) 导入按键」：选战雷导出的 controls `.blk`，自动导入减速板 / 计分板两个键；
> `俯仰零位(F7)`、`坡度翻转(F8)`、`纯机体固连(F9)`、`状态复位(F5)`、`长按隐藏(U)`、`失速校准(CTRL+ALT+S)` 也都能在面板里重新绑定。

## 配置项（`hud-config.json`）

| 字段 | 含义 | 默认 |
|---|---|---|
| `verticalFovDeg` | 垂直视场角（角度→像素换算） | 43 |
| `layoutOffsetYDeg` | HUD 整体垂直偏移（度） | 1.8 |
| `pitchZeroDeg` | 俯仰零位 | 0 |
| `bankSign` / `bankTrimDeg` | 坡度方向 / 微调 | 1 / 0 |
| `fpmTrimDeg` / `fpmAosSign` | 飞行矢量纵向偏置 / 横向符号 | 0 / -1 |
| `rollCoupling` | 相机滚转耦合 | 0 |
| `extrapolate` | 速度/高度预测（默认关） | false |
| `hudVisible` | HUD 总开关 | true |
| `showPitchLadder` / `showFpm` | 显示俯仰梯 / 飞行矢量 | true / true |
| `hideInCockpit` / `hideInNose` | 座舱 / 机头视角隐藏 | true / false |
| `hideOnGround` / `groundHideSpeedKmh` / `groundHideDelayMs` | 地面隐藏 / 判定速度 / 延迟 | true / 5 / 500 |
| `tapeRadarAltitude` | 高度带优先显示雷达高度（机型支持时） | true |
| `cursorHoleHalfPx` | 光标洞半径（px，0 = 关闭） | 16 |
| `overspeedKmh` / `overspeedMach` | 超速阈值（IAS，机型无 `vne` 时使用 / 马赫阈值）；**两者同时判断** | 1400 / 1.80 |
| `sinkRateMs` | 下降率告警阈值（m/s） | 40 |
| `pullUpTtiS` | PULL UP 提前量（秒，距撞地时间小于它即告警） | 5.0 |
| `splashEnabled` / `playerName` | 击杀播报开关 / 自己游戏昵称（用于判断「自己击落」） | true / `""` |
| `perVehicleOverrides` | 按机型覆盖表（机型 → 参数），10 项可覆盖 | `{}` |
| `stallKey` / `stallAoaDeg` / `stallAoaByVehicle` | 失速校准键 / 通用阈值 / 每机型校准表 | CTRL+ALT+S / 22 / {} |
| `enemyAirfieldAlertKm` | 敌机场告警距离（km，0=关闭） | 12 |
| `friendlySpaaKm` | 友军防空（SPAA）显示距离（km，0=关闭） | 60 |
| `rollCouplingMode` | 坡度耦合方式：`auto` 自动 / `on` 开 / `off` 关 | auto |
| `pitchSign` | 原生俯仰符号（0=自动判定，±1=强制） | 0 |
| `aoaWarnDeg` | AOA 告警阈值（度） | 18 |
| `bingoFuelSeconds` | BINGO 油量阈值（秒） | 180 |
| `enduranceScale` | 续航标定系数（对齐游戏读数） | 1.30 |
| `mapYFlip` / `edgeMarginFraction` | 地图 Y 轴翻转 / 边界告警范围 | true / 0.06 |
| `viewKey` `zoomKey` `mapKey` `scoreboardKey` `holdHideKey` `resetKey` `settingsKey` `pitchZeroKey` `bankFlipKey` `bodyFixedKey` `showKey` `exitKey` `saveKey` `cursorHoleKey` | 全部可自定义键位 | 见上表 |
## 从源码构建

需要 **.NET 8 SDK**：

```powershell
cd src/WarThunderHud
./build.ps1          # 编译
./run-hud.ps1        # 运行
./run-hud.ps1 -Demo  # 不开游戏也能看 HUD 长什么样
```

发布单文件 exe：

```powershell
dotnet publish src/WarThunderHud/WarThunderHud.csproj -c Release -r win-x64 `
  --self-contained true -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true -o dist
```

其他诊断模式（命令行参数）：`overlay`（HUD）、`probe`（实时遥测）、`selftest`（解析单测）、
`fusiontest`（姿态融合场景）、`preview out.png`（渲染 PNG）、`replay <file.jsonl>`（回放）、`dumpgeo`。

## 数据来源与限制

| 数据 | 来源 | 说明 |
|---|---|---|
| 速度/高度/马赫/G/AoA/AoS/升降率/油量/襟翼/起落架/减速板 | `/state` | 所有机型都有 |
| 航向/原生姿态/弹药 | `/indicators` | 仅部分机型有 |
| 地图/聊天/击杀日志 | `/map_obj.json` 等 | 本 HUD **不使用**（见合规说明） |

已知限制：

- **顶级喷气无原生姿态**：俯仰/坡度靠 `航迹角 + AoA` 与转弯率合成，稳态可用、剧烈机动有偏差，HUD 会标注 `AHRS EST`
- **少数机型（如阵风 C）连航向都没有**：坡度改用滚转速率积分（停止滚转后缓慢回平），
  导航方位改用地图位置推算的航迹（COG）；航迹不可用时只显示机场距离
- **原生姿态是座舱陀螺仪读数**：`AHRS GAME` 机型按游戏数据直通显示，极端姿态下与真实姿态可能有偏差（这是仪表本身的特性）
- 接口更新率约 17–25 Hz（游戏侧节流），HUD 用预测/插值补偿但仍有轻微滞后
- 续航、襟翼档位等为**推算值**，可能与游戏内显示不同（续航可用标定系数对齐）
- 游戏使用**软件光标**，叠加层与光标归属存在竞争，因此保留了可调光标洞 + 看门狗机制

## 合规说明（重要）

- 本工具**只读取**游戏自带的本地 HTTP 接口，不修改客户端、不注入进程、不读取内存、不修改游戏画面渲染
- **不显示敌人位置、不做 ESP**。不使用 `/map_obj.json` 绘制敌情
- 官方论坛口径：使用 localhost 数据做 overlay 一般允许，但官方不对第三方工具作任何保证，**使用风险自负**
- 请遵守 Gaijin 用户协议与各服务器规则；因使用本工具产生的任何后果由使用者承担

## 目录结构

```
src/WarThunderHud/
  Telemetry/   8111 客户端、解析、能力探测、姿态融合
  Hud/         符号基元、几何布局、标定、配置
  Graphics/    D3D11/D2D 画布、绘制器、PNG 输出
  Overlay/     Win32 窗口、DirectComposition、光标洞、热键
  Input/       按键绑定解析（支持组合键）
  UI/          设置面板（WinForms）
docs/          设计文档、接口调研、视角实验
```

## 许可

本项目采用 **[GNU AGPL-3.0](LICENSE)** 许可。

- ✅ 可以自由使用、修改、分发
- ✅ 可以 fork、二次开发、发布自己的版本
- ⚠️ **衍生作品必须同样以 AGPL-3.0 开源**
- ⚠️ **如果把修改版做成网络服务（SaaS / 网页 / API）提供给他人使用，也必须公开源码**（AGPL 第 13 条）

简单说：**不禁止"用"，但禁止"拿着改一改就闭源商用"**。如需商业授权（豁免上述条款），请联系作者。

---

*本项目与 Gaijin Entertainment 无关。War Thunder 是 Gaijin Entertainment 的商标。*

---

<a id="english"></a>

# English

A military-style HUD overlay for **War Thunder**, driven entirely by the official local API on
`http://127.0.0.1:8111`.

**Read-only by design**: no injection, no memory reads, no file modification, no packet
interception, no automation. It never draws enemy positions (no ESP).

Current version **v0.3.7** · [Changelog](CHANGELOG.md) · [AGPL-3.0](LICENSE)

## Screenshots

| Third-person HUD | Close view |
|---|---|
| ![Third-person HUD](docs/screenshots/hud-01-third-person.jpg) | ![Close view](docs/screenshots/hud-02-close-view.jpg) |

![All elements](docs/screenshots/hud-all-elements.jpg)

## Features

### MFD settings panel (F1)

- Bezel and 25 physical buttons rendered from a **Blender model**, mapped 1:1 to the ten screen
  rows: **side key = select, CONFIRM = apply**
- Three levels: main menu → module page (Display / Camera / Alerts / Keys / Panel / Language /
  Airframes / DIAG / Snapshots) → second-level page (numeric entry / key capture / text entry /
  confirmation)
- Numbers can be typed directly; key bindings are captured **live while the combo is held**
  (hold `Ctrl` → `CTRL`, then press `Q` → `CTRL+Q`, release to freeze, press again to restart)
- Bezel text, screen text and HUD alerts each have their own Chinese/English setting;
  alert box widths are recomputed from the measured text width of the active language
- DIAG page shows live latency/validity of `/state`, `/indicators`, `/map_obj`, `/map_info`
  and can check the latest GitHub release
- F1 toggles the panel; it scales to the screen and the window region is clipped to the bezel
  silhouette (no black box)

### Attitude symbols

- Pitch ladder, horizon line and bank scale (ladder stays level in third-person view)
- Bank pointer showing the roll angle and `+` / `-` (belly down / up)
- Flight path marker (FPM) with adjustable trim

### Data tapes

| Position | Content |
|---|---|
| Left (speed) | IAS readout, Mach `M`, longitudinal `ACC` + trend bar |
| Right (altitude) | Barometric altitude `H`, `V/S` + trend bar |
| Top | Heading tape scrolling with the heading |

### Data block

| Row | Content |
|---|---|
| `G` | Normal load factor (colour coded) |
| `AOA` | Angle of attack |
| `FUEL` | Remaining endurance `mm:ss` |
| `ALT` | Barometric altitude |
| `GRID` | Map grid cell (e.g. `F2`) |

`FUEL` is "how long you can still fly at the current burn rate", estimated over a 12 s window and
scalable with a calibration factor.

### Configuration hints (gear / flaps / airbrake)

Motion states blink amber (`GEAR DOWN`, `FLAPS DOWN`, `AIRBRAKE OUT`) and settle to a steady
confirmation (`STB`, `FLAPS 30`, `AIRBRAKE`). The A-10 family uses deceleron brakes, so their
airbrake indication is driven by the key instead of the noisy telemetry value.

### Kill banner (SPLASH)

- When **you** shoot down a player, a solid green banner appears above screen centre:
  「击落」 when the alert language is Chinese, `SPLASH` when it is English; the victim's name is
  shown below in plain green text. **No animation** — appears instantly, disappears after 1.2 s
  (multiple kills queue up)
- Data comes from 8111 `/hudmsg` (4 Hz incremental polling, de-duplicated by id, silent on errors)
- **Only "shot down" (击落了) counts**: AI / ground "destroyed" messages, your own death and other
  players' kills are ignored
- Setup: `F1` → Display page 2 → `Player name` (must match the in-game name; type ASCII or paste
  with `Ctrl+V`) → enable `Splash alerts`

### Per-airframe settings

- Ten parameters can be overridden per aircraft: pitch zero / pitch sign / bank trim / marker trim /
  stall AoA / AoA warning / overspeed IAS / overspeed Mach / sink rate / PULL UP lead time
- Resolution order: **airframe override > airframe profile (`airframes.json`) > global config**
- The editor header shows the active **write layer**; the airframe detail page shows each value's
  **source** (airframe / profile / global)

### Navigation and alerts

- `GRID` cell, nearest friendly/enemy airfield bearing and distance (`AF ↗ 4.2km`)
- Blinking red `RETURN ↑` near the map edge; blinking `AF!` box inside the enemy-airfield radius
- Terrain alerts: `SINK RATE` (adjustable threshold per airframe / global), upgraded to `PULL UP`
  when the aircraft exposes radar altitude (`PULL UP lead` time is configurable, default 5 s)
- Overspeed: red speed readout plus blinking `REDUCE SPEED` boxes; **IAS and Mach are checked
  simultaneously** (either limit triggers it), using the aircraft `vne` when available
- Stall: blinking `STALL` (amber = pre-stall, red = stall), using a per-aircraft calibrated
  angle first, then the cockpit indexer (200 ms debounce + 0.85 hysteresis), then a generic threshold
- `AOA`, `BINGO`, `RETURN` alerts
- Only own aircraft and airfields are read; enemy units are never drawn

### Layout

- `Z` aiming layout: tapes move outboard, ladder drops, readouts sink, heading tape moves up,
  bank arc grows — 0.28 s smooth transition
- Cockpit view hides the HUD (nose view optional); view changes are counted with `V`
- HUD hides while the map or scoreboard is held
- Adjustable cursor hole (`Ctrl+Alt+H` cycles sizes) with a cursor watchdog
- **Background behaviour**: the overlay hides itself entirely while the game is not in the foreground,
  so it cannot block clicks on other windows; it stays visible while the F1 panel is open.
  The "hide in background" toggle was removed in v0.3.7 — hiding is unconditional
- **Single instance**: only one WtHud may run at a time; a second launch shows a message and exits
  (two instances fight over the hotkeys, make F10/F1 dead and stack two HUDs)
- **Click back**: if the overlay ever swallows the click that was meant to bring the game back to the
  foreground, that click is turned into "activate the game"

## Requirements

1. Windows 10/11 with a DX11 GPU
2. Game display mode must be **Fullscreen Window (borderless)** — exclusive fullscreen cannot be
   overlaid
3. The game must be running (the 8111 service starts with the game)
4. Custom battles / test flights need **"Allow web UI"** enabled in the custom settings

## Install

Download `WtHud.exe` (or the zip) from [Releases](../../releases) and unpack it into **any standalone
folder** (not the game directory — the launcher deletes third-party executables). Run it; no .NET
installation is required.

- First run writes `hud-config.json` next to the exe
- Press `F11` twice to exit (a single press is ignored to avoid accidents)
- Press `F1` to open the settings panel

## Hotkeys

Everything except the three fixed combinations can be rebound in the F1 panel (combos such as
`CTRL+N`, `SHIFT+TAB`, `ALT+F1` are supported).

### Fixed

| Hotkey | Action |
|---|---|
| `Ctrl+Alt+↑ / ↓` | Move the HUD up/down by 0.5° |
| `Ctrl+Alt+PgUp / PgDn` | Vertical FoV (angular scale) ∓1° |
| `Ctrl+Alt+R` | Camera roll coupling 0/1 |

### Rebindable (default in brackets)

| Function | Default |
|---|---|
| Show / hide HUD | `F10` |
| Body-fixed mode | `F9` |
| Exit (press twice) | `F11` |
| Settings panel | `F1` |
| Pitch zero calibration | `F7` |
| Flip bank direction | `F8` |
| Save config | `CTRL+F12` |
| Cycle cursor hole size | `CTRL+ALT+H` |
| View counter | `V` |
| Aiming layout | `Z` |
| Map (hold to hide HUD) | `M` |
| Scoreboard (toggle hide) | `CTRL+N` |
| Hold to hide HUD | `U` |
| Reset state | `F5` |

Keys are either polled passively or registered as system hotkeys; polled keys never swallow game
input, and busy hotkeys are reported in the log.

> The bottom of Keys page 2 has **"Import WT .blk keys"**: pick a War Thunder `controls` export and
> the airbrake / scoreboard bindings are imported automatically. Pitch zero (F7), flip bank (F8),
> body-fixed (F9), reset (F5), hold-to-hide (U) and stall calibration (CTRL+ALT+S) are rebindable
> in the panel as well.

## Configuration (`hud-config.json`)

See the table in the Chinese section above for every field; the file is written next to the
executable and can also be edited from the MFD panel. Notables added in v0.3.x:

- `sinkRateMs` — sink-rate alert threshold (m/s, default 40)
- `pullUpTtiS` — PULL UP lead time in seconds (default 5.0)
- `splashEnabled` / `playerName` — kill banner toggle / your in-game name
- `perVehicleOverrides` — per-airframe overrides (ten parameters; airframe > profile > global)
- `overspeedKmh` / `overspeedMach` — both limits are checked simultaneously

## Build from source

Requires the **.NET 8 SDK**:

```powershell
cd src/WarThunderHud
./build.ps1          # build
./run-hud.ps1        # run
./run-hud.ps1 -Demo  # preview the HUD without the game
```

Single-file publish:

```powershell
dotnet publish src/WarThunderHud/WarThunderHud.csproj -c Release -r win-x64 `
  --self-contained true -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true -o dist
```

Other CLI modes: `overlay`, `probe`, `selftest`, `fusiontest`, `preview out.png`,
`replay <file.jsonl>`, `dumpgeo`, plus `mfd` (standalone panel) and `mapprobe` (map diagnostics).

## Data sources and limitations

| Data | Source | Notes |
|---|---|---|
| Speed / altitude / Mach / G / AoA / AoS / V-S / fuel / flaps / gear / airbrake | `/state` | Available on all aircraft |
| Heading / native attitude / ammo | `/indicators` | Only on some aircraft |
| Map objects, chat, kill log | `/map_obj.json` etc. | **Not used** (see compliance) |

Known limitations:

- **Top-tier jets expose no native attitude**: pitch and bank are synthesised from flight-path
  angle + AoA and turn rate — fine in steady flight, less accurate in hard manoeuvres (marked
  `AHRS EST`)
- **A few aircraft (e.g. Rafale C) have no heading at all**: bank falls back to roll-rate
  integration, navigation uses course over ground derived from map positions
- Native attitude is the cockpit gyro reading, passed through as-is on `AHRS GAME` aircraft
- The API updates at roughly 17–25 Hz; extrapolation hides part of the stepping but some lag
  remains
- Endurance, flap position and similar values are estimates and may differ from the in-game
  readouts (endurance has a calibration factor)

## Compliance (important)

- The tool **only reads** the game's own local HTTP API. It does not modify the client, inject
  into the process, read memory or alter rendering
- **No enemy positions, no ESP.** `/map_obj.json` is never used to draw enemy units
- Gaijin's forum position is that localhost-driven overlays are generally tolerated, but no
  third-party tool is officially supported — **use at your own risk**
- Follow the Gaijin Terms of Service and each server's rules; you are responsible for the
  consequences of using this tool

## License

**[GNU AGPL-3.0](LICENSE)**

- ✅ Free to use, modify and redistribute
- ✅ Forking and publishing your own version is allowed
- ⚠️ Derivative works must also be released under AGPL-3.0
- ⚠️ If you expose a modified version as a network service, you must publish the source (AGPL §13)

In short: **using it is fine; taking it, modifying it and shipping it closed-source is not.**
For commercial licensing (waiving the above), contact the author.

---

*Not affiliated with Gaijin Entertainment. War Thunder is a trademark of Gaijin Entertainment.*
