using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using StreamWeaver.Core.Models.Settings;
using StreamWeaver.UI.ViewModels;

namespace StreamWeaver.UI.Views.SettingsPages;

/// <summary>
/// Represents the settings page for managing connected accounts (Twitch, YouTube, Streamlabs).
/// This page typically binds to the main <see cref="SettingsViewModel"/>.
/// </summary>
public sealed partial class AccountsSettingsPage : Page
{
    private readonly ILogger<AccountsSettingsPage> _logger;

    /// <summary>
    /// Gets the ViewModel associated with this page's DataContext.
    /// Assumes the DataContext is set to an instance of <see cref="SettingsViewModel"/>.
    /// </summary>
    public SettingsViewModel? ViewModel => DataContext as SettingsViewModel;

    /// <summary>
    /// Initializes a new instance of the <see cref="AccountsSettingsPage"/> class.
    /// </summary>
    public AccountsSettingsPage()
    {
        InitializeComponent();
        try
        {
            _logger = App.GetService<ILogger<AccountsSettingsPage>>();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AccountsSettingsPage] FATAL: Failed to resolve ILogger<AccountsSettingsPage>: {ex}");
            throw;
        }

        _logger.LogInformation("AccountsSettingsPage initialized.");
    }

    /// <summary>
    /// Handles Toggled event for both Twitch and YouTube account switches.
    /// Calls the ViewModel to handle the connection/disconnection logic.
    /// </summary>
    private async void AccountToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleSwitch toggleSwitch)
            return;
        if (ViewModel == null)
        {
            _logger.LogWarning("AccountToggle_Toggled fired but ViewModel is null.");
            return;
        }

        object? account = toggleSwitch.DataContext;
        bool connect = toggleSwitch.IsOn;

        if (account is TwitchAccount twitchAccount)
        {
            _logger.LogInformation("Twitch account toggle changed for {Username}. New state: {IsOn}", twitchAccount.Username, connect);
            await ViewModel.HandleAccountToggleAsync(twitchAccount, connect);
        }
        else if (account is YouTubeAccount youtubeAccount)
        {
            _logger.LogInformation("YouTube account toggle changed for {ChannelName}. New state: {IsOn}", youtubeAccount.ChannelName, connect);
            await ViewModel.HandleAccountToggleAsync(youtubeAccount, connect);
        }
        else
        {
            _logger.LogWarning(
                "AccountToggle_Toggled fired, but DataContext was not a recognized account type ({AccountType})",
                account?.GetType().Name ?? "null"
            );
        }
    }

    /// <summary>
    /// Event handler for the Toggled event of the Streamlabs Enable ToggleSwitch.
    /// Calls the corresponding method in the ViewModel to handle the state change and associated logic.
    /// </summary>
    /// <param name="sender">The ToggleSwitch object that fired the event.</param>
    /// <param name="e">Event arguments.</param>
    private async void StreamlabsEnableToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch toggle && ViewModel != null)
        {
            bool isEnabled = toggle.IsOn;
            _logger.LogInformation("Streamlabs Enable ToggleSwitch toggled. New state: IsOn = {IsEnabled}", isEnabled);

            try
            {
                await ViewModel.ToggleStreamlabsEnableAsync(isEnabled);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while ViewModel handled Streamlabs toggle state change to {IsEnabled}", isEnabled);
                // Optionally revert the toggle state or show an error to the user.
                // toggle.IsOn = !isEnabled; // Example of reverting UI state on error
                // TODO: Show error message to user (e.g., using InfoBar)
            }
        }
        else
        {
            _logger.LogWarning("StreamlabsEnableToggle_Toggled event fired, but sender is not a ToggleSwitch or ViewModel is null.");
        }
    }
}
