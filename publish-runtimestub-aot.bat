@echo off
chcp 65001 >nul
setlocal enabledelayedexpansion

echo ============================================
echo RuntimeStub Native AOT 单独发布脚本
echo 产物：原生 RuntimeStub.exe（约 4.6MB，零 .NET 依赖）
echo ============================================
echo.

cd /d "%~dp0"

dotnet publish RuntimeStub/RuntimeStub.csproj -c Release -r win-x64

if %errorlevel% neq 0 (
    echo.
    echo 发布失败，请检查错误信息。
    pause
    exit /b %errorlevel%
)

echo.
echo 发布成功！产物路径：
echo RuntimeStub\bin\Release\net10.0\win-x64\publish\RuntimeStub.exe
pause
