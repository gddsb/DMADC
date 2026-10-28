using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using DMADC.Models;

namespace DMADC.Services;

/// <summary>
/// 配置读写服务 —— 所有写操作前置检查配置锁（采集中禁止写）
/// </summary>
public class ConfigService : IConfigService
{
    private readonly DbReadConnection _db;
    private readonly string _dbPath;

    public ConfigService(DbReadConnection db, string dbPath)
    {
        _db = db;
        _dbPath = dbPath;
    }

    /// <summary>采集中写配置时抛出</summary>
    private static void ThrowIfCollecting(RunState state)
    {
        if (state.IsCollecting)
            throw new InvalidOperationException("采集中，不能修改配置。请先停止采集。");
    }

    // ====== 读 ======

    public Task<SystemConfig> GetSystemConfigAsync() => Task.FromResult(_db.GetSystemConfig());

    public Task<List<CollectionScheme>> GetAllSchemesAsync() => Task.FromResult(_db.GetAllSchemes());

    public Task<CollectionScheme?> GetSchemeAsync(string schemeId) => Task.FromResult(_db.GetScheme(schemeId));

    public Task<List<ControlRule>> GetRulesBySchemeAsync(string schemeId) => Task.FromResult(_db.GetRulesByScheme(schemeId));

    public Task<RunState> GetRunStateAsync() => Task.FromResult(_db.GetRunState());

    // ====== system_config 写 ======

    public async Task UpdateSystemConfigAsync(SystemConfig config)
    {
        ThrowIfCollecting(await GetRunStateAsync());

        using var conn = OpenWriteConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE system_config SET
                password_hash = @ph, password_salt = @ps,
                polling_interval_ms = @pi, heartbeat_interval_sec = @hi,
                auto_start = @as, require_password_on_open = @rpo,
                updated_at = datetime('now')
            WHERE rowid = 1";
        cmd.Parameters.AddWithValue("@ph", config.PasswordHash);
        cmd.Parameters.AddWithValue("@ps", config.PasswordSalt);
        cmd.Parameters.AddWithValue("@pi", config.PollingIntervalMs);
        cmd.Parameters.AddWithValue("@hi", config.HeartbeatIntervalSec);
        cmd.Parameters.AddWithValue("@as", config.AutoStart ? 1 : 0);
        cmd.Parameters.AddWithValue("@rpo", config.RequirePasswordOnOpen ? 1 : 0);
        await cmd.ExecuteNonQueryAsync();
    }

    // ====== collection_scheme ======

