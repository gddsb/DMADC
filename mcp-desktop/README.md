# DMADC Windows 桌面控制 MCP Server

让 Trae Agent 在 Windows 桌面上启动程序、输入文字、截屏、操控窗口 —— 通过 Model Context Protocol (MCP)。

## 功能一览

| Tool | 说明 |
|---|---|
| `run_command` | 启动任意程序或命令（notepad, calc, cmd, explorer...） |
| `open_notepad` | 打开记事本 + 可选输入文字 + 可选保存到桌面 |
| `open_url_in_browser` | 用默认浏览器打开网址 |
| `list_windows` | 列出所有可见窗口（标题/PID/坐标） |
| `find_window` | 按标题关键词找窗口 |
| `activate_window` | 把窗口激活到前台 |
| `type_text` | 向当前窗口输入文字（**支持中文和 emoji**，自动走剪贴板） |
| `press_key` | 按键/组合键（enter, ctrl+c, alt+tab...） |
| `mouse_click` | 鼠标点击（指定坐标或当前位置） |
| `mouse_move` | 移动鼠标 |
| `screenshot` | 全屏/区域截屏，返回文件路径 + Base64 |
| `get_mouse_pos` | 鼠标坐标 + 屏幕分辨率 |
| `kill_process` | 按 PID 杀进程 |

## Windows 上安装（3 步）

### 1. 装 Python 3.10+

下载：https://www.python.org/downloads/ → Windows installer

**安装时必须勾选 "Add Python to PATH"**

### 2. 双击 `setup.bat`

自动升级 pip + 装全部依赖 + 验证导入。

### 3. 在 Trae 里配置 MCP

打开 Trae → 设置 → MCP → 添加 → 手动添加，粘贴：

```json
{
  "mcpServers": {
    "windows-desktop": {
      "command": "python",
      "args": ["C:/path/to/DMADC/mcp-desktop/mcp_desktop.py"],
      "env": {
        "PYTHONUNBUFFERED": "1"
      }
    }
  }
}
```

> 把 `C:/path/to/DMADC/` 换成你的实际路径（反斜杠 `/` 或正斜杠 `\` 都行）。

保存后 Trae 会自动加载，稍等几秒就能在 MCP 面板看到 `windows-desktop` 状态为 ✅ 连接。

## 打包成单文件 exe（可选，更方便）

不想让依赖 Python 环境？一行命令打成单文件：

```cmd
pip install pyinstaller
pyinstaller --onefile --name mcp-desktop mcp_desktop.py
```

产物在 `dist\mcp-desktop.exe`，Trae 配置改成：

```json
{
  "mcpServers": {
    "windows-desktop": {
      "command": "C:/path/to/mcp-desktop.exe",
      "args": []
    }
  }
}
```

## 验证连通性

Trae 里对 Agent 说：

> 打开记事本，输入"TRAE 电脑控制测试"

Agent 应该会依次调用 `run_command("notepad.exe")` → `type_text(...)`，记事本窗口弹出并自动填入文字。

## 注意事项

- MCP Server 在 **Windows 本地进程**里运行，能访问完整 Win32 API
- Trae Agent 必须是 **本地模式** 任务；云端模式的 Agent 调不到本地 MCP
- `pyautogui` 有 FAILSAFE：鼠标移到屏幕左上角会强制终止（防失控）
- 中文/emoji 输入自动走剪贴板，英文走键盘 typewrite
- 截屏文件默认存系统临时目录，会在重启清理
