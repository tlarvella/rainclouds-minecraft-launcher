using System.Text.Json.Serialization;

namespace RCL.Core.Models;

/// <summary>Mod 加载器类型。</summary>
public enum ModLoaderType
{
    Vanilla,
    Fabric,
    Forge,
    Quilt,
    /// <summary>Rainclouds 自研核心（10yv 核心）：自带加载器与 Mod API。</summary>
    RaincloudsCore
}

/// <summary>
/// 一个游戏实例。每个实例拥有独立的 .minecraft 目录，
/// 因此模组、插件、配置、存档、资源包互不干扰，可以同时共存。
/// </summary>
public class Profile
{
    public string Id { get; set; } = System.Guid.NewGuid().ToString("N");

    /// <summary>实例显示名称。</summary>
    public string Name { get; set; } = "最新版本";

    /// <summary>基础游戏版本（如 1.20.1）。</summary>
    public string GameVersion { get; set; } = "latest_release";

    /// <summary>Mod 加载器类型。</summary>
    public ModLoaderType LoaderType { get; set; } = ModLoaderType.Vanilla;

    /// <summary>加载器版本（Fabric loader / Forge 版本 / Quilt 版本）。</summary>
    public string? LoaderVersion { get; set; }

    /// <summary>实际解析出的版本 JSON id（安装后确定）。</summary>
    public string? ResolvedVersionId { get; set; }

    /// <summary>该实例专用 Java 路径（留空用全局）。</summary>
    public string? JavaPath { get; set; }

    /// <summary>最小内存 MB。</summary>
    public int? MinMemoryMb { get; set; }

    /// <summary>最大内存 MB。</summary>
    public int? MaxMemoryMb { get; set; }

    /// <summary>窗口宽。</summary>
    public int? WindowWidth { get; set; }

    /// <summary>窗口高。</summary>
    public int? WindowHeight { get; set; }

    /// <summary>全屏。</summary>
    public bool? Fullscreen { get; set; }

    /// <summary>附加 JVM 参数。</summary>
    public string? ExtraJvmArgs { get; set; }

    /// <summary>附加游戏参数。</summary>
    public string? ExtraGameArgs { get; set; }

    /// <summary>离线模式用户名（无账号时使用）。</summary>
    public string? OfflineUsername { get; set; }

    /// <summary>上次游玩时间。</summary>
    public System.DateTime? LastPlayed { get; set; }

    /// <summary>
    /// 自定义游戏目录。留空则使用实例专属目录
    /// %APPDATA%\RaincloudsMC\instances\&lt;Id&gt;\minecraft。
    /// </summary>
    public string? GameDirectory { get; set; }

    /// <summary>实例图标标识（用于列表中的颜色）。</summary>
    public string Icon { get; set; } = "default";

    /// <summary>是否为内置的 Rainclouds 核心实例。</summary>
    public bool IsRaincloudsCore { get; set; }

    /// <summary>核心整合包预置模组是否已补齐。</summary>
    public bool CoreModsProvisioned { get; set; }

    /// <summary>备注。</summary>
    public string? Notes { get; set; }

    /// <summary>创建时间。</summary>
    public System.DateTime CreatedAt { get; set; } = System.DateTime.Now;

    /// <summary>数据版本：2 起使用独立实例目录，0/1 为旧版共享目录。</summary>
    public int Schema { get; set; } = 2;

    /// <summary>实际使用的 gameDir。</summary>
    [JsonIgnore]
    public string ResolvedGameDir => string.IsNullOrWhiteSpace(GameDirectory)
        ? Utils.Paths.InstanceGameDir(Id)
        : GameDirectory!;

    /// <summary>实际使用的 mods 目录。</summary>
    [JsonIgnore]
    public string ResolvedModsDir => System.IO.Path.Combine(ResolvedGameDir, "mods");

    /// <summary>加载器显示名。</summary>
    [JsonIgnore]
    public string LoaderLabel => LoaderType switch
    {
        ModLoaderType.Fabric => "Fabric",
        ModLoaderType.Forge => "Forge",
        ModLoaderType.Quilt => "Quilt",
        ModLoaderType.RaincloudsCore => "Rainclouds 核心",
        _ => "原版"
    };

    /// <summary>列表中的副标题。</summary>
    [JsonIgnore]
    public string Subtitle => $"{GameVersion} · {LoaderLabel}";

    /// <summary>列表图标上的首字母。</summary>
    [JsonIgnore]
    public string Initial => string.IsNullOrEmpty(Name) ? "?" : Name[..1].ToUpperInvariant();

    /// <summary>列表图标底色。</summary>
    [JsonIgnore]
    public string IconColor => IsRaincloudsCore ? "#1f6feb" : "#5b6472";

    /// <summary>内存显示文本。</summary>
    [JsonIgnore]
    public string MemoryText => $"{MinMemoryMb ?? 2048} / {MaxMemoryMb ?? 4096} MB";
}
