using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RCL.Core.Libraries;
using RCL.Core.Models;
using RCL.Core.Utils;
using RCL.Core.Versions;

namespace RCL.Core.Launch;

/// <summary>
/// 组装 JVM 与游戏启动参数、进行变量替换，并启动游戏进程。
/// </summary>
public static class Launcher
{
    public const string LauncherName = "RaincloudsMC";
    public const string LauncherVersion = "1.0.0";

    /// <summary>构建完整启动命令行。</summary>
    public static (string JavaExe, string Arguments) BuildLaunchCommand(
        ResolvedVersion rv, Profile profile, Account? account, string javaPath, string gameDir)
    {
        var nativesDir = Paths.NativesDir(rv.Id);
        var classpath = LibraryManager.BuildClasspath(rv);
        var assetsId = rv.AssetIndex?.Id ?? "legacy";

        var offlineName = profile.OfflineUsername ?? "Player";
        var isMicrosoft = account != null && account.IsMicrosoft && !string.IsNullOrEmpty(account.AccessToken);
        var playerName = isMicrosoft ? account!.Username ?? offlineName : (account?.Username ?? offlineName);
        var uuid = (isMicrosoft ? account!.Uuid : null) ?? OfflineUuid(playerName);
        var token = isMicrosoft ? account!.AccessToken! : "0";
        var userType = isMicrosoft ? "msa" : "offline";

        var width = profile.WindowWidth ?? 854;
        var height = profile.WindowHeight ?? 480;
        var hasResolution = profile.Fullscreen != true && (profile.WindowWidth.HasValue || profile.WindowHeight.HasValue);

        var vars = new Dictionary<string, string>
        {
            ["natives_directory"] = nativesDir,
            ["launcher_name"] = LauncherName,
            ["launcher_version"] = LauncherVersion,
            ["library_directory"] = Paths.LibrariesDir,
            ["classpath"] = classpath,
            ["version_name"] = rv.Id,
            ["version_type"] = rv.LegacyArguments != null ? "release" : (rv.Id.Contains("forge") || rv.Id.Contains("fabric") || rv.Id.Contains("quilt") ? "RCL" : "release"),
            ["assets_root"] = Paths.AssetsDir,
            ["assets_index_name"] = assetsId,
            ["game_directory"] = gameDir,
            ["auth_player_name"] = playerName,
            ["auth_uuid"] = uuid,
            ["auth_access_token"] = token,
            ["auth_xuid"] = account?.Xuid ?? "0",
            ["user_type"] = userType,
            ["user_properties"] = "{}",
            ["resolution_width"] = width.ToString(),
            ["resolution_height"] = height.ToString()
        };

        var features = new HashSet<string>();
        if (hasResolution) features.Add("has_custom_resolution");

        // JVM 参数
        var jvmArgs = new List<string>();
        if (!string.IsNullOrWhiteSpace(profile.ExtraJvmArgs ?? ""))
            jvmArgs.AddRange(SplitArgs(profile.ExtraJvmArgs!));
        var minMem = profile.MinMemoryMb ?? 2048;
        var maxMem = profile.MaxMemoryMb ?? 4096;
        jvmArgs.Add($"-Xms{minMem}M");
        jvmArgs.Add($"-Xmx{maxMem}M");

        // 日志配置（现代版本）
        if (rv.Logging?.Client?.File != null && !string.IsNullOrEmpty(rv.Logging.Client.Argument))
        {
            var logPath = EnsureLogConfig(rv);
            if (logPath != null)
                jvmArgs.Add(rv.Logging.Client.Argument.Replace("${path}", logPath));
        }

        // ----- Rainclouds 自研核心（10yv 核心）：必须在 ResolveArgs 之前把加载器 jar 并入 classpath，
        //       否则 -cp ${classpath} 解析时不会包含核心 jar，导致主类无法加载 -----
        string effectiveMain = rv.MainClass;
        if (profile.LoaderType == ModLoaderType.RaincloudsCore)
        {
            classpath = classpath + Path.PathSeparator + Paths.RaincloudsCoreJar;
            vars["classpath"] = classpath;
            effectiveMain = "io.rainclouds.core.loader.RaincloudsLoader";
        }

        jvmArgs.AddRange(ResolveArgs(rv.JvmArgs, vars, features));

        // 核心专用系统属性，放在 jvmArgs 最前优先解析
        if (profile.LoaderType == ModLoaderType.RaincloudsCore)
        {
            jvmArgs.Insert(0, "-Drainclouds.client=true");
            jvmArgs.Insert(0, "-Drainclouds.gameVersion=" + rv.Id);
            jvmArgs.Insert(0, "-Drainclouds.modsDir=" + profile.ResolvedModsDir);
            jvmArgs.Insert(0, "-Drainclouds.gameMain=" + rv.MainClass);
        }

        // 游戏参数（legacy 或 新版）
        var gameArgs = new List<string>();
        if (rv.LegacyArguments != null)
            gameArgs.AddRange(SplitArgs(rv.LegacyArguments));
        else
            gameArgs.AddRange(ResolveArgs(rv.GameArgs, vars, features));

        if (profile.Fullscreen == true)
            gameArgs.Add("--fullscreen");
        if (!string.IsNullOrWhiteSpace(profile.ExtraGameArgs ?? ""))
            gameArgs.AddRange(SplitArgs(profile.ExtraGameArgs!));

        var sb = new StringBuilder();
        foreach (var a in jvmArgs) sb.Append(' ').Append(QuoteIfNeeded(a));
        sb.Append(' ').Append(QuoteIfNeeded(effectiveMain));
        foreach (var a in gameArgs) sb.Append(' ').Append(QuoteIfNeeded(a));

        return (javaPath, sb.ToString().Trim());
    }

