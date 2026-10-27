using System;
using System.Drawing;
using System.Windows.Forms;

namespace DMADC.Services;

/// <summary>
/// 基于 System.Windows.Forms.NotifyIcon 的托盘实现
/// </summary>
public class TrayIconService : ITrayIconService
{
    private readonly NotifyIcon _notify;
    private readonly ToolStripMenuItem _startItem;
    private readonly ToolStripMenuItem _stopItem;

    public event EventHandler? ShowMainWindowRequested;
    public event EventHandler? StartCollectRequested;
    public event EventHandler? StopCollectRequested;
    public event EventHandler? ExitRequested;

    public TrayIconService()
    {
        _notify = new NotifyIcon
        {
            Icon = SystemIcons.Application, // TODO: 替换为项目自定义图标
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
    }
}
