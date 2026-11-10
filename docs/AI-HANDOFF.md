# WtHud 项目 AI 交接文档

> 给接管这个项目的下一个 AI（DeepSeek / WorkBuddy）：
> 你是接替上一个会话继续开发 War Thunder 第三人称 HUD（WtHud）的工程代理。
> **先读完本文件，再按需读 `docs/HANDOFF.md`（更深的工程细节与历史决策）。**
> 用户母语中文，回复用中文；风格：简短、少问、用真实证据（日志/截图/构建输出）说话。

---

## 1. 项目一句话

WtHud = 战争雷霆（War Thunder）**第三人称 HUD 叠加层**：只读 8111 本地 HTTP 接口，
用 DirectComposition + Direct2D 画一层透明 HUD（俯仰梯、速度/高度带、F1 设置面板、告警、击杀播报）。
不注入、不读内存、不显示敌人位置。C# / .NET 8 / WinForms + Vortice。

## 2. 工作区与仓库（绝对路径）

| 用途 | 路径 |
|---|---|
| 工作区根 | `D:\Documents\deepseek-harness\default-workspace` |
| **源码（唯一编辑入口）** | `...\src\WarThunderHud\` |
| 本地 git 仓库（镜像） | `...\wt-hud\`（含 README/CHANGELOG/docs/RELEASE_NOTES） |
| 便携 .NET 8 SDK | `...\.tools\dotnet\dotnet.exe` |
| 运行/测试目录（用户实际在跑） | `...\dist-wthud\` |
| 发布材料（用户手动上传） | `C:\Users\noapi\Desktop\WtHud-发布\` |
| 封面/临时脚本 | `...\.diag\` |

**同步规则（每次改完源码必须做，否则 git 里不是最新）：**
```powershell
cd D:\Documents\deepseek-harness\default-workspace
Copy-Item "wt-hud\docs\HANDOFF.md" "src\WarThunderHud\HANDOFF.md" -Force   # 若改了 HANDOFF
robocopy "src\WarThunderHud" "wt-hud\src\WarThunderHud" /MIR /XD bin obj .vs /XF HANDOFF.md /NFL /NDL /NJH /NJS /NP
```
`src\WarThunderHud` 是编辑区，`wt-hud\src\WarThunderHud` 是 git 里的副本；`HANDOFF.md` 不镜像进 git 的 src。

## 3. 环境怪癖（踩过的坑，别重复）

- 系统只有 **Windows PowerShell 5.1**，没有 `pwsh`。要跑脚本文件用 `powershell -File xxx.ps1`。
- **不支持 heredoc**（`<<'PY'` 会解析失败）；写临时脚本用文件工具生成 `.py` / `.ps1` 再执行。
- PS 5.1 解析非 ASCII 脚本需要 **BOM**；临时脚本尽量纯 ASCII。
- 控制台显示中文会乱码，**读文件用 read 工具**，不要信 `Get-Content` 的屏幕输出。
- 构建：
  ```powershell
  cd D:\Documents\deepseek-harness\default-workspace
  $env:DOTNET_ROOT="$PWD\.tools\dotnet"
  & "src\WarThunderHud\build.ps1"
  ```
  产物：`src\WarThunderHud\bin\Debug\net8.0-windows\WtHud.exe`
- 单文件发布（Release）：
  ```powershell
  & ".tools\dotnet\dotnet.exe" publish "src\WarThunderHud\WarThunderHud.csproj" -c Release -r win-x64 `
    --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true -p:DebugType=none -o "dist-wthud"
  ```
- 每次 publish 前先 `Get-Process WtHud | Stop-Process -Force`（文件占用）。
- 中文文本文件（使用说明.txt）必须 **UTF-8 with BOM**；zip 用 `[System.IO.Compression.ZipFile]::CreateFromDirectory`。

## 4. 当前状态快照（2026-11-10 晚）

