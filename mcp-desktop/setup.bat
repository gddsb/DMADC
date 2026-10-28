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

REM --- 1. Find a REAL working Python ---
REM Windows 10/11 has a Microsoft Store "stub" that where.exe
REM will happily return even when Python is NOT installed.
REM We must actually EXECUTE python -c to confirm it works.

set "PYTHON_CMD="

where py >nul 2>nul
if not errorlevel 1 (
    for /f "delims=" %%p in ('where py 2^>nul ^| findstr /v /i /c:"WindowsApps"') do (
        py -3 -c "print('ok')" >nul 2>nul
        if not errorlevel 1 (
            set "PYTHON_CMD=py -3"
            goto :python_ok
        )
    )
)

where python >nul 2>nul
if not errorlevel 1 (
    REM Filter out Windows Store stubs (AppExecution aliases)
    for /f "delims=" %%p in ('where python 2^>nul ^| findstr /v /i /c:"WindowsApps"') do (
        "%%p" -c "print('ok')" >nul 2>nul
        if not errorlevel 1 (
            set "PYTHON_CMD=python"
            goto :python_ok
        )
    )
)

REM Also try the standard install locations directly
if exist "%LOCALAPPDATA%\Programs\Python\Python312\python.exe" set "PYTHON_CMD=%LOCALAPPDATA%\Programs\Python\Python312\python.exe"
if exist "%LOCALAPPDATA%\Programs\Python\Python311\python.exe" set "PYTHON_CMD=%LOCALAPPDATA%\Programs\Python\Python311\python.exe"
if exist "%LOCALAPPDATA%\Programs\Python\Python310\python.exe" set "PYTHON_CMD=%LOCALAPPDATA%\Programs\Python\Python310\python.exe"
if defined PYTHON_CMD (
    %PYTHON_CMD% -c "print('ok')" >nul 2>nul
    if errorlevel 1 set "PYTHON_CMD="
)

if not defined PYTHON_CMD (
    echo [ERROR] No real Python found.
    echo.
    echo         Please install Python 3.10+ first:
    echo         https://www.python.org/downloads/
    echo.
    echo         IMPORTANT - During install, CHECK these two boxes:
    echo           [x] Add Python to PATH
    echo           [x] Install for all users (recommended)
    echo.
    echo         Or open Microsoft Store and search "Python 3.12".
    pause
    exit /b 1
)

:python_ok
for /f "tokens=*" %%v in ('%PYTHON_CMD% --version 2^>^&1') do set PYVER=%%v
echo [OK] Using %PYVER%  (command: %PYTHON_CMD%)

REM --- 2. Upgrade pip (always via python -m pip, never bare "pip") ---
echo.
echo [1/3] Upgrading pip ...
%PYTHON_CMD% -m pip install --upgrade pip -q

REM --- 3. Install dependencies ---
echo.
echo [2/3] Installing MCP Server dependencies (1-2 min) ...
if not exist requirements.txt (
    echo [ERROR] requirements.txt not found in "%cd%"
    pause
    exit /b 1
)
%PYTHON_CMD% -m pip install -r requirements.txt
if errorlevel 1 (
    echo.
    echo [ERROR] Dependency install failed. Check network then retry.
    echo         If behind a proxy, set HTTP_PROXY / HTTPS_PROXY first.
    pause
    exit /b 1
)

REM --- 4. Verify ---
echo.
echo [3/3] Verifying MCP Server ...
%PYTHON_CMD% -c "import mcp_desktop; print('  [OK] mcp_desktop loaded')" 2>nul
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
