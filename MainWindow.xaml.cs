using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using StreamWeaver.UI.ViewModels;
using StreamWeaver.UI.Views;

namespace StreamWeaver;

/// <summary>
/// The main application window containing the primary navigation and content frame.
/// </summary>
public sealed partial class MainWindow : Window
{
    private readonly ILogger<MainWindow> _logger;
    public MainWindowViewModel ViewModel { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="MainWindow"/> class.
    /// </summary>
    public MainWindow(ILogger<MainWindow> logger, MainWindowViewModel viewModel) // Inject logger and ViewModel
    {
        _logger = logger;
        ViewModel = viewModel; // Assign injected ViewModel

        _logger.LogInformation("Initializing...");

        InitializeComponent();

        // --- Custom Title Bar Setup ---
        ExtendsContentIntoTitleBar = true; // Allow content like the NavigationView to extend into the title bar area.
        if (AppTitleBar != null)
        {
            SetTitleBar(AppTitleBar); // Set the XAML element as the draggable region for the window.
            _logger.LogInformation("Custom title bar region set.");
        }
        else
        {
            _logger.LogWarning("AppTitleBar element not found in XAML. Custom drag region will not be active.");
        }
        NavView.Loaded += OnNavViewLoaded;
    }

    /// <summary>
    /// Handles the Loaded event for the NavigationView to set the initial selected item and page.
    /// </summary>
    private void OnNavViewLoaded(object sender, RoutedEventArgs args)
    {
        // Ensure NavView has items and nothing is selected yet
        if (NavView.MenuItems.Count > 0 && NavView.SelectedItem == null)
        {
            NavView.SelectedItem = NavView.MenuItems[0];
            Navigate("Chat");
            _logger.LogInformation("Initial navigation view set to Chat.");
        }

        // Unsubscribe after first load if only needed once
        // NavView.Loaded -= OnNavViewLoaded;
    }

    /// <summary>
    /// Handles selection changes within the NavigationView to navigate the ContentFrame.
    /// </summary>
    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        string? tag = null;
        if (args.IsSettingsSelected)
        {
            tag = "Settings";
        }
        else if (args.SelectedItemContainer?.Tag is string itemTag)
        {
            tag = itemTag;
        }

        if (!string.IsNullOrEmpty(tag))
        {
            _logger.LogDebug("NavigationView selection changed. Attempting navigation to view tag: {ViewTag}", tag);
            Navigate(tag);
        }
        else
        {
            _logger.LogWarning("NavigationView selection changed, but no valid tag found for navigation.");
        }
    }

    /// <summary>
    /// Navigates the ContentFrame to the page associated with the given tag.
    /// </summary>
    /// <param name="tag">The string tag identifying the target page.</param>
    private void Navigate(string tag)
    {
        Type pageType = tag switch
        {
            "Chat" => typeof(MainChatView),
            "Logs" => typeof(LogsView),
            "Settings" => typeof(SettingsView),
            _ => typeof(MainChatView),
        };

        if (ContentFrame.CurrentSourcePageType != pageType)
        {
            _logger.LogInformation("Navigating ContentFrame to {PageType}", pageType.Name);
            ContentFrame.Navigate(pageType);
        }
        else
        {
            _logger.LogDebug("Navigation requested to {PageType}, but it is already the current page. Navigation skipped.", pageType.Name);
        }
    }
}
