using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;

namespace RCL.UI.Views;

/// <summary>
/// 启动页：在后台完成初始化（加载配置、预热）期间向用户展示品牌与进度。
/// 由 App 在 OnFrameworkInitializationCompleted 中显示，初始化完成后关闭并打开主窗口。
/// </summary>
public partial class SplashWindow : Window
{
    private readonly CancellationTokenSource _cts = new();

    public SplashWindow()
    {
        InitializeComponent();
        _ = AnimateAsync(_cts.Token);
    }

    public void SetStatus(string text) => TxtStatus.Text = text;
    public void SetProgress(double value) => ProgBar.Value = Math.Clamp(value, 0, 100);

    private async Task AnimateAsync(CancellationToken ct)
    {
        string[] steps =
        {
            "正在初始化...",
            "加载账号与实例...",
            "准备 Rainclouds 自研核心...",
            "就绪"
        };
        try
        {
            for (var i = 0; i < steps.Length; i++)
            {
                if (ct.IsCancellationRequested) return;
                Dispatcher.UIThread.Post(() =>
                {
                    SetStatus(steps[i]);
                    SetProgress((i + 1) * 100.0 / steps.Length);
                });
                await Task.Delay(280, ct);
            }
        }
        catch (OperationCanceledException) { }
    }

    protected override void OnClosed(EventArgs e)
    {
        _cts.Cancel();
        base.OnClosed(e);
    }
}
