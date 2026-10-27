using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace DMADC.Helpers;

/// <summary>
/// 开机自启管理 —— 写入/删除 HKCU Run 注册表项
/// </summary>
public static class StartupManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "DMADC";

    public static bool IsAutoStartEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(AppName) != null;
        }
    }

    public static void EnableAutoStart()
    {
        string exePath = Process.GetCurrentProcess().MainModule?.FileName
                         ?? throw new InvalidOperationException("无法获取当前进程路径");

        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        key?.SetValue(AppName, $"\"{exePath}\"");
    }

    public static void DisableAutoStart()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true);
        key?.DeleteValue(AppName, throwOnMissingValue: false);
    }

    public static void SetAutoStart(bool enabled)
    {
        if (enabled) EnableAutoStart();
        else DisableAutoStart();
    }
}
