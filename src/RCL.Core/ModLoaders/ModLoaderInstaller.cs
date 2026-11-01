using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using RCL.Core.Java;
using RCL.Core.Utils;

namespace RCL.Core.ModLoaders;

/// <summary>Mod 加载器安装结果。</summary>
public class InstallResult
{
    public bool Success { get; set; }
    public string? VersionId { get; set; }
    public string? Error { get; set; }
    public string Log { get; set; } = "";
}

/// <summary>Mod 加载器安装的通用逻辑：下载 installer jar 并静默执行。</summary>
public static class ModLoaderInstaller
{
    /// <summary>运行 installer jar（headless）。</summary>
    public static async Task<InstallResult> RunInstallerAsync(string javaPath, string installerJar,
        string arguments, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo(javaPath, $"-jar \"{installerJar}\" {arguments}")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Paths.Root
        };
        var log = new System.Text.StringBuilder();
        using var p = Process.Start(psi)!;
        _ = Task.Run(async () =>
        {
            var buf = new char[1024];
            int n;
            while ((n = await p.StandardOutput.ReadAsync(buf, ct)) > 0) log.Append(buf, 0, n);
        }, ct);
        _ = Task.Run(async () =>
        {
            var buf = new char[1024];
            int n;
            while ((n = await p.StandardError.ReadAsync(buf, ct)) > 0) log.Append(buf, 0, n);
        }, ct);

        await p.WaitForExitAsync(ct);
        if (p.ExitCode != 0)
            return new InstallResult { Success = false, Error = $"安装器退出码 {p.ExitCode}", Log = log.ToString() };
        return new InstallResult { Success = true, Log = log.ToString() };
    }

    public static string InstallerCacheDir => Path.Combine(Paths.Root, "cache", "installers");
}
