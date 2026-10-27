# DMADC — Windows 数据采集监控应用 · 设计方案

**日期**：2026-10-27  
**技术栈**：C# + .NET WPF + UI Automation + SQLite  
**目标**：对 Windows 桌面应用的控件进行自动采集、解析、存储，用于工业设备/业务流程的运行监控

---

## 第一段：整体架构与进程模型

### 1.1 项目结构

```
AutoDataCollector/
├── AutoDataCollector.sln
├── src/
│   └── AutoDataCollector/
│       ├── AutoDataCollector.csproj
│       ├── App.xaml / App.xaml.cs        ← WPF 入口
│       ├── MainWindow.xaml / .cs         ← 主窗口（配置查看）
│       ├── Services/                     ← 业务服务层
│       │   ├── IDataCollectionService.cs  ← 采集接口
│       │   ├── DataCollectionService.cs   ← 采集引擎实现
│       │   ├── IUiAutomationService.cs    ← UI Automation 封装
│       │   ├── UiAutomationService.cs
│       │   ├── ISqliteWriter.cs           ← SQLite 写入接口
│       │   ├── SqliteWriter.cs            ← Channel 队列 + 单写线程
│       │   ├── IConfigService.cs          ← 配置读写接口（全部走 SQLite）
│       │   ├── ConfigService.cs           ← 含配置锁前置检查
│       │   ├── ITrayIconService.cs        ← 托盘图标接口
│       │   ├── TrayIconService.cs
│       │   ├── IPasswordService.cs        ← 密码验证接口（存 SQLite）
│       │   └── PasswordService.cs
│       ├── Models/                       ← 数据模型
│       │   ├── CollectionConfig.cs        ← 采集配置
│       │   ├── ControlRule.cs             ← 单条控件采集规则
│       │   ├── ControlSnapshot.cs         ← 控件快照（用于 diff）
│       │   └── DataRecord.cs              ← 数据库记录
│       ├── Views/                        ← 界面
│       │   ├── PasswordDialog.xaml
│       │   ├── ConfigWizard.xaml
│       │   ├── ControlPicker.xaml          ← 十字光标拾取器
│       │   └── LiveViewer.xaml             ← 实时数据查看
│       ├── Helpers/                      ← 工具类
│       │   ├── Win32Interop.cs            ← Win32 API 封装
│       │   ├── PasswordHasher.cs          ← SHA256 哈希
│       │   └── StartupManager.cs          ← 开机自启管理
│       └── Assets/                       ← 图标等资源
└── docs/superpowers/specs/
    └── 2026-10-27-windows-data-collector-design.md
```

### 1.2 进程模型（单进程 + 多线程）

```
┌─────────────────────────────────────────────────────┐
│                   WPF 主进程                         │
│                                                     │
│  主线程 (UI Dispatcher)                              │
│  ├── 托盘图标交互（Show Balloon / Menu）             │
│  ├── 配置向导 / 密码窗口 / 拾取器 / 查看器           │
│  └── 通过 CancellationToken 控制后台线程生命周期      │
│                                                     │
│  后台线程 ①：采集轮询线程 (DataCollectionService)     │
│  ├── 周期 = 配置的采集间隔（默认 1 秒）               │
│  ├── 调 UiAutomationService 抓取控件快照             │
│  ├── 与上一次快照做 diff → 判断哪些控件变化了         │
│  ├── 变化事件 + 定时心跳事件 → 推入 Channel<Record>   │
│  └── 异常隔离：try-catch 包裹整个循环，不崩溃         │
│                                                     │
│  后台线程 ②：SQLite 写入线程 (SqliteWriter)           │
│  ├── 从 Channel<Record> 读取数据（WaitToReadAsync）  │
│  ├── 批量写入（攒够 N 条或超过 M 毫秒）               │
│  ├── SQLite WAL 模式 + 单线程串行 → 零锁冲突          │
│  └── 程序退出时 Drain Channel，确保数据不丢           │
│                                                     │
└─────────────────────────────────────────────────────┘
```

### 1.3 关键设计约束

