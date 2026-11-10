# 会话交接（WtHud / 战雷 8111 HUD）

> 给下一个会话：先读本文件，再动手。所有路径均在工作区 `D:\Documents\deepseek-harness\default-workspace` 下。

## 1. 项目与路径

| 项 | 路径 |
|---|---|
| 源码（工作副本，日常编辑） | `src\WarThunderHud\` |
| 源码（git 仓库副本） | `wt-hud\`（分支 `v0.3.0`，远端 `A-common-Chinese/WarThunder-8111-HUD`） |
| 运行目录（用户实际启动） | `dist-wthud\`（`WtHud.exe` + 配置 + `temp\` 快照） |
| 工具脚本 | `tools\scan_page_migration.py`、`tools\gen_scope_doc.py` |
| 文档 | `wt-hud\docs\settings-scope.md`（自动生成）、`docs\snapshot-restore-spec.md` |
| 设计素材 | `wt8111-research\`、`blender\` |

**重要**：日常改 `src\`，提交前用 `robocopy src\WarThunderHud wt-hud\src\WarThunderHud /MIR /XD bin obj .vs` 同步到仓库。

## 2. 常用命令

```powershell
$env:DOTNET_ROOT = "D:\Documents\deepseek-harness\default-workspace\.tools\dotnet"

# 编译（Debug）
& "src\WarThunderHud\build.ps1"

# 发布到运行目录（必须先停进程，否则 MSB4018）
Get-Process WtHud -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 700
& ".tools\dotnet\dotnet.exe" publish "src\WarThunderHud\WarThunderHud.csproj" -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
  -p:DebugType=none -o "dist-wthud"

# 启动
Start-Process "dist-wthud\WtHud.exe"

# 日志
Get-Content "dist-wthud\wthud-overlay.log"

# 面板离线预览（无需游戏）
$env:WT_MFD_PANEL='en'; $env:WT_MFD_SCREEN='zh'
& "src\WarThunderHud\bin\Debug\net8.0-windows\WtHud.exe" mfdpreview "out.png" 0    # 参数 = 页索引