    private static string? EnsureLogConfig(ResolvedVersion rv)
    {
        var file = rv.Logging!.Client!.File!;
        var dest = Path.Combine(Paths.AssetsDir, "log_configs", file.Sha1!);
        if (!File.Exists(dest))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            Download.Downloader.DownloadFileAsync(file.Url!, dest, file.Sha1).GetAwaiter().GetResult();
        }
        return File.Exists(dest) ? dest : null;
    }

    /// <summary>解析参数列表（处理字符串与带规则的对象）。</summary>
    private static List<string> ResolveArgs(List<JsonElement> args, Dictionary<string, string> vars, HashSet<string> features)
    {
        var result = new List<string>();
        foreach (var el in args)
        {
            if (el.ValueKind == JsonValueKind.String)
            {
                result.Add(ReplaceVars(el.GetString()!, vars));
            }
            else if (el.ValueKind == JsonValueKind.Object)
            {
                if (!ArgsRuleAllows(el, features)) continue;
                if (el.TryGetProperty("value", out var value))
                {
                    if (value.ValueKind == JsonValueKind.String)
                        result.Add(ReplaceVars(value.GetString()!, vars));
                    else if (value.ValueKind == JsonValueKind.Array)
                        foreach (var v in value.EnumerateArray())
                            if (v.ValueKind == JsonValueKind.String)
                                result.Add(ReplaceVars(v.GetString()!, vars));
                }
            }
        }
        return result;
    }

    private static bool ArgsRuleAllows(JsonElement obj, HashSet<string> features)
    {
        if (!obj.TryGetProperty("rules", out var rules) || rules.ValueKind != JsonValueKind.Array) return true;
        var allowed = false;
        foreach (var rule in rules.EnumerateArray())
        {
            if (!rule.TryGetProperty("action", out var action)) continue;
            var matches = true;
            if (rule.TryGetProperty("os", out var os) && os.ValueKind == JsonValueKind.Object)
            {
                if (os.TryGetProperty("name", out var n) && n.GetString() != "windows") matches = false;
                if (os.TryGetProperty("arch", out var a) && a.GetString() != "x86_64") matches = false;
            }
            if (matches && rule.TryGetProperty("features", out var feats) && feats.ValueKind == JsonValueKind.Object)
            {
                foreach (var f in feats.EnumerateObject())
                {
                    var enabled = features.Contains(f.Name);
                    if (enabled != f.Value.GetBoolean()) { matches = false; break; }
                }
            }
            if (matches) allowed = action.GetString() == "allow";
        }
        return allowed;
    }

    private static string ReplaceVars(string input, Dictionary<string, string> vars)
    {
        foreach (var (k, v) in vars)
            input = input.Replace("${" + k + "}", v);
        return input;
    }

    private static string QuoteIfNeeded(string arg)
    {
        if (arg.Length == 0) return "\"\"";
        if (arg.Contains(' ') || arg.Contains('"') || arg.Contains('&') || arg.Contains('|') || arg.Contains('<') || arg.Contains('>'))
            return "\"" + arg.Replace("\"", "\\\"") + "\"";
        return arg;
    }

    private static List<string> SplitArgs(string line)
    {
        var args = new List<string>();
        var sb = new StringBuilder();
        bool inQuote = false;
        foreach (var c in line)
        {
            if (c == '"') { inQuote = !inQuote; continue; }
            if (char.IsWhiteSpace(c) && !inQuote)
            {
                if (sb.Length > 0) { args.Add(sb.ToString()); sb.Clear(); }
            }
            else sb.Append(c);
        }
        if (sb.Length > 0) args.Add(sb.ToString());
        return args;
    }

    private static string OfflineUuid(string name)
    {
        using var md5 = MD5.Create();
        var hash = md5.ComputeHash(Encoding.UTF8.GetBytes("OfflinePlayer:" + name));
        hash[6] = (byte)((hash[6] & 0x0f) | 0x30);
        hash[8] = (byte)((hash[8] & 0x3f) | 0x80);
        var hex = Convert.ToHexString(hash).ToLowerInvariant();
        return $"{hex[..8]}-{hex.Substring(8, 4)}-{hex.Substring(12, 4)}-{hex.Substring(16, 4)}-{hex.Substring(20, 12)}";
    }

    /// <summary>启动游戏进程。</summary>
    public static Process Launch(string javaExe, string arguments, string workingDirectory)
    {
        Directory.CreateDirectory(workingDirectory);
        var psi = new ProcessStartInfo(javaExe, arguments)
        {
            UseShellExecute = false,
            WorkingDirectory = workingDirectory,
            CreateNoWindow = false,
            RedirectStandardOutput = false,
            RedirectStandardError = false
        };
        return Process.Start(psi)!;
    }
}
