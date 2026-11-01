using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace RCL.Core.Models;

/// <summary>账号类型常量。</summary>
public static class AccountTypes
{
    public const string Microsoft = "microsoft";
    public const string Offline = "offline";
}

/// <summary>已登录的微软账号，或本地离线账号。</summary>
public class Account
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>账号类型：microsoft（正版）或 offline（离线）。</summary>
    public string Type { get; set; } = AccountTypes.Microsoft;

    /// <summary>Minecraft 用户名（离线账号即玩家名）。</summary>
    public string? Username { get; set; }

    /// <summary>Minecraft UUID。</summary>
    public string? Uuid { get; set; }

    /// <summary>Minecraft Services 访问令牌。</summary>
    public string? AccessToken { get; set; }

    /// <summary>令牌过期时间（UTC）。</summary>
    public DateTime AccessTokenExpiresAt { get; set; }

    /// <summary>微软刷新令牌（用于静默续期）。</summary>
    public string? RefreshToken { get; set; }

    /// <summary>微软刷新令牌过期时间（UTC）。</summary>
    public DateTime RefreshTokenExpiresAt { get; set; }

    /// <summary>Xbox 用户 XUID。</summary>
    public string? Xuid { get; set; }

    /// <summary>皮肤 URL。</summary>
    public string? SkinUrl { get; set; }

    /// <summary>最近使用时间。</summary>
    public DateTime LastUsed { get; set; } = DateTime.Now;

    /// <summary>是否为微软正版账号。</summary>
    [JsonIgnore]
    public bool IsMicrosoft => string.Equals(Type, AccountTypes.Microsoft, StringComparison.OrdinalIgnoreCase);

    /// <summary>账号类型显示名。</summary>
    [JsonIgnore]
    public string TypeLabel => IsMicrosoft ? "微软账号" : "离线账号";

    [JsonIgnore]
    public bool IsValid => !string.IsNullOrEmpty(AccessToken)
        && AccessTokenExpiresAt > DateTime.UtcNow.AddMinutes(5);

    [JsonIgnore]
    public bool CanRefresh => !string.IsNullOrEmpty(RefreshToken)
        && RefreshTokenExpiresAt > DateTime.UtcNow;

    /// <summary>用于展示的名字。</summary>
    [JsonIgnore]
    public string DisplayName => string.IsNullOrWhiteSpace(Username) ? "未命名账号" : Username!;
}
