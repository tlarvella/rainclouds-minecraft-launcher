using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RCL.Core.Content;

/// <summary>一条内容（模组 / 插件 / 资源包）。</summary>
public class ContentItem
{
    public string FullPath { get; set; } = "";
    public string FileName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Version { get; set; } = "";
    public string Kind { get; set; } = "模组";
    public string Loader { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public long Size { get; set; }

    public string SubText => string.IsNullOrWhiteSpace(Version)
        ? FileName
        : $"{Version} · {FileName}";

    public string SizeText => Size >= 1024 * 1024
        ? $"{Size / 1024.0 / 1024.0:0.0} MB"
        : $"{Math.Max(1, Size / 1024)} KB";
}

/// <summary>
/// 模组 / 插件管理：扫描实例的 mods 目录，读取 jar 内元数据识别名称与来源，
/// 支持启用/禁用（改扩展名，不删文件）、导入与删除。
/// </summary>
public static class ContentManager
{
    private static readonly string[] EnabledExts = { ".jar", ".zip", ".litemod" };
    private const string DisabledSuffix = ".disabled";

    /// <summary>扫描目录下的所有内容项。</summary>
    public static List<ContentItem> Scan(string dir)
    {
        var result = new List<ContentItem>();
        if (!Directory.Exists(dir)) return result;

        foreach (var path in Directory.GetFiles(dir))
        {
            var name = Path.GetFileName(path);
            var lower = name.ToLowerInvariant();
            var enabled = true;
            var probe = lower;

            if (lower.EndsWith(DisabledSuffix))
            {
                enabled = false;
                probe = lower[..^DisabledSuffix.Length];
            }

            if (!EnabledExts.Any(e => probe.EndsWith(e))) continue;

            var item = new ContentItem
            {
                FullPath = path,
                FileName = enabled ? name : name[..^DisabledSuffix.Length],
                Enabled = enabled,
                Size = TrySize(path)
            };

            var meta = Describe(path);
            item.DisplayName = string.IsNullOrWhiteSpace(meta.Name)
                ? Path.GetFileNameWithoutExtension(item.FileName)
                : meta.Name;
            item.Version = meta.Version;
            item.Kind = meta.Kind;
            item.Loader = meta.Loader;
            result.Add(item);
        }

        return result
            .OrderByDescending(i => i.Enabled)
            .ThenBy(i => i.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static long TrySize(string path)
    {
        try { return new FileInfo(path).Length; } catch { return 0; }
    }

    /// <summary>读取 jar 内的元数据，判断是模组还是插件。</summary>
    public static (string Name, string Version, string Kind, string Loader) Describe(string path)
    {
        try
        {
            using var zip = ZipFile.OpenRead(path);
            var names = zip.Entries.Select(e => e.FullName.Replace('\\', '/')).ToList();

            // Fabric / Quilt 模组
            var fabric = zip.GetEntry("fabric.mod.json") ?? zip.GetEntry("quilt.mod.json");
            if (fabric != null)
            {
                var text = ReadEntry(fabric);
                using var doc = JsonDocument.Parse(text);
                var root = doc.RootElement;
                var name = root.TryGetProperty("name", out var n) ? n.GetString() : null;
                var ver = root.TryGetProperty("version", out var v) ? v.GetString() : null;
                if (string.IsNullOrWhiteSpace(ver) && root.TryGetProperty("quilt_loader", out var ql)
                    && ql.TryGetProperty("version", out var qv))
                    ver = qv.GetString();
                return (name ?? "", ver ?? "", "模组", fabric.FullName.StartsWith("quilt") ? "Quilt" : "Fabric");
            }

            // Forge / NeoForge 模组
            var toml = zip.GetEntry("META-INF/mods.toml") ?? zip.GetEntry("META-INF/neoforge.mods.toml");
            if (toml != null)
            {
                var text = ReadEntry(toml);
                var display = Match(text, @"displayName\s*=\s*""([^""]*)""") ?? Match(text, @"modId\s*=\s*""([^""]*)""");
                var ver = Match(text, @"^\s*version\s*=\s*""([^""]*)""");
                var loader = toml.FullName.Contains("neoforge") ? "NeoForge" : "Forge";
                return (display ?? "", ver ?? "", "模组", loader);
            }

            // Bukkit / Spigot / Paper 插件
            var pluginYml = zip.GetEntry("plugin.yml") ?? zip.GetEntry("paper-plugin.yml") ?? zip.GetEntry("bungee.yml");
            if (pluginYml != null)
            {
                var text = ReadEntry(pluginYml);
                var name = Match(text, @"(?m)^\s*name\s*:\s*['""]?([^'""\r\n]+)");
                var ver = Match(text, @"(?m)^\s*version\s*:\s*['""]?([^'""\r\n]+)");
                return (name ?? "", ver ?? "", "插件", "插件");
            }

            // 资源包 / 光影包
            if (names.Contains("pack.mcmeta"))
            {
                var entry = zip.GetEntry("pack.mcmeta")!;
                var text = ReadEntry(entry);
                var name = Match(text, @"""description""\s*:\s*""([^""]*)""");
                var fmt = Match(text, @"""pack_format""\s*:\s*(\d+)");
                return (name ?? "", fmt == null ? "" : "格式 " + fmt, "资源包", "资源包");
            }
        }
        catch
        {
            // 损坏或非 zip，忽略
        }

        return ("", "", "模组", "");
    }

    private static string ReadEntry(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var reader = new StreamReader(stream);
        var buffer = new char[64 * 1024];
        var read = reader.ReadBlock(buffer, 0, buffer.Length);
        return new string(buffer, 0, read);
    }

    private static string? Match(string text, string pattern)
    {
        var m = Regex.Match(text, pattern);
        return m.Success ? m.Groups[1].Value.Trim() : null;
    }

    /// <summary>启用/禁用（通过重命名，不删除文件）。</summary>
    public static string SetEnabled(string path, bool enabled)
    {
        var lower = path.ToLowerInvariant();
        var isDisabled = lower.EndsWith(DisabledSuffix);

        if (enabled && isDisabled)
        {
            var target = path[..^DisabledSuffix.Length];
            File.Move(path, target, overwrite: true);
            return target;
        }

        if (!enabled && !isDisabled)
        {
            var target = path + DisabledSuffix;
            File.Move(path, target, overwrite: true);
            return target;
        }

        return path;
    }

    /// <summary>导入一个文件到目录，重名自动加序号。</summary>
    public static string Import(string sourceFile, string dir)
    {
        Directory.CreateDirectory(dir);
        var name = Path.GetFileName(sourceFile);
        var target = Path.Combine(dir, name);
        var i = 1;
        while (File.Exists(target))
        {
            var stem = Path.GetFileNameWithoutExtension(name);
            var ext = Path.GetExtension(name);
            target = Path.Combine(dir, $"{stem} ({i}){ext}");
            i++;
        }
        File.Copy(sourceFile, target, overwrite: false);
        return target;
    }

    /// <summary>删除一个内容文件。</summary>
    public static void Delete(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }
}