| 约束 | 原因 |
|---|---|
| **只有一个线程写 SQLite** | 多线程并发写 = `database is locked` |
| **采集线程运行时禁止改配置** | 配置变更会导致轮询线程行为不确定，通过 `app_state.is_collecting` 强制前置检查 |
| **不要额外加 Program.cs** | WPF 默认入口 + 自定义 Program = 入口冲突 |
| **后台线程 try-catch 全包裹** | 采集目标可能随时关闭，线程不能因一次异常就死 |
| **`Channel<T>` 而非 lock 队列** | 高性能、异步友好、自动背压，比手动加锁安全 |
| **所有配置存 SQLite，无 JSON 文件** | 避免多源配置漂移，统一通过 `ConfigService` 读写 |
| **先冻结最小可编译 UI 骨架** | 避免批量铺 XAML 后连续编译错误 |

---

## 第二段：采集引擎与控件拾取器

### 2.1 UI Automation 采集原理

.NET 的 `System.Windows.Automation`（UIA）是 Windows 原生自动化框架，对各技术栈覆盖程度：

| 目标技术栈 | UIA 支持程度 | 可获取的属性 |
|---|---|---|
| .NET WinForms / WPF | ⭐⭐⭐⭐⭐ | AutomationId、Name、ClassName、IsEnabled、IsOffscreen |
| MFC / C++ 原生 | ⭐⭐⭐⭐ | ClassName + Name 组合、IsEnabled |
| Qt | ⭐⭐⭐⭐ | Qt UIA 插件暴露的属性 |
| Electron / CEF | ⭐⭐ | 有限支持，可能需要回退到 Win32 API |
| 其他 | ⭐⭐ | 兜底用 Win32 `GetWindowText` + `SendMessage(WM_GETTEXT)` |

**结论**：UIA 优先 + Win32 API 兜底的双通道策略。

### 2.2 控件定位标识策略（ControlRule 模型）

由于目标应用技术栈不确定，不能假设 AutomationId 一定存在。采用 **多级组合键** 定位：

```
ControlRule（控件采集规则）:
├── RuleId: string            ← 规则唯一 ID（自动生成 GUID）
├── DisplayName: string       ← 用户给这个控件起的名字（如"检测状态"、"启动按钮"）
│
├── 窗口定位（找哪个窗口）:
│   ├── WindowTitlePattern: string   ← 窗口标题匹配模式（支持 * 通配符）
│   └── WindowClassName: string      ← 窗口类名（备用）
│
├── 控件定位（在窗口内找哪个控件）:
│   ├── 优先：AutomationId: string           ← UIA 的 AutomationId
│   ├── 次选：ClassName + Name 组合          ← UIA ClassName + Name 属性
│   ├── 兜底：Win32 ClassName + Win32 文本
│   └── Fallback：按控件索引位置             ← 如果都没有，用树中的路径索引
│
├── 采集内容（采什么）:
│   ├── CollectText: bool                   ← 是否采集文本
│   ├── CollectEnabledState: bool           ← 是否采集启用/禁用状态
│   └── CollectVisibleState: bool           ← 是否采集可见性
│
└── 触发条件（什么时候触发记录）:
    ├── TriggerOnTextChange: bool
    ├── TriggerOnEnabledChange: bool        ← 按钮变灰/变亮时触发
    └── TriggerOnVisibleChange: bool
```

### 2.3 采集引擎轮询流程

