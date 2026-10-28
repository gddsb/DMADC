"""
DMADC Windows 桌面控制 MCP Server
===================================
功能：让 Trae Agent 远程控制 Windows 桌面（启动程序、输入文字、截屏、操控窗口）

运行：
    # 方式 1：Python 直接跑（需要先 pip install -r requirements.txt）
    python mcp_desktop.py

    # 方式 2：pyinstaller 打成单文件 exe（推荐生产使用）
    pip install pyinstaller
    pyinstaller --onefile --name mcp-desktop mcp_desktop.py

Trae 配置（mcp.json）：
    {
      "mcpServers": {
        "windows-desktop": {
          "command": "python",
          "args": ["C:/path/to/mcp-desktop/mcp_desktop.py"]
        }
      }
    }
    # 或者用 pyinstaller 打包后的 exe：
    {
      "mcpServers": {
        "windows-desktop": {
          "command": "C:/path/to/mcp-desktop.exe",
          "args": []
        }
      }
    }
"""

from __future__ import annotations

import os
import sys
import subprocess
import time
import base64
import tempfile
from pathlib import Path

# mcp 1.x 和 2.x 的 API 不一样，两个都兼容
try:
    from mcp.server.fastmcp import FastMCP as _MCPServer  # mcp 1.x
except ImportError:
    try:
        from mcp.server.fastmcp import MCPServer as _MCPServer  # mcp 2.x renamed
    except ImportError:
        # 终极兜底：直接从 mcp.server 拿
        from mcp.server import Server as _MCPServer  # type: ignore

# FastMCP 别名，让后续代码不动
FastMCP = _MCPServer

# Windows-only 依赖（非 Windows 上会 ImportError，让它在 tool 调用时报友好错误）
_IS_WINDOWS = sys.platform.startswith("win")
if _IS_WINDOWS:
    try:
        import pyautogui          # 屏幕坐标 / 鼠标 / 键盘
        import pygetwindow as gw   # 窗口管理
        from PIL import Image      # 截屏保存
        import win32gui            # 找窗口（比 pygetwindow 更底层）
        import win32con
        import win32process
        import psutil
    except ImportError as e:
        print(f"[mcp-desktop] 缺少依赖：{e}", file=sys.stderr)
        print("[mcp-desktop] 请先运行 setup.bat 或 pip install -r requirements.txt", file=sys.stderr)

    # pyautogui 安全边界（鼠标别跑出屏幕）
    pyautogui.FAILSAFE = True
    pyautogui.PAUSE = 0.05

mcp = FastMCP("windows-desktop")


# ──────────── 辅助 ────────────
def _win_only():
    if not _IS_WINDOWS:
        raise RuntimeError("此 MCP Server 只能在 Windows 上运行")


# ══════════════════════════════════════════════════════════
# Tools
# ══════════════════════════════════════════════════════════

@mcp.tool()
def run_command(command: str, args: str = "", wait_seconds: int = 0) -> dict:
    """
    启动一个程序或命令。

    Args:
        command: 可执行文件名或完整路径，如 "notepad", "calc", "explorer", "cmd"
        args:    命令行参数，用空格分隔，如 "/c dir C:\\"
        wait_seconds: 等待秒数（0 表示后台立即返回，不等待退出）

    Returns:
        {"pid": 进程ID, "returncode": 退出码(仅 wait_seconds>0 时), "stderr": 错误输出}
    """
    _win_only()
    try:
        parts = [command] + ([a for a in args.split() if a] if args else [])
        p = subprocess.Popen(parts, shell=False)
        if wait_seconds > 0:
            try:
                rc = p.wait(timeout=wait_seconds)
                return {"pid": p.pid, "returncode": rc, "ok": True}
            except subprocess.TimeoutExpired:
                return {"pid": p.pid, "returncode": None, "ok": True, "note": f"等待 {wait_seconds}s 超时，进程仍在运行"}
        return {"pid": p.pid, "ok": True}
    except Exception as e:
        return {"pid": None, "ok": False, "error": str(e)}


