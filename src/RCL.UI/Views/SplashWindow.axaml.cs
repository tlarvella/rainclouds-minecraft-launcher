using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using RCL.Core.Extras;

namespace RCL.UI.Views;

/// <summary>
/// 启动页：在后台完成初始化（加载配置、预热）期间向用户展示品牌与进度。
/// 由 App 在 OnFrameworkInitializationCompleted 中显示，初始化完成后关闭并打开主窗口。
/// 每次启动随机展示一条内嵌名言；用户可在彩蛋馆把任意内置壁纸设为这里背景。
/// </summary>
public partial class SplashWindow : Window
{
    private readonly CancellationTokenSource _cts = new();

    public SplashWindow()
    {
        InitializeComponent();
        TxtQuote.Text = EasterEggAssets.RandomQuote();
        ApplyBackground();
        _ = AnimateAsync(_cts.Token);
    }

    /// <summary>载入自定义启动页背景（来自内嵌壁纸导出），失败时回退默认深色。</summary>
    private void ApplyBackground()
    {
        if (EasterEggAssets.HasCustomBackground)
        {
            try
            {
                using var fs = File.OpenRead(EasterEggAssets.BackgroundPath);
                var bmp = new Bitmap(fs);
                Background = new ImageBrush(bmp) { Stretch = Stretch.UniformToFill };
                DimLayer.IsVisible = true;
                return;
            }
            catch
            {
                // 背景文件损坏 / 格式异常时静默回退默认样式
            }
        }
        Background = new SolidColorBrush(Color.Parse("#14161c"));
        DimLayer.IsVisible = false;
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
