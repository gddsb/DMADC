using System;
using System.Collections.Generic;

namespace DMADC.Models;

/// <summary>
/// 采集方案（一个方案对应一个目标应用窗口，内含多条控件采集规则）
/// 映射 collection_scheme 表
/// </summary>
public class CollectionScheme
{
    public int Id { get; set; }
    public string SchemeId { get; set; } = Guid.NewGuid().ToString();
    public string SchemeName { get; set; } = "";
    public string? WindowTitlePattern { get; set; }
    public string? WindowClassName { get; set; }
    public bool IsEnabled { get; set; } = true;
    public bool IsActive { get; set; }
    /// <summary>NULL 表示使用全局值</summary>
    public int? PollingIntervalMs { get; set; }
    /// <summary>NULL 表示使用全局值</summary>
    public int? HeartbeatIntervalSec { get; set; }
    public string? Remark { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // 导航属性（不持久化）
    public List<ControlRule> Rules { get; set; } = new();
}
