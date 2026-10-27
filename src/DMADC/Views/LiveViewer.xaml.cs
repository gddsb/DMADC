using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Controls;
using DMADC.Models;

namespace DMADC.Views;

public partial class LiveViewer : UserControl
{
    public LiveViewer()
    {
        InitializeComponent();
        Loaded += LiveViewer_Loaded;
    }

    private async void LiveViewer_Loaded(object sender, System.Windows.RoutedEventArgs e)
    {
        await RefreshAsync();
    }

    private async void BtnRefresh_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        try
        {
            var state = await App.Services.Config.GetRunStateAsync();
            var schemes = await App.Services.Config.GetAllSchemesAsync();
            var active = schemes.FirstOrDefault(s => s.IsEnabled && s.IsActive)
                         ?? schemes.FirstOrDefault(s => s.IsEnabled);
            if (active == null) { LiveGrid.ItemsSource = new List<object>(); HintText.Text = "没有启用的采集方案"; return; }

            var rules = await App.Services.Config.GetRulesBySchemeAsync(active.SchemeId);
            HintText.Text = state.IsCollecting
                ? $"采集中（方案：{active.SchemeName}，控件数：{rules.Count}）"
                : $"已停止（最后一次快照：{active.SchemeName}）";

            // 由于我们没有持久化"最新快照"表，这里简化显示所有规则
            // 真实场景应该查 collect_log 按 rule_id 取最新一条
            var display = rules.Select(r => new
            {
                RuleName = r.DisplayName,
                Text = "(参考值 — 等待采集写入)",
                IsEnabled = (bool?)null,
                IsVisible = (bool?)null,
                Source = "",
                Timestamp = DateTime.UtcNow
            }).ToList();
            LiveGrid.ItemsSource = display;
        }
        catch (Exception ex)
        {
            HintText.Text = "加载失败：" + ex.Message;
        }
    }
}
