using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using StreamWeaver.UI.ViewModels;
using StreamWeaver.UI.Views.SettingsPages; // Namespace for specific settings pages

namespace StreamWeaver.UI.Views;

/// <summary>
/// Represents the main view container for application settings,
/// hosting navigation and the content frame for specific settings pages.
/// </summary>
public sealed partial class SettingsView : Page
{
    private readonly ILogger<SettingsView> _logger;

    /// <summary>
    /// Gets the ViewModel associated with this view.
    /// </summary>
    public SettingsViewModel ViewModel { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="SettingsView"/> class.
    /// Resolves the required ViewModel and sets the DataContext.
    /// </summary>
    public SettingsView()
    {
        InitializeComponent();

        try
        {
            // Resolve dependencies using the static service locator.
            ViewModel = App.GetService<SettingsViewModel>();
            _logger = App.GetService<ILogger<SettingsView>>(); // Resolve logger
            DataContext = ViewModel; // Set DataContext for XAML bindings.
            _logger.LogInformation("DataContext set to SettingsViewModel.");

            // Defer initial navigation until the page and its controls are loaded.
            Loaded += SettingsView_Loaded;
        }
        catch (Exception ex)
        {
            // Log critical failure if ViewModel resolution fails.
            // Logging directly here as _logger might not be initialized if GetService fails early.
            App.GetService<ILogger<SettingsView>>()
                ?.LogCritical(ex, "FATAL: Failed to resolve SettingsViewModel or ILogger. SettingsView cannot function.");
            throw;
            // Consider displaying an error message overlay or navigating to an error page.
        }
    }

    /// <summary>
    /// Handles the Loaded event of the SettingsView page.
    /// Performs initial navigation to the section selected in the ViewModel.
    /// </summary>
    private void SettingsView_Loaded(object sender, RoutedEventArgs e)
    {
        // Ensure initial navigation occurs only once after loading.
        Loaded -= SettingsView_Loaded; // Unsubscribe to prevent re-navigation on subsequent loads (e.g., theme changes)

        // Navigate based on the ViewModel's currently selected section, if any.
        if (ViewModel.SelectedSection != null)
        {
            _logger.LogDebug("View loaded. Navigating to initial section from ViewModel: {SectionTag}", ViewModel.SelectedSection.Tag);
            NavigateToSection(ViewModel.SelectedSection.Tag);
        }
        // Fallback: If ViewModel has no selection but sections exist, select the first one.
        // The SelectionChanged handler will then trigger navigation.
        else if (ViewModel.SettingsSections.Count > 0)
        {
            _logger.LogDebug(
                "View loaded. ViewModel had no selected section. Selecting first section programmatically: {SectionTag}",
                ViewModel.SettingsSections[0].Tag
            );
            // Setting SelectedItem programmatically triggers SelectionChanged event.
            SettingsNavView.SelectedItem = ViewModel.SettingsSections[0];
        }
        else
        {
            _logger.LogWarning("View loaded, but no settings sections are available in the ViewModel.");
        }
    }

    /// <summary>
    /// Handles selection changes within the settings NavigationView.
    /// Navigates the content frame to the page corresponding to the selected section.
    /// </summary>
    /// <param name="sender">The NavigationView control.</param>
    /// <param name="args">Event arguments containing the selected item.</param>
    private void SettingsNavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        // The SelectedItem is bound to ViewModel.SelectedSection, but handling the event
        // directly here simplifies navigation logic within the view's code-behind.
        if (args.SelectedItem is SettingsSection selectedSection)
        {
            _logger.LogInformation(
                "Settings navigation selection changed to: {SectionName} (Tag: {SectionTag})",
                selectedSection.Name,
                selectedSection.Tag
            );
            NavigateToSection(selectedSection.Tag);
        }
        else if (args.IsSettingsSelected) // Handle selection of the built-in settings item if used
        {
            _logger.LogInformation("Settings navigation selection changed to: Settings (IsSettingsSelected)");
            // NavigateToSection("Settings"); // Or handle differently if 'Settings' tag isn't used for a custom section
        }
        else
        {
            _logger.LogWarning("SettingsNavView_SelectionChanged: SelectedItem is not a SettingsSection or null.");
        }
    }

    /// <summary>
    /// Navigates the <see cref="SettingsContentFrame"/> to the page associated with the specified section tag.
    /// </summary>
    /// <param name="sectionTag">The unique tag identifying the target settings page.</param>
    private void NavigateToSection(string sectionTag)
    {
        // Map the section tag string to the corresponding Page Type.
        Type? pageType = sectionTag switch
        {
            "Credentials" => typeof(CredentialsSettingsPage),
            "Accounts" => typeof(AccountsSettingsPage),
            "Overlays" => typeof(OverlaysSettingsPage),
            "TTS" => typeof(TtsSettingsPage),
            "Modules" => typeof(ModulesSettingsPage),
            "Plugins" => typeof(PluginsSettingsPage),
            _ => null, // Return null for unrecognized tags.
        };

        // Navigate the content frame if a valid page type was found
        // and it's different from the currently displayed page.
        if (pageType != null && SettingsContentFrame.CurrentSourcePageType != pageType)
        {
            _logger.LogInformation("Navigating SettingsContentFrame to {PageTypeName}", pageType.Name);
            SettingsContentFrame.Navigate(pageType);
        }
        else if (pageType == null)
        {
            // Log a warning if no page is defined for the given tag.
            _logger.LogWarning("No settings page is defined for section tag: {SectionTag}", sectionTag);
            // Optionally, navigate to a default placeholder or error page:
            // SettingsContentFrame.Navigate(typeof(PlaceholderSettingsPage));
        }
        else
        {
            // Log if navigation is skipped because the target page is already displayed.
            _logger.LogDebug("Navigation to {PageTypeName} skipped as it is already the current page.", pageType.Name);
        }
    }
}
