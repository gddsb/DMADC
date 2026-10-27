using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using DMADC.Models;

namespace DMADC.Services;

/// <summary>
/// 采集引擎实现 —— 单进程后台线程，快照 Diff + 定时心跳
/// </summary>
public class DataCollectionService : IDataCollectionService
{
    private readonly IConfigService _config;
    private readonly IUiAutomationService _uia;
    private readonly ISqliteWriter _writer;

    private CancellationTokenSource? _cts;
    private Task? _pollLoop;

    // 上一轮快照缓存（方案ID + 规则ID → 快照）
    private readonly ConcurrentDictionary<string, ControlSnapshot> _previous = new();

    public bool IsRunning { get; private set; }
    public event EventHandler<bool>? RunningChanged;
    public event EventHandler<string>? StatusMessage;

    public DataCollectionService(
        IConfigService config,
        IUiAutomationService uia,
        ISqliteWriter writer)
    {
        _config = config;
        _uia = uia;
        _writer = writer;
    }

    public async Task StartAsync()
    {
        if (IsRunning) return;

        // 取当前激活的方案
        var schemes = await _config.GetAllSchemesAsync();
        var active = schemes.FirstOrDefault(s => s.IsEnabled && s.IsActive)
                     ?? schemes.FirstOrDefault(s => s.IsEnabled);

        if (active == null)
        {
            OnStatusMessage("没有可用的采集方案");
            return;
        }

        var rules = await _config.GetRulesBySchemeAsync(active.SchemeId);
        rules = rules.Where(r => r.IsEnabled).ToList();

        OnStatusMessage($"启动采集：方案[{active.SchemeName}]，规则数={rules.Count}");

        // 切换配置锁
        await _writer.WriteAsync(new AppStateMessage
        {
            IsCollecting = true,
            IsConfigLocked = true,
            ActiveSchemeId = active.SchemeId,
            LastError = null,
            TargetWindowTitle = null
        });

        IsRunning = true;
        RunningChanged?.Invoke(this, true);

        _cts = new CancellationTokenSource();
        _previous.Clear();
        _pollLoop = Task.Run(() => PollLoopAsync(active, rules, _cts.Token));
    }

    public async Task StopAsync()
    {
        if (!IsRunning) return;

        OnStatusMessage("停止采集...");
        _cts?.Cancel();

        if (_pollLoop != null)
        {
            try { await _pollLoop; }
            catch (OperationCanceledException) { }
        }

        await _writer.WriteAsync(new AppStateMessage
        {
            IsCollecting = false,
            IsConfigLocked = false,
            LastHeartbeat = null
        });

        IsRunning = false;
        RunningChanged?.Invoke(this, false);
        OnStatusMessage("已停止");
    }

    private async Task PollLoopAsync(CollectionScheme scheme, List<ControlRule> rules, CancellationToken ct)
    {
        var sysCfg = await _config.GetSystemConfigAsync();
        int pollInterval = scheme.PollingIntervalMs ?? sysCfg.PollingIntervalMs;
        int heartbeatSec = scheme.HeartbeatIntervalSec ?? sysCfg.HeartbeatIntervalSec;

        int tickCount = 0;
        var heartbeatIntervalMs = heartbeatSec * 1000;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                // Step 1: 找目标窗口
                var window = _uia.FindTargetWindow(scheme.WindowTitlePattern, scheme.WindowClassName);
                string? windowTitle = window?.Current.Name;

                if (window == null)
                {
                    await _writer.WriteAsync(new AppStateMessage
                    {
                        TargetWindowTitle = null,
                        LastError = $"目标窗口未找到（{scheme.WindowTitlePattern ?? "无标题模式"}）"
                    });
                    OnStatusMessage("目标窗口未找到，等待下一轮...");
                    await Task.Delay(pollInterval, ct);
                    continue;
                }

                await _writer.WriteAsync(new AppStateMessage { TargetWindowTitle = windowTitle });

                // Step 2 & 3: 遍历规则，定位 + 读取 + Diff
                var currentSnapshots = new Dictionary<string, ControlSnapshot>();
                var changeRecords = new List<CollectRecordMessage>();

                foreach (var rule in rules)
                {
                    string key = $"{scheme.SchemeId}:{rule.RuleId}";
                    var location = _uia.LocateControl(window, rule);
                    var snap = _uia.ReadSnapshot(rule, location);
                    snap.RuleId = rule.RuleId;
                    snap.RuleName = rule.DisplayName;

                    currentSnapshots[key] = snap;

                    var previous = _previous.TryGetValue(key, out var prev) ? prev : null;
                    var diffs = snap.Diff(previous, rule);

                    if (diffs.Count > 0)
                    {
                        changeRecords.Add(new CollectRecordMessage
                        {
                            RecordType = RecordType.Change,
                            SchemeId = scheme.SchemeId,
                            RuleId = rule.RuleId,
                            RuleName = rule.DisplayName,
                            WindowTitle = windowTitle,
                            TextValue = snap.Text,
                            IsEnabled = snap.IsEnabled,
                            IsVisible = snap.IsVisible,
                            Source = snap.Source,
                            DiffFields = diffs
                        });
                    }
                }

                // 写入变化记录
                foreach (var record in changeRecords)
                    await _writer.WriteAsync(record);

                // Step 4: 定时心跳
                tickCount++;
                if ((tickCount * pollInterval) >= heartbeatIntervalMs)
                {
                    tickCount = 0;
                    OnStatusMessage($"心跳 #{DateTime.Now:HH:mm:ss} —— {currentSnapshots.Count} 个控件");

                    foreach (var kv in currentSnapshots)
                    {
                        var snap = kv.Value;
                        await _writer.WriteAsync(new CollectRecordMessage
                        {
                            RecordType = RecordType.Heartbeat,
                            SchemeId = scheme.SchemeId,
                            RuleId = snap.RuleId,
                            RuleName = snap.RuleName,
                            WindowTitle = windowTitle,
                            TextValue = snap.Text,
                            IsEnabled = snap.IsEnabled,
                            IsVisible = snap.IsVisible,
                            Source = snap.Source,
                            DiffFields = null
                        });
                    }

                    await _writer.WriteAsync(new AppStateMessage
                    {
                        LastHeartbeat = DateTime.UtcNow.ToString("o"),
                        LastError = null
                    });
                }

                // 更新快照缓存
                _previous.Clear();
                foreach (var kv in currentSnapshots)
                    _previous[kv.Key] = kv.Value;
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                // 异常隔离：不退出循环
                OnStatusMessage($"采集异常：{ex.Message}");
                await _writer.WriteAsync(new AppStateMessage { LastError = ex.Message });
            }

            try { await Task.Delay(pollInterval, ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    protected virtual void OnStatusMessage(string msg)
    {
        StatusMessage?.Invoke(this, msg);
    }
}
