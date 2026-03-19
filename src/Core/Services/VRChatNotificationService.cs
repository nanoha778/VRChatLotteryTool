using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using VRChatLotteryTool.Core.Models;

namespace VRChatLotteryTool.Core.Services;

public interface IVRChatNotificationService
{
    event EventHandler<RequestInviteEventArgs>? RequestInviteReceived;
    void Start();
    void Stop();
}

public class RequestInviteEventArgs : EventArgs
{
    public string UserId        { get; init; } = string.Empty;
    public string DisplayName   { get; init; } = string.Empty;
    public string NotificationId { get; init; } = string.Empty;  // 返信用通知ID
    public DateTime ReceivedAt  { get; init; } = DateTime.Now;
}

// ── Pipeline WebSocket メッセージ DTO ─────────────────────────────────────

record PipelineMessage(
    [property: JsonPropertyName("type")]    string Type,
    [property: JsonPropertyName("content")] string? Content
);

record PipelineContent(
    [property: JsonPropertyName("id")]            string? Id,
    [property: JsonPropertyName("type")]           string? Type,
    [property: JsonPropertyName("senderUserId")]   string? SenderUserId,
    [property: JsonPropertyName("senderUsername")] string? SenderUsername,
    [property: JsonPropertyName("created_at")]     string? CreatedAt
);

/// <summary>
/// VRChat Pipeline（WebSocket）に接続して RequestInvite 通知を受信するサービス。
/// wss://pipeline.vrchat.cloud/?authToken={authCookie} に接続する。
/// </summary>
public class VRChatNotificationService : IVRChatNotificationService, IDisposable
{
    private const string PipelineUrl = "wss://pipeline.vrchat.cloud/";

    private readonly IVRChatApiClient _api;
    private readonly ILogService      _log;

    private ClientWebSocket? _ws;
    private CancellationTokenSource? _cts;

    public event EventHandler<RequestInviteEventArgs>? RequestInviteReceived;

    public VRChatNotificationService(IVRChatApiClient api, ILogService log)
    {
        _api = api;
        _log = log;
    }

    public void Start()
    {
        if (!_api.IsLoggedIn)
        {
            _log.Warning("[Pipeline] ログインしていないため接続できません。");
            return;
        }

        var authToken = _api.AuthCookieValue;
        if (string.IsNullOrEmpty(authToken))
        {
            _log.Warning("[Pipeline] auth Cookie が取得できません。");
            return;
        }

        _cts = new CancellationTokenSource();

        // Task.Run の第2引数にキャンセルトークンを渡さない
        // (渡すとトークンがキャンセル済みの場合にタスク開始自体がキャンセルされる)
        _ = Task.Run(async () =>
        {
            try
            {
                await ConnectLoopAsync(authToken, _cts.Token);
            }
            catch (Exception ex)
            {
                _log.Error($"[Pipeline] 予期しないエラー: {ex.Message}");
            }
        });
    }

    public void Stop()
    {
        _cts?.Cancel();
        _ws?.Dispose();
        _ws = null;
        _log.Info("[Pipeline] 切断しました。");
    }

    private async Task ConnectLoopAsync(string authToken, CancellationToken ct)
    {
        var delay = TimeSpan.FromSeconds(5);
        const int maxDelaySeconds = 60;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await ConnectAsync(authToken, ct);
                delay = TimeSpan.FromSeconds(5);
            }
            catch (OperationCanceledException)
            {
                // 明示的なキャンセル → ループ終了
                break;
            }
            catch (Exception ex)
            {
                _log.Warning($"[Pipeline] 切断 ({ex.Message})。{delay.TotalSeconds}秒後に再接続します...");
                try
                {
                    // Delay 中のキャンセルは OperationCanceledException を吐くが
                    // それもここで catch してループ継続する
                    await Task.Delay(delay, ct);
                }
                catch (OperationCanceledException) { break; }

                delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, maxDelaySeconds));
            }
        }

        _log.Info("[Pipeline] ループ終了。");
    }

    private async Task ConnectAsync(string authToken, CancellationToken ct)
    {
        _ws?.Dispose();
        _ws = new ClientWebSocket();
        _ws.Options.SetRequestHeader("User-Agent", "VRChatLotteryTool/1.0");

        var uri = new Uri($"{PipelineUrl}?authToken={Uri.EscapeDataString(authToken)}");
        await _ws.ConnectAsync(uri, ct);
        _log.Info("[Pipeline] WebSocket 接続しました。RequestInvite 待機中...");

        var buffer = new byte[8192];
        var sb     = new StringBuilder();

        while (_ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            sb.Clear();
            WebSocketReceiveResult result;
            do
            {
                result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                if (result.MessageType == WebSocketMessageType.Close) goto disconnected;
                sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
            }
            while (!result.EndOfMessage);

            ProcessMessage(sb.ToString());
        }

        disconnected:
        _log.Info("[Pipeline] WebSocket 切断されました。");
    }

    private void ProcessMessage(string json)
    {
        try
        {
            var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var msg  = JsonSerializer.Deserialize<PipelineMessage>(json, opts);
            if (msg == null) return;

            // notification イベントの中から requestInvite を拾う
            if (!msg.Type.Equals("notification", StringComparison.OrdinalIgnoreCase)) return;
            if (string.IsNullOrEmpty(msg.Content)) return;

            var content = JsonSerializer.Deserialize<PipelineContent>(msg.Content, opts);
            if (content == null) return;
            if (!string.Equals(content.Type, "requestInvite", StringComparison.OrdinalIgnoreCase)) return;

            _log.Info($"[Pipeline] RequestInvite 受信: {content.SenderUsername} ({content.SenderUserId})");

            RequestInviteReceived?.Invoke(this, new RequestInviteEventArgs
            {
                UserId         = content.SenderUserId  ?? string.Empty,
                DisplayName    = content.SenderUsername ?? string.Empty,
                NotificationId = content.Id             ?? string.Empty,
                ReceivedAt     = DateTime.TryParse(content.CreatedAt, out var dt) ? dt : DateTime.Now
            });
        }
        catch (Exception ex)
        {
            _log.Warning($"[Pipeline] メッセージ解析失敗: {ex.Message}");
        }
    }

    /// <summary>テスト用: 手動でイベントを発火する</summary>
    public void SimulateRequestInvite(string userId, string displayName)
    {
        RequestInviteReceived?.Invoke(this, new RequestInviteEventArgs
        {
            UserId      = userId,
            DisplayName = displayName,
            ReceivedAt  = DateTime.Now
        });
    }

    public void Dispose() => Stop();
}
