using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using RCL.Core.Content;
using RCL.Core.Licensing;
using RCL.Core.Models;
using RCL.Core.Profiles;
using RCL.Core.Utils;
using RCL.Core.Versions;

namespace RCL.UI.ViewModels;

public class MainViewModel : ViewModelBase
{
    private Profile? _selectedProfile;
    private AccountItemVm? _selectedAccount;
    private Settings _settings = new();
    private List<string> _versionOptions = new();
    private string _statusMessage = "就绪";
    private string _logText = "";
    private bool _isBusy;
    private double _progressValue;
    private string _progressText = "";
    private string _licenseText = "";
    private string _modulesText = "";

    public MainViewModel()
    {
        Profiles = new ObservableCollection<Profile>();
        Accounts = new ObservableCollection<AccountItemVm>();
        Mods = new ObservableCollection<ContentItemVm>();
    }

    public ObservableCollection<Profile> Profiles { get; }

    public ObservableCollection<AccountItemVm> Accounts { get; }

    public ObservableCollection<ContentItemVm> Mods { get; }

    public Profile? SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            _selectedProfile = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasProfile));
            OnPropertyChanged(nameof(ProfileTitle));
            OnPropertyChanged(nameof(ProfileSubtitle));
            OnPropertyChanged(nameof(ProfileIcon));
        }
    }

    public AccountItemVm? SelectedAccount
    {
        get => _selectedAccount;
        set
        {
            _selectedAccount = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(AccountLabel));
            OnPropertyChanged(nameof(AccountTypeLabel));
            OnPropertyChanged(nameof(CurrentHead));
            OnPropertyChanged(nameof(HasCurrentHead));
            OnPropertyChanged(nameof(HasNoCurrentHead));
            OnPropertyChanged(nameof(AccountInitial));
            OnPropertyChanged(nameof(AccountColor));
            OnPropertyChanged(nameof(AccountBrush));
        }
    }

    public Settings Settings
    {
        get => _settings;
        set { _settings = value; OnPropertyChanged(); }
    }

    public List<string> VersionOptions
    {
        get => _versionOptions;
        set { _versionOptions = value; OnPropertyChanged(); }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set { _statusMessage = value; OnPropertyChanged(); }
    }

    public string LogText
    {
        get => _logText;
        set { _logText = value; OnPropertyChanged(); }
    }

    public bool IsBusy
    {
        get => _isBusy;
        set { _isBusy = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanPlay)); }
    }

    public double ProgressValue
    {
        get => _progressValue;
        set { _progressValue = value; OnPropertyChanged(); }
    }

    public string ProgressText
    {
        get => _progressText;
        set { _progressText = value; OnPropertyChanged(); }
    }

    public string LicenseText
    {
        get => _licenseText;
        set { _licenseText = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsLicensed)); }
    }

    public bool IsLicensed => LicenseManager.IsActivated;

    public string ModulesText
    {
        get => _modulesText;
        set { _modulesText = value; OnPropertyChanged(); }
    }

    public bool ModulesUnlocked => ModulesLicenseManager.IsActivated;

    public bool CanPlay => !IsBusy;

    public bool HasProfile => SelectedProfile != null;

    public string ProfileTitle => SelectedProfile?.Name ?? "未选择实例";

    public string ProfileSubtitle => SelectedProfile?.Subtitle ?? "";

    public string ProfileIcon => string.IsNullOrEmpty(SelectedProfile?.Name)
        ? "?"
        : SelectedProfile!.Name[..1].ToUpperInvariant();

    public string AccountLabel => SelectedAccount?.Name ?? "未登录";

    public string AccountTypeLabel => SelectedAccount?.TypeText ?? "离线模式";

    public Avalonia.Media.Imaging.Bitmap? CurrentHead => SelectedAccount?.Head;

    public bool HasCurrentHead => SelectedAccount?.Head != null;

    public bool HasNoCurrentHead => SelectedAccount?.Head == null;

    public string AccountInitial => SelectedAccount?.Initial ?? "?";

    public string AccountColor => SelectedAccount?.ColorHex ?? "#1f6feb";

    public Avalonia.Media.IBrush AccountBrush =>
        Avalonia.Media.SolidColorBrush.Parse(AccountColor);

    public void AddLog(string line)
    {
        var sb = new StringBuilder(_logText);
        sb.AppendLine($"[{DateTime.Now:HH:mm:ss}] {line}");
        // 只保留最近 400 行，避免长时间运行占用内存
        var text = sb.ToString();
        if (text.Length > 60000) text = text[^40000..];
        LogText = text;
    }

    public void LoadAll()
    {
        Settings = Store.LoadSettings();
        if (!string.IsNullOrEmpty(Settings.GameRoot))
            Paths.Root = Settings.GameRoot;
        Paths.EnsureDirectories();

        LicenseManager.EnsureLoaded();
        LicenseText = LicenseManager.StatusText;

        ModulesLicenseManager.EnsureLoaded();
        ModulesText = ModulesLicenseManager.StatusText;

        Accounts.Clear();
        foreach (var acc in Store.LoadAccounts())
            Accounts.Add(new AccountItemVm(acc));

        Profiles.Clear();
        foreach (var p in Store.LoadProfiles())
        {
            Paths.EnsureInstanceDirectories(p.Id);
            Profiles.Add(p);
        }

        SelectedAccount = Accounts.FirstOrDefault(a => a.Account.Id == Settings.CurrentAccountId)
                          ?? Accounts.FirstOrDefault();
        SelectedProfile = Profiles.FirstOrDefault(p => p.Id == Settings.LastProfileId)
                          ?? Profiles.FirstOrDefault();

        _ = LoadHeadsAsync();
    }

    private async Task LoadHeadsAsync()
    {
        foreach (var item in Accounts.ToList())
            await item.LoadHeadAsync();
        OnPropertyChanged(nameof(CurrentHead));
        OnPropertyChanged(nameof(HasCurrentHead));
        OnPropertyChanged(nameof(HasNoCurrentHead));
    }

    public void SaveAll()
    {
        if (!string.IsNullOrEmpty(Settings.GameRoot))
            Paths.Root = Settings.GameRoot;
        Settings.CurrentAccountId = SelectedAccount?.Account.Id;
        Settings.LastProfileId = SelectedProfile?.Id;
        Store.SaveSettings(Settings);
        Store.SaveAccounts(Accounts.Select(a => a.Account).ToList());
        Store.SaveProfiles(Profiles.ToList());
    }

    public async Task LoadVersionListAsync()
    {
        try
        {
            var manifest = await VersionManifestFetcher.FetchAsync();
            var list = manifest.Versions
                .OrderByDescending(v => v.ReleaseTime)
                .Select(v => v.Id)
                .ToList();
            list.Insert(0, "latest_release");
            list.Insert(1, "latest_snapshot");
            VersionOptions = list;
        }
        catch (Exception ex)
        {
            AddLog("加载版本列表失败: " + ex.Message);
        }
    }

    /// <summary>扫描当前实例的 mods 目录。</summary>
    public void RefreshMods()
    {
        Mods.Clear();
        var p = SelectedProfile;
        if (p == null) return;

        var dir = p.ResolvedModsDir;
        Directory.CreateDirectory(dir);

        foreach (var item in ContentManager.Scan(dir))
        {
            var vm = new ContentItemVm(item, OnModToggled);
            Mods.Add(vm);
        }

        OnPropertyChanged(nameof(ModSummary));
    }

    private void OnModToggled(ContentItemVm vm, bool enabled)
    {
        try
        {
            var newPath = ContentManager.SetEnabled(vm.Item.FullPath, enabled);
            vm.Item.Enabled = enabled;
            vm.Item.FullPath = newPath;
            AddLog((enabled ? "已启用 " : "已禁用 ") + vm.Item.FileName);
        }
        catch (Exception ex)
        {
            AddLog("切换失败: " + ex.Message);
            vm.Enabled = !enabled; // 回滚勾选状态
        }
    }

    public string ModSummary
    {
        get
        {
            var enabled = Mods.Count(m => m.Enabled);
            return $"{enabled} 个启用 / 共 {Mods.Count} 个";
        }
    }
}
