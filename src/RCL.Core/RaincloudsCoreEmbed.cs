using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using RCL.Core.Models;
using RCL.Core.Utils;

namespace RCL.Core;

/// <summary>
/// 负责把内嵌在程序集中的自研核心 jar（加载器 + 演示 Mod）抽取到本地磁盘，
/// 供启动器在启动游戏时加入 classpath / mods 目录。
/// 抽取是幂等的：仅当目标文件缺失或体积不符时才写盘。
/// </summary>
public static class RaincloudsCoreEmbed
{
    // 资源清单名（csproj 以 EmbeddedResource 嵌入 Resources\ 下的 jar）
    private const string CoreJarResource = "RCL.Core.Resources.rainclouds-core.jar";
    private static readonly string[] DemoModResources =
    {
        "RCL.Core.Resources.rainclouds-hello.jar",
        "RCL.Core.Resources.rainclouds-fps.jar",
        "RCL.Core.Resources.rainclouds-spawn.jar"
    };

    private static readonly string[] DemoModFilenames =
    {
        "rainclouds-hello.jar",
        "rainclouds-fps.jar",
        "rainclouds-spawn.jar"
    };

    /// <summary>确保自研核心加载器 jar 已抽取到 RaincloudsCoreJar。</summary>
    public static Task EnsureCoreJarAsync(CancellationToken ct = default)
        => ExtractIfNeededAsync(CoreJarResource, Paths.RaincloudsCoreJar, ct);

    /// <summary>确保 3 个演示 Mod 已抽取到实例的 mods 目录。</summary>
    public static async Task EnsureDemoModsAsync(Profile profile, CancellationToken ct = default)
    {
        var dir = profile.ResolvedModsDir;
        Directory.CreateDirectory(dir);
        for (int i = 0; i < DemoModResources.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            var dest = Path.Combine(dir, DemoModFilenames[i]);
            await ExtractIfNeededAsync(DemoModResources[i], dest, ct);
        }
    }

    private static async Task ExtractIfNeededAsync(string resourceName, string dest, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        if (File.Exists(dest) && new FileInfo(dest).Length > 0)
            return;

        var asm = Assembly.GetExecutingAssembly();
        await using var stream = asm.GetManifestResourceStream(resourceName);
        if (stream == null)
            throw new InvalidOperationException($"内置资源缺失：{resourceName}（请确认已重新构建）");
        await using var outStream = File.Create(dest);
        await stream.CopyToAsync(outStream, ct);
    }

    /// <summary>列出已抽取到某实例 mods 目录的自研演示 Mod 文件名（诊断用）。</summary>
    public static string[] ListDemoMods(Profile profile)
    {
        var dir = profile.ResolvedModsDir;
        return DemoModFilenames
            .Where(f => File.Exists(Path.Combine(dir, f)))
            .ToArray();
    }
}
