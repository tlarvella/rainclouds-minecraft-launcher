using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RCL.Core.Versions;

/// <summary>单个下载构件（jar / native）。</summary>
public class Artifact
{
    public string? Path { get; set; }
    public string? Url { get; set; }
    public string? Sha1 { get; set; }
    public long Size { get; set; }
}

/// <summary>依赖库（Maven 坐标 + 下载信息 + 平台规则）。</summary>
public class Library
{
    /// <summary>Maven 坐标，如 net.fabricmc:fabric-loader:0.14.21</summary>
    public string? Name { get; set; }

    /// <summary>官方下载构件（主 jar）。</summary>
    public Artifact? Artifact { get; set; }

    /// <summary>分类构件，通常是 natives（按平台）。</summary>
    public Dictionary<string, Artifact>? Classifiers { get; set; }

    /// <summary>natives 映射：操作系统 -> classifier 后缀。</summary>
    public Dictionary<string, string>? Natives { get; set; }

    /// <summary>提取规则（解压 native 时排除项）。</summary>
    public Extract? Extract { get; set; }

    /// <summary>平台/特性规则。</summary>
    public List<Rule>? Rules { get; set; }

    /// <summary>原生库 URL（非官方源，部分库使用）。</summary>
    public Dictionary<string, string>? NativeUrlFallback { get; set; }
}

public class Extract
{
    public List<string>? Exclude { get; set; }
}

public class Rule
{
    public string? Action { get; set; }
    public OsRule? Os { get; set; }
    public Dictionary<string, bool>? Features { get; set; }
}

public class OsRule
{
    public string? Name { get; set; }
    public string? Arch { get; set; }
    public string? Version { get; set; }
}

/// <summary>资产索引引用。</summary>
public class AssetIndexRef
{
    public string? Id { get; set; }
    public string? Sha1 { get; set; }
    public long Size { get; set; }
    public long TotalSize { get; set; }
    public string? Url { get; set; }
}

/// <summary>客户端/服务端下载。</summary>
public class Downloads
{
    public Artifact? Client { get; set; }
    public Artifact? Server { get; set; }
}

public class JavaVersion
{
    public string? Component { get; set; }
    public int MajorVersion { get; set; }
}

/// <summary>日志配置（1.12 以后）。</summary>
public class LoggingConfig
{
    public LoggingClient? Client { get; set; }
}

public class LoggingClient
{
    public Artifact? File { get; set; }
    public string? Argument { get; set; }
}

/// <summary>版本 JSON（与 Mojang 官方版本文件对应）。</summary>
public class VersionJson
{
    public string? Id { get; set; }
    public string? Type { get; set; }
    public string? InheritsFrom { get; set; }
    public string? MainClass { get; set; }
    public string? MinecraftArguments { get; set; }
    public Arguments? Arguments { get; set; }
    public List<Library>? Libraries { get; set; }
    public AssetIndexRef? AssetIndex { get; set; }
    public Downloads? Downloads { get; set; }
    public JavaVersion? JavaVersion { get; set; }
    public int MinimumLauncherVersion { get; set; }
    public LoggingConfig? Logging { get; set; }
    public int? ComplianceLevel { get; set; }
}

public class Arguments
{
    /// <summary>游戏参数（字符串或带规则的参数对象）。</summary>
    public List<JsonElement>? Game { get; set; }

    /// <summary>JVM 参数（字符串或带规则的参数对象）。</summary>
    public List<JsonElement>? Jvm { get; set; }
}