```
采集轮询循环（每 1 秒一次，可配置）:
│
├─ Step 1: 查找目标窗口
│   └─ 按 WindowTitlePattern / WindowClassName 枚举顶层窗口
│   └─ 找匹配的 Process → 如果没找到 → 标记"目标未运行" → 等下一轮
│
├─ Step 2: 遍历 ControlRule 列表
│   │
│   ├─ 对每条规则定位控件:
│   │   ├─ 先尝试 UIA AutomationElement.FindAll/FindFirst
│   │   ├─ 失败则用 Win32 FindWindowEx 枚举子窗口
│   │   └─ 都找不到 → 标记控件为"未找到"，保留上次值
│   │
│   ├─ 读取控件当前值:
│   │   ├─ Text = current.Name / Win32 WM_GETTEXT
│   │   ├─ IsEnabled = current.Current.IsEnabled
│   │   └─ IsVisible = current.Current.IsOffscreen == false
│   │
│   └─ 生成 ControlSnapshot（快照）
│
├─ Step 3: 与上一轮快照做 Diff
│   │
│   ├─ 遍历每个控件的前后值:
│   │   ├─ Text 变了 && TriggerOnTextChange → 变化事件
│   │   ├─ IsEnabled 变了 && TriggerOnEnabledChange → 变化事件 ⬅️ 按钮变灰/变亮
│   │   └─ IsVisible 变了 && TriggerOnVisibleChange → 变化事件
│   │
│   └─ 变化事件 → Channel.WriteAsync(ChangeRecord)
│
└─ Step 4: 定时心跳（每 N 秒，默认 30 秒）
    └─ 不管有没有变化，强制写一条全量快照 → Channel.WriteAsync(HeartbeatRecord)
```

### 2.4 控件拾取器设计（交互式）

```
ControlPicker 窗口（WPF Window，置顶）:
│
├── 顶部工具栏:
│   ├── 🏹 "开始拾取" 按钮 → 切换十字光标模式
│   ├── 📋 "当前控件信息" 面板
│   │   ├── AutomationId: xxx
│   │   ├── ClassName: xxx
│   │   ├── Name (Text): xxx
│   │   ├── IsEnabled: True/False
│   │   ├── Process: xxx.exe (PID)
│   │   └── Bounds: X=... Y=... W=... H=...
│   └── ✅ "确认添加为采集规则" 按钮
│
├── 拾取流程:
│   1. 用户点"开始拾取" → 应用隐藏主窗口，光标变为十字
│   2. 用户移动光标到目标控件上（按 F1 或点击）
│   3. 应用调用 AutomationElement.FromPoint(cursorPos)
│   4. 自动向上追溯到顶层窗口（确认属于目标应用）
│   5. 显示所有可用属性 → 用户勾选要采集的内容
│   6. 点"确认" → 生成 ControlRule 并保存到当前配置
│
└── 实现要点:
    ├── FromPoint() 可能返回容器而非叶子控件 → 需要向下找最小可交互元素
    ├── 如果鼠标悬停期间目标窗口被遮挡 → 临时 BringToTop（仅拾取期间）
    ├── Esc 键随时取消拾取 → 恢复正常光标
    └── 拾取期间禁用采集轮询 → 避免干扰
```

### 2.5 UiAutomationService 接口设计

```csharp
public interface IUiAutomationService
{
    // 查找顶层窗口
    AutomationElement FindTargetWindow(string titlePattern, string className);

    // 在窗口内按规则定位控件（自动降级）
    ControlLocationResult LocateControl(AutomationElement window, ControlRule rule);

    // 读取控件当前状态
    ControlSnapshot ReadSnapshot(ControlLocationResult location);

    // 十字光标拾取（配合 ControlPicker UI 使用）
    AutomationElement PickAtPoint(Point screenPoint);
}

// 定位结果：可能来自 UIA 或 Win32
public class ControlLocationResult
{
    public bool Found { get; set; }
    public bool UsedWin32Fallback { get; set; }
    public AutomationElement? UiaElement { get; set; }
    public IntPtr Win32Handle { get; set; }
    public string? MatchedBy { get; set; } // "AutomationId" / "ClassName+Name" / "Win32"
}
```

---

## 第三段：SQLite 写入队列与数据模型

### 3.1 核心原则

**整个应用只有一个线程写 SQLite**。所有组件都只往 Channel 里投递消息，真正的 SQL 执行全部集中在 `SqliteWriter` 的一个专用线程中。

### 3.2 SQLite 配置调优

连接打开时一次性执行以下 PRAGMA：

