using System.Globalization;
using System.Text;
using System.Threading.Channels;
using Application.Logging;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Logging;

// Producer/consumer logger: every ILogger.Log call does a non-blocking TryWrite into a bounded channel;
// one background task drains the channel to the output (stdout). Full channel => oldest entry is dropped.
// Shutdown is best effort: entries still queued when the process exits may be lost.
public sealed class ChannelLoggerProvider : ILoggerProvider
{
    private readonly Channel<LogEntry> _channel;
    private readonly TextWriter _output;
    private readonly Task _consumer;

    public ChannelLoggerProvider(TextWriter output, int capacity)
    {
        _output = output;
        _channel = Channel.CreateBounded<LogEntry>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });
        _consumer = Task.Run(ConsumeAsync);
    }

    public ILogger CreateLogger(string categoryName) => new ChannelLogger(categoryName, _channel.Writer);

    public void Dispose() => _channel.Writer.TryComplete();

    private async Task ConsumeAsync()
    {
        await foreach (LogEntry entry in _channel.Reader.ReadAllAsync())
        {
            await _output.WriteLineAsync(Format(entry));
        }
    }

    private static string Format(LogEntry entry)
    {
        StringBuilder sb = new();
        sb.Append(entry.Timestamp.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture))
            .Append(' ').Append(LevelName(entry.Level))
            .Append(" [").Append(entry.RequestId ?? "-").Append("] ")
            .Append(entry.Category);

        if (entry.Caller is not null)
            sb.Append(' ').Append(entry.Caller);

        sb.Append(" - ").Append(entry.Message);

        if (entry.Exception is not null)
            sb.AppendLine().Append(entry.Exception);

        return sb.ToString();
    }

    private static string LevelName(LogLevel level) => level switch
    {
        LogLevel.Trace => "TRACE",
        LogLevel.Debug => "DEBUG",
        LogLevel.Information => "INFO ",
        LogLevel.Warning => "WARN ",
        LogLevel.Error => "ERROR",
        LogLevel.Critical => "CRIT ",
        _ => "NONE "
    };

    private sealed record LogEntry(
        DateTime Timestamp,
        LogLevel Level,
        string Category,
        string? RequestId,
        string? Caller,
        string Message,
        Exception? Exception);

    private sealed class ChannelLogger : ILogger
    {
        private readonly string _category;
        private readonly ChannelWriter<LogEntry> _writer;

        public ChannelLogger(string category, ChannelWriter<LogEntry> writer)
        {
            _category = category;
            _writer = writer;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;

            string? caller = null;
            string? requestId = RequestContext.RequestId;
            if (state is CallerLogState callerState)
            {
                caller = $"{callerState.File}:{callerState.Member}:{callerState.Line}";
                requestId = callerState.RequestId;
            }

            _writer.TryWrite(new LogEntry(
                DateTime.UtcNow, logLevel, _category, requestId, caller, formatter(state, exception), exception));
        }
    }
}
