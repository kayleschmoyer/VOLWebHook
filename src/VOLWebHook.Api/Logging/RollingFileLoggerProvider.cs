using System.Collections.Concurrent;
using System.Text;
using VOLWebHook.Api.Configuration;

namespace VOLWebHook.Api.Logging;

public sealed class RollingFileLoggerProvider : ILoggerProvider
{
    private readonly LoggingSettings _settings;
    private readonly ConcurrentDictionary<string, RollingFileLogger> _loggers = new();
    private readonly RollingFileWriter _fileWriter;

    public RollingFileLoggerProvider(LoggingSettings settings)
    {
        _settings = settings;
        _fileWriter = new RollingFileWriter(settings);
    }

    public ILogger CreateLogger(string categoryName)
    {
        return _loggers.GetOrAdd(categoryName, name => new RollingFileLogger(name, _fileWriter, _settings));
    }

    public void Dispose()
    {
        _fileWriter.Dispose();
        _loggers.Clear();
    }
}

internal sealed class RollingFileLogger : ILogger
{
    private readonly string _categoryName;
    private readonly RollingFileWriter _fileWriter;
    private readonly LoggingSettings _settings;

    public RollingFileLogger(string categoryName, RollingFileWriter fileWriter, LoggingSettings settings)
    {
        _categoryName = categoryName;
        _fileWriter = fileWriter;
        _settings = settings;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
            return;

        var timestamp = DateTime.UtcNow;
        var level = logLevel.ToString().ToUpperInvariant().PadRight(11);
        var message = formatter(state, exception);
        var category = ShortenCategoryName(_categoryName);

        var logLine = new StringBuilder()
            .Append(timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff"))
            .Append(" UTC | ")
            .Append(level)
            .Append(" | ")
            .Append(category)
            .Append(" | ")
            .Append(message);

        if (exception != null)
        {
            logLine.AppendLine();
            logLine.Append("    Exception: ");
            logLine.Append(exception.GetType().FullName);
            logLine.Append(": ");
            logLine.AppendLine(exception.Message);
            logLine.Append("    StackTrace: ");
            logLine.Append(exception.StackTrace);
        }

        _fileWriter.WriteLine(logLine.ToString());
    }

    private static string ShortenCategoryName(string categoryName)
    {
        var lastDot = categoryName.LastIndexOf('.');
        return lastDot >= 0 ? categoryName[(lastDot + 1)..] : categoryName;
    }
}

internal sealed class RollingFileWriter : IDisposable
{
    private readonly LoggingSettings _settings;
    private readonly object _lock = new();
    private StreamWriter? _currentWriter;
    private string? _currentFilePath;
    private DateTime _currentDate;
    private long _currentFileSize;

    public RollingFileWriter(LoggingSettings settings)
    {
        _settings = settings;
        EnsureDirectoryExists();
    }

    public void WriteLine(string message)
    {
        lock (_lock)
        {
            EnsureWriter();
            if (_currentWriter == null)
                return;

            _currentWriter.WriteLine(message);
            _currentWriter.Flush();
            _currentFileSize += Encoding.UTF8.GetByteCount(message) + Environment.NewLine.Length;

            if (_currentFileSize >= _settings.MaxFileSizeBytes)
            {
                RollOver();
            }
        }
    }

    private void EnsureDirectoryExists()
    {
        if (!Directory.Exists(_settings.LogDirectory))
        {
            Directory.CreateDirectory(_settings.LogDirectory);
        }
    }

    private void EnsureWriter()
    {
        var today = DateTime.UtcNow.Date;

        if (_currentWriter != null && _currentDate == today)
            return;

        CloseCurrentWriter();

        _currentDate = today;
        var fileName = _settings.FileNamePattern.Replace("{date}", today.ToString("yyyy-MM-dd"));
        _currentFilePath = Path.Combine(_settings.LogDirectory, fileName);

        try
        {
            var fileInfo = new FileInfo(_currentFilePath);
            _currentFileSize = fileInfo.Exists ? fileInfo.Length : 0;
            _currentWriter = new StreamWriter(_currentFilePath, append: true, Encoding.UTF8);
        }
        catch
        {
            _currentWriter = null;
        }
    }

    private void RollOver()
    {
        CloseCurrentWriter();

        if (_currentFilePath != null && File.Exists(_currentFilePath))
        {
            var timestamp = DateTime.UtcNow.ToString("HHmmss");
            var directory = Path.GetDirectoryName(_currentFilePath)!;
            var fileNameWithoutExt = Path.GetFileNameWithoutExtension(_currentFilePath);
            var extension = Path.GetExtension(_currentFilePath);
            var newPath = Path.Combine(directory, $"{fileNameWithoutExt}_{timestamp}{extension}");

            try
            {
                File.Move(_currentFilePath, newPath);
            }
            catch
            {
                // Ignore move failures
            }
        }

        _currentFileSize = 0;
    }

    private void CloseCurrentWriter()
    {
        if (_currentWriter != null)
        {
            try
            {
                _currentWriter.Flush();
                _currentWriter.Dispose();
            }
            catch
            {
                // Ignore close failures
            }
            _currentWriter = null;
        }
    }

    public void Dispose()
    {
        CloseCurrentWriter();
    }
}

public static class RollingFileLoggerExtensions
{
    public static ILoggingBuilder AddRollingFile(this ILoggingBuilder builder, LoggingSettings settings)
    {
        if (settings.Enabled)
        {
            builder.AddProvider(new RollingFileLoggerProvider(settings));
        }
        return builder;
    }
}
