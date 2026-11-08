# 参数作用域决策（v0.3.0）

优先级：**机型覆盖 > 全局 > 内置默认**

## 支持按机型覆盖（9 项）

### 数值
| 行 key | 名称 |
|---|---|
| `row.pitchzero` | 俯仰零点（已接入，作为模板） |
| `row.bankTrim` | 坡度修正 |
| `row.fpmTrim` | 矢量修正 |
| `row.stallAoa` | 失速迎角 |
| `row.aoaWarn` | 迎角告警 |
| `row.overspeedIas` | 超速 IAS |
| `row.overspeedMach` | 超速 Mach |

### 枚举
| 行 key | 名称 |
|---|---|
| `row.rollCoupleMode` | 滚转耦合方式 |
| `row.bankSign` | 坡度方向 |
| `row.pitchSign` | 俯仰符号 |

## 仅全局（其余全部）

### 开关（10）——一律只写全局
`row.hideCockpit` `row.hideNose` `row.extrapolate` `row.hideGround` `row.showHud`
`row.hideBg` `row.ladder` `row.fpm` `row.tapeRadar` `row.aimRmb` `row.realisticKeys`

说明：开关没有编辑页，无法选择作用域；按机型区分的价值低，统一走全局。

### 数值（全局）
`row.fov` `row.yoff` `row.hole` `row.holeX` `row.holeY`
`row.groundSpeed` `row.spaaRange` `row.bingo` `row.afAlert` `row.edgeMargin`
`row.rollCouple`

返航油量 `bingo` 按“剩余可飞时间”计算，属于战术偏好，保持全局。

### 其它
- 颜色预设、面板缩放：全局
- 语言（界面/告警）、键位绑定：全局
- `row.tapeRadar`（高度带雷达）：保持现状，不接入覆盖，由检测/机型库决定

## 实现约定
1. 每接入一行独立 commit，并用 `tools/scan_page_migration.py` 验证无遗留分支。
2. 读取统一走 `SettingsRouter.Get`（自动状态解析“检测到的机型”）。
3. 写入：`EditScope.IsGlobal` → 写全局字段；否则 → 写该机型覆盖。
4. RESET：机型作用域清空该机型全部覆盖并保持作用域；全局作用域恢复默认并保持作用域。
5. 开关行不经过 `SettingsRouter`。
