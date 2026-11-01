using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RCL.Core.Utils;

namespace RCL.Core.Versions;

/// <summary>合并后的最终版本信息，供下载与启动使用。</summary>
public class ResolvedVersion
{
    public string Id { get; set; } = "";
    public string MainClass { get; set; } = "";
    public List<Library> Libraries { get; set; } = new();
    public List<JsonElement> GameArgs { get; set; } = new();
    public List<JsonElement> JvmArgs { get; set; } = new();
    public string? LegacyArguments { get; set; }
    public AssetIndexRef? AssetIndex { get; set; }
    public Artifact? ClientDownload { get; set; }
    public LoggingConfig? Logging { get; set; }
    public JavaVersion? JavaVersion { get; set; }
}

/// <summary>
/// 加载版本 JSON（优先本地，否则从清单下载并缓存），
/// 并递归合并 inheritsFrom 得到完整启动信息。
/// </summary>
public static class VersionResolver
{
    /// <summary>获取版本 JSON：本地存在则直接读，否则从清单下载缓存。</summary>
    public static async Task<VersionJson> GetVersionJsonAsync(string versionId, CancellationToken ct = default)
    {
        var local = Paths.VersionJsonPath(versionId);
        if (File.Exists(local))
            return Json.Deserialize<VersionJson>(await File.ReadAllTextAsync(local, ct))!;

        var entry = await VersionManifestFetcher.FindEntryAsync(versionId, ct)
                    ?? throw new InvalidOperationException($"找不到版本 {versionId} 的清单条目");
        var json = await Download.Downloader.Client.GetStringAsync(entry.Url, ct);
        Directory.CreateDirectory(Path.GetDirectoryName(local)!);
        await File.WriteAllTextAsync(local, json, ct);
        return Json.Deserialize<VersionJson>(json)!;
    }

    /// <summary>递归解析并合并 inheritsFrom。</summary>
    public static async Task<ResolvedVersion> ResolveAsync(string versionId, CancellationToken ct = default)
    {
        var chain = new List<VersionJson>();
        var current = versionId;
        var guard = 0;
        while (current != null && guard++ < 20)
        {
            var vj = await GetVersionJsonAsync(current, ct);
            chain.Add(vj);
            current = vj.InheritsFrom;
        }

        // chain[0] 是最子节点；父节点在后面。合并时父在前、子覆盖。
        var merged = new ResolvedVersion { Id = versionId };
        List<Library> mergedLibs = new();
        var libOrder = new List<string>();
        var libByName = new Dictionary<string, Library>();

        List<JsonElement> gameArgs = new();
        List<JsonElement> jvmArgs = new();
        string? legacy = null;

        foreach (var vj in chain.AsEnumerable().Reverse()) // 父 -> 子
        {
            if (!string.IsNullOrEmpty(vj.MainClass)) merged.MainClass = vj.MainClass;
            if (vj.AssetIndex != null) merged.AssetIndex = vj.AssetIndex;
            if (vj.ClientDownloadSafe() != null) merged.ClientDownload = vj.ClientDownloadSafe();
            if (vj.Logging != null) merged.Logging = vj.Logging;
            if (vj.JavaVersion != null) merged.JavaVersion = vj.JavaVersion;

            if (!string.IsNullOrEmpty(vj.MinecraftArguments))
                legacy = vj.MinecraftArguments;

            if (vj.Arguments?.Game != null)
                gameArgs.AddRange(vj.Arguments.Game);
            if (vj.Arguments?.Jvm != null)
                jvmArgs.AddRange(vj.Arguments.Jvm);

            if (vj.Libraries != null)
            {
                foreach (var lib in vj.Libraries)
                {
                    if (string.IsNullOrEmpty(lib.Name)) continue;
                    libByName[lib.Name] = lib;
                    if (!libOrder.Contains(lib.Name)) libOrder.Add(lib.Name);
                }
            }
        }

        merged.Libraries = libOrder.Select(n => libByName[n]).ToList();
        merged.GameArgs = gameArgs;
        merged.JvmArgs = jvmArgs;
        merged.LegacyArguments = legacy;
        return merged;
    }
}

internal static class VersionJsonExtensions
{
    public static Artifact? ClientDownloadSafe(this VersionJson vj) => vj.Downloads?.Client;
}
