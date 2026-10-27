using System;
using System.Runtime.InteropServices;
using System.Text;

namespace DMADC.Helpers;

/// <summary>
/// Win32 API 封装 —— 用于 UI Automation 降级和窗口查找
/// </summary>
public static class Win32Interop
{
    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter,
        string? lpszClass, string? lpszWindow);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    public static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool IsWindowEnabled(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    public const uint WM_GETTEXT = 0x000D;
    public const uint WM_GETTEXTLENGTH = 0x000E;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, StringBuilder lParam);

    [DllImport("user32.dll")]
    public static extern int SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    /// <summary>
    /// 获取控件文本（优先 WM_GETTEXT，退化到 GetWindowText）
    /// </summary>
    public static string GetControlText(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero) return string.Empty;

        // 先查长度
        int len = GetWindowTextLength(hWnd);
        if (len <= 0)
        {
            // 某些控件只响应 WM_GETTEXT
            int wmLen = SendMessage(hWnd, WM_GETTEXTLENGTH, IntPtr.Zero, IntPtr.Zero);
            if (wmLen <= 0) return string.Empty;
            var sb2 = new StringBuilder(wmLen + 1);
            SendMessage(hWnd, WM_GETTEXT, new IntPtr(sb2.Capacity), sb2);
            return sb2.ToString();
        }

        var sb = new StringBuilder(len + 1);
        GetWindowText(hWnd, sb, sb.Capacity);
        return sb.ToString();
    }

    /// <summary>
    /// 根据窗口标题通配模式（* 为通配符）查找顶层窗口句柄
    /// </summary>
    public static IntPtr FindWindowByTitlePattern(string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return IntPtr.Zero;

        var regexPattern = "^" + System.Text.RegularExpressions.Regex
            .Escape(pattern).Replace(@"\*", ".*") + "$";

        IntPtr found = IntPtr.Zero;
        EnumWindows((hWnd, _) =>
        {
            if (!IsWindowVisible(hWnd)) return true;
            int len = GetWindowTextLength(hWnd);
            if (len <= 0) return true;
            var sb = new StringBuilder(len + 1);
            GetWindowText(hWnd, sb, sb.Capacity);
            if (System.Text.RegularExpressions.Regex.IsMatch(sb.ToString(), regexPattern,
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                found = hWnd;
                return false; // 停止枚举
            }
            return true;
        }, IntPtr.Zero);

        return found;
    }
}
