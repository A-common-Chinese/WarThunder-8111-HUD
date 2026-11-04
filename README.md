# War Thunder HUD (8111)

用官方 `localhost:8111` 接口驱动的**战争雷霆平视显示器（HUD）叠加层**，反作弊安全：**不注入、不读内存、不改文件、不拦截网络、不自动操作**。

A military-style HUD overlay for **War Thunder**, driven entirely by the official local API on
`http://127.0.0.1:8111`. Read-only: no injection, no memory reads, no file modification,
no packet interception, no automation.

当前版本 **v0.1.1** · [更新日志](CHANGELOG.md) · [AGPL-3.0](LICENSE)

---

## 效果截图

第三人称（HUD 全要素）：

![第三人称 HUD](docs/screenshots/hud-01-third-person.jpg)

近距离视角：

![近距离视角](docs/screenshots/hud-02-close-view.jpg)

## 特性

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

> `FUEL` 是"按当前油耗还能飞多久"，用 12 秒滑动窗口最小二乘估计油耗，可在设置里用标定系数对齐游戏内显示。

### 构型提示（中偏左）

| 提示 | 运动中 | 到位后 |
|---|---|---|
| 起落架 | 琥珀**闪烁** `GEAR DOWN` / `GEAR UP` | 绿色常亮 `STB` 0.8 秒 |
| 襟翼 | 琥珀**闪烁** `FLAPS DOWN` / `FLAPS UP` | 常亮 `FLAPS <档位>`（收回 0 后消失） |
| 减速板 | 琥珀**闪烁** `AIRBRAKE OUT` / `AIRBRAKE IN` | 常亮 `AIRBRAKE`（提示仍处于展开状态） |

三者共用同一列、随 `Z` 瞄准布局一起下沉。

### 告警

- **超速**：IAS 超过阈值（默认 1400 km/h，可调）时速度数字变红
- **过载**：见上表 G 值配色
- **油量**：续航低于 2 分钟变红

### 布局与交互

- **Z 瞄准布局**：数据带外推、俯仰梯下移、数值区下沉、航向带置顶、坡度圆弧放大，**0.28 s 平滑过渡**
- **视角感知**：座舱内自动隐藏 HUD（机头视角可选），`V` 键计数视图切换
- **地图 / 计分板按住时隐藏 HUD**
- **地面也显示数据类符号**（姿态类自动淡出，避免地面 AoA/AoS 噪声）
- **光标洞可调**：`Ctrl+Alt+H` 循环 0/32/48/64/80/112/140 px（默认 32 px），
  越小越不会遮挡飞行矢量；配合光标看门狗自动清理残留箭头

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

| 热键 | 功能 |
|---|---|
| `F1` | 设置面板（所有参数 + 全部快捷键一览） |
| `F5` | 视角/地图状态复位 |
| `F7` | 俯仰零位校准（平飞时按下，把当前俯仰记为 0） |
| `F8` | 反转坡度方向 |
| `F9` | 纯机体固连模式（隐藏姿态类符号） |
| `F10` | HUD 显示 / 隐藏 |
| `F11` **连按两次** | 退出 |
| `F12` | 保存配置 |
| `Ctrl+Alt+↑ / ↓` | HUD 整体上移 / 下移 0.5° |
| `Ctrl+Alt+PgUp / PgDn` | 垂直视场角（角度尺度）∓1° |
| `Ctrl+Alt+R` | 相机滚转耦合 0/1 |
| `Ctrl+Alt+H` | 循环光标洞尺寸（0/32/48/64/80/112/140 px） |
| `Z` | 瞄准布局切换（跟随游戏放大键） |
| `V` | 视角切换计数（座舱隐藏 HUD） |

以上 `V` / `Z` / 地图键 / 计分板键**均可在设置面板自定义**，支持 `CTRL+N`、`SHIFT+TAB` 这类组合键。
所有按键都是**被动轮询**，不会吞掉游戏输入。

## 配置项（`hud-config.json`）

| 字段 | 含义 | 默认 |
|---|---|---|
| `verticalFovDeg` | 垂直视场角（角度→像素换算） | 43 |
| `layoutOffsetYDeg` | HUD 整体垂直偏移（度） | 1.8 |
| `pitchZeroDeg` | 俯仰零位 | 0 |
| `bankSign` / `bankTrimDeg` | 坡度方向 / 微调 | 1 / 0 |
| `fpmTrimDeg` | 飞行矢量偏置 | 0 |
| `rollCoupling` | 相机滚转耦合 | 0 |
| `extrapolate` | 速度/高度预测（默认关） | false |
| `hudVisible` | HUD 总开关 | true |
| `cursorHoleHalfPx` | 光标洞半径（px，0 = 关闭） | 16 |
| `overspeedKmh` | 超速阈值（IAS km/h） | 1400 |
| `enduranceScale` | 续航标定系数（对齐游戏读数） | 1.30 |
| `hideInCockpit` / `hideInNose` | 座舱 / 机头视角隐藏 | true / false |
| `viewKey` / `zoomKey` / `mapKey` / `scoreboardKey` | 自定义按键（支持组合键） | `V` / `Z` / `M` / `TAB` |

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
