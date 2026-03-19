using System.ComponentModel;
using VRChatLotteryTool.Core.Models;

namespace VRChatLotteryTool.Core.Services;

public interface ISessionStateService : INotifyPropertyChanged
{
    LotterySession? CurrentSession { get; }
    SessionStatus Status { get; }
    int EntryCount { get; }
    bool IsDrawn { get; }

    void SetSession(LotterySession session);
    void UpdateStatus(SessionStatus status);
    void IncrementEntryCount();
    void MarkDrawn();
    void Reset();
}

public class SessionStateService : ISessionStateService
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private LotterySession? _currentSession;
    private SessionStatus _status = SessionStatus.Waiting;
    private int _entryCount;
    private bool _isDrawn;

    public LotterySession? CurrentSession => _currentSession;
    public SessionStatus Status => _status;
    public int EntryCount => _entryCount;
    public bool IsDrawn => _isDrawn;

    public void SetSession(LotterySession session)
    {
        _currentSession = session;
        _entryCount = 0;
        _isDrawn = false;
        Notify(nameof(CurrentSession));
        Notify(nameof(EntryCount));
        Notify(nameof(IsDrawn));
    }

    public void UpdateStatus(SessionStatus status)
    {
        _status = status;
        Notify(nameof(Status));
    }

    public void IncrementEntryCount()
    {
        _entryCount++;
        Notify(nameof(EntryCount));
    }

    public void MarkDrawn()
    {
        _isDrawn = true;
        Notify(nameof(IsDrawn));
    }

    public void Reset()
    {
        _currentSession = null;
        _status = SessionStatus.Waiting;
        _entryCount = 0;
        _isDrawn = false;
        Notify(null);
    }

    private void Notify(string? name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
