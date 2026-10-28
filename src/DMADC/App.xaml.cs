using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using DMADC.Services;

namespace DMADC;

public partial class App : Application
{
    // 依赖注入容器（简易版，不引入第三方 IoC）
    public static ServiceContainer Services { get; private set; } = null!;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 确定数据库路径：%LOCALAPPDATA%\DMADC\dmadc.db
        string dbDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DMADC");
        Directory.CreateDirectory(dbDir);
        string dbPath = Path.Combine(dbDir, "dmadc.db");

        // 初始化服务
        var dbRead = new DbReadConnection(dbPath);
        var sqliteWriter = new SqliteWriter(dbPath);
        var config = new ConfigService(dbRead, dbPath);
        var password = new PasswordService(dbRead, config);
        var uia = new UiAutomationService();
        var collection = new DataCollectionService(config, uia, sqliteWriter);
        var tray = new TrayIconService();

        Services = new ServiceContainer
        {
            DbPath = dbPath,
            DbRead = dbRead,
            SqliteWriter = sqliteWriter,
            Config = config,
            Password = password,
            Uia = uia,
            Collection = collection,
            Tray = tray
        };

        // 异常兜底
        DispatcherUnhandledException += (_, args) =>
        {
            System.IO.File.AppendAllText(
                Path.Combine(dbDir, "error.log"),
                $"[{DateTime.Now:o}] {args.Exception}\n\n");
            args.Handled = true;
        };

        // 初始化数据库（触发 SqliteWriter 启动时执行建表）
        // 给 SqliteWriter 一个瞬间启动的机会
        await Task.Delay(100);

        // 显示托盘
        tray.Show();

        // 默认隐藏主窗口（应用从托盘启动）
        var mainWin = Views.MainWindow.GetInstance();
        if (mainWin == null)
        {
            mainWin = new Views.MainWindow();
            mainWin.Close(); // 不自动 Show，让用户从托盘打开
        }

        // 绑定托盘事件
        tray.ShowMainWindowRequested += (_, _) => ShowMainWindow();
        tray.StartCollectRequested += (_, _) => _ = collection.StartAsync();
        tray.StopCollectRequested += (_, _) => _ = collection.StopAsync();
        tray.ExitRequested += (_, _) => ShutdownApp();

        collection.RunningChanged += (_, running) => tray.UpdateCollectingState(running);

        // 开机自启设置
        try
        {
            var sysCfg = await config.GetSystemConfigAsync();
            Helpers.StartupManager.SetAutoStart(sysCfg.AutoStart);
        }
        catch { }
    }

    private void ShowMainWindow()
    {
        var win = Views.MainWindow.GetInstance();
        if (win == null)
        {
            win = new Views.MainWindow();
        }
        win.Show();
        if (win.WindowState == WindowState.Minimized)
            win.WindowState = WindowState.Normal;
        win.Activate();
    }

    private async void ShutdownApp()
    {
        try
        {
            if (Services.Collection.IsRunning)
                await Services.Collection.StopAsync();
        }
        catch { }

        Services.Tray.Dispose();
        await Services.SqliteWriter.DisposeAsync();
        await Services.DbRead.DisposeAsync();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // ShutdownApp 可能已处理，这里兜底
        Services?.Tray.Dispose();
        base.OnExit(e);
    }
}

/// <summary>
/// 简易依赖注入容器 —— 一个小 class，替代第三方 IoC，便于理解
/// </summary>
public class ServiceContainer
{
    public string DbPath { get; set; } = "";
    public DbReadConnection DbRead { get; set; } = null!;
    public SqliteWriter SqliteWriter { get; set; } = null!;
    public IConfigService Config { get; set; } = null!;
    public IPasswordService Password { get; set; } = null!;
    public IUiAutomationService Uia { get; set; } = null!;
    public IDataCollectionService Collection { get; set; } = null!;
    public ITrayIconService Tray { get; set; } = null!;
}
