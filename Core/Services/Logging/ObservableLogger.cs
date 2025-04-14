using Microsoft.Extensions.Logging;
using StreamWeaver.Core.Models;

namespace StreamWeaver.Core.Services.Logging;

/// <summary>
/// Custom ILogger implementation that sends log entries to the LogViewerService.
/// </summary>
public sealed class ObservableLogger(string categoryName, LogViewerService logViewerService) : ILogger
{
    private readonly string _categoryName = categoryName;
    private readonly LogViewerService _logViewerService = logViewerService;

    // Scope handling is not implemented in this simple logger
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => default!;

    // Check if a log level is enabled (respects filtering configured in App.xaml.cs)
    public bool IsEnabled(LogLevel logLevel) =>
        // For this provider, we generally want to capture everything passed to it,
        // assuming filtering is done upstream via AddFilter or minimum level settings.
        // You *could* add explicit filtering here based on build configuration too.
        logLevel != LogLevel.None;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        // Format the message using the provided formatter
        string message = formatter(state, exception);

        LogEntry entry = new(Timestamp: DateTime.Now, Level: logLevel, Category: _categoryName, Message: message, Exception: exception);

        _logViewerService.AddLogEntry(entry);
    }
}
