using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VRChatLotteryTool.Core.Services;

// ── DTOs ─────────────────────────────────────────────────────────────────

public record VRChatUser(
    [property: JsonPropertyName("id")]          string Id,
    [property: JsonPropertyName("displayName")] string DisplayName,
    [property: JsonPropertyName("requiresTwoFactorAuth")] List<string>? RequiresTwoFactorAuth,
    [property: JsonPropertyName("location")]    string? Location   // 現在いるインスタンスID
);

public record VRChatNotification(
    [property: JsonPropertyName("id")]             string Id,
    [property: JsonPropertyName("type")]           string Type,
    [property: JsonPropertyName("senderUserId")]   string SenderUserId,
    [property: JsonPropertyName("senderUsername")] string SenderUsername,
    [property: JsonPropertyName("created_at")]     string CreatedAt
);

public enum TwoFactorType { None, Totp, EmailOtp }

public record LoginResult(
    bool Success,
    string Message,
    TwoFactorType TwoFactorRequired = TwoFactorType.None
);

// ── Interface ─────────────────────────────────────────────────────────────

public interface IVRChatApiClient
{
    bool IsLoggedIn { get; }
    VRChatUser? CurrentUser { get; }
    string? AuthCookieValue { get; }

    Task<LoginResult> LoginAsync(string username, string password, CancellationToken ct = default);
    Task<(bool success, string message)> VerifyTwoFactorAsync(string code, TwoFactorType type, CancellationToken ct = default);
    Task<bool> VerifySessionAsync(CancellationToken ct = default);
    Task<string?> GetLocationAsync(CancellationToken ct = default);
    Task<string?> GetWorldNameAsync(string worldId, CancellationToken ct = default);
    Task<List<VRChatNotification>> GetNotificationsAsync(string type = "requestInvite", CancellationToken ct = default);
    Task<bool> SendInviteToUserAsync(string senderUserId, CancellationToken ct = default);
    Task<bool> HideNotificationAsync(string notificationId, CancellationToken ct = default);
    Task LogoutAsync(CancellationToken ct = default);
    void SaveAuthCookie(string path);
    bool LoadAuthCookie(string path);
}

// ── 実装 ──────────────────────────────────────────────────────────────────

public class VRChatApiClient : IVRChatApiClient, IDisposable
{
    // ※ 末尾スラッシュあり → 相対パスが "auth/user" (スラッシュなし) で正しく結合される
    private const string BaseUrl    = "https://api.vrchat.cloud/api/1/";
    private static readonly Uri     BaseUri = new(BaseUrl);
    private static readonly Uri     CookieUri = new("https://api.vrchat.cloud/");

    private readonly CookieContainer _cookies;
    private readonly HttpClient      _http;
    private readonly ILogService     _log;

    public bool IsLoggedIn    { get; private set; }
    public VRChatUser? CurrentUser { get; private set; }

    // auth Cookie の値（WebSocket Pipeline 接続に使用）
    public string? AuthCookieValue
        => _cookies.GetCookies(CookieUri)
                   .Cast<Cookie>()
                   .FirstOrDefault(c => c.Name.Equals("auth", StringComparison.OrdinalIgnoreCase))
                   ?.Value;

