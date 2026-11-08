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
```

## 3. 当前版本与提交

- 版本号：`0.3.0`（`WarThunderHud.csproj`）
- 分支：`v0.3.0`（本地与远端一致），tag `v0.3.0-wip`
- 最近提交（新→旧）：
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
- 序列化/反序列化用 `System.Text.Json`，回填用**反射复制属性**（`HudConfig` 没有 `CopyTo`）

### 4.5 其它
- 俯仰方向检测：比较「游戏俯仰」与「**实际显示的估计俯仰**」（不是航迹角），相关系数 <0 → 判反向
- 中文告警框宽：ASCII 0.62em / 中文 1.0em
- 机型数据库只收飞机（`tankModels/*` 排除）
- 面板保存后 `RebindeKeys(cfg)` + `ApplyHotkeys` 让自定义键立即生效

## 5. 未完成的工作（按优先级）

### A. 回归快照（规格已写：`docs/snapshot-restore-spec.md`）
1. `ConfigSnapshots.ListRestorable()`（列 `temp\*.json`，排除 `_cancel.json`）+ `RestoreFrom(path, cfg)`
2. 新页面 `SnapshotsPage`（参考 `PickVehiclePage`：2×5 网格、翻页、单击选中、双击确认）
3. 首页模块 +1「回归快照」（现 8 个，容量 10）
4. `MfdFrameForm` 增加 `Mode.Snapshots` 的绘制/确认/返回/移动/翻页/单元格点击
5. 二级确认复用 `ConfirmPage`；确认后恢复 `_cfg`+`_work`，删除 `temp\session`，**不自动 SAVE**

### B. 死代码清理到 CLEAN
工具：`python tools\scan_page_migration.py Confirm PitchDetect EditNumber Preset PickVehicle`
当前命中：PickVehicle **1**、Preset **4**、EditNumber **14**、Confirm **22**、PitchDetect **1**
剩余是**仍被使用**的旧字段与直接跳转，需要逐项确认调用方：
- `_confirmSel`：`DrawDecisionRows`（L~775）仍读它，调用点 L~422 需确认归属
- `_mode = Mode.X` 直接赋值若干处 → 换成进入函数
- 已删：`_pickSel/_pickReturn/_pickPage/_editBuffer/_presetSel/_presetTarget` + 5 个死函数

### C. 其它待办
- v0.3.0 changelog + 打包发布（尚未做；用户此前说先不发）
- 面板剩余页面迁移：Help / Detail / Diag / Items 主列表

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

## 7. 验证清单（改完面板必跑）

1. F1 能开能关
2. 首页 UP/DOWN + CONFIRM 进入对应模块
3. 数值行 → 编辑页 → 输入/退格/三选项/ESC
4. 颜色预设 CONFIRM 立即换色、CANCEL 不空白
5. 编辑页「手动选择机型」→ 列表首项「全局」→ 切换后顶栏与数值一致
6. 机型详情数值 = 生效值；RESET 清空该机型覆盖
7. CANCEL 后 `temp\session` 消失、值回到打开时
8. `python tools\scan_page_migration.py ...` 命中数不增加

## 8. 协作约定

- 每完成一小步：编译 → 发布 → 启动 → （必要时预览）→ `robocopy` 同步 → `git commit` → `git push`
- 不确认行为前不要大删代码；删除前先确认“只剩定义、无调用”
- 用户偏好中文回复、简洁结论、少问多验证
