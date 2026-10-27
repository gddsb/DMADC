using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DMADC.Models;

namespace DMADC.Services;

/// <summary>
/// 配置读写接口（所有写操作前置检查采集中 → 抛 InvalidOperationException）
/// </summary>
public interface IConfigService
{
    // === 读操作（采集中也可调用）===
    Task<SystemConfig> GetSystemConfigAsync();
    Task<List<CollectionScheme>> GetAllSchemesAsync();
    Task<CollectionScheme?> GetSchemeAsync(string schemeId);
    Task<List<ControlRule>> GetRulesBySchemeAsync(string schemeId);
    Task<RunState> GetRunStateAsync();

    // === 写操作（采集中调用会抛异常）===
    Task AddSchemeAsync(CollectionScheme scheme);
    Task UpdateSchemeAsync(CollectionScheme scheme);
    Task DeleteSchemeAsync(string schemeId);
    Task ActivateSchemeAsync(string schemeId);
    Task AddRuleAsync(ControlRule rule);
    Task UpdateRuleAsync(ControlRule rule);
    Task DeleteRuleAsync(string ruleId);
    Task UpdateSystemConfigAsync(SystemConfig config);
}
