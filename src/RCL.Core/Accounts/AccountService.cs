using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RCL.Core.Auth;
using RCL.Core.Models;

namespace RCL.Core.Accounts;

/// <summary>账号辅助：离线账号生成、令牌静默续期、展示信息。</summary>
public static class AccountService
{
    /// <summary>生成离线 UUID（与官方 OfflinePlayer 规则一致，第三方服务器可识别）。</summary>
    public static string OfflineUuid(string name)
    {
        using var md5 = MD5.Create();
        var hash = md5.ComputeHash(Encoding.UTF8.GetBytes("OfflinePlayer:" + name));
        hash[6] = (byte)((hash[6] & 0x0f) | 0x30);
        hash[8] = (byte)((hash[8] & 0x3f) | 0x80);
        var hex = Convert.ToHexString(hash).ToLowerInvariant();
        return $"{hex[..8]}-{hex.Substring(8, 4)}-{hex.Substring(12, 4)}-{hex.Substring(16, 4)}-{hex.Substring(20, 12)}";
    }

    /// <summary>创建离线账号。</summary>
    public static Account CreateOffline(string username)
    {
        var name = (username ?? "").Trim();
        if (name.Length == 0) throw new ArgumentException("离线用户名不能为空");
        if (name.Length > 16) name = name[..16];
        return new Account
        {
            Type = AccountTypes.Offline,
            Username = name,
            Uuid = OfflineUuid(name),
            AccessToken = "0",
            AccessTokenExpiresAt = DateTime.MaxValue,
            LastUsed = DateTime.Now
        };
    }

    /// <summary>
    /// 确保账号令牌可用。过期时用刷新令牌静默续期；
    /// 续期失败则原样返回（仍可进入离线游戏）。
    /// </summary>
    public static async Task<bool> EnsureValidAsync(Account account, string? clientId,
        CancellationToken ct = default)
    {
        if (!account.IsMicrosoft) return true;
        if (account.IsValid) return true;
        if (!account.CanRefresh || string.IsNullOrWhiteSpace(clientId)) return false;

        try
        {
            var auth = new MicrosoftAuthenticator(clientId.Trim());
            var ms = await auth.RefreshAsync(account.RefreshToken!, ct);
            if (ms?.AccessToken == null) return false;

            var fresh = await auth.LoginAsync(ms, ct);
            account.AccessToken = fresh.AccessToken;
            account.AccessTokenExpiresAt = fresh.AccessTokenExpiresAt;
            account.RefreshToken = fresh.RefreshToken ?? account.RefreshToken;
            account.RefreshTokenExpiresAt = fresh.RefreshTokenExpiresAt;
            account.Xuid = fresh.Xuid ?? account.Xuid;
            account.Username = string.IsNullOrWhiteSpace(fresh.Username) ? account.Username : fresh.Username;
            account.Uuid = string.IsNullOrWhiteSpace(fresh.Uuid) ? account.Uuid : fresh.Uuid;
            account.SkinUrl = fresh.SkinUrl ?? account.SkinUrl;
            return true;
        }
        catch (OperationCanceledException) { throw; }
        catch { return false; }
    }

    /// <summary>头像图片地址（离线账号用正版皮肤站兜底，失败时界面回退为字母头像）。</summary>
    public static string? AvatarUrl(Account account)
    {
        if (string.IsNullOrWhiteSpace(account.Uuid)) return null;
        var id = account.Uuid!.Replace("-", "");
        return $"https://mc-heads.net/avatar/{id}/64";
    }
}
