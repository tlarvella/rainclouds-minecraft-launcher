using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RCL.Core.Utils;

namespace RCL.Core.Versions;

/// <summary>版本清单条目。</summary>
public class VersionEntry
{
    public string Id { get; set; } = "";
    public string Type { get; set; } = "release";
    public string Url { get; set; } = "";
    public string Time { get; set; } = "";
    public string ReleaseTime { get; set; } = "";
}

public class LatestInfo
{
    public string Release { get; set; } = "";
    public string Snapshot { get; set; } = "";
}

public class VersionManifest
{
    public LatestInfo Latest { get; set; } = new();
    public List<VersionEntry> Versions { get; set; } = new();
}

/// <summary>拉取并解析 Mojang 官方版本清单。</summary>
public static class VersionManifestFetcher
{
    private const string ManifestUrl = "https://launchermeta.mojang.com/mc/game/version_manifest_v2.json";

    public static async Task<VersionManifest> FetchAsync(CancellationToken ct = default)
    {
        var json = await Download.Downloader.Client.GetStringAsync(ManifestUrl, ct);
        return Json.Deserialize<VersionManifest>(json)!;
    }

    public static async Task<string?> ResolveVersionIdAsync(string selector, CancellationToken ct = default)
    {
        if (selector is "latest_release" or "latest_snapshot")
        {
            var m = await FetchAsync(ct);
            return selector == "latest_release" ? m.Latest.Release : m.Latest.Snapshot;
        }
        return selector;
    }

    public static async Task<VersionEntry?> FindEntryAsync(string versionId, CancellationToken ct = default)
    {
        var m = await FetchAsync(ct);
        return m.Versions.Find(v => v.Id == versionId);
    }
}
