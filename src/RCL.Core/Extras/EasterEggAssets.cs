using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using RCL.Core.Utils;

namespace RCL.Core.Extras;

/// <summary>离线资料馆中的一项资源（壁纸 / 音效 / 资料）。</summary>
public sealed record ExtraItem(string Category, string File, string Title, string Desc);

/// <summary>
/// 访问随 rcl.exe 一起嵌入发布的「离线资料馆 / 彩蛋」资源。
///
/// 资源由 tools/gen_extras.py 生成，经 RCL.Core.csproj 以 EmbeddedResource 嵌入，
/// 因此完全离线可用。资源清单名按「文件名后缀」动态匹配，避免硬编码前缀，
/// 增删资源只需重新生成并构建，无需改这里的代码。
/// </summary>
public static class EasterEggAssets
{
    public const string Wallpapers = "wallpapers";
    public const string Audio = "audio";
    public const string Data = "data";

    private static readonly Assembly Asm = typeof(EasterEggAssets).Assembly;
    private static readonly object Sync = new();
    private static List<ExtraItem>? _items;
    private static string[]? _quotes;

    /// <summary>全部资源项（按 index.json 顺序：壁纸 → 音效 → 资料）。</summary>
    public static IReadOnlyList<ExtraItem> Items
    {
        get
        {
            EnsureLoaded();
            return _items!;
        }
    }

    public static IEnumerable<ExtraItem> Of(string category)
        => Items.Where(i => i.Category == category);

    /// <summary>资源包版本（来自 index.json）。</summary>
    public static string Version
    {
        get
        {
            using var s = OpenStream("index.json");
            if (s == null) return "unknown";
            using var doc = JsonDocument.Parse(s);
            return doc.RootElement.TryGetProperty("version", out var v) ? v.GetString() ?? "unknown" : "unknown";
        }
    }

    /// <summary>本程序集内嵌的 Extras 资源总字节数（诊断 / 体积验证用）。</summary>
    public static long TotalEmbeddedBytes
    {
        get
        {
            long total = 0;
            foreach (var name in Asm.GetManifestResourceNames())
            {
                if (!name.Contains(".Extras.", StringComparison.Ordinal))
                    continue;
                using var s = Asm.GetManifestResourceStream(name);
                if (s != null) total += s.Length;
            }
            return total;
        }
    }

    public static int EmbeddedResourceCount
        => Asm.GetManifestResourceNames().Count(n => n.Contains(".Extras.", StringComparison.Ordinal));

    /// <summary>按文件名读取嵌入资源字节；不存在返回 null。</summary>
    public static byte[]? Read(string file)
    {
        using var s = OpenStream(file);
        if (s == null) return null;
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }

    /// <summary>按文件名打开嵌入资源流；调用方负责释放。</summary>
    public static Stream? OpenStream(string file)
    {
        foreach (var name in Asm.GetManifestResourceNames())
        {
            if (!name.EndsWith("." + file, StringComparison.OrdinalIgnoreCase))
                continue;
            return Asm.GetManifestResourceStream(name);
        }
        return null;
    }

    /// <summary>按文件名读取 UTF-8 文本资源；不存在返回空串。</summary>
    public static string ReadText(string file)
    {
        var data = Read(file);
        return data == null ? "" : Encoding.UTF8.GetString(data);
    }

    // ------------------------------------------------------------ 名言

    /// <summary>去掉注释与空行后的名言列表。</summary>
    public static IReadOnlyList<string> Quotes
    {
        get
        {
            if (_quotes != null) return _quotes;
            lock (Sync)
            {
                _quotes ??= ReadText("quotes.txt")
                    .Split('\n')
                    .Select(l => l.Trim())
                    .Where(l => l.Length > 0 && !l.StartsWith("#"))
                    .ToArray();
            }
            return _quotes;
        }
    }

    /// <summary>随机取一条启动名言；无资源时返回默认短句。</summary>
    public static string RandomQuote()
    {
        var q = Quotes;
        return q.Count == 0 ? "雨会停，存档还在。" : q[Random.Shared.Next(q.Count)];
    }

