# wrpl-acmi 组件说明（分发合规）

本发布包内附的 `wrpl-acmi.exe` 来自第三方开源项目：

- 项目：https://github.com/3794432/wrpl-acmi
- 许可证：**GNU Affero General Public License v3.0（AGPL-3.0）**
- 我们分发的版本：release tag `acmi-2.60.0.21-2026.11.02`
- 对应源码提交：`9ed43b9f108b062b408b1d4d0c2a62d46b00bfe4`
- 官方下载：https://github.com/3794432/wrpl-acmi/releases/tag/acmi-2.60.0.21-2026.11.02
- 文件 SHA-256：`c80d9c5c04531dadc27342788f9800a440a6f258076054dabfb6f9904e71890e`

## 是否修改

**没有修改。** 本包内的 `wrpl-acmi.exe` 与上述官方 release 资产逐字节一致（SHA-256 相同），仅重命名路径分发。

## 对应源码（Corresponding Source）

本发布包**不内置源码压缩包**，对应源码通过**同一下载页/发布说明中的链接**提供（AGPL-3.0 §6）：

- 上游仓库：https://github.com/3794432/wrpl-acmi
- 对应 tag：`acmi-2.60.0.21-2026.11.02`
- 对应提交：`9ed43b9f108b062b408b1d4d0c2a62d46b00bfe4`
- 源码快照：https://github.com/3794432/wrpl-acmi/archive/refs/tags/acmi-2.60.0.21-2026.11.02.zip
- 或从同一 Release 下载页的「Source code (zip)」获取

本包内的 `wrpl-acmi.exe` 与上游该 tag 的官方构建**逐字节一致**（SHA-256 相同），未做任何修改；
按 AGPL-3.0，任何人可依据上述源码重新构建。

该文件即上述 tag 的完整源码快照（GitHub archive 解压后重新打包），包含 `build.ps1`、`go.mod`、`go.sum`、`wrpl/`、`cmd/acmi/`、`data/` 等构建所需的全部文件。

重新构建（Windows PowerShell，需要 Go 1.25.6+）：

```powershell
# 解压源码 zip 后，在源码根目录
.\build.ps1
```

## 许可证文本

`licenses/wrpl-acmi-LICENSE.txt`（上游 AGPL-3.0 全文）。

## 第三方内容（exe 内嵌目录数据）

见同目录 `wrpl-acmi-THIRD-PARTY.md`。

## 其他说明

- 我们只是 **以独立进程方式调用** `wrpl-acmi.exe`（`FlightLog.exe` 通过命令行调用它把 `.wrpl` 转成 `.acmi`），没有链接进同一程序；在同一发布包中分发属于聚合，不改变各自许可证。
- AGPL-3.0 允许再分发；本包已按 GPLv3/AGPL §6 提供许可证文本、未修改声明与对应源码（同一发布位置并列提供）。
- 上游项目 README 声明：项目仍在早期开发，使用者自行承担运行构建产物的后果；不构成对 War Thunder 游戏资产的再分发授权。
