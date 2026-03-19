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
    private readonly IServiceProvider           _sp;
    private readonly ISessionStateService       _state;
    private readonly ISchedulerService          _scheduler;
    private readonly IVRChatApiClient           _api;
    public  ILogService                         Log { get; }
    public  IVRChatNotificationService          NotificationService { get; }

    private LotterySession? _activeSession;
    private static readonly string CookiePath = AppPaths.AuthCookie;

    // ── 設定 ──────────────────────────────────────────────
    [ObservableProperty] private int    _winnerCount        = 1;
    [ObservableProperty] private bool   _isReliefMode       = false;
    [ObservableProperty] private string _modeLabel          = "公平";
    [ObservableProperty] private string _receptionStartTime = "21:00";
    [ObservableProperty] private string _receptionEndTime   = "22:00";
    [ObservableProperty] private string _replyTime          = "22:05";

    // ── 状態表示 ───────────────────────────────────────────
    [ObservableProperty] private string _statusText         = "待機中";
    [ObservableProperty] private int    _entryCount;
    [ObservableProperty] private string _nextReplyTime      = "--:--";
    [ObservableProperty] private bool   _canStartSession    = true;   // 1. セッション開始
    [ObservableProperty] private bool   _canStartReception  = false;  // 2. 受付開始（手動）
    [ObservableProperty] private bool   _canCloseReception  = false;  // 3. 受付終了（手動）
    [ObservableProperty] private bool   _canDraw            = false;  // 4. 抽選・Invite送信（手動）
    [ObservableProperty] private bool   _canStopSession     = false;  // 5. セッション終了
    [ObservableProperty] private string _loginUserName      = string.Empty;
    [ObservableProperty] private string _currentWorldName   = "--";
    [ObservableProperty] private string _currentInstanceId  = string.Empty;
    [ObservableProperty] private string _currentTime        = string.Empty;

    private Timer? _instancePollingTimer;
    private Timer? _clockTimer;

    public ObservableCollection<LogEntry>       LogEntries => Log.Entries;
    public ObservableCollection<EntryViewModel> Entries    { get; } = [];

    public MainViewModel(
        IServiceProvider sp,
        ISessionStateService state,
        ISchedulerService scheduler,
        ILogService log,
        IVRChatNotificationService notificationService,
        IVRChatApiClient api)
    {
        _sp        = sp;
        _state     = state;
        _scheduler = scheduler;
        Log        = log;
        NotificationService = notificationService;
        _api       = api;

        // スケジューラーイベント
        _scheduler.ReceptionStarted += (_, _) => OnReceptionStarted();
        _scheduler.ReceptionEnded   += (_, _) => OnReceptionEnded();
        _scheduler.ReplyTimeReached += async (_, _) => await ExecuteDrawAndSendAsync();

        notificationService.RequestInviteReceived += OnRequestInviteReceived;

        LoginUserName = api.CurrentUser?.DisplayName ?? string.Empty;

        var s = AppSettings.Load();
        _winnerCount        = s.WinnerCount;
        _isReliefMode       = s.Mode == LotteryMode.Relief;
        _receptionStartTime = s.ReceptionStartTime;
        _receptionEndTime   = s.ReceptionEndTime;
        _replyTime          = s.ReplyTime;
        ModeLabel = _isReliefMode ? "救済" : "公平";

        StartInstancePolling();
    }

    // ── 設定変更時に自動保存 ──────────────────────────────
    private void SaveSettings() =>
        new AppSettings
        {
            WinnerCount        = WinnerCount,
            Mode               = IsReliefMode ? LotteryMode.Relief : LotteryMode.Fair,
            ReceptionStartTime = ReceptionStartTime,
            ReceptionEndTime   = ReceptionEndTime,
            ReplyTime          = ReplyTime,
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

    // ════════════════════════════════════════════════════════
    // 1. セッション開始
    // ════════════════════════════════════════════════════════
    [RelayCommand]
    private async Task StartSessionAsync()
    {
        if (!TryParseSettings(out var startSpan, out var endSpan, out var replySpan))
            return;

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

        // スケジューラー起動（時刻自動処理）
        _scheduler.Start(_activeSession);

        CanStartSession   = false;
        CanStartReception = true;   // 手動受付開始を有効化
        CanStopSession    = true;
        NextReplyTime     = _activeSession.ReplyAt.ToString("HH:mm");
        UpdateStatusText(SessionStatus.BeforeReception);
        Log.Info($"セッションを開始しました。受付開始: {_activeSession.ReceptionStartAt:HH:mm} / 終了: {_activeSession.ReceptionEndAt:HH:mm} / Invite: {_activeSession.ReplyAt:HH:mm}");
    }

    // ════════════════════════════════════════════════════════
    // 2. 受付開始（手動）
    // ════════════════════════════════════════════════════════
    [RelayCommand]
    private void StartReceptionManually()
    {
        if (_activeSession == null) return;
        OnReceptionStarted();
        Log.Info("受付を手動で開始しました。");
    }

    private void OnReceptionStarted()
    {
        if (_activeSession == null) return;
        _state.UpdateStatus(SessionStatus.Accepting);
        Application.Current?.Dispatcher.Invoke(() =>
        {
            CanStartReception = false;
            CanCloseReception = true;
        });
        UpdateStatusText(SessionStatus.Accepting);
        Log.Info("受付中です。Request Invite をお待ちください。");
    }

    // ════════════════════════════════════════════════════════
    // 3. 受付終了（手動）
    // ════════════════════════════════════════════════════════
    [RelayCommand]
    private void CloseReceptionManually()
    {
        if (_activeSession == null) return;
        OnReceptionEnded();
        Log.Info("受付を手動で終了しました。");
    }

    private void OnReceptionEnded()
    {
        if (_activeSession == null) return;
        _activeSession.ReceptionEndAt = DateTime.Now;
        _activeSession.Status         = SessionStatus.ReceptionClosed;
        _state.UpdateStatus(SessionStatus.ReceptionClosed);
        Application.Current?.Dispatcher.Invoke(() =>
        {
            CanCloseReception = false;
            CanDraw           = EntryCount > 0;
        });
        UpdateStatusText(SessionStatus.ReceptionClosed);
        Log.Info($"受付を終了しました。応募数: {EntryCount}人");
    }

    // ════════════════════════════════════════════════════════
    // 4. 今すぐ抽選・Invite送信（手動）
    // ════════════════════════════════════════════════════════
    [RelayCommand]
    private async Task DrawNowAsync()
    {
        if (_activeSession == null) { Log.Warning("セッションが開始されていません。"); return; }
        if (_state.IsDrawn)         { Log.Warning("すでに抽選済みです。"); return; }
        if (Entries.Count == 0)     { Log.Warning("応募者が0人のため抽選できません。"); return; }
        CanDraw = false;
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

            // 受付がまだ終了していなければ自動終了
            if (_state.Status == SessionStatus.Accepting)
                OnReceptionEnded();

            if (!_state.IsDrawn)
            {
                var winners = await lotteryService.DrawAsync(_activeSession);
                foreach (var w in winners) Log.Success($"当選: {w.DisplayName}");
                _state.MarkDrawn();
                _activeSession.DrawExecutedAt = DateTime.UtcNow;
                _activeSession.Status         = SessionStatus.Drawn;
            }

            var targets = _activeSession.Entries.Where(e => e.IsWinner).ToList();
            await inviteService.SendInvitesAsync(targets);

            await entryRepo.UpdateRangeAsync(_activeSession.Entries);
            _activeSession.ReplyExecutedAt = DateTime.UtcNow;
            _activeSession.Status          = SessionStatus.Completed;
            await sessionRepo.UpdateAsync(_activeSession);

            _state.UpdateStatus(SessionStatus.Completed);
            UpdateStatusText(SessionStatus.Completed);

            Application.Current?.Dispatcher.Invoke(() =>
            {
                CanDraw        = false;
                CanStopSession = true;
                RefreshEntries();
            });

            Log.Success("Invite 送信が完了しました。セッション停止ボタンで次のセッションに進めます。");
        }
        catch (Exception ex)
        {
            Log.Error($"抽選・送信中にエラーが発生しました: {ex.Message}");
        }
    }

    // ════════════════════════════════════════════════════════
    // 5. セッション終了
    // ════════════════════════════════════════════════════════
    [RelayCommand]
    private async Task StopSessionAsync()
    {
        if (_activeSession == null) return;

        var result = MessageBox.Show(
            "セッションを終了しますか？",
            "セッション終了", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        _scheduler.Stop();

        if (_activeSession.Status != SessionStatus.Completed)
        {
            _activeSession.Status = SessionStatus.Error;
            using var scope = _sp.CreateScope();
            var sessionRepo = scope.ServiceProvider.GetRequiredService<ILotterySessionRepository>();
            await sessionRepo.UpdateAsync(_activeSession);
        }

        ResetSession();
        Log.Info("セッションを終了しました。");
    }

    // ── ログアウト ─────────────────────────────────────────
    [RelayCommand]
    private async Task LogoutAsync()
    {
        var result = MessageBox.Show(
            "ログアウトしますか？",
            "ログアウト", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes) return;

        if (_activeSession != null) await StopSessionAsync();

        NotificationService.Stop();
        await _api.LogoutAsync();
        if (File.Exists(CookiePath)) File.Delete(CookiePath);

        Log.Info("ログアウトしました。");
        Application.Current?.Shutdown();
    }

    // ── Request Invite 受信 ───────────────────────────────
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
            // 受付終了後にも応募が来た場合はCanDrawを再評価
            if (_state.Status == SessionStatus.ReceptionClosed)
                CanDraw = EntryCount > 0;
            Entries.Add(new EntryViewModel(entry));
        });

        Log.Info($"応募受理: {e.DisplayName}");
    }

    // ── ヘルパー ──────────────────────────────────────────
    private void ResetSession()
    {
        _activeSession = null;
        _state.Reset();
        Application.Current?.Dispatcher.Invoke(() =>
        {
            Entries.Clear();
            EntryCount        = 0;
            CanStartSession   = true;
            CanStartReception = false;
            CanCloseReception = false;
            CanDraw           = false;
            CanStopSession    = false;
            NextReplyTime     = "--:--";
        });
        UpdateStatusText(SessionStatus.Waiting);
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
            { Log.Warning("Invite送信時間の形式が正しくありません (例: 22:05)"); ok = false; }
        if (ok && start >= end)
            { Log.Warning("受付開始時間は終了時間より前に設定してください。"); ok = false; }
        return ok;
    }

    private static bool TryParseTime(string input, out TimeSpan result)
        => TimeSpan.TryParseExact(input.Trim(), @"hh\:mm", null, out result)
        || TimeSpan.TryParseExact(input.Trim(), @"h\:mm",  null, out result);

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

    // ── インスタンス情報ポーリング ────────────────────────
    private void StartInstancePolling()
    {
        _clockTimer = new Timer(
            _ => Application.Current?.Dispatcher.Invoke(
                () => CurrentTime = DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss")),
            null, TimeSpan.Zero, TimeSpan.FromSeconds(1));

        _ = Task.Run(UpdateInstanceInfoAsync);
        _instancePollingTimer = new Timer(
            async _ => await UpdateInstanceInfoAsync(),
            null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
    }

    private async Task UpdateInstanceInfoAsync()
    {
        try
        {
            if (!_api.IsLoggedIn) return;
            var location = await _api.GetLocationAsync();

            string worldName;
            if (string.IsNullOrEmpty(location)
                || location.Equals("offline",   StringComparison.OrdinalIgnoreCase)
                || location.Equals("traveling", StringComparison.OrdinalIgnoreCase))
            {
                worldName = "オフライン";
                location  = string.Empty;
            }
            else if (location.Equals("private", StringComparison.OrdinalIgnoreCase))
            {
                worldName = "プライベート";
            }
            else
            {
                var colonIdx = location.IndexOf(':');
                var worldId  = colonIdx > 0 ? location[..colonIdx] : location;
                var name     = await _api.GetWorldNameAsync(worldId);
                worldName    = name ?? worldId;
            }

            Application.Current?.Dispatcher.Invoke(() =>
            {
                CurrentWorldName  = worldName;
                CurrentInstanceId = location ?? string.Empty;
            });
        }
        catch { /* サイレント */ }
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