@mcp.tool()
def open_notepad(text: str = "", save_to_desktop: bool = False, filename: str = "Untitled.txt") -> dict:
    """
    打开记事本，可选填入文字并保存到桌面。

    Args:
        text: 要输入的文字（UTF-8），空则只打开记事本
        save_to_desktop: True 时 Ctrl+S 保存到桌面
        filename: 保存文件名（仅 save_to_desktop=True 时生效）

    Returns:
        操作结果
    """
    _win_only()
    r = run_command("notepad.exe")
    if not r.get("ok"):
        return r
    time.sleep(0.6)

    if text:
        try:
            pyautogui.typewrite(text, interval=0.02)  # 英文+数字
        except Exception:
            # 中文走剪贴板
            import pyperclip
            pyperclip.copy(text)
            pyautogui.hotkey("ctrl", "v")
            time.sleep(0.3)

    if save_to_desktop:
        pyautogui.hotkey("ctrl", "s")
        time.sleep(0.5)
        desktop = Path.home() / "Desktop"
        pyperclip.copy(str(desktop / filename))
        pyautogui.hotkey("ctrl", "a")
        pyautogui.press("delete")
        pyautogui.hotkey("ctrl", "v")
        pyautogui.press("enter")
        time.sleep(0.3)

    return {"ok": True, "pid": r["pid"]}


@mcp.tool()
def open_url_in_browser(url: str) -> dict:
    """
    用默认浏览器打开网址。

    Args:
        url: 完整网址，如 https://github.com

    Returns:
        启动结果
    """
    _win_only()
    try:
        os.startfile(url)  # noqa: PLC2801 (Windows-only)
        return {"ok": True}
    except Exception as e:
        return {"ok": False, "error": str(e)}


@mcp.tool()
def list_windows(visible_only: bool = True) -> list:
    """
    列出当前桌面所有窗口。

    Args:
        visible_only: True 只返回可见窗口

    Returns:
        [{"title": 窗口标题, "pid": 进程ID, "hwnd": 句柄, "bounds": [x,y,w,h]}]
    """
    _win_only()
    results = []

    def _cb(hwnd, _):
        if visible_only and not win32gui.IsWindowVisible(hwnd):
            return
        title = win32gui.GetWindowText(hwnd)
        if not title:
            return
        try:
            _, pid = win32process.GetWindowThreadProcessId(hwnd)
            rect = win32gui.GetWindowRect(hwnd)
            results.append({
                "title": title,
                "pid": int(pid),
                "hwnd": int(hwnd),
                "bounds": {"x": rect[0], "y": rect[1], "w": rect[2] - rect[0], "h": rect[3] - rect[1]},
            })
        except Exception:
            pass

    win32gui.EnumWindows(_cb, None)
    return sorted(results, key=lambda w: w["title"].lower())


@mcp.tool()
def find_window(keyword: str) -> dict | None:
    """
    按标题关键词查找窗口。

    Args:
        keyword: 标题中包含的关键词（大小写不敏感）

    Returns:
        匹配的第一个窗口信息，没找到返回 None
    """
    _win_only()
    for w in list_windows(visible_only=True):
        if keyword.lower() in w["title"].lower():
            return w
    return None


@mcp.tool()
def activate_window(keyword: str) -> dict:
    """
    把窗口激活到前台（按标题关键词匹配）。

    Args:
        keyword: 窗口标题中的关键词

    Returns:
        操作结果
    """
    _win_only()
    w = find_window(keyword)
    if not w:
        return {"ok": False, "error": f"没找到包含 '{keyword}' 的窗口"}
    try:
        import ctypes
        hwnd = w["hwnd"]
        # 先恢复（最小化时）
        if win32gui.IsIconic(hwnd):
            win32gui.ShowWindow(hwnd, win32con.SW_RESTORE)
        # 置顶（兼容被锁前台的情况）
        ctypes.windll.user32.SetForegroundWindow(hwnd)
        win32gui.SetForegroundWindow(hwnd)
        return {"ok": True, "window": w["title"]}
    except Exception as e:
        return {"ok": False, "error": str(e)}


@mcp.tool()
def type_text(text: str, interval_ms: int = 15) -> dict:
    """
    向当前激活的窗口输入文字（支持中文，走剪贴板）。

    Args:
        text: 要输入的文字
        interval_ms: 英文单字间隔毫秒（中文忽略，整体粘贴）

    Returns:
        操作结果
    """
    _win_only()
    try:
        import pyperclip
        # 检测是否含非 ASCII（中文、emoji 等）
        has_unicode = any(ord(c) > 127 for c in text)
        if has_unicode:
            pyperclip.copy(text)
            pyautogui.hotkey("ctrl", "v")
        else:
            pyautogui.typewrite(text, interval=interval_ms / 1000.0)
        return {"ok": True, "chars": len(text)}
    except Exception as e:
        return {"ok": False, "error": str(e)}


