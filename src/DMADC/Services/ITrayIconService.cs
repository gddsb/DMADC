using System;

namespace DMADC.Services;

/// <summary>
/// 系统托盘接口 —— UI 框架无关（真实实现用 WPF NotifyIcon）
/// </summary>
public interface ITrayIconService : IDisposable
{
    void Show();
    void Hide();
    void UpdateCollectingState(bool isCollecting);
    void ShowBalloon(string title, string message);
    event EventHandler? ShowMainWindowRequested;
    event EventHandler? StartCollectRequested;
    event EventHandler? StopCollectRequested;
    event EventHandler? ExitRequested;
}
