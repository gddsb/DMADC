using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using DMADC.Models;

namespace DMADC.Services;

public class SqliteWriter : ISqliteWriter, IAsyncDisposable
{
    private readonly Channel<DbMessage> _channel;
    private readonly Task _writerLoop;
    private readonly CancellationTokenSource _cts;
    private readonly string _dbPath;
    private bool _disposed;

    private const int BatchSize = 50;
    private static readonly TimeSpan BatchTimeout = TimeSpan.FromMilliseconds(500);

    public SqliteWriter(string dbPath)
    {
        _dbPath = dbPath;
        _channel = Channel.CreateBounded<DbMessage>(
            new BoundedChannelOptions(capacity: 1000)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false
            });
        _cts = new CancellationTokenSource();
        _writerLoop = Task.Run(() => WriterLoopAsync(_cts.Token));
    }

    public async ValueTask WriteAsync(DbMessage message)
    {
        if (_disposed) return;
        await _channel.Writer.WriteAsync(message);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _channel.Writer.Complete();
        _cts.Cancel();
        await _writerLoop;
        _cts.Dispose();
    }

    private async Task WriterLoopAsync(CancellationToken ct)
    {
        // 确保目录存在
        Directory.CreateDirectory(Path.GetDirectoryName(_dbPath)!);

        using var conn = OpenOptimizedConnection();
        await InitializeSchemaAsync(conn);

        var batch = new List<DbMessage>(BatchSize);
        using var timer = new PeriodicTimer(BatchTimeout);

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var hasData = await _channel.Reader.WaitToReadAsync(ct);
                if (!hasData) break;

                // 尽量多读几条
                while (batch.Count < BatchSize && _channel.Reader.TryRead(out var msg))
                    batch.Add(msg);

                // 再等一小会看能不能攒更多
                while (batch.Count < BatchSize && await timer.WaitForNextTickAsync(ct))
                {
                    if (_channel.Reader.TryRead(out var msg2))
                        batch.Add(msg2);
                    else break;
                }

                if (batch.Count > 0)
                {
                    await FlushBatchAsync(conn, batch);
                    batch.Clear();
                }
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            // Drain 剩余数据
            while (_channel.Reader.TryRead(out var remaining))
                batch.Add(remaining);
            if (batch.Count > 0)
                await FlushBatchAsync(conn, batch);
        }
    }

    private SqliteConnection OpenOptimizedConnection()
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = _dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Default,
            Pooling = false
        };

        var conn = new SqliteConnection(builder.ConnectionString);
        conn.Open();

        // 一次性 PRAGMA 调优
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                PRAGMA journal_mode = WAL;
                PRAGMA synchronous = NORMAL;
                PRAGMA cache_size = -64000;
                PRAGMA temp_store = MEMORY;
                PRAGMA busy_timeout = 3000;";
            cmd.ExecuteNonQuery();
        }

        return conn;
    }

    private async Task InitializeSchemaAsync(SqliteConnection conn)
    {
        // 内嵌 schema.sql 资源或内联 SQL
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS system_config (
    rowid INTEGER PRIMARY KEY,
    password_hash TEXT NOT NULL,
    password_salt TEXT NOT NULL,
    polling_interval_ms INTEGER DEFAULT 1000,
    heartbeat_interval_sec INTEGER DEFAULT 30,
    auto_start INTEGER DEFAULT 0,
    require_password_on_open INTEGER DEFAULT 1,
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL
);
INSERT OR IGNORE INTO system_config(rowid, password_hash, password_salt, created_at, updated_at)
VALUES(1, '', '', datetime('now'), datetime('now'));

CREATE TABLE IF NOT EXISTS collection_scheme (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    scheme_id TEXT NOT NULL UNIQUE,
    scheme_name TEXT NOT NULL,
    window_title_pattern TEXT,
    window_class_name TEXT,
    is_enabled INTEGER DEFAULT 1,
    is_active INTEGER DEFAULT 0,
    polling_interval_ms INTEGER,
    heartbeat_interval_sec INTEGER,
    remark TEXT,
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS idx_scheme_active ON collection_scheme(is_active);

CREATE TABLE IF NOT EXISTS control_rule (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    rule_id TEXT NOT NULL UNIQUE,
    scheme_id TEXT NOT NULL,
    display_name TEXT NOT NULL,
    automation_id TEXT,
    class_name TEXT,
    name_match TEXT,
    win32_class_name TEXT,
    win32_text TEXT,
    fallback_index INTEGER,
    collect_text INTEGER DEFAULT 1,
    collect_enabled_state INTEGER DEFAULT 1,
    collect_visible_state INTEGER DEFAULT 1,
    trigger_on_text_change INTEGER DEFAULT 1,
    trigger_on_enabled_change INTEGER DEFAULT 1,
    trigger_on_visible_change INTEGER DEFAULT 1,
    is_enabled INTEGER DEFAULT 1,
    remark TEXT,
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL,
    FOREIGN KEY (scheme_id) REFERENCES collection_scheme(scheme_id)
);
CREATE INDEX IF NOT EXISTS idx_rule_scheme ON control_rule(scheme_id);

CREATE TABLE IF NOT EXISTS collect_log (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    timestamp TEXT NOT NULL,
    record_type TEXT NOT NULL,
    scheme_id TEXT NOT NULL,
    rule_id TEXT NOT NULL,
    rule_name TEXT NOT NULL,
    window_title TEXT,
    text_value TEXT,
    is_enabled INTEGER,
    is_visible INTEGER,
    source TEXT,
    diff_fields TEXT,
    FOREIGN KEY (scheme_id) REFERENCES collection_scheme(scheme_id),
    FOREIGN KEY (rule_id) REFERENCES control_rule(rule_id)
);
CREATE INDEX IF NOT EXISTS idx_collect_log_timestamp ON collect_log(timestamp);
CREATE INDEX IF NOT EXISTS idx_collect_log_rule_id ON collect_log(rule_id);
CREATE INDEX IF NOT EXISTS idx_collect_log_type ON collect_log(record_type);
CREATE INDEX IF NOT EXISTS idx_collect_log_scheme ON collect_log(scheme_id);

CREATE TABLE IF NOT EXISTS app_state (
    rowid INTEGER PRIMARY KEY,
    is_collecting INTEGER DEFAULT 0,
    is_config_locked INTEGER DEFAULT 0,
    active_scheme_id TEXT,
    last_heartbeat TEXT,
    target_window_title TEXT,
    last_error TEXT,
    started_at TEXT,
    updated_at TEXT NOT NULL
);
INSERT OR IGNORE INTO app_state(rowid, is_collecting, is_config_locked, updated_at)
VALUES(1, 0, 0, datetime('now'));";
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task FlushBatchAsync(SqliteConnection conn, List<DbMessage> batch)
    {
        await using var tx = await conn.BeginTransactionAsync();
        try
        {
            foreach (var msg in batch)
            {
                switch (msg)
                {
                    case CollectRecordMessage cr:
                        await InsertCollectRecordAsync(conn, cr);
                        break;
                    case AppStateMessage st:
                        await UpdateAppStateAsync(conn, st);
                        break;
                }
            }
            await tx.CommitAsync();
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    private async Task InsertCollectRecordAsync(SqliteConnection conn, CollectRecordMessage msg)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO collect_log
                (timestamp, record_type, scheme_id, rule_id, rule_name,
                 window_title, text_value, is_enabled, is_visible, source, diff_fields)
            VALUES (@ts, @rt, @sid, @rid, @rn, @wt, @tv, @ie, @iv, @src, @df)";
        cmd.Parameters.AddWithValue("@ts", msg.Timestamp.ToString("o"));
        cmd.Parameters.AddWithValue("@rt", msg.RecordType.ToString().ToLowerInvariant());
        cmd.Parameters.AddWithValue("@sid", msg.SchemeId);
        cmd.Parameters.AddWithValue("@rid", msg.RuleId);
        cmd.Parameters.AddWithValue("@rn", msg.RuleName);
        cmd.Parameters.AddWithValue("@wt", (object?)msg.WindowTitle ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@tv", (object?)msg.TextValue ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ie", msg.IsEnabled.HasValue ? (msg.IsEnabled.Value ? 1 : 0) : DBNull.Value);
        cmd.Parameters.AddWithValue("@iv", msg.IsVisible.HasValue ? (msg.IsVisible.Value ? 1 : 0) : DBNull.Value);
        cmd.Parameters.AddWithValue("@src", msg.Source.ToString());
        cmd.Parameters.AddWithValue("@df", msg.DiffFields != null ? JsonSerializer.Serialize(msg.DiffFields) : DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task UpdateAppStateAsync(SqliteConnection conn, AppStateMessage msg)
    {
        // 逐字段 UPDATE，只处理非 null 的值
        var sets = new List<string>();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE app_state SET ";

        if (msg.IsCollecting.HasValue) { sets.Add("is_collecting = @iscol"); cmd.Parameters.AddWithValue("@iscol", msg.IsCollecting.Value ? 1 : 0); }
        if (msg.IsConfigLocked.HasValue) { sets.Add("is_config_locked = @cfg"); cmd.Parameters.AddWithValue("@cfg", msg.IsConfigLocked.Value ? 1 : 0); }
        if (msg.ActiveSchemeId != null) { sets.Add("active_scheme_id = @asid"); cmd.Parameters.AddWithValue("@asid", msg.ActiveSchemeId); }
        if (msg.LastHeartbeat != null) { sets.Add("last_heartbeat = @lh"); cmd.Parameters.AddWithValue("@lh", msg.LastHeartbeat); }
        if (msg.TargetWindowTitle != null) { sets.Add("target_window_title = @tw"); cmd.Parameters.AddWithValue("@tw", msg.TargetWindowTitle); }
        if (msg.LastError != null) { sets.Add("last_error = @le"); cmd.Parameters.AddWithValue("@le", msg.LastError); }

        if (sets.Count == 0) return;

        sets.Add("updated_at = datetime('now')");
        cmd.CommandText += string.Join(", ", sets) + " WHERE rowid = 1";
        await cmd.ExecuteNonQueryAsync();
    }
}
