using System;

namespace DMADC.Models;

/// <summary>
/// 系统配置（映射 system_config 表，单行）
/// </summary>
public class SystemConfig
{
    public int RowId { get; set; }
    public string PasswordHash { get; set; } = "";
    public string PasswordSalt { get; set; } = "";
    public int PollingIntervalMs { get; set; } = 1000;
    public int HeartbeatIntervalSec { get; set; } = 30;
    public bool AutoStart { get; set; }
    public bool RequirePasswordOnOpen { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
