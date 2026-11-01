using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RCL.Core.Download;
using RCL.Core.Models;
using RCL.Core.Utils;

namespace RCL.Core.Content;

/// <summary>预置模组定义。</summary>
public class PackMod
{
    public string Slug { get; init; } = "";
    public string Name { get; init; } = "";
    public string Note { get; init; } = "";

    public PackMod(string slug, string name, string note = "")
    {
        Slug = slug; Name = name; Note = note;
    }
}

public class ProvisionResult
{
    public int Added { get; set; }
    public int Skipped { get; set; }
    public List<string> Failed { get; set; } = new();
    public string ModsDir { get; set; } = "";
    public int Total => Added + Skipped + Failed.Count;
}

internal class PackManifest
{
    public string GameVersion { get; set; } = "";
    public string Loader { get; set; } = "fabric";
    public Dictionary<string, string> Files { get; set; } = new();
}

/// <summary>
/// 内置的 Rainclouds 核心：预置游戏版本、加载器与一套优化/体验模组，
/// 首次创建或启动时自动从 Modrinth 拉取，组装成一个开箱即用的实例。
/// 注意：不内嵌、不分发 Minecraft 本体，客户端仍从官方源下载。
/// </summary>
public static class RaincloudsPack
{
    public const string DefaultGameVersion = "1.20.1";
    public const string DefaultLoader = "fabric";

    /// <summary>核心预置模组（全部为 1.20.1 Fabric 客户端可用、许可证允许分发的开源模组）。</summary>
    public static readonly PackMod[] Mods =
    {
        new("sodium", "Sodium", "渲染优化，大幅提升帧率"),
        new("lithium", "Lithium", "服务端/客户端逻辑优化"),
        new("iris", "Iris Shaders", "光影支持"),
        new("indium", "Indium", "Sodium 与 Fabric 渲染 API 兼容"),
        new("ferrite-core", "FerriteCore", "内存占用优化"),
        new("krypton", "Krypton", "网络栈优化"),
        new("lazydfu", "LazyDFU", "加快启动"),
        new("memoryleakfix", "Memory Leak Fix", "修复内存泄漏"),
        new("entityculling", "Entity Culling", "实体剔除优化"),
        new("immediatelyfast", "ImmediatelyFast", "即时渲染优化"),
        new("modernfix", "ModernFix", "启动与内存优化"),
        new("dynamic-fps", "Dynamic FPS", "后台自动降帧"),
        new("c2me-fabric", "C2ME", "多线程区块加载"),
        new("badoptimizations", "BadOptimizations", "细粒度性能优化"),
        new("noisium", "Noisium", "世界生成优化"),
        new("modmenu", "Mod Menu", "模组列表菜单"),
        new("cloth-config", "Cloth Config API", "模组配置库"),
        new("rei", "Roughly Enough Items", "物品配方查询"),
        new("appleskin", "AppleSkin", "饥饿值与饱和度显示"),
        new("jade", "Jade", "方块/实体信息提示"),
        new("xaeros-minimap", "Xaero's Minimap", "小地图"),
        new("continuity", "Continuity", "连接纹理"),
        new("lambdabettergrass", "LambdaBetterGrass", "更自然的草地")
    };

    /// <summary>
    /// 创建一个 Rainclouds 自研核心实例（10yv 核心）：基于原版 1.20.1，
    /// 由内置 Rainclouds 加载器接管，并预置 3 个演示 Mod，开箱即用。
    /// </summary>
    public static Profile CreateProfile(string? name = null)
    {
        var p = new Profile
        {
            Name = string.IsNullOrWhiteSpace(name) ? $"Rainclouds 核心 {DefaultGameVersion}" : name!,
            GameVersion = DefaultGameVersion,
            LoaderType = ModLoaderType.RaincloudsCore,
            LoaderVersion = "10yv",
            IsRaincloudsCore = true,
            Icon = "rainclouds",
            MinMemoryMb = 2048,
            MaxMemoryMb = 4096,
            WindowWidth = 854,
            WindowHeight = 480,
            Notes = "内置 Rainclouds 自研核心（10yv）：原版 1.20.1 + 自研加载器 + 演示 Mod，开箱即用。"
        };
        Paths.EnsureInstanceDirectories(p.Id);
        return p;
    }

