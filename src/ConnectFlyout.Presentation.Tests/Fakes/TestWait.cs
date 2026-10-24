namespace ConnectFlyout.Presentation.Tests;

/// <summary>
/// Polling helper for work that finishes on another thread. Shared with the UI tests.
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