- 版本：`0.3.4`（`src\WarThunderHud\WarThunderHud.csproj` 的 `<Version>`）
- 分支 `v0.3.0`，**本地领先 origin 25 个提交，未 push**（约定：只本地提交，用户手动发布）
- 最近提交：`c24a5af fix(hotkey): auto-elevate when game is elevated; keep HUD visible with panel; rebind hotkeys on overlay thread`
- 本地 tag：`v0.3.0 / v0.3.1 / v0.3.2 / v0.3.3`（0.3.4 还没打 tag）
- 桌面发布目录：`WtHud-v0.3.4-win-x64-test.zip`（用户给反馈者的测试包，可能被用户改名）、
  `WtHud-v0.3.3-win-x64.zip`、`RELEASE_NOTES_v0.3.3/0.3.4`（中英）、`SHA256.txt`
- HUD 当前**没有在跑**（写完交接时 pid 已退出）；`dist-wthud\WtHud.exe` 就是 0.3.4
- 0.3.3 已出包（是否已发布/推远端以用户说的为准）；0.3.4 是修 bug 的版本，用户正在让反馈者测试

## 5. 立即要处理（P0）：反馈者的 F10 问题

背景：反馈者游戏以**管理员**运行、WtHud 不是 → Windows UIPI 让游戏内全局热键全部失效（F1/F10/F9…）。
0.3.4 已修：启动时比较两者权限，游戏提权而 WtHud 没有 → **自动以管理员身份重启自己**（一次 UAC）。
反馈结果：**"其他功能都正常了，但是 F10 出了问题"**，F5–F7 未知。

**还没拿到 0.3.4 的日志，先不要改代码。** 需要反馈者提供：
1. 0.3.4 这次运行的 `dist-wthud\wthud-overlay.log`
2. 他的 `hud-config.json`（键位字段）
3. F10 具体现象（没反应？隐藏后回不来？按了变别的？）

日志判断方法：

| 现象 | 判断 |
|---|---|
| `overlay start` 出现 ≥2 次 | **双实例**（自动提权重启与旧进程没退干净 / 手动开两次）→ F10 切换了不可见的那个；最可疑 |
| `hotkey 1 (F10) ... err=1409` | F10 被别的程序/另一实例占用，注册失败 |
| 有 `F10 -> HUD on/off` 但画面没反应 | 热键收到了 → 渲染/双实例问题 |
| 完全没有 `F10 -> HUD on/off` | 热键没投递/被吃 |
| 没有 `game is elevated but WtHud is not` 但热键正常 | 用户本来就是管理员运行，或没触发自动提权 |

另一个高概率原因：**0.3.4 修了「面板 SAVE 后热键重注册失败」**，所以用户以前在面板里改过、但一直没生效的键位，
这一版开始真正生效了。他早期日志有 `score=TAB`（改过键位），若他把「显示/隐藏 HUD」从 F10 改成别的键并 SAVE 过，
那 F10 现在本来就不归 HUD 管——不是 bug。**查 `hud-config.json` 的 `showKey` / `resetKey` / `pitchZeroKey` / `bankFlipKey`。**

0.3.4 每次热键触发都会写日志，可直接验证 F5–F7：
- F10 → `F10 -> HUD on/off`
- F9 → `F9 -> body-fixed mode ...`
- F7 → `F7  -> pitch zero set to ...`
- F8 → `F8  -> bank sign = ...`
- F11 → `F11 pressed once ...`
- F5 → `F5 -> view state + map state reset`（F5 是轮询键，不是 RegisterHotKey）
- F1 → 面板打开（目前没有日志，可顺手补一行）

## 6. 常用验证命令

