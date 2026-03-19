using VRChatLotteryTool.Core.Models;

namespace VRChatLotteryTool.Core.Services;

public interface ISchedulerService
{
    void Start(LotterySession session);
    void Stop();
    event EventHandler? ReceptionStarted;
    event EventHandler? ReceptionEnded;
    event EventHandler? ReplyTimeReached;
}

public class SchedulerService : ISchedulerService, IDisposable
{
    private readonly ILogService _log;
    private Timer? _timer;
    private LotterySession? _session;

    public event EventHandler? ReceptionStarted;
    public event EventHandler? ReceptionEnded;
    public event EventHandler? ReplyTimeReached;

    private bool _receptionStartFired;
    private bool _receptionEndFired;
    private bool _replyFired;

    public SchedulerService(ILogService log) => _log = log;

    public void Start(LotterySession session)
    {
        Stop();
        _session = session;
        _receptionStartFired = false;
        _receptionEndFired = false;
        _replyFired = false;

        _timer = new Timer(Tick, null, TimeSpan.Zero, TimeSpan.FromSeconds(5));
        _log.Info("スケジューラを開始しました。");
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
    }

    private void Tick(object? _)
    {
        if (_session == null) return;
        var now = DateTime.Now;

        if (!_receptionStartFired && now >= _session.ReceptionStartAt)
        {
            _receptionStartFired = true;
            _log.Info("受付開始時刻になりました。");
            ReceptionStarted?.Invoke(this, EventArgs.Empty);
        }

        if (!_receptionEndFired && now >= _session.ReceptionEndAt)
        {
            _receptionEndFired = true;
            _log.Info("受付終了時刻になりました。");
            ReceptionEnded?.Invoke(this, EventArgs.Empty);
        }

        if (!_replyFired && now >= _session.ReplyAt)
        {
            _replyFired = true;
            _log.Info("返信時刻になりました。");
            ReplyTimeReached?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Dispose() => Stop();
}
