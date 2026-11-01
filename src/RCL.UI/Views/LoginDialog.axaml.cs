using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media;
using RCL.Core.Auth;
using RCL.Core.Models;

namespace RCL.UI.Views;

public partial class LoginDialog : Window
{
    private MicrosoftAuthenticator? _auth;
    private readonly CancellationTokenSource _cts = new();
    private bool _running;

    public Account? Result { get; private set; }

    public LoginDialog() : this("")
    {
    }

    public LoginDialog(string clientId)
    {
        InitializeComponent();
        ClientIdBox.Text = clientId ?? "";

        CancelBtn.Click += (_, _) => { _cts.Cancel(); Close(); };
        OkBtn.Click += (_, _) => Close();
        StartBtn.Click += async (_, _) => await StartFlowAsync();
        HelpBtn.Click += (_, _) => OpenHelp();
        CopyBtn.Click += (_, _) => { try { Clipboard?.SetTextAsync(CodeBox.Text); } catch { } };
        OpenUrlBtn.Click += (_, _) => OpenUrl(UrlBox.Text);

        Opened += async (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(ClientIdBox.Text))
                await StartFlowAsync();
            else
            {
                StatusBox.Text = "请先填写 ClientId，再点击“开始登录”。";
                StatusBox.Foreground = Brushes.OrangeRed;
            }
        };
    }

    private async Task StartFlowAsync()
    {
        if (_running) return;

        var cid = (ClientIdBox.Text ?? "").Trim();
        if (cid.Length == 0)
        {
            StatusBox.Text = "ClientId 不能为空。点击“如何获取”查看注册方法。";
            StatusBox.Foreground = Brushes.OrangeRed;
            return;
        }

        _running = true;
        StartBtn.IsEnabled = false;
        StatusBox.Foreground = Brushes.DimGray;
        StatusBox.Text = "正在请求设备码…";

        try
        {
            _auth = new MicrosoftAuthenticator(cid);
            var device = await _auth.RequestDeviceCodeAsync(_cts.Token);
            UrlBox.Text = device.VerificationUrl;
            CodeBox.Text = device.UserCode;
            CodePanel.IsVisible = true;
            StatusBox.Text = "已生成用户代码，请在浏览器中完成登录…";

            var token = await _auth.PollForTokenAsync(device, _cts.Token);
            if (token.Error != null)
            {
                Fail("登录失败：" + TranslateError(token.Error, token.ErrorDescription));
                return;
            }

            StatusBox.Text = "已获取令牌，正在登录 Minecraft…";
            var account = await _auth.LoginAsync(token, _cts.Token);

            if (string.IsNullOrEmpty(account.Username))
            {
                var ok = await _auth.HasEntitlementAsync(account, _cts.Token);
                if (!ok)
                {
                    Fail("该微软账号未购买 Minecraft，无法在线游玩。可改用离线模式（填写离线用户名后直接启动）。");
                    return;
                }
            }

            Result = account;
            StatusBox.Text = "登录成功：" + (account.Username ?? "未知");
            StatusBox.Foreground = Brushes.Green;
            OkBtn.IsVisible = true;
            OkBtn.IsEnabled = true;
        }
        catch (OperationCanceledException)
        {
            // 用户取消
        }
        catch (Exception ex)
        {
            Fail(ex.Message);
        }
        finally
        {
            _running = false;
            StartBtn.IsEnabled = true;
        }
    }

    private void Fail(string msg)
    {
        StatusBox.Text = msg;
        StatusBox.Foreground = Brushes.Red;
        HintBox.IsVisible = true;
        HintBox.Text = "若提示“未找到应用 / AADSTS700016”，说明 ClientId 无效：请确认复制完整，或重新注册一个 Azure 应用。";
    }

    private static string TranslateError(string code, string? desc)
    {
        return code switch
        {
            "expired_token" => "用户代码已过期，请重新点击“开始登录”。",
            "authorization_declined" => "你在浏览器中拒绝了授权。",
            "bad_verification_code" => "用户代码不正确，请重新登录。",
            "timeout" => "等待超时，请重新点击“开始登录”。",
            _ => string.IsNullOrWhiteSpace(desc) ? code : $"{code}（{desc}）"
        };
    }

    private void OpenHelp()
    {
        OpenUrl("https://portal.azure.com/#view/Microsoft_AAD_RegisteredApps/ApplicationsListBlade");
        StatusBox.Foreground = Brushes.DimGray;
        StatusBox.Text = "已打开 Azure 门户：新建注册 → 账户类型选“仅个人 Microsoft 账户” → 在“身份验证”里启用“允许公共客户端流” → 复制应用程序(客户端) ID。";
    }

    private static void OpenUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
    }
}
