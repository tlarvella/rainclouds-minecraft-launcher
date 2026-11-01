using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using RCL.Core.Accounts;
using RCL.Core.Content;
using RCL.Core.Java;
using RCL.Core.Launch;
using RCL.Core.Licensing;
using RCL.Core.Models;
using RCL.Core.ModLoaders;
using RCL.Core.Profiles;
using RCL.Core.Utils;
using RCL.UI.ViewModels;

namespace RCL.UI.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm = new();
    private CancellationTokenSource? _playCts;
    private bool _suppressSelection;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _vm;

        Loaded += OnLoaded;

        LstProfiles.SelectionChanged += (_, _) =>
        {
            if (_suppressSelection) return;
            if (LstProfiles.SelectedItem is Profile p)
            {
                _vm.SelectedProfile = p;
                PopulateEditor();
                _vm.RefreshMods();
            }
        };

        // 顶栏
        BtnAccount.Click += async (_, _) => await OpenAccountsAsync();
        BtnLicense.Click += async (_, _) => await OpenActivationAsync(false);
        BtnModules.Click += async (_, _) => await OpenActivationAsync(true);

        // 工具箱（进阶模块）
        BtnNetwork.Click += async (_, _) => await OpenNetworkAsync();
        BtnFrpc.Click += async (_, _) => await OpenFrpcAsync();
        BtnServer.Click += async (_, _) => await OpenServerAsync();

        // 实例
        BtnNewInstance.Click += (_, _) => NewInstance();
        BtnCore.Click += async (_, _) => await CreateCoreInstanceAsync();
        BtnDeleteProfile.Click += async (_, _) => await DeleteProfileAsync();
        BtnOpenInstance.Click += (_, _) => OpenFolder(CurrentGameDir());
        BtnPlay.Click += async (_, _) => await PlayAsync();
        BtnToggleLog.Click += (_, _) => TxtLog.IsVisible = !TxtLog.IsVisible;

        // 概览
        BtnOpenMods.Click += (_, _) => OpenFolder(CurrentModsDir());
        BtnOpenGameDir.Click += (_, _) => OpenFolder(CurrentGameDir());
        BtnProvisionCore.Click += async (_, _) => await ProvisionCoreAsync();

        // 模组
        BtnAddMod.Click += async (_, _) => await AddModsAsync();
        BtnRefreshMods.Click += (_, _) => { _vm.RefreshMods(); _vm.AddLog("已刷新模组列表"); };
        BtnDeleteMod.Click += async (_, _) => await DeleteModAsync();
        BtnOpenMods2.Click += (_, _) => OpenFolder(CurrentModsDir());

        // 设置
        BtnSaveProfile.Click += (_, _) => SaveProfile();
        BtnRefreshVersions.Click += async (_, _) => await LoadVersionsAsync();
        BtnLatestLoader.Click += async (_, _) => await FetchLatestLoaderAsync();
        BtnAutoJava.Click += (_, _) => AutoDetectJava();
        BtnBrowseJava.Click += async (_, _) => await BrowseJavaAsync();
        BtnBrowseDir.Click += async (_, _) => await BrowseDirAsync();
    }

    // ---------------------------------------------------------------- 初始化

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        _vm.LoadAll();

        TxtGameRoot.Text = _vm.Settings.GameRoot ?? "";
        TxtClientId.Text = _vm.Settings.AzureClientId ?? "";
        NumConcurrency.Value = _vm.Settings.DownloadConcurrency;

        NumMinMem.Value = 2048;
        NumMaxMem.Value = 4096;
        NumWidth.Value = 854;
        NumHeight.Value = 480;

        _suppressSelection = true;
        LstProfiles.SelectedItem = _vm.SelectedProfile;
        _suppressSelection = false;

        PopulateEditor();
        _vm.RefreshMods();
        _ = LoadVersionsAsync();
        UpdateLicenseUi();

        _vm.AddLog("Rainclouds 启动器已就绪");
        _vm.AddLog("实例目录: " + Paths.InstancesDir);

        if (!LicenseManager.IsActivated)
            _vm.AddLog("当前为未解锁状态，点击右上角“未解锁”输入激活码。");
    }

    private async Task LoadVersionsAsync()
    {
        await _vm.LoadVersionListAsync();
        PopulateEditor();
    }

    private void UpdateLicenseUi()
    {
        _vm.LicenseText = LicenseManager.IsActivated
            ? "已解锁"
            : "未解锁 · 1 元";
        BtnLicense.Foreground = LicenseManager.IsActivated
            ? new SolidColorBrush(Color.Parse("#7ee2a8"))
            : new SolidColorBrush(Color.Parse("#ffd479"));

        _vm.ModulesText = ModulesLicenseManager.IsActivated
            ? "模块已解锁"
            : "模块 ¥20";
        BtnModules.Foreground = ModulesLicenseManager.IsActivated
            ? new SolidColorBrush(Color.Parse("#7ee2a8"))
            : new SolidColorBrush(Color.Parse("#ffd479"));
    }

    // ---------------------------------------------------------------- 实例

    private string CurrentGameDir()
        => _vm.SelectedProfile?.ResolvedGameDir ?? Paths.Root;

    private string CurrentModsDir()
        => _vm.SelectedProfile?.ResolvedModsDir ?? Path.Combine(Paths.Root, "mods");

    private void NewInstance()
    {
        SyncEditorToProfile();
        var p = new Profile
        {
            Name = "新实例 " + (_vm.Profiles.Count + 1),
            GameVersion = "latest_release",
            MinMemoryMb = 2048,
            MaxMemoryMb = 4096
        };
        Paths.EnsureInstanceDirectories(p.Id);
        _vm.Profiles.Add(p);
        SelectProfile(p);
        _vm.AddLog("已创建实例: " + p.Name);
    }

    private void SelectProfile(Profile p)
    {
        _suppressSelection = true;
        _vm.SelectedProfile = p;
        LstProfiles.SelectedItem = p;
        _suppressSelection = false;
        PopulateEditor();
        _vm.RefreshMods();
    }

    private async Task CreateCoreInstanceAsync()
    {
        var p = RaincloudsPack.CreateProfile();
        // 避免重名
        var baseName = p.Name;
        var i = 2;
        while (_vm.Profiles.Any(x => x.Name == p.Name))
            p.Name = $"{baseName} ({i++})";

        _vm.Profiles.Add(p);
        SelectProfile(p);
        _vm.AddLog("已创建内置核心实例: " + p.Name);

        // 自研核心（RaincloudsCore）的加载器与演示 Mod 在首次启动时由 GameManager 自动解压，
        // 这里无需提前下载任何内容。
        if (p.LoaderType == ModLoaderType.RaincloudsCore)
        {
            _vm.AddLog("该实例使用 Rainclouds 自研核心（10yv），首次启动将自动解压加载器与演示 Mod。");
            _vm.StatusMessage = "自研核心已就绪，点击启动即可游玩";
            SaveProfile();
            return;
        }

        _vm.StatusMessage = "正在准备核心模组...";

        var progress = new Progress<string>(s => Dispatcher.UIThread.Post(() =>
        {
            _vm.StatusMessage = s;
            _vm.AddLog(s);
        }));

        try
        {
            var r = await RaincloudsPack.EnsureModsAsync(p, progress);
            _vm.AddLog($"核心模组准备完成：新增 {r.Added}，已有 {r.Skipped}，失败 {r.Failed.Count}");
            if (r.Failed.Count > 0)
                _vm.AddLog("失败项: " + string.Join("、", r.Failed));
            _vm.StatusMessage = "核心已就绪";
            _vm.RefreshMods();
            SaveProfile();
        }
        catch (Exception ex)
        {
            _vm.AddLog("核心准备失败: " + ex.Message);
        }
    }

    private async Task ProvisionCoreAsync()
    {
        var p = _vm.SelectedProfile;
        if (p == null) return;

        _vm.StatusMessage = "正在补齐核心模组...";
        var progress = new Progress<string>(s => Dispatcher.UIThread.Post(() => _vm.AddLog(s)));
        try
        {
            var r = await RaincloudsPack.EnsureModsAsync(p, progress);
            _vm.AddLog($"补齐完成：新增 {r.Added}，已有 {r.Skipped}，失败 {r.Failed.Count}");
            if (r.Failed.Count > 0) _vm.AddLog("失败项: " + string.Join("、", r.Failed));
            _vm.StatusMessage = "核心已就绪";
            _vm.RefreshMods();
            SaveProfile();
        }
        catch (Exception ex)
        {
            _vm.AddLog("补齐失败: " + ex.Message);
        }
    }

    private async Task DeleteProfileAsync()
    {
        var p = _vm.SelectedProfile;
        if (p == null) return;

        var ok = await Dialogs.ConfirmAsync(this, "删除实例",
            $"确定删除实例「{p.Name}」吗？\n\n仅从列表中移除，游戏文件仍保留在：\n{p.ResolvedGameDir}");
        if (!ok) return;

        _vm.Profiles.Remove(p);
        SelectProfile(_vm.Profiles.FirstOrDefault() ?? new Profile());
        _vm.AddLog("已移除实例: " + p.Name);
        SaveProfile();
    }

    // ---------------------------------------------------------------- 编辑器

    private void PopulateEditor()
    {
        var p = _vm.SelectedProfile;
        if (p == null) return;

        TxtName.Text = p.Name;
        CmbVersion.SelectedItem = _vm.VersionOptions.Contains(p.GameVersion) ? p.GameVersion : null;
        CmbLoader.SelectedIndex = (int)p.LoaderType;
        TxtLoaderVersion.Text = p.LoaderVersion ?? "";
        NumMinMem.Value = p.MinMemoryMb ?? 2048;
        NumMaxMem.Value = p.MaxMemoryMb ?? 4096;
        NumWidth.Value = p.WindowWidth ?? 854;
        NumHeight.Value = p.WindowHeight ?? 480;
        ChkFullscreen.IsChecked = p.Fullscreen ?? false;
        TxtJava.Text = p.JavaPath ?? "";
        TxtGameDir.Text = p.GameDirectory ?? "";
        TxtOfflineName.Text = p.OfflineUsername ?? "";
        TxtExtraJvm.Text = p.ExtraJvmArgs ?? "";
        TxtExtraGame.Text = p.ExtraGameArgs ?? "";
    }

    private void SyncEditorToProfile()
    {
        var p = _vm.SelectedProfile;
        if (p == null) return;

        p.Name = string.IsNullOrWhiteSpace(TxtName.Text) ? "未命名实例" : TxtName.Text!.Trim();
        if (CmbVersion.SelectedItem is string v) p.GameVersion = v;
        p.LoaderType = (ModLoaderType)(CmbLoader.SelectedIndex < 0 ? 0 : CmbLoader.SelectedIndex);
        p.LoaderVersion = string.IsNullOrWhiteSpace(TxtLoaderVersion.Text) ? null : TxtLoaderVersion.Text!.Trim();
        p.MinMemoryMb = (int)(NumMinMem.Value ?? 2048);
        p.MaxMemoryMb = (int)(NumMaxMem.Value ?? 4096);
        p.WindowWidth = (NumWidth.Value ?? 0) > 0 ? (int)NumWidth.Value! : null;
        p.WindowHeight = (NumHeight.Value ?? 0) > 0 ? (int)NumHeight.Value! : null;
        p.Fullscreen = ChkFullscreen.IsChecked ?? false;
        p.JavaPath = string.IsNullOrWhiteSpace(TxtJava.Text) ? null : TxtJava.Text;
        p.GameDirectory = string.IsNullOrWhiteSpace(TxtGameDir.Text) ? null : TxtGameDir.Text;
        p.OfflineUsername = string.IsNullOrWhiteSpace(TxtOfflineName.Text) ? null : TxtOfflineName.Text;
        p.ExtraJvmArgs = string.IsNullOrWhiteSpace(TxtExtraJvm.Text) ? null : TxtExtraJvm.Text;
        p.ExtraGameArgs = string.IsNullOrWhiteSpace(TxtExtraGame.Text) ? null : TxtExtraGame.Text;
    }

    private void SaveProfile()
    {
        SyncEditorToProfile();
        _vm.Settings.GameRoot = string.IsNullOrWhiteSpace(TxtGameRoot.Text) ? null : TxtGameRoot.Text;
        _vm.Settings.AzureClientId = string.IsNullOrWhiteSpace(TxtClientId.Text) ? null : TxtClientId.Text!.Trim();
        _vm.Settings.DownloadConcurrency = (int)(NumConcurrency.Value ?? 8);
        _vm.SaveAll();

        _vm.StatusMessage = "已保存";
        _vm.AddLog("设置已保存");
        _vm.RefreshMods();
    }

    // ---------------------------------------------------------------- 账号 / 授权

    private async Task OpenAccountsAsync()
    {
        var dlg = new AccountManagerWindow(_vm);
        await dlg.ShowDialog(this);
        _vm.SaveAll();
        _vm.AddLog("当前账号: " + _vm.AccountLabel);
    }

    private async Task OpenActivationAsync(bool focusModules)
    {
        if (!focusModules && LicenseManager.IsActivated && ModulesLicenseManager.IsActivated)
        {
            await Dialogs.ShowInfoAsync(this, "已解锁",
                "本机已解锁完整版与进阶模块，感谢支持。\n\n完整版：" + LicenseManager.StatusText
                + "\n进阶模块：" + ModulesLicenseManager.StatusText);
            return;
        }

        var dlg = new ActivationWindow();
        await dlg.ShowDialog(this);
        UpdateLicenseUi();
        if (dlg.Unlocked)
        {
            _vm.AddLog("完整版激活成功");
            _vm.StatusMessage = "已解锁";
        }
        if (dlg.ModulesUnlocked)
        {
            _vm.AddLog("进阶模块激活成功，联机/穿透/开服已可用");
        }
    }

    // ---------------------------------------------------------------- 进阶模块

    private async Task OpenNetworkAsync()
    {
        if (!ModulesLicenseManager.IsActivated)
        {
            await OpenActivationAsync(true);
            if (!ModulesLicenseManager.IsActivated) return;
        }
        var w = new NetworkWindow(_vm);
        await w.ShowDialog(this);
    }

    private async Task OpenFrpcAsync()
    {
        if (!ModulesLicenseManager.IsActivated)
        {
            await OpenActivationAsync(true);
            if (!ModulesLicenseManager.IsActivated) return;
        }
        var w = new FrpcWindow(_vm);
        await w.ShowDialog(this);
    }

    private async Task OpenServerAsync()
    {
        if (!ModulesLicenseManager.IsActivated)
        {
            await OpenActivationAsync(true);
            if (!ModulesLicenseManager.IsActivated) return;
        }
        var w = new ServerWindow(_vm);
        await w.ShowDialog(this);
    }

    // ---------------------------------------------------------------- 模组

    private async Task AddModsAsync()
    {
        var p = _vm.SelectedProfile;
        if (p == null) return;

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择模组或插件（可多选）",
            AllowMultiple = true,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("模组 / 插件")
                {
                    Patterns = new[] { "*.jar", "*.zip", "*.litemod" }
                }
            }
        });

        if (files.Count == 0) return;

        var dir = CurrentModsDir();
        var added = 0;
        foreach (var f in files)
        {
            var path = f.TryGetLocalPath();
            if (string.IsNullOrEmpty(path)) continue;
            try
            {
                ContentManager.Import(path, dir);
                added++;
            }
            catch (Exception ex)
            {
                _vm.AddLog("导入失败 " + Path.GetFileName(path) + ": " + ex.Message);
            }
        }

        _vm.AddLog($"已导入 {added} 个文件到 {dir}");
        _vm.RefreshMods();
    }

    private async Task DeleteModAsync()
    {
        if (LstMods.SelectedItem is not ContentItemVm item) return;

        var ok = await Dialogs.ConfirmAsync(this, "删除内容",
            $"确定删除「{item.DisplayName}」吗？\n文件将从磁盘移除，无法恢复。");
        if (!ok) return;

        try
        {
            ContentManager.Delete(item.Item.FullPath);
            _vm.AddLog("已删除 " + item.Item.FileName);
        }
        catch (Exception ex)
        {
            _vm.AddLog("删除失败: " + ex.Message);
        }
        _vm.RefreshMods();
    }

    // ---------------------------------------------------------------- 设置项辅助

    private async Task FetchLatestLoaderAsync()
    {
        try
        {
            var type = (ModLoaderType)(CmbLoader.SelectedIndex < 0 ? 0 : CmbLoader.SelectedIndex);
            string? ver = type switch
            {
                ModLoaderType.Fabric => await FabricInstaller.GetLatestLoaderAsync(),
                ModLoaderType.Quilt => await QuiltInstaller.GetLatestLoaderAsync(),
                _ => null
            };

            if (ver != null)
            {
                TxtLoaderVersion.Text = ver;
                _vm.AddLog("最新加载器版本: " + ver);
            }
            else
            {
                _vm.AddLog("该加载器版本留空时，启动会自动选择兼容版本");
            }
        }
        catch (Exception ex)
        {
            _vm.AddLog("获取加载器版本失败: " + ex.Message);
        }
    }

    private void AutoDetectJava()
    {
        var installed = JavaManager.DetectInstalled();
        var best = JavaManager.FindBest(installed, 17) ?? installed.FirstOrDefault();
        if (best != null)
        {
            TxtJava.Text = best.JavaPath;
            _vm.AddLog($"检测到 Java: {best.JavaPath} (JDK {best.MajorVersion})");
        }
        else
        {
            _vm.AddLog("未检测到 Java，启动时会自动下载 Temurin");
        }
    }

    private async Task BrowseJavaAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择 java.exe",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Java 可执行文件") { Patterns = new[] { "java.exe", "java" } },
                new FilePickerFileType("所有文件") { Patterns = new[] { "*" } }
            }
        });

        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (!string.IsNullOrEmpty(path)) TxtJava.Text = path;
    }

    private async Task BrowseDirAsync()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "选择游戏目录",
            AllowMultiple = false
        });

        var path = folders.FirstOrDefault()?.TryGetLocalPath();
        if (!string.IsNullOrEmpty(path)) TxtGameDir.Text = path;
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
        catch (Exception ex)
        {
            Debug.WriteLine(ex.Message);
        }
    }

    // ---------------------------------------------------------------- 启动

    private async Task PlayAsync()
    {
        var profile = _vm.SelectedProfile;
        if (profile == null)
        {
            _vm.StatusMessage = "请先选择或创建实例";
            return;
        }

        // 付费门槛
        if (!LicenseManager.IsActivated)
        {
            await OpenActivationAsync(false);
            if (!LicenseManager.IsActivated) return;
        }

        SyncEditorToProfile();
        SaveProfile();

        _playCts?.Cancel();
        _playCts = new CancellationTokenSource();
        var ct = _playCts.Token;

        _vm.IsBusy = true;
        BtnPlay.IsEnabled = false;
        TxtLog.IsVisible = true;

        try
        {
            var settings = _vm.Settings;
            settings.JavaPath = profile.JavaPath ?? settings.JavaPath;

            var account = _vm.SelectedAccount?.Account;
            if (account is { IsMicrosoft: true })
            {
                var refreshed = await AccountService.EnsureValidAsync(account, settings.AzureClientId, ct);
                if (!refreshed)
                    _vm.AddLog("账号令牌已失效且无法自动续期，将以离线身份进入游戏");
                else
                    Store.SaveAccounts(_vm.Accounts.Select(a => a.Account).ToList());
            }

            var progress = new Progress<PrepareProgress>(p => Dispatcher.UIThread.Post(() =>
            {
                _vm.StatusMessage = p.Stage;
                _vm.ProgressText = p.Total > 0 ? $"{p.Stage} ({p.Done}/{p.Total})" : p.Stage;
                _vm.ProgressValue = p.Total > 0 ? (double)p.Done / p.Total * 100 : (_vm.ProgressValue > 0 ? 100 : 8);
                _vm.AddLog(p.Stage);
            }));

            var proc = await GameManager.PlayAsync(profile, settings, account, progress, ct);
            _vm.StatusMessage = "游戏已启动 (PID " + proc.Id + ")";
            _vm.AddLog("游戏进程已启动 PID=" + proc.Id);
            _vm.ProgressValue = 0;
            _vm.ProgressText = "";
            _vm.RefreshMods();
        }
        catch (OperationCanceledException)
        {
            _vm.StatusMessage = "已取消";
        }
        catch (Exception ex)
        {
            _vm.StatusMessage = "启动失败: " + ex.Message;
            _vm.AddLog("启动失败: " + ex);
        }
        finally
        {
            _vm.IsBusy = false;
            BtnPlay.IsEnabled = true;
        }
    }
}
