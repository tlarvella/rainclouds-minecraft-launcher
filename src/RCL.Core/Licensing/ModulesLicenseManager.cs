using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using RCL.Core.Utils;

namespace RCL.Core.Licensing;

/// <summary>进阶模块授权状态。</summary>
public class ModulesLicenseState
{
    public string Key { get; set; } = "";
    public DateTime ActivatedAt { get; set; } = DateTime.Now;
}

/// <summary>
/// 进阶模块（联机 / 内网穿透 / 一键开服）解锁校验。
/// 激活码格式：RCM-XXXXX-XXXXX-XXXXX-XXXXX，本地离线 HMAC 校验，与主程序解锁相互独立。
/// 三模块打包 ¥20 解锁，共用同一把 RCM 授权。
/// </summary>
public static class ModulesLicenseManager
{
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    // 与 tools/keygen/keygen.py --tier modules 的密钥保持一致（不同于主程序 RCL 密钥）
    private const string Secret = "rainclouds-modules-2026-7b3e9c2a1f5d4e8c";

    private const int PayloadLength = 5;
    private const int SignatureLength = 15;
    private const int KeyLength = PayloadLength + SignatureLength;

    private static ModulesLicenseState? _state;
    private static bool _loaded;

    public static bool IsActivated
    {
        get
        {
            EnsureLoaded();
            return _state != null && Verify(_state.Key);
        }
    }

    public static ModulesLicenseState? State
    {
        get { EnsureLoaded(); return IsActivated ? _state : null; }
    }

    public static string StatusText => IsActivated
        ? "进阶模块已解锁 · " + Mask(_state!.Key)
        : "进阶模块未解锁（¥20）";

    public static void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;
        try
        {
            if (File.Exists(Paths.ModulesLicenseFile))
                _state = Json.Deserialize<ModulesLicenseState>(File.ReadAllText(Paths.ModulesLicenseFile));
        }
        catch
        {
            _state = null;
        }
    }

    public static (bool Ok, string Message) Activate(string? input)
    {
        EnsureLoaded();
        var key = Normalize(input);
        if (key.Length == 0)
            return (false, "请输入进阶模块激活码。");
        if (key.Length != KeyLength)
            return (false, "激活码长度不正确，应为 RCM- 加 20 位字符。");
        if (!Verify(key))
            return (false, "激活码无效，请检查是否复制完整。");

        _state = new ModulesLicenseState { Key = Format(key), ActivatedAt = DateTime.Now };
        try
        {
            Directory.CreateDirectory(Paths.Root);
            File.WriteAllText(Paths.ModulesLicenseFile, Json.Serialize(_state));
        }
        catch (Exception ex)
        {
            return (false, "写入授权文件失败：" + ex.Message);
        }
        return (true, "进阶模块解锁成功，联机/穿透/开服已可用！");
    }

    public static void Deactivate()
    {
        _state = null;
        _loaded = true;
        try { if (File.Exists(Paths.ModulesLicenseFile)) File.Delete(Paths.ModulesLicenseFile); } catch { }
    }

    public static string Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return "";
        var sb = new StringBuilder();
        foreach (var c in input.ToUpperInvariant())
        {
            if (c >= 'A' && c <= 'Z') sb.Append(c);
            else if (c >= '0' && c <= '9') sb.Append(c);
        }
        var s = sb.ToString();
        if (s.StartsWith("RCM", StringComparison.Ordinal)) s = s[3..];
        return s;
    }

    public static string Format(string key)
    {
        var k = Normalize(key);
        if (k.Length != KeyLength) return key;
        return "RCM-" + k[..5] + "-" + k.Substring(5, 5) + "-" + k.Substring(10, 5) + "-" + k.Substring(15, 5);
    }

    public static string Mask(string? key)
    {
        var k = Normalize(key);
        if (k.Length != KeyLength) return "••••";
        return $"RCM-{k[..5]}-{k.Substring(5, 5)}-*****-{k.Substring(15, 5)}";
    }

    public static bool Verify(string key)
    {
        var k = Normalize(key);
        if (k.Length != KeyLength) return false;
        var payload = k[..PayloadLength];
        var sig = k[PayloadLength..];
        var expected = ComputeSignature(payload);
        return FixedTimeEquals(sig, expected);
    }

    private static bool FixedTimeEquals(string a, string b)
    {
        if (a.Length != b.Length) return false;
        var diff = 0;
        for (var i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
        return diff == 0;
    }

    private static string ComputeSignature(string payload)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(Secret));
        var digest = hmac.ComputeHash(Encoding.UTF8.GetBytes("RCM1:" + payload));
        return ToBase32(digest, SignatureLength);
    }

    private static string ToBase32(byte[] data, int chars)
    {
        var sb = new StringBuilder(chars);
        var buffer = 0;
        var bits = 0;
        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                sb.Append(Alphabet[(buffer >> (bits - 5)) & 31]);
                bits -= 5;
                if (sb.Length == chars) return sb.ToString();
            }
        }
        while (sb.Length < chars) sb.Append(Alphabet[0]);
        return sb.ToString();
    }
}
