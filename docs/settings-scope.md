# 参数作用域（自动生成，请勿手改）

生成命令：`python tools/gen_scope_doc.py`  
来源：`src/WarThunderHud/Hud/SettingScopes.cs`

优先级：**机型覆盖 > 全局 > 内置默认**

## 按机型覆盖（8 项）

| 行 key | 原因 |
|---|---|
| `row.aoaWarn` | 气动特性 |
| `row.bankTrim` | 校准值 |
| `row.fpmTrim` | 校准值 |
| `row.overspeedIas` | 结构限制 |
| `row.overspeedMach` | 结构限制 |
| `row.pitchSign` | 个别机型反向 |
| `row.pitchzero` | 座舱/装配基准，随机型 |
| `row.stallAoa` | 气动特性 |

## 显式登记为全局（0 项）

| 行 key | 原因 |
|---|---|

## 默认规则

- 未在注册表中出现的行 = 全局
- 开关（Toggle）与点击式枚举一律全局：没有编辑页，无法选择作用域
- `row.pitchSign` 是唯一按机型的枚举，由「自动判断俯仰方向」或手动选择机型写入
