@echo off
chcp 65001 >nul
cd /d "%~dp0"
set "EXE=%~dp0bin\Release\net8.0-windows\DeepSeekPet.exe"
set "STARTUP=%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup"

if exist "%EXE%" "%EXE%" --autostart-off
if exist "%STARTUP%\DeepSeekPet.cmd" del /q "%STARTUP%\DeepSeekPet.cmd"

echo.
echo 已取消开机自启动（注册表和「启动」文件夹都清理过了）。
pause
exit /b 0
