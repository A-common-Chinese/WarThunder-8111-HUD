@echo off
REM Rebuild FlightLog.exe (needs local Python + PyInstaller)
REM Usage: double-click this file; output: flightlog\dist\FlightLog.exe
REM Note: .wrpl replay import needs wrpl-acmi.exe. This script copies it into dist\
REM       so it sits next to FlightLog.exe. If missing, run fetch_wrpl_acmi.ps1
REM       first (wrpl-acmi is a third-party AGPL-3.0 converter), then rebuild.
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
  echo Build FAILED
  pause
  exit /b 1
)

set "CONVERTER=flightlog\wrpl-acmi.exe"
if exist "%CONVERTER%" (
  copy /y "%CONVERTER%" "flightlog\dist\wrpl-acmi.exe" >nul
  if errorlevel 1 (
    echo WARNING: failed to copy wrpl-acmi.exe into dist\
  ) else (
    echo Copied wrpl-acmi.exe to dist\
  )
) else (
  echo WARNING: %CONVERTER% not found - dist will not include the replay converter
  echo          Run tools\flightlog\fetch_wrpl_acmi.ps1 and rebuild.
)

echo Done: %~dp0..\flightlog\dist\FlightLog.exe
dir /b "flightlog\dist"
pause
