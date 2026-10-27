-- ============================================================
-- 系统配置表：访问密码 + 全局参数（只存一行，rowid 固定为 1）
-- ============================================================
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

-- ============================================================
-- 采集方案表
-- ============================================================
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

-- ============================================================
-- 控件采集规则表
-- ============================================================
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

-- ============================================================
-- 主数据表：采集记录
-- ============================================================
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

-- ============================================================
-- 应用运行状态表
-- ============================================================
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
VALUES(1, 0, 0, datetime('now'));
