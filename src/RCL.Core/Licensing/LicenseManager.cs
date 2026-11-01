using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using RCL.Core.Utils;

namespace RCL.Core.Licensing;

public class LicenseState
{
    public string Key { get; set; } = "";
    public DateTime ActivatedAt { get; set; } = DateTime.Now;
}

/// <summary>
/// 一元解锁校验。激活码格式：RCL-XXXXX-XXXXX-XXXXX-XXXXX
/// 前 5 位为随机载荷，后 15 位为基于密钥的 HMAC 签名，本地离线校验，无需联网。
/// </summary>
public static class LicenseManager
{
    // 自定义 Base32 字母表，剔除易混淆的 I / O / 0 / 1
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    // 签名密钥（与 tools/keygen/keygen.py 保持一致）
    private const string Secret = "rainclouds-launcher-2026-9f3c1a7e5b2d4c8f";

    private const int PayloadLength = 5;
    private const int SignatureLength = 15;
    private const int KeyLength = PayloadLength + SignatureLength;

    private static LicenseState? _state;
    private static bool _loaded;

    /// <summary>是否已解锁。</summary>
    public static bool IsActivated
    {
        get
        {
            EnsureLoaded();
            return _state != null && Verify(_state.Key);
        }
    }

    /// <summary>当前激活信息（未激活为 null）。</summary>
    public static LicenseState? State
    {
        get { EnsureLoaded(); return IsActivated ? _state : null; }
    }

    public static string StatusText => IsActivated
        ? "已解锁 · " + Mask(_state!.Key)
        : "未解锁";

    /// <summary>读取本地授权文件。</summary>
    public static void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;
        try
        {
            if (File.Exists(Paths.LicenseFile))
                _state = Json.Deserialize<LicenseState>(File.ReadAllText(Paths.LicenseFile));
        }
        catch
        {
            _state = null;
        }
    }

    /// <summary>校验并激活。</summary>
    public static (bool Ok, string Message) Activate(string? input)
    {
        EnsureLoaded();
        var key = Normalize(input);
        if (key.Length == 0)
            return (false, "请输入激活码。");
        if (key.Length != KeyLength)
            return (false, "激活码长度不正确，应为 RCL- 加 20 位字符。");
        if (!Verify(key))
            return (false, "激活码无效，请检查是否复制完整。");

        _state = new LicenseState { Key = Format(key), ActivatedAt = DateTime.Now };
        try
        {
            Directory.CreateDirectory(Paths.Root);
            File.WriteAllText(Paths.LicenseFile, Json.Serialize(_state));
        }
        catch (Exception ex)
        {
            return (false, "写入授权文件失败：" + ex.Message);
        }
        return (true, "解锁成功，感谢支持！");
    }

    /// <summary>取消激活（用于更换机器或测试）。</summary>
    public static void Deactivate()
    {
        _state = null;
        _loaded = true;
        try { if (File.Exists(Paths.LicenseFile)) File.Delete(Paths.LicenseFile); } catch { }
    }

    /// <summary>去掉前缀与分隔符，转大写。</summary>
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
        if (s.StartsWith("RCL", StringComparison.Ordinal)) s = s[3..];
        return s;
    }

    public static string Format(string key)
    {
        var k = Normalize(key);
        if (k.Length != KeyLength) return key;
        return "RCL-" + k[..5] + "-" + k.Substring(5, 5) + "-" + k.Substring(10, 5) + "-" + k.Substring(15, 5);
    }

    public static string Mask(string? key)
    {
        var k = Normalize(key);
        if (k.Length != KeyLength) return "••••";
        return $"RCL-{k[..5]}-{k.Substring(5, 5)}-*****-{k.Substring(15, 5)}";
    }

    /// <summary>验证签名。</summary>
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
        var digest = hmac.ComputeHash(Encoding.UTF8.GetBytes("RCL1:" + payload));
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
