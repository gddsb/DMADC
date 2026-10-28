using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using DMADC.Models;
using DMADC.Services;

using MessageBox = System.Windows.MessageBox;

namespace DMADC.Views;

public partial class MainWindow : Window
{
    private static MainWindow? _instance;
    private bool _loading;

    public static MainWindow? GetInstance() => _instance;

    public MainWindow()
    {
        InitializeComponent();
        _instance = this;

        var svc = App.Services;
        svc.Collection.RunningChanged += Collection_RunningChanged;
        svc.Collection.StatusMessage += Collection_StatusMessage;
        Loaded += MainWindow_Loaded;
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _instance = null;
        // 关闭主窗口 = 最小化到托盘
        Hide();
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await RefreshAllAsync();
    }

    private async Task RefreshAllAsync()
    {
        _loading = true;
        try
        {
            await RefreshSchemesAsync();
            await RefreshSettingsAsync();
            await RefreshRunStateAsync();
        }
        finally { _loading = false; }
    }

    private async Task RefreshSchemesAsync()
    {
        var schemes = await App.Services.Config.GetAllSchemesAsync();
        var rulesByScheme = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<ControlRule>>();
        foreach (var s in schemes)
        {
            s.Rules = await App.Services.Config.GetRulesBySchemeAsync(s.SchemeId);
        }
        SchemeGrid.ItemsSource = schemes;
    }

    private async Task RefreshSettingsAsync()
    {
        var cfg = await App.Services.Config.GetSystemConfigAsync();
        ChkRequirePwd.IsChecked = cfg.RequirePasswordOnOpen;
        ChkAutoStart.IsChecked = cfg.AutoStart;
        TxtPollingMs.Text = cfg.PollingIntervalMs.ToString();
        TxtHeartbeatSec.Text = cfg.HeartbeatIntervalSec.ToString();
    }

    private async Task RefreshRunStateAsync()
    {
        var state = await App.Services.Config.GetRunStateAsync();
        IsCollecting = state.IsCollecting;
        RunStateText.Text = state.IsConfigLocked ? "（配置锁定中）" : "";
        DbPathText.Text = "数据库：" + App.Services.DbPath;

        BtnStart.IsEnabled = !state.IsCollecting;
        BtnStop.IsEnabled = state.IsCollecting;
    }

    public bool IsCollecting { get; private set; }

    private void Collection_RunningChanged(object? sender, bool running)
    {
        Dispatcher.InvokeAsync(() =>
        {
            IsCollecting = running;
            StatusText.Text = running ? "状态：采集中" : "状态：已停止";
            BtnStart.IsEnabled = !running;
            BtnStop.IsEnabled = running;
            _ = RefreshRunStateAsync();
        });
    }

    private void Collection_StatusMessage(object? sender, string msg)
    {
        Dispatcher.InvokeAsync(() => LastMessageText.Text = msg);
    }

    private async void BtnStart_Click(object sender, RoutedEventArgs e)
    {
        await App.Services.Collection.StartAsync();
    }

    private async void BtnStop_Click(object sender, RoutedEventArgs e)
    {
        await App.Services.Collection.StopAsync();
    }

    private async void BtnNewScheme_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dlg = new Views.ConfigWizard(null);
            if (dlg.ShowDialog() == true)
                await RefreshSchemesAsync();
        }
        catch (InvalidOperationException ex)
        {
            MessageBox.Show(ex.Message, "不能修改配置", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void BtnEditScheme_Click(object sender, RoutedEventArgs e)
    {
        if (SchemeGrid.SelectedItem is not CollectionScheme scheme) return;
        try
        {
            var dlg = new Views.ConfigWizard(scheme.SchemeId);
            if (dlg.ShowDialog() == true)
                await RefreshSchemesAsync();
        }
        catch (InvalidOperationException ex)
        {
            MessageBox.Show(ex.Message, "不能修改配置", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void BtnDeleteScheme_Click(object sender, RoutedEventArgs e)
    {
        if (SchemeGrid.SelectedItem is not CollectionScheme scheme) return;
        if (MessageBox.Show($"确定要删除方案 \"{scheme.SchemeName}\" 及其所有控件规则吗？",
                "确认", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        try
        {
            await App.Services.Config.DeleteSchemeAsync(scheme.SchemeId);
            await RefreshSchemesAsync();
        }
        catch (InvalidOperationException ex)
        {
            MessageBox.Show(ex.Message, "不能修改配置", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void BtnActivateScheme_Click(object sender, RoutedEventArgs e)
    {
        if (SchemeGrid.SelectedItem is not CollectionScheme scheme) return;
        try
        {
            await App.Services.Config.ActivateSchemeAsync(scheme.SchemeId);
            await RefreshSchemesAsync();
        }
        catch (InvalidOperationException ex)
        {
            MessageBox.Show(ex.Message, "不能修改配置", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void BtnChangePwd_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Views.PasswordDialog();
        dlg.Owner = this;
        dlg.ShowDialog();
    }

    private async void BtnSaveSettings_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var cfg = await App.Services.Config.GetSystemConfigAsync();
            cfg.RequirePasswordOnOpen = ChkRequirePwd.IsChecked == true;
            cfg.AutoStart = ChkAutoStart.IsChecked == true;

            if (int.TryParse(TxtPollingMs.Text, out int pi) && pi >= 100) cfg.PollingIntervalMs = pi;
            else { MessageBox.Show("轮询间隔必须 >= 100 毫秒"); return; }

            if (int.TryParse(TxtHeartbeatSec.Text, out int hi) && hi >= 1) cfg.HeartbeatIntervalSec = hi;
            else { MessageBox.Show("心跳间隔必须 >= 1 秒"); return; }

            await App.Services.Config.UpdateSystemConfigAsync(cfg);
            Helpers.StartupManager.SetAutoStart(cfg.AutoStart);
            MessageBox.Show("设置已保存", "DMADC", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (InvalidOperationException ex)
        {
            MessageBox.Show(ex.Message, "不能修改配置", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show("保存失败：" + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
