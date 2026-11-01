using System;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using System.Threading.Tasks;

namespace RCL.UI.Views;

/// <summary>Avalonia 没有内置消息框，这里用纯代码构造一个轻量确认框。</summary>
public static class Dialogs
{
    public static Task<bool> ConfirmAsync(Window owner, string title, string message,
        string okText = "确定", string cancelText = "取消")
    {
        var tcs = new TaskCompletionSource<bool>();
        var win = new Window
        {
            Title = title,
            Width = 440,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Brushes.White
        };

        var text = new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13,
            Foreground = Brushes.Black,
            Margin = new Avalonia.Thickness(0, 0, 0, 18)
        };

        var cancel = new Button { Content = cancelText, Padding = new Avalonia.Thickness(18, 6) };
        var ok = new Button
        {
            Content = okText,
            Padding = new Avalonia.Thickness(18, 6),
            Background = new SolidColorBrush(Color.Parse("#1f6feb")),
            Foreground = Brushes.White
        };

        cancel.Click += (_, _) => { tcs.TrySetResult(false); win.Close(); };
        ok.Click += (_, _) => { tcs.TrySetResult(true); win.Close(); };
        win.Closed += (_, _) => tcs.TrySetResult(false);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8
        };
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);

        var panel = new StackPanel { Margin = new Avalonia.Thickness(22), Spacing = 6 };
        panel.Children.Add(new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeight.Bold });
        panel.Children.Add(text);
        panel.Children.Add(buttons);

        win.Content = panel;
        win.ShowDialog(owner);
        return tcs.Task;
    }

    public static Task ShowInfoAsync(Window owner, string title, string message)
    {
        var tcs = new TaskCompletionSource<bool>();
        var win = new Window
        {
            Title = title,
            Width = 480,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Brushes.White
        };

        var ok = new Button
        {
            Content = "知道了",
            Padding = new Avalonia.Thickness(18, 6),
            HorizontalAlignment = HorizontalAlignment.Right,
            Background = new SolidColorBrush(Color.Parse("#1f6feb")),
            Foreground = Brushes.White
        };
        ok.Click += (_, _) => { tcs.TrySetResult(true); win.Close(); };
        win.Closed += (_, _) => tcs.TrySetResult(true);

        var panel = new StackPanel { Margin = new Avalonia.Thickness(22), Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeight.Bold });
        panel.Children.Add(new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.Parse("#333333"))
        });
        panel.Children.Add(ok);

        win.Content = panel;
        win.ShowDialog(owner);
        return tcs.Task;
    }
}
