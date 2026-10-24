using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

[assembly: DoNotParallelize]

namespace ConnectFlyout.App.UITests;

/// <summary>
/// One WinUI thread for the whole run: a host window whose content the tests swap. Test bodies
/// run on that thread through <see cref="RunAsync"/>, so awaits land back on it like in the app.
/// </summary>
[TestClass]
public static class UiHost
{
    private static readonly ManualResetEventSlim Ready = new();
    private static Thread? _thread;

    public static Window Window { get; private set; } = null!;

    /// <summary>
    /// Where a test puts the element under test; cleared between tests.
    /// </summary>
    public static Grid Root { get; private set; } = null!;

    private static DispatcherQueue Dispatcher => Window.DispatcherQueue;

    /// <summary>
    /// Writes a line to the console and to %TEMP%\ConnectFlyout.UITests.log, which survives a
    /// crash of the test host (whose console output doesn't).
    /// </summary>
    public static void Note(string line)
    {
        Console.WriteLine(line);
        try
        {
            File.AppendAllText(Path.Combine(Path.GetTempPath(), "ConnectFlyout.UITests.log"), $"{DateTime.Now:HH:mm:ss.fff} {line}{Environment.NewLine}");
        }
        catch (IOException)
        {
        }
    }

    [AssemblyInitialize]
    public static void Start(TestContext context)
    {
        Note($"starting the host in {Environment.ProcessPath} from {AppContext.BaseDirectory}");
        _thread = new Thread(() =>
        {
            try
            {
                WinRT.ComWrappersSupport.InitializeComWrappers();
                Application.Start(parameters =>
                {
                    SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
                    _ = new TestApp();
                });
                Note("the host thread finished");
            }
            catch (Exception exception)
            {
                Note("the host thread failed: " + exception);
            }
        });
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.IsBackground = true;
        _thread.Start();
        if (!Ready.Wait(TimeSpan.FromSeconds(60)))
        {
            throw new TimeoutException("The WinUI host window did not open.");
        }
    }

    internal static void OnLaunched()
    {
        Note("launched");
        Root = new Grid();
        Window = new Window { Content = Root };
        // Tall enough that every settings card realizes without scrolling
        Window.AppWindow.Resize(new Windows.Graphics.SizeInt32(1200, 900));
        Window.Activate();
        Ready.Set();
    }

    [AssemblyCleanup]
    public static void Stop()
    {
        if (Window is null)
        {
            return;
        }
        Dispatcher.TryEnqueue(() =>
        {
            Window.Close();
            Application.Current.Exit();
        });
        _thread?.Join(TimeSpan.FromSeconds(10));
    }

    /// <summary>
    /// Runs a test body on the UI thread; its exception fails the test.
    /// </summary>
    public static Task RunAsync(Func<Task> body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var done = new TaskCompletionSource();
        if (!Dispatcher.TryEnqueue(async () =>
        {
            try
            {
                await body();
                done.SetResult();
            }
            catch (Exception exception)
            {
                done.SetException(exception);
            }
        }))
        {
            done.SetException(new InvalidOperationException("The UI thread is gone."));
        }
        return done.Task;
    }

    /// <summary>
    /// Shows the element in the host window (at the width given, or stretched), waits for it to
    /// load and for the layout to settle.
    /// </summary>
    public static async Task<T> LoadAsync<T>(T element, double? width = null) where T : FrameworkElement
    {
        ArgumentNullException.ThrowIfNull(element);
        var loaded = new TaskCompletionSource();
        element.Loaded += (_, _) => loaded.TrySetResult();
        Root.Children.Clear();
        Root.Children.Add(width is { } w ? new Border { Width = w, VerticalAlignment = VerticalAlignment.Top, Child = element } : element);
        await loaded.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await IdleAsync();
        return element;
    }

    /// <summary>
    /// Lays out and lets queued UI work (Loaded handlers, bindings, TextChanged) run.
    /// </summary>
    public static async Task IdleAsync()
    {
        Root.UpdateLayout();
        var idle = new TaskCompletionSource();
        Dispatcher.TryEnqueue(DispatcherQueuePriority.Low, () => idle.SetResult());
        await idle.Task;
        Root.UpdateLayout();
    }

    /// <summary>
    /// Waits for a condition, laying out between looks.
    /// </summary>
    public static async Task<bool> UntilAsync(Func<bool> condition, int timeoutMilliseconds = 3000)
    {
        var deadline = Environment.TickCount64 + timeoutMilliseconds;
        while (!condition() && Environment.TickCount64 < deadline)
        {
            await Task.Delay(15);
            await IdleAsync();
        }
        return condition();
    }

    /// <summary>
    /// Clears the host, closing any dialog left open (WinUI allows one at a time).
    /// </summary>
    public static async Task ClearAsync()
    {
        foreach (var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(Root.XamlRoot))
        {
            if (popup.Child is ContentDialog dialog)
            {
                dialog.Hide();
            }
        }
        Root.Children.Clear();
        await IdleAsync();
    }
}