```sql
PRAGMA journal_mode = WAL;      -- 写前日志：读写并发不阻塞，比默认 DELETE 快 2-3 倍
PRAGMA synchronous = NORMAL;    -- 平衡安全与性能（掉电可能丢最后几百毫秒的数据）
PRAGMA cache_size = -64000;     -- 64MB 页缓存
PRAGMA temp_store = MEMORY;     -- 临时表放内存
PRAGMA busy_timeout = 3000;     -- 其他进程读锁时最多等 3 秒
```

### 3.3 数据库 Schema

```sql
-- ============================================================
-- 系统配置表：访问密码 + 全局参数（只存一行，rowid 固定为 1）
-- ============================================================
CREATE TABLE IF NOT EXISTS system_config (
    rowid INTEGER PRIMARY KEY,             -- 固定 rowid=1
    password_hash TEXT NOT NULL,           -- SHA256(password + salt)
    password_salt TEXT NOT NULL,            -- 生成哈希时使用的盐
    polling_interval_ms INTEGER DEFAULT 1000,  -- 全局采集轮询间隔（毫秒），默认 1000
    heartbeat_interval_sec INTEGER DEFAULT 30, -- 全局心跳间隔（秒），默认 30
    auto_start INTEGER DEFAULT 0,          -- 0=不自动启动采集 1=启动时自动开始
    require_password_on_open INTEGER DEFAULT 1, -- 0=打开免密 1=打开需密码
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL
);
INSERT OR IGNORE INTO system_config(rowid, password_hash, password_salt, created_at, updated_at)
VALUES(1, '', '', datetime('now'), datetime('now'));

-- ============================================================
-- 采集方案表：一个系统可以有多个采集方案（对应不同目标应用）
-- ============================================================
CREATE TABLE IF NOT EXISTS collection_scheme (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    scheme_id TEXT NOT NULL UNIQUE,        -- 业务 ID（GUID），供 ControlRule 关联
    scheme_name TEXT NOT NULL,             -- 方案显示名，如"上位机A"、"检测台"
    window_title_pattern TEXT,             -- 目标窗口标题匹配模式（* 通配符）
    window_class_name TEXT,                -- 目标窗口类名（备用）
    is_enabled INTEGER DEFAULT 1,          -- 0=禁用 1=启用
    is_active INTEGER DEFAULT 0,           -- 0=当前未激活 1=当前激活的采集方案
    polling_interval_ms INTEGER,           -- 方案级覆盖（NULL 则使用全局）
    heartbeat_interval_sec INTEGER,         -- 方案级覆盖（NULL 则使用全局）
    remark TEXT,
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS idx_scheme_active ON collection_scheme(is_active);

-- ============================================================
-- 控件采集规则表：每条规则属于一个采集方案
-- ============================================================
CREATE TABLE IF NOT EXISTS control_rule (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    rule_id TEXT NOT NULL UNIQUE,          -- 业务 ID（GUID）
    scheme_id TEXT NOT NULL,               -- 关联 collection_scheme.scheme_id
    display_name TEXT NOT NULL,            -- 用户给控件起的名字，如"检测状态"
    -- 控件定位（多级降级，见 2.2 节）
    automation_id TEXT,                    -- UIA AutomationId（优先）
    class_name TEXT,                       -- UIA ClassName + Name 组合（次选）
    name_match TEXT,
    win32_class_name TEXT,                 -- Win32 兜底
    win32_text TEXT,
    fallback_index INTEGER,                -- 按树路径索引兜底
    -- 采集内容
    collect_text INTEGER DEFAULT 1,        -- 0=不采 1=采
    collect_enabled_state INTEGER DEFAULT 1,
    collect_visible_state INTEGER DEFAULT 1,
    -- 触发条件
    trigger_on_text_change INTEGER DEFAULT 1,
    trigger_on_enabled_change INTEGER DEFAULT 1,
    trigger_on_visible_change INTEGER DEFAULT 1,
    is_enabled INTEGER DEFAULT 1,          -- 0=禁用此规则
    remark TEXT,
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL,
    FOREIGN KEY (scheme_id) REFERENCES collection_scheme(scheme_id)
);
CREATE INDEX IF NOT EXISTS idx_rule_scheme ON control_rule(scheme_id);

-- ============================================================
-- 主数据表：采集记录（变化触发 + 定时心跳）
-- ============================================================
CREATE TABLE IF NOT EXISTS collect_log (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    timestamp TEXT NOT NULL,           -- ISO-8601 UTC
    record_type TEXT NOT NULL,         -- 'change' / 'heartbeat'
    scheme_id TEXT NOT NULL,           -- 所属采集方案
    rule_id TEXT NOT NULL,             -- 关联 control_rule.rule_id
    rule_name TEXT NOT NULL,           -- 冗余：控件显示名，方便查询
    window_title TEXT,                 -- 采集时目标窗口标题
    text_value TEXT,                   -- 采集到的文本（可空）
    is_enabled INTEGER,                -- 0/1，启用状态（可空）
    is_visible INTEGER,                -- 0/1，可见性（可空）
    source TEXT,                       -- 'UIA' / 'Win32' / 'NotFound'
    diff_fields TEXT,                  -- 仅 change 记录：JSON 数组
    FOREIGN KEY (scheme_id) REFERENCES collection_scheme(scheme_id),
    FOREIGN KEY (rule_id) REFERENCES control_rule(rule_id)
);
CREATE INDEX IF NOT EXISTS idx_collect_log_timestamp ON collect_log(timestamp);
CREATE INDEX IF NOT EXISTS idx_collect_log_rule_id ON collect_log(rule_id);
CREATE INDEX IF NOT EXISTS idx_collect_log_type ON collect_log(record_type);
CREATE INDEX IF NOT EXISTS idx_collect_log_scheme ON collect_log(scheme_id);

-- ============================================================
-- 应用运行状态表：只存一行，采集线程运行时动态更新
-- ============================================================
CREATE TABLE IF NOT EXISTS app_state (
    rowid INTEGER PRIMARY KEY,             -- 固定 rowid=1
    is_collecting INTEGER DEFAULT 0,       -- 0=已停止 1=采集中
    is_config_locked INTEGER DEFAULT 0,    -- 0=配置可改 1=配置锁定（采集中）
    active_scheme_id TEXT,                 -- 当前激活的方案
    last_heartbeat TEXT,                   -- 最后一次心跳时间
    target_window_title TEXT,              -- 最后检测到的目标窗口标题
    last_error TEXT,                       -- 最近一次错误信息
    started_at TEXT,                       -- 本次采集启动时间
    updated_at TEXT NOT NULL
);
INSERT OR IGNORE INTO app_state(rowid, is_collecting, is_config_locked, updated_at)
VALUES(1, 0, 0, datetime('now'));
```

