using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DMADC.Services;

/// <summary>
/// 基于 System.Windows.Forms.NotifyIcon 的托盘实现
/// 图标：优先从 exe 嵌入的 ApplicationIcon 提取，失败时从 Assets/app.ico 读取
/// </summary>
public class TrayIconService : ITrayIconService
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern int ExtractIconEx(string lpszFile, int nIconIndex,
        out IntPtr phiconLarge, out IntPtr phiconSmall, int nIcons);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private readonly NotifyIcon _notify;
    private readonly ToolStripMenuItem _startItem;
    private readonly ToolStripMenuItem _stopItem;
    private readonly Icon _trayIcon;

    public event EventHandler? ShowMainWindowRequested;
    public event EventHandler? StartCollectRequested;
    public event EventHandler? StopCollectRequested;
    public event EventHandler? ExitRequested;

    public TrayIconService()
    {
        _trayIcon = LoadApplicationIcon();

        _notify = new NotifyIcon
        {
            Icon = _trayIcon,
            Visible = false,
            Text = "DMADC - 数据采集监控"
        };

        var menu = new ContextMenuStrip();
        var showItem = new ToolStripMenuItem("显示主窗口");
        showItem.Click += (_, _) => ShowMainWindowRequested?.Invoke(this, EventArgs.Empty);
        menu.Items.Add(showItem);

        menu.Items.Add(new ToolStripSeparator());

        _startItem = new ToolStripMenuItem("开始采集");
        _startItem.Click += (_, _) => StartCollectRequested?.Invoke(this, EventArgs.Empty);
        menu.Items.Add(_startItem);

        _stopItem = new ToolStripMenuItem("停止采集");
        _stopItem.Enabled = false;
        _stopItem.Click += (_, _) => StopCollectRequested?.Invoke(this, EventArgs.Empty);
        menu.Items.Add(_stopItem);

        menu.Items.Add(new ToolStripSeparator());

        var exitItem = new ToolStripMenuItem("退出");
        exitItem.Click += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);
        menu.Items.Add(exitItem);

        _notify.ContextMenuStrip = menu;
        _notify.DoubleClick += (_, _) => ShowMainWindowRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 加载图标：优先级 ExtractIconEx（从 exe ApplicationIcon）→ Assets/app.ico 文件 → 系统默认
    /// </summary>
    private static Icon LoadApplicationIcon()
    {
        try
        {
            string exePath = Environment.ProcessPath
                ?? Assembly.GetExecutingAssembly().Location;

            if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
            {
                if (ExtractIconFromExe(exePath, out var hSmall) && hSmall != IntPtr.Zero)
                {
                    try { return (Icon)Icon.FromHandle(hSmall).Clone(); }
                    finally { DestroyIcon(hSmall); }
                }
            }
        }
        catch { }

        // 回退：从 Assets 目录读取 .ico
        try
        {
            string assetPath = Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico");
            if (File.Exists(assetPath))
                return new Icon(assetPath);
        }
        catch { }

        // 最终兜底
        return SystemIcons.Application;
    }

    private static bool ExtractIconFromExe(string exePath, out IntPtr hSmall)
    {
        hSmall = IntPtr.Zero;
        try
        {
            int count = ExtractIconEx(exePath, 0, out _, out hSmall, 1);
            return count > 0;
        }
        catch { return false; }
    }

    public void Show() { _notify.Visible = true; }
    public void Hide() { _notify.Visible = false; }

    public void UpdateCollectingState(bool isCollecting)
    {
        _startItem.Enabled = !isCollecting;
        _stopItem.Enabled = isCollecting;
        _notify.Text = isCollecting ? "DMADC - 采集中..." : "DMADC - 已停止";
    }

    public void ShowBalloon(string title, string message)
    {
        _notify.ShowBalloonTip(3000, title, message, ToolTipIcon.Info);
    }

    public void Dispose()
    {
        _notify.Visible = false;
        _notify.Dispose();
        _trayIcon.Dispose();
    }
}
