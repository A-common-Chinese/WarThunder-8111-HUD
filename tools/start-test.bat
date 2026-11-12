@echo off
rem WtHud link test launcher - double click, type the peer IP, done.
rem Keep this file ASCII-only: Chinese text in a .bat gets garbled by the OEM codepage.
pushd "%~dp0"

set "PEER=%~1"
if "%PEER%"=="" set /p PEER=Peer virtual IP (e.g. 172.16.1.229):
if "%PEER%"=="" goto :usage

powershell -NoProfile -ExecutionPolicy Bypass -File ".\datalink-test.ps1" -Peer %PEER%
if "%~1"=="" pause
popd
exit /b 0

:usage
echo.
echo No IP given. Run again and type the peer's virtual IP.
if "%~1"=="" pause
popd
exit /b 2
