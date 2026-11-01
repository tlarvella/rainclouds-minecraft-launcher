using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RCL.Core.Utils;

namespace RCL.Core.Tools;

/// <summary>一个被收藏的服务器地址。</summary>
public class ServerFavorite
{
    public string Name { get; set; } = "";
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 25565;
    public string Notes { get; set; } = "";
}

/// <summary>联机模块：服务器收藏的持久化与局域网扫描。</summary>
public static class NetworkStore
{
    private static readonly string ServersFile = Path.Combine(Paths.Root, "servers.json");

    public static List<ServerFavorite> Load()
    {
        try
        {
            if (File.Exists(ServersFile))
                return Json.Deserialize<List<ServerFavorite>>(File.ReadAllText(ServersFile)) ?? new();
        }
        catch { }
        return new();
    }

    public static void Save(List<ServerFavorite> list)
    {
        try
        {
            Directory.CreateDirectory(Paths.Root);
            File.WriteAllText(ServersFile, Json.Serialize(list));
        }
        catch { }
    }

    /// <summary>
    /// 局域网扫描：对本地 IP 所在 /24 网段的 25565 端口做短超时 TCP 探测，
    /// 返回可达的地址。这是尽力而为的轻量实现，不依赖特定的 MC 协议。
    /// </summary>
    public static async Task<List<string>> ScanLanAsync(CancellationToken ct = default)
    {
        var found = new List<string>();
        string? local = GetLocalIp();
        if (local == null) return found;

        var parts = local.Split('.');
        if (parts.Length != 4) return found;
        var prefix = parts[0] + "." + parts[1] + "." + parts[2] + ".";

        var tasks = new List<Task>();
        for (int i = 1; i <= 254; i++)
        {
            var ip = prefix + i;
            tasks.Add(TryConnectAsync(ip, 25565, found, ct));
            if (tasks.Count >= 64) // 控制并发
            {
                await Task.WhenAny(tasks.ToArray());
                tasks.RemoveAll(t => t.IsCompleted);
            }
        }
        try { await Task.WhenAll(tasks.ToArray()); } catch { }
        return found.OrderBy(x => x, StringComparer.Ordinal).ToList();
    }

    private static async Task TryConnectAsync(string ip, int port, List<string> found, CancellationToken ct)
    {
        try
        {
            using var client = new TcpClient();
            var connect = client.ConnectAsync(ip, port);
            var completed = await Task.WhenAny(connect, Task.Delay(350, ct));
            if (completed != connect) return;
            if (client.Connected)
            {
                lock (found) found.Add(ip);
            }
        }
        catch { }
    }

    private static string? GetLocalIp()
    {
        try
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork && !ip.ToString().StartsWith("127."))
                    return ip.ToString();
            }
        }
        catch { }
        return null;
    }

    /// <summary>生成一段可直接复制的“加入信息”文本。</summary>
    public static string JoinInfoText(ServerFavorite s)
        => $"{s.Name}\n地址: {s.Host}:{s.Port}";
}
