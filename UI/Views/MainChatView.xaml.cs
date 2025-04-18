using System.Collections.Specialized;
using CommunityToolkit.WinUI;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching; // Keep this for DispatcherQueue itself
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using StreamWeaver.UI.ViewModels;
using Windows.System;

namespace StreamWeaver.UI.Views;

public sealed partial class MainChatView : Page
{
    private readonly ILogger<MainChatView> _logger;
    public MainChatViewModel ViewModel { get; }
    private bool _shouldScrollToBottom = true;
    private ScrollViewer? _chatScrollViewer;

    public MainChatView()
    {
        InitializeComponent();
        try
        {
            ViewModel = App.GetService<MainChatViewModel>();
            _logger = App.GetService<ILogger<MainChatView>>();
            DataContext = ViewModel;
            _logger.LogInformation("DataContext set to MainChatViewModel.");
        }
        catch (Exception ex)
        {
            App.GetService<ILogger<MainChatView>>()
                ?.LogCritical(ex, "FATAL: Failed to resolve MainChatViewModel or ILogger. View cannot function.");
            throw new InvalidOperationException("Failed to initialize MainChatView dependencies.", ex);
        }
    }

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        _logger.LogDebug("MainChatView Page_Loaded.");
        if (ViewModel?.Events != null)
        {
            ViewModel.Events.CollectionChanged -= Events_CollectionChanged;
            ViewModel.Events.CollectionChanged += Events_CollectionChanged;
            _logger.LogDebug("Subscribed to ViewModel Events CollectionChanged.");
        }
        else
        {
            _logger.LogError("ViewModel or ViewModel.Events collection is null on Page_Loaded. Cannot subscribe for auto-scroll.");
        }

        FindAndHookScrollViewer();
        _logger.LogDebug("Scheduling initial ScrollToBottom after a short delay.");

        _ = DispatcherQueue.EnqueueAsync(async () =>
        {
            _logger.LogTrace("Executing delayed initial ScrollToBottom...");
            await Task.Delay(250); // Keep delay for visual settling
            if (IsLoaded) // Check if page is still loaded
            {
                await ScrollToBottom();
                _logger.LogDebug("Initial scroll attempt finished.");
            }
            else
            {
                _logger.LogDebug("Initial scroll cancelled as page was unloaded.");
            }
        });
    }

    private void FindAndHookScrollViewer()
    {
        if (_chatScrollViewer == null)
        {
            _chatScrollViewer = FindScrollViewer(ChatListView);
            if (_chatScrollViewer != null)
            {
                _chatScrollViewer.ViewChanged -= ScrollViewer_ViewChanged;
                _chatScrollViewer.ViewChanged += ScrollViewer_ViewChanged;
                _logger.LogInformation("Found and hooked ScrollViewer within ChatListView.");
            }
            else
            {
                _logger.LogWarning("Could not find the internal ScrollViewer for ChatListView on initial load. Auto-scroll may be delayed or disabled.");
            }
        }
        else
        {
            // Re-hook just in case something unloaded/reloaded it internally
            _chatScrollViewer.ViewChanged -= ScrollViewer_ViewChanged;
            _chatScrollViewer.ViewChanged += ScrollViewer_ViewChanged;
            _logger.LogDebug("ScrollViewer already cached, ensured ViewChanged event is hooked.");
        }
    }

    private void MessageInputBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            if (ViewModel?.SendMessageCommand?.CanExecute(null) ?? false)
            {
                _logger.LogDebug("Enter key pressed in input box, executing SendMessageCommand.");
                ViewModel.SendMessageCommand.Execute(null);
                e.Handled = true;
            }
            else
            {
                _logger.LogDebug("Enter key pressed in input box, but SendMessageCommand cannot execute or ViewModel is null.");
            }
        }
    }

    private void Events_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add && _shouldScrollToBottom)
        {
            _logger.LogTrace("New items added and auto-scroll enabled. Enqueueing ScrollToBottom task.");
            _ = DispatcherQueue.EnqueueAsync(ScrollToBottom);
        }
        else if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            _logger.LogDebug("Event collection reset. Re-enabling auto-scroll.");
            _shouldScrollToBottom = true;
            _ = DispatcherQueue.EnqueueAsync(ScrollToBottom);
        }
    }

    private void ScrollViewer_ViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        if (_chatScrollViewer != null && !e.IsIntermediate)
        {
            const double bottomTolerance = 10.0;
            bool isNearBottom = _chatScrollViewer.VerticalOffset >= _chatScrollViewer.ScrollableHeight - bottomTolerance;

            if (isNearBottom)
            {
                if (!_shouldScrollToBottom)
                {
                    _logger.LogDebug("User scrolled near bottom. Re-enabling auto-scroll.");
                    _shouldScrollToBottom = true;
                }
            }
            else // User scrolled up significantly
            {
                if (_shouldScrollToBottom)
                {
                    _logger.LogDebug("User scrolled up ({VerticalOffset}/{ScrollableHeight}). Disabling auto-scroll.",
                        _chatScrollViewer.VerticalOffset, _chatScrollViewer.ScrollableHeight);
                    _shouldScrollToBottom = false;
                }
            }
        }
    }

    private async Task ScrollToBottom()
    {
        if (_chatScrollViewer == null)
        {
            _logger.LogTrace("ScrollToBottom called, attempting to find ScrollViewer first.");
            FindAndHookScrollViewer();
            if (_chatScrollViewer == null)
            {
                _logger.LogWarning("ScrollToBottom: ScrollViewer not found. Cannot scroll.");
                return;
            }
        }

        await Task.Yield(); // Allow layout to potentially update
        // Short delay might still be helpful sometimes after Yield
        await Task.Delay(50);

        if (ChatListView.Items.Count > 0)
        {
            try
            {
                object lastItem = ChatListView.Items[^1];
                _logger.LogTrace("Scrolling last item into view using default ScrollIntoView (after delay).");
                ChatListView.ScrollIntoView(lastItem, ScrollIntoViewAlignment.Leading);
                // Re-assert that we want auto-scroll after programmatically scrolling
                _shouldScrollToBottom = true;
            }
            catch (ArgumentOutOfRangeException ex)
            {
                _logger.LogWarning(ex, "Failed to get last item for scrolling (collection might have changed).");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred during simple ScrollIntoView operation.");
            }
        }
        else
        {
            _logger.LogTrace("ScrollToBottom called but ChatListView is empty.");
        }
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject element)
    {
        if (element is ScrollViewer viewer)
            return viewer;

        int childrenCount = VisualTreeHelper.GetChildrenCount(element);
        for (int i = 0; i < childrenCount; i++)
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
        _logger.LogInformation("Page unloaded. Unsubscribing from events.");
        if (ViewModel?.Events != null)
        {
            ViewModel.Events.CollectionChanged -= Events_CollectionChanged;
        }

        if (_chatScrollViewer != null)
        {
            _chatScrollViewer.ViewChanged -= ScrollViewer_ViewChanged;
        }
    }

    private void ChatListView_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.InRecycleQueue)
        {
            _logger.LogTrace("ListView item container entering recycle queue. Phase: {Phase}", args.Phase);
        }

        args.Handled = true; // Mark handled to potentially improve performance
    }
}
