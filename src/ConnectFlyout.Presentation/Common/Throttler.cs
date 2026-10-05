namespace ConnectFlyout.Presentation.Common;

/// <summary>
/// Runs at most one action per interval and always runs the last one it was given.
/// </summary>
/// <remarks>
/// Used for sliders: dragging sends a command right away, then at most one more per interval,
/// and the value where the drag stops is always sent. Actions run on the thread pool (or the
/// fake clock's thread in tests) and must handle their own errors.
/// </remarks>
public sealed class Throttler : IDisposable
{
    private readonly TimeSpan _interval;
    private readonly TimeProvider _timeProvider;
    private readonly Lock _gate = new();

    private Func<Task>? _pending;
    private ITimer? _timer;
    private long _lastRun;
    private bool _hasRun;

    public Throttler(TimeSpan interval, TimeProvider timeProvider)
    {
        _interval = interval;
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// True while an action is waiting for its turn.
    /// </summary>
    public bool HasPending
    {
        get
        {
            lock (_gate)
            {
                return _pending is not null;
            }
        }
    }

    public void Run(Func<Task> action)
    {
        lock (_gate)
        {
            var now = _timeProvider.GetTimestamp();
            var elapsed = _hasRun ? _timeProvider.GetElapsedTime(_lastRun, now) : TimeSpan.MaxValue;

            if (_timer is null && elapsed >= _interval)
            {
                _lastRun = now;
                _hasRun = true;
                _ = action();
                return;
            }

            _pending = action;
            if (_timer is not null)
            {
                return;
            }
            _timer = _timeProvider.CreateTimer(_ => Flush(), null, _interval - elapsed, Timeout.InfiniteTimeSpan);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _timer?.Dispose();
            _timer = null;
            _pending = null;
        }
    }

    private void Flush()
    {
        Func<Task>? action;
        lock (_gate)
        {
            action = _pending;
            _pending = null;
            _timer?.Dispose();
            _timer = null;
            _lastRun = _timeProvider.GetTimestamp();
            _hasRun = true;
        }
        _ = action?.Invoke();
    }
}
