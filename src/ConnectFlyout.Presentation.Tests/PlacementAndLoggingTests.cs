using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using ConnectFlyout.Presentation.Common;
using ConnectFlyout.Presentation.Logging;
using ConnectFlyout.Presentation.Placement;

namespace ConnectFlyout.Presentation.Tests;

[TestClass]
public sealed class FlyoutPlacementTests
{
    private static readonly PixelRect Monitor = new(0, 0, 1920, 1080);

    [TestMethod]
    public void BottomTaskbarPutsFlyoutBottomRight()
    {
        var workArea = new PixelRect(0, 0, 1920, 1032);

        Assert.AreEqual(TaskbarEdge.Bottom, FlyoutPlacement.DetectEdge(Monitor, workArea));
        Assert.AreEqual(new PixelRect(1548, 260, 1908, 1020), FlyoutPlacement.Calculate(Monitor, workArea, 1.0));
    }

    [TestMethod]
    public void TopTaskbarPutsFlyoutTopRight()
    {
        var workArea = new PixelRect(0, 48, 1920, 1080);

        Assert.AreEqual(TaskbarEdge.Top, FlyoutPlacement.DetectEdge(Monitor, workArea));
        Assert.AreEqual(new PixelRect(1548, 60, 1908, 820), FlyoutPlacement.Calculate(Monitor, workArea, 1.0));
    }

    [TestMethod]
    public void LeftTaskbarStillPutsFlyoutOnTheRight()
    {
        var workArea = new PixelRect(48, 0, 1920, 1080);

        Assert.AreEqual(TaskbarEdge.Left, FlyoutPlacement.DetectEdge(Monitor, workArea));
        Assert.AreEqual(new PixelRect(1548, 308, 1908, 1068), FlyoutPlacement.Calculate(Monitor, workArea, 1.0));
    }

    [TestMethod]
    public void RightTaskbarKeepsFlyoutLeftOfIt()
    {
        var workArea = new PixelRect(0, 0, 1872, 1080);

        Assert.AreEqual(TaskbarEdge.Right, FlyoutPlacement.DetectEdge(Monitor, workArea));
        Assert.AreEqual(new PixelRect(1500, 308, 1860, 1068), FlyoutPlacement.Calculate(Monitor, workArea, 1.0));
    }

    [TestMethod]
    public void ContentHeightSetsFlyoutHeight()
    {
        var bottom = new PixelRect(0, 0, 1920, 1032);
        var top = new PixelRect(0, 48, 1920, 1080);

        Assert.AreEqual(new PixelRect(1548, 520, 1908, 1020), FlyoutPlacement.Calculate(Monitor, bottom, 1.0, 500));
        Assert.AreEqual(new PixelRect(1548, 60, 1908, 560), FlyoutPlacement.Calculate(Monitor, top, 1.0, 500));
    }

    [TestMethod]
    public void AutoHiddenTaskbarCountsAsBottom() =>
        Assert.AreEqual(TaskbarEdge.Bottom, FlyoutPlacement.DetectEdge(Monitor, Monitor));

    [TestMethod]
    public void ScalesWithDpi()
    {
        var monitor = new PixelRect(0, 0, 2880, 1620);
        var workArea = new PixelRect(0, 0, 2880, 1548);

        Assert.AreEqual(new PixelRect(2322, 390, 2862, 1530), FlyoutPlacement.Calculate(monitor, workArea, 1.5));
    }

    [TestMethod]
    public void SecondaryMonitorUsesItsOwnCoordinates()
    {
        var monitor = new PixelRect(-1920, 0, 0, 1080);
        var workArea = new PixelRect(-1920, 0, 0, 1032);

        Assert.AreEqual(new PixelRect(-372, 260, -12, 1020), FlyoutPlacement.Calculate(monitor, workArea, 1.0));
    }

    [TestMethod]
    public void ShrinksToFitShortWorkArea()
    {
        var monitor = new PixelRect(0, 0, 1280, 600);
        var workArea = new PixelRect(0, 0, 1280, 552);

        var rect = FlyoutPlacement.Calculate(monitor, workArea, 2.0);

        Assert.IsTrue(rect.Top >= workArea.Top && rect.Bottom <= workArea.Bottom, $"{rect} leaves {workArea}");
        Assert.AreEqual(1280 - 24, rect.Right);
    }
}

[TestClass]
public sealed class RollingFileLoggerTests
{
    private string _directory = "";

    [TestInitialize]
    public void CreateDirectory() => _directory = Path.Combine(Path.GetTempPath(), "ConnectFlyout-tests", Guid.NewGuid().ToString("N"));

