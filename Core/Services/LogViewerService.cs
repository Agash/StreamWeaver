using System.Collections.ObjectModel;
using Microsoft.UI.Dispatching;
using StreamWeaver.Core.Models;

namespace StreamWeaver.Core.Services;

/// <summary>
/// Simple singleton service to hold the observable collection of log entries
/// for the UI, ensuring updates are dispatched to the UI thread.
/// </summary>
public class LogViewerService
{
    private const int MAX_LOG_ENTRIES = 500;
    private readonly DispatcherQueue _dispatcherQueue;

    public ObservableCollection<LogEntry> LogEntries { get; } = [];

    public LogViewerService() =>
        _dispatcherQueue =
            DispatcherQueue.GetForCurrentThread() ?? throw new InvalidOperationException("LogViewerService must be initialized on the UI thread.");

    public void AddLogEntry(LogEntry entry) =>
        _dispatcherQueue.TryEnqueue(() =>
        {
            LogEntries.Add(entry);
            while (LogEntries.Count > MAX_LOG_ENTRIES)
            {
                LogEntries.RemoveAt(0);
            }
        });

    public void ClearLogs() => _dispatcherQueue.TryEnqueue(LogEntries.Clear);
}
