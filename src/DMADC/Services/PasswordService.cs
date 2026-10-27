using System;
using System.Threading.Tasks;
using DMADC.Helpers;
using DMADC.Models;

namespace DMADC.Services;

/// <summary>
/// 密码验证 / 修改 —— 存 system_config，修改前置检查配置锁
/// </summary>
public class PasswordService : IPasswordService
{
    private readonly DbReadConnection _db;
    private readonly IConfigService _config;

    public PasswordService(DbReadConnection db, IConfigService config)
    {
        _db = db;
        _config = config;
    }

    public Task<bool> IsFirstTimeSetupAsync()
    {
        var cfg = _db.GetSystemConfig();
        return Task.FromResult(string.IsNullOrEmpty(cfg.PasswordHash));
    }

    public Task<bool> VerifyAsync(string inputPassword)
    {
        var cfg = _db.GetSystemConfig();
        // 未设置密码时，任何非空密码都应拒绝（只有空输入可以进入）
        if (string.IsNullOrEmpty(cfg.PasswordHash))
            return Task.FromResult(string.IsNullOrEmpty(inputPassword));

        bool ok = PasswordHasher.Verify(inputPassword, cfg.PasswordHash, cfg.PasswordSalt);
        return Task.FromResult(ok);
    }

    public async Task ChangePasswordAsync(string oldPassword, string newPassword)
    {
        var state = await _config.GetRunStateAsync();
        if (state.IsCollecting)
            throw new InvalidOperationException("采集中，不能修改密码。请先停止采集。");

        // 验证旧密码
        bool firstTime = await IsFirstTimeSetupAsync();
        if (!firstTime)
        {
            bool ok = await VerifyAsync(oldPassword);
            if (!ok)
                throw new InvalidOperationException("旧密码不正确");
        }

        if (string.IsNullOrEmpty(newPassword))
            throw new ArgumentException("新密码不能为空", nameof(newPassword));

        var cfg = await _config.GetSystemConfigAsync();
        cfg.PasswordSalt = PasswordHasher.GenerateSalt();
        cfg.PasswordHash = PasswordHasher.Hash(newPassword, cfg.PasswordSalt);
        await _config.UpdateSystemConfigAsync(cfg);
    }
}
