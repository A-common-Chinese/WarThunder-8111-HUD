# wrpl-acmi 第三方内容与许可声明

`wrpl-acmi.exe` 由第三方项目 [3794432/wrpl-acmi](https://github.com/3794432/wrpl-acmi)（AGPL-3.0）构建，
本发布包仅按原样分发，未做修改。其内嵌的目录数据（机型/武器/雷达/机场等查找表）来源如下，
具体许可以上游仓库与其文档为准。

## 1. wrpl-inspector（解析库上游）

- https://github.com/maxsupermanhd/wrpl-inspector
- 许可证：AGPL-3.0
- `wrpl-acmi` 保留其 Go 回放解析库；Go module 路径仍为 `github.com/maxsupermanhd/wrpl-inspector/v3`。
- 对应的 Go 依赖与版本锁定见源码包内的 `go.mod` / `go.sum`；各依赖按其自身许可证分发。

## 2. War-Thunder-Datamine（数据来源）

- https://github.com/gszabi99/War-Thunder-Datamine
- 该仓库用于提供游戏内数据（模型名、武器参数等）的社区解包结果。
- **注意**：该仓库在 GitHub 上未声明标准开源许可证（API 返回 `license: null`）。
  其内容的使用/再分发条款需向该仓库作者确认；`wrpl-acmi` 上游选择将其派生数据内嵌进 AGPL 构建，
  相关责任与授权以上游项目为准。
- 本发布包**不分发** War Thunder 原始游戏资产、地形或 `levels/` 数据。

## 3. WT-MAP-ANALYZE（地理参考数据）

- https://github.com/ShioUmiusi/WT-MAP-ANALYZE
- 许可证：MIT
- 用于地图的地理参考/经纬度估算；上游 README 说明部分地图为 Atlas fallback 坐标，
  仅供技术展示，不代表真实地理位置。

## 4. 游戏资产与商标

- War Thunder 及其资产、商标归 Gaijin Entertainment 所有。
- 本发布包只包含第三方转换器可执行文件与我们的分析工具，不包含游戏本体文件、地形或模型资源。
- 上游 `wrpl-acmi` README 明确说明：避免分发预生成游戏数据可降低再分发风险，
  但并不构成对提取、转换、使用或再分发游戏资产的授权。

## 5. 我们的工具（FlightLog.exe）

`FlightLog.exe` 由本项目（WtHud）以 AGPL-3.0-or-later 发布，源码见同发布位置的
`FlightLog-src-0.4.40.zip`，许可证文本见 `licenses/AGPL-3.0.txt`。
