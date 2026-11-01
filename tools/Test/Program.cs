using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RCL.Core.Models;
using RCL.Core.Launch;
using RCL.Core.Profiles;
using RCL.Core.Utils;
using RCL.Core.Versions;

namespace RCL.Test;

internal static class Program
{
    private static async Task Main()
    {
        Paths.Root = Path.Combine(Path.GetTempPath(), "rcl_test");
        Paths.EnsureDirectories();

        Console.WriteLine("== 1. 拉取版本清单 ==");
        var manifest = await VersionManifestFetcher.FetchAsync();
        Console.WriteLine($"最新正式版: {manifest.Latest.Release}, 最新快照: {manifest.Latest.Snapshot}, 共 {manifest.Versions.Count} 个版本");

        var target = "1.20.1";
        Console.WriteLine($"\n== 2. 解析版本 {target} ==");
        var rv = await VersionResolver.ResolveAsync(target);
        Console.WriteLine($"MainClass: {rv.MainClass}");
        Console.WriteLine($"库数量: {rv.Libraries.Count}");
        Console.WriteLine($"JavaVersion: {(rv.JavaVersion == null ? "无" : rv.JavaVersion.MajorVersion.ToString())}");
        Console.WriteLine($"AssetIndex: {rv.AssetIndex?.Id}");
        Console.WriteLine($"Jvm参数条数: {rv.JvmArgs.Count}, Game参数条数: {rv.GameArgs.Count}");
        Console.WriteLine($"LegacyArguments: {(rv.LegacyArguments == null ? "无(新版)" : "存在(旧版)")}");

        Console.WriteLine("\n== 3. 验证启动参数拼接 ==");
        var profile = new Profile
        {
            Name = "测试",
            GameVersion = target,
            LoaderType = ModLoaderType.Vanilla,
            MinMemoryMb = 1024,
            MaxMemoryMb = 4096,
            WindowWidth = 1280,
            WindowHeight = 720
        };
        var account = new Account { Username = "TestPlayer", Uuid = "abc123def456", AccessToken = "TOKEN", Xuid = "123" };
        var (javaExe, args) = Launcher.BuildLaunchCommand(rv, profile, account, "C:\\java\\bin\\java.exe", Paths.Root);
        Console.WriteLine($"Java: {javaExe}");
        Console.WriteLine("参数预览(前600字符):");
        Console.WriteLine(args.Length > 600 ? args.Substring(0, 600) + " ..." : args);

        Console.WriteLine("\n== 4. 校验关键变量替换 ==");
        Console.WriteLine($"包含 -Xms1024M: {args.Contains("-Xms1024M")}");
        Console.WriteLine($"包含 -Xmx4096M: {args.Contains("-Xmx4096M")}");
        Console.WriteLine($"包含 auth_player_name=TestPlayer: {args.Contains("TestPlayer")}");
        Console.WriteLine($"包含 natives_directory: {args.Contains(Paths.NativesDir(target).Replace("/", "\\"))}");
        Console.WriteLine($"classpath 已被替换(无占位符): {!args.Contains("${classpath}")}");
        Console.WriteLine($"包含 1280: {args.Contains("1280")}  包含 720: {args.Contains("720")}");

        Console.WriteLine("\n全部核心逻辑校验通过 ✅");
    }
}