    // ------------------------------------------------------------ 导出 / 背景

    /// <summary>用户可写目录：%APPDATA%/RaincloudsMC/extras。</summary>
    public static string ExtrasDir => Path.Combine(Paths.Root, "extras");

    /// <summary>启动页自定义背景图路径。</summary>
    public static string BackgroundPath => Path.Combine(ExtrasDir, "background.png");

    /// <summary>把某项资源导出到指定目录（保持分类子目录），返回写出的完整路径。</summary>
    public static string Export(string file, string dir)
    {
        var item = Items.FirstOrDefault(i => i.File == file);
        var sub = item?.Category ?? "misc";
        var targetDir = Path.Combine(dir, sub);
        Directory.CreateDirectory(targetDir);
        var path = Path.Combine(targetDir, file);
        var data = Read(file) ?? throw new InvalidOperationException($"内置资源缺失：{file}");
        File.WriteAllBytes(path, data);
        return path;
    }

    /// <summary>导出全部资源到指定目录，返回 (文件数, 总字节)。</summary>
    public static (int Count, long Bytes) ExportAll(string dir)
    {
        int count = 0;
        long bytes = 0;
        foreach (var item in Items)
        {
            var data = Read(item.File);
            if (data == null) continue;
            var targetDir = Path.Combine(dir, item.Category);
            Directory.CreateDirectory(targetDir);
            File.WriteAllBytes(Path.Combine(targetDir, item.File), data);
            count++;
            bytes += data.Length;
        }
        // 清单与生成器说明一并导出，便于离线查证来源
        var idx = Read("index.json");
        if (idx != null)
        {
            File.WriteAllBytes(Path.Combine(dir, "index.json"), idx);
            count++;
            bytes += idx.Length;
        }
        return (count, bytes);
    }

    /// <summary>把某张壁纸设为启动页背景（复制为 background.png）。</summary>
    public static void SetBackground(string wallpaperFile)
    {
        var data = Read(wallpaperFile)
                   ?? throw new InvalidOperationException($"内置壁纸缺失：{wallpaperFile}");
        Directory.CreateDirectory(ExtrasDir);
        File.WriteAllBytes(BackgroundPath, data);
    }

    /// <summary>恢复默认启动页（删除自定义背景）。</summary>
    public static void ResetBackground()
    {
        try { if (File.Exists(BackgroundPath)) File.Delete(BackgroundPath); }
        catch { /* 背景清理失败不影响主流程 */ }
    }

    public static bool HasCustomBackground => File.Exists(BackgroundPath);

    /// <summary>把资源字节写到临时文件（音效试听用），返回路径。</summary>
    public static string ToTempFile(string file)
    {
        var data = Read(file) ?? throw new InvalidOperationException($"内置资源缺失：{file}");
        var path = Path.Combine(Path.GetTempPath(), "rcl_extras_" + Guid.NewGuid().ToString("N") +
                                Path.GetExtension(file));
        File.WriteAllBytes(path, data);
        return path;
    }

    // ------------------------------------------------------------ 内部

    private static void EnsureLoaded()
    {
        if (_items != null) return;
        lock (Sync)
        {
            if (_items != null) return;
            var list = new List<ExtraItem>();
            using var s = OpenStream("index.json");
            if (s != null)
            {
                using var doc = JsonDocument.Parse(s);
                foreach (var cat in new[] { Wallpapers, Audio, Data })
                {
                    if (!doc.RootElement.TryGetProperty(cat, out var arr)) continue;
                    foreach (var e in arr.EnumerateArray())
                    {
                        list.Add(new ExtraItem(
                            cat,
                            Get(e, "file"),
                            Get(e, "title"),
                            Get(e, "desc")));
                    }
                }
            }
            _items = list;
        }
    }

    private static string Get(JsonElement e, string prop)
        => e.TryGetProperty(prop, out var v) ? v.GetString() ?? "" : "";
}
