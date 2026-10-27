using System;
using System.Threading.Tasks;

namespace DMADC.Services;

/// <summary>
/// 采集引擎生命周期接口
/// </summary>
public interface IDataCollectionService
{
    /// <summary>开始采集（启动后台轮询线程）</summary>
    Task StartAsync();

    /// <summary>停止采集（等待队列 drain 后返回）</summary>
    Task StopAsync();

    /// <summary>当前是否在运行</summary>
    bool IsRunning { get; }

    /// <summary>运行状态变化事件（UI 订阅此事件刷新按钮状态）</summary>
    event EventHandler<bool>? RunningChanged;

    /// <summary>采集状态变化事件（消息供实时查看窗口用）</summary>
    event EventHandler<string>? StatusMessage;
}
