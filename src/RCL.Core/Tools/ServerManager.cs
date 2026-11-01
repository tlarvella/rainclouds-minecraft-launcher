using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RCL.Core.Models;
using RCL.Core.Utils;

namespace RCL.Core.Tools;

/// <summary>
/// 一键开服：解析服务端下载地址、生成 eula 与 server.properties、启停服务端子进程。
/// 支持 Vanilla / Paper / Fabric（Fabric 通过官方 installer 生成服务端）。
/// </summary>
public static class ServerManager
{
    public static string ServerDir => Path.Combine(Paths.Root, "server");
    public static string ServerJarPath => Path.Combine(ServerDir, "server.jar");
    public static string PluginsDir => Path.Combine(ServerDir, "plugins");

    /// <summary>取得服务端 jar 的下载地址与文件名（不下载，仅解析）。</summary>
    public static async Task<(string Url, string Filename)> ResolveServerJarAsync(ServerConfig cfg, CancellationToken ct)
    {
        switch ((cfg.Type ?? "vanilla").ToLowerInvariant())
        {
            case "paper":
                return await ResolvePaperAsync(cfg.Version, ct);
            case "fabric":
                return ("https://maven.fabricmc.net/net/fabricmc/fabric-installer/"
                        + "1.0.1/fabric-installer-1.0.1.jar", "fabric-installer.jar");
            case "vanilla":
            default:
                return await ResolveVanillaAsync(cfg.Version, ct);
        }
    }

    private static async Task<(string, string)> ResolveVanillaAsync(string version, CancellationToken ct)
    {
        var json = await new HttpClient { Timeout = TimeSpan.FromSeconds(60) }
            .GetStringAsync("https://launchermeta.mojang.com/mc/game/version_manifest_v2.json", ct);
        var doc = JsonDocument.Parse(json);
        string? url = null;
        foreach (var v in doc.RootElement.GetProperty("versions").EnumerateArray())
        {
            if (v.GetProperty("id").GetString() == version)
            {
                url = v.GetProperty("url").GetString();
                break;
            }
        }
        if (url == null) throw new InvalidOperationException("找不到原版版本 " + version);
        var vjson = await new HttpClient { Timeout = TimeSpan.FromSeconds(60) }
            .GetStringAsync(url, ct);
        var vdoc = JsonDocument.Parse(vjson);
        var serverUrl = vdoc.RootElement.GetProperty("downloads").GetProperty("server")
            .GetProperty("url").GetString()
            ?? throw new InvalidOperationException("该版本不含服务端下载");
        return (serverUrl, "server.jar");
    }

    private static async Task<(string, string)> ResolvePaperAsync(string version, CancellationToken ct)
    {
        var baseUrl = "https://api.papermc.io/v2/projects/paper/versions/" + version + "/builds";
        var json = await new HttpClient { Timeout = TimeSpan.FromSeconds(60) }.GetStringAsync(baseUrl, ct);
        var doc = JsonDocument.Parse(json);
        var builds = doc.RootElement.GetProperty("builds").EnumerateArray();
        int latest = -1;
        foreach (var b in builds) latest = Math.Max(latest, b.GetProperty("build").GetInt32());
        if (latest < 0) throw new InvalidOperationException("Paper 没有 " + version + " 的构建");
        var dl = baseUrl + "/" + latest + "/downloads/paper-" + version + "-" + latest + ".jar";
        return (dl, "server.jar");
    }

