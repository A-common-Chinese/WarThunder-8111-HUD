# 回归快照（待实现规格）

状态：**未实现**，仅记录本轮确认的需求。

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
