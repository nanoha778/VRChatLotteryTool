namespace VRChatLotteryTool.Core.Models;

public class User
{
    public string UserId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public int TotalEntries { get; set; }
    public int TotalWins { get; set; }
    public int LoseStreak { get; set; }
    public DateTime? LastWinAt { get; set; }
    public DateTime? LastEntryAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
