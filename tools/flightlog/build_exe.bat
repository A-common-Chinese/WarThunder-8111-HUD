@echo off
REM 重新打包 FlightLog.exe（需要本机 Python + PyInstaller）
REM 用法：双击本文件，产物在 flightlog\dist\FlightLog.exe
setlocal
cd /d "%~dp0.."
python -m PyInstaller --noconfirm --onefile --noconsole --clean ^
  --name FlightLog ^
  --distpath "flightlog\dist" ^
  --workpath "flightlog\build_tmp" ^
  --specpath "flightlog\build_tmp" ^
  --paths . ^
  --collect-submodules flightlog ^
  "flightlog\launch.py"
if errorlevel 1 (
  echo 打包失败
  pause
  exit /b 1
)
echo 完成: %~dp0..\flightlog\dist\FlightLog.exe
pause