在 `src\WarThunderHud\bin\Debug\net8.0-windows\` 下（或 `dist-wthud\WtHud.exe`）：

```powershell
WtHud.exe selftest                          # 单位/解析/键位导入/killfeed，全 PASS 才 exit 0
WtHud.exe scopetest                         # 按机型作用域 38 项
WtHud.exe fusiontest                        # 姿态融合
WtHud.exe preview out.png 1920 1080 splash  # HUD 预览（另有 level/pitch5/bank30/aoa/stall/...）
WtHud.exe mfdpreview out.png display2       # 面板预览（display/alerts/keys/keys2/detail <id>/...）
```
`mfdpreview` 的路径必须是**绝对路径**；`WT_MFD_PANEL=en|zh`、`WT_MFD_SCREEN=zh` 可覆盖语言。
`preview` 的 `splash` 变体需要 `WT_MFD_ALERTS=zh` 才出中文「击落」。

## 7. 架构地图（先看这几个文件）

| 文件 | 职责 |
|---|---|
| `Program.cs`（~2100 行） | 入口/命令分发、渲染主循环、F1 面板线程、热键回调、遥测接线、预览命令、selftest/scopetest |
| `Hud\HudGeometry.cs` | **纯数学**：`HudGeometry.Build(snap, att, w, h) → List<HudPrim>`；含 `HudOptions` 全部绘制开关 |
| `Hud\HudConfig.cs` | `hud-config.json` 所有字段（版本升级时注意兼容） |
| `Hud\EffectiveSettings.cs` | 按机型解析：机型覆盖 > 机型档案 > 全局；`Resolve()` 返回来源 |
| `Hud\SettingScopes.cs` | 哪些行可「按机型」存（10 项）+ `Scoped.Get/Set` 写入层路由 |
| `Hud\SettingsRouter.cs` | `GetForVehicle` / `SetFor`，面板与运行态共用 |
| `Hud\AirframeDb.cs` | `airframes.json`：每机型档（stallAoa/aoaWarn/overspeed/sinkRate/pullUpTti/雷达高度…） |
| `Telemetry\TelemetryParser.cs` | 8111 `/state` `/indicators` 解析（**radio_altitude 是英尺 → ×0.3048**） |
| `Telemetry\HudKillFeed.cs` | SPLASH：清洗 `^联队标记^`/零宽字符，只解析「击落了」 |
| `Telemetry\HudMsgWatcher.cs` | 4 Hz 轮询 `/hudmsg`、按 id 去重、新对局 resync、失败静默 |
| `Overlay\OverlayWindow.cs` | D3D11/DComp 透明置顶窗、**RegisterHotKey 注册**、`PumpMessages`、光标洞 |
| `Overlay\NativeMethods.cs` | 全部 P/Invoke（热键、窗口、进程 token/提权判断） |
| `Graphics\HudDrawer.cs` | HudPrim → D2D 绘制（Polyline 只描边，不填充！实心块用扫描线） |
| `UI\Mfd\MfdFrameForm.cs` | F1 面板窗体（自绘 + 侧键/鼠标/键盘、模式状态机） |
| `UI\Mfd\MfdPages.cs` | 所有页面/行定义（`ItemKind`：Toggle/Number/Enum/Action/KeyBinding/Text/Detail/ReadOnly） |
| `UI\Mfd\Pages\*.cs` | 二级页：NumberEditPage / TextEditPage / DetailPage / PickVehiclePage… |
| `UI\L10n.cs` | 面板/屏幕/告警三套语言；新行/新提示都要加 key |
| `build.ps1` | Debug 构建 |

数据流：`8111 HTTP → TelemetryClient → TelemetryParser → TelemetrySnapshot → AttitudeFusion → HudGeometry.Build → HudDrawer(D2D) → DComp 窗口`。

## 8. 关键约定 / 不能踩的坑

1. **radio_altitude 必须保持英尺→米换算**（×0.3048）。改回原始值会让 RA 带和 PULL UP 偏高 3.28 倍。
2. 按机型写入层：**不要用 `EditScope.IsGlobal` 判断**（自动模式下 `Target==null` 恒为 true）；
   用 `ExplicitGlobal` + `ResolvedVehicle`。规则：非按机型行/显式全局→全局；自动+识别到机型→该机型覆盖；自动+没机型→全局（琥珀提示）。
3. **热键是线程绑定的**：`UnregisterHotKey/RegisterHotKey` 必须在创建窗口的 overlay 线程执行；
   面板 SAVE 后走 `pendingHotkeyRefresh` 标志回主循环重建，不要直接在面板线程调 `ApplyHotkeys`。
4. 游戏以管理员运行 → UIPI 会让非管理员 WtHud 的热键在游戏前台失效。
   0.3.4 起启动时检测并自动提权重启（`--elevated-retry` 防循环）；API：`NativeMethods.OpenProcess/OpenProcessToken/GetTokenInformation`。
5. SPLASH 只认 `/hudmsg` 里「**击落了**」且 actor == 配置的 `playerName`；AI/地面用「击毁了」，不播；被击落不播。
6. `^DFCN^` 这类是**联队/颜色标记、因人而异**，不能当身份标识。
7. 8111 `/hudmsg` **重复推送同一条消息**（同 id、time 递增）→ 必须按 id 去重。
8. HUD 文字绘制：`AddBoxedText` 是描边框；实心填充不可用（`FigureBegin.Hollow`），要实心用横线扫描。
9. `hud-config.json` / `airframes.json` 都支持热改；不要改字段名（会破坏用户配置兼容）。
10. 面板打开期间 `settingsOpen=true`：`wantVisible = foreground || settingsOpen || !cfg.HideWhenInactive`（否则 HUD 会被隐藏）。

## 9. 项目工作流（用户习惯）

1. 改 `src\WarThunderHud` → build → selftest/scopetest → 必要时预览出图（`read_image` 检查）。
2. 需要看运行效果：publish 到 `dist-wthud` → 重启 `WtHud.exe` → 读 `dist-wthud\wthud-overlay.log`。
3. 用户手动验证（他经常一边打游戏一边反馈）。
4. 同步 `wt-hud` → 本地 `git commit`（**不 push**，除非用户明确说）。
5. 发布由用户手动做：桌面 `WtHud-发布\` 里放 zip + `RELEASE_NOTES_vX.md`(.en) + `SHA256.txt` + 封面（需要时）。
6. 版本号：`<Version>` 在 csproj；CHANGELOG 顶部 `## 未发布（vX）`，发布时改成 `## vX — 日期`。
7. 用户偏好：**先定位原因再改代码**；能确认的 bug 一次修干净；改动后必须给验证命令与预期结果。

