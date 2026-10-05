using Microsoft.Extensions.Logging;

namespace ConnectFlyout.App;

/// <summary>
/// Every log message the app shell writes, as source-generated LoggerMessage delegates.
/// </summary>
internal static partial class AppLog
{
    [LoggerMessage(EventId = 900, Message = "{Message}")]
    public static partial void Native(ILogger logger, LogLevel level, string message);

    [LoggerMessage(EventId = 901, Level = LogLevel.Information, Message = "ConnectFlyout {Version} started")]
    public static partial void Started(ILogger logger, string version);

    [LoggerMessage(EventId = 902, Level = LogLevel.Information, Message = "ConnectFlyout quit")]
    public static partial void Quit(ILogger logger);

    [LoggerMessage(EventId = 903, Level = LogLevel.Critical, Message = "Unhandled exception")]
    public static partial void Unhandled(ILogger logger, Exception? exception);

    [LoggerMessage(EventId = 904, Level = LogLevel.Warning, Message = "Unobserved task exception")]
    public static partial void UnobservedTask(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 905, Level = LogLevel.Warning, Message = "App notifications aren't available")]
    public static partial void NotificationsUnavailable(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 906, Level = LogLevel.Warning, Message = "Couldn't open the log folder")]
    public static partial void OpenFolderFailed(ILogger logger, Exception exception);
}
