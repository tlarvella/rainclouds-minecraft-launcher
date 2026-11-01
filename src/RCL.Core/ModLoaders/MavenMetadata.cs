using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using RCL.Core.Utils;

namespace RCL.Core.ModLoaders;

/// <summary>解析 Maven 仓库的 maven-metadata.xml，获取可用版本。</summary>
public static class MavenMetadata
{
    public static async Task<List<string>> GetVersionsAsync(string metadataUrl, CancellationToken ct = default)
    {
        var xml = await Download.Downloader.Client.GetStringAsync(metadataUrl, ct);
        var doc = XDocument.Parse(xml);
        var versions = doc.Root?.Element("versioning")?.Element("versions");
        if (versions == null) return new List<string>();
        return versions.Elements("version").Select(e => e.Value).ToList();
    }

    public static async Task<string?> GetLatestAsync(string metadataUrl, CancellationToken ct = default)
    {
        var xml = await Download.Downloader.Client.GetStringAsync(metadataUrl, ct);
        var doc = XDocument.Parse(xml);
        var v = doc.Root?.Element("versioning");
        var latest = v?.Element("release")?.Value ?? v?.Element("latest")?.Value;
        if (!string.IsNullOrEmpty(latest)) return latest;
        var versions = v?.Element("versions")?.Elements("version").Select(e => e.Value).ToList();
        return versions?.LastOrDefault();
    }
}