    public async Task AddSchemeAsync(CollectionScheme scheme)
    {
        ThrowIfCollecting(await GetRunStateAsync());

        scheme.CreatedAt = DateTime.Now;
        scheme.UpdatedAt = DateTime.Now;
        using var conn = OpenWriteConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO collection_scheme
                (scheme_id, scheme_name, window_title_pattern, window_class_name,
                 is_enabled, is_active, polling_interval_ms, heartbeat_interval_sec,
                 remark, created_at, updated_at)
            VALUES (@sid, @sn, @wtp, @wcn, @ie, @ia, @pi, @hi, @rm, @ca, @ua)";
        BindScheme(cmd, scheme);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task UpdateSchemeAsync(CollectionScheme scheme)
    {
        ThrowIfCollecting(await GetRunStateAsync());

        scheme.UpdatedAt = DateTime.Now;
        using var conn = OpenWriteConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE collection_scheme SET
                scheme_name = @sn, window_title_pattern = @wtp, window_class_name = @wcn,
                is_enabled = @ie, is_active = @ia, polling_interval_ms = @pi,
                heartbeat_interval_sec = @hi, remark = @rm, updated_at = @ua
            WHERE scheme_id = @sid";
        BindScheme(cmd, scheme);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DeleteSchemeAsync(string schemeId)
    {
        ThrowIfCollecting(await GetRunStateAsync());

        using var conn = OpenWriteConnection();
        await using var tx = await conn.BeginTransactionAsync();
        try
        {
            await using (var c1 = conn.CreateCommand())
            {
                c1.CommandText = "DELETE FROM control_rule WHERE scheme_id = @sid";
                c1.Parameters.AddWithValue("@sid", schemeId);
                await c1.ExecuteNonQueryAsync();
            }
            await using (var c2 = conn.CreateCommand())
            {
                c2.CommandText = "DELETE FROM collection_scheme WHERE scheme_id = @sid";
                c2.Parameters.AddWithValue("@sid", schemeId);
                await c2.ExecuteNonQueryAsync();
            }
            await tx.CommitAsync();
        }
        catch { await tx.RollbackAsync(); throw; }
    }

    public async Task ActivateSchemeAsync(string schemeId)
    {
        ThrowIfCollecting(await GetRunStateAsync());

        using var conn = OpenWriteConnection();
        await using var tx = await conn.BeginTransactionAsync();
        try
        {
            await using (var c1 = conn.CreateCommand())
            {
                c1.CommandText = "UPDATE collection_scheme SET is_active = 0";
                await c1.ExecuteNonQueryAsync();
            }
            await using (var c2 = conn.CreateCommand())
            {
                c2.CommandText = "UPDATE collection_scheme SET is_active = 1 WHERE scheme_id = @sid";
                c2.Parameters.AddWithValue("@sid", schemeId);
                await c2.ExecuteNonQueryAsync();
            }
            await tx.CommitAsync();
        }
        catch { await tx.RollbackAsync(); throw; }
    }

    // ====== control_rule ======

    public async Task AddRuleAsync(ControlRule rule)
    {
        ThrowIfCollecting(await GetRunStateAsync());

        rule.CreatedAt = DateTime.Now;
        rule.UpdatedAt = DateTime.Now;
        using var conn = OpenWriteConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO control_rule
                (rule_id, scheme_id, display_name, automation_id, class_name, name_match,
                 win32_class_name, win32_text, fallback_index,
                 collect_text, collect_enabled_state, collect_visible_state,
                 trigger_on_text_change, trigger_on_enabled_change, trigger_on_visible_change,
                 is_enabled, remark, created_at, updated_at)
            VALUES (@rid, @sid, @dn, @ai, @cn, @nm, @wcn, @wt, @fi, @ct, @ces, @cvs,
                    @ttc, @tec, @tvc, @ie, @rm, @ca, @ua)";
        BindRule(cmd, rule);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task UpdateRuleAsync(ControlRule rule)
    {
        ThrowIfCollecting(await GetRunStateAsync());

        rule.UpdatedAt = DateTime.Now;
        using var conn = OpenWriteConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE control_rule SET
                display_name = @dn, automation_id = @ai, class_name = @cn, name_match = @nm,
                win32_class_name = @wcn, win32_text = @wt, fallback_index = @fi,
                collect_text = @ct, collect_enabled_state = @ces, collect_visible_state = @cvs,
                trigger_on_text_change = @ttc, trigger_on_enabled_change = @tec,
                trigger_on_visible_change = @tvc, is_enabled = @ie, remark = @rm,
                updated_at = @ua
            WHERE rule_id = @rid";
        BindRule(cmd, rule);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DeleteRuleAsync(string ruleId)
    {
        ThrowIfCollecting(await GetRunStateAsync());

        using var conn = OpenWriteConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM control_rule WHERE rule_id = @rid";
        cmd.Parameters.AddWithValue("@rid", ruleId);
        await cmd.ExecuteNonQueryAsync();
    }

    // ====== helpers ======

    private SqliteConnection OpenWriteConnection()
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = _dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false
        };
        var conn = new SqliteConnection(builder.ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode = WAL; PRAGMA busy_timeout = 3000;";
        cmd.ExecuteNonQuery();
        return conn;
    }

    private static void BindScheme(SqliteCommand cmd, CollectionScheme s)
    {
        cmd.Parameters.AddWithValue("@sid", s.SchemeId);
        cmd.Parameters.AddWithValue("@sn", s.SchemeName);
        cmd.Parameters.AddWithValue("@wtp", (object?)s.WindowTitlePattern ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@wcn", (object?)s.WindowClassName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ie", s.IsEnabled ? 1 : 0);
        cmd.Parameters.AddWithValue("@ia", s.IsActive ? 1 : 0);
        cmd.Parameters.AddWithValue("@pi", s.PollingIntervalMs.HasValue ? s.PollingIntervalMs.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("@hi", s.HeartbeatIntervalSec.HasValue ? s.HeartbeatIntervalSec.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("@rm", (object?)s.Remark ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ca", s.CreatedAt.ToString("o"));
        cmd.Parameters.AddWithValue("@ua", s.UpdatedAt.ToString("o"));
    }

    private static void BindRule(SqliteCommand cmd, ControlRule r)
    {
        cmd.Parameters.AddWithValue("@rid", r.RuleId);
        cmd.Parameters.AddWithValue("@sid", r.SchemeId);
        cmd.Parameters.AddWithValue("@dn", r.DisplayName);
        cmd.Parameters.AddWithValue("@ai", (object?)r.AutomationId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@cn", (object?)r.ClassName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@nm", (object?)r.NameMatch ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@wcn", (object?)r.Win32ClassName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@wt", (object?)r.Win32Text ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@fi", r.FallbackIndex.HasValue ? r.FallbackIndex.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("@ct", r.CollectText ? 1 : 0);
        cmd.Parameters.AddWithValue("@ces", r.CollectEnabledState ? 1 : 0);
        cmd.Parameters.AddWithValue("@cvs", r.CollectVisibleState ? 1 : 0);
        cmd.Parameters.AddWithValue("@ttc", r.TriggerOnTextChange ? 1 : 0);
        cmd.Parameters.AddWithValue("@tec", r.TriggerOnEnabledChange ? 1 : 0);
        cmd.Parameters.AddWithValue("@tvc", r.TriggerOnVisibleChange ? 1 : 0);
        cmd.Parameters.AddWithValue("@ie", r.IsEnabled ? 1 : 0);
        cmd.Parameters.AddWithValue("@rm", (object?)r.Remark ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ca", r.CreatedAt.ToString("o"));
        cmd.Parameters.AddWithValue("@ua", r.UpdatedAt.ToString("o"));
    }
}
