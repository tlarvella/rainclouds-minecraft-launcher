using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RCL.Core.Download;
using RCL.Core.Java;
using RCL.Core.Utils;

namespace RCL.Core.ModLoaders;

/// <summary>Forge 加载器安装（基于 promotions 清单 + installer jar）。</summary>
public static class ForgeInstaller
{
    private const string PromotionsUrl = "https://files.minecraftforge.net/net/minecraftforge/forge/promotions_slim.json";
    private const string Maven = "https://maven.minecraftforge.net/net/minecraftforge/forge/";

    /// <summary>获取某 Minecraft 版本对应的推荐/最新 Forge 版本。</summary>
    public static async Task<string?> GetForgeVersionAsync(string mcVersion, bool recommended = true, CancellationToken ct = default)
    {
        var json = await Download.Downloader.Client.GetStringAsync(PromotionsUrl, ct);
        var doc = Json.Deserialize<Dictionary<string, JsonElement>>(json)!;
        if (!doc.TryGetValue("promos", out var promos) || promos.ValueKind != JsonValueKind.Object)
            return null;
        var keyRec = $"{mcVersion}-recommended";
        var keyLat = $"{mcVersion}-latest";
        if (recommended && promos.TryGetProperty(keyRec, out var r)) return r.GetString();
        if (promos.TryGetProperty(keyLat, out var l)) return l.GetString();
        if (promos.TryGetProperty(keyRec, out var r2)) return r2.GetString();
        return null;
    }

    /// <summary>安装 Forge，返回版本 id（&lt;mc&gt;-&lt;forge&gt;）。</summary>
    public static async Task<InstallResult> InstallAsync(string javaPath, string mcVersion,
        string? forgeVersion = null, CancellationToken ct = default)
    {
        forgeVersion ??= await GetForgeVersionAsync(mcVersion, ct: ct);
        if (forgeVersion == null)
            return new InstallResult { Success = false, Error = $"未找到 {mcVersion} 对应的 Forge 版本" };

        var full = $"{mcVersion}-{forgeVersion}";
        var installerUrl = $"{Maven}{full}/forge-{full}-installer.jar";
        Directory.CreateDirectory(ModLoaderInstaller.InstallerCacheDir);
        var jar = Path.Combine(ModLoaderInstaller.InstallerCacheDir, $"forge-{full}-installer.jar");
        if (!File.Exists(jar))
            await Downloader.DownloadFileAsync(installerUrl, jar, ct: ct);

        var args = $"--installClient \"{Paths.Root}\"";
        var result = await ModLoaderInstaller.RunInstallerAsync(javaPath, jar, args, ct);
        result.VersionId = full;
        return result;
    }
}