@mcp.tool()
def press_key(key: str) -> dict:
    """
    按下一个按键。

    Args:
        key: 按键名，如 "enter", "tab", "esc", "f5", "ctrl+c", "alt+tab"

    Returns:
        操作结果
    """
    _win_only()
    try:
        if "+" in key.lower():
            keys = [k.strip() for k in key.split("+") if k.strip()]
            pyautogui.hotkey(*keys)
        else:
            pyautogui.press(key)
        return {"ok": True, "key": key}
    except Exception as e:
        return {"ok": False, "error": str(e)}


@mcp.tool()
def mouse_click(x: int | None = None, y: int | None = None, button: str = "left") -> dict:
    """
    鼠标点击。不传 x/y 则点击当前位置。

    Args:
        x: 屏幕 X 坐标（像素）
        y: 屏幕 Y 坐标（像素）
        button: "left" | "right" | "middle"

    Returns:
        实际点击坐标
    """
    _win_only()
    try:
        if x is not None and y is not None:
            pyautogui.click(x=x, y=y, button=button)
            return {"ok": True, "x": x, "y": y}
        else:
            cur_x, cur_y = pyautogui.position()
            pyautogui.click(button=button)
            return {"ok": True, "x": cur_x, "y": cur_y}
    except Exception as e:
        return {"ok": False, "error": str(e)}


@mcp.tool()
def mouse_move(x: int, y: int) -> dict:
    """把鼠标移动到指定坐标"""
    _win_only()
    try:
        pyautogui.moveTo(x, y)
        return {"ok": True, "x": x, "y": y}
    except Exception as e:
        return {"ok": False, "error": str(e)}


@mcp.tool()
def screenshot(path: str | None = None, region: list[int] | None = None) -> dict:
    """
    截屏并保存，返回文件路径 + Base64 预览。

    Args:
        path: 保存路径（可选）；不传则存临时文件
        region: 区域 [left, top, width, height]；不传则全屏

    Returns:
        {"path": 保存路径, "size": [w,h], "base64": "data:image/png;base64,..."}
    """
    _win_only()
    try:
        if path is None:
            path = str(Path(tempfile.gettempdir()) / f"dmadc_screenshot_{int(time.time())}.png")

        if region:
            img = pyautogui.screenshot(region=tuple(region))
        else:
            img = pyautogui.screenshot()

        img.save(path)
        b64 = base64.b64encode(Path(path).read_bytes()).decode()
        return {
            "ok": True,
            "path": path,
            "size": [img.width, img.height],
            "base64": f"data:image/png;base64,{b64}",
        }
    except Exception as e:
        return {"ok": False, "error": str(e)}


@mcp.tool()
def get_mouse_pos() -> dict:
    """获取当前鼠标坐标 + 屏幕分辨率"""
    _win_only()
    try:
        x, y = pyautogui.position()
        sx, sy = pyautogui.size()
        return {"mouse": {"x": int(x), "y": int(y)}, "screen": {"w": int(sx), "h": int(sy)}}
    except Exception as e:
        return {"ok": False, "error": str(e)}


@mcp.tool()
def kill_process(pid: int) -> dict:
    """按 PID 杀掉进程"""
    _win_only()
    try:
        p = psutil.Process(pid)
        p.terminate()
        return {"ok": True, "pid": pid}
    except Exception as e:
        return {"ok": False, "error": str(e)}


# ──────────── main ────────────
if __name__ == "__main__":
    if not _IS_WINDOWS:
        print("⚠️ 此 MCP Server 必须在 Windows 上运行（当前：" + sys.platform + "）", file=sys.stderr)
        # 不退出，让 stdio 握手失败更友好
    print(f"[mcp-desktop] DMADC Windows 桌面 MCP Server 启动中...", file=sys.stderr)
    print(f"[mcp-desktop] 依赖检查: pyautogui={_IS_WINDOWS}", file=sys.stderr)
    mcp.run()
