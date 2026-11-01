using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using RCL.Core.Java;
using RCL.Core.Models;
using RCL.Core.Tools;
using RCL.Core.Utils;
using RCL.UI.ViewModels;

namespace RCL.UI.Views;

public partial class ServerWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly ServerConfig _cfg;
    private Process? _proc;
    private CancellationTokenSource? _cts;

    public ServerWindow(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        _cfg = vm.Settings.Server;

        CmbType.SelectedIndex = _cfg.Type switch { "paper" => 1, "fabric" => 2, _ => 0 };
        TxtVersion.Text = _cfg.Version;
        NumPort.Value = _cfg.Port;
        NumMem.Value = _cfg.MemoryMb;
        TxtMotd.Text = _cfg.Motd;
        ChkOnline.IsChecked = _cfg.OnlineMode;
        TxtJava.Text = _cfg.JavaPath;

        BtnDownload.Click += async (_, _) => await DownloadAsync();
        BtnStart.Click += async (_, _) => await StartAsync();
        BtnStop.Click += (_, _) => Stop();
        BtnPlugins.Click += (_, _) => OpenFolder(ServerManager.PluginsDir);
        BtnJava.Click += async (_, _) => await BrowseJavaAsync();
        Closed += (_, _) => Stop();
    }

    private void Sync()
    {
        _cfg.Type = CmbType.SelectedIndex switch { 1 => "paper", 2 => "fabric", _ => "vanilla" };
        _cfg.Version = string.IsNullOrWhiteSpace(TxtVersion.Text) ? "1.20.1" : TxtVersion.Text!.Trim();
        _cfg.Port = (int)(NumPort.Value ?? 25565);
        _cfg.MemoryMb = (int)(NumMem.Value ?? 2048);
        _cfg.Motd = TxtMotd.Text?.Trim() ?? "A Rainclouds Server";
        _cfg.OnlineMode = ChkOnline.IsChecked ?? true;
        _cfg.JavaPath = TxtJava.Text?.Trim() ?? "";
        _vm.SaveAll();
    }

    private async Task BrowseJavaAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择 java.exe",
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("Java") { Patterns = new[] { "java.exe", "java" } } }
        });
        var p = files.FirstOrDefault()?.TryGetLocalPath();
        if (!string.IsNullOrEmpty(p)) TxtJava.Text = p;
    }

    private async Task DownloadAsync()
    {
        Sync();
        TxtLog.Text = "正在解析服务端下载地址...";
        try
        {
            var (url, filename) = await ServerManager.ResolveServerJarAsync(_cfg, CancellationToken.None);
            TxtLog.Text += "\n下载: " + url;
            await ServerManager.DownloadServerJarAsync(url, filename, CancellationToken.None);
            TxtLog.Text += "\n✓ 服务端已下载到 " + ServerManager.ServerDir;
        }
        catch (Exception ex)
        {
            TxtLog.Text += "\n下载失败: " + ex.Message;
        }
    }

    private async Task StartAsync()
    {
        Sync();
        TxtLog.Text = "正在准备启动服务端...";
        try
        {
            Stop();
            var javaPath = await ResolveJavaAsync();
            _proc = await ServerManager.StartAsync(_cfg, javaPath);
            _cts = new CancellationTokenSource();
            _ = ReadOutputAsync(_proc, _cts.Token);
        }
        catch (Exception ex)
        {
            TxtLog.Text += "\n启动失败: " + ex.Message;
        }
    }

    private async Task<string> ResolveJavaAsync()
    {
        if (!string.IsNullOrWhiteSpace(_cfg.JavaPath) && File.Exists(_cfg.JavaPath))
            return _cfg.JavaPath!;
        var installed = JavaManager.DetectInstalled();
        var best = JavaManager.FindBest(installed, 17) ?? installed.FirstOrDefault();
        if (best != null) return best.JavaPath;
        var dl = await JavaManager.DownloadTemurinAsync(17);
        return dl?.JavaPath ?? throw new InvalidOperationException("未找到 Java，请在设置中指定。");
    }

    private async Task ReadOutputAsync(Process proc, CancellationToken ct)
    {
        try
        {
            while (!proc.StandardOutput.EndOfStream && !ct.IsCancellationRequested)
            {
                var line = await proc.StandardOutput.ReadLineAsync();
                if (line == null) break;
                var kind = ServerManager.ClassifyLine(line);
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    TxtLog.Text += "\n" + line;
                    if (kind == ServerLineKind.Ready) TxtLog.Text += "\n✓ 服务端已就绪，玩家可连接";
                    if (kind == ServerLineKind.Error) TxtLog.Text += "\n⚠ 发生错误，请查看上方日志";
                });
            }
        }
        catch { }
    }

    private static void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            if (OperatingSystem.IsWindows())
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
            else if (OperatingSystem.IsMacOS())
                Process.Start(new ProcessStartInfo("open", $"\"{path}\"") { UseShellExecute = true });
            else
                Process.Start(new ProcessStartInfo("xdg-open", $"\"{path}\"") { UseShellExecute = true });
        }
        catch { }
    }

    private void Stop()
    {
        try { _cts?.Cancel(); } catch { }
        try { _proc?.Kill(); _proc?.Dispose(); } catch { }
        _proc = null;
    }
}
