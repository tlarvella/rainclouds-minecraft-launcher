using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using RCL.Core.Download;
using RCL.Core.Utils;
using RCL.Core.Versions;

namespace RCL.Core.Libraries;

/// <summary>
/// 依赖库解析：按平台规则筛选、收集下载项、拼接 classpath、解压 natives。
/// </summary>
public static class LibraryManager
{
    /// <summary>当前运行平台（启动器实际运行在 Windows 10+）。</summary>
    public static string CurrentOs => "windows";
    public static string CurrentArch => "x86_64";

    private const string LibBase = "https://libraries.minecraft.net/";

    /// <summary>判断某库是否适用于当前平台。</summary>
    public static bool IsAllowed(Library lib)
    {
        if (lib.Rules is null || lib.Rules.Count == 0) return true;
        var allowed = false;
        foreach (var rule in lib.Rules)
        {
            if (!RuleMatches(rule)) continue;
            allowed = rule.Action == "allow";
        }
        return allowed;
    }

    private static bool RuleMatches(Rule rule)
    {
        if (rule.Os != null)
        {
            if (!string.IsNullOrEmpty(rule.Os.Name) && rule.Os.Name != CurrentOs) return false;
            if (!string.IsNullOrEmpty(rule.Os.Arch) && rule.Os.Arch != CurrentArch) return false;
        }
        if (rule.Features != null)
        {
            // 启动器默认不启用任何特性（如 is_demo_user）。
            foreach (var f in rule.Features)
                if (f.Value) return false; // 需要某特性但我们未启用
        }
        return true;
    }

    /// <summary>收集需要下载的库构件（主 jar + 适用 natives）。</summary>
    public static List<DownloadItem> CollectDownloads(ResolvedVersion rv)
    {
        var items = new List<DownloadItem>();
        foreach (var lib in rv.Libraries)
        {
            if (!IsAllowed(lib)) continue;
            if (lib.Artifact != null && !string.IsNullOrEmpty(lib.Artifact.Url))
            {
                var dest = Path.Combine(Paths.LibrariesDir, lib.Artifact.Path!);
                if (!Utils.Hash.Sha1Matches(dest, lib.Artifact.Sha1))
                    items.Add(new DownloadItem
                    {
                        Url = lib.Artifact.Url,
                        Destination = dest,
                        ExpectedSha1 = lib.Artifact.Sha1,
                        Size = lib.Artifact.Size
                    });
            }

            if (lib.Natives != null && lib.Natives.TryGetValue(CurrentOs, out var classifier)
                && lib.Classifiers != null && lib.Classifiers.TryGetValue(classifier, out var native))
            {
                if (string.IsNullOrEmpty(native.Url)) continue;
                var dest = Path.Combine(Paths.LibrariesDir, native.Path!);
                if (!Utils.Hash.Sha1Matches(dest, native.Sha1))
                    items.Add(new DownloadItem
                    {
                        Url = native.Url,
                        Destination = dest,
                        ExpectedSha1 = native.Sha1,
                        Size = native.Size
                    });
            }
        }
        return items;
    }

    /// <summary>拼接 classpath（仅包含有主 jar 的库）。</summary>
    public static string BuildClasspath(ResolvedVersion rv)
    {
        var jars = new List<string>();
        foreach (var lib in rv.Libraries)
        {
            if (!IsAllowed(lib)) continue;
            if (lib.Artifact != null && !string.IsNullOrEmpty(lib.Artifact.Path))
            {
                var p = Path.Combine(Paths.LibrariesDir, lib.Artifact.Path);
                if (File.Exists(p)) jars.Add(p);
            }
        }
        // 追加客户端主 jar
        var clientJar = Paths.VersionJarPath(rv.Id);
        if (File.Exists(clientJar)) jars.Add(clientJar);
        return string.Join(Path.PathSeparator.ToString(), jars);
    }

    /// <summary>解压所有适用 natives 到指定目录。</summary>
    public static void ExtractNatives(ResolvedVersion rv, string nativesDir)
    {
        Directory.CreateDirectory(nativesDir);
        foreach (var lib in rv.Libraries)
        {
            if (!IsAllowed(lib)) continue;
            if (lib.Natives == null || !lib.Natives.TryGetValue(CurrentOs, out var classifier)) continue;
            if (lib.Classifiers == null || !lib.Classifiers.TryGetValue(classifier, out var native)) continue;
            if (string.IsNullOrEmpty(native.Path)) continue;
            var jar = Path.Combine(Paths.LibrariesDir, native.Path);
            if (!File.Exists(jar)) continue;

            using var za = ZipFile.OpenRead(jar);
            foreach (var entry in za.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name) && entry.FullName.EndsWith("/")) continue;
                if (lib.Extract?.Exclude != null &&
                    lib.Extract.Exclude.Any(e => entry.FullName.StartsWith(e, StringComparison.Ordinal)))
                    continue;
                var outPath = Path.Combine(nativesDir, entry.FullName);
                Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
                if (!string.IsNullOrEmpty(entry.Name))
                    entry.ExtractToFile(outPath, overwrite: true);
            }
        }
    }
}
