# 击落播报（SPLASH）方案 · 已实现（v0.3.3，未发布）

状态：**已实现**。真实对局采样（2026-11-09，213 条消息 / 59 次击杀）确认最终规则：
**只认「击落了」**（玩家空战击杀）；AI/地面是「击毁了」，不播报；被击落不播报；`^DFCN^` 是联队/颜色标记，
因人而异，不能当身份 → 昵称改为显示页手动输入。
实现：`Telemetry/HudKillFeed.cs`（清洗/解析）+ `Telemetry/HudMsgWatcher.cs`（4 Hz 轮询/按 id 去重）+
`HudGeometry.AddSplash`（2.2 s 队列、淡入淡出）+ 显示页「击落播报 / 玩家昵称」。

需求来源：用户希望在自己击落敌机后弹视觉提示（AI 击杀的小提示已按后续要求取消，只保留 SPLASH）。

## 已确认的数据事实（2026-11-08 实测，中文客户端）

数据源：`GET http://127.0.0.1:8111/hudmsg?lastEvt=<id>&lastDmg=<id>`
- 不带参数 → HTTP 400；带参数 → `{"events":[...],"damage":[...]}`
- 增量语义：传已见过的最大 id 只返回更新条目（`lastDmg=99999` 返回空）→ 可低频轮询去重
- `events` 实测一直为空，**不用管**；`damage` 流足够
- `damage` 条目：`{"id":1,"msg":"...","sender":"","enemy":false,"mode":"","time":2122}`
  - 结构化字段（sender/enemy/mode）在实测样本里全是空的 → **只能解析本地化文本**
  - `id` 严格递增；`time` = 任务内秒数
- `msg` 格式（实测）：`^DFCN^ <玩家名> (<自己机型>) 击毁了 <目标名>`
  - 玩家：`^DFCN^ ACCvTARDISdevice (F-4J) 击毁了 AI米格-15bis`
  - 自己坠毁：`^DFCN^ ACCvTARDISdevice (F-4J) 已坠毁。`
  - 每个汉字之间夹 **U+200B 零宽空格**，另有 `^..^` 颜色标记 → 匹配前必须清洗
  - AI 目标名带 `AI` 前缀（`AI米格-15bis`）→ AI/玩家击杀可区分
- 多人房 `damage` 流会包含**其他玩家**的击杀行 → 必须按自己玩家名过滤（用户已确认）
- 8111 没有 `/profile`（404）；`/state`、`/indicators` 不含昵称 → **自己的玩家名只能手填或读游戏本地文件（不推荐）**

## 规格（用户已拍板）

1. AI 击杀 → 小提示「AI击落」（小框/单行）
2. 击落玩家 → 大「SPLASH」卡片（描边 + 淡入淡出，约 0.12s / 1.8s / 0.25s）
3. 暂不管英文客户端（关键词只做中文「击毁了 / 击落了」）
4. 解析失败**静默**，不报错、不刷日志；HTTP 失败也不刷
5. F1 面板「显示」页新增「是否启用击落播报」Toggle，**全局设置**（不进 `SettingScopes` 注册表）
6. 需要玩家 ID 输入窗口 → **建议归属「显示」页**，紧挨开关（追加到显示页最后，避免重排侧键位）；确认按钮进入新的 `TextEditPage`（支持 ASCII 键入 + Ctrl+V 粘贴 + 退格 + CONFIRM 保存/ESC 取消，~24 字符，保存即生效无需重启）

## 待验证（明天的真实对局）

- **一条「击落玩家」的 `damage` 原始条目**：确认 target 格式（是否也带 `(机型)`）、动词是「击落了」还是「击毁了」、`sender/enemy` 是否有值
- 多人房确认：其他玩家的击杀行确实出现在同一 `damage` 流（用户观察如此）
- 可选：一次连杀，确认连杀时条目的时间间隔（用于堆叠/合并策略）

采样方法（打完之后跑，把输出贴给下一个会话）：
```powershell
$r = Invoke-WebRequest "http://127.0.0.1:8111/hudmsg?lastEvt=0&lastDmg=0" -UseBasicParsing
[System.Text.Encoding]::UTF8.GetString($r.RawContentStream.ToArray())
```
（先记一次基线，击落后再抓一次，前后 diff 即可看出新条目）

## 实现清单（未开始）

1. `HudConfig`：`killFeedEnabled: bool`、`playerName: string`
2. `UI/Mfd/Pages/TextEditPage.cs`：文本输入页（Ctrl+V、退格、CONFIRM/ESC、长度上限）
3. `UI/Mfd/MfdPages.cs`：显示页追加 2 行（Toggle「击落播报」+ 玩家名输入）；`MfdItem` 增加文本输入入口
4. `Hud/HudEvents.cs`：`/hudmsg` 4 Hz 轮询 + 清洗（`^..^`、U+200B/200C/200D）+ 正则解析 + id 去重 + 两级事件
5. 渲染：`HudPrim` 画「SPLASH」大卡片与「AI击落」小提示；连杀合并（如 2s 内 ×N）；堆叠上限 2–3
6. 解析器：`^(?<actor>[^(]+?)\s*\((?<type>[^)]+)\)\s*(击毁了|击落了)\s*(?<target>.+)$`；actor 与配置名做忽略大小写/去空格/**包含匹配**（名字正则转义）；target 以 `AI` 开头 = AI 击杀
7. 诊断/仿真：`WtHud.exe hudmsg`（dump 原始 JSON）、建议再加 `hudmsgsim <json>`（用录好的样本离线渲染验证）
8. 文档：HANDOFF / help-desc 同步

## 风险

- 玩家击杀文案未采样（唯一硬 gate）
- 名字可能带战队/平台前后缀 → 用「包含匹配」并在面板显示当前名字
- 中文名无法键盘键入 → 靠 Ctrl+V 粘贴兜底
- 面板打开期间轮询积压 → 恢复后按 lastDmg 增量补齐，单次处理上限防卡
- 游戏更新可能改文案 → 关键词表化 + 解析失败静默

## 待用户拍板（下次继续时确认）

1. 「击落播报」开关默认值：开（填名字即生效）还是关？
2. 输入页 v1 就做，还是先只支持配置文件、输入页下一版补？
