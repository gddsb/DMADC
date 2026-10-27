using System;

namespace DMADC.Models;

/// <summary>
/// 控件采集规则（映射 control_rule 表）
/// </summary>
public class ControlRule
{
    public int Id { get; set; }
    public string RuleId { get; set; } = Guid.NewGuid().ToString();
    public string SchemeId { get; set; } = "";
    public string DisplayName { get; set; } = "";

    // 控件定位标识（多级降级）
    public string? AutomationId { get; set; }
    public string? ClassName { get; set; }
    public string? NameMatch { get; set; }
    public string? Win32ClassName { get; set; }
    public string? Win32Text { get; set; }
    public int? FallbackIndex { get; set; }

    // 采集内容
    public bool CollectText { get; set; } = true;
    public bool CollectEnabledState { get; set; } = true;
    public bool CollectVisibleState { get; set; } = true;

    // 触发条件
    public bool TriggerOnTextChange { get; set; } = true;
    public bool TriggerOnEnabledChange { get; set; } = true;
    public bool TriggerOnVisibleChange { get; set; } = true;

    public bool IsEnabled { get; set; } = true;
    public string? Remark { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
