using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using VRChatLotteryTool.Core;
using VRChatLotteryTool.Core.Models;
using VRChatLotteryTool.Core.Services;
using VRChatLotteryTool.Data.Repositories;

namespace VRChatLotteryTool.UI.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IServiceProvider        _sp;
    private readonly ISchedulerService       _scheduler;
    private readonly ISessionStateService    _state;
    private readonly IVRChatApiClient        _api;
    public  ILogService                      Log { get; }
    public  IVRChatNotificationService       NotificationService { get; }

    private LotterySession? _activeSession;

    private static readonly string CookiePath = AppPaths.AuthCookie;

    // ── 設定 ──────────────────────────────────────────────
    [ObservableProperty] private int    _winnerCount       = 1;
    [ObservableProperty] private bool   _isReliefMode      = false;
    [ObservableProperty] private string _receptionStartTime = "21:00";
    [ObservableProperty] private string _receptionEndTime   = "22:00";
    [ObservableProperty] private string _replyTime          = "22:05";

    // ── 状態表示 ───────────────────────────────────────────
    [ObservableProperty] private string _statusText      = "待機中";
    [ObservableProperty] private int    _entryCount;
    [ObservableProperty] private string _nextReplyTime   = "--:--:--";
    [ObservableProperty] private bool   _canDraw;
    [ObservableProperty] private bool   _canStartSession  = true;
    [ObservableProperty] private bool   _canStopSession   = false;   // セッション停止ボタン用
    [ObservableProperty] private string _modeLabel        = "公平";
    [ObservableProperty] private string _loginUserName    = string.Empty;
    [ObservableProperty] private string _currentWorldName   = "--";
    [ObservableProperty] private string _currentInstanceId  = string.Empty;
    [ObservableProperty] private string _currentTime        = string.Empty;

    private Timer? _instancePollingTimer;
    private Timer? _clockTimer;

    public ObservableCollection<LogEntry>    LogEntries => Log.Entries;
    public ObservableCollection<EntryViewModel> Entries { get; } = [];

    public MainViewModel(
        IServiceProvider sp,
        ISchedulerService scheduler,
        ISessionStateService state,
        ILogService log,
        IVRChatNotificationService notificationService,
        IVRChatApiClient api)
    {
        _sp      = sp;
        _scheduler = scheduler;
        _state   = state;
        Log      = log;
        NotificationService = notificationService;
        _api     = api;

        _scheduler.ReceptionStarted += (_, _) => OnReceptionStarted();
        _scheduler.ReceptionEnded   += (_, _) => OnReceptionEnded();
        _scheduler.ReplyTimeReached += async (_, _) => await OnReplyTimeReachedAsync();

        notificationService.RequestInviteReceived += OnRequestInviteReceived;
        // Start() はログイン完了後に App.xaml.cs から呼ぶ

        // ログイン済みユーザー名を表示
        LoginUserName = api.CurrentUser?.DisplayName ?? string.Empty;

        // 保存済み設定を読み込む
        var s = AppSettings.Load();
        _winnerCount        = s.WinnerCount;
        _isReliefMode       = s.Mode == LotteryMode.Relief;
        _receptionStartTime = s.ReceptionStartTime;
        _receptionEndTime   = s.ReceptionEndTime;
        _replyTime          = s.ReplyTime;
        ModeLabel = _isReliefMode ? "救済" : "公平";

        // インスタンス情報を30秒ごとに更新
        StartInstancePolling();
    }

    // ── インスタンス情報ポーリング ────────────────────────
    private void StartInstancePolling()
    {
        // 現在時刻タイマー（1秒ごと）
        _clockTimer = new Timer(
            _ => System.Windows.Application.Current?.Dispatcher.Invoke(
                () => CurrentTime = DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss")),
            null, TimeSpan.Zero, TimeSpan.FromSeconds(1));

        // インスタンス情報（起動直後 + 30秒ごと）
        _ = Task.Run(UpdateInstanceInfoAsync);
        _instancePollingTimer = new Timer(
            async _ => await UpdateInstanceInfoAsync(),
            null,
            TimeSpan.FromSeconds(30),
            TimeSpan.FromSeconds(30));
    }

    private async Task UpdateInstanceInfoAsync()
    {
        try
        {
            if (!_api.IsLoggedIn) return;

            var location = await _api.GetLocationAsync();

            string worldName, instanceId;
            if (string.IsNullOrEmpty(location)
                || location.Equals("offline",   StringComparison.OrdinalIgnoreCase)
                || location.Equals("traveling", StringComparison.OrdinalIgnoreCase))
            {
                worldName  = "オフライン";
                instanceId = string.Empty;
            }
            else if (location.Equals("private", StringComparison.OrdinalIgnoreCase))
            {
                worldName  = "プライベート";
                instanceId = location;
            }
            else
            {
                // location = "wrld_xxx:12345~hidden(...)~region(jp)"
                var colonIdx = location.IndexOf(':');
                var worldId  = colonIdx > 0 ? location[..colonIdx]        : location;
                instanceId   = colonIdx > 0 ? location[(colonIdx + 1)..] : location;

                // GET /worlds/{worldId} でワールド名を取得
                var name = await _api.GetWorldNameAsync(worldId);
                worldName = name ?? worldId;  // 取得失敗時は worldId をそのまま表示
            }

            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                CurrentWorldName  = worldName;
                CurrentInstanceId = location ?? string.Empty;  // ToolTip にフル文字列
            });
        }
        catch (Exception ex)
        {
            Log.Warning($"[Instance] 取得失敗: {ex.Message}");
        }
    }

    // ── 設定変更時に自動保存 ──────────────────────────────
    private void SaveSettings() =>
        new AppSettings
        {
            WinnerCount        = WinnerCount,
            Mode               = IsReliefMode ? LotteryMode.Relief : LotteryMode.Fair,
            ReceptionStartTime = ReceptionStartTime,
            ReceptionEndTime   = ReceptionEndTime,
            ReplyTime          = ReplyTime
        }.Save();

    partial void OnWinnerCountChanged(int value)           => SaveSettings();
    partial void OnIsReliefModeChanged(bool value)
    {
        ModeLabel = value ? "救済" : "公平";
        SaveSettings();
    }
    partial void OnReceptionStartTimeChanged(string value) => SaveSettings();
    partial void OnReceptionEndTimeChanged(string value)   => SaveSettings();
    partial void OnReplyTimeChanged(string value)          => SaveSettings();

    // ── セッション開始 ────────────────────────────────────
    [RelayCommand]
    private async Task StartSessionAsync()
    {
        if (!TryParseSettings(out var startSpan, out var endSpan, out var replySpan))
            return;

        if (startSpan >= endSpan)
        {
            Log.Warning("受付開始時間は終了時間より前に設定してください。");
            return;
        }

        var today = DateTime.Now.Date;
        var session = new LotterySession
        {
            ReceptionStartAt = today + startSpan,
            ReceptionEndAt   = today + endSpan,
            ReplyAt          = today + replySpan,
            WinnerCount      = WinnerCount,
            Mode             = IsReliefMode ? LotteryMode.Relief : LotteryMode.Fair,
            Status           = SessionStatus.BeforeReception
        };

        using var scope = _sp.CreateScope();
        var sessionRepo = scope.ServiceProvider.GetRequiredService<ILotterySessionRepository>();
        _activeSession = await sessionRepo.CreateAsync(session);

        _state.SetSession(_activeSession);
        CanStartSession = false;
        CanStopSession  = true;
        NextReplyTime   = _activeSession.ReplyAt.ToString("HH:mm:ss");
        UpdateStatusText(SessionStatus.BeforeReception);

        _scheduler.Start(_activeSession);
        Log.Info($"セッションを作成しました。返信予定: {_activeSession.ReplyAt:HH:mm:ss}");
    }

    // ── セッション停止 ────────────────────────────────────
    [RelayCommand]
    private async Task StopSessionAsync()
    {
        if (_activeSession == null) return;

        var result = MessageBox.Show(
            "現在のセッションを停止しますか?\n応募データはリセットされます。",
            "セッション停止", MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        _scheduler.Stop();

        // DBのステータスを更新
        if (_activeSession.Status != SessionStatus.Completed)
        {
            _activeSession.Status = SessionStatus.Error;
            using var scope = _sp.CreateScope();
            var sessionRepo = scope.ServiceProvider.GetRequiredService<ILotterySessionRepository>();
            await sessionRepo.UpdateAsync(_activeSession);
        }

        // UI・状態リセット
        _activeSession = null;
        _state.Reset();

        Application.Current?.Dispatcher.Invoke(() =>
        {
            Entries.Clear();
            EntryCount    = 0;
            CanDraw       = false;
            CanStartSession = true;
            CanStopSession  = false;
            NextReplyTime   = "--:--:--";
        });

        UpdateStatusText(SessionStatus.Waiting);
        Log.Info("セッションを停止しました。");
    }

    // ── 今すぐ抽選 ────────────────────────────────────────
    [RelayCommand]
    private async Task DrawNowAsync()
    {
        if (_activeSession == null) { Log.Warning("セッションが開始されていません。"); return; }
        if (_state.IsDrawn)         { Log.Warning("すでに抽選済みです。"); return; }
        if (Entries.Count == 0)     { Log.Warning("応募者が0人のため抽選できません。"); return; }

        CanDraw = false;
        await ExecuteDrawAndSendAsync();
    }

    // ── テスト: Request Invite シミュレート ───────────────
    [RelayCommand]
    private void SimulateRequestInvite()
    {
        if (NotificationService is VRChatNotificationService svc)
        {
            var id = $"user_{Guid.NewGuid().ToString()[..8]}";
            svc.SimulateRequestInvite(id, $"TestUser_{id[5..]}");
        }
    }

    // ── ログアウト ─────────────────────────────────────────
    [RelayCommand]
    private async Task LogoutAsync()
    {
        var result = MessageBox.Show(
            "ログアウトしますか?\nアプリを再起動してログインし直す必要があります。",
            "ログアウト", MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes) return;

        // セッション中なら停止
        if (_activeSession != null)
            await StopSessionAsync();

        // Pipeline切断
        NotificationService.Stop();

        // API ログアウト・Cookie削除
        await _api.LogoutAsync();
        if (File.Exists(CookiePath)) File.Delete(CookiePath);

        Log.Info("ログアウトしました。アプリを再起動してください。");

        // ログアウト後はアプリを終了してログイン画面へ促す
        Application.Current?.Shutdown();
    }

    // ── Request Invite 受信処理 ───────────────────────────
    private async void OnRequestInviteReceived(object? sender, RequestInviteEventArgs e)
    {
        if (_activeSession == null) return;

        var status = _state.Status;
        using var scope = _sp.CreateScope();
        var entryRepo = scope.ServiceProvider.GetRequiredService<ILotteryEntryRepository>();

        if (status != SessionStatus.Accepting)
        {
            var reason = status == SessionStatus.BeforeReception ? "受付開始前" : "受付終了後";
            Log.Warning($"[{reason}] 無効応募: {e.DisplayName}");
            await entryRepo.AddAsync(new LotteryEntry
            {
                SessionId    = _activeSession.SessionId,
                UserId       = e.UserId,
                DisplayName  = e.DisplayName,
                RequestedAt  = e.ReceivedAt,
                IsAccepted   = false,
                RejectReason = reason
            });
            return;
        }

        var dup = await entryRepo.FindDuplicateAsync(_activeSession.SessionId, e.UserId);
        if (dup != null)
        {
            // 重複応募 → NotificationId だけ最新に更新（古い通知は失効している可能性があるため）
            if (!string.IsNullOrEmpty(e.NotificationId) && dup.NotificationId != e.NotificationId)
            {
                dup.NotificationId = e.NotificationId;
                await entryRepo.UpdateAsync(dup);
                Log.Info($"重複応募: NotificationId を更新しました ({e.DisplayName})");
            }
            else
            {
                Log.Warning($"重複応募を検出（スキップ）: {e.DisplayName}");
            }
            return;
        }

        var entry = new LotteryEntry
        {
            SessionId      = _activeSession.SessionId,
            UserId         = e.UserId,
            DisplayName    = e.DisplayName,
            NotificationId = e.NotificationId,
            RequestedAt    = e.ReceivedAt,
            IsAccepted     = true
        };

        await entryRepo.AddAsync(entry);
        _activeSession.Entries.Add(entry);
        _state.IncrementEntryCount();

        Application.Current?.Dispatcher.Invoke(() =>
        {
            EntryCount = _state.EntryCount;
            CanDraw    = EntryCount > 0;
            Entries.Add(new EntryViewModel(entry));
        });

        Log.Info($"応募受理: {e.DisplayName}");
    }

    private void OnReceptionStarted()
    {
        _state.UpdateStatus(SessionStatus.Accepting);
        UpdateStatusText(SessionStatus.Accepting);
        Log.Info("受付を開始しました。");
    }

    private void OnReceptionEnded()
    {
        _state.UpdateStatus(SessionStatus.ReceptionClosed);
        UpdateStatusText(SessionStatus.ReceptionClosed);
        Application.Current?.Dispatcher.Invoke(() => CanDraw = EntryCount > 0);
        Log.Info($"受付を終了しました。応募数: {EntryCount}人");
    }

    private async Task OnReplyTimeReachedAsync()
    {
        if (_activeSession == null) return;
        await ExecuteDrawAndSendAsync();
    }

    private async Task ExecuteDrawAndSendAsync()
    {
        if (_activeSession == null) return;
        try
        {
            using var scope    = _sp.CreateScope();
            var lotteryService = scope.ServiceProvider.GetRequiredService<ILotteryService>();
            var inviteService  = scope.ServiceProvider.GetRequiredService<IInviteService>();
            var sessionRepo    = scope.ServiceProvider.GetRequiredService<ILotterySessionRepository>();
            var entryRepo      = scope.ServiceProvider.GetRequiredService<ILotteryEntryRepository>();

            if (!_state.IsDrawn)
            {
                var winners = await lotteryService.DrawAsync(_activeSession);
                foreach (var w in winners) Log.Success($"当選者: {w.DisplayName}");
                _state.MarkDrawn();
                _activeSession.DrawExecutedAt = DateTime.UtcNow;
                _activeSession.Status = SessionStatus.Drawn;
            }

            var targets = _activeSession.Entries.Where(e => e.IsWinner).ToList();
            await inviteService.SendInvitesAsync(targets);

            await entryRepo.UpdateRangeAsync(_activeSession.Entries);
            _activeSession.ReplyExecutedAt = DateTime.UtcNow;
            _activeSession.Status = SessionStatus.Completed;
            await sessionRepo.UpdateAsync(_activeSession);

            _state.UpdateStatus(SessionStatus.Completed);
            UpdateStatusText(SessionStatus.Completed);
            Log.Success("Invite 送信が完了しました。");

            // 完了後、少し待ってから準備中に戻す
            await Task.Delay(2000);

            _activeSession = null;
            _state.Reset();

            Application.Current?.Dispatcher.Invoke(() =>
            {
                Entries.Clear();
                EntryCount      = 0;
                CanDraw         = false;
                CanStopSession  = false;
                CanStartSession = true;
                NextReplyTime   = "--:--:--";
            });

            UpdateStatusText(SessionStatus.Waiting);
            Log.Info("準備中に戻りました。次のセッションを開始できます。");
        }
        catch (Exception ex)
        {
            Log.Error($"抽選・送信中にエラーが発生しました: {ex.Message}");
        }
    }

    private void RefreshEntries()
    {
        Entries.Clear();
        if (_activeSession == null) return;
        foreach (var e in _activeSession.Entries)
            Entries.Add(new EntryViewModel(e));
    }

    private bool TryParseSettings(out TimeSpan start, out TimeSpan end, out TimeSpan reply)
    {
        start = end = reply = TimeSpan.Zero;
        bool ok = true;
        if (!TryParseTime(ReceptionStartTime, out start))
            { Log.Warning("受付開始時間の形式が正しくありません (例: 21:00)"); ok = false; }
        if (!TryParseTime(ReceptionEndTime, out end))
            { Log.Warning("受付終了時間の形式が正しくありません (例: 22:00)"); ok = false; }
        if (!TryParseTime(ReplyTime, out reply))
            { Log.Warning("返信時間の形式が正しくありません (例: 22:05)"); ok = false; }
        return ok;
    }

    private static bool TryParseTime(string input, out TimeSpan result)
        => TimeSpan.TryParseExact(input, @"hh\:mm", null, out result)
        || TimeSpan.TryParseExact(input, @"h\:mm",  null, out result)
        || TimeSpan.TryParseExact(input, @"hh\:mm\:ss", null, out result)
        || TimeSpan.TryParseExact(input, @"h\:mm\:ss",  null, out result);

    private void UpdateStatusText(SessionStatus s)
    {
        var text = s switch
        {
            SessionStatus.Waiting         => "待機中",
            SessionStatus.BeforeReception => "受付前",
            SessionStatus.Accepting       => "受付中",
            SessionStatus.ReceptionClosed => "受付終了",
            SessionStatus.Drawn           => "抽選済み",
            SessionStatus.Replied         => "返信済み",
            SessionStatus.Completed       => "完了",
            SessionStatus.Error           => "エラー",
            _                             => "不明"
        };
        Application.Current?.Dispatcher.Invoke(() => StatusText = text);
    }
}

public class EntryViewModel(LotteryEntry entry)
{
    public string DisplayName { get; } = entry.DisplayName;
    public string RequestedAt { get; } = entry.RequestedAt.ToString("HH:mm:ss");
    public bool   IsWinner    { get; } = entry.IsWinner;
    public string Status      { get; } = entry.IsWinner ? "🏆 当選"
                                       : entry.IsAccepted ? "応募中" : "無効";
}
