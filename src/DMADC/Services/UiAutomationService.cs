using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using DMADC.Helpers;
using DMADC.Models;

namespace DMADC.Services;

/// <summary>
/// UI Automation + Win32 双通道实现
/// </summary>
public class UiAutomationService : IUiAutomationService
{
    public AutomationElement? FindTargetWindow(string? titlePattern, string? className)
    {
        // 1. 尝试 UIA 查找
        try
        {
            var conditions = new List<Condition>();
            conditions.Add(new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Window));

            if (!string.IsNullOrEmpty(className))
                conditions.Add(new PropertyCondition(AutomationElement.ClassNameProperty, className));

            var scope = AutomationElement.RootElement;
            AutomationElementCollection matches;

            if (!string.IsNullOrEmpty(titlePattern))
            {
                // 通配符：用自定义条件过滤
                string regex = "^" + System.Text.RegularExpressions.Regex
                    .Escape(titlePattern).Replace(@"\*", ".*") + "$";

                matches = scope.FindAll(TreeScope.Children,
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Window));

                foreach (AutomationElement el in matches)
                {
                    try
                    {
                        string? name = el.Current.Name;
                        if (name != null && System.Text.RegularExpressions.Regex
                                .IsMatch(name, regex, System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                        {
                            if (string.IsNullOrEmpty(className) ||
                                string.Equals(el.Current.ClassName, className, StringComparison.OrdinalIgnoreCase))
                            {
                                return el;
                            }
                        }
                    }
                    catch { }
                }
                return null;
            }

            // 无标题条件，只用类名
            if (conditions.Count > 1)
                return scope.FindFirst(TreeScope.Children, new AndCondition(conditions.ToArray()));
        }
        catch { }

        // 2. 降级到 Win32
        if (!string.IsNullOrEmpty(titlePattern))
        {
            IntPtr hwnd = Win32Interop.FindWindowByTitlePattern(titlePattern);
            if (hwnd != IntPtr.Zero)
            {
                try { return AutomationElement.FromHandle(hwnd); }
                catch { }
            }
        }

        return null;
    }

    public ControlLocationResult LocateControl(AutomationElement window, ControlRule rule)
    {
        var result = new ControlLocationResult();

        try
        {
            // 1. AutomationId 优先
            if (!string.IsNullOrEmpty(rule.AutomationId))
            {
                var el = window.FindFirst(TreeScope.Descendants,
                    new PropertyCondition(AutomationElement.AutomationIdProperty, rule.AutomationId));
                if (el != null)
                {
                    result.Found = true;
                    result.UiaElement = el;
                    result.MatchedBy = "AutomationId";
                    return result;
                }
            }

            // 2. ClassName + Name 组合
            if (!string.IsNullOrEmpty(rule.ClassName) || !string.IsNullOrEmpty(rule.NameMatch))
            {
                var conds = new List<Condition>();
                if (!string.IsNullOrEmpty(rule.ClassName))
                    conds.Add(new PropertyCondition(AutomationElement.ClassNameProperty, rule.ClassName));
                if (!string.IsNullOrEmpty(rule.NameMatch))
                    conds.Add(new PropertyCondition(AutomationElement.NameProperty, rule.NameMatch));

                var matches = window.FindAll(TreeScope.Descendants,
                    conds.Count == 1 ? conds[0] : new AndCondition(conds.ToArray()));

                if (matches.Count > 0)
                {
                    result.Found = true;
                    result.UiaElement = matches[0];
                    result.MatchedBy = "ClassName+Name";
                    return result;
                }
            }

            // 3. 降级到 Win32 枚举
            IntPtr win32Handle = TryWin32Locate(window, rule);
            if (win32Handle != IntPtr.Zero)
            {
                result.Found = true;
                result.UsedWin32Fallback = true;
                result.Win32Handle = win32Handle;
                result.MatchedBy = "Win32";
                try { result.UiaElement = AutomationElement.FromHandle(win32Handle); }
                catch { }
                return result;
            }

            // 4. FallbackIndex
            if (rule.FallbackIndex.HasValue)
            {
                var all = window.FindAll(TreeScope.Descendants, Condition.TrueCondition);
                int idx = rule.FallbackIndex.Value;
                if (idx >= 0 && idx < all.Count)
                {
                    result.Found = true;
                    result.UiaElement = all[idx];
                    result.MatchedBy = "FallbackIndex";
                    return result;
                }
            }
        }
        catch { }

        return result;
    }

