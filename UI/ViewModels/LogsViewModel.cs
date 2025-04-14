using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using StreamWeaver.Core.Models;
using StreamWeaver.Core.Services;

namespace StreamWeaver.UI.ViewModels;

public partial class LogsViewModel : ObservableObject, IDisposable
{
    private readonly ILogger<LogsViewModel> _logger;
    private readonly LogViewerService _logViewerService;
    private readonly DispatcherQueue _dispatcherQueue;
    private bool _disposed = false;

    public ObservableCollection<LogEntry> AllLogEntries => _logViewerService.LogEntries;

    [ObservableProperty]
    public partial ObservableCollection<LogEntry> FilteredLogEntries { get; set; } = [];

    [ObservableProperty]
    public partial bool ShowDebug { get; set; } = true;

    [ObservableProperty]
    public partial bool ShowInfo { get; set; } = true;

    [ObservableProperty]
    public partial bool ShowWarn { get; set; } = true;

    [ObservableProperty]
    public partial bool ShowError { get; set; } = true;

    public LogsViewModel(ILogger<LogsViewModel> logger, LogViewerService logViewerService)
    {
        _logger = logger;
        _logViewerService = logViewerService;
        _dispatcherQueue =
            DispatcherQueue.GetForCurrentThread()
            ?? throw new InvalidOperationException($"{nameof(LogsViewModel)} must be initialized on the UI thread.");

        _logger.LogInformation("Initializing LogsViewModel.");
        AllLogEntries.CollectionChanged += AllLogEntries_CollectionChanged;
        RebuildFilteredLogs();
        PropertyChanged += ViewModel_PropertyChanged;
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ShowDebug) or nameof(ShowInfo) or nameof(ShowWarn) or nameof(ShowError))
        {
            _logger.LogDebug("Filter property changed: {PropertyName}. Rebuilding filtered logs.", e.PropertyName);
            RebuildFilteredLogs();
        }
    }

    /// <summary>
    /// Handles changes to the source log collection incrementally.
    /// </summary>
    private void AllLogEntries_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_disposed)
            return;

        _dispatcherQueue.TryEnqueue(() =>
        {
            if (_disposed)
                return;

            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Add:
                    if (e.NewItems != null)
                    {
                        foreach (LogEntry? newItem in e.NewItems.OfType<LogEntry>())
                        {
                            if (newItem != null && ShouldDisplayLog(newItem))
                            {
                                FilteredLogEntries.Add(newItem);
                                _logger.LogTrace(
                                    "Added new log entry to filtered view: Level={Level}, Category={Category}",
                                    newItem.Level,
                                    newItem.Category
                                );
                            }
                        }
                    }

                    break;

                case NotifyCollectionChangedAction.Remove:
                    if (e.OldItems != null)
                    {
                        foreach (LogEntry? oldItem in e.OldItems.OfType<LogEntry>())
                        {
                            if (oldItem != null)
                            {
                                bool removed = FilteredLogEntries.Remove(oldItem);
                                if (removed)
                                    _logger.LogTrace("Removed log entry from filtered view due to source removal.");
                            }
                        }
                    }

                    break;

                case NotifyCollectionChangedAction.Reset:
                    _logger.LogInformation("Source log collection cleared. Clearing filtered view.");
                    FilteredLogEntries.Clear();
                    break;

                case NotifyCollectionChangedAction.Replace:
                case NotifyCollectionChangedAction.Move:
                default:

                    _logger.LogDebug("Unhandled collection action ({Action}) or complex change detected. Rebuilding filtered logs.", e.Action);
                    RebuildFilteredLogs();
                    break;
            }
        });
    }

    /// <summary>
    /// Determines if a log entry should be displayed based on current filters.
    /// </summary>
    private bool ShouldDisplayLog(LogEntry entry) =>
        entry.Level switch
        {
            LogLevel.Trace => ShowDebug,
            LogLevel.Debug => ShowDebug,
            LogLevel.Information => ShowInfo,
            LogLevel.Warning => ShowWarn,
            LogLevel.Error => ShowError,
            LogLevel.Critical => ShowError,
            LogLevel.None => false,
            _ => true,
        };

    /// <summary>
    /// Clears and rebuilds the FilteredLogEntries collection based on AllLogEntries and current filters.
    /// Used for initial load and when filter criteria change. Now replaces the collection instance.
    /// </summary>
    private void RebuildFilteredLogs() =>
        _dispatcherQueue.TryEnqueue(() =>
        {
            if (_disposed)
                return;
            _logger.LogDebug("Rebuilding filtered log entries...");
            try
            {
                var newFilteredList = AllLogEntries.Where(ShouldDisplayLog).ToList();

                FilteredLogEntries = [.. newFilteredList];
                _logger.LogInformation("Filtered log entries rebuilt (Collection Replaced). Count: {Count}", FilteredLogEntries.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during filtered log rebuild.");
            }
        });

    [RelayCommand]
    private void ClearLogs()
    {
        _logger.LogInformation("ClearLogs command executed.");
        _logViewerService.ClearLogs();
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _logger.LogInformation("Disposing LogsViewModel...");
        if (AllLogEntries != null)
        {
            try
            {
                AllLogEntries.CollectionChanged -= AllLogEntries_CollectionChanged;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Exception unsubscribing from AllLogEntries.CollectionChanged.");
            }
        }

        PropertyChanged -= ViewModel_PropertyChanged;

        _dispatcherQueue?.TryEnqueue(() => FilteredLogEntries?.Clear());
        _logger.LogInformation("LogsViewModel Dispose finished.");
        GC.SuppressFinalize(this);
    }
}