    private static readonly JsonSerializerOptions _json = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public VRChatApiClient(ILogService log)
    {
        _log     = log;
        _cookies = new CookieContainer();

        var handler = new HttpClientHandler
        {
            CookieContainer        = _cookies,
            UseCookies             = true,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            AllowAutoRedirect      = true,
        };

        _http = new HttpClient(handler)
        {
            BaseAddress = BaseUri,
            Timeout     = TimeSpan.FromSeconds(30)
        };

        // User-Agent は RFC 7230 のパーサーが @ や : を拒否するため
        // TryAddWithoutValidation でバリデーションを回避して設定する
        _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "VRChatLotteryTool/1.0");
        _http.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
    }

    // ── ログイン ──────────────────────────────────────────────────────────
    // フロー:
    //   1. GET auth/user (Basic 認証ヘッダー)
    //      → 成功: auth Cookie が Set-Cookie で返ってくる
    //   2a. requiresTwoFactorAuth が空 → 完了
    //   2b. ["totp"] or ["emailOtp"] → 2FA 必要
    //   3. POST auth/twofactorauth/totp/verify (同じ CookieContainer を使い続ける)
    //   4. GET auth/user で CurrentUser 再取得

    public async Task<LoginResult> LoginAsync(
        string username, string password, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            return new LoginResult(false, "ユーザー名またはパスワードが空です。");

        try
        {
            var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(
                $"{Uri.EscapeDataString(username)}:{Uri.EscapeDataString(password)}"));

            // ※ "auth/user" — 先頭スラッシュなし (BaseAddress が末尾スラッシュありのため)
            using var request = new HttpRequestMessage(HttpMethod.Get, "auth/user");
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", encoded);

            using var resp = await _http.SendAsync(request, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);

            _log.Info($"[VRChat API] Login → HTTP {(int)resp.StatusCode}");
            _log.Info($"[VRChat API] body: {Trim(body)}");

            if (resp.StatusCode == HttpStatusCode.Unauthorized)
                return new LoginResult(false, "ユーザー名またはパスワードが正しくありません。");

            if (!resp.IsSuccessStatusCode)
                return new LoginResult(false,
                    $"ログイン失敗 (HTTP {(int)resp.StatusCode})\n{Trim(body)}");

            var user = JsonSerializer.Deserialize<VRChatUser>(body, _json);
            if (user == null)
                return new LoginResult(false, "レスポンスの解析に失敗しました。");

            var twoFaType = DetectTwoFactorType(user.RequiresTwoFactorAuth);
            if (twoFaType != TwoFactorType.None)
            {
                _log.Info($"[VRChat API] 2FA 必要: {twoFaType}");
                return new LoginResult(true, "2FA_REQUIRED", twoFaType);
            }

            CurrentUser = user;
            IsLoggedIn  = true;
            _log.Success($"[VRChat API] ログイン成功: {user.DisplayName}");
            return new LoginResult(true, "OK");
        }
        catch (TaskCanceledException)
        {
            return new LoginResult(false, "接続がタイムアウトしました。");
        }
        catch (HttpRequestException ex)
        {
            return new LoginResult(false, $"ネットワークエラー: {ex.Message}");
        }
        catch (Exception ex)
        {
            _log.Error($"[VRChat API] ログイン例外: {ex}");
            return new LoginResult(false, $"エラー: {ex.Message}");
        }
    }

    // ── 2FA 検証 ──────────────────────────────────────────────────────────
    // Login で得た auth Cookie が CookieContainer に残っているので
    // そのまま同じ HttpClient で POST すればよい

    public async Task<(bool success, string message)> VerifyTwoFactorAsync(
        string code, TwoFactorType type, CancellationToken ct = default)
    {
        try
        {
            var endpoint = type == TwoFactorType.Totp
                ? "auth/twofactorauth/totp/verify"
                : "auth/twofactorauth/emailotp/verify";

            var payload = JsonSerializer.Serialize(new { code = code.Trim() });
            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            using var resp    = await _http.PostAsync(endpoint, content, ct);
            var body          = await resp.Content.ReadAsStringAsync(ct);

            _log.Info($"[VRChat API] 2FA verify → HTTP {(int)resp.StatusCode}  body: {Trim(body)}");

            if (!resp.IsSuccessStatusCode)
            {
                var msg = body.Contains("verified", StringComparison.OrdinalIgnoreCase)
                    ? "認証コードが正しくありません。"
                    : $"2FA 検証失敗 (HTTP {(int)resp.StatusCode})";
                return (false, msg);
            }

            // 2FA 完了後に CurrentUser を再取得
            var user = await FetchCurrentUserAsync(ct);
            CurrentUser = user;
            IsLoggedIn  = user != null;
            _log.Success($"[VRChat API] 2FA 認証成功: {user?.DisplayName}");
            return (true, "OK");
        }
        catch (Exception ex)
        {
            _log.Error($"[VRChat API] 2FA 例外: {ex.Message}");
            return (false, ex.Message);
        }
    }

    // ── Cookie のみでセッション復元 ──────────────────────────────────────

    public async Task<bool> VerifySessionAsync(CancellationToken ct = default)
    {
        try
        {
            var user = await FetchCurrentUserAsync(ct);
            if (user == null) return false;
            if (DetectTwoFactorType(user.RequiresTwoFactorAuth) != TwoFactorType.None) return false;

            CurrentUser = user;
            IsLoggedIn  = true;
            _log.Success($"[VRChat API] セッション復元成功: {user.DisplayName}");
            return true;
        }
        catch (Exception ex)
        {
            _log.Warning($"[VRChat API] セッション復元失敗: {ex.Message}");
            return false;
        }
    }

    public async Task<string?> GetLocationAsync(CancellationToken ct = default)
    {
        try
        {
            var userId = CurrentUser?.Id;
            if (string.IsNullOrEmpty(userId))
            {
                var me = await FetchCurrentUserAsync(ct);
                if (me != null) CurrentUser = me;
                userId = me?.Id;
            }

            if (string.IsNullOrEmpty(userId)) return null;

            using var resp = await _http.GetAsync($"users/{userId}", ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            _log.Info($"[VRChat API] GET users/{userId} → HTTP {(int)resp.StatusCode}");
            _log.Info($"[VRChat API] location body: {Trim(body)}");

            if (!resp.IsSuccessStatusCode) return null;

            var user = JsonSerializer.Deserialize<VRChatUser>(body, _json);
            if (user != null) CurrentUser = user;
            return user?.Location;
        }
        catch (Exception ex)
        {
            _log.Warning($"[VRChat API] GetLocation 失敗: {ex.Message}");
            return null;
        }
    }

    public async Task<string?> GetWorldNameAsync(string worldId, CancellationToken ct = default)
    {
        try
        {
            using var resp = await _http.GetAsync($"worlds/{worldId}", ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode) return null;

            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("name", out var nameProp))
                return nameProp.GetString();
            return null;
        }
        catch (Exception ex)
        {
            _log.Warning($"[VRChat API] GetWorldName 失敗: {ex.Message}");
            return null;
        }
    }

    private async Task<VRChatUser?> FetchCurrentUserAsync(CancellationToken ct)
    {
        using var resp = await _http.GetAsync("auth/user", ct);
        _log.Info($"[VRChat API] GET auth/user → HTTP {(int)resp.StatusCode}");
        if (!resp.IsSuccessStatusCode) return null;
        var body = await resp.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<VRChatUser>(body, _json);
    }

    // ── 通知取得 ──────────────────────────────────────────────────────────

    public async Task<List<VRChatNotification>> GetNotificationsAsync(
        string type = "requestInvite", CancellationToken ct = default)
    {
        try
        {
            using var resp = await _http.GetAsync($"auth/user/notifications?type={type}", ct);
            if (!resp.IsSuccessStatusCode) return [];
            var body = await resp.Content.ReadAsStringAsync(ct);
            return JsonSerializer.Deserialize<List<VRChatNotification>>(body, _json) ?? [];
        }
        catch (Exception ex)
        {
            _log.Error($"[VRChat API] 通知取得エラー: {ex.Message}");
            return [];
        }
    }

    // ── Invite 送信 ───────────────────────────────────────────────────────

    // ── RequestInvite への承認（当選者に Invite を返送）──────────────────
    // 方針: Request Invite の承認 = senderUserId に対して POST /invite/{senderUserId}
    //       本文に instanceId（自分の現在地）を付与する
    //       notificationId は使わない（notificationId は /response 系返信専用）

    public async Task<bool> SendInviteToUserAsync(string senderUserId, CancellationToken ct = default)
    {
        try
        {
            if (string.IsNullOrEmpty(senderUserId))
            {
                _log.Error("[VRChat API] SendInvite 失敗: senderUserId が空です。");
                return false;
            }

            // ── 自分の現在地 (instanceId) を /users/{myId} から取得 ────────
            // /auth/user では location が null/offline になることがあるため
            // /users/{userId} を使うと正確な location が取得できる
            var myId = CurrentUser?.Id;
            if (string.IsNullOrEmpty(myId))
            {
                var me0 = await FetchCurrentUserAsync(ct);
                if (me0 != null) CurrentUser = me0;
                myId = me0?.Id;
            }

            VRChatUser? me = null;
            if (!string.IsNullOrEmpty(myId))
            {
                using var meResp = await _http.GetAsync($"users/{myId}", ct);
                var meBody = await meResp.Content.ReadAsStringAsync(ct);
                _log.Info($"[VRChat API] GET users/{myId} body: {Trim(meBody)}");
                me = JsonSerializer.Deserialize<VRChatUser>(meBody, _json);
                if (me != null) CurrentUser = me;
            }

            var location = me?.Location;
            _log.Info($"[Invite] Raw location  = {location ?? "null"}");

            // 特殊値チェック
            if (string.IsNullOrWhiteSpace(location)
                || location.Equals("offline",             StringComparison.OrdinalIgnoreCase)
                || location.Equals("private",             StringComparison.OrdinalIgnoreCase)
                || location.Equals("traveling",           StringComparison.OrdinalIgnoreCase)
                || location.Equals("traveling:traveling", StringComparison.OrdinalIgnoreCase))
            {
                _log.Error($"[Invite] 失敗: 招待に使えない location です ({location ?? "null"})。VRChat を起動してワールドに入ってください。");
                return false;
            }

            // VRChat Invite API は instanceId フィールドに "worldId:instanceId" の
            // location 全体文字列を要求する（コロン以降だけでは "Invalid location" になる）
            var instanceId = location;

            _log.Info($"[Invite] instanceId    = {instanceId}");
            _log.Info($"[Invite] senderUserId  = {senderUserId}");

            // ── senderUserId に Invite 送信 ───────────────────────────────
            var payload = JsonSerializer.Serialize(new { instanceId });
            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            using var resp    = await _http.PostAsync($"invite/{senderUserId}", content, ct);
            var body          = await resp.Content.ReadAsStringAsync(ct);

            _log.Info($"[VRChat API] SendInvite → HTTP {(int)resp.StatusCode}");
            if (resp.IsSuccessStatusCode) { _log.Success($"Invite 送信成功: {senderUserId}"); return true; }
            _log.Error($"SendInvite 失敗 [{senderUserId}] ({(int)resp.StatusCode}): {Trim(body)}");
            return false;
        }
        catch (Exception ex)
        {
            _log.Error($"[VRChat API] SendInvite 例外: {ex.Message}");
            return false;
        }
    }

    // ── 通知を既読 ────────────────────────────────────────────────────────

    public async Task<bool> HideNotificationAsync(string notificationId, CancellationToken ct = default)
    {
        try
        {
            using var resp = await _http.PutAsync(
                $"auth/user/notifications/{notificationId}/hide", null, ct);
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    // ── ログアウト ────────────────────────────────────────────────────────

    public async Task LogoutAsync(CancellationToken ct = default)
    {
        try { using var _ = await _http.PutAsync("logout", null, ct); } catch { }
        IsLoggedIn  = false;
        CurrentUser = null;
        _log.Info("[VRChat API] ログアウトしました。");
    }

    // ── Cookie 永続化 ─────────────────────────────────────────────────────

    public void SaveAuthCookie(string path)
    {
        try
        {
            var value = AuthCookieValue;
            if (string.IsNullOrEmpty(value))
            {
                _log.Warning("[VRChat API] auth Cookie が見つかりません。保存をスキップします。");
                return;
            }
            var json      = JsonSerializer.Serialize(new { value });
            var encrypted = System.Security.Cryptography.ProtectedData.Protect(
                Encoding.UTF8.GetBytes(json), null,
                System.Security.Cryptography.DataProtectionScope.CurrentUser);
            File.WriteAllBytes(path, encrypted);
            _log.Info("[VRChat API] 認証 Cookie を保存しました。");
        }
        catch (Exception ex) { _log.Warning($"[VRChat API] Cookie 保存失敗: {ex.Message}"); }
    }

    public bool LoadAuthCookie(string path)
    {
        try
        {
            if (!File.Exists(path)) return false;
            var decrypted = System.Security.Cryptography.ProtectedData.Unprotect(
                File.ReadAllBytes(path), null,
                System.Security.Cryptography.DataProtectionScope.CurrentUser);
            var doc   = JsonSerializer.Deserialize<JsonElement>(Encoding.UTF8.GetString(decrypted));
            var value = doc.GetProperty("value").GetString();
            if (string.IsNullOrEmpty(value)) return false;

            // Cookie は vrchat.cloud ドメイン全体に適用
            _cookies.Add(CookieUri, new Cookie("auth", value));
            _log.Info("[VRChat API] 保存済み Cookie を読み込みました。");
            return true;
        }
        catch
        {
            if (File.Exists(path)) File.Delete(path);
            return false;
        }
    }

    // ── ヘルパー ──────────────────────────────────────────────────────────

    private static TwoFactorType DetectTwoFactorType(List<string>? list)
    {
        if (list == null || list.Count == 0) return TwoFactorType.None;
        foreach (var item in list)
        {
            if (item.Equals("totp",     StringComparison.OrdinalIgnoreCase)) return TwoFactorType.Totp;
            if (item.Equals("emailOtp", StringComparison.OrdinalIgnoreCase)) return TwoFactorType.EmailOtp;
        }
        return TwoFactorType.Totp;
    }

    private static string Trim(string s) => s.Length > 200 ? s[..200] + "..." : s;

    public void Dispose() => _http.Dispose();
}
