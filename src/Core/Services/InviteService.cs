using VRChatLotteryTool.Core.Models;

namespace VRChatLotteryTool.Core.Services;

public interface IInviteService
{
    Task SendInvitesAsync(IEnumerable<LotteryEntry> winners, CancellationToken ct = default);
}

/// <summary>
/// Request Invite の承認 = 当選者 (senderUserId) に対して Invite を送ること。
/// POST /invite/{senderUserId} に自分の instanceId を付けて呼ぶ。
/// </summary>
public class InviteService : IInviteService
{
    private readonly IVRChatApiClient _api;
    private readonly ILogService      _log;

    public InviteService(IVRChatApiClient api, ILogService log)
    {
        _api = api;
        _log = log;
    }

    public async Task SendInvitesAsync(IEnumerable<LotteryEntry> winners, CancellationToken ct = default)
    {
        foreach (var winner in winners)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                // userId が空の場合はスキップ（テスト送信など）
                if (string.IsNullOrEmpty(winner.UserId))
                {
                    winner.InviteSent   = false;
                    winner.ErrorMessage = "UserId が記録されていません。";
                    _log.Warning($"[Invite] {winner.DisplayName}: UserId なしのためスキップ");
                    continue;
                }

                // senderUserId に対して Invite を送ることで承認扱いになる
                bool ok = await _api.SendInviteToUserAsync(winner.UserId, ct);
                if (ok)
                {
                    winner.InviteSent   = true;
                    winner.InviteSentAt = DateTime.UtcNow;
                    _log.Success($"Invite 送信成功（承認）: {winner.DisplayName}");
                }
                else
                {
                    winner.InviteSent   = false;
                    winner.ErrorMessage = "API から失敗レスポンス";
                    _log.Error($"Invite 送信失敗: {winner.DisplayName}");
                }
            }
            catch (Exception ex)
            {
                winner.InviteSent   = false;
                winner.ErrorMessage = ex.Message;
                _log.Error($"Invite 例外 [{winner.DisplayName}]: {ex.Message}");
            }
        }
    }
}
