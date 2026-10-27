using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using DMADC.Models;

namespace DMADC.Services;

/// <summary>
/// 独立只读连接 —— 用于 ConfigService / PasswordService 的配置读写
/// 不走 Channel 队列，直接开独立只读连接，避免背压延迟
/// </summary>
public class DbReadConnection : IAsyncDisposable
{
    private readonly string _dbPath;
    private SqliteConnection? _conn;
    private bool _disposed;

    public DbReadConnection(string dbPath)
    {
        _dbPath = dbPath;
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
    }

    private SqliteConnection EnsureOpen()
    {
        if (_conn != null) return _conn;

        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = _dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false
        };
        _conn = new SqliteConnection(builder.ConnectionString);
        _conn.Open();

        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode = WAL; PRAGMA busy_timeout = 3000;";
        cmd.ExecuteNonQuery();

        return _conn;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        if (_conn != null)
        {
            await _conn.DisposeAsync();
            _conn = null;
        }
    }

    // ====== 运行状态查询 ======

    public RunState GetRunState()
    {
        using var cmd = EnsureOpen().CreateCommand();
        cmd.CommandText = @"SELECT is_collecting, is_config_locked, active_scheme_id,
                                   last_heartbeat, target_window_title, last_error
                            FROM app_state WHERE rowid = 1";
        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
            return new RunState(false, false, null, null, null, null);

        return new RunState(
            IsCollecting: reader.GetInt32(0) == 1,
            IsConfigLocked: reader.GetInt32(1) == 1,
            ActiveSchemeId: reader.IsDBNull(2) ? null : reader.GetString(2),
            LastHeartbeat: reader.IsDBNull(3) ? null : reader.GetString(3),
            TargetWindowTitle: reader.IsDBNull(4) ? null : reader.GetString(4),
            LastError: reader.IsDBNull(5) ? null : reader.GetString(5)
        );
    }

    // ====== system_config ======

    public SystemConfig GetSystemConfig()
    {
        using var cmd = EnsureOpen().CreateCommand();
        cmd.CommandText = "SELECT * FROM system_config WHERE rowid = 1";
        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
            throw new InvalidOperationException("system_config 不存在");

        return new SystemConfig
        {
            RowId = reader.GetInt32(0),
            PasswordHash = reader.GetString(1),
            PasswordSalt = reader.GetString(2),
            PollingIntervalMs = reader.GetInt32(3),
            HeartbeatIntervalSec = reader.GetInt32(4),
            AutoStart = reader.GetInt32(5) == 1,
            RequirePasswordOnOpen = reader.GetInt32(6) == 1,
            CreatedAt = DateTime.Parse(reader.GetString(7)),
            UpdatedAt = DateTime.Parse(reader.GetString(8))
        };
    }

    // ====== collection_scheme ======

    public List<CollectionScheme> GetAllSchemes()
    {
        var list = new List<CollectionScheme>();
        using var cmd = EnsureOpen().CreateCommand();
        cmd.CommandText = "SELECT * FROM collection_scheme ORDER BY id";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(MapScheme(reader));
        }
        return list;
    }

    public CollectionScheme? GetScheme(string schemeId)
    {
        using var cmd = EnsureOpen().CreateCommand();
        cmd.CommandText = "SELECT * FROM collection_scheme WHERE scheme_id = @sid";
        cmd.Parameters.AddWithValue("@sid", schemeId);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? MapScheme(reader) : null;
    }

    private static CollectionScheme MapScheme(SqliteDataReader reader)
    {
        return new CollectionScheme
        {
            Id = reader.GetInt32(0),
            SchemeId = reader.GetString(1),
            SchemeName = reader.GetString(2),
            WindowTitlePattern = reader.IsDBNull(3) ? null : reader.GetString(3),
            WindowClassName = reader.IsDBNull(4) ? null : reader.GetString(4),
            IsEnabled = reader.GetInt32(5) == 1,
            IsActive = reader.GetInt32(6) == 1,
            PollingIntervalMs = reader.IsDBNull(7) ? null : reader.GetInt32(7),
            HeartbeatIntervalSec = reader.IsDBNull(8) ? null : reader.GetInt32(8),
            Remark = reader.IsDBNull(9) ? null : reader.GetString(9),
            CreatedAt = DateTime.Parse(reader.GetString(10)),
            UpdatedAt = DateTime.Parse(reader.GetString(11))
        };
    }

    // ====== control_rule ======

    public List<ControlRule> GetRulesByScheme(string schemeId)
    {
        var list = new List<ControlRule>();
        using var cmd = EnsureOpen().CreateCommand();
        cmd.CommandText = "SELECT * FROM control_rule WHERE scheme_id = @sid ORDER BY id";
        cmd.Parameters.AddWithValue("@sid", schemeId);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(MapRule(reader));
        }
        return list;
    }

    private static ControlRule MapRule(SqliteDataReader reader)
    {
        return new ControlRule
        {
            Id = reader.GetInt32(0),
            RuleId = reader.GetString(1),
            SchemeId = reader.GetString(2),
            DisplayName = reader.GetString(3),
            AutomationId = reader.IsDBNull(4) ? null : reader.GetString(4),
            ClassName = reader.IsDBNull(5) ? null : reader.GetString(5),
            NameMatch = reader.IsDBNull(6) ? null : reader.GetString(6),
            Win32ClassName = reader.IsDBNull(7) ? null : reader.GetString(7),
            Win32Text = reader.IsDBNull(8) ? null : reader.GetString(8),
            FallbackIndex = reader.IsDBNull(9) ? null : reader.GetInt32(9),
            CollectText = reader.GetInt32(10) == 1,
            CollectEnabledState = reader.GetInt32(11) == 1,
            CollectVisibleState = reader.GetInt32(12) == 1,
            TriggerOnTextChange = reader.GetInt32(13) == 1,
            TriggerOnEnabledChange = reader.GetInt32(14) == 1,
            TriggerOnVisibleChange = reader.GetInt32(15) == 1,
            IsEnabled = reader.GetInt32(16) == 1,
            Remark = reader.IsDBNull(17) ? null : reader.GetString(17),
            CreatedAt = DateTime.Parse(reader.GetString(18)),
            UpdatedAt = DateTime.Parse(reader.GetString(19))
        };
    }
}
