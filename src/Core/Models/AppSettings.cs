using System.IO;
using System.Text.Json;
using VRChatLotteryTool.Core;
using VRChatLotteryTool.Core.Models;
using VRChatLotteryTool.Core.Services;

namespace VRChatLotteryTool.Core.Models;

public class AppSettings
{
    public int         WinnerCount        { get; set; } = 1;
    public LotteryMode Mode               { get; set; } = LotteryMode.Fair;
    public string      ReceptionStartTime { get; set; } = "21:00";
    public string      ReceptionEndTime   { get; set; } = "22:00";
    public string      ReplyTime          { get; set; } = "22:05";
    public AppTheme    Theme              { get; set; } = AppTheme.Dark;

    // ── 永続化 ────────────────────────────────────────────────────────

    private static readonly string FilePath = AppPaths.AppSettings;

    private static readonly JsonSerializerOptions _json = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new AppSettings();
            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<AppSettings>(json, _json) ?? new AppSettings();
        }
        catch { return new AppSettings(); }
    }

    public void Save()
    {
        try
        {
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, _json));
        }
        catch { /* 保存失敗はサイレントに無視 */ }
    }
}
