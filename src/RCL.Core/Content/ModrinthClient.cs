using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using RCL.Core.Utils;

namespace RCL.Core.Content;

public class ModrinthFile
{
    [JsonPropertyName("url")] public string Url { get; set; } = "";
    [JsonPropertyName("filename")] public string Filename { get; set; } = "";
    [JsonPropertyName("primary")] public bool Primary { get; set; }
    [JsonPropertyName("size")] public long Size { get; set; }
    [JsonPropertyName("hashes")] public Dictionary<string, string>? Hashes { get; set; }

    public string? Sha1 => Hashes != null && Hashes.TryGetValue("sha1", out var v) ? v : null;
}

public class ModrinthVersion
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("version_number")] public string VersionNumber { get; set; } = "";
    [JsonPropertyName("game_versions")] public List<string> GameVersions { get; set; } = new();
    [JsonPropertyName("loaders")] public List<string> Loaders { get; set; } = new();
    [JsonPropertyName("files")] public List<ModrinthFile> Files { get; set; } = new();

    public ModrinthFile? PrimaryFile =>
        Files.FirstOrDefault(f => f.Primary) ?? Files.FirstOrDefault();
}

/// <summary>Modrinth 开放 API 客户端（无需密钥）。</summary>
public static class ModrinthClient
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(40),
        DefaultRequestHeaders = { { "User-Agent", "RaincloudsLauncher/1.1 (rcl)" } }
    };

    public static async Task<List<ModrinthVersion>> GetVersionsAsync(string slug, string gameVersion,
        string loader = "fabric", CancellationToken ct = default)
    {
        var gv = Uri.EscapeDataString("[\"" + gameVersion + "\"]");
        var ld = Uri.EscapeDataString("[\"" + loader + "\"]");
        var url = $"https://api.modrinth.com/v2/project/{Uri.EscapeDataString(slug)}/version?game_versions={gv}&loaders={ld}";
        using var resp = await Http.GetAsync(url, ct);
        if (!resp.IsSuccessStatusCode) return new List<ModrinthVersion>();
        var json = await resp.Content.ReadAsStringAsync(ct);
        // 接口本身按发布时间倒序返回，取第一条即最新兼容版本
        return Json.Deserialize<List<ModrinthVersion>>(json) ?? new List<ModrinthVersion>();
    }

    /// <summary>按关键词搜索项目（用于模组搜索界面）。</summary>
    public static async Task<List<ModrinthSearchHit>> SearchAsync(string query, string gameVersion,
        string loader = "fabric", int limit = 20, CancellationToken ct = default)
    {
        var url = "https://api.modrinth.com/v2/search?limit=" + limit
                  + "&query=" + Uri.EscapeDataString(query)
                  + "&facets=" + Uri.EscapeDataString($"[[\"project_type:mod\"],[\"categories:{loader}\"],[\"versions:{gameVersion}\"]]");
        using var resp = await Http.GetAsync(url, ct);
        if (!resp.IsSuccessStatusCode) return new List<ModrinthSearchHit>();
        var d = Json.Deserialize<ModrinthSearchResult>(await resp.Content.ReadAsStringAsync(ct));
        return d?.Hits ?? new List<ModrinthSearchHit>();
    }
}

public class ModrinthSearchResult
{
    [JsonPropertyName("hits")] public List<ModrinthSearchHit> Hits { get; set; } = new();
}

public class ModrinthSearchHit
{
    [JsonPropertyName("slug")] public string Slug { get; set; } = "";
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("description")] public string Description { get; set; } = "";
    [JsonPropertyName("downloads")] public long Downloads { get; set; }
    [JsonPropertyName("icon_url")] public string? IconUrl { get; set; }
}
