using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using RCL.Core.Tools;
using RCL.UI.ViewModels;

namespace RCL.UI.Views;

public partial class NetworkWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly ObservableCollection<ServerFavorite> _items = new();
    private CancellationTokenSource? _scanCts;

    public NetworkWindow(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;

        foreach (var s in NetworkStore.Load())
            _items.Add(s);
        LstServers.ItemsSource = _items;

        BtnAdd.Click += (_, _) => AddServer();
        BtnDelete.Click += (_, _) => DeleteSelected();
        BtnCopy.Click += (_, _) => CopySelected();
        BtnScan.Click += (_, _) => _ = ScanAsync();
        Closed += (_, _) => { _scanCts?.Cancel(); Save(); };
    }

    private void AddServer()
    {
        var name = string.IsNullOrWhiteSpace(TxtName.Text) ? TxtHost.Text ?? "服务器" : TxtName.Text!.Trim();
        var host = TxtHost.Text?.Trim();
        if (string.IsNullOrWhiteSpace(host))
        {
            TxtScan.Text = "请填写地址";
            return;
        }
        _items.Add(new ServerFavorite
        {
            Name = name,
            Host = host,
            Port = (int)(NumPort.Value ?? 25565)
        });
        TxtName.Text = "";
        TxtHost.Text = "";
        TxtScan.Text = "已添加";
        Save();
    }

    private void DeleteSelected()
    {
        if (LstServers.SelectedItem is ServerFavorite s)
        {
            _items.Remove(s);
            Save();
        }
    }

    private async void CopySelected()
    {
        if (LstServers.SelectedItem is ServerFavorite s)
        {
            try { await Clipboard!.SetTextAsync(NetworkStore.JoinInfoText(s)); TxtScan.Text = "已复制"; }
            catch { TxtScan.Text = "复制失败"; }
        }
    }

    private async Task ScanAsync()
    {
        _scanCts?.Cancel();
        _scanCts = new CancellationTokenSource();
        BtnScan.IsEnabled = false;
        TxtScan.Text = "扫描中...";
        try
        {
            var found = await NetworkStore.ScanLanAsync(_scanCts.Token);
            if (found.Count == 0)
            {
                TxtScan.Text = "未发现局域网服务端（请确认服务端已启动且在同网段）";
            }
            else
            {
                foreach (var ip in found)
                    if (!_items.Any(x => x.Host == ip))
                        _items.Add(new ServerFavorite { Name = "LAN " + ip, Host = ip, Port = 25565 });
                TxtScan.Text = $"发现 {found.Count} 个，已加入列表";
                Save();
            }
        }
        catch
        {
            TxtScan.Text = "扫描出错";
        }
        finally
        {
            BtnScan.IsEnabled = true;
        }
    }

    private void Save()
    {
        NetworkStore.Save(new List<ServerFavorite>(_items));
    }
}