**关键变更说明**：

- 原来的 JSON 配置文件彻底取消，所有配置（系统密码、采集方案、控件规则）全部存 SQLite
- `app_state.is_config_locked` 是配置锁标记：**采集线程运行期间 = 1，配置读写禁止写入操作**；停止时 = 0，才允许增删改配置
- 系统首次启动时密码为空字符串，用户在主界面 → 设置里设置密码后写入 `system_config`
- `scheme_id` / `rule_id` 字段统一用 GUID 字符串做主键关联，内部 `id`（INTEGER）只用于自增排序

### 3.4 数据模型

```csharp
// ---- 数据库持久化模型（直接映射表）----

public class SystemConfig
{
    public string PasswordHash { get; set; } = "";
    public string PasswordSalt { get; set; } = "";
    public int PollingIntervalMs { get; set; } = 1000;
    public int HeartbeatIntervalSec { get; set; } = 30;
    public bool AutoStart { get; set; }
    public bool RequirePasswordOnOpen { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class CollectionScheme
{
    public string SchemeId { get; set; } = Guid.NewGuid().ToString();
    public string SchemeName { get; set; } = "";
    public string? WindowTitlePattern { get; set; }
    public string? WindowClassName { get; set; }
    public bool IsEnabled { get; set; } = true;
    public bool IsActive { get; set; }
    public int? PollingIntervalMs { get; set; }      // NULL 则用全局
    public int? HeartbeatIntervalSec { get; set; }   // NULL 则用全局
    public string? Remark { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    // 导航属性
    public List<ControlRule> Rules { get; set; } = new();
}

public class ControlRule
{
    public string RuleId { get; set; } = Guid.NewGuid().ToString();
    public string SchemeId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    // 定位标识（多级降级）
    public string? AutomationId { get; set; }
    public string? ClassName { get; set; }
    public string? NameMatch { get; set; }
    public string? Win32ClassName { get; set; }
    public string? Win32Text { get; set; }
    public int? FallbackIndex { get; set; }
    // 采集内容
    public bool CollectText { get; set; } = true;
    public bool CollectEnabledState { get; set; } = true;
    public bool CollectVisibleState { get; set; } = true;
    // 触发条件
    public bool TriggerOnTextChange { get; set; } = true;
    public bool TriggerOnEnabledChange { get; set; } = true;
    public bool TriggerOnVisibleChange { get; set; } = true;
    public bool IsEnabled { get; set; } = true;
    public string? Remark { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

// ---- 投递到 SqliteWriter Channel 的消息 ----

public abstract class DbMessage
{
    public DateTime Timestamp { get; } = DateTime.UtcNow;
}

// 采集记录（变化触发 / 定时心跳）
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

// 应用运行状态更新
public class AppStateMessage : DbMessage
{
    public bool? IsCollecting { get; init; }
    public bool? IsConfigLocked { get; init; }   // 采集线程启停时顺带更新
    public string? ActiveSchemeId { get; init; }
    public string? LastHeartbeat { get; init; }
    public string? TargetWindowTitle { get; init; }
    public string? LastError { get; init; }
}

public enum RecordType { Change, Heartbeat }
public enum UiaSource { Uia, Win32, NotFound }
```

