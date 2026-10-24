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
