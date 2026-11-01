using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RCL.Core.Models;
using RCL.Core.Utils;

namespace RCL.Core.Auth;

/// <summary>设备码登录第一步返回的信息，需展示给用户去浏览器输入。</summary>
public class DeviceCodeInfo
{
    public string UserCode { get; set; } = "";
    public string VerificationUrl { get; set; } = "";
    public string Message { get; set; } = "";
    public int ExpiresIn { get; set; }
    public int Interval { get; set; }
    public string DeviceCode { get; set; } = "";
}

public class TokenResponse
{
    public string? AccessToken { get; set; }
    public string? RefreshToken { get; set; }
    public int ExpiresIn { get; set; }
    public int RefreshTokenExpiresIn { get; set; }
    public string? TokenType { get; set; }
    public string? Error { get; set; }
    public string? ErrorDescription { get; set; }
}

public class XboxResponse
{
    public string? Token { get; set; }
    public string? Uhs { get; set; }
}

/// <summary>
/// 微软设备码登录 → Xbox Live → XSTS → Minecraft Services 的完整认证流程。
/// 参考 Mojang/Microsoft 官方 OAuth 文档实现。
/// </summary>
public sealed class MicrosoftAuthenticator
{
    private static readonly HttpClient Http = Download.Downloader.Client;
    private const string AuthBase = "https://login.microsoftonline.com/consumers/oauth2/v2.0";
    private const string XboxUserAuth = "https://user.auth.xboxlive.com/user/authenticate";
    private const string XboxXstsAuth = "https://xsts.auth.xboxlive.com/xsts/authorize";
    private const string McLogin = "https://api.minecraftservices.com/authentication/login_with_xbox";
    private const string McProfile = "https://api.minecraftservices.com/minecraft/profile";
    private const string McEntitlements = "https://api.minecraftservices.com/entitlements/mcstore";

    /// <summary>Minecraft 设备码登录所需的作用域。</summary>
    public const string Scope = "XboxLive.signin offline_access";

    public MicrosoftAuthenticator(string clientId)
    {
        ClientId = (clientId ?? string.Empty).Trim();
    }

    public string ClientId { get; }

    /// <summary>是否已配置 ClientId。未配置时无法发起设备码流程。</summary>
    public bool HasClientId => ClientId.Length > 0;

