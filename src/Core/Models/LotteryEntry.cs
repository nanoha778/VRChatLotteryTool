namespace VRChatLotteryTool.Core.Models;

public class LotteryEntry
{
    public string EntryId { get; set; } = Guid.NewGuid().ToString();
    public string SessionId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? NotificationId { get; set; }   // RequestInvite の通知ID（返信に使用）
    public DateTime RequestedAt { get; set; }
    public bool IsAccepted { get; set; }
    public string? RejectReason { get; set; }
    public double AppliedWeight { get; set; }
    public bool IsWinner { get; set; }
    public bool InviteSent { get; set; }
    public DateTime? InviteSentAt { get; set; }
    public string? ErrorMessage { get; set; }

    public LotterySession? Session { get; set; }
}
