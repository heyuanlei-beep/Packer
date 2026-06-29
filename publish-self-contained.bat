@echo off
chcp 65001 >nul
setlocal enabledelayedexpansion

echo ============================================
echo Packer 单文件发布脚本（自包含）
echo 产物：单个 Packer.exe（约 115MB）
echo 运行要求：目标机器无需安装 .NET 运行即可运行
echo ============================================
echo.

cd /d "%~dp0"

dotnet publish Packer/Packer.csproj -c Release -r win-x64 -p:PublishSingleFile=true -p:SelfContained=true

if %errorlevel% neq 0 (
    echo.
    echo 发布失败，请检查错误信息。
    pause
    exit /b %errorlevel%
)

echo.
echo 发布成功！产物路径：
echo Packer\bin\Release\net10.0-windows\win-x64\publish\Packer.exe
pause
