namespace ConnectFlyout.Presentation.Common;

/// <summary>
/// Runs work on the thread that created it (the UI thread in the app).
/// </summary>
/// <remarks>
/// Captures <see cref="SynchronizationContext.Current"/> at construction. Tests have no
/// context, so work runs inline.
/// </remarks>
public sealed class UiContext
{
    private readonly SynchronizationContext? _context = SynchronizationContext.Current;

    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (_context is null || SynchronizationContext.Current == _context)
        {
            action();
            return;
        }
        _context.Post(static state => ((Action)state!)(), action);
    }

    /// <summary>
    /// Runs work after the current UI event finishes, even when already on the UI thread.
    /// Used when reacting to a collection change that controls bound to the same collection
    /// haven't seen yet.
    /// </summary>
    public void Defer(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (_context is null)
        {
            action();
            return;
        }
        _context.Post(static state => ((Action)state!)(), action);
    }
}
