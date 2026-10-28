@echo off
chcp 65001 >nul
title DMADC Windows 桌面 MCP Server — 一键安装
REM =============================================================
REM 双击运行即可：自动检查 Python、安装依赖
REM 完成后按照提示配置 Trae 的 mcp.json
REM =============================================================

echo.
echo ============================================================
echo    DMADC Windows 桌面控制 MCP Server - 一键安装
echo ============================================================
echo.

REM ── 1. 检查 Python ─────────────────────────────────────────
where python >nul 2>nul
if %errorlevel% neq 0 (
    echo [错误] 未检测到 Python，请先安装 Python 3.10+
    echo        下载地址: https://www.python.org/downloads/
    echo        安装时请勾选 "Add Python to PATH"
    pause
    exit /b 1
)

for /f "delims=" %%v in ('python --version 2^>^&1') do set PYVER=%%v
echo [OK] 检测到 %PYVER%

REM ── 2. 升级 pip ──────────────────────────────────────────
echo.
echo [1/3] 升级 pip ...
python -m pip install --upgrade pip -q

REM ── 3. 安装依赖 ──────────────────────────────────────────
echo.
echo [2/3] 安装 MCP Server 依赖（可能需要 1-2 分钟）...
pip install -r "%~dp0requirements.txt"
if %errorlevel% neq 0 (
    echo.
    echo [错误] 依赖安装失败，请检查网络后重试
    pause
    exit /b 1
)

REM ── 4. 验证导入 ──────────────────────────────────────────
echo.
echo [3/3] 验证 MCP Server ...
python -c "from mcp_desktop import mcp; print('  [OK] mcp_desktop 加载成功')" 2>nul
if %errorlevel% neq 0 (
    echo  [警告] 直接导入失败，尝试用 python -m 方式...
    cd /d "%~dp0"
    python -c "import mcp_desktop; print('  [OK] mcp_desktop 加载成功')"
)

echo.
echo ============================================================
echo    ✅ 安装完成！
echo ============================================================
echo.
echo    下一步：在 Trae 里配置 MCP Server
echo.
echo    方式 A（IDE 界面操作）：
echo      设置 -^> MCP -^> 添加 -^> 手动添加
echo      粘贴下方 JSON 配置
echo.
echo    方式 B（直接写配置文件）：
echo      打开文件  %APPDATA%\Trae\User\settings\mcp.json
echo      追加下面这段 JSON 到 mcpServers 对象里
echo.

REM ── 5. 生成建议的 mcp.json 片段 ────────────────────────────
set "HERE=%~dp0"
set "HERE=%HERE:\=/%"
echo    建议的 mcp.json 配置片段：
echo.
echo    {
echo      "mcpServers": {
echo        "windows-desktop": {
echo          "command": "python",
echo          "args": ["%HERE%mcp_desktop.py"],
echo          "env": {
echo            "PYTHONUNBUFFERED": "1"
echo          }
echo        }
echo      }
echo    }
echo.
echo    注意：如果上面路径有空格，请改用短路径（8.3）
echo    或者用 pyinstaller 打包成 exe（见 README）
echo.

pause
