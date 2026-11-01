@echo off
REM 在 Windows 上构建 Rainclouds Minecraft Launcher (rcl.exe)
REM 前置依赖：安装 .NET 6 SDK (https://dotnet.microsoft.com/download)
setlocal
cd /d %~dp0
dotnet publish src\RCL.UI\RCL.UI.csproj -c Release -r win-x64 --self-contained ^
  -p:PublishSingleFile=true -p:PublishTrimmed=false -o dist
echo.
echo 构建完成，产物位于 dist\rcl.exe
endlocal
pause
