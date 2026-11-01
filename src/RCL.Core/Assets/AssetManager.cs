using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using RCL.Core.Download;
using RCL.Core.Utils;
using RCL.Core.Versions;

namespace RCL.Core.Assets;

/// <summary>资产索引中的单个对象。</summary>
public class AssetObject
{
    public string Hash { get; set; } = "";
    public long Size { get; set; }
}

public class AssetIndex
{
    public bool Virtual { get; set; }
    public Dictionary<string, AssetObject> Objects { get; set; } = new();
}

/// <summary>下载资产索引与对象文件，并处理 legacy 虚拟资产。</summary>
public static class AssetManager
{
    private const string ResourceBase = "https://resources.download.minecraft.net/";

    public static async Task<AssetIndex> EnsureIndexAsync(AssetIndexRef @ref, CancellationToken ct = default)
    {
        var indexFile = Path.Combine(Paths.AssetsIndexesDir, @ref.Id + ".json");
        if (!File.Exists(indexFile) || !Utils.Hash.Sha1Matches(indexFile, @ref.Sha1))
        {
            Directory.CreateDirectory(Paths.AssetsIndexesDir);
            await Downloader.DownloadFileAsync(@ref.Url!, indexFile, @ref.Sha1, ct: ct);
        }
        var json = await File.ReadAllTextAsync(indexFile, ct);
        return Utils.Json.Deserialize<AssetIndex>(json)!;
    }

    public static List<DownloadItem> CollectObjectDownloads(AssetIndex index)
    {
        var items = new List<DownloadItem>();
        foreach (var (path, obj) in index.Objects)
        {
            var prefix = obj.Hash.Substring(0, 2);
            var dest = Path.Combine(Paths.AssetsObjectsDir, prefix, obj.Hash);
            if (!Utils.Hash.Sha1Matches(dest, obj.Hash))
                items.Add(new DownloadItem
                {
                    Url = ResourceBase + prefix + "/" + obj.Hash,
                    Destination = dest,
                    ExpectedSha1 = obj.Hash,
                    Size = obj.Size
                });
        }
        return items;
    }

    /// <summary>对 legacy 虚拟资产，将对象复制为 virtual/&lt;id&gt;/&lt;path&gt;。</summary>
    public static void MaterializeVirtual(AssetIndex index, string id)
    {
        if (!index.Virtual) return;
        var target = Path.Combine(Paths.AssetsVirtualDir, id);
        foreach (var (path, obj) in index.Objects)
        {
            var src = Path.Combine(Paths.AssetsObjectsDir, obj.Hash.Substring(0, 2), obj.Hash);
            if (!File.Exists(src)) continue;
            var dst = Path.Combine(target, path);
            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
            if (!File.Exists(dst))
                File.Copy(src, dst, overwrite: false);
        }
    }
}
