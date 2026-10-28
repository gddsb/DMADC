@echo off
REM ============================================================
REM  DMADC — 一键发布 self-contained 单文件 exe（零依赖）
REM  前提：Windows 10/11 + .NET 8 SDK
REM  用法：双击此文件，或 cmd 里运行
REM  产出：publish\DMADC-win-x64\DMADC.exe —— 单文件，用户双击即用
REM ============================================================

@echo on
setlocal

cd /d "%~dp0"

echo === 清理旧产物 ===
if exist publish\DMADC-win-x64 rmdir /s /q publish\DMADC-win-x64

echo === Restore ===
dotnet restore src\DMADC\DMADC.csproj
if errorlevel 1 goto :err

echo === Publish Self-Contained Single-File ===
dotnet publish src\DMADC\DMADC.csproj ^
    -c Release ^
    -r win-x64 ^
    --self-contained true ^
    -p:PublishSingleFile=true ^
    -p:IncludeNativeLibrariesForSelfExtract=true ^
    -p:EnableCompressionInSingleFile=true ^
    -o publish\DMADC-win-x64
if errorlevel 1 goto :err

echo.
echo === 成功！===
echo 产物：publish\DMADC-win-x64\DMADC.exe
echo 用户下载后双击即用，无需安装任何运行时。
pause
exit /b 0

:err
echo.
echo === 失败！===
pause
exit /b 1