### 3.5 配置锁机制（关键约束）

```
配置写操作前置检查流程：

用户在 UI 上点"保存配置 / 添加规则 / 修改密码"
    │
    ▼
IConfigService.TryXxxAsync(...) / IPasswordService.TryChangePasswordAsync(...)
    │
    ▼
 ┌───────────────────────────────────┐
 │ 读取 app_state.is_collecting     │  ← 同步读，走独立只读连接
 │                                   │
 │ 如果 is_collecting == true        │
 │   → throw InvalidOperationException│
 │     ("采集中，不能修改配置")        │
 │   → UI 弹出 Toast 提示             │
 │                                   │
 │ 如果 is_collecting == false       │
 │   → 继续写入 Channel               │
 └───────────────────────────────────┘

采集线程启动 / 停止时：
  启动 → 写入 AppStateMessage { IsCollecting = true, IsConfigLocked = true }
  停止 → 写入 AppStateMessage { IsCollecting = false, IsConfigLocked = false }
```

**设计结论**：
- 配置读操作**无限制**（采集中也能看）
- 配置写操作**强制先查锁**，锁由采集线程的启停状态驱动
- 读取 `is_collecting` 用独立只读 SQLite 连接（不走 Channel），因为它非常轻量（单行查询），且 Channel 可能有背压延迟
- `is_config_locked` 字段和 `is_collecting` 在同一条 `AppStateMessage` 中一起写入，保持强一致

### 3.6 ConfigService & IPasswordService 接口

```csharp
public interface IConfigService
{
    // 读操作（采集中也可调用）
    Task<SystemConfig> GetSystemConfigAsync();
    Task<List<CollectionScheme>> GetAllSchemesAsync();
    Task<CollectionScheme?> GetSchemeAsync(string schemeId);
    Task<List<ControlRule>> GetRulesBySchemeAsync(string schemeId);
    Task<(bool isCollecting, bool isConfigLocked)> GetRunStateAsync();

    // 写操作（采集中调用会抛异常）
    Task AddSchemeAsync(CollectionScheme scheme);
    Task UpdateSchemeAsync(CollectionScheme scheme);
    Task DeleteSchemeAsync(string schemeId);
    Task ActivateSchemeAsync(string schemeId);
    Task AddRuleAsync(ControlRule rule);
    Task UpdateRuleAsync(ControlRule rule);
    Task DeleteRuleAsync(string ruleId);
    Task UpdateSystemConfigAsync(SystemConfig config);
}

public interface IPasswordService
{
    // 验证（主界面打开时调用）
    Task<bool> VerifyAsync(string inputPassword);
    // 修改（采集中抛异常）
    Task ChangePasswordAsync(string oldPassword, string newPassword);
    // 是否首次启动（密码为空）
    Task<bool> IsFirstTimeSetupAsync();
}
```

