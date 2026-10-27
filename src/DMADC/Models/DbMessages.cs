using System;
using System.Collections.Generic;

namespace DMADC.Models;

public enum RecordType { Change, Heartbeat }
public enum UiaSource { Uia, Win32, NotFound }

/// <summary>
/// 投递到 SqliteWriter Channel 的统一消息基类
/// </summary>
public abstract class DbMessage
{
    public DateTime Timestamp { get; } = DateTime.UtcNow;
}

/// <summary>
/// 采集记录消息（变化触发 / 定时心跳）
/// </summary>
public class CollectRecordMessage : DbMessage
{
    public required RecordType RecordType { get; init; }
    public required string SchemeId { get; init; }
    public required string RuleId { get; init; }
    public required string RuleName { get; init; }
    public string? WindowTitle { get; init; }
    public string? TextValue { get; init; }
    public bool? IsEnabled { get; init; }
    public bool? IsVisible { get; init; }
    public UiaSource Source { get; init; }
    public List<string>? DiffFields { get; init; }
}

/// <summary>
/// 应用运行状态更新消息（采集中/停止、最后心跳等）
/// </summary>
public class AppStateMessage : DbMessage
{
    public bool? IsCollecting { get; init; }
    public bool? IsConfigLocked { get; init; }
    public string? ActiveSchemeId { get; init; }
    public string? LastHeartbeat { get; init; }
    public string? TargetWindowTitle { get; init; }
    public string? LastError { get; init; }
}
