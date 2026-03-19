using System.Collections.ObjectModel;
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

    public void Info(string message) => Add(Models.LogLevel.Info, message);
    public void Warning(string message) => Add(Models.LogLevel.Warning, message);
    public void Error(string message) => Add(Models.LogLevel.Error, message);
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
    }
}
