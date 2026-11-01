using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using RCL.Core.Download;
using RCL.Core.Java;
using RCL.Core.Utils;

namespace RCL.Core.ModLoaders;

/// <summary>Quilt 加载器安装。</summary>
public static class QuiltInstaller
{
    private const string Base = "https://maven.quiltmc.org/repository/release/org/quiltmc/";

    public static async Task<string> GetLatestInstallerAsync(CancellationToken ct = default)
        => (await MavenMetadata.GetLatestAsync(Base + "quilt-installer/maven-metadata.xml", ct))!;

    public static async Task<string> GetLatestLoaderAsync(CancellationToken ct = default)
        => (await MavenMetadata.GetLatestAsync(Base + "quilt-loader/maven-metadata.xml", ct))!;

    /// <summary>安装 Quilt，返回版本 id（quilt-loader-&lt;loader&gt;-&lt;mc&gt;）。</summary>
    public static async Task<InstallResult> InstallAsync(string javaPath, string mcVersion,
        string? loaderVersion = null, CancellationToken ct = default)
    {
        loaderVersion ??= await GetLatestLoaderAsync(ct);
        var installerVer = await GetLatestInstallerAsync(ct);
        var installerUrl = $"{Base}quilt-installer/{installerVer}/quilt-installer-{installerVer}.jar";
        Directory.CreateDirectory(ModLoaderInstaller.InstallerCacheDir);
        var jar = Path.Combine(ModLoaderInstaller.InstallerCacheDir, $"quilt-installer-{installerVer}.jar");
        if (!File.Exists(jar))
            await Downloader.DownloadFileAsync(installerUrl, jar, ct: ct);

        var args = $"install client {mcVersion} {loaderVersion} --install-dir \"{Paths.Root}\" --no-profile";
        var result = await ModLoaderInstaller.RunInstallerAsync(javaPath, jar, args, ct);
        result.VersionId = $"quilt-loader-{loaderVersion}-{mcVersion}";
        return result;
    }
}
