using System.Collections.Specialized;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using StreamWeaver.UI.ViewModels;

namespace StreamWeaver.UI.Views;

/// <summary>
/// Page for displaying application logs with filtering capabilities.
/// Includes auto-scrolling behavior similar to the chat view.
/// </summary>
public sealed partial class LogsView : Page
{
    private readonly ILogger<LogsView> _logger;
    private bool _shouldScrollToBottom = true;
    private ScrollViewer? _logScrollViewer;

    public LogsViewModel ViewModel => (LogsViewModel)DataContext;

    public LogsView()
    {
        InitializeComponent();
        try
        {
            DataContext = App.GetService<LogsViewModel>();
            _logger = App.GetService<ILogger<LogsView>>();
            _logger.LogInformation("LogsView initialized and DataContext set.");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LogsView] FATAL: Failed to resolve LogsViewModel or ILogger: {ex}");
            throw;
        }
    }

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        _logger.LogDebug("LogsView Page_Loaded.");
        if (ViewModel?.FilteredLogEntries != null)
        {
            ViewModel.FilteredLogEntries.CollectionChanged -= FilteredLogEntries_CollectionChanged;
            ViewModel.FilteredLogEntries.CollectionChanged += FilteredLogEntries_CollectionChanged;
            _logger.LogDebug("Subscribed to FilteredLogEntries CollectionChanged.");
        }
        else
        {
            _logger.LogWarning("ViewModel or FilteredLogEntries is null on Page_Loaded. CollectionChanged subscription skipped.");
        }

        FindAndHookScrollViewer();
        ScrollToBottom();
    }

    private void FindAndHookScrollViewer()
    {
        if (_logScrollViewer == null)
        {
            _logScrollViewer = FindScrollViewer(LogsListView);
            if (_logScrollViewer != null)
            {
                _logScrollViewer.ViewChanged -= ScrollViewer_ViewChanged;
                _logScrollViewer.ViewChanged += ScrollViewer_ViewChanged;
                _logger.LogInformation("Found and hooked ScrollViewer within LogsListView.");
            }
            else
            {
                _logger.LogWarning(
                    "Could not find the internal ScrollViewer for LogsListView on initial load. Auto-scroll may be delayed or disabled."
                );
            }
        }
        else
        {
            _logScrollViewer.ViewChanged -= ScrollViewer_ViewChanged;
            _logScrollViewer.ViewChanged += ScrollViewer_ViewChanged;
            _logger.LogDebug("LogsView ScrollViewer already cached, ensured ViewChanged event is hooked.");
        }
    }

    private void FilteredLogEntries_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add && _shouldScrollToBottom)
        {
            _logger.LogTrace("New items added to filtered logs and auto-scroll enabled. Enqueueing ScrollToBottom.");
            bool enqueued = DispatcherQueue.TryEnqueue(ScrollToBottom);
            if (!enqueued)
                _logger.LogWarning("Failed to enqueue ScrollToBottom operation for logs.");
        }
        else if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            _logger.LogDebug("Filtered log collection reset. Re-enabling auto-scroll.");
            _shouldScrollToBottom = true;
        }
    }

    // Handle user scrolling to disable/re-enable auto-scroll
    private void ScrollViewer_ViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        if (_logScrollViewer != null && !e.IsIntermediate)
        {
            // Define a tolerance for being "at the bottom"
            const double bottomTolerance = 10.0;
            bool isNearBottom = _logScrollViewer.VerticalOffset >= _logScrollViewer.ScrollableHeight - bottomTolerance;

            if (isNearBottom)
            {
                if (!_shouldScrollToBottom)
                {
                    _logger.LogDebug("User scrolled near bottom of logs. Re-enabling auto-scroll.");
                    _shouldScrollToBottom = true;
                }
            }
            else // User scrolled up significantly
            {
                if (_shouldScrollToBottom)
                {
                    _logger.LogDebug(
                        "User scrolled up in logs ({VerticalOffset}/{ScrollableHeight}). Disabling auto-scroll.",
                        _logScrollViewer.VerticalOffset,
                        _logScrollViewer.ScrollableHeight
                    );
                    _shouldScrollToBottom = false;
                }
            }
        }
    }

    private void ScrollToBottom()
    {
        if (_logScrollViewer == null)
        {
            _logger.LogTrace("LogsView ScrollToBottom called, attempting to find ScrollViewer first.");
            FindAndHookScrollViewer();
            if (_logScrollViewer == null)
            {
                _logger.LogWarning("LogsView ScrollToBottom: ScrollViewer still not found. Cannot scroll.");
                return;
            }
        }

        if (LogsListView.Items.Count > 0)
        {
            try
            {
                object lastItem = LogsListView.Items[^1];
                _logger.LogTrace("Scrolling last log item into view.");
                LogsListView.ScrollIntoView(lastItem, ScrollIntoViewAlignment.Leading);
                _shouldScrollToBottom = true;
            }
            catch (ArgumentOutOfRangeException ex)
            {
                _logger.LogWarning(ex, "Failed to get last log item for scrolling (collection might have changed).");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred during log ScrollIntoView operation.");
            }
        }
        else
        {
            _logger.LogTrace("ScrollToBottom called but LogsListView is empty.");
        }
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject element)
    {
        if (element is ScrollViewer viewer)
            return viewer;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(element, i);
            ScrollViewer? result = FindScrollViewer(child);
            if (result != null)
                return result;
        }

        return null;
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        _logger.LogDebug("Page unloaded. Unsubscribing from log view event listeners.");
        if (ViewModel?.FilteredLogEntries != null)
        {
            ViewModel.FilteredLogEntries.CollectionChanged -= FilteredLogEntries_CollectionChanged;
        }

        if (_logScrollViewer != null)
        {
            _logScrollViewer.ViewChanged -= ScrollViewer_ViewChanged;
            _logScrollViewer = null;
        }
    }
}
