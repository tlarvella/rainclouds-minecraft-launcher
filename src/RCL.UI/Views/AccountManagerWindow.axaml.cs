using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using RCL.Core.Accounts;
using RCL.Core.Profiles;
using RCL.UI.ViewModels;

namespace RCL.UI.Views;

public partial class AccountManagerWindow : Window
{
    private readonly MainViewModel _vm;

    public AccountManagerWindow() : this(new MainViewModel())
    {
    }

    public AccountManagerWindow(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;

        LstAccounts.SelectedItem = vm.SelectedAccount;
        LstAccounts.SelectionChanged += (_, _) =>
        {
            if (LstAccounts.SelectedItem is AccountItemVm item)
                _vm.SelectedAccount = item;
        };

        BtnAddMs.Click += async (_, _) => await AddMicrosoftAsync();
        BtnAddOffline.Click += (_, _) => ShowOfflinePanel(true);
        BtnOfflineOk.Click += (_, _) => CreateOffline();
        BtnDelete.Click += (_, _) => DeleteSelected();
        BtnSetCurrent.Click += (_, _) => SetCurrent();
        BtnClose.Click += (_, _) => Close();

        Closing += (_, _) => Persist();
    }

    private void Persist()
    {
        _vm.Settings.CurrentAccountId = _vm.SelectedAccount?.Account.Id;
        Store.SaveSettings(_vm.Settings);
        Store.SaveAccounts(_vm.Accounts.Select(a => a.Account).ToList());
    }

    private void ShowOfflinePanel(bool show)
    {
        OfflinePanel.IsVisible = show;
        OfflineHint.Text = "";
        if (show) TxtOfflineName.Focus();
    }

    private void CreateOffline()
    {
        var name = (TxtOfflineName.Text ?? "").Trim();
        if (!Regex.IsMatch(name, "^[A-Za-z0-9_]{3,16}$"))
        {
            OfflineHint.Text = "玩家名需为 3-16 位字母、数字或下划线。";
            OfflineHint.Foreground = Brushes.OrangeRed;
            return;
        }

        if (_vm.Accounts.Any(a => string.Equals(a.Account.Username, name, StringComparison.OrdinalIgnoreCase)))
        {
            OfflineHint.Text = "该玩家名已存在。";
            OfflineHint.Foreground = Brushes.OrangeRed;
            return;
        }

        var account = AccountService.CreateOffline(name);
        var item = new AccountItemVm(account);
        _vm.Accounts.Add(item);
        _vm.SelectedAccount = item;
        LstAccounts.SelectedItem = item;
        TxtOfflineName.Text = "";
        ShowOfflinePanel(false);
    }

    private async System.Threading.Tasks.Task AddMicrosoftAsync()
    {
        var dlg = new LoginDialog(_vm.Settings.AzureClientId ?? "");
        await dlg.ShowDialog(this);
        if (dlg.Result == null) return;

        var account = dlg.Result;
        var existing = _vm.Accounts.FirstOrDefault(a =>
            !string.IsNullOrEmpty(a.Account.Uuid) && a.Account.Uuid == account.Uuid);
        if (existing != null)
        {
            _vm.Accounts.Remove(existing);
        }

        var item = new AccountItemVm(account);
        _vm.Accounts.Add(item);
        _vm.SelectedAccount = item;
        LstAccounts.SelectedItem = item;
        _ = item.LoadHeadAsync();
        Persist();
    }

    private void DeleteSelected()
    {
        var item = _vm.SelectedAccount;
        if (item == null) return;
        _vm.Accounts.Remove(item);
        _vm.SelectedAccount = _vm.Accounts.FirstOrDefault();
        LstAccounts.SelectedItem = _vm.SelectedAccount;
        Persist();
    }

    private void SetCurrent()
    {
        if (_vm.SelectedAccount == null) return;
        Persist();
        BtnSetCurrent.Content = "已设为当前";
    }
}
