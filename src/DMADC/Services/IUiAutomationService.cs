using System;
using System.Windows;
using System.Windows.Automation;
using DMADC.Models;

namespace DMADC.Services;

/// <summary>
/// UI Automation 封装接口 —— 双通道（UIA + Win32）采集
/// </summary>
public interface IUiAutomationService
{
    /// <summary>按窗口标题/类名查找顶层应用窗口</summary>
    AutomationElement? FindTargetWindow(string? titlePattern, string? className);

    /// <summary>在给定窗口内按规则定位控件（自动降级）</summary>
    ControlLocationResult LocateControl(AutomationElement window, ControlRule rule);

    /// <summary>读取控件当前状态（文本、启用、可见）</summary>
    ControlSnapshot ReadSnapshot(ControlRule rule, ControlLocationResult location);

    /// <summary>十字光标拾取：给定屏幕坐标返回最底层的可交互 AutomationElement</summary>
    AutomationElement? PickAtPoint(Point screenPoint);
}
