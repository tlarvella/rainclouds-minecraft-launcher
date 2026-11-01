using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RCL.Core.Assets;
using RCL.Core.Download;
using RCL.Core.Java;
using RCL.Core.Libraries;
using RCL.Core.Models;
using RCL.Core.ModLoaders;
using RCL.Core.Profiles;
using RCL.Core.Utils;
using RCL.Core.Versions;

namespace RCL.Core.Launch;

/// <summary>准备阶段进度。</summary>
public class PrepareProgress
{
    public string Stage { get; set; } = "";
    public int Done { get; set; }
    public int Total { get; set; }
    public long Bytes { get; set; }
}

/// <summary>准备完成的游戏信息。</summary>
public class PreparedGame
{
    public string JavaPath { get; set; } = "";
    public string GameDir { get; set; } = "";
    public ResolvedVersion Version { get; set; } = null!;
}

/// <summary>编排：准备 Java、安装 Mod 加载器、下载资源、解压 natives。</summary>
public static class GameManager
{
    public static async Task<string> ResolveBaseVersionIdAsync(string selector, CancellationToken ct = default)
        => await VersionManifestFetcher.ResolveVersionIdAsync(selector, ct) ?? selector;

    private static int DefaultJavaMajor(string versionId)
    {
        // 根据版本粗略推断所需 Java 主版本
        if (versionId.StartsWith("1.7") || versionId.StartsWith("1.8") || versionId.StartsWith("1.9")
            || versionId.StartsWith("1.10") || versionId.StartsWith("1.11") || versionId.StartsWith("1.12"))
            return 8;
        if (versionId.StartsWith("1.13") || versionId.StartsWith("1.14") || versionId.StartsWith("1.15") || versionId.StartsWith("1.16"))
            return 8;
        if (versionId.StartsWith("1.17")) return 16;
        if (versionId.StartsWith("1.18") || versionId.StartsWith("1.19") || versionId.StartsWith("1.20") && versionId.Length <= 6)
            return 17;
        if (versionId.Contains("1.20.5") || versionId.Contains("1.20.6") || versionId.StartsWith("1.21"))
            return 21;
        return 17;
    }

    /// <summary>确保 Java 可用，返回 java 路径。</summary>
    public static async Task<string> EnsureJavaAsync(int requiredMajor, Settings settings,
        IProgress<PrepareProgress>? progress = null, CancellationToken ct = default)
    {
        if (!string.IsNullOrEmpty(settings.JavaPath) && File.Exists(settings.JavaPath))
            return settings.JavaPath;

        var installed = JavaManager.DetectInstalled();
        var best = JavaManager.FindBest(installed, requiredMajor);
        if (best != null) return best.JavaPath;

        progress?.Report(new PrepareProgress { Stage = "未检测到合适的 Java，正在下载 Temurin..." });
        var downloaded = await JavaManager.DownloadTemurinAsync(requiredMajor,
            new Progress<(long, string)>(t => progress?.Report(new PrepareProgress { Stage = t.Item2 })), ct);
        if (downloaded != null) return downloaded.JavaPath;
        throw new InvalidOperationException("无法获取 Java 运行时，请手动在设置中指定 Java 路径。");
    }

    /// <summary>安装/确认 Mod 加载器，返回最终版本 id。</summary>
    public static async Task<string> EnsureModLoaderAsync(string javaPath, Profile profile, string baseId,
        IProgress<PrepareProgress>? progress = null, CancellationToken ct = default)
    {
        string resolvedId = baseId;
        // 先确保基础版本 JSON 已缓存，供 inheritsFrom 解析
        await Versions.VersionResolver.GetVersionJsonAsync(baseId, ct);
        switch (profile.LoaderType)
        {
            case ModLoaderType.Vanilla:
            case ModLoaderType.RaincloudsCore:
                // 自研核心基于原版构建，由加载器 jar 在运行时接管主类
                break;
            case ModLoaderType.Fabric:
                resolvedId = $"fabric-loader-{profile.LoaderVersion}-" + baseId;
                if (!File.Exists(Paths.VersionJsonPath(resolvedId)))
                {
                    progress?.Report(new PrepareProgress { Stage = "安装 Fabric 加载器..." });
                    var r = await FabricInstaller.InstallAsync(javaPath, baseId, profile.LoaderVersion, ct);
                    if (!r.Success) throw new InvalidOperationException("Fabric 安装失败: " + r.Error);
                }
                break;
            case ModLoaderType.Forge:
                resolvedId = $"{baseId}-{profile.LoaderVersion}";
                if (!File.Exists(Paths.VersionJsonPath(resolvedId)))
                {
                    progress?.Report(new PrepareProgress { Stage = "安装 Forge 加载器..." });
                    var r = await ForgeInstaller.InstallAsync(javaPath, baseId, profile.LoaderVersion, ct);
                    if (!r.Success) throw new InvalidOperationException("Forge 安装失败: " + r.Error);
                }
                break;
            case ModLoaderType.Quilt:
                resolvedId = $"quilt-loader-{profile.LoaderVersion}-" + baseId;
                if (!File.Exists(Paths.VersionJsonPath(resolvedId)))
                {
                    progress?.Report(new PrepareProgress { Stage = "安装 Quilt 加载器..." });
                    var r = await QuiltInstaller.InstallAsync(javaPath, baseId, profile.LoaderVersion, ct);
                    if (!r.Success) throw new InvalidOperationException("Quilt 安装失败: " + r.Error);
                }
                break;
        }
        return resolvedId;
    }