    /// <summary>第一步：获取设备码。</summary>
    public async Task<DeviceCodeInfo> RequestDeviceCodeAsync(CancellationToken ct = default)
    {
        if (!HasClientId)
            throw new InvalidOperationException(
                "尚未配置 Azure 应用 ClientId。微软要求第三方启动器使用自己注册的应用，请点击“如何获取”按说明注册后填入。");

        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = ClientId,
            ["scope"] = Scope
        });
        using var resp = await Http.PostAsync($"{AuthBase}/devicecode", content, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException("微软拒绝请求：" + ExtractError(body));
        }
        var json = await resp.Content.ReadAsStringAsync(ct);
        var d = Json.Deserialize<Dictionary<string, JsonElement>>(json)!;
        return new DeviceCodeInfo
        {
            UserCode = d["user_code"].GetString() ?? "",
            VerificationUrl = d.TryGetValue("verification_uri", out var vu) ? vu.GetString() ?? "" : (d.TryGetValue("verification_url", out var vu2) ? vu2.GetString() ?? "" : ""),
            Message = d["message"].GetString() ?? "",
            ExpiresIn = d.TryGetValue("expires_in", out var e) ? e.GetInt32() : 900,
            Interval = d.TryGetValue("interval", out var i) ? i.GetInt32() : 5,
            DeviceCode = d["device_code"].GetString() ?? ""
        };
    }

    /// <summary>第二步：轮询直到用户完成登录，返回微软令牌。</summary>
    public async Task<TokenResponse> PollForTokenAsync(DeviceCodeInfo device, CancellationToken ct = default)
    {
        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = ClientId,
            ["device_code"] = device.DeviceCode,
            ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
            ["scope"] = Scope
        });
        var deadline = DateTime.UtcNow.AddSeconds(device.ExpiresIn);
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(Math.Max(1, device.Interval) * 1000, ct);
            using var resp = await Http.PostAsync($"{AuthBase}/token", content, ct);
            var json = await resp.Content.ReadAsStringAsync(ct);
            var d = Json.Deserialize<Dictionary<string, JsonElement>>(json);
            if (d is null) continue;
            var code = d.TryGetValue("error", out var errEl) && errEl.ValueKind == JsonValueKind.String
                ? errEl.GetString() : null;

            if (code == "authorization_pending" || code == "slow_down") continue;
            if (code == "expired_token") return new TokenResponse { Error = "expired_token" };
            if (code != null)
            {
                var desc = d.TryGetValue("error_description", out var edEl) && edEl.ValueKind == JsonValueKind.String
                    ? edEl.GetString() : json;
                return new TokenResponse { Error = code, ErrorDescription = desc };
            }
            if (!resp.IsSuccessStatusCode)
                return new TokenResponse { Error = $"HTTP {(int)resp.StatusCode}", ErrorDescription = json };

            return new TokenResponse
            {
                AccessToken = d.GetValueOrDefault("access_token").GetString(),
                RefreshToken = d.GetValueOrDefault("refresh_token").GetString(),
                ExpiresIn = d.TryGetValue("expires_in", out var ei) ? ei.GetInt32() : 3600,
                RefreshTokenExpiresIn = d.TryGetValue("refresh_token_expires_in", out var re) ? re.GetInt32() : 2592000,
                TokenType = d.GetValueOrDefault("token_type").GetString()
            };
        }
        return new TokenResponse { Error = "timeout" };
    }

    /// <summary>用刷新令牌静默续期。</summary>
    public async Task<TokenResponse?> RefreshAsync(string refreshToken, CancellationToken ct = default)
    {
        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = ClientId,
            ["refresh_token"] = refreshToken,
            ["grant_type"] = "refresh_token",
            ["scope"] = Scope
        });
        using var resp = await Http.PostAsync($"{AuthBase}/token", content, ct);
        if (!resp.IsSuccessStatusCode) return null;
        var d = Json.Deserialize<Dictionary<string, JsonElement>>(await resp.Content.ReadAsStringAsync(ct))!;
        return new TokenResponse
        {
            AccessToken = d.GetValueOrDefault("access_token").GetString(),
            RefreshToken = d.GetValueOrDefault("refresh_token").GetString() ?? refreshToken,
            ExpiresIn = d.TryGetValue("expires_in", out var ei) ? ei.GetInt32() : 3600,
            RefreshTokenExpiresIn = d.TryGetValue("refresh_token_expires_in", out var re) ? re.GetInt32() : 2592000
        };
    }

    private static async Task<XboxResponse> XboxAuthenticateAsync(string url, object body, CancellationToken ct)
    {
        using var resp = await Http.PostAsJsonAsync(url, body, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var errBody = await resp.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException("Xbox 认证失败：" + ExtractError(errBody));
        }
        var d = Json.Deserialize<Dictionary<string, JsonElement>>(await resp.Content.ReadAsStringAsync(ct))!;
        var token = d["Token"].GetString();
        string? uhs = null;
        if (d.TryGetValue("DisplayClaims", out var dc) && dc.ValueKind == JsonValueKind.Object)
        {
            if (dc.TryGetProperty("xui", out var xui) && xui.ValueKind == JsonValueKind.Array && xui.GetArrayLength() > 0)
                uhs = xui[0].GetProperty("uhs").GetString();
        }
        return new XboxResponse { Token = token, Uhs = uhs };
    }

    /// <summary>完整登录：返回可用的 Minecraft 账号（含令牌与资料）。</summary>
    public async Task<Account> LoginAsync(TokenResponse msToken, CancellationToken ct = default)
    {
        var xbl = await XboxAuthenticateAsync(XboxUserAuth, new
        {
            Properties = new
            {
                AuthMethod = "RPS",
                SiteName = "user.auth.xboxlive.com",
                RpsTicket = "d=" + msToken.AccessToken
            },
            RelyingParty = "http://auth.xboxlive.com",
            TokenType = "JWT"
        }, ct);

        var xsts = await XboxAuthenticateAsync(XboxXstsAuth, new
        {
            Properties = new
            {
                SandboxId = "RETAIL",
                UserTokens = new[] { xbl.Token }
            },
            RelyingParty = "rp://api.minecraftservices.com/",
            TokenType = "JWT"
        }, ct);

        var identity = $"XBL3.0 x={xsts.Uhs};{xsts.Token}";
        using var mcReq = new HttpRequestMessage(HttpMethod.Post, McLogin)
        {
            Content = JsonContent.Create(new { identityToken = identity })
        };
        using var mcResp = await Http.SendAsync(mcReq, ct);
        if (!mcResp.IsSuccessStatusCode)
        {
            var errBody = await mcResp.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException("Minecraft 登录失败：" + ExtractError(errBody));
        }
        var mc = Json.Deserialize<Dictionary<string, JsonElement>>(await mcResp.Content.ReadAsStringAsync(ct))!;
        var mcToken = mc["access_token"].GetString();

        var account = new Account
        {
            Type = "microsoft",
            AccessToken = mcToken,
            AccessTokenExpiresAt = DateTime.UtcNow.AddSeconds(msToken.ExpiresIn),
            RefreshToken = msToken.RefreshToken,
            RefreshTokenExpiresAt = DateTime.UtcNow.AddSeconds(msToken.RefreshTokenExpiresIn),
            Xuid = xsts.Uhs
        };

        await FillProfileAsync(account, ct);
        return account;
    }

    /// <summary>拉取 Minecraft 资料（用户名、UUID、皮肤）。</summary>
    public async Task FillProfileAsync(Account account, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, McProfile);
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", account.AccessToken);
        using var resp = await Http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode) return;
        var d = Json.Deserialize<Dictionary<string, JsonElement>>(await resp.Content.ReadAsStringAsync(ct));
        if (d is null) return;
        account.Uuid = d.GetValueOrDefault("id").GetString();
        account.Username = d.GetValueOrDefault("name").GetString();
        if (d.TryGetValue("skins", out var skins) && skins.ValueKind == JsonValueKind.Array && skins.GetArrayLength() > 0)
            account.SkinUrl = skins[0].GetProperty("url").GetString();
    }

    /// <summary>检查账号是否拥有正版 Minecraft 授权。</summary>
    public async Task<bool> HasEntitlementAsync(Account account, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, McEntitlements);
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", account.AccessToken);
        using var resp = await Http.SendAsync(req, ct);
        return resp.IsSuccessStatusCode;
    }

    /// <summary>从微软错误响应中提取可读原因（优先 AADSTS 描述），避免只显示 HTTP 400。</summary>
    private static string ExtractError(string body)
    {
        try
        {
            var d = Json.Deserialize<Dictionary<string, JsonElement>>(body);
            if (d != null)
            {
                if (d.TryGetValue("error_description", out var ed) && ed.ValueKind == JsonValueKind.String)
                {
                    var s = ed.GetString() ?? "";
                    var cut = s.IndexOf(" Trace ID:", StringComparison.OrdinalIgnoreCase);
                    if (cut > 0) s = s.Substring(0, cut);
                    if (s.Length > 300) s = s.Substring(0, 300) + "…";
                    return s;
                }
                if (d.TryGetValue("error", out var er) && er.ValueKind == JsonValueKind.String)
                    return er.GetString() ?? "未知错误";
            }
        }
        catch
        {
            // 非 JSON 响应，原样返回
        }
        return body.Length > 300 ? body.Substring(0, 300) + "…" : body;
    }
}
