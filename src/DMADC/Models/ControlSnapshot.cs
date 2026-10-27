using System.Collections.Generic;

namespace DMADC.Models;

/// <summary>
/// 某一轮轮询读到的某个控件的快照（用于与上一轮 Diff）
/// </summary>
public class ControlSnapshot
{
    public string RuleId { get; set; } = "";
    public string RuleName { get; set; } = "";
    public bool Found { get; set; }
    public string? Text { get; set; }
    public bool? IsEnabled { get; set; }
    public bool? IsVisible { get; set; }
    public UiaSource Source { get; set; }

    /// <summary>
    /// 与上一个快照对比，返回哪些字段变了（仅包含启用 Trigger 的字段）
    /// </summary>
    public List<string> Diff(ControlSnapshot? previous, ControlRule rule)
    {
        var changed = new List<string>();
        if (previous == null) return changed;

        if (rule.CollectText && rule.TriggerOnTextChange
            && Text != previous.Text)
            changed.Add("text");

        if (rule.CollectEnabledState && rule.TriggerOnEnabledChange
            && IsEnabled != previous.IsEnabled)
            changed.Add("is_enabled");

        if (rule.CollectVisibleState && rule.TriggerOnVisibleChange
            && IsVisible != previous.IsVisible)
            changed.Add("is_visible");

        return changed;
    }
}
