using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using RCL.Core.Accounts;
using RCL.Core.Models;

namespace RCL.UI.ViewModels;

/// <summary>账号列表中的一项（含皮肤头像）。</summary>
public class AccountItemVm : ViewModelBase
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };

    private Bitmap? _head;

    public AccountItemVm(Account account)
    {
        Account = account;
    }

    public Account Account { get; }

    public string Name => Account.DisplayName;

    public string TypeText => Account.TypeLabel;

    public string Initial => string.IsNullOrEmpty(Account.DisplayName)
        ? "?"
        : Account.DisplayName[..1].ToUpperInvariant();

    /// <summary>无头像时的底色，按名字散列，保证每个账号颜色稳定。</summary>
    public string ColorHex
    {
        get
        {
            var palette = new[]
            {
                "#1f6feb", "#2ea043", "#a371f7", "#db6d28",
                "#e5534b", "#1b7c83", "#bf8700", "#8250df"
            };
            var hash = 0;
            foreach (var c in Account.DisplayName) hash = (hash * 31 + c) & 0x7fffffff;
            return palette[hash % palette.Length];
        }
    }

    public Bitmap? Head
    {
        get => _head;
        private set
        {
            _head = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasHead));
            OnPropertyChanged(nameof(HasNoHead));
        }
    }

    public bool HasHead => _head != null;
    public bool HasNoHead => _head == null;

    /// <summary>无头像时的底色画刷。</summary>
    public Avalonia.Media.IBrush ColorBrush => Avalonia.Media.SolidColorBrush.Parse(ColorHex);

    public string UuidText => string.IsNullOrWhiteSpace(Account.Uuid) ? "—" : Account.Uuid!;

    public string DetailText => Account.IsMicrosoft
        ? (Account.IsValid ? "令牌有效" : Account.CanRefresh ? "令牌已过期（启动时自动续期）" : "需要重新登录")
        : "本地离线账号";

    /// <summary>异步加载皮肤头像，失败则保留字母头像。</summary>
    public async Task LoadHeadAsync()
    {
        var url = AccountService.AvatarUrl(Account);
        if (string.IsNullOrWhiteSpace(url)) return;
        try
        {
            var bytes = await Http.GetByteArrayAsync(url);
            if (bytes.Length < 64) return;
            using var ms = new MemoryStream(bytes);
            var bmp = new Bitmap(ms);
            await Dispatcher.UIThread.InvokeAsync(() => Head = bmp);
        }
        catch
        {
            // 网络不可用时静默回退到字母头像
        }
    }
}
