using System;

// Assuming your UI framework is WinUI. Adjust if using WPF or another.
// For WinUI, Page and UserControl are in Microsoft.UI.Xaml.Controls.
// using Microsoft.UI.Xaml.Controls;

namespace StreamWeaver.Core.Plugins;

/// <summary>
/// Defines the contract for a plugin that provides a custom user interface (UI) page
/// to be displayed in the main application's settings area.
/// </summary>
/// <remarks>
/// Plugins implementing this interface allow the main application to discover
/// and dynamically display their settings UI. The provided <see cref="SettingsPageType"/>
/// should be a <c>Microsoft.UI.Xaml.Controls.Page</c> or <c>Microsoft.UI.Xaml.Controls.UserControl</c>
/// that can be resolved and instantiated via the application's dependency injection container.
/// </remarks>
public interface IPluginUIPageProvider
{
    /// <summary>
    /// Gets the user-friendly display name for this plugin's settings page.
    /// This name will be used in the settings navigation menu.
    /// </summary>
    /// <example>"Banned Words Configuration"</example>
    string SettingsPageDisplayName { get; }

    /// <summary>
    /// Gets the <see cref="Type"/> of the WinUI <c>Page</c> or <c>UserControl</c> that represents
    /// this plugin's settings UI.
    /// This type must be resolvable through the application's dependency injection container
    /// so that its dependencies (like ViewModels or IOptionsMonitor) can be injected.
    /// </summary>
    /// <example>typeof(MyPluginSettingsPage)</example>
    Type SettingsPageType { get; }

    /// <summary>
    /// Gets an optional icon glyph (e.g., Segoe Fluent Icons) for the settings navigation entry.
    /// </summary>
    string? SettingsIconGlyph { get; }

    /// <summary>
    /// Gets an optional tooltip for the settings navigation entry.
    /// </summary>
    string? SettingsPageTooltip { get; }
}
