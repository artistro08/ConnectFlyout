[assembly: Parallelize(Scope = ExecutionScope.ClassLevel)]

namespace ConnectFlyout.Presentation.Tests;

/// <summary>
/// UI-thread stand-in: posted work queues up until <see cref="RunPending"/>, like the
/// dispatcher running it after the current event finishes.
/// </summary>
internal sealed class QueueingSynchronizationContext : SynchronizationContext
{
    private readonly Queue<(SendOrPostCallback Callback, object? State)> _queue = new();

    public override void Post(SendOrPostCallback d, object? state) => _queue.Enqueue((d, state));

    public void RunPending()
    {
        while (_queue.TryDequeue(out var item))
        {
            item.Callback(item.State);
        }
    }
}

/// <summary>
/// Polling helper for work that finishes on another thread.
/// </summary>
internal static class TestWait
{
    public static async Task<bool> UntilAsync(Func<bool> condition, int timeoutMilliseconds = 2000)
    {
        var deadline = Environment.TickCount64 + timeoutMilliseconds;
        while (Environment.TickCount64 < deadline)
        {
            if (condition())
            {
                return true;
            }
            await Task.Delay(10).ConfigureAwait(false);
        }
        return condition();
    }
}
