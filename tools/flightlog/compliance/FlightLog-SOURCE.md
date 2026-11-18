# FlightLog 源码说明（AGPL-3.0-or-later）

`FlightLog.exe` / `wrpl-acmi.exe` 随包的飞行日志与回放分析工具，由本项目 WtHud 发布。

- 项目：WtHud（War Thunder HUD）
- 版本：0.4.42
- 许可证：**GNU Affero General Public License v3.0 or later（AGPL-3.0-or-later）**
- 许可证文本：`licenses/AGPL-3.0.txt`
- 对应源码：与本发布包**并列放在同一发布目录**的

```
FlightLog-src-0.4.42.zip
```

源码包内容包括：`parse.py`、`report.py`、`acmi.py`、`replay.py`、`picker.py`、`launch.py`、
`__main__.py`、`selftest.py`、`build_exe.bat`、`fetch_wrpl_acmi.ps1`、`README.md` 等全部 Python 源码与构建脚本。

## 重新构建 FlightLog.exe

需要 Windows + Python 3.10+：

```bat
pip install pyinstaller
cd wt-hud\tools\flightlog
build_exe.bat
```

产物在 `dist\FlightLog.exe`；`build_exe.bat` 会把 `wrpl-acmi.exe` 一同复制到 `dist\`。

## 说明

- `FlightLog.exe` 通过命令行调用同目录的 `wrpl-acmi.exe` 完成 `.wrpl → .acmi` 转换；
  两者是独立进程，不是链接关系。
- WtHud 项目源码（HUD 本体）同样以 AGPL-3.0-or-later 发布。
- 如只需要源码而不需要二进制，可直接使用上述源码 zip；构建不需要游戏本体。
