using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;

namespace Application.Logging;

// Debug/Info/Warn/Error that also record the calling file, method and line (like __FILE__/__func__/__LINE__).
public static class LoggerExtensions
{
    public static void Debug(this ILogger logger, string message,
        [CallerMemberName] string member = "", [CallerFilePath] string file = "", [CallerLineNumber] int line = 0) =>
        Write(logger, LogLevel.Debug, null, message, member, file, line);

    public static void Info(this ILogger logger, string message,
        [CallerMemberName] string member = "", [CallerFilePath] string file = "", [CallerLineNumber] int line = 0) =>
        Write(logger, LogLevel.Information, null, message, member, file, line);

    public static void Warn(this ILogger logger, string message,
        [CallerMemberName] string member = "", [CallerFilePath] string file = "", [CallerLineNumber] int line = 0) =>
        Write(logger, LogLevel.Warning, null, message, member, file, line);

    public static void Error(this ILogger logger, string message,
        [CallerMemberName] string member = "", [CallerFilePath] string file = "", [CallerLineNumber] int line = 0) =>
        Write(logger, LogLevel.Error, null, message, member, file, line);

    public static void Error(this ILogger logger, Exception exception, string message,
        [CallerMemberName] string member = "", [CallerFilePath] string file = "", [CallerLineNumber] int line = 0) =>
        Write(logger, LogLevel.Error, exception, message, member, file, line);

    private static void Write(ILogger logger, LogLevel level, Exception? exception, string message,
        string member, string file, int line)
    {
        if (!logger.IsEnabled(level))
            return;

        // CallerFilePath is the build machine's path; keep only the file name (handles / and \).
        string fileName = file[(file.LastIndexOfAny(['/', '\\']) + 1)..];
        CallerLogState state = new(message, member, fileName, line, RequestContext.RequestId);
        logger.Log(level, default, state, exception, static (s, _) => s.Message);
    }
}
