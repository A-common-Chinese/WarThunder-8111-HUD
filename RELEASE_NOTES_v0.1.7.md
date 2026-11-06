# 战雷 8111 第三人称 HUD · v0.1.7

本次是一次大版本更新：设置面板重做成 **MFD（实体按键）风格**，并加入多语言、诊断与更新检查。

## 新增

### MFD 设置面板（F1）
- 机框与 25 个实体按键来自 Blender 建模渲染，非平面贴图
- 三级结构：主菜单 → 模块页（2 列 × 5 行）→ 二级页（数值输入 / 键位捕获 / 确认）
- 物理按键与屏幕行一一对应：**侧键 = 选择，CONFIRM = 执行**
- 鼠标可直接点选屏幕行（双击 = 确认）
- 数值项：CONFIRM 进入输入页，可直接键入数字
- 键位项：CONFIRM 进入捕获页，**按住组合键实时显示**（按住 Ctrl 显示 CTRL，再按 Q 变 CTRL+Q，松手冻结，再按重新开始）
- 执行类操作（重置 / 导入 / 检查更新等）需二次确认
- 有未保存改动时，CANCEL 会二次确认；SAVE 直接保存
- PAGE 仅在本模块内翻页（不再跳模块），顶栏显示页码
- HELP 浮层、未保存 `*` 标记
- 面板按屏幕尺寸自适应缩放（1080p 自动缩到约 92%）
- **F1 可开关**：再按一次 F1 关闭面板（走 CANCEL 流程），不会叠出多个窗口
- 机框轮廓做了窗口裁剪，四周不再有黑底

### 三作用域语言
- 外板丝印 / 屏幕内容 / HUD 告警可**分别**设置中英文
- 默认：外板英文、屏幕中文、告警英文
- 支持跟随系统语言

### 告警本地化
- `STALL → 失速`、`PULL UP → 拉起`、`SINK RATE → 下降率`、`REDUCE SPEED → 减速`、`RETURN → 返回战区`、`AF → 机场`、`AF! → ！机场`、`BINGO → 低油量`、`AOA → 迎角`
- 告警框宽度按**当前语言文本实测宽度**自动重算，中英文都不溢出

### 诊断页（DIAG）
- `/state`、`/indicators`、`/map_obj`、`/map_info` 的**实时延迟与有效性**
- `/map_obj` 直接显示字节数与机场数量（排查“机场指示消失”的关键指标）

### 检查更新
- 查询 GitHub 最新 release 并比对版本（数字逐段比较，`0.1.10 > 0.1.9`）
- 只显示 `发现新版本 x.x.x` 或 `已是最新`，不下载、不跳转

### 其他
- 全新应用图标（头盔）
- 新增调试命令：`WtHud.exe mfd`（独立面板）、`WtHud.exe mapprobe`（地图数据诊断）

## 修复
- `map_obj.json` 返回空响应时 JSON 解析异常会中断整个地图轮询，连带 GRID / 导航失效
- 地图空帧导致机场指示 / 边界告警闪烁消失（加 5 秒宽限缓存）
- 按键按下状态被永久保留（现在按下才暗、松开恢复）
- SAVE 后关闭时仍弹出“放弃修改”确认
- 主菜单按 BACK 会误退出面板（现在根层级不退出）

## 下载
- `WtHud.exe`（单文件，自包含，无需安装 .NET）
- `WtHud-v0.1.7-win-x64.zip`

SHA256：
- `WtHud.exe` `B54E840D7B812B742CFB18F0794EB8926D67E904186F5A0AB490C7F31A674046`
- `WtHud-v0.1.7-win-x64.zip` `3D60D6FDFE7D55ADF32E4EEE37E640A4360A8A56F18928F8A675526B1BE6614D`

## 使用
1. 进入对局（8111 可访问）
2. 运行 `WtHud.exe`
3. 游戏内按 **F1** 打开设置面板；再按 F1 关闭

---

# War Thunder 8111 Third-Person HUD · v0.1.7

A major update: the settings panel has been rebuilt in an **MFD (physical bezel buttons) style**, plus multi-language support, diagnostics and update checking.

## Added

### MFD settings panel (F1)
- The bezel and its 25 physical buttons come from a Blender model render — not a flat texture
- Three levels: main menu → module page (2 columns × 5 rows) → second-level page (numeric entry / key capture / confirmation)
- Physical buttons map 1:1 to screen rows: **side key = select, CONFIRM = apply**
- Rows can also be clicked directly (double-click = confirm)
- Numeric rows: CONFIRM opens an entry page where you type the value
- Key rows: CONFIRM opens a capture page with **live combo display** (hold Ctrl → `CTRL`, then press Q → `CTRL+Q`, release freezes it, press again to restart)
- One-shot actions (reset / import / check update …) require a second confirmation
- CANCEL asks before discarding unsaved changes; SAVE saves immediately
- PAGE only paginates the current module (no longer jumps modules); the page count is shown in the header
- HELP overlay and an unsaved `*` marker
- The panel scales itself to the screen (~92% on 1080p)
- **F1 is a toggle**: pressing F1 again closes the panel (through the CANCEL flow) instead of opening duplicates
- Window region is clipped to the bezel silhouette, so no black box around it

### Three independent language scopes
- Bezel silkscreen / screen text / HUD alerts can each be set to Chinese or English
- Defaults: bezel English, screen Chinese, alerts English
- Optional follow-system language

### Localized alerts
- `STALL → 失速`, `PULL UP → 拉起`, `SINK RATE → 下降率`, `REDUCE SPEED → 减速`, `RETURN → 返回战区`, `AF → 机场`, `AF! → ！机场`, `BINGO → 低油量`, `AOA → 迎角`
- Alert box widths are recomputed from the **measured text width of the active language**, so neither language overflows

### DIAG page
- Live latency and validity for `/state`, `/indicators`, `/map_obj`, `/map_info`
- `/map_obj` reports payload bytes and airfield count (the key signal when “airfield indicator disappeared”)

### Check for updates
- Queries the latest GitHub release and compares versions numerically (`0.1.10 > 0.1.9`)
- Only reports `发现新版本 x.x.x` / `已是最新`; nothing is downloaded or opened

### Other
- New application icon (flight helmet)
- New debug commands: `WtHud.exe mfd` (standalone panel), `WtHud.exe mapprobe` (map data diagnostics)

## Fixed
- An empty `/map_obj.json` response raised inside the JSON parser and killed the whole map poll, taking GRID / navigation down with it
- Brief empty map frames made the airfield indicator and edge warning flicker away (5 s grace cache)
- Button pressed state could stick permanently (now only pressed while the mouse is down)
- Closing after SAVE still asked to discard changes
- BACK on the root menu used to exit the panel (root no longer exits)

## Download
- `WtHud.exe` (single self-contained file, no .NET install required)
- `WtHud-v0.1.7-win-x64.zip`

SHA256:
- `WtHud.exe` `B54E840D7B812B742CFB18F0794EB8926D67E904186F5A0AB490C7F31A674046`
- `WtHud-v0.1.7-win-x64.zip` `3D60D6FDFE7D55ADF32E4EEE37E640A4360A8A56F18928F8A675526B1BE6614D`

## Usage
1. Enter a battle (the 8111 API must be reachable)
2. Run `WtHud.exe`
3. Press **F1** in-game to open the settings panel; press F1 again to close it
