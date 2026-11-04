# 8111 HUD 插件 — 决策记录与待办（2026-11-04 13:55 存档）

## 一、已确认的需求（用户拍板）

1. **形态**：叠加在游戏画面上的**真 HUD**（居中的平视显示，不是数据面板）
2. **基础符号**：两侧速度带 / 高度带（真实飞机样式）、飞行矢量标记（FPM）
3. **主力机型**：顶级喷气为主
4. **姿态策略**：允许**合成姿态**（原生姿态可用时优先，不可用时用合成，需显式标注来源）
5. **叠加前提**：接受把游戏切「无边框窗口」
6. **技术栈**：**C#**
7. **视觉风格**：军用单色 HUD
8. **显示条件**：第三人称显示；座舱视角、机头视角隐藏
9. **视角检测**：用户提议按 V 循环计数（默认第三人称 → V 座舱 → V 机头 → V 第三人称，3 态循环）；主方案 + 重同步 + 手动兜底
10. **校准**：除标定向导外，允许**快捷键**在玩家目视对准 0° 时手动校准

## 二、环境实测事实（已核实）

- 游戏：`D:\Program Files (x86)\Steam\steamapps\common\War Thunder`，`aces` PID 15692
- `config.blk`：`use_eac:b=no`、`mode:t="fullscreen"`（叠加前需改无边框窗口）、`fpsLimit:i=100`、3840×2160
- 8111 服务：`ACAO: *`；返回的头无 Server 字段；`/map.img` 实为 **JPEG**（非 PNG）
- 更新率：**约 25Hz**（实测值变化间隔均值 40ms，抖动 29–84ms），比业界传的 10Hz 快
- 端点共 8 个；`/hud`、`/planes.json`、`/battle`、`/respawn.json` 均 404
- `map_obj.json` 正常返回（未复现 2026-09 的"返回空"报告）
- `map_info.json` 实测多出文档外字段：`grid_size`、`hud_type`、`valid`

## 三、机型数据能力（关键约束）

| 机型 | `/indicators` 字段数 | 姿态字段 |
|---|---|---|
| `j_16`（顶级喷气） | 20 | ❌ 无 aviahorizon / bank / turn |
| `mig-19j_6a`（早期喷气） | 52 | ✅ aviahorizon_pitch/roll、bank、turn 齐全 |

- 顶级喷气：速度/高度/马赫/G/AoA/AoS/航向 只能从 `/state` 取（`IAS/TAS/H/M/Ny/AoA/AoS/Vy/Mfuel` + `compass`）
- 早期喷气额外有：`ammo_counter1-3`、`weapon_lock_lamp2`、`vario`、`altitude_hour/min`、`rpm`、`water_temperature`、`fuel`、`trimmer`、`clock_*`
- **单位校准**：`indicators.speed` 是 **m/s**（不是 km/h、不是归一化）；`altitude_hour` 总高度、`altitude_min` 余量

## 四、姿态合成实验结论（MiG-19 上 136 帧实测）

- 俯仰：`pitch = asin(Vy/TAS) + AoA` 物理上成立；但**原生 `aviahorizon_pitch` 自带约 −4.3° 固定偏置**（停放时也读 −4.28），**不能当基准**
- 坡度：`atan(V·dψ/dt/g)`（罗盘差分）误差 14.6° 均值 / 48° 最大 → **方案废弃**
- 替代：坡度主源用 `cos φ = 1/Ny`（两种机型都有 Ny）；方向由罗盘低频趋势 + `Wx` 积分确定；`turn` 字段（早期喷气有）作校验
- 结论：合成姿态精度 ±3–5°（稳态），必须显式标注 `AHRS: GAME / EST`，瞬态不保证

## 五、技术方案（已定，待写正式文档）

- .NET 8/9 + WPF 宿主窗 + Direct3D11/Direct2D（`Vortice.Windows`）
- 窗口：`WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOPMOST | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW`，点击穿透
- 只在 HUD 脏矩形刷新（4K 必备）；渲染循环独立线程 60–100fps；数据 25Hz → 外推插值
- 数据层：单线程串行轮询（8111 服务是单线程）、200–1000ms 超时、断线重连
- 校准：F7 零位（对准 0°）、F8 增益（另取一个姿态求 px/度）、或直接填 FOV；按「分辨率 + FOV」存 profile
- 视角：V 循环计数状态机 + 重同步（载具 `type` 变化 / mission 状态变化 / 进程重启 / 重生）+ F9 强制第三人称 + F10 全局开关
- HUD 元素：两侧速度带（IAS + 马赫框）、高度带（气压高 + 升降率）、顶部航向带、中央 FPM（AoA/AoS，无需姿态）、坡度尺、俯仰梯、G/AoA 数字、油量/续航、告警

## 六、待办

1. **[已完成 2026-11-04 19:22] 视角检测实验**：结论 **8111 完全不暴露视角**（`hud_type`/`isig`/`nkeys`/`nobj`/`me.*`/`mission` 全部 0 跳变）→ 采用 V 循环计数 + 重同步 + 手动兜底。详见 `03-视角实验结论.md`
2. **[已完成] 正式实施方案文档** → `02-实施方案.md`（待用户批准后进 M0）
3. 叠加前需要用户把游戏切「无边框窗口」

## 七、踩过的环境坑（写代码时要避开）

- 本机后台任务是 **Windows PowerShell 5.1**：默认**没有** `System.Net.Http` 程序集 → `HttpClient` 报"找不到类型"，用 `Invoke-WebRequest`
- **`.ps1` 必须纯 ASCII 或带 UTF-8 BOM**：无 BOM 的中文会被按 GBK 解析，吞掉引号导致语法错误
- 启动器会删除游戏根目录内的第三方 exe → 插件放独立目录
