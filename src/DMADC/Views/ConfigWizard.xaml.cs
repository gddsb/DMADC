using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using DMADC.Models;
using DMADC.Services;

namespace DMADC.Views;

public partial class ConfigWizard : Window
{
    private readonly IConfigService _config = App.Services.Config;
    private CollectionScheme? _scheme;
    private readonly List<ControlRule> _rules = new();

    public ConfigWizard(string? schemeId)
    {
        InitializeComponent();
        Loaded += async (_, _) => await LoadAsync(schemeId);
    }

    private async Task LoadAsync(string? schemeId)
    {
        if (schemeId != null)
        {
            _scheme = await _config.GetSchemeAsync(schemeId);
            if (_scheme == null) { Close(); return; }
            _rules.Clear();
            _rules.AddRange(await _config.GetRulesBySchemeAsync(schemeId));

            TxtSchemeName.Text = _scheme.SchemeName;
            TxtTitlePattern.Text = _scheme.WindowTitlePattern ?? "";
            TxtClassName.Text = _scheme.WindowClassName ?? "";
            ChkEnabled.IsChecked = _scheme.IsEnabled;
            Title = "编辑方案：" + _scheme.SchemeName;
        }
        else
        {
            _scheme = new CollectionScheme { SchemeName = "新方案", IsEnabled = true };
            Title = "新建采集方案";
        }
        RuleGrid.ItemsSource = _rules;
    }

    private async void BtnPick_Click(object sender, RoutedEventArgs e)
    {
        var picker = new Views.ControlPicker();
        picker.Owner = this;
        if (picker.ShowDialog() == true && picker.PickedElement != null)
        {
            var rule = picker.BuildControlRule();
            rule.SchemeId = _scheme!.SchemeId;
            _rules.Add(rule);
            RuleGrid.Items.Refresh();
        }
    }

    private void BtnAddRule_Click(object sender, RoutedEventArgs e)
    {
        var rule = new ControlRule
        {
            RuleId = Guid.NewGuid().ToString(),
            SchemeId = _scheme!.SchemeId,
            DisplayName = "新控件",
            IsEnabled = true
        };
        _rules.Add(rule);
        RuleGrid.Items.Refresh();
    }

    private void BtnEditRule_Click(object sender, RoutedEventArgs e)
    {
        // 简单内联编辑：双击即可。这里用 Message 提示（可改成专门的规则编辑窗口）
        if (RuleGrid.SelectedItem is ControlRule r)
        {
            var edit = new Views.ControlRuleEditor(r);
            edit.Owner = this;
            if (edit.ShowDialog() == true) RuleGrid.Items.Refresh();
        }
    }

    private void BtnDelRule_Click(object sender, RoutedEventArgs e)
    {
        if (RuleGrid.SelectedItem is ControlRule r)
        {
            _rules.Remove(r);
            RuleGrid.Items.Refresh();
        }
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private async void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        if (_scheme == null) return;
        if (string.IsNullOrWhiteSpace(TxtSchemeName.Text))
        {
            MessageBox.Show("请输入方案名称"); return;
        }

        try
        {
            _scheme.SchemeName = TxtSchemeName.Text.Trim();
            _scheme.WindowTitlePattern = string.IsNullOrWhiteSpace(TxtTitlePattern.Text) ? null : TxtTitlePattern.Text.Trim();
            _scheme.WindowClassName = string.IsNullOrWhiteSpace(TxtClassName.Text) ? null : TxtClassName.Text.Trim();
            _scheme.IsEnabled = ChkEnabled.IsChecked == true;

            if (_scheme.Id == 0)
                await _config.AddSchemeAsync(_scheme);
            else
                await _config.UpdateSchemeAsync(_scheme);

            // 保存控件规则：先全删再全加（简单策略）
            if (_scheme.Id != 0)
            {
                var existing = await _config.GetRulesBySchemeAsync(_scheme.SchemeId);
                foreach (var r in existing)
                    await _config.DeleteRuleAsync(r.RuleId);
            }
            foreach (var r in _rules)
            {
                r.SchemeId = _scheme.SchemeId;
                await _config.AddRuleAsync(r);
            }

            DialogResult = true;
            Close();
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
