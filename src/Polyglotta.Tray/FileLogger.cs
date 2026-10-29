using System.Collections.Concurrent;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Polyglotta.Tray;

/// <summary>
/// Minimal file logging for the tray agent. A background app has no console, and
/// "it just stopped working" with nowhere to look is the worst way to run one.
/// <para>
/// One file per day, written on a single worker so audio and provider callbacks never
/// wait on the disk.
/// </para>
/// </summary>
public static class FileLogger
{
    public static ILoggerFactory CreateFactory(string directory, LogLevel minimum = LogLevel.Information) =>
        LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(minimum);
            builder.AddProvider(new FileLoggerProvider(directory));
        });
}

internal sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly BlockingCollection<string> _queue = new(new ConcurrentQueue<string>(), boundedCapacity: 4096);
    private readonly Task _writer;
    private readonly string _directory;

    public FileLoggerProvider(string directory)
    {
        _directory = directory;
        Directory.CreateDirectory(_directory);
        _writer = Task.Factory.StartNew(WriteLoop, TaskCreationOptions.LongRunning);
    }

    public ILogger CreateLogger(string categoryName) => new Writer(this, categoryName);

    /// <summary>Drops the line rather than blocking a callback when the disk falls behind.</summary>
    private void Enqueue(string line) => _queue.TryAdd(line);

    private void WriteLoop()
    {
        foreach (var line in _queue.GetConsumingEnumerable())
        {
            try
            {
                var path = Path.Combine(_directory, $"polyglotta-{DateTime.Now:yyyy-MM-dd}.log");
                File.AppendAllText(path, line, Encoding.UTF8);
            }
            catch (IOException)
            {
                // A log that cannot be written must not take the app down with it.
            }
        }
    }

    public void Dispose()
    {
        _queue.CompleteAdding();
        _writer.Wait(TimeSpan.FromSeconds(2));
        _queue.Dispose();
    }

    private sealed class Writer(FileLoggerProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;

            var line = new StringBuilder()
                .Append(DateTime.Now.ToString("HH:mm:ss.fff "))
                .Append(logLevel.ToString().ToLowerInvariant().PadRight(12))
                .Append(category)
                .Append(": ")
                .AppendLine(formatter(state, exception));

            if (exception is not null)
                line.AppendLine(exception.ToString());

            provider.Enqueue(line.ToString());
        }
    }
}
