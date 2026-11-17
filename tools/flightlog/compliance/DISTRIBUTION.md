# 发布包结构与合规说明（v0.4.40）

本文件记录 v0.4.40 发布包的组成、第三方组件与再分发合规做法，便于以后复现。

## 1. 发布目录布局（桌面 `WtHud-发布\`）

```
WtHud-v0.4.40-win-x64.zip          # 主发布包（HUD + FlightLog + wrpl-acmi + 文档 + licenses）
wrpl-acmi-src-acmi-2.60.0.21-2026.11.02.zip   # wrpl-acmi 对应源码（与主包并列）
FlightLog-src-0.4.40.zip           # FlightLog 源码（与主包并列）
SHA256.txt                         # 上述文件的 SHA-256
BUILD.txt                          # 构建记录
RELEASE_NOTES_v0.4.40.md / .en.md  # 发布说明
```

`WtHud-v0.4.40-win-x64.zip` 内容（14 项）：

- `WtHud.exe`（本体，AGPL-3.0-or-later）
- `FlightLog.exe`（日志/回放分析器，AGPL-3.0-or-later）
- `wrpl-acmi.exe`（第三方，AGPL-3.0，官方 release，未修改）
- `licenses/`（AGPL 文本、两个组件的 SOURCE/THIRD-PARTY 说明）
- `LICENSE.txt`、`NOTICE.txt`、`使用教程.md`、`使用说明.txt`、`编队速查卡.md`、`FlightLog-README.md`

## 2. 第三方组件

| 组件 | 版本 | 许可证 | 修改 |
|---|---|---|---|
| wrpl-acmi | release `acmi-2.60.0.21-2026.11.02`（commit `9ed43b9f…`） | AGPL-3.0 | 未修改，SHA-256 `c80d9c5c…890e` |
| wrpl-inspector（上游库） | 随 wrpl-acmi 源码 | AGPL-3.0 | — |
| War-Thunder-Datamine（数据来源） | 上游固定提交 | 未声明标准许可证 | 仅随 wrpl-acmi 二进制内嵌派生目录 |
| WT-MAP-ANALYZE（地理参考） | 上游固定版本 | MIT | — |

## 3. 合规要点

- `wrpl-acmi.exe` 未修改，保留原文件名与版权；许可证文本在 `licenses/wrpl-acmi-LICENSE.txt`。
- **对应源码**：`wrpl-acmi-src-acmi-2.60.0.21-2026.11.02.zip` 与主包**并列放在同一发布目录**
  （AGPL §6(d) 的等价访问）；包内 `licenses/wrpl-acmi-SOURCE.md` 给出 tag/commit/SHA/构建方法。
- FlightLog 源码：`FlightLog-src-0.4.40.zip` 同目录并列；`licenses/FlightLog-SOURCE.md` 说明。
- 独立进程调用（非链接/非组合作品），同一 zip 内属于聚合。
- AGPL §13 网络条款不触发（本地工具，不提供网络服务）。
- 游戏资产：只分发转换器可执行文件，不分发 War Thunder 原资产/地形；上游 README 已声明不构成资产再分发授权。

## 4. 复现步骤

1. 构建 HUD：`dotnet publish src/WarThunderHud/WarThunderHud.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true ...`
2. 构建 FlightLog：`cd tools/flightlog && python selftest.py && build_exe.bat`
3. 获取转换器：`powershell -ExecutionPolicy Bypass -File tools/flightlog/fetch_wrpl_acmi.ps1`
   （GitHub 直连可能被加速器拦截，脚本会自动走镜像；校验 SHA-256）
4. 拉取上游源码：`https://github.com/3794432/wrpl-acmi/archive/refs/tags/acmi-2.60.0.21-2026.11.02.zip`
5. 生成 FlightLog 源码 zip（只含 `*.py`、`*.bat`、`*.ps1`、`README.md`、`.gitignore`）
6. 按第 1 节布局组装 zip，更新 `SHA256.txt` / `BUILD.txt` / release notes。

## 5. v0.4.40 校验值

- `WtHud-v0.4.40-win-x64.zip`：`A16BA722A7B1EAC2CA6AC6FCD69B5CF455FFD6DD313D3F8122F20E3AD8171C79`（99.4 MB）
- `wrpl-acmi-src-acmi-2.60.0.21-2026.11.02.zip`：`267DE55D12868D20210D25744D661F352DAF08F7849BBBDBF975A6BCDAD884D8`（9.3 MB）
- `FlightLog-src-0.4.40.zip`：`6BD8A812BE732DED93A18E68D7E67D67AD5E22D462DBBC3B5BD6DA7DA22330AF`（108 KB）
- `wrpl-acmi.exe`：`c80d9c5c04531dadc27342788f9800a440a6f258076054dabfb6f9904e71890e`
