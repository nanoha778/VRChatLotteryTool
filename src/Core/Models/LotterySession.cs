namespace VRChatLotteryTool.Core.Models;

public enum SessionStatus
{
    Waiting,        // 待機中
    BeforeReception,// 受付前
    Accepting,      // 受付中
    ReceptionClosed,// 受付終了
    Drawn,          // 抽選済み
    Replied,        // 返信済み
    Completed,      // 完了
    Error           // エラー
}

public enum LotteryMode
{
    Fair,       // 公平
    Relief      // 救済
}

public class LotterySession
{
    public string SessionId { get; set; } = Guid.NewGuid().ToString();
    public DateTime ReceptionStartAt { get; set; }
    public DateTime ReceptionEndAt { get; set; }
    public DateTime ReplyAt { get; set; }
    public int WinnerCount { get; set; } = 1;
    public LotteryMode Mode { get; set; } = LotteryMode.Fair;
    public SessionStatus Status { get; set; } = SessionStatus.Waiting;
    public DateTime? DrawExecutedAt { get; set; }
    public DateTime? ReplyExecutedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<LotteryEntry> Entries { get; set; } = new List<LotteryEntry>();
}
