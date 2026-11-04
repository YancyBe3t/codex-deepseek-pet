@echo off
chcp 65001 >nul
cd /d "%~dp0"
set "EXE=%~dp0bin\Release\net8.0-windows\DeepSeekPet.exe"
set "LOG=%~dp0bin\Release\net8.0-windows\pet-error.log"

if exist "%EXE%" goto run

where dotnet >nul 2>nul
if errorlevel 1 (
  echo 没有找到 .NET。请先安装 .NET 8 桌面运行时。
  pause
  exit /b 1
)

echo 首次运行，正在编译，请稍等……
dotnet build -c Release --nologo
if errorlevel 1 (
  echo 编译失败，错误信息在上面。
  pause
  exit /b 1
)

:run
start "" "%EXE%"
powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Sleep -Milliseconds 2200; if (Get-Process -Name DeepSeekPet -ErrorAction SilentlyContinue) { exit 0 } else { exit 1 }" >nul 2>&1
if errorlevel 1 goto failed
exit /b 0

:failed
echo.
echo 宠物没能起来。最近的错误日志：
echo ----------------------------------------
if exist "%LOG%" (type "%LOG%") else (echo 没有日志，说明程序在写日志之前就退出了。)
echo ----------------------------------------
echo 也可以运行「诊断.bat」生成完整报告。
pause
exit /b 1
