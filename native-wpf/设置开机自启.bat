@echo off
chcp 65001 >nul
cd /d "%~dp0"
set "EXE=%~dp0bin\Release\net8.0-windows\DeepSeekPet.exe"
set "STARTUP=%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup"

if not exist "%EXE%" (
  echo 还没有编译过，请先双击「启动大肥鱼.bat」跑一次。
  pause
  exit /b 1
)

"%EXE%" --autostart-on
if not errorlevel 1 goto ok

echo.
echo 注册表被拦住了，改用「启动」文件夹的方式……
> "%STARTUP%\DeepSeekPet.cmd" (
  echo @echo off
  echo start "" "%EXE%"
)
if errorlevel 1 (
  echo.
  echo 两种方式都被系统拦住了，可以手动来：
  echo   1. 按 Win+R，输入 shell:startup，回车；
  echo   2. 在打开的文件夹里新建指向下面这个 exe 的快捷方式：
  echo      %EXE%
  echo 详细信息见 bin\Release\net8.0-windows\pet-error.log
  pause
  exit /b 1
)
echo.
echo 已在「启动」文件夹放了 DeepSeekPet.cmd，下次登录会自动运行。
echo 想取消就双击「取消开机自启.bat」。
pause
exit /b 0

:ok
echo.
echo 已设置开机自启动：下次登录 Windows 时大肥鱼会自己爬出来。
echo 想取消就双击「取消开机自启.bat」。
pause
exit /b 0
