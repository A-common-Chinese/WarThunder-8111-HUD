# 战争雷霆 8111 真 HUD 插件 — 实施方案 v1

> 状态：待用户批准后进入 M0 编码。本文只做设计，不含代码。

## 0. 目标与非目标

**目标**：在战争雷霆**第三人称视角**下叠加一层军用风格的真 HUD（平视显示），居中显示飞行矢量标记，两侧为滚动速度带与高度带，顶部航向带，随姿态联动的俯仰梯与坡度尺，辅助 G/AoA/马赫/油量/告警。

**非目标（明确不做，合规红线）**：
- 不做地图/敌情类信息（不读 `map_obj.json` 绘制敌人位置，避免 ESP 定性）
- 不注入游戏进程、不 hook Present、不读游戏内存、不改游戏文件、不做读屏 OCR
- 不自动操作游戏

## 1. 技术栈

| 项 | 选型 |
|---|---|
| 语言/运行时 | C# / .NET 8 |
| UI 宿主 | WPF（配置窗、校准窗、托盘） |
| HUD 渲染 | Direct3D 11 + Direct2D（`Vortice.Windows` 原生 COM 封装） |
| 数据 | `HttpClient`（长连接、单线程串行、超时 500ms） |
| 配置 | JSON profile（System.Text.Json） |
| 打包 | 单文件自包含 win-x64（放独立目录，避开启动器清理） |

## 2. 进程结构与模块

```
WarThunderHud.exe
├─ App / Tray              托盘、配置窗、热键注册
├─ DataSource              8111 轮询（25Hz），端点调度、超时、重连
├─ TelemetryModel          单位归一、字段映射、机型能力探测、地面/空中判定
├─ AttitudeFusion          姿态融合（原生 / 合成 / 置信度）
├─ MotionExtrapolator      25Hz → 渲染帧的外推与平滑
├─ ViewStateController     V 键循环计数、重同步、显示开关
├─ CalibrationService      F7 零位 / F8 增益 / FOV 手填，profile 持久化
├─ HudRenderer             D2D 绘制：符号库、脏矩形
└─ OverlayWindow           分层透明窗、点击穿透、DPI、多显示器
```

**线程模型**：数据线程（25Hz 轮询 + 融合）→ 共享快照（不可变结构 + 双缓冲）→ 渲染线程（60–100fps 外推插值）。UI 线程只处理窗口消息，不参与数据与绘制。

## 3. 数据层

### 3.1 端点调度
| 端点 | 频率 | 用途 |
|---|---|---|
| `/state` | 25Hz | 主数据源（两种机型都有） |
| `/indicators` | 25Hz | 姿态/仪表/弹药（机型相关） |
| `/map_info.json` | 1Hz | 仅取 `valid`/`hud_type`（备用），不取地图用途 |
| `/mission.json` | 1Hz | 状态变化 → 视角重同步触发 |
| `/hudmsg`、`/gamechat`、`/map_obj.json` | 不轮询 | 本期不做 |

### 3.2 模型字段（内部统一单位：m/s、m、deg、kg、G）
| 内部字段 | 来源优先级 |
|---|---|
| `IasKmh` / `TasMs` / `Mach` | `state.IAS, km/h` / `state.TAS, km/h` / `state.M` |
| `AltM` | `state.H, m`（备用 `ind.altitude_hour`） |
| `VyMs` | `state.Vy, m/s`（备用 `ind.vario`） |
| `AoaDeg` / `AosDeg` | `state.AoA/AoS, deg` |
| `NyG` | `state.Ny`（备用 `ind.g_meter`） |
| `RollRateDegS` | `state.Wx, deg/s` |
| `HeadingDeg` | `ind.compass` |
| `FuelKg` / `Fuel0Kg` | `state.Mfuel/Mfuel0, kg`（备用 `ind.fuel`） |
| `Pitch/ Roll` | `ind.aviahorizon_pitch/roll`（可能缺失） |
| `Bank/ TurnRate` | `ind.bank` / `ind.turn`（可能缺失） |
| `Ammo[]` | `ind.ammo_counter1..3`（可能缺失） |

### 3.3 机型能力探测
首次拿到 `ind.type` 时计算能力位（`HasAttitude`、`HasAmmo`、`HasAltTape`…），缓存到 profile（`mig-19j_6a`、`j_16` 已有实测基线）。能力位决定 HUD 符号的可用性，缺失则隐藏对应符号并记录日志。

### 3.4 地面/空中判定
地面时 `AoA/AoS` 为垃圾值（实测 −28.9 / −36.4）。判定：`valid == true && IAS > 30 km/h` → 空中；否则 HUD 进入"地面模式"（只显示速度/高度/油量，不画姿态符号）。

## 4. 姿态融合

| 输出 | 原生可用 | 原生缺失（顶级喷气） |
|---|---|---|
| 俯仰 | `aviahorizon_pitch` + 自学习偏置（地面静止时校准，实测偏置约 −4.3°） | `asin(Vy/TAS) + AoA` |
| 坡度 | `−aviahorizon_roll` 或 `bank` | `acos(1/Ny)` 限幅 ±80°；符号由罗盘 1–2s 滑动趋势 + `Wx` 积分确定 |
| 航向 | `compass` | 同 |
| 转弯率 | `turn` | 罗盘低频差分（经低通，禁止逐帧差分） |

- 融合器：互补滤波（`α≈0.15` / 25Hz），角速率限幅，死区 0.3°
- 输出带 `AttitudeSource { Game, Estimated }`，HUD 角落标注 `AHRS: GAME/EST`
- 置信度低（如 `Ny` 跳变、`valid=false`）时淡出俯仰梯，显示"姿态不可用"
- **精度目标**：GAME ±1°；EST 稳态 ±3–5°，瞬态不保证

