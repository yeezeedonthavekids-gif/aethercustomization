using System.Collections.Concurrent;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Aether.Services.Logging;

public sealed record LogRecord(DateTimeOffset Timestamp, LogLevel Level, string Category, string Message, string? Exception);

/// <summary>
/// Writes daily log files under %LocalAppData%\Aether\logs and keeps the latest entries in memory for the
/// Diagnostics page. Writes are buffered on a background thread so logging never blocks the UI.
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _directory;
    private readonly BlockingCollection<LogRecord> _queue = new(boundedCapacity: 10_000);
    private readonly ConcurrentQueue<LogRecord> _recent = new();
    private readonly Thread _writer;
    private readonly int _retentionDays;

    public FileLoggerProvider(string directory, Func<LogLevel> minimumLevel, int retentionDays = 14)
    {
        _directory = directory;
        MinimumLevel = minimumLevel;
        _retentionDays = retentionDays;
        Directory.CreateDirectory(directory);
        _writer = new Thread(WriteLoop) { IsBackground = true, Name = "AETHER log writer" };
        _writer.Start();
        PruneOldFiles();
    }

    public Func<LogLevel> MinimumLevel { get; }
    public event Action<LogRecord>? Logged;

    public IReadOnlyList<LogRecord> Recent => _recent.ToArray();
    public string Directory_ => _directory;
    public string CurrentFile => Path.Combine(_directory, $"aether-{DateTime.Now:yyyyMMdd}.log");

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    internal void Enqueue(LogRecord record)
    {
        _recent.Enqueue(record);
        while (_recent.Count > 500 && _recent.TryDequeue(out _)) { }
        _queue.TryAdd(record);
        Logged?.Invoke(record);
    }

    private void WriteLoop()
    {
        foreach (var record in _queue.GetConsumingEnumerable())
        {
            try
            {
                var sb = new StringBuilder();
                sb.Append(record.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff")).Append(' ')
                  .Append(record.Level.ToString().ToUpperInvariant()[..4]).Append(' ')
                  .Append(record.Category).Append(": ").Append(record.Message);
                if (record.Exception is not null) sb.AppendLine().Append(record.Exception);
                File.AppendAllText(CurrentFile, sb.AppendLine().ToString());
            }
            catch (IOException) { /* the log file is locked; drop the line rather than crash */ }
        }
    }

    private void PruneOldFiles()
    {
        try
        {
            foreach (var f in Directory.GetFiles(_directory, "aether-*.log"))
                if (File.GetLastWriteTime(f) < DateTime.Now.AddDays(-_retentionDays)) File.Delete(f);
        }
        catch (IOException) { }
    }

    public void Dispose()
    {
        _queue.CompleteAdding();
        _writer.Join(TimeSpan.FromSeconds(2));
    }

    private sealed class FileLogger(FileLoggerProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None && logLevel >= provider.MinimumLevel();

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            var shortCategory = category.Contains('.') ? category[(category.LastIndexOf('.') + 1)..] : category;
            provider.Enqueue(new LogRecord(DateTimeOffset.Now, logLevel, shortCategory, formatter(state, exception), exception?.ToString()));
        }
    }
}
