@echo off
REM =============================================================
REM DMADC Windows Desktop MCP Server - One-click installer
REM Double-click to run. Auto-checks Python, installs deps.
REM =============================================================

REM --- Switch to the script's own directory (robust against spaces/paths) ---
cd /d "%~dp0"

echo.
echo ============================================================
echo    DMADC Windows Desktop MCP Server - Setup
echo ============================================================
echo.

REM --- 1. Check Python ---
where python >nul 2>nul
if errorlevel 1 (
    echo [ERROR] Python not found. Please install Python 3.10+
    echo         Download: https://www.python.org/downloads/
    echo         During install, CHECK "Add Python to PATH"
    pause
    exit /b 1
)

for /f "tokens=*" %%v in ('python --version 2^>^&1') do set PYVER=%%v
echo [OK] Found %PYVER%

REM --- 2. Upgrade pip ---
echo.
echo [1/3] Upgrading pip ...
python -m pip install --upgrade pip -q

REM --- 3. Install dependencies ---
echo.
echo [2/3] Installing MCP Server dependencies (1-2 min) ...
if not exist requirements.txt (
    echo [ERROR] requirements.txt not found in "%cd%"
    pause
    exit /b 1
)
pip install -r requirements.txt
if errorlevel 1 (
    echo.
    echo [ERROR] Dependency install failed. Check network then retry.
    pause
    exit /b 1
)

REM --- 4. Verify ---
echo.
echo [3/3] Verifying MCP Server ...
python -c "import mcp_desktop; print('  [OK] mcp_desktop loaded')" 2>nul
if errorlevel 1 (
    echo [WARN] Import test failed. Check that requirements installed cleanly.
)

echo.
echo ============================================================
echo    Setup complete !
echo ============================================================
echo.
echo    Next step: configure Trae MCP Server
echo.
echo    In Trae, open Settings - MCP - Add - Manual, then paste:
echo.

REM --- 5. Build the mcp.json snippet (normalize backslashes to forward slashes) ---
set "HERE=%cd:\=/%"
echo    {
echo      "mcpServers": {
echo        "windows-desktop": {
echo          "command": "python",
echo          "args": ["%HERE%/mcp_desktop.py"],
echo          "env": { "PYTHONUNBUFFERED": "1" }
echo        }
echo      }
echo    }
echo.
echo    Save it, Trae will auto-connect.
echo    Note: path with spaces works fine in JSON.
echo.

pause
