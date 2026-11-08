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

- 版本号：`0.3.0`（`WarThunderHud.csproj`）
- 分支：`v0.3.0`（本地与远端一致），tag `v0.3.0-wip`
- 最近提交（新→旧）：
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

### 4.2 参数作用域（v0.3.0 核心）
- 三层：**机型覆盖 > 全局 > 内置默认**
- 存储：`hud-config.json` 的 `perVehicleOverrides: { "f-4j": { "row.pitchSign": -1 } }`
- 注册表：`Hud/SettingScopes.cs`（**唯一来源**，未登记 = 全局）
  - 按机型 8 项：`pitchzero / pitchSign / bankTrim / fpmTrim / stallAoa / aoaWarn / overspeedIas / overspeedMach`
  - 开关与点击式枚举（`rollCoupleMode`、`bankSign`）**一律全局**
- 路由：`Scoped.Get/Set`（面板行）与 `SettingsRouter.Get/Set/GetFor/SetFor`（显式机型）
- 作用域三态：`EditScope.Auto / ExplicitGlobal / UseVehicle`；`ResolvedVehicle` 决定读写层
- 面板顶栏只在**编辑页**显示当前层（`全局数据` / `机型数据：xxx`），列表页只显示生效值
- RESET：机型作用域清该机型全部覆盖并保持作用域；全局作用域恢复默认

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
- 页面回调命名与用途：`RunPending/CancelPending`（ConfirmPage onOk/onCancel）、`CommitNumber(string)`（NumberEditPage onCommit）、`CommitPreset(item,key)`（PresetPage onApply）、`RestoreSnapshot`（快照确认 onOk）
- 扫描器约定：`_mode = Mode.X;` 只允许出现在 `Enter*` 方法体内；`LEGACY` 只登记真正的遗留旁路状态（`_confirmSel/_editBuffer/_pickSel...`），页面回调不登记
- **6 个已迁移页全部 CLEAN**：`python tools\scan_page_migration.py Confirm PitchDetect EditNumber Preset PickVehicle Snapshots` → 全 0 risky

### 4.7 其它
- 俯仰方向检测：比较「游戏俯仰」与「**实际显示的估计俯仰**」（不是航迹角），相关系数 <0 → 判反向
- 中文告警框宽：ASCII 0.62em / 中文 1.0em
- 机型数据库只收飞机（`tankModels/*` 排除）
- 面板保存后 `RebindeKeys(cfg)` + `ApplyHotkeys` 让自定义键立即生效

## 5. 未完成的工作（按优先级）

### A. 其它待办
- v0.3.0 changelog + 打包发布（尚未做；用户此前说先不发）
- 面板剩余页面迁移：Help / Detail / Diag / 主列表 Items（这几页还没进页面层，扫描器未覆盖）
- 回归快照后续扩展：把 `temp\session\*.json`（标注 `edit`）也列出来，支持回退到本次会话中间状态（规格已预留）
- 已知遗留告警（面板外，未动）：`Program.cs` 4 个 CS0169 私有字段 + CS0162 不可达代码；`AttitudeFusion.cs` CS0169×5 + CS0414×2；`MfdPages.cs` 里 `DiagState` 两个 CS0649 字段；`MfdFrameForm.Move(int)` 遮蔽 `Control.Move` 事件的 CS0108（改名可消，但涉及多处调用，暂留）
- 页面层待定项：`MfdPageBase.Id/HintKey`、`MfdPageBase.Buttons` 目前零外部读取（迁移脚手架），确认后再决定删留

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

## 8. 协作约定

- 每完成一小步：编译 → 发布 → 启动 → （必要时预览）→ `robocopy` 同步 → `git commit` → `git push`
- 不确认行为前不要大删代码；删除前先确认“只剩定义、无调用”
- 用户偏好中文回复、简洁结论、少问多验证
