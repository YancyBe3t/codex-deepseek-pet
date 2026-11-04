@echo off
chcp 65001 >nul
cd /d "%~dp0"
set "REPORT=%~dp0诊断报告.txt"
set "EXE=%~dp0bin\Release\net8.0-windows\DeepSeekPet.exe"

> "%REPORT%" echo === 大肥鱼诊断报告 ===
>> "%REPORT%" echo 时间: %DATE% %TIME%
>> "%REPORT%" echo 目录: %~dp0
>> "%REPORT%" echo.
>> "%REPORT%" echo [1] exe 是否存在
if exist "%EXE%" (>> "%REPORT%" echo   OK  %EXE%) else (>> "%REPORT%" echo   缺失 %EXE%)
>> "%REPORT%" echo.
>> "%REPORT%" echo [2] 动画帧
if exist "%~dp0bin\Release\net8.0-windows\assets\frames\idle\f00.png" (>> "%REPORT%" echo   OK) else (>> "%REPORT%" echo   缺失 assets\frames)
>> "%REPORT%" echo.
>> "%REPORT%" echo [3] .NET 运行时
dotnet --list-runtimes > "%TEMP%\pet_rt.txt" 2>&1
type "%TEMP%\pet_rt.txt" | findstr /i "WindowsDesktop" >> "%REPORT%"
>> "%REPORT%" echo.
>> "%REPORT%" echo [4] 直接启动一次
if exist "%EXE%" (
  "%EXE%" --autostart-status
  >> "%REPORT%" echo   启动测试退出码: %ERRORLEVEL%
)
>> "%REPORT%" echo.
>> "%REPORT%" echo [5] 最近日志
if exist "%~dp0bin\Release\net8.0-windows\pet-error.log" (type "%~dp0bin\Release\net8.0-windows\pet-error.log" >> "%REPORT%") else (>> "%REPORT%" echo   没有日志)
>> "%REPORT%" echo.
>> "%REPORT%" echo [6] 开机自启
reg query "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v DeepSeekPet >> "%REPORT%" 2>&1
>> "%REPORT%" echo.

echo 报告已生成：
echo %REPORT%
echo.
type "%REPORT%"
pause
