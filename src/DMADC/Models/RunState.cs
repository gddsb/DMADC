namespace DMADC.Models;

/// <summary>
/// 应用运行状态快照（只读查询返回）
/// </summary>
public record RunState(
    bool IsCollecting,
    bool IsConfigLocked,
    string? ActiveSchemeId,
    string? LastHeartbeat,
    string? TargetWindowTitle,
    string? LastError
);
