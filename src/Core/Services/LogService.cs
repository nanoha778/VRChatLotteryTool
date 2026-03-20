using System.Collections.ObjectModel;
using System.IO;
using VRChatLotteryTool.Core;
using VRChatLotteryTool.Core.Models;

namespace VRChatLotteryTool.Core.Services;

public interface ILogService
{
    ObservableCollection<LogEntry> Entries { get; }
    void Info(string message);
    void Warning(string message);
    void Error(string message);
    void Success(string message);
    event EventHandler<LogEntry>? LogAdded;
}

public class LogService : ILogService
{
    public ObservableCollection<LogEntry> Entries { get; } = [];
    public event EventHandler<LogEntry>? LogAdded;

    private const int MaxEntries = 500;
    private static readonly string LogPath = AppPaths.LogFile;
    private static readonly SemaphoreSlim _lock = new(1, 1);

    public LogService()
    {
        // 起動時にログファイルをリセット（最新セッションのみ保持）
        try
        {
            File.WriteAllText(LogPath,
                $"=== VRChat Lottery Tool  起動: {DateTime.Now:yyyy-MM-dd HH:mm:ss} ==={Environment.NewLine}");
        }
        catch { /* 書き込み失敗はサイレント */ }
    }

    public void Info(string message)    => Add(Models.LogLevel.Info,    message);
    public void Warning(string message) => Add(Models.LogLevel.Warning, message);
    public void Error(string message)   => Add(Models.LogLevel.Error,   message);
    public void Success(string message) => Add(Models.LogLevel.Success, message);

    private void Add(Models.LogLevel level, string message)
    {
        var entry = new LogEntry { Level = level, Message = message };

        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            Entries.Insert(0, entry);
            while (Entries.Count > MaxEntries)
                Entries.RemoveAt(Entries.Count - 1);
        });

        LogAdded?.Invoke(this, entry);

        // ファイルに非同期で書き出す
        _ = WriteToFileAsync(entry);
    }

    private static async Task WriteToFileAsync(LogEntry entry)
    {
        await _lock.WaitAsync();
        try
        {
            var levelTag = entry.Level switch
            {
                Models.LogLevel.Info    => "INFO   ",
                Models.LogLevel.Warning => "WARN   ",
                Models.LogLevel.Error   => "ERROR  ",
                Models.LogLevel.Success => "SUCCESS",
                _                       => "INFO   "
            };
            var line = $"[{entry.Timestamp:yyyy-MM-dd HH:mm:ss}] [{levelTag}] {entry.Message}{Environment.NewLine}";
            await File.AppendAllTextAsync(LogPath, line);
        }
        catch { /* ログ書き込み失敗はサイレント */ }
        finally
        {
            _lock.Release();
        }
    }
}