### 3.7 SqliteWriter 写入队列架构

```
生产者们（多个线程）
 ┌─────────────────────┐
 │ DataCollection      │
 │ Service（采集线程）  │
 │ ┌── Change 事件      │
 │ └── Heartbeat 事件   │
 └─────────┬───────────┘
           │
 ┌─────────┴───────────┐
 │ Channel<DbMessage>  │ ← bounded 容量 1000
 │ (CreateBounded)     │ 满时：采集线程 await 等待（背压）
 └─────────┬───────────┘
           │ WaitToReadAsync
 ┌─────────▼───────────┐
 │ SqliteWriter        │
 │ (单 dedicated 线程) │
 │                     │
 │ 批量写入策略：       │
 │ ├── 攒满 50 条 立即写│
 │ └── 或超过 500ms 写 │ ← 防止低流量时数据延迟
 │                     │
 │ using (var tx = ...)│
 │ {                   │
 │   foreach msg in batch│
 │   { INSERT ... }    │
 │   tx.Commit();      │ ← 一个事务提交一批
 │ }                   │
 └──────────────────────┘
           │
 ┌─────────▼───────────┐
 │ SQLite (WAL 模式)    │
 └──────────────────────┘
```

### 3.8 SqliteWriter 伪代码

```csharp
public class SqliteWriter : ISqliteWriter, IAsyncDisposable
{
    private readonly Channel<DbMessage> _channel;
    private readonly Task _writerLoop;
    private readonly CancellationTokenSource _cts;
    private readonly string _dbPath;

    private const int BatchSize = 50;
    private static readonly TimeSpan BatchTimeout = TimeSpan.FromMilliseconds(500);

    public SqliteWriter(string dbPath)
    {
        _dbPath = dbPath;
        _channel = Channel.CreateBounded<DbMessage>(
            new BoundedChannelOptions(capacity: 1000)
            {
                FullMode = BoundedChannelFullMode.Wait, // 背压，不丢数据
                SingleReader = true,
                SingleWriter = false
            });
        _cts = new CancellationTokenSource();
        _writerLoop = Task.Run(() => WriterLoopAsync(_cts.Token));
    }

    // 公开接口
    public async ValueTask WriteAsync(DbMessage message)
        => await _channel.Writer.WriteAsync(message);

    private async Task WriterLoopAsync(CancellationToken ct)
    {
        using var conn = OpenOptimizedConnection();
        var batch = new List<DbMessage>(BatchSize);
        var timer = new PeriodicTimer(BatchTimeout);

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var hasData = await _channel.Reader.WaitToReadAsync(ct);
                if (!hasData) break;

                while (batch.Count < BatchSize &&
                       _channel.Reader.TryRead(out var msg))
                    batch.Add(msg);

                while (batch.Count < BatchSize &&
                       await timer.WaitForNextTickAsync(ct))
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
            while (_channel.Reader.TryRead(out var remaining))
                batch.Add(remaining);
            if (batch.Count > 0)
                await FlushBatchAsync(conn, batch);
        }
    }
}
```

### 3.9 性能预期

| 指标 | 预估值 | 说明 |
|---|---|---|
| 单条 INSERT 耗时 | ~0.1ms | WAL 模式下 |
| 50 条批量事务 | ~3-5ms | 远快于逐条提交 |
| 20 控件 × 1 秒轮询 × 30 天 | ~50 万条 | SQLite 轻松处理 |
| 数据库大小（30 天） | ~50-100 MB | 取决于文本字段长度 |
