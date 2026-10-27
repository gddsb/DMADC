using System;
using System.Windows.Automation;

namespace DMADC.Models;

/// <summary>
/// 控件定位结果（可能来自 UIA 或 Win32 降级）
/// </summary>
public class ControlLocationResult
{
    public bool Found { get; set; }
    public bool UsedWin32Fallback { get; set; }
    public AutomationElement? UiaElement { get; set; }
    public IntPtr Win32Handle { get; set; }
    public string? MatchedBy { get; set; } // "AutomationId" / "ClassName+Name" / "Win32" / "FallbackIndex"
}
