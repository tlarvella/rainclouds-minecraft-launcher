using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using RCL.Core.Models;
using RCL.Core.Tools;
using RCL.Core.Utils;
using RCL.UI.ViewModels;

namespace RCL.UI.Views;

public partial class FrpcWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly FrpcConfig _cfg;
    private Process? _proc;
    private CancellationTokenSource? _cts;

    public FrpcWindow(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        _cfg = vm.Settings.Frpc;

        TxtAddr.Text = _cfg.ServerAddr;
        NumSrvPort.Value = _cfg.ServerPort;
        TxtToken.Text = _cfg.Token;
        NumLocal.Value = _cfg.LocalPort;
        NumRemote.Value = _cfg.RemotePort;
        CmbType.SelectedIndex = _cfg.Type == "udp" ? 1 : 0;
        TxtPath.Text = _cfg.FrpcPath;

        BtnCheck.Click += async (_, _) => await CheckAsync();
        BtnStart.Click += async (_, _) => await StartAsync();
        BtnStop.Click += (_, _) => Stop();
        BtnBrowse.Click += async (_, _) => await BrowseAsync();
        Closed += (_, _) => Stop();
    }

    private void Sync()
    {
        _cfg.ServerAddr = TxtAddr.Text?.Trim() ?? "";
        _cfg.ServerPort = (int)(NumSrvPort.Value ?? 7000);
        _cfg.Token = TxtToken.Text?.Trim() ?? "";
        _cfg.LocalPort = (int)(NumLocal.Value ?? 25565);
        _cfg.RemotePort = (int)(NumRemote.Value ?? 25565);
        _cfg.Type = CmbType.SelectedIndex == 1 ? "udp" : "tcp";
        _cfg.FrpcPath = TxtPath.Text?.Trim() ?? "";
        _vm.SaveAll();
    }

    private async Task BrowseAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择 frpc.exe",
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("可执行文件") { Patterns = new[] { "*.exe" } } }
        });
        var p = files.FirstOrDefault()?.TryGetLocalPath();
        if (!string.IsNullOrEmpty(p)) TxtPath.Text = p;
    }

    private async Task CheckAsync()
    {
        Sync();
        TxtLog.Text = "正在检查 frpc...";
        try
        {
            var path = await FrpcManager.EnsureFrpcAsync(_cfg.FrpcPath);
            TxtLog.Text += "\nfrpc 就绪: " + path;
        }
        catch (Exception ex)
        {
            TxtLog.Text += "\n" + ex.Message;
        }
    }

    private async Task StartAsync()
    {
        Sync();
        if (string.IsNullOrWhiteSpace(_cfg.ServerAddr))
        {
            TxtLog.Text = "请先填写 frps 地址（需自备公网 frps 服务器）。";
            return;
        }
        TxtLog.Text = "正在准备 frpc...";
        try
        {
            Stop();
            var exe = await FrpcManager.EnsureFrpcAsync(_cfg.FrpcPath);
            var cfgPath = Path.Combine(Paths.Root, "tools", "frpc.toml");
            FrpcManager.BuildConfig(_cfg, cfgPath);
            TxtLog.Text += "\n已生成配置: " + cfgPath;
            _proc = FrpcManager.Start(exe, cfgPath, Path.Combine(Paths.Root, "tools"));
            _cts = new CancellationTokenSource();
            _ = ReadOutputAsync(_proc, _cts.Token);
        }
        catch (Exception ex)
        {
            TxtLog.Text += "\n启动失败: " + ex.Message;
        }
    }

    private async Task ReadOutputAsync(Process proc, CancellationToken ct)
    {
        try
        {
            while (!proc.StandardOutput.EndOfStream && !ct.IsCancellationRequested)
            {
                var line = await proc.StandardOutput.ReadLineAsync();
                if (line == null) break;
                var kind = FrpcManager.ClassifyLine(line);
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    TxtLog.Text += "\n" + line;
                    if (kind == FrpcLineKind.LoginOk) TxtLog.Text += "\n✓ 已连接 frps";
                    if (kind == FrpcLineKind.ProxyOk) TxtLog.Text += "\n✓ 穿透通道已建立";
                    if (kind == FrpcLineKind.Error) TxtLog.Text += "\n⚠ 错误，请检查服务器/令牌/端口";
                });
            }
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
