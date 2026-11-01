using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RCL.Core.Download;
using RCL.Core.Utils;

namespace RCL.Core.Java;

/// <summary>一个可用的 Java 运行时。</summary>
public class JavaRuntime
{
    public string JavaPath { get; set; } = "";
    public int MajorVersion { get; set; }
    public string Source { get; set; } = "";
}

/// <summary>
/// Java 运行时管理：扫描本机已安装 JDK/JRE、按版本挑选、必要时下载 Adoptium Temurin。
/// </summary>
public static class JavaManager
{
    public static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    /// <summary>扫描本机常见位置的 Java + PATH 中的 java。</summary>
    public static List<JavaRuntime> DetectInstalled()
    {
        var found = new List<JavaRuntime>();
        var dirs = new List<string>();

        if (IsWindows)
        {
            var pf = Environment.GetEnvironmentVariable("ProgramFiles");
            var pf86 = Environment.GetEnvironmentVariable("ProgramFiles(x86)");
            if (pf != null) dirs.Add(Path.Combine(pf, "Java"));
            if (pf86 != null) dirs.Add(Path.Combine(pf86, "Java"));
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            dirs.Add(Path.Combine(local, "Programs", "Eclipse Adoptium"));
            dirs.Add(Path.Combine(local, "Programs", "AdoptOpenJDK"));
            dirs.Add(Path.Combine(local, "Microsoft", "Microsoft Java"));
        }
        else
        {
            dirs.Add("/usr/lib/jvm");
            dirs.Add("/opt");
            dirs.Add("/Library/Java/JavaVirtualMachines");
        }

        // 扫描目录树寻找 bin/java(.exe)
        foreach (var dir in dirs.Where(Directory.Exists))
        {
            try
            {
                foreach (var sub in Directory.EnumerateDirectories(dir, "*", SearchOption.AllDirectories))
                {
                    var candidate = Path.Combine(sub, "bin", IsWindows ? "java.exe" : "java");
                    if (File.Exists(candidate))
                        TryAdd(found, candidate, "system");
                }
            }
            catch { /* 权限或符号链接问题忽略 */ }
        }

        // PATH 中的 java
        var pathJava = FindInPath("java" + (IsWindows ? ".exe" : ""));
        if (pathJava != null) TryAdd(found, pathJava, "path");

        // 启动器自带的运行时
        if (Directory.Exists(Paths.JavaDir))
        {
            foreach (var sub in Directory.EnumerateDirectories(Paths.JavaDir))
            {
                var candidate = Path.Combine(sub, "bin", IsWindows ? "java.exe" : "java");
                if (File.Exists(candidate)) TryAdd(found, candidate, "bundled");
            }
        }

        return found.DistinctBy(r => r.JavaPath).ToList();
    }

    private static void TryAdd(List<JavaRuntime> list, string path, string source)
    {
        var major = GetMajorVersion(path);
        if (major <= 0) return;
        list.Add(new JavaRuntime { JavaPath = path, MajorVersion = major, Source = source });
    }

    /// <summary>运行 java -version 解析主版本号。</summary>
    public static int GetMajorVersion(string javaPath)
    {
        try
        {
            var psi = new ProcessStartInfo(javaPath, "-version")
            {
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi)!;
            var output = p.StandardError.ReadToEnd() + p.StandardOutput.ReadToEnd();
            p.WaitForExit(5000);
            // 形如 "openjdk version \"17.0.2\" 17.0.2+8" 或 "java version \"1.8.0_301\""
            var idx = output.IndexOf("version \"", StringComparison.Ordinal);
            if (idx < 0) return 0;
            var start = idx + 9;
            var end = output.IndexOf('"', start);
            var ver = output.Substring(start, end - start);
            return ParseMajor(ver);
        }
        catch
        {
            return 0;
        }
    }

    private static int ParseMajor(string ver)
    {
        var main = ver.Split('.')[0];
        if (main == "1") // 1.8 -> 8
        {
            var second = ver.Split('.')[^1].Split('_')[0];
            if (int.TryParse(second, out var m)) return m;
        }
        if (int.TryParse(main, out var major)) return major;
        return 0;
    }

    /// <summary>挑选满足最低版本要求的最佳 Java（优先精确匹配或更高版本）。</summary>
    public static JavaRuntime? FindBest(List<JavaRuntime> installed, int requiredMajor)
    {
        if (installed.Count == 0) return null;
        var exact = installed.FirstOrDefault(j => j.MajorVersion == requiredMajor);
        if (exact != null) return exact;
        var higher = installed.Where(j => j.MajorVersion >= requiredMajor)
                              .OrderBy(j => j.MajorVersion).FirstOrDefault();
        if (higher != null) return higher;
        // 没有更高版本时返回最高的（勉强可用）
        return installed.OrderByDescending(j => j.MajorVersion).FirstOrDefault();
    }

    private static string? FindInPath(string name)
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var p in pathEnv.Split(Path.PathSeparator))
        {
            try
            {
                var full = Path.Combine(p.Trim(), name);
                if (File.Exists(full)) return full;
            }
            catch { }
        }
        return null;
    }

    /// <summary>下载并解压指定主版本号的 Temurin JDK 到运行时目录。</summary>
    public static async Task<JavaRuntime?> DownloadTemurinAsync(int major,
        IProgress<(long bytes, string stage)>? progress = null, CancellationToken ct = default)
    {
        var url = $"https://api.adoptium.net/v3/binary/latest/{major}/ga/windows/x64/jdk/hotspot/normal/eclipse";
        var tmpZip = Path.Combine(Path.GetTempPath(), $"temurin-{major}.zip");
        progress?.Report((0, $"下载 Temurin JDK {major}"));
        var ok = await Downloader.DownloadFileAsync(url, tmpZip, progress: new Progress<long>(b => progress?.Report((b, "下载中"))), ct: ct);
        if (!ok) return null;

        var extractDir = Path.Combine(Paths.JavaDir, major.ToString());
        Directory.CreateDirectory(extractDir);
        progress?.Report((0, "解压 JDK"));
        ZipFile.ExtractToDirectory(tmpZip, extractDir, overwriteFiles: true);
        File.Delete(tmpZip);

        // 找到 bin/java.exe
        var javaExe = Directory.EnumerateFiles(extractDir, "java.exe", SearchOption.AllDirectories).FirstOrDefault();
        if (javaExe == null) return null;
        var actualMajor = GetMajorVersion(javaExe);
        return new JavaRuntime { JavaPath = javaExe, MajorVersion = actualMajor > 0 ? actualMajor : major, Source = "bundled" };
    }
}
