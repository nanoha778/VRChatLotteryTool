using Microsoft.Extensions.DependencyInjection;
using System.IO;
using System.Windows;
using System.Windows.Input;
using VRChatLotteryTool.Core;
using VRChatLotteryTool.Core.Services;
using VRChatLotteryTool.UI.ViewModels;

namespace VRChatLotteryTool.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel        _vm;
    private readonly IVRChatApiClient     _api;
    private readonly ICredentialStore     _store;
    private readonly IVRChatNotificationService _notification;

    private static readonly string CookiePath = AppPaths.AuthCookie;

    private TwoFactorType _pendingTwoFaType = TwoFactorType.None;
    private string        _pendingUsername  = string.Empty;
    private string        _pendingPassword  = string.Empty;

    public MainWindow()
    {
        InitializeComponent();

        _vm           = App.Services.GetRequiredService<MainViewModel>();
        _api          = App.Services.GetRequiredService<IVRChatApiClient>();
        _store        = App.Services.GetRequiredService<ICredentialStore>();
        _notification = App.Services.GetRequiredService<IVRChatNotificationService>();

        DataContext = _vm;

        _vm.Log.LogAdded += (_, _) =>
            Dispatcher.InvokeAsync(() =>
            {
                if (LogListView.Items.Count > 0)
                    LogListView.ScrollIntoView(LogListView.Items[0]);
            });

        // 保存済み認証情報を自動入力
        var saved = _store.Load();
        if (saved != null)
        {
            UsernameBox.Text        = saved.Value.username;
            PasswordBox.Password    = saved.Value.password;
            RememberCheck.IsChecked = true;
        }

        // ウィンドウ表示後にログイン処理を開始
        Loaded += OnLoaded;
    }

    // ── 起動時ログイン処理 ────────────────────────────────────────────

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        // 保存済み Cookie があれば自動ログインを試みる
        if (_api.LoadAuthCookie(CookiePath))
        {
            SetLoginBusy(true, "セッション復元中...");
            bool ok = await _api.VerifySessionAsync();
            SetLoginBusy(false);

            if (ok)
            {
                // Cookie が有効 → そのまま自動ログイン完了
                CompleteLogin();
                return;
            }
            // Cookie が無効だった場合はフォームを表示して待機
            SetLoginStatus(string.Empty);
        }

        // Cookie がない / 無効 → フォームを表示してユーザーの操作を待つ
        UsernameBox.Focus();
    }

    // ── ボタン・キーイベント ──────────────────────────────────────────

    private async void LoginButtonClick(object sender, RoutedEventArgs e) => await ExecuteLoginAsync();
    private async void LoginInputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) await ExecuteLoginAsync();
    }

    private async Task ExecuteLoginAsync()
    {
        if (_pendingTwoFaType != TwoFactorType.None) { await Verify2FAAsync(); return; }
        await DoLoginAsync();
    }

    private async Task DoLoginAsync()
    {
        var username = UsernameBox.Text.Trim();
        var password = PasswordBox.Password;
        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        { ShowLoginError("ユーザー名とパスワードを入力してください。"); return; }

        SetLoginBusy(true, "ログイン中...");

        LoginResult result;
        try   { result = await _api.LoginAsync(username, password); }
        catch (Exception ex) { SetLoginBusy(false); ShowLoginError($"通信エラー: {ex.Message}"); return; }

        SetLoginBusy(false);

        if (!result.Success) { ShowLoginError(result.Message); return; }

        if (result.TwoFactorRequired != TwoFactorType.None)
        {
            _pendingTwoFaType = result.TwoFactorRequired;
            _pendingUsername  = username;
            _pendingPassword  = password;
            ShowTwoFaPanel(result.TwoFactorRequired);
            return;
        }

        SaveCredentials(username, password);
        _api.SaveAuthCookie(CookiePath);
        CompleteLogin();
    }

    private async Task Verify2FAAsync()
    {
        var code = TwoFaCodeBox.Text.Trim().Replace(" ", "");
        if (code.Length != 6 || !code.All(char.IsDigit))
        { ShowLoginError("6桁の数字コードを入力してください。"); return; }

        SetLoginBusy(true, "2FA コード確認中...");
        var (success, message) = await _api.VerifyTwoFactorAsync(code, _pendingTwoFaType);
        SetLoginBusy(false);

        if (!success) { ShowLoginError(message); TwoFaCodeBox.Clear(); TwoFaCodeBox.Focus(); return; }

        SaveCredentials(_pendingUsername, _pendingPassword);
        _api.SaveAuthCookie(CookiePath);
        CompleteLogin();
    }

    private void CompleteLogin()
    {
        // ViewModel にログインユーザー名を設定
        _vm.LoginUserName = _api.CurrentUser?.DisplayName ?? string.Empty;

        // Pipeline 接続（バックグラウンド、失敗してもアプリ継続）
        _ = Task.Run(() =>
        {
            try   { _notification.Start(); _vm.Log.Info("Pipeline 接続を開始しました。"); }
            catch (Exception ex) { _vm.Log.Warning($"Pipeline 接続に失敗しました: {ex.Message}"); }
        });

        // オーバーレイを非表示、メインコンテンツを有効化
        LoginOverlay.Visibility = Visibility.Collapsed;
        MainContent.IsEnabled   = true;
        MainContent.Opacity     = 1.0;
    }

    // ── UI ヘルパー ───────────────────────────────────────────────────

    private void ShowTwoFaPanel(TwoFactorType type)
    {
        LoginFormPanel.Visibility = Visibility.Collapsed;
        TwoFaPanel.Visibility     = Visibility.Visible;
        LoginButton.Content       = "認証する";
        TwoFaHintText.Text = type == TwoFactorType.Totp
            ? "認証アプリ（Google Authenticator 等）の\n6桁コードを入力してください。"
            : "メールアドレスへ送信された\n6桁コードを入力してください。";
        HideLoginError();
        TwoFaCodeBox.Focus();
    }

    private void BackToLoginClick(object sender, RoutedEventArgs e)
    {
        _pendingTwoFaType = TwoFactorType.None;
        _pendingUsername  = string.Empty;
        _pendingPassword  = string.Empty;

        TwoFaCodeBox.Clear();
        TwoFaPanel.Visibility     = Visibility.Collapsed;
        LoginFormPanel.Visibility = Visibility.Visible;
        LoginButton.Content       = "ログイン";
        HideLoginError();
        SetLoginStatus(string.Empty);
        UsernameBox.Focus();
    }

    private void SaveCredentials(string u, string p)
    {
        if (RememberCheck.IsChecked == true) _store.Save(u, p); else _store.Clear();
    }

    private void ShowLoginError(string msg)
    {
        LoginErrorText.Text       = msg;
        LoginErrorText.Visibility = Visibility.Visible;
    }

    private void HideLoginError() => LoginErrorText.Visibility = Visibility.Collapsed;

    private void SetLoginStatus(string msg) => LoginStatusText.Text = msg;

    private void SetLoginBusy(bool busy, string? msg = null)
    {
        LoginButton.IsEnabled  = !busy;
        UsernameBox.IsEnabled  = !busy && _pendingTwoFaType == TwoFactorType.None;
        PasswordBox.IsEnabled  = !busy && _pendingTwoFaType == TwoFactorType.None;
        TwoFaCodeBox.IsEnabled = !busy;
        LoginStatusText.Text   = msg ?? string.Empty;
        HideLoginError();
    }

    // ── メインウィンドウのイベント ────────────────────────────────────

    private void IncrementWinnerCount(object sender, RoutedEventArgs e)
    { if (_vm.WinnerCount < 20) _vm.WinnerCount++; }

    private void DecrementWinnerCount(object sender, RoutedEventArgs e)
    { if (_vm.WinnerCount > 1) _vm.WinnerCount--; }

    private void ClearLog(object sender, RoutedEventArgs e) => _vm.Log.Entries.Clear();

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        Application.Current.Shutdown();
    }
}