## 5. 渲染

- 窗口：`WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOPMOST | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW`，`SetWindowPos` 保持顶层；DPI 感知 v2
- 合成：D3D11 交换链 + D2D 目标；预乘 alpha
- 刷新：只重绘 HUD 包围盒（脏矩形），4K 下避免全屏 Clear
- 角度↔像素：`pxPerDeg = screenH / verticalFov`，`verticalFov` 来自校准
- 符号规格（初版）：
  - 俯仰梯：±90°，每 5° 一条，±10° 以内为实线，其余虚线；随坡度旋转、随俯仰平移
  - 速度带：右侧？不，**左速度右高度**（真实 HUD 惯例）：速度带每 50 km/h 一格，数字框显示当前 IAS，马赫数小字
  - 高度带：每 100 m 一格，数字框显示当前气压高度 + 升降率箭头
  - 航向带：顶部，每 10° 一格，每 30° 标数字
  - FPM：机身固连，屏幕偏移 = (−AoA, +AoS) × pxPerDeg
  - 坡度尺：顶部弧形刻度 + 指针
  - 数字区：G、AoA、马赫、油量/续航
- 视觉：单色绿（默认 `#3CFF6E`，可配），线宽 2px@4K，等宽点阵字，带轻微发光（D2D 高斯或双层描边）

## 6. 校准

| 步骤 | 热键 | 内容 |
|---|---|---|
| 零位 | F7 | 目视配平水平，按 F7 记录地平线像素偏移 |
| 增益 | F8 | 抬头至合成俯仰 +15~20°，按 F8 求 px/度 |
| 手填 | 设置窗 | 已知 FOV 可直接输入，跳过 F7/F8 |
| 校验 | F6 | 显示参考地平线叠加层，目视检查对齐 |

校准结果存 `profiles/{vehicleType}.json`，键为「分辨率 + FOV」；分辨率或 FOV 变化时提示重标。

## 7. 视角与显示控制

- 状态机 `0=第三人称(显示) / 1=座舱(隐藏) / 2=机头(隐藏)`，按 V 循环
- 重同步：载具 `type` 变化、`mission.status` 变化、999 进程重启、重生事件 → 归零
- 兜底：F9 = 强制第三人称；F10 = 全局显示/隐藏
- 改键采样：设置里点"录制按键"，按一次实际切视角的键
- 只在 `aces.exe` 前台时计数（`GetForegroundWindow` + 进程名）

## 8. 里程碑与验收

| 里程碑 | 内容 | 验收标准 |
|---|---|---|
| **M0** 骨架 | 透明置顶点击穿透窗、D2D 画固定 HUD、读真实 8111 数据、mock/回放数据源 | 4K 下 60fps 稳定，数据变化能实时反映到 HUD，游戏内可见 |
| **M1** 数据带 | 速度带/高度带/航向带/FPM/G/AoA/马赫/油量 | 与游戏内数值一致；地面自动降级 |
| **M2** 姿态 | GAME 源优先 + 校准 F7/F8 | MiG-19 上俯仰梯与游戏地平线偏差 ≤2° |
| **M3** 合成姿态 | EST 源（Ny 反算坡度 + γ+AoA 俯仰） | 顶级喷气上可用；标注 EST；误差 ≤5° 稳态 |
| **M4** 视角控制 | V 计数 + 重同步 + 热键 | 座舱/机头自动隐藏，误判可按 F9 恢复 |
| **M5** 打磨 | 告警、profile、性能、稳定性 | 4K/100fps 下 CPU < 5%，Alt-Tab 不崩，游戏退出不残留 |

## 9. 风险与对策

| 风险 | 对策 |
|---|---|
| 4K 透明合成掉帧 | 脏矩形、D3D11 直接合成、避免全屏 Clear；先做性能基线 |
| HAGS 导致 alt-tab 卡顿 | 文档提示关闭；检测到 HAGS 时提示 |
| 游戏是独占全屏 | 启动检查窗口模式，提示切"无边框窗口" |
| 合成姿态在大过载/滚转中失真 | 限幅 + 淡出 + EST 标注；提供"仅姿态无关层"开关 |
| 8111 字段随版本变化 | 能力探测 + 缺失隐藏 + 日志；不硬编码字段存在性 |
| 启动器删文件 | 程序放独立目录，启动时检测是否在游戏根目录 |
| 合规争议 | 仅自机数据、不读地图敌情、不注入、论坛不公开传播演示视频 |

## 10. 工程约定

- 目录：`src/WarThunderHud/`（单体多模块）、`tests/`（数据解析与融合算法单测，用真实抓包回放）
- 真实抓包已存 `wt8111-research/live/*.jsonl`，作为回放测试语料（MiG-19 与 J-16 两种能力集）
- 日志：滚动文件 + 关键状态变化记录（视角检测、姿态源切换、数据缺失）
- 版本：`v0.1.0` 起，`M0` 完成打第一个 tag

## 11. 开工前需用户确认

1. **左右布局**：速度带在左、高度带在右（真实 HUD 惯例）——确认
2. **速度单位**：km/h 还是节（真实 HUD 多用节，但游戏内是 km/h）
3. **高度单位**：米（游戏内）还是英尺
4. **是否需要"仅姿态无关层"降级开关**（合成姿态不可信时退化成数据带）
5. **开发期数据来源**：先用真实 8111 直接调试（游戏开着），还是先做回放 mock（不开游戏也能开发）