    /// <summary>
    /// 创建一个基于 Fabric 的优化核心实例（23 个开源优化/体验 Mod，按需从 Modrinth 下载）。
    /// 作为自研核心之外的“高性能整合包”备选。
    /// </summary>
    public static Profile CreateFabricProfile(string? name = null)
    {
        var p = new Profile
        {
            Name = string.IsNullOrWhiteSpace(name) ? $"Rainclouds 优化核心 {DefaultGameVersion}" : name!,
            GameVersion = DefaultGameVersion,
            LoaderType = ModLoaderType.Fabric,
            LoaderVersion = null,
            IsRaincloudsCore = true,
            Icon = "rainclouds",
            MinMemoryMb = 2048,
            MaxMemoryMb = 4096,
            WindowWidth = 854,
            WindowHeight = 480,
            Notes = "内置优化核心：1.20.1 + Fabric + 23 个开源优化/体验 Mod。"
        };
        Paths.EnsureInstanceDirectories(p.Id);
        return p;
    }

    /// <summary>
    /// 补齐核心模组到实例 mods 目录。幂等：已下载的文件会跳过。
    /// </summary>
    public static async Task<ProvisionResult> EnsureModsAsync(Profile profile,
        IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var dir = profile.ResolvedModsDir;
        Directory.CreateDirectory(dir);

        var result = new ProvisionResult { ModsDir = dir };
        var manifest = LoadManifest(profile);
        manifest.GameVersion = profile.GameVersion;
        var loader = profile.LoaderType switch
        {
            ModLoaderType.Forge => "forge",
            ModLoaderType.Quilt => "quilt",
            ModLoaderType.Fabric => "fabric",
            _ => "fabric"
        };
        manifest.Loader = loader;

        foreach (var mod in Mods)
        {
            ct.ThrowIfCancellationRequested();

            if (manifest.Files.TryGetValue(mod.Slug, out var existing)
                && File.Exists(Path.Combine(dir, existing)))
            {
                result.Skipped++;
                continue;
            }

            progress?.Report($"获取 {mod.Name} ...");
            try
            {
                var versions = await ModrinthClient.GetVersionsAsync(mod.Slug, profile.GameVersion, loader, ct);
                var file = versions.Count > 0 ? versions[0].PrimaryFile : null;
                if (file == null)
                {
                    result.Failed.Add(mod.Name + "（无兼容版本）");
                    continue;
                }

                var dest = Path.Combine(dir, file.Filename);
                if (File.Exists(dest))
                {
                    manifest.Files[mod.Slug] = file.Filename;
                    result.Skipped++;
                    continue;
                }

                progress?.Report($"下载 {mod.Name} ...");
                var ok = await Downloader.DownloadFileAsync(file.Url, dest, file.Sha1, null, ct);
                if (ok)
                {
                    manifest.Files[mod.Slug] = file.Filename;
                    result.Added++;
                }
                else
                {
                    result.Failed.Add(mod.Name + "（下载失败）");
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                result.Failed.Add($"{mod.Name}（{ex.Message}）");
            }
        }

        SaveManifest(profile, manifest);
        if (result.Failed.Count == 0) profile.CoreModsProvisioned = true;
        return result;
    }

    private static PackManifest LoadManifest(Profile profile)
    {
        try
        {
            var path = Paths.InstanceManifestPath(profile.Id);
            if (File.Exists(path))
                return Json.Deserialize<PackManifest>(File.ReadAllText(path)) ?? new PackManifest();
        }
        catch { }
        return new PackManifest();
    }

    private static void SaveManifest(Profile profile, PackManifest manifest)
    {
        try
        {
            Directory.CreateDirectory(Paths.InstanceDir(profile.Id));
            File.WriteAllText(Paths.InstanceManifestPath(profile.Id), Json.Serialize(manifest));
        }
        catch { }
    }
}
