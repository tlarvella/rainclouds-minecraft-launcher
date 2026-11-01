using System.Collections.Generic;

namespace RCL.Core.Models;

/// <summary>全局设置，持久化到 settings.json。</summary>
public class Settings
{
    /// <summary>游戏根目录（覆盖默认 AppData 路径）。</summary>
    public string? GameRoot { get; set; }

    /// <summary>微软 Azure AD 应用 ClientId（设备码登录需要）。</summary>
    public string? AzureClientId { get; set; }

    /// <summary>默认分配内存（MB）。</summary>
    public int MemoryMb { get; set; } = 2048;

    /// <summary>默认最大内存（MB）。</summary>
    public int MaxMemoryMb { get; set; } = 4096;

    /// <summary>窗口宽度，0 表示默认（全屏则由 Fullscreen 决定）。</summary>
    public int WindowWidth { get; set; } = 854;

    /// <summary>窗口高度。</summary>
    public int WindowHeight { get; set; } = 480;

    /// <summary>全屏启动。</summary>
    public bool Fullscreen { get; set; }

    /// <summary>附加 JVM 参数。</summary>
    public string ExtraJvmArgs { get; set; } = "";

    /// <summary>附加游戏参数。</summary>
    public string ExtraGameArgs { get; set; } = "";

    /// <summary>默认 Java 路径（留空则自动检测/下载）。</summary>
    public string? JavaPath { get; set; }

    /// <summary>是否在启动前自动下载缺失资源。</summary>
    public bool AutoDownload { get; set; } = true;

    /// <summary>并发下载数。</summary>
    public int DownloadConcurrency { get; set; } = 8;

    /// <summary>上次使用的账号 Id。</summary>
    public string? CurrentAccountId { get; set; }

    /// <summary>上次选择的实例 Id。</summary>
    public string? LastProfileId { get; set; }

    /// <summary>是否已提示过激活。</summary>
    public bool ActivationPrompted { get; set; }

    /// <summary>是否已阅读过核心整合包说明。</summary>
    public bool CoreIntroShown { get; set; }

    // ---------- 进阶模块配置（需 ¥20 解锁） ----------

    /// <summary>内网穿透（frp）配置。</summary>
    public FrpcConfig Frpc { get; set; } = new();

    /// <summary>一键开服配置。</summary>
    public ServerConfig Server { get; set; } = new();
}

/// <summary>内网穿透配置：把本地服务映射为 frp 服务器上的公网地址。</summary>
public class FrpcConfig
{
    /// <summary>frp 服务器地址（需自备公网服务器并部署 frps）。</summary>
    public string ServerAddr { get; set; } = "";

    /// <summary>frp 服务器端口（默认 7000）。</summary>
    public int ServerPort { get; set; } = 7000;

    /// <summary>frp 令牌（与 frps 一致）。</summary>
    public string Token { get; set; } = "";

    /// <summary>本地要穿透的服务端口（如本地 MC 服务端 25565）。</summary>
    public int LocalPort { get; set; } = 25565;

    /// <summary>frp 分配的远程端口（公网访问端口）。</summary>
    public int RemotePort { get; set; } = 25565;

    /// <summary>穿透类型（tcp / udp）。</summary>
    public string Type { get; set; } = "tcp";

    /// <summary>本地 frpc 可执行文件路径（留空则启动器自动下载）。</summary>
    public string FrpcPath { get; set; } = "";
}

/// <summary>一键开服配置。</summary>
public class ServerConfig
{
    /// <summary>服务端类型。</summary>
    public string Type { get; set; } = "vanilla";

    /// <summary>游戏版本。</summary>
    public string Version { get; set; } = "1.20.1";

    /// <summary>服务端口。</summary>
    public int Port { get; set; } = 25565;

    /// <summary>最大内存 MB。</summary>
    public int MemoryMb { get; set; } = 2048;

    /// <summary>服务器 MOTD。</summary>
    public string Motd { get; set; } = "A Rainclouds Server";

    /// <summary>是否开启正版验证。</summary>
    public bool OnlineMode { get; set; } = true;

    /// <summary>用于运行服务端的 Java 路径（留空则自动检测/下载）。</summary>
    public string JavaPath { get; set; } = "";
}