    /// <summary>下载服务端 jar 到 ServerDir（幂等）。</summary>
    public static async Task DownloadServerJarAsync(string url, string filename, CancellationToken ct)
    {
        Directory.CreateDirectory(ServerDir);
        var dest = Path.Combine(ServerDir, filename);
        if (File.Exists(dest)) return;
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(600) };
        var bytes = await client.GetByteArrayAsync(url, ct);
        await File.WriteAllBytesAsync(dest, bytes, ct);
    }

    public static void GenerateEula()
    {
        Directory.CreateDirectory(ServerDir);
        File.WriteAllText(Path.Combine(ServerDir, "eula.txt"),
            "# 由 Rainclouds 启动器生成\n" +
            "# https://account.mojang.com/documents/minecraft_eula\n" +
            "eula=true\n");
    }

    public static void GenerateServerProperties(ServerConfig cfg)
    {
        Directory.CreateDirectory(ServerDir);
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("# 由 Rainclouds 启动器生成");
        sb.AppendLine("server-port=" + cfg.Port);
        sb.AppendLine("motd=" + (cfg.Motd ?? "A Rainclouds Server"));
        sb.AppendLine("online-mode=" + (cfg.OnlineMode ? "true" : "false"));
        sb.AppendLine("level-name=world");
        sb.AppendLine("max-players=20");
        sb.AppendLine("enable-command-block=true");
        sb.AppendLine("view-distance=10");
        sb.AppendLine("spawn-protection=0");
        File.WriteAllText(Path.Combine(ServerDir, "server.properties"), sb.ToString());
    }

    /// <summary>启动服务端子进程。对 Fabric 用 installer 生成真正的服务端后再启动。</summary>
    public static async Task<System.Diagnostics.Process> StartAsync(ServerConfig cfg, string javaPath, CancellationToken ct = default)
    {
        Directory.CreateDirectory(ServerDir);
        Directory.CreateDirectory(PluginsDir);
        GenerateEula();
        GenerateServerProperties(cfg);

        string jarToRun;
        var type = (cfg.Type ?? "vanilla").ToLowerInvariant();
        if (type == "fabric")
        {
            // Fabric：先通过 installer 生成服务端（首次），再启动生成的 fabric-server-launch.jar
            var installer = Path.Combine(ServerDir, "fabric-installer.jar");
            if (!File.Exists(installer))
                throw new InvalidOperationException("Fabric installer 尚未下载，请先点击“下载服务端”。");
            var launchJar = Path.Combine(ServerDir, "fabric-server-launch.jar");
            if (!File.Exists(launchJar))
            {
                var psi = new System.Diagnostics.ProcessStartInfo(javaPath,
                    "-jar fabric-installer.jar server -downloadMinecraft -mcversion " + cfg.Version)
                {
                    WorkingDirectory = ServerDir,
                    UseShellExecute = false,
                    CreateNoWindow = false
                };
                using var p = System.Diagnostics.Process.Start(psi)!;
                await p.WaitForExitAsync(ct);
                if (!File.Exists(launchJar))
                    throw new InvalidOperationException("Fabric 服务端生成失败。");
            }
            jarToRun = "fabric-server-launch.jar";
        }
        else
        {
            jarToRun = "server.jar";
        }

        var psi2 = new System.Diagnostics.ProcessStartInfo(javaPath,
            $"-Xms{cfg.MemoryMb}M -Xmx{cfg.MemoryMb}M -jar \"{jarToRun}\" nogui")
        {
            WorkingDirectory = ServerDir,
            UseShellExecute = false,
            CreateNoWindow = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        return System.Diagnostics.Process.Start(psi2)!;
    }

    /// <summary>从一行日志判断服务端启动状态。</summary>
    public static ServerLineKind ClassifyLine(string line)
    {
        if (line.Contains("Done", StringComparison.OrdinalIgnoreCase)
            && line.Contains("For help", StringComparison.OrdinalIgnoreCase))
            return ServerLineKind.Ready;
        if (line.Contains("[ERROR]", StringComparison.OrdinalIgnoreCase)
            || line.Contains("Exception", StringComparison.OrdinalIgnoreCase)
            || line.Contains("Server failed", StringComparison.OrdinalIgnoreCase))
            return ServerLineKind.Error;
        return ServerLineKind.Info;
    }
}

public enum ServerLineKind
{
    Info,
    Ready,
    Error
}
