using System;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using RCL.Core.Licensing;

namespace RCL.UI.Views;

public partial class ActivationWindow : Window
{
    public ActivationWindow()
    {
        InitializeComponent();

        try
        {
            // 程序集名为 rcl，avares 资源路径需使用程序集名而非根命名空间
            var uri = new Uri("avares://rcl/Assets/pay.png");
            using var stream = AssetLoader.Open(uri);
            PayImage.Source = new Bitmap(stream);
        }
        catch
        {
            // 图片缺失时不影响激活流程
        }

        BtnActivateCore.Click += (_, _) => DoActivateCore();
        BtnPasteCore.Click += async (_, _) =>
        {
            try
            {
                var text = await Clipboard!.GetTextAsync();
                if (!string.IsNullOrWhiteSpace(text)) TxtKey.Text = text.Trim();
            }
            catch { }
        };

        BtnActivateModules.Click += (_, _) => DoActivateModules();
        BtnPasteModules.Click += async (_, _) =>
        {
            try
            {
                var text = await Clipboard!.GetTextAsync();
                if (!string.IsNullOrWhiteSpace(text)) TxtKeyModules.Text = text.Trim();
            }
            catch { }
        };

        BtnClose.Click += (_, _) => Close();

        // 初始化两个层级的已解锁状态
        if (LicenseManager.IsActivated)
        {
            Unlocked = true;
            TxtKey.Text = LicenseManager.Mask(LicenseManager.State!.Key);
            BtnActivateCore.Content = "已激活";
            BtnActivateCore.IsEnabled = false;
            MsgCore.Text = "完整版已解锁 · " + LicenseManager.Mask(LicenseManager.State.Key);
            MsgCore.Foreground = Brushes.Green;
        }

        if (ModulesLicenseManager.IsActivated)
        {
            ModulesUnlocked = true;
            TxtKeyModules.Text = ModulesLicenseManager.Mask(ModulesLicenseManager.State!.Key);
            BtnActivateModules.Content = "已激活";
            BtnActivateModules.IsEnabled = false;
            MsgModules.Text = "进阶模块已解锁 · " + ModulesLicenseManager.Mask(ModulesLicenseManager.State.Key);
            MsgModules.Foreground = Brushes.Green;
        }
    }

    /// <summary>完整版是否已解锁。</summary>
    public bool Unlocked { get; private set; }

    /// <summary>进阶模块是否已解锁。</summary>
    public bool ModulesUnlocked { get; private set; }

    private void DoActivateCore()
    {
        var (ok, message) = LicenseManager.Activate(TxtKey.Text);
        MsgCore.Text = message;
        MsgCore.Foreground = ok ? Brushes.Green : Brushes.OrangeRed;
        if (ok)
        {
            Unlocked = true;
            BtnActivateCore.Content = "已激活";
            BtnActivateCore.IsEnabled = false;
        }
    }

    private void DoActivateModules()
    {
        var (ok, message) = ModulesLicenseManager.Activate(TxtKeyModules.Text);
        MsgModules.Text = message;
        MsgModules.Foreground = ok ? Brushes.Green : Brushes.OrangeRed;
        if (ok)
        {
            ModulesUnlocked = true;
            BtnActivateModules.Content = "已激活";
            BtnActivateModules.IsEnabled = false;
        }
    }
}