# 回归快照预览（视图：snapshots / snapconfirm / snaprestore）
# 注意：快照目录 = exe 同目录 temp\；dev 预览前先把 dist-wthud\temp\*-save.json 复制到
#       src\WarThunderHud\bin\Debug\net8.0-windows\temp\，否则列表为空
& "src\WarThunderHud\bin\Debug\net8.0-windows\WtHud.exe" mfdpreview "out.png" snapshots     # 快照列表
& "src\WarThunderHud\bin\Debug\net8.0-windows\WtHud.exe" mfdpreview "out.png" snapconfirm   # 二级确认
& "src\WarThunderHud\bin\Debug\net8.0-windows\WtHud.exe" mfdpreview "out.png" snaprestore   # 真跑一次恢复（写审计、删 session）
```

## 3. 当前版本与提交

- 版本号：`0.3.4`（`WarThunderHud.csproj`；0.3.3 已出包，0.3.4 修「游戏提权时热键失效 / 面板开 HUD 隐藏 / SAVE 后热键重注册失败」）
- 分支：`v0.3.0`（本地领先远端，**未 push**）；tag 本地有 `v0.3.0`（030845e）、`v0.3.1`（`98ceb9f`）、`v0.3.2`（`02f3cd7`）、`v0.3.3`（`df97024`）
- 最近提交（新→旧）：
  - 待提交：fix(hotkey): 游戏提权时自动以管理员重启 + 面板打开不隐藏 HUD + overlay 线程重建热键
  - `205dd05` docs: README v0.3.3 + English release notes
  - `df97024` release: v0.3.3 - SPLASH kill banner, PULL UP lead time, auto write-layer fix
  - `066103c` feat(hud): SPLASH 中文化（击落/SPLASH）+ 中文字距控制
  - `e85311a` fix(hud): SPLASH 框压扁
  - `45f6051` feat(hud): SPLASH 实心绿底、中心上方、无动画
  - `c1f5edc` fix(stall): F/A-18A STALL 闪烁（AOA 指示器防抖）
  - `c8560f2` feat(hud): 中心 SPLASH + PULL UP 提前量可配置 + 删除极速依据
  - `aa58e52` fix(scope): 自动写入层失效（Scoped.Set 在自动模式写全局）
  - `02f3cd7` fix(telemetry): radio_altitude 是英尺 → 换算成米（RA/PULL UP 偏高 3.28 倍）
  - `97855ca` refactor(panel): 旧设置窗体剩余项迁入 MFD，删除 SettingsForm/OpenSettings
  - `164e086` feat(keys): 恢复战雷 .blk 按键导入（按键页第 2 页）
  - `d497c1c` feat(panel): 告警页下降率（三层）+ 按键页补全可编辑键/固定只读键
  - `ff5e52b` fix(scope): 编辑页显示写入层 + 详情页显示数值来源
  - `dfa72ee` docs: HANDOFF 记录作用域修复提交
  - `69e47a4` fix(scope): 按机型覆盖真正生效 + 详情页按本页机型读值（j16 覆盖 f4j 的 bug）
  - `98ceb9f` release: v0.3.1（版本号/CHANGELOG/RELEASE_NOTES）
  - `57755f5` feat(hud): 地平线加长加粗 + 机头基准短线（v0.3.1）
  - `2e02771` refactor: Help/Detail 页迁入页面层（扫描器 8 页 CLEAN）
  - `9230b12` chore: 0 警告 — D2D/DWrite 工厂判空 + 可空性标注
  - `b033435` chore: 清剩余死代码告警 + 修 fusiontest + DIAG 复制接线（告警 28→11，仅剩可空性）
  - `5afcccb` refactor: 页面统一 Enter* 构造器 + 删确认/捕获遗留状态（扫描器 6 页 CLEAN）
  - `ca72215` docs: 确认页修复记录
  - `0b66417` fix: 二级确认页支持鼠标单击/双击，侧键取消回到来源页
  - `a0b3348` feat: 回归快照（列 temp 下 SAVE 快照 → 二次确认 → 恢复）
  - `0e0c3b3` docs: 快照回退规格
  - `827a814` fix: CANCEL 也清 session 快照
  - `955f13f` feat: 每次改动写 session 快照，SAVE 后清空
  - `c39f103` feat: CANCEL 回退到 F1 打开时的快照
  - `18f01df` fix: 机型详情 RESET 同时清统一覆盖表

## 4. 已实现的功能（稳定，别轻易动）

### 4.1 HUD 渲染与性能
- DirectComposition + D2D（跑在 D3D11 上），**不要**再加 DPI 反缩放，实测 `SetTransform(Identity)` 才对
- `Present(0, PresentFlags.DoNotWait)` 是关键优化：改成 `None` 会把帧率从 30 掉到 9–19
- `--perf` 参数打印 `build/draw/present/loop` 耗时
- 覆盖层只覆盖**游戏所在显示器**；HUD 中心 = 游戏客户区中心；`PxReferenceHeight` 用客户区高度
- **俯仰梯/地平线（2026-11-08 调整）**：
  - 地平线加长到 ±`NoseRefInnerDeg`(7.4°)、线宽 ×2（醒目）；不含端部竖线
  - 两侧**机头基准短线**固定在俯仰梯锚点高度（±7.4°…±8.8°，不随坡度旋转）：梯子移动时，短线对准的刻度 = 机头当前俯仰；平飞时短线与地平线端头相接，读作一条贯穿的长横线
  - 为保证净空，读数微调：`ACC`/`M` 外移 0.3°、`V/S` 外移 0.5°、导航 `AF/AF!` 右移 2.4°（这些值都在 `HudGeometry` 顶部常量里）
  - 预览检查：`preview out.png 1920 1080 level|pitch5|bank30`，或用环境变量 `WT_PREVIEW_ATT="pitch,bank"` 指定任意姿态；`dumpgeo w h pitch bank` 可导出该姿态的图元坐标做碰撞检查

### 4.2 参数作用域（v0.3.0 核心）
- 三层：**机型覆盖 > 全局 > 内置默认**
- 存储：`hud-config.json` 的 `perVehicleOverrides: { "f-4j": { "row.pitchSign": -1 } }`
- 注册表：`Hud/SettingScopes.cs`（**唯一来源**，未登记 = 全局）
  - 按机型 10 项：`pitchzero / pitchSign / bankTrim / fpmTrim / stallAoa / aoaWarn / overspeedIas / overspeedMach / sinkRate / pullUp`
  - 开关与点击式枚举（`rollCoupleMode`、`bankSign`）**一律全局**
- 路由：`Scoped.Get/Set`（面板行）与 `SettingsRouter.Get/Set/GetFor/SetFor`（显式机型）
- 作用域三态：`EditScope.Auto / ExplicitGlobal / UseVehicle`；`ResolvedVehicle` 决定读写层
- 面板顶栏只在**编辑页**显示当前层（`全局数据` / `机型数据：xxx`），列表页只显示生效值
- RESET：机型作用域清该机型全部覆盖并保持作用域；全局作用域恢复默认
- **运行时解析**走 `Hud/EffectiveSettings.cs`（覆盖 > 机型档案 > 全局）：`pitchzero/bankTrim/fpmTrim/
  stallAoa/aoaWarn/overspeedIas/overspeedMach/sinkRate` 已全部接入 HUD 主循环；极速阈值优先级 = 机型覆盖 >
  座舱 Vne > 档案 > 全局；下降率阈值 0 时回退 40 m/s。（v0.3.1 前这些按机型值只在面板显示，HUD 实际没读覆盖表）
- **显示某台具体机型**的页面必须用 `SettingsRouter.GetForVehicle(cfg, id, ...)`；用 `Scoped.Get` 会跟着
  面板当前编辑的机型走 → 所有详情页显示同一台的值（"j16 覆盖 f4j" 的 bug）
- **详情页显示来源**（`EffectiveSettings.Resolve` → `机型/档案/全局`）：没有按机型覆盖时不能只显示「默认」，
  全局回退必须标「（全局）」，否则用户会以为改了机型没生效（实际是改到了全局层）
- **编辑页顶部显示写入层**（`NumberEditPage`）：`写入层：机型 x / 全局（未识别机型，会影响所有机型）`，
  非按机型的行显示「全局设置（所有机型共用）」。没识别到机型时写的是全局层，会影响所有未覆盖机型
- **写入路由（`Scoped.Set`）**：非按机型行 / 显式全局 → 全局；**自动且识别到机型 → 写该机型覆盖**；
  自动且没识别到机型 → 退回全局。别再用 `EditScope.IsGlobal` 判断（`Target==null` 在自动模式也成立，
  v0.3.2 因此出现「自动也写全局、必须手动选一次」的 bug）
- `AirframeDb.StoreAlertOverrides` 已删除（旧逻辑 SAVE 时把全局值写进当前机型档案，形成第二套按机型数据）

### 4.3 面板页面层（重构进行中）
新页面（`UI/Mfd/Pages/`）：`MfdPageBase`、`MfdRenderContext`、`MfdNavigator`、`ConfirmPage`、`PitchDetectPage`、`NumberEditPage`、`PresetPage`、`PickVehiclePage`
- 新页面通过 `_nav.Push()` 进入、`Handle(MfdCommand)` 处理输入、`Draw(ctx)` 绘制
- **进入页面必须走构造/进入函数**，不要直接 `_mode = Mode.X`（会得到空白页）

### 4.4 快照
- `Hud/ConfigSnapshots.cs`，目录 `dist-wthud\temp\`
  - `_cancel.json`：每次 F1 打开覆盖，CANCEL 回退到它
  - `session\*-edit.json`：每次改动一份（上限 10），SAVE / CANCEL 后整个删除
  - `*-save.json`：每次 SAVE 一份（保留 10）
  - `*-restore.json`：回归快照恢复后的审计留痕；**不参与** 10 份裁剪、不进可恢复列表
- 序列化/反序列化用 `System.Text.Json`，回填用**反射复制属性**（`HudConfig` 没有 `CopyTo`）

### 4.5 回归快照（v0.3.0 新增）
- 入口：首页第 **9** 个模块「回归快照」（row-major 填充 → **左列第 5 行 / 侧键 4**；规格文档写的「侧键 8 右列第 4 行」与现行填充顺序冲突，侧键 8 已是「诊断」，未改菜单布局）
- 页面：`UI/Mfd/Pages/SnapshotsPage.cs`（2 列 × 5 行、PAGE 翻页、单击选中、双击/确认进确认页；空列表画 `snap.empty`）
- 后端：`ConfigSnapshots.ListRestorable()`（只列 `*-save.json`，按文件名时间倒序）+ `RestoreFrom(path, cfg)`
- 恢复流程（**不自动 SAVE**）：`ConfirmPage` 确认 → `RestoreFrom(_cfg)`（反射回填 + 删 `temp\session` + 写 `*-restore.json`）→ `_work.CopyFrom(_cfg)` 同步草稿 → 刷新 L10n/主题 → 回首页并 toast「已恢复到 xx:xx 的快照」；`perVehicleOverrides` 一起回退
- 首页模块的数量显示用 `MfdPage.LiveCount`（实时读快照数）
- 二级确认页（所有 `ConfirmPage`）：鼠标单击「确认/取消」行 = 选中，双击（或同格 400ms 内重点）= 执行；物理侧键 2/3、RESET、CANCEL、SAVE 全部走 `_confirmPage.Handle(...)`，页面回调决定回到哪一页（快照确认取消 → 回快照列表）
- 列表只在进入页面 `Build()` 时读盘：面板开着时新 SAVE 的快照要**退出重进**才看得到
- 扫描器已支持：`python tools\scan_page_migration.py Snapshots` → CLEAN（`Enter*` 方法体里的 `_mode = Mode.X` 不再误报）

### 4.6 页面构造约定（v0.3.0 重构）
- **所有二级页必须有 `Enter*` 构造入口**：`EnterConfirm / EnterEditNumber / EnterPickVehicle / EnterPreset / EnterCaptureKey / EnterDetail / EnterPitchDetect / EnterSnapshots / EnterSnapshotConfirm / EnterHelp / EnterPage`。`_mode` 只在 `Enter*` 里赋值；直接赋 `_mode` 会得到空白页
- 页面回调命名与用途：`RunPending/CancelPending`（ConfirmPage onOk/onCancel）、`CommitNumber(string)`（NumberEditPage onCommit）、`CommitPreset(item,key)`（PresetPage onApply）、`RestoreSnapshot`（快照确认 onOk）、`DetailPage.onBack/onReset`、`HelpPage.onClose`
- 扫描器约定：`_mode = Mode.X;` 只允许出现在 `Enter*` 方法体内；`LEGACY` 只登记真正的遗留旁路状态（`_confirmSel/_editBuffer/_pickSel...`），页面回调不登记
- **8 个已迁移页全部 CLEAN**：`python tools\scan_page_migration.py Confirm PitchDetect EditNumber Preset PickVehicle Snapshots Help Detail` → 全 0 risky
- 页面清单位置：`UI/Mfd/Pages/` 下 `HelpPage`（按键说明 + 结构树 + INFO 说明，页面自持分页/选中/点击）、`DetailPage`（机型能力汇总 + reset/back 回调）、`PageText`（共享换行）、`ConfirmPage/NumberEditPage/PresetPage/PickVehiclePage/PitchDetectPage/SnapshotsPage`
- **Diag 页**：本来就是数据驱动的 `MfdPage`（`MfdPages.Diag()` 的 Info/Action 行），没有模式分支，不需要迁移；**主列表 Items** 仍是 Form 的默认渲染器，属于纯结构搬运（无遗留分支），评估后暂不做，避免为架构一致性引入核心交互回归

### 4.7 其它
- 按键页共 **20 行**（2 页）：第 1 页 10 个原可编辑键；第 2 页 6 个补全的可编辑键
  （`pitchZero/bankFlip/bodyFixed/reset/holdHide/stall`）+ 3 个**只读固定组合键**
  （`CTRL+ALT+↑/↓` 上下偏移、`CTRL+ALT+PgUp/PgDn` FOV、`CTRL+ALT+R` 滚转耦合）+ 1 个
  「从战雷 (.blk) 导入按键」动作行。只读行用 `ItemKind.ReadOnly`，点按会给「只读」toast；
  标签要短（值右对齐，太长会压到标签）
- `.blk` 导入：`Input/WtControlsFile.cs`（解析 + scan code→键名）+ `Input/WtControlsImport.cs`（文件对话框 + 应用）。
  只映射 **减速板 / 计分板**（HUD 监听、且游戏内也用的两个键）；写入草稿，SAVE 才落盘。
- 旧 `UI/SettingsForm.cs` 与 `Program.OpenSettings()` **已删除**（迁完旧窗体剩余项后清理）。
  旧窗体功能对照：`地面延迟/续航系数/构型调试块` → 显示页；`极速依据(IAS/MACH)` → 告警页；
  `.blk 导入` → 按键页；其余开关/数值/键位原本 MFD 面板就有。
  唯一未保留的是「纯机体固连」复选（运行态不写配置，F9 键切换即可）
- 告警页「下降率告警」：`row.sinkRate`，单位 ` m/s`，范围 5–200，默认全局 `sinkRateMs=40`
- 告警页「PULL UP 提前量」：`row.pullUp`，单位 `s`，范围 1.5–10，默认全局 `pullUpTtiS=5.0`；
  触发条件 `TTI < 提前量`（机型覆盖 > 机型档案 `pullUpTti` > 全局）。原来硬编码 3.5 s 偏晚
- **极速告警是双路判断**（IAS 或 Mach 任一超限即触发），没有「极速依据」开关；`overspeedByMach` 字段已删除
- **SPLASH 击落播报（v0.3.3）**：`HudMsgWatcher` 4 Hz 轮询 `/hudmsg`，`HudKillFeed` 清洗并只解析「击落了」；
  自己昵称来自 `playerName`（显示页文本输入，ASCII 打字 / Ctrl+V 粘贴），命中后 `Program.UpdateSplash`
  以 2.2 s 队列播放，`HudGeometry.AddSplash` 在中心下方画大号带框 `SPLASH` + 目标名。
  注意：**只认「击落了」**（AI/地面是「击毁了」不播）；被击落不播；按 id 去重；失败静默
- **8111 的 `radio_altitude` 是英尺**（不是米）：`TelemetryParser` 解析时 ×0.3048 存成 `RadioAltM`。
  证据：同一帧 baro=91 m 时 raw=205，按米算 AGL>绝对高度不可能；205 ft=62 m 才合理。
  别改回去，否则 RA 带和 PULL UP 会偏高 3.28 倍（2026-11-09 修复）
- 俯仰方向检测：比较「游戏俯仰」与「**实际显示的估计俯仰**」（不是航迹角），相关系数 <0 → 判反向
- 中文告警框宽：ASCII 0.62em / 中文 1.0em
- 机型数据库只收飞机（`tankModels/*` 排除）
- 面板保存后 `RebindeKeys(cfg)` + `ApplyHotkeys` 让自定义键立即生效
- DIAG 页「复制诊断信息」= 把 `/state`、`/indicators`、`/map_obj`、`/map_info` 四段文本写进剪贴板（之前 `DiagState` 的委托从未被宿主接线，那一行是空操作；现在直接实现，剪贴板忙时静默忽略）
- 自检：`WtHud.exe fusiontest` 三个姿态融合场景全 PASS（顺手修了两个陈旧场景：测试快照没置 `HasHeading` 导致转弯场景恒 0；以及早已删除的 native bias 学习场景）
- 编译现在是 **0 error / 0 warning**（Debug Rebuild 实测）。图形相关注意（别改回原样）：
  - `Graphics/HudCanvas.cs` / `Overlay/OverlayWindow.cs` 的 `D2D1CreateFactory` / `DWriteCreateFactory` 的 out 参数**按可空接收后立即判空抛异常**（Vortice 标注为可空，直接信任会吃 CS8600/NRE）
  - `HudDrawer.CreateTextFormat(..., null!, ...)`：null = 系统字体集合，Vortice 把它标成非空，只能 `null!`

## 5. 未完成的工作（按优先级）

### A. 其它待办
- **v0.3.2 发布材料已备好**（`CHANGELOG.md` v0.3.2 段、`RELEASE_NOTES_v0.3.2.md`、`WtHud-v0.3.2-win-x64.zip` + SHA256 在 `C:\Users\noapi\Desktop\WtHud-发布\`），**由用户手动发布 release（不代推）**；本地 tag `v0.3.2` 已打、远端未推。zip SHA256 见 `SHA256.txt`
- **击落播报（SPLASH）已实现（v0.3.3，未发布）**：见 `docs/killfeed-splash-plan.md`。真实对局数据已抓
  （213 条 / 59 次击杀），确认：只认「击落了」；`^DFCN^` 是联队/颜色标记（因人而异，不能当身份）；
  AI/地面用「击毁了」；API 会重复推送同 id；目标名可含空格/符号/`š`。实现：`Telemetry/HudKillFeed.cs` +
  `Telemetry/HudMsgWatcher.cs` + 显示页「击落播报 / 玩家昵称」。用户已定：**不播报被击落、不播 AI**
- 回归快照后续扩展：把 `temp\session\*.json`（标注 `edit`）也列出来，支持回退到本次会话中间状态（规格已预留）
- 编译告警已清零（图形判空注意事项见 §4.7）；后续改动不要再引入 CS0169/CS0414/CS0162/CS0108 或可空性告警
- 页面层待定项：`MfdPageBase.Id/HintKey`、`MfdPageBase.Buttons` 目前零外部读取（迁移脚手架），确认后再决定删留
- 主列表 Items 若将来要迁页面层（纯结构搬运），见 §4.6 的评估：先做交互回归清单（UP/DOWN/CONFIRM/侧键/翻页/鼠标双击/编辑页入口），再动手

## 6. 踩过的坑（务必避免重复）

| 现象 | 原因 | 对策 |
|---|---|---|
| 面板空白页 | 直接 `_mode = Mode.X` 绕过构造 | 用进入函数 / `EnterConfirm()` |
| 单击无反应、双击才有 | 旧字段与页面状态双写 | 用扫描器找遗留分支，统一改成页面调用 |
| 文案空白 | 新页面用了 L10n 里不存在的 key | 先加 key 再写页面 |
| publish 报 MSB4018 | HUD 进程占用 exe | 先 `Stop-Process` |
| 覆盖层偏移/缩放错 | 给 DComp visual 加了 1/dpi 缩放 | 保持 `Identity` |
| 帧率只有 9-19 | `Present` 阻塞 | `PresentFlags.DoNotWait` |
| 菜单 CONFIRM 永远进第一项 | 路由器懒创建，Move 时还是 null | `EnsureMenuPage()` 后再 `Move` |
| 预设改了不生效 | 只写 `_work` 没写 `_cfg` | 提交时同时应用到运行配置 |
| robocopy 报错 | 用了不存在的 `/NJL` 参数 | 只用 `/NFL /NDL /NJH /NJS /NP` |
| 快照迁移覆盖新值 | “新值优先”规则把刚改的值覆盖 | 迁移前确认，或改完再迁移 |
| 回归快照列表为空 | 快照目录 = **exe 同目录** `temp\`；dev 预览跑的是 `bin\Debug\...`，那里没有 `*-save.json` | 预览前把 `dist-wthud\temp\*-save.json` 复制到 `bin\Debug\net8.0-windows\temp\` |
| 恢复后历史快照变少 | `*-restore.json` 审计文件被 `SaveHistory` 的 10 份裁剪算进去 | 已在 `History()/SaveHistory` 排除 `-restore.json` |
| 扫描器把 `Enter*` 入口判成 risky | 迁移后的页面构造入口也写 `_mode = Mode.X` | 扫描器已豁免 `Enter*` 方法体内的赋值 |
| 二级确认页鼠标点了没反应 | `Mode.Confirm` 没接鼠标命中（只有侧键/键盘） | 用 `ConfirmRowHitTest`/`TryClickConfirmRow`：单击选中、双击执行 |
| 确认页按「取消」侧键后空白页 | 侧键直接调 `CancelPending()`，绕过页面回调 → `_mode = Items`（快照模块 0 行） | 确认页所有输入统一走 `_confirmPage.Handle(...)`，由页面回调决定返回哪一页 |
| 确认页高亮在「取消」但 CONFIRM 仍是确认 | `Confirm()` 固定发 `confirm.ok` | 已改为跟随 `_confirmPage.Selected` |
| 扫描器把页面回调报成 legacy | LEGACY 名单里混入了 `RunPending/CancelPending/ApplyPreset/CommitNumber` | 名单只登记遗留**状态字段/旧绘制**；回调是页面层本身 |
| 删「零引用」成员后编译失败 | 方法组（`onOk: CancelPending`）grep 看不到调用 | 删前同时找 `onOk/onCancel/onApply/onCommit/onPicked` 实参位置 |

## 7. 验证清单（改完面板必跑）

1. F1 能开能关
2. 首页 UP/DOWN + CONFIRM 进入对应模块
3. 数值行 → 编辑页 → 输入/退格/三选项/ESC
4. 颜色预设 CONFIRM 立即换色、CANCEL 不空白
5. 编辑页「手动选择机型」→ 列表首项「全局」→ 切换后顶栏与数值一致
6. 机型详情数值 = 生效值；RESET 清空该机型覆盖
7. CANCEL 后 `temp\session` 消失、值回到打开时
8. `python tools\scan_page_migration.py ...` 命中数不增加
9. 首页第 9 模块「回归快照」→ 列出 `temp` 下 SAVE 快照（倒序、形如 `11-08 21:37  save`、不含 `_cancel.json`）
10. 快照页：UP/DOWN 选中、PAGE 翻页、单击选中、双击/CONFIRM 进确认页（文案含时间）
11. 确认页（所有二级确认）：鼠标单击某行 = 选中高亮、双击 = 执行；物理侧键 2/3 也执行；侧键「取消」/BACK 回到来源页（快照确认回到快照列表，不再是空白页）
12. 确认恢复 → 回首页 toast「已恢复到 xx:xx 的快照」；`temp\session` 消失、多一份 `*-restore.json`；**没有自动 SAVE**（面板仍开着，值已是快照值）
13. `python tools\scan_page_migration.py Snapshots` → verdict CLEAN
14. 扫描器 6 页全清：`python tools\scan_page_migration.py Confirm PitchDetect EditNumber Preset PickVehicle Snapshots` → 6 × CLEAN
15. 页面构造入口没被绕过：`grep "_mode = Mode\." MfdFrameForm.cs` → 全部落在 `Enter*` 方法体内
16. 预览回归（`mfdpreview` 视图）：`menu/help/preset/edit/capture/confirm/detail/pickvehicle/pitch/detailreset/snapshots/snapconfirm/snapback/snapclick` 都能渲染出内容（无空白页）
17. 数值提交链路：编辑页输入新值 → CONFIRM → 回到模块页数值已变（`editcommit` 视图等价）
18. 选机型链路：编辑页「手动选择机型」→ 选中飞机 → 回到编辑页顶栏变「机型数据：xxx」（`pickpicked`）；返回则为原编辑页（`pickback`）
19. 色板应用：选「红色」→ CONFIRM → 面板立即整体变色（`presetapply` 视图等价）
20. 自检：`dist-wthud\WtHud.exe fusiontest` → 3 场景全 PASS、exit 0
21. 编译告警体检：`dotnet build -t:Rebuild` → **0 error / 0 warning**（CS0169/CS0414/CS0162/CS0649/CS0108 与可空性告警都应清零）
22. HELP 页：HELP 打开/再按关闭回到原页面；PAGE 翻到 6/6、UP/DOWN 选中跟随；点击行选中（`help/help3/helpback` 视图）；help 渲染必须与迁移前逐字节一致
23. 机型详情：进入显示汇总、RESET 弹「重置该机型到默认？」、取消回详情、确认后回详情且数值更新；BACK 回机型列表（`detail/detailreset/detailback` 视图）
24. 扫描器 8 页全清：`python tools\scan_page_migration.py Confirm PitchDetect EditNumber Preset PickVehicle Snapshots Help Detail` → 8 × CLEAN
25. 作用域自检：`WtHud.exe scopetest` → 38 项全 PASS、exit 0；`mfdpreview out.png detail f-4j` 与 `detail j\_16` 的 IAS/Mach 必须各显示各的（不能两台一样）
26. 编辑页写入层：`mfdpreview out.png editscoped` → 未识别机型时必须是琥珀色「写入层：全局（未识别机型，会影响所有机型）」；`mfdpreview out.png detail a_10a` 的 IAS/Mach 应显示「（全局）」而不是「默认」
27. 告警页：`mfdpreview out.png alerts` → 有「下降率告警 40 m/s」「PULL UP 提前量 5.0 s」，**没有**「极速依据」；改成机型覆盖后按机型生效（日志 `SINK RATE ... thr=` / `PULL UP ... ralt=` 与之一致）
28. 按键页两页：`mfdpreview out.png keys`（1/2）与 `keys2`（2/2）→ 第 2 页 6 个新增可编辑键（F7/F8/F9/F5/U/CTRL+ALT+S）+ 3 个固定只读键（CTRL+ALT+↑↓ / PgUp/PgDn / R）+ 「从战雷 (.blk) 导入按键」动作行，固定行标签不能被右侧键位压住
29. .blk 导入：`WtHud.exe selftest` → 3 项 blk 检查 PASS；进面板按键页第 2 页点导入 → 选一个 war thunder controls .blk → toast 显示导入结果，SAVE 后 `hud-config.json` 的 `airbrakeKey`/`scoreboardKey` 更新
30. 旧窗体迁移项：`mfdpreview out.png display`（2 页）→ 有 `地面延迟 ms` / `续航系数` / `构型调试块`；`alerts` → 有 `极速依据 IAS/MACH`；`diag` 第 2 页仍有「检查更新」。`grep SettingsForm` 无残留、`OpenSettings` 无残留
31. 雷达高度单位：`WtHud.exe selftest` → 2 项 `radio altitude` PASS（1000 ft → 304.8 m）；进游戏低空看 RA 带 ≈ 座舱英尺读数 × 0.3048，PULL UP 不再偏晚
32. SPLASH：`WtHud.exe selftest` → 6 项 killfeed 检查 PASS；`preview out.png 1920 1080 splash` 出图有中心 SPLASH 框+目标名；`mfdpreview out.png display2` 有「击落播报 / 玩家昵称」两行；进对局击落玩家 → 1.2 s 播报，AI/地面/被击落不播
33. PULL UP 提前量：告警页改成 6 s → 日志 `PULL UP ON: ralt=... tti=...` 出现更早（tti 门限 6）；机型覆盖后详情/生效值一致；`overspeedByMach` 不再存在于 `hud-config.json` 新存档
34. 提权兼容（v0.3.4）：游戏以管理员运行时启动 WtHud → 日志出现 `game is elevated but WtHud is not -> restarting WtHud as administrator`，UAC 确认后新进程起来；游戏内 F1/F10/F9 直接可用（反馈者已实测管理员运行后 F1/F10 正常）
35. 面板不隐藏 HUD（v0.3.4）：F1 打开面板 → HUD 仍然显示；关闭面板后正常
36. SAVE 后热键（v0.3.4）：F1 → 改一个键 → SAVE → 日志不再出现 `hotkey N ... could not be registered`；新键立即生效（无需重启）；`err=` 字段只在真冲突时出现

## 8. 协作约定

- 每完成一小步：编译 → 发布 → 启动 → （必要时预览）→ `robocopy` 同步 → `git commit` → `git push`
- 不确认行为前不要大删代码；删除前先确认“只剩定义、无调用”
- 用户偏好中文回复、简洁结论、少问多验证