    private static IntPtr TryWin32Locate(AutomationElement window, ControlRule rule)
    {
        try
        {
            IntPtr root = (IntPtr)window.Current.NativeWindowHandle;
            if (root == IntPtr.Zero) return IntPtr.Zero;

            IntPtr found = IntPtr.Zero;
            Win32Interop.EnumWindowsProc? callback = null;
            callback = (hwnd, _) =>
            {
                // 只枚举子窗口
                IntPtr parent = GetParent(hwnd);
                if (parent != root) return true;

                if (!string.IsNullOrEmpty(rule.Win32ClassName))
                {
                    var sb = new System.Text.StringBuilder(256);
                    Win32Interop.GetClassName(hwnd, sb, sb.Capacity);
                    if (!string.Equals(sb.ToString(), rule.Win32ClassName, StringComparison.OrdinalIgnoreCase))
                        return true;
                }

                if (!string.IsNullOrEmpty(rule.Win32Text))
                {
                    string text = Win32Interop.GetControlText(hwnd);
                    if (!string.Equals(text, rule.Win32Text, StringComparison.OrdinalIgnoreCase))
                        return true;
                }

                found = hwnd;
                return false;
            };

            // 枚举子窗口
            EnumChildWindows(root, callback, IntPtr.Zero);
            return found;
        }
        catch { return IntPtr.Zero; }
    }

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(IntPtr hWndParent, Win32Interop.EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr GetParent(IntPtr hWnd);

    public ControlSnapshot ReadSnapshot(ControlRule rule, ControlLocationResult location)
    {
        var snap = new ControlSnapshot
        {
            RuleId = rule.RuleId,
            RuleName = rule.DisplayName,
            Found = location.Found,
            Source = location.Found
                ? (location.UsedWin32Fallback ? UiaSource.Win32 : UiaSource.Uia)
                : UiaSource.NotFound
        };

        if (!location.Found) return snap;

        try
        {
            if (location.UiaElement != null)
            {
                var el = location.UiaElement;
                if (rule.CollectText)
                    snap.Text = el.Current.Name;

                if (rule.CollectEnabledState)
                {
                    try { snap.IsEnabled = el.Current.IsEnabled; }
                    catch { snap.IsEnabled = Win32Interop.IsWindowEnabled(el.Current.NativeWindowHandle); }
                }

                if (rule.CollectVisibleState)
                {
                    try { snap.IsVisible = !el.Current.IsOffscreen; }
                    catch { snap.IsVisible = Win32Interop.IsWindowVisible(el.Current.NativeWindowHandle); }
                }
            }
            else if (location.Win32Handle != IntPtr.Zero)
            {
                if (rule.CollectText)
                    snap.Text = Win32Interop.GetControlText(location.Win32Handle);

                if (rule.CollectEnabledState)
                    snap.IsEnabled = Win32Interop.IsWindowEnabled(location.Win32Handle);

                if (rule.CollectVisibleState)
                    snap.IsVisible = Win32Interop.IsWindowVisible(location.Win32Handle);
            }
        }
        catch { /* 读取失败保留默认值 */ }

        return snap;
    }

    public AutomationElement? PickAtPoint(Point screenPoint)
    {
        try
        {
            var el = AutomationElement.FromPoint(screenPoint);
            if (el == null) return null;

            // 向下追溯到最小可交互元素
            var children = el.FindAll(TreeScope.Children, Condition.TrueCondition);
            while (children.Count > 0)
            {
                var bestChild = children.Cast<AutomationElement>()
                    .FirstOrDefault(c => IsUnderPoint(c, screenPoint) && IsInteractive(c));
                if (bestChild == null) break;
                el = bestChild;
                children = el.FindAll(TreeScope.Children, Condition.TrueCondition);
            }

            return el;
        }
        catch { return null; }
    }

    private static bool IsUnderPoint(AutomationElement el, Point p)
    {
        try
        {
            var rect = el.Current.BoundingRectangle;
            return rect.Contains(p);
        }
        catch { return false; }
    }

    private static bool IsInteractive(AutomationElement el)
    {
        try
        {
            var ct = el.Current.ControlType;
            return !int.Equals(ct.Id, ControlType.Custom.Id);
        }
        catch { return true; }
    }
}

// Win32Interop 缺少 GetClassName，补一个
namespace DMADC.Helpers
{
    public static partial class Win32Interop
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);
    }
}
