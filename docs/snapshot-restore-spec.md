# 回归快照（已实现）

状态：**已实现**（commit `a0b3348`，2026-11-08）。本文档保留为需求与实现说明。

## 入口
首页菜单新增第 9 个模块：**回归快照**（`menu.snapshots`）。
- 侧键 8（右列第 4 行）对应
- 图标/标题沿用现有菜单风格

## 一级页：快照列表
布局与其它列表页一致：
- 2 列 × 5 行，最多 10 项/页
- 一个条目对应一个侧键
- `PAGE◀ / PAGE▶` 翻页
- 单击选中（高亮），双击直接进入确认

数据源：`dist-wthud\temp\*.json`
- **包含**：所有 `*-save.json`（先只做 save 快照）
- **排除**：`_cancel.json`
- 排序：按修改时间倒序（最新在上）
- 显示文本：`MM-dd HH:mm  save`（文件名解析）

后续扩展：把 `temp\session\*.json` 也列出来（标注 `edit`），用于回退到本次会话的中间状态。

## 二级页：确认
选中条目 → CONFIRM → 通用确认页：
```
恢复到 11-08 21:37 的 save 快照？
[确认] [取消]
```
- 确认 → 执行恢复
- 取消 / BACK → 回到列表，不做任何修改

## 恢复动作
1. 读快照 JSON，反序列化为 `HudConfig`
2. 用反射复制全部可读写属性到**运行中配置 `_cfg`**
3. 同步到草稿 `_work`
4. 删除整个 `temp\session\`
5. 写一份 `temp\{时间}-restore.json` 作为审计记录（可选）
6. 提示「已恢复到 xx:xx 的快照」，返回首页
7. 不关闭面板，让用户能立刻核对数值

## 需要复用的既有设施
- `ConfigSnapshots`（`C:\...\Hud\ConfigSnapshots.cs`）：已有 `History()/SaveHistory()/RestoreCancelPoint()`
- 页面层：`MfdPageBase` + `MfdNavigator`（参考 `PickVehiclePage` 的写法）
- 二级确认：`ConfirmPage`（已有，传 `texts/onOk/onCancel/layout`）
- 扫描器：`tools\scan_page_migration.py Snapshots`（迁移完应只剩页面调用）

## 注意
- 恢复后**不要**自动 SAVE：让用户 проверять后自己按 SAVE，避免误覆盖
- 恢复是“全局配置整体替换”，机型覆盖表 `perVehicleOverrides` 也包含在快照中，会一起回退
- 首页模块数从 8 → 9，仍在 10 个侧键容量内

## 实现记录（2026-11-08）

代码：
- `Hud/ConfigSnapshots.cs`：嵌套 `SnapshotEntry`（`Path/LocalTime/Kind` + `TimeText/Display`）、`ListRestorable()`、`RestoreFrom(path, target)`
- `UI/Mfd/Pages/SnapshotsPage.cs`：2×5 列表页（翻页/选中/空列表提示），构造/回调契约见 `MfdPageBase`
- `UI/Mfd/MfdFrameForm.cs`：`Mode.Snapshots` 的绘制/确认/返回/移动/翻页/侧键/点击；入口集中在 `EnterSnapshots()` 与 `EnterSnapshotConfirm()`（避免直接 `_mode = Mode.Snapshots` 造成空白页）
- `UI/Mfd/MfdPages.cs`：第 9 个模块 `page.snapshots`，`MfdPage.LiveCount` 实时显示快照数
- `UI/L10n.cs`：`page.snapshots / menu.hint.snapshots / snap.*` 共 9 个键
- `Program.cs`：`mfdpreview` 新增 `snapshots / snapconfirm / snaprestore` 视图（后者会真实执行一次恢复，用于回归验证）
- `tools/scan_page_migration.py`：支持 `Snapshots`；`Enter*` 方法体内的 `_mode = Mode.X` 不再误报为 direct jump

实现时确认的细节（与上文规格的差异/补充）：
1. **首页位置**：现有 8 个模块按 row-major 填充（逐行读），第 9 个模块落在**左列第 5 行（侧键 4）**。本文档原写的「侧键 8（右列第 4 行）」与现行填充顺序冲突（侧键 8 已是「诊断」），未改菜单布局以免移动既有 8 个模块。
2. `ListRestorable()` 目前**只列 `*-save.json`**（`_cancel.json`、`*-restore.json`、`session/` 均不进列表）；`session/*.json` 标注 `edit` 的扩展未做（见「后续扩展」）。
3. 审计文件 `temp/{yyyyMMdd-HHmmss}-restore.json` 写入后**不参与** `History()/SaveHistory()` 的 10 份裁剪，否则恢复几次就会挤掉真正的历史快照。
4. 恢复成功：回首页 + toast「已恢复到 xx:xx 的快照」，面板不关、不自动 SAVE；列表页在进入时读盘（面板开着时新 SAVE 的东西要退出重进才看到）。
5. 列表页 RESET 不生效（只读页，只提示），避免误清配置。

验证：
- 后端隔离单测：5 个 `-save.json` 过滤/倒序/`Display` 格式、反射回填、`perVehicleOverrides` 一起回退、`session` 删除、审计写入且不进列表/不参与裁剪、坏文件与缺失文件返回 false 且不改 target —— 全部 PASS
- 面板预览：`mfdpreview ... snapshots / snapconfirm / snaprestore` 三种视图渲染正确（9 模块首页、5 条快照倒序、确认页文案含时间、恢复后首页 toast）
- `python tools/scan_page_migration.py Snapshots` → CLEAN；Debug/Release 编译 0 错误；发布到 `dist-wthud` 后启动正常（30fps 日志无异常）

## 修复记录（2026-11-08，commit `0b66417`）

用户实测发现两个 bug，均已修复并用预览回归：

1. **所有二级确认页鼠标无效**（单击不选中、双击不执行）
   - 原因：`MfdFrameForm.OnMouseDown` 只有 Menu/Items/PickVehicle/Snapshots/Preset/EditNumber 的命中分支，`Mode.Confirm` 没有命中测试；落到通用 `RowHitTest` 分支后既不改选中也不执行。
   - 修复：`ConfirmRowHitTest(point) → option`（行矩形与 `ConfirmPage.Draw` 共用 `MfdPageLayout.ListRowRect(0, 2+i)`，左右各放宽 6*s 覆盖高亮条）+ `TryClickConfirmRow(point, clicks)`：单击 `_confirmPage.Select(option)`，双击（或同一格 400ms 内重点）执行 `confirm.ok / confirm.cancel`。
   - 顺带：`Confirm()` 的 CONFIRM 键改为跟随 `_confirmPage.Selected`（高亮在「取消」时不再误执行确认）。

2. **快照二级确认页按「取消」侧键 → 空白页**
   - 原因：`SidePressed` 在 `Mode.Confirm` 直接调 `RunPending()/CancelPending()`，绕过 `ConfirmPage` 的 `onOk/onCancel`；快照确认的取消于是执行 `_mode = Mode.Items`，而快照模块行列表为 0 → 满屏 `—` 的空页。
   - 修复：确认页的物理侧键 2/3、RESET、CANCEL、SAVE 路径全部改走 `_confirmPage.Handle(...)`，由页面回调决定返回哪一页（快照取消 → 快照列表；通用确认取消 → 原模块页 / 机型详情）。

回归视图（`mfdpreview`）：`snapback`（侧键取消 → 回列表）、`snapclick`（单击「取消」行 → 只高亮）、`snapclickok`（双击「确认」行 → 执行恢复）；三者渲染均符合预期，扫描器仍 CLEAN。
