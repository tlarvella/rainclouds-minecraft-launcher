using System;
using System.IO;

namespace RCL.Core.Utils;

/// <summary>
/// 集中管理所有本地路径，默认为 %APPDATA%/RaincloudsMC。
/// 全部可配置，便于测试与自定义游戏根目录。
/// </summary>
public static class Paths
{
    private static string? _root;

    /// <summary>启动器/游戏根目录（默认 AppData/RaincloudsMC）。</summary>
    public static string Root
    {
        get
        {
            if (_root is null)
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                if (string.IsNullOrEmpty(appData))
                    appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".raincloudsmc");
                _root = Path.Combine(appData, "RaincloudsMC");
            }
            return _root;
        }
        set => _root = value;
    }

    public static string VersionsDir => Path.Combine(Root, "versions");
    public static string LibrariesDir => Path.Combine(Root, "libraries");
    public static string AssetsDir => Path.Combine(Root, "assets");
    public static string AssetsIndexesDir => Path.Combine(AssetsDir, "indexes");
    public static string AssetsObjectsDir => Path.Combine(AssetsDir, "objects");
    public static string AssetsVirtualDir => Path.Combine(AssetsDir, "virtual");
    public static string NativesDir(string versionId) => Path.Combine(VersionsDir, versionId, "natives");
    public static string VersionDir(string versionId) => Path.Combine(VersionsDir, versionId);
    public static string VersionJsonPath(string versionId) => Path.Combine(VersionsDir, versionId, versionId + ".json");
    public static string VersionJarPath(string versionId) => Path.Combine(VersionsDir, versionId, versionId + ".jar");
    public static string LogsDir => Path.Combine(Root, "logs");
    public static string JavaDir => Path.Combine(Root, "runtime", "java");
    public static string AccountsFile => Path.Combine(Root, "accounts.json");
    public static string ProfilesFile => Path.Combine(Root, "rcl.json");
    public static string SettingsFile => Path.Combine(Root, "settings.json");
    public static string LicenseFile => Path.Combine(Root, "license.json");

    /// <summary>进阶模块（联机/内网穿透/一键开服）授权文件。</summary>
    public static string ModulesLicenseFile => Path.Combine(Root, "modules-license.json");

    /// <summary>Rainclouds 自研核心加载器 jar 的解压目录与文件名。</summary>
    public static string RaincloudsCoreDir => Path.Combine(Root, "rainclouds-core");
    public static string RaincloudsCoreJar => Path.Combine(RaincloudsCoreDir, "rainclouds-core.jar");

    // ---------- 实例（每个实例独立的游戏目录，实现模组/插件互不干扰） ----------

    /// <summary>所有实例的存放目录。</summary>
    public static string InstancesDir => Path.Combine(Root, "instances");

    public static string InstanceDir(string id) => Path.Combine(InstancesDir, id);

    /// <summary>实例的 .minecraft 目录（gameDir）。</summary>
    public static string InstanceGameDir(string id) => Path.Combine(InstanceDir(id), "minecraft");

    public static string InstanceModsDir(string id) => Path.Combine(InstanceGameDir(id), "mods");
    public static string InstanceConfigDir(string id) => Path.Combine(InstanceGameDir(id), "config");
    public static string InstanceSavesDir(string id) => Path.Combine(InstanceGameDir(id), "saves");
    public static string InstanceResourcePacksDir(string id) => Path.Combine(InstanceGameDir(id), "resourcepacks");
    public static string InstanceShaderPacksDir(string id) => Path.Combine(InstanceGameDir(id), "shaderpacks");
    public static string InstanceManifestPath(string id) => Path.Combine(InstanceDir(id), "rainclouds-mods.json");

    public static void EnsureDirectories()
    {
        foreach (var d in new[]
        {
            Root, VersionsDir, LibrariesDir, AssetsDir, AssetsIndexesDir,
            AssetsObjectsDir, AssetsVirtualDir, LogsDir, JavaDir, InstancesDir
        })
            Directory.CreateDirectory(d);
    }

    /// <summary>创建一个实例所需的完整目录骨架。</summary>
    public static string EnsureInstanceDirectories(string id)
    {
        var game = InstanceGameDir(id);
        foreach (var d in new[]
        {
            game,
            InstanceModsDir(id),
            InstanceConfigDir(id),
            InstanceSavesDir(id),
            InstanceResourcePacksDir(id),
            InstanceShaderPacksDir(id),
            Path.Combine(game, "logs"),
            Path.Combine(game, "crash-reports")
        })
            Directory.CreateDirectory(d);
        return game;
    }
}
