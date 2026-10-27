using System;
using System.Security.Cryptography;
using System.Text;

namespace DMADC.Helpers;

/// <summary>
/// 密码哈希工具：SHA256 + 随机盐
/// </summary>
public static class PasswordHasher
{
    /// <summary>
    /// 生成随机盐（Base64 编码，24 字节 → 32 字符）
    /// </summary>
    public static string GenerateSalt()
    {
        var bytes = new byte[24];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return Convert.ToBase64String(bytes);
    }

    /// <summary>
    /// 将 (password + salt) 做 SHA256，迭代 1000 次，返回 Base64 哈希
    /// </summary>
    public static string Hash(string password, string salt)
    {
        if (password == null) throw new ArgumentNullException(nameof(password));
        if (salt == null) throw new ArgumentNullException(nameof(salt));

        byte[] bytes = Encoding.UTF8.GetBytes(password + salt);
        byte[] hash = SHA256.HashData(bytes);

        // 迭代 1000 次增加破解成本
        for (int i = 1; i < 1000; i++)
        {
            hash = SHA256.HashData(hash);
        }

        return Convert.ToBase64String(hash);
    }

    /// <summary>
    /// 验证输入密码是否匹配存储的哈希
    /// </summary>
    public static bool Verify(string inputPassword, string storedHash, string storedSalt)
    {
        if (string.IsNullOrEmpty(storedHash))
        {
            // 存储为空 = 未设置密码，视为首次
            return string.IsNullOrEmpty(inputPassword);
        }

        string inputHash = Hash(inputPassword, storedSalt);
        return inputHash == storedHash;
    }
}