    [TestCleanup]
    public void DeleteDirectory()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, true);
        }
    }

    [TestMethod]
    public void WritesFormattedLine()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 9, 23, 15, 4, 5, 123, TimeSpan.Zero));
        using var provider = new RollingFileLoggerProvider(_directory, new LogLevelSwitch(), time);

        provider.CreateLogger("Tests").Log(LogLevel.Information, "hello {Name}", "world");

        Assert.AreEqual("2026-09-23T15:04:05.123Z [Information] Tests: hello world" + Environment.NewLine, File.ReadAllText(provider.CurrentFilePath));
    }

    [TestMethod]
    public void SkipsDebugUntilSwitchAllowsIt()
    {
        var levelSwitch = new LogLevelSwitch();
        using var provider = new RollingFileLoggerProvider(_directory, levelSwitch, TimeProvider.System);
        var logger = provider.CreateLogger("Tests");

        logger.Log(LogLevel.Debug, "hidden");
        Assert.IsFalse(File.Exists(provider.CurrentFilePath));

        levelSwitch.MinimumLevel = LogLevel.Debug;
        logger.Log(LogLevel.Debug, "shown");
        StringAssert.Contains(File.ReadAllText(provider.CurrentFilePath), "shown");
    }

    [TestMethod]
    public void RollsAndKeepsFiveFiles()
    {
        using var provider = new RollingFileLoggerProvider(_directory, new LogLevelSwitch(), TimeProvider.System, maxFileBytes: 200, maxFiles: 5);
        var logger = provider.CreateLogger("Tests");

        for (var i = 0; i < 40; i++)
        {
            logger.Log(LogLevel.Information, "line {Number} padded to take up room in the file", i);
        }

        var files = Directory.GetFiles(_directory).Select(Path.GetFileName).Order().ToList();
        CollectionAssert.AreEqual(
            new[] { "ConnectFlyout.1.log", "ConnectFlyout.2.log", "ConnectFlyout.3.log", "ConnectFlyout.4.log", "ConnectFlyout.log" },
            files);
        Assert.IsTrue(Directory.GetFiles(_directory).All(file => new FileInfo(file).Length <= 200));
        StringAssert.Contains(File.ReadAllText(provider.CurrentFilePath), "line 39");
    }
}

[TestClass]
public sealed class ThrottlerTests
{
    private readonly FakeTimeProvider _time = new();

    [TestMethod]
    public void RunsFirstActionRightAway()
    {
        using var throttler = new Throttler(TimeSpan.FromMilliseconds(150), _time);
        var runs = new List<int>();

        throttler.Run(Record(runs, 1));

        CollectionAssert.AreEqual(new[] { 1 }, runs);
    }

    [TestMethod]
    public void CollapsesBurstIntoLastValue()
    {
        using var throttler = new Throttler(TimeSpan.FromMilliseconds(150), _time);
        var runs = new List<int>();

        throttler.Run(Record(runs, 1));
        throttler.Run(Record(runs, 2));
        throttler.Run(Record(runs, 3));
        Assert.IsTrue(throttler.HasPending);

        _time.Advance(TimeSpan.FromMilliseconds(150));

        CollectionAssert.AreEqual(new[] { 1, 3 }, runs);
        Assert.IsFalse(throttler.HasPending);
    }

    [TestMethod]
    public void RunsImmediatelyAgainAfterQuietInterval()
    {
        using var throttler = new Throttler(TimeSpan.FromMilliseconds(150), _time);
        var runs = new List<int>();

        throttler.Run(Record(runs, 1));
        _time.Advance(TimeSpan.FromMilliseconds(200));
        throttler.Run(Record(runs, 2));

        CollectionAssert.AreEqual(new[] { 1, 2 }, runs);
    }

    [TestMethod]
    public void NeverRunsMoreThanOncePerInterval()
    {
        using var throttler = new Throttler(TimeSpan.FromMilliseconds(150), _time);
        var runs = new List<int>();

        for (var i = 0; i < 30; i++)
        {
            throttler.Run(Record(runs, i));
            _time.Advance(TimeSpan.FromMilliseconds(10));
        }
        _time.Advance(TimeSpan.FromMilliseconds(150));

        Assert.IsTrue(runs.Count <= 4, $"ran {runs.Count} times in 450 ms");
        Assert.AreEqual(29, runs[^1]);
    }

    private static Func<Task> Record(List<int> runs, int value) => () =>
    {
        runs.Add(value);
        return Task.CompletedTask;
    };
}
