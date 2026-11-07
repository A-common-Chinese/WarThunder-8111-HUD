# 战雷 8111 第三人称 HUD · v0.1.8

## 新增
- **全真导航 · 友军防空**：HUD 显示最近友方 SPAA 的方位与距离，例如 `AA ↗ 190° 7.8km`
  - 距离上限默认 **60 km**，可在「告警」页调整，设为 0 则关闭
  - 关闭俯仰梯时地平线一并隐藏
- **坡度耦合方式**：新增 `自动 / 开 / 关`；全真座舱视角下坡度梯随飞机滚转

## 修复
- 全真模式地面单位坐标解析用错字段（`sx/sy` → `x/y`），导致距离恒为 0、AA 永不显示
- `GroundUnits` 解析结果未赋值，列表始终为空
- 关闭俯仰梯后地平线仍显示

## 内部
- MFD 面板控件层重构起步：`VirtualButton` / `RealButton` / `MfdInputRouter` / `MfdPageLayout` / `MfdScreenPage`
- 首页菜单输入已切换到新框架（侧键 / CONFIRM / 鼠标）

## 下载
- `WtHud.exe`（单文件，自包含）
- `WtHud-v0.1.8-win-x64.zip`

SHA256: `535C78BE8D44EF2E68D5E3A8BC17EAA3C335EBC64F94166426EE3279AAFA8728`

---

# War Thunder 8111 HUD · v0.1.8

## Added
- **Sim navigation · friendly SPAA**: nearest friendly SPAA bearing/distance on the HUD (`AA ↗ 190° 7.8km`)
  - range limit defaults to **60 km**, configurable in the Alerts page (0 disables it)
  - the horizon line now hides together with the pitch ladder
- **Roll coupling mode**: `auto / on / off`; in the sim cockpit view the ladder rotates with the aircraft

## Fixed
- Ground unit coordinates were read from the wrong fields (`sx/sy` instead of `x/y`) in sim, so distances were always 0 and AA never appeared
- The parsed ground-unit list was never assigned, leaving it permanently empty
- The horizon stayed visible after turning the pitch ladder off

## Internal
- Started the reusable control layer for the MFD panel: `VirtualButton` / `RealButton` / `MfdInputRouter` / `MfdPageLayout` / `MfdScreenPage`
- Main menu input now runs on the new framework (side keys / CONFIRM / mouse)

## Download
- `WtHud.exe` (single self-contained file)
- `WtHud-v0.1.8-win-x64.zip`

SHA256: `535C78BE8D44EF2E68D5E3A8BC17EAA3C335EBC64F94166426EE3279AAFA8728`
