using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using RCL.UI.Views;

namespace RCL.UI;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var splash = new SplashWindow();
            splash.Show();

            // 在启动页展示期间完成轻量初始化，然后切换到主窗口
            _ = Task.Run(async () =>
            {
                await Task.Delay(900);
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    var main = new MainWindow();
                    main.Show();
                    desktop.MainWindow = main;
                    splash.Close();
                });
            });
        }

        base.OnFrameworkInitializationCompleted();
    }
}
