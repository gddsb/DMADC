using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;
using DMADC.Models;

namespace DMADC.Views;

public partial class ControlPicker : Window
{
    private bool _isPicking;
    private AutomationElement? _pickedElement;

    public AutomationElement? PickedElement => _pickedElement;

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int X, int Y);

    [DllImport("user32.dll")]
    private static extern IntPtr SetCursor(IntPtr hCursor);

    [DllImport("user32.dll")]
    private static extern IntPtr LoadCursor(IntPtr hInstance, int lpCursorName);

    private static readonly IntPtr CrossCursor = LoadCursor(IntPtr.Zero, 32515); // IDC_CROSS

    public ControlPicker()
    {
        InitializeComponent();
        PreviewKeyDown += ControlPicker_PreviewKeyDown;
    }

    private void ControlPicker_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (_isPicking) StopPicking();
            else { DialogResult = false; Close(); }
            e.Handled = true;
        }
        else if (e.Key == Key.F1 && !_isPicking)
        {
            StartPicking();
            e.Handled = true;
        }
    }

    private void BtnStartPick_Click(object sender, RoutedEventArgs e)
    {
        StartPicking();
    }

    private System.Windows.Threading.DispatcherTimer? _pickTimer;

    private void StartPicking()
    {
        _isPicking = true;
        BtnStartPick.Content = "⏹ 停止拾取 (Esc)";
        HintText.Text = "移动光标到目标控件上，左键点击确认当前元素";

        Hide(); // 隐藏本窗口避免遮挡
        Task.Delay(100).ContinueWith(_ => Dispatcher.Invoke(() =>
        {
            SetCursor(CrossCursor);
            var t = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(200)
            };
            t.Tick += (_, _) => UpdateUnderCursor();
            t.Start();
            _pickTimer = t;
        }));
    }

    private void UpdateUnderCursor()
    {
        if (!_isPicking) return;
        try
        {
            var point = new System.Windows.Point(System.Windows.Forms.Cursor.Position.X, System.Windows.Forms.Cursor.Position.Y);
            var el = App.Services.Uia.PickAtPoint(point);
            if (el != null)
            {
                ShowElementInfo(el);
                _pickedElement = el;
                BtnConfirm.IsEnabled = true;
            }
        }
        catch { }
    }

    private void ShowElementInfo(AutomationElement el)
    {
        try
        {
            TxtAutomationId.Text = el.Current.AutomationId ?? "(无)";
            TxtClassName.Text = el.Current.ClassName ?? "(无)";
            TxtName.Text = el.Current.Name ?? "(无)";
            TxtEnabled.Text = el.Current.IsEnabled.ToString();
            var handle = el.Current.NativeWindowHandle;
            if (handle != IntPtr.Zero)
            {
                System.Diagnostics.Process? p = null;
                try
                {
                    uint pid;
                    DMADC.Helpers.Win32Interop.GetWindowThreadProcessId(handle, out pid);
                    p = System.Diagnostics.Process.GetProcessById((int)pid);
                }
                catch { }
                TxtProcess.Text = p?.ProcessName ?? "(未知)";
            }
            else
            {
                TxtProcess.Text = "(非顶层窗口)";
            }
            var rect = el.Current.BoundingRectangle;
            TxtBounds.Text = $"X={rect.Left:F0} Y={rect.Top:F0} W={rect.Width:F0} H={rect.Height:F0}";
        }
        catch { }
    }

    private void StopPicking()
    {
        _isPicking = false;
        _pickTimer?.Stop();
        _pickTimer = null;
        SetCursor(IntPtr.Zero);
        BtnStartPick.Content = "🏹 重新拾取 (F1)";
        Show();
    }

    public ControlRule BuildControlRule()
    {
        var rule = new ControlRule { DisplayName = "新控件", IsEnabled = true };
        if (_pickedElement != null)
        {
            try { rule.AutomationId = _pickedElement.Current.AutomationId; } catch { }
            try { rule.ClassName = _pickedElement.Current.ClassName; } catch { }
            try { rule.NameMatch = _pickedElement.Current.Name; } catch { }
        }
        return rule;
    }

    private void BtnConfirm_Click(object sender, RoutedEventArgs e)
    {
        StopPicking();
        DialogResult = true;
        Close();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        StopPicking();
        DialogResult = false;
        Close();
    }
}
