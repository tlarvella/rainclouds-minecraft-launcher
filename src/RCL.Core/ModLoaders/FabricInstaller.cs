using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using RCL.Core.Download;
using RCL.Core.Java;
using RCL.Core.Utils;

namespace RCL.Core.ModLoaders;

/// <summary>Fabric 加载器安装（下载 installer 并静默安装）。</summary>
public static class FabricInstaller
{
    private const string Base = "https://maven.fabricmc.net/net/fabricmc/";

    public static async Task<string> GetLatestInstallerAsync(CancellationToken ct = default)
        => (await MavenMetadata.GetLatestAsync(Base + "fabric-installer/maven-metadata.xml", ct))!;

    public static async Task<string> GetLatestLoaderAsync(CancellationToken ct = default)
        => (await MavenMetadata.GetLatestAsync(Base + "fabric-loader/maven-metadata.xml", ct))!;

    /// <summary>安装 Fabric，返回版本 id（fabric-loader-&lt;loader&gt;-&lt;mc&gt;）。</summary>
    public static async Task<InstallResult> InstallAsync(string javaPath, string mcVersion,
        string? loaderVersion = null, CancellationToken ct = default)
    {
        loaderVersion ??= await GetLatestLoaderAsync(ct);
        var installerVer = await GetLatestInstallerAsync(ct);
        var installerUrl = $"{Base}fabric-installer/{installerVer}/fabric-installer-{installerVer}.jar";
        Directory.CreateDirectory(ModLoaderInstaller.InstallerCacheDir);
        var jar = Path.Combine(ModLoaderInstaller.InstallerCacheDir, $"fabric-installer-{installerVer}.jar");
        if (!File.Exists(jar))
            await Downloader.DownloadFileAsync(installerUrl, jar, ct: ct);

        var args = $"client -mcversion {mcVersion} -loader {loaderVersion} -dir \"{Paths.Root}\" -downloadMinecraft -noprofile";
        var result = await ModLoaderInstaller.RunInstallerAsync(javaPath, jar, args, ct);
        result.VersionId = $"fabric-loader-{loaderVersion}-{mcVersion}";
        return result;
    }
}
