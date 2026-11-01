using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using RCL.Core.Models;
using RCL.Core.Utils;

namespace RCL.Core.Tools;

/// <summary>
/// 内网穿透（frp）管理：自动获取 frpc 客户端、生成 frpc.toml、启停子进程并解析状态。
/// 注意：frp 需要一台有公网 IP 的服务器运行 frps（由用户自备），启动器只负责客户端。
/// </summary>
public static class FrpcManager
{
    // 固定版本，避免每次变；如需更新改这里
    private const string FrpVersion = "0.51.3";
    private const string DownloadBase =
        "https://github.com/fatedier/frp/releases/download/v" + FrpVersion + "/";
    private const string ZipName = "frpc_" + FrpVersion + "_windows_amd64.zip";

    public static string LocalFrpcPath => Path.Combine(Paths.Root, "tools", "frpc.exe");

    /// <summary>
    /// 确保 frpc.exe 可用：优先使用用户指定路径，否则尝试自动下载到本地 tools 目录。
    /// 返回可用路径；若均不可用则抛出异常并附带说明。
    /// </summary>
    public static async Task<string> EnsureFrpcAsync(string? userPath, CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(userPath) && File.Exists(userPath))
            return userPath;

        if (File.Exists(LocalFrpcPath))
            return LocalFrpcPath;

        var zip = Path.Combine(Paths.Root, "tools", ZipName);
        Directory.CreateDirectory(Path.GetDirectoryName(LocalFrpcPath)!);
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
            var bytes = await client.GetByteArrayAsync(DownloadBase + ZipName, ct);
            await File.WriteAllBytesAsync(zip, bytes, ct);
            System.IO.Compression.ZipFile.ExtractToDirectory(zip, Path.GetDirectoryName(LocalFrpcPath)!, true);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "无法自动下载 frpc。请手动从 https://github.com/fatedier/frp/releases 下载 frpc_" + FrpVersion
                + "_windows_amd64.zip，解压后把 frpc.exe 路径填到设置→内网穿透。错误：" + ex.Message);
        }
        finally
        {
            try { if (File.Exists(zip)) File.Delete(zip); } catch { }
        }

        if (!File.Exists(LocalFrpcPath))
            throw new InvalidOperationException("frpc.exe 解压失败，请检查 tools 目录权限。");
        return LocalFrpcPath;
    }

    /// <summary>根据配置生成 frpc.toml。</summary>
    public static string BuildConfig(FrpcConfig cfg, string outputPath)
    {
        var text = new System.Text.StringBuilder();
        text.AppendLine("serverAddr = \"" + (cfg.ServerAddr ?? "") + "\"");
        text.AppendLine("serverPort = " + cfg.ServerPort);
        if (!string.IsNullOrWhiteSpace(cfg.Token))
            text.AppendLine("auth.token = \"" + cfg.Token + "\"");
        text.AppendLine();
        text.AppendLine("[[proxies]]");
        text.AppendLine("name = \"rainclouds-mc\"");
        text.AppendLine("type = \"" + (cfg.Type ?? "tcp") + "\"");
        text.AppendLine("localIP = \"127.0.0.1\"");
        text.AppendLine("localPort = " + cfg.LocalPort);
        text.AppendLine("remotePort = " + cfg.RemotePort);
        File.WriteAllText(outputPath, text.ToString());
        return outputPath;
    }

    /// <summary>启动 frpc 子进程（托管模式，日志由调用方读取）。</summary>
    public static System.Diagnostics.Process Start(string frpcExe, string configPath, string workingDir)
    {
        Directory.CreateDirectory(workingDir);
        var psi = new System.Diagnostics.ProcessStartInfo(frpcExe, "-c \"" + configPath + "\"")
        {
            UseShellExecute = false,
            WorkingDirectory = workingDir,
            CreateNoWindow = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        return System.Diagnostics.Process.Start(psi)!;
    }

    /// <summary>从一行日志判断穿透状态。</summary>
    public static FrpcLineKind ClassifyLine(string line)
    {
        if (line.Contains("login to server success", StringComparison.OrdinalIgnoreCase)
            || line.Contains("login success", StringComparison.OrdinalIgnoreCase))
            return FrpcLineKind.LoginOk;
        if (line.Contains("start proxy success", StringComparison.OrdinalIgnoreCase)
            || line.Contains("proxy success", StringComparison.OrdinalIgnoreCase))
            return FrpcLineKind.ProxyOk;
        if (line.Contains("[E]", StringComparison.OrdinalIgnoreCase)
            || line.Contains("error", StringComparison.OrdinalIgnoreCase)
            || line.Contains("dial tcp", StringComparison.OrdinalIgnoreCase))
            return FrpcLineKind.Error;
        return FrpcLineKind.Info;
    }
}

public enum FrpcLineKind
{
    Info,
    LoginOk,
    ProxyOk,
    Error
}
