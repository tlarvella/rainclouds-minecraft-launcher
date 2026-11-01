using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace RCL.Core.Download;

/// <summary>单条下载任务的进度信息。</summary>
public class DownloadItem
{
    public string Url { get; init; } = "";
    public string Destination { get; init; } = "";
    public string? ExpectedSha1 { get; init; }
    public long Size { get; init; }
    public long Downloaded { get; set; }
    public bool Done { get; set; }
    public bool Failed { get; set; }
    public string? Error { get; set; }
}

/// <summary>并行下载管理器，支持总体进度回调与 SHA1 校验。</summary>
public sealed class Downloader
{
    private static readonly HttpClient _http = new()
    {
        Timeout = TimeSpan.FromMinutes(5),
        DefaultRequestHeaders = { { "User-Agent", "RaincloudsMC/rcl" } }
    };

    /// <summary>下载单个文件（带重试与校验）。</summary>
    public static async Task<bool> DownloadFileAsync(string url, string dest, string? expectedSha1 = null,
        IProgress<long>? progress = null, CancellationToken ct = default, int retries = 3)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        for (int attempt = 0; attempt < retries; attempt++)
        {
            try
            {
                if (expectedSha1 != null && Utils.Hash.Sha1Matches(dest, expectedSha1))
                    return true;

                using var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
                resp.EnsureSuccessStatusCode();
                await using var stream = await resp.Content.ReadAsStreamAsync(ct);
                await using var fs = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
                var buffer = new byte[81920];
                int read;
                while ((read = await stream.ReadAsync(buffer, ct)) > 0)
                {
                    await fs.WriteAsync(buffer.AsMemory(0, read), ct);
                    progress?.Report(read);
                }
                await fs.FlushAsync(ct);

                if (expectedSha1 != null && !Utils.Hash.Sha1Matches(dest, expectedSha1))
                    throw new IOException("SHA1 校验失败");
                return true;
            }
            catch (Exception) when (attempt < retries - 1 && !ct.IsCancellationRequested)
            {
                await Task.Delay(300 * (attempt + 1), ct);
            }
            catch (Exception)
            {
                return false;
            }
        }
        return false;
    }

    /// <summary>并行下载一组文件，返回失败数量。</summary>
    public static async Task<int> DownloadBatchAsync(DownloadItem[] items, int maxConcurrency = 8,
        IProgress<(int done, int total, long bytes)>? progress = null, CancellationToken ct = default)
    {
        var done = 0;
        var total = items.Length;
        var failed = 0;
        long bytes = 0;
        using var throttler = new SemaphoreSlim(maxConcurrency);
        var tasks = items.Select(async item =>
        {
            await throttler.WaitAsync(ct);
            try
            {
                var ok = await DownloadFileAsync(item.Url, item.Destination, item.ExpectedSha1,
                    new Progress<long>(b => { Interlocked.Add(ref bytes, b); item.Downloaded += b; }), ct);
                if (ok) { item.Done = true; }
                else { item.Failed = true; Interlocked.Increment(ref failed); }
            }
            finally
            {
                Interlocked.Increment(ref done);
                progress?.Report((done, total, bytes));
                throttler.Release();
            }
        });
        await Task.WhenAll(tasks);
        return failed;
    }

    public static HttpClient Client => _http;
}