## 10. 文档索引

| 文件 | 内容 |
|---|---|
| `wt-hud\docs\HANDOFF.md` | **详细工程交接**：目录结构、逐模块说明、历史决策、验收清单、常见故障 |
| `wt-hud\README.md` | 中英双语功能/快捷键/配置表（0.3.3 已更新，0.3.4 的提权/面板修复还没写进去） |
| `wt-hud\CHANGELOG.md` | 版本历史；顶部有 `## 未发布（v0.3.4）` |
| `wt-hud\docs\settings-scope.md` | 按机型参数表（`python tools\gen_scope_doc.py` 重新生成） |
| `wt-hud\docs\killfeed-splash-plan.md` | SPLASH 设计与数据事实 |
| `wt-hud\RELEASE_NOTES_v0.3.4.md/.en.md` | 0.3.4 发布正文（已写好） |

## 11. 下一步待办（按优先级）

1. **P0 定位 F10 反馈**（见第 5 节）：要日志 + config + 现象；先别改代码。
2. 0.3.4 定稿：README 补 0.3.4 的「自动提权 / 面板不隐藏 HUD / SAVE 后热键」说明；使用说明已含。
3. 0.3.4 发布时：`git tag v0.3.4`，桌面 zip 改名 `WtHud-v0.3.4-win-x64.zip`，更新 SHA256，必要时加封面。
4. 若反馈者拒绝 UAC：可考虑给设置键/显示键加 `GetAsyncKeyState` 轮询兜底（游戏也会收到该键，需权衡）。
5. 可选：给 F1 面板打开/关闭补日志；给 F5–F7 补「日志验证」说明。
6. 可选优化（用户提过但未定）：SPLASH 时长/位置微调、PULL UP 默认提前量、极速双路阈值。

## 12. 合规红线

- 只读 8111 本地接口；**不注入、不读内存、不绘制敌人位置**（README/合规说明有写）。
- 不要加入任何自动化游戏操作（自瞄/按键宏等）。
- 改动热键/提权逻辑时注意别把 Windows 的 UAC 搞成无限循环（`--elevated-retry`）。

---

**接手第一步建议**：读本文件 → `git log --oneline -5` → 读 `docs/HANDOFF.md` 第 3/4 节 → 看 `dist-wthud\wthud-overlay.log` 最后 30 行 →
然后向用户要反馈者的 0.3.4 日志和 config，按第 5 节判断表定位 F10。
