using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace RCL.Core.Utils;

/// <summary>文件哈希与校验工具。</summary>
public static class Hash
{
    public static async Task<string> Sha1Async(string path, CancellationToken ct = default)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        using var sha = SHA1.Create();
        var bytes = await sha.ComputeHashAsync(fs, ct);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static bool Sha1Matches(string path, string? expected)
    {
        if (string.IsNullOrEmpty(expected)) return true;
        if (!File.Exists(path)) return false;
        try
        {
            var actual = Sha1Async(path).GetAwaiter().GetResult();
            return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
