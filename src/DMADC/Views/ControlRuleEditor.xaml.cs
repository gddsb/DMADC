using System;
using System.Windows;
using DMADC.Models;

namespace DMADC.Views;

public partial class ControlRuleEditor : Window
{
    private readonly ControlRule _rule;

    public ControlRuleEditor(ControlRule rule)
    {
        InitializeComponent();
        _rule = rule;
        Loaded += (_, _) => Load();
    }

    private void Load()
    {
        TxtDisplayName.Text = _rule.DisplayName;
        ChkRuleEnabled.IsChecked = _rule.IsEnabled;
        TxtAutomationId.Text = _rule.AutomationId ?? "";
        TxtClassName.Text = _rule.ClassName ?? "";
        TxtNameMatch.Text = _rule.NameMatch ?? "";
        TxtWin32Class.Text = _rule.Win32ClassName ?? "";
        TxtWin32Text.Text = _rule.Win32Text ?? "";
        TxtFallbackIndex.Text = _rule.FallbackIndex.HasValue ? _rule.FallbackIndex.Value.ToString() : "";
        ChkCollectText.IsChecked = _rule.CollectText;
        ChkCollectEnabled.IsChecked = _rule.CollectEnabledState;
        ChkCollectVisible.IsChecked = _rule.CollectVisibleState;
        ChkTrigText.IsChecked = _rule.TriggerOnTextChange;
        ChkTrigEnabled.IsChecked = _rule.TriggerOnEnabledChange;
        ChkTrigVisible.IsChecked = _rule.TriggerOnVisibleChange;
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TxtDisplayName.Text))
        {
            MessageBox.Show("请输入控件显示名"); return;
        }
        _rule.DisplayName = TxtDisplayName.Text.Trim();
        _rule.IsEnabled = ChkRuleEnabled.IsChecked == true;
        _rule.AutomationId = string.IsNullOrWhiteSpace(TxtAutomationId.Text) ? null : TxtAutomationId.Text.Trim();
        _rule.ClassName = string.IsNullOrWhiteSpace(TxtClassName.Text) ? null : TxtClassName.Text.Trim();
        _rule.NameMatch = string.IsNullOrWhiteSpace(TxtNameMatch.Text) ? null : TxtNameMatch.Text.Trim();
        _rule.Win32ClassName = string.IsNullOrWhiteSpace(TxtWin32Class.Text) ? null : TxtWin32Class.Text.Trim();
        _rule.Win32Text = string.IsNullOrWhiteSpace(TxtWin32Text.Text) ? null : TxtWin32Text.Text.Trim();
        if (int.TryParse(TxtFallbackIndex.Text, out int idx)) _rule.FallbackIndex = idx;
        else _rule.FallbackIndex = null;

        _rule.CollectText = ChkCollectText.IsChecked == true;
        _rule.CollectEnabledState = ChkCollectEnabled.IsChecked == true;
        _rule.CollectVisibleState = ChkCollectVisible.IsChecked == true;
        _rule.TriggerOnTextChange = ChkTrigText.IsChecked == true;
        _rule.TriggerOnEnabledChange = ChkTrigEnabled.IsChecked == true;
        _rule.TriggerOnVisibleChange = ChkTrigVisible.IsChecked == true;

        DialogResult = true;
        Close();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
