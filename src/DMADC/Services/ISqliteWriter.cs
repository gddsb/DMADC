using System;
using System.Threading.Tasks;
using DMADC.Models;

namespace DMADC.Services;

/// <summary>
/// SQLite 单写线程队列 —— 所有写操作都通过 Channel 投递
/// </summary>
public interface ISqliteWriter : IAsyncDisposable
{
    /// <summary>投递一条消息到写入队列（背压，不丢数据）</summary>
    ValueTask WriteAsync(DbMessage message);
}