    /// <summary>下载客户端、库与资产，解压 natives。返回解析后的版本。</summary>
    public static async Task<ResolvedVersion> DownloadAllAsync(string resolvedId, Settings settings,
        IProgress<PrepareProgress>? progress = null, CancellationToken ct = default)
    {
        var rv = await Versions.VersionResolver.ResolveAsync(resolvedId, ct);

        var items = new List<DownloadItem>();

        // 客户端主 jar
        if (rv.ClientDownload != null && !string.IsNullOrEmpty(rv.ClientDownload.Url))
        {
            var jar = Paths.VersionJarPath(resolvedId);
            if (!Utils.Hash.Sha1Matches(jar, rv.ClientDownload.Sha1))
                items.Add(new DownloadItem { Url = rv.ClientDownload.Url, Destination = jar, ExpectedSha1 = rv.ClientDownload.Sha1, Size = rv.ClientDownload.Size });
        }

        // 库
        items.AddRange(LibraryManager.CollectDownloads(rv));

        // 资产
        if (rv.AssetIndex != null)
        {
            progress?.Report(new PrepareProgress { Stage = "下载资产索引..." });
            var index = await AssetManager.EnsureIndexAsync(rv.AssetIndex, ct);
            items.AddRange(AssetManager.CollectObjectDownloads(index));
            // 暂存 index 供启动/虚拟资产使用
            rv.AssetIndex.Id ??= rv.AssetIndex.Id;
            AssetManager.MaterializeVirtual(index, rv.AssetIndex.Id!);
        }

        if (items.Count > 0)
        {
            progress?.Report(new PrepareProgress { Stage = $"下载 {items.Count} 个文件...", Total = items.Count });
            var failed = await Downloader.DownloadBatchAsync(items.ToArray(), settings.DownloadConcurrency,
                new Progress<(int, int, long)>(t => progress?.Report(new PrepareProgress { Stage = "下载资源中...", Done = t.Item1, Total = t.Item2, Bytes = t.Item3 })), ct);
            if (failed > 0)
                throw new InvalidOperationException($"有 {failed} 个文件下载失败");
        }

        progress?.Report(new PrepareProgress { Stage = "解压原生库..." });
        LibraryManager.ExtractNatives(rv, Paths.NativesDir(resolvedId));
        return rv;
    }

    /// <summary>完整准备（不含启动）。</summary>
    public static async Task<PreparedGame> PrepareAsync(Profile profile, Settings settings, Account? account,
        IProgress<PrepareProgress>? progress = null, CancellationToken ct = default)
    {
        Paths.EnsureDirectories();

        // 实例目录骨架（自建 gameDir 时也确保 mods 存在）
        var gameDir = profile.ResolvedGameDir;
        Directory.CreateDirectory(gameDir);
        Directory.CreateDirectory(Path.Combine(gameDir, "mods"));

        var baseId = await ResolveBaseVersionIdAsync(profile.GameVersion, ct);

        // 读取基础版本以获取所需 Java 版本
        var baseJson = await Versions.VersionResolver.GetVersionJsonAsync(baseId, ct);
        int required = baseJson.JavaVersion?.MajorVersion ?? DefaultJavaMajor(baseId);

        var javaPath = await EnsureJavaAsync(required, settings, progress, ct);
        var resolvedId = await EnsureModLoaderAsync(javaPath, profile, baseId, progress, ct);
        var rv = await DownloadAllAsync(resolvedId, settings, progress, ct);

        // 内置核心：首次启动自动补齐预置内容（幂等）
        if (profile.IsRaincloudsCore && !profile.CoreModsProvisioned)
        {
            if (profile.LoaderType == ModLoaderType.RaincloudsCore)
            {
                progress?.Report(new PrepareProgress { Stage = "解压 Rainclouds 自研核心..." });
                await RaincloudsCoreEmbed.EnsureCoreJarAsync(ct);
                await RaincloudsCoreEmbed.EnsureDemoModsAsync(profile, ct);
                progress?.Report(new PrepareProgress { Stage = "Rainclouds 自研核心已就绪（加载器 + 3 个演示 Mod）" });
                profile.CoreModsProvisioned = true;
            }
            else
            {
                progress?.Report(new PrepareProgress { Stage = "准备 Rainclouds 核心模组..." });
                var r = await Content.RaincloudsPack.EnsureModsAsync(profile,
                    new Progress<string>(s => progress?.Report(new PrepareProgress { Stage = s })), ct);
                progress?.Report(new PrepareProgress
                {
                    Stage = $"核心模组就绪：新增 {r.Added}，已有 {r.Skipped}" +
                            (r.Failed.Count > 0 ? $"，失败 {r.Failed.Count}" : "")
                });
                if (r.Failed.Count == 0) profile.CoreModsProvisioned = true;
            }
        }

        profile.ResolvedVersionId = resolvedId;
        profile.LastPlayed = DateTime.Now;

        return new PreparedGame
        {
            JavaPath = javaPath,
            GameDir = gameDir,
            Version = rv
        };
    }

    /// <summary>准备并启动游戏。</summary>
    public static async Task<System.Diagnostics.Process> PlayAsync(Profile profile, Settings settings, Account? account,
        IProgress<PrepareProgress>? progress = null, CancellationToken ct = default)
    {
        var prepared = await PrepareAsync(profile, settings, account, progress, ct);
        var (javaExe, args) = Launcher.BuildLaunchCommand(prepared.Version, profile, account, prepared.JavaPath, prepared.GameDir);
        progress?.Report(new PrepareProgress { Stage = "启动游戏中..." });
        return Launcher.Launch(javaExe, args, prepared.GameDir);
    }
}
