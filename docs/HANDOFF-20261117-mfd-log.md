# HANDOFF — MFD 内置飞行记录分析（WIP，2026-11-17 断电中断）

> 用户当场断电，本分支保存了中断瞬间的工作现场。恢复后从这里继续。

## 分支

```
git checkout wip/mfd-log
```

`main` 没有被污染；本分支 = `main` + 两个子代理的未完成改动。

## 目标（用户需求）

WtHud 内置接口调用 `FlightLog.exe` 分析 **HUD 自己录的 `.jsonl`**（不支持 `.wrpl` 直接打开），
在 **MFD 自己的页面风格**里显示：**左侧折线图 + 右侧统计值**（最大速度、平均速度、最大 G 等）。
**不做手动时间范围**；平均速度由 FlightLog 自动排除「死亡前 10 秒窗口 + MAD 3σ 尖峰」后给出。

## 已完成 / 进行中

### FlightLog（Python，`tools/flightlog/`）
- 新增 `mfd.py`（WIP）：`--mfd-json` 接口的数据模型/导出逻辑
- 改 `__main__.py`：接 `--mfd-json` / `--out`
- 改 `__init__.py`、`launch.py`（小改动，未完成验证）
- **未完成**：selftest 新断言、真实样例 JSON（`out/mfd/*.json`）、README 章节、实际跑通
- 目标 CLI：
  ```bat
  FlightLog.exe --mfd-json "<log.jsonl>" --out "<tmp.json>"
  ```
  JSON schema=1，`flights[i].series`（1–2 Hz、≤900 点、null 表示缺失），
  `stats.avg_*` / `max_*_filtered_*` / `avg_filter`。

### HUD（C#，`src/WarThunderHud/`）
- 新增 `UI/Mfd/FlightLogBridge.cs`（WIP）：定位 FlightLog.exe、跑进程、解析 JSON
- 新增 `UI/Mfd/FlightLogPlot.cs`（WIP）：MFD 折线绘制
- 改 `UI/Mfd/MfdTheme.cs`（WIP）：图表配色
- **未完成**：`MfdPages` 注册「记录」模块与列表/图表页、`MfdFrameForm` 输入路由、
  L10n 文案、help-desc、`mfdpreview` 视图、selftest、编译验证（可能编译不过）

## 恢复后的第一步

1. `git checkout wip/mfd-log`，`dotnet build` 看编译错误（预计不少，子代理写到一半）
2. 先让 FlightLog 侧完成：`python tools/flightlog/selftest.py`、生成 `out/mfd/sample-204002.json`，
   用 `python -m flightlog <log> --mfd-json out/mfd/x.json` 验证结构
3. 再让 HUD 侧基于真实样例完成页面与预览图

## 相关资源

- 样例飞行记录：`C:\Users\noapi\Desktop\WtHud-发布\recordings\wtlog-20261117-204002-jh_7a.jsonl`
  （约 5153 样本）等
- 现有 FlightLog 报告/翻页实现：`tools/flightlog/report.py`
- 通用层纪律：`docs/05-architecture-common-layer.md`
- 用户此前确认：方案 A（原生画图）；左图右数；平均不可手动选范围；不直接开 `.wrpl`

## 其他未提交现场

- `HANDOFF-20261115.md`（更早的未跟踪文件，保持原样）
