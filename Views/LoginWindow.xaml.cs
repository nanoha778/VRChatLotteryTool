using Microsoft.Extensions.DependencyInjection;
using System.IO;
using System.Windows;
using System.Windows.Input;
using VRChatLotteryTool.Core;
using VRChatLotteryTool.Core.Services;

namespace VRChatLotteryTool.Views;

public partial class LoginWindow : Window
{
    private readonly IVRChatApiClient _api;
    private readonly ICredentialStore _store;

    private static readonly string CookiePath = AppPaths.AuthCookie;

    private TwoFactorType _pendingTwoFactorType = TwoFactorType.None;

    public bool LoginSucceeded { get; private set; }

    public LoginWindow()
    {
        InitializeComponent();
        _api   = App.Services.GetRequiredService<IVRChatApiClient>();
        _store = App.Services.GetRequiredService<ICredentialStore>();

        // 保存済み認証情報を自動入力
        var saved = _store.Load();
        if (saved != null)
        {
            UsernameBox.Text        = saved.Value.username;
            PasswordBox.Password    = saved.Value.password;
            RememberCheck.IsChecked = true;
        }

        Loaded += (_, _) => UsernameBox.Focus();
    }

    private async void LoginButtonClick(object sender, RoutedEventArgs e)
        => await ExecuteAsync();

    private async void InputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) await ExecuteAsync();
    }

    private async Task ExecuteAsync()
    {
        if (_pendingTwoFactorType != TwoFactorType.None)
        {
            await Verify2FAAsync();
            return;
        }
        await DoLoginAsync();
    }

    // ── Step 1: ユーザー名・パスワードでログイン ─────────────────────────

    private async Task DoLoginAsync()
    {
        var username = UsernameBox.Text.Trim();
        var password = PasswordBox.Password;

        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        {
            ShowError("ユーザー名とパスワードを入力してください。");
            return;
        }

        SetBusy(true, "VRChat にログイン中...");

        LoginResult result;
        try
        {
            result = await _api.LoginAsync(username, password);
        }
        catch (Exception ex)
        {
            SetBusy(false);
            ShowError($"通信エラー: {ex.Message}");
            return;
        }

        SetBusy(false);

        if (!result.Success)
        {
            ShowError(result.Message);
            return;
        }

        if (result.TwoFactorRequired != TwoFactorType.None)
        {
            _pendingTwoFactorType = result.TwoFactorRequired;
            _pendingUsername      = username;
            _pendingPassword      = password;
            ShowTwoFaPanel(result.TwoFactorRequired);
            return;
        }

        // 2FA 不要でログイン成功
        SaveCredentialsIfNeeded(username, password);
        _api.SaveAuthCookie(CookiePath);
        CompleteLogin();
    }

    private string _pendingUsername = string.Empty;
    private string _pendingPassword = string.Empty;

    // ── Step 2: 2FA コード検証 ───────────────────────────────────────────

    private async Task Verify2FAAsync()
    {
        var code = TwoFaCodeBox.Text.Trim().Replace(" ", "");

        if (code.Length != 6 || !code.All(char.IsDigit))
        {
            ShowError("6桁の数字コードを入力してください。");
            return;
        }

        SetBusy(true, "2FA コードを確認中...");
        var (success, message) = await _api.VerifyTwoFactorAsync(code, _pendingTwoFactorType);
        SetBusy(false);

        if (!success)
        {
            ShowError(message);
            TwoFaCodeBox.Clear();
            TwoFaCodeBox.Focus();
            return;
        }

        // 2FA 成功
        SaveCredentialsIfNeeded(_pendingUsername, _pendingPassword);
        _api.SaveAuthCookie(CookiePath);
        CompleteLogin();
    }

    // ── UI ヘルパー ──────────────────────────────────────────────────────

    private void ShowTwoFaPanel(TwoFactorType type)
    {
        LoginPanel.Visibility  = Visibility.Collapsed;
        TwoFaPanel.Visibility  = Visibility.Visible;
        LoginButton.Content    = "認証する";
        Height = 380;

        TwoFaHintText.Text = type == TwoFactorType.Totp
            ? "認証アプリ（Google Authenticator / Authy 等）に表示されている\n6桁のコードを入力してください。"
            : "VRChat に登録したメールアドレスへ送信された\n6桁のコードを入力してください。";

        HideError();
        TwoFaCodeBox.Focus();
    }

    private void SaveCredentialsIfNeeded(string username, string password)
    {
        if (RememberCheck.IsChecked == true)
            _store.Save(username, password);
        else
            _store.Clear();
    }

    private void CompleteLogin()
    {
        LoginSucceeded = true;
        Close();
    }

    private void ShowError(string msg)
    {
        ErrorText.Text       = msg;
        ErrorText.Visibility = Visibility.Visible;
    }

    private void HideError() => ErrorText.Visibility = Visibility.Collapsed;

    private void SetBusy(bool busy, string? msg = null)
    {
        LoginButton.IsEnabled  = !busy;
        UsernameBox.IsEnabled  = !busy && _pendingTwoFactorType == TwoFactorType.None;
        PasswordBox.IsEnabled  = !busy && _pendingTwoFactorType == TwoFactorType.None;
        TwoFaCodeBox.IsEnabled = !busy;
        StatusText.Text        = msg ?? string.Empty;
        HideError();
    }
}
