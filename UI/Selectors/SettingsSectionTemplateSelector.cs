using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using StreamWeaver.UI.ViewModels;

namespace StreamWeaver.UI.Selectors;

/// <summary>
/// Selects a DataTemplate for displaying the content of a settings section
/// based on the <see cref="SettingsSection.Tag"/> property of the bound item.
/// </summary>
public partial class SettingsSectionTemplateSelector : DataTemplateSelector
{
    private static readonly Lazy<ILogger<SettingsSectionTemplateSelector>?> s_lazyLogger = new(() =>
    {
        try
        {
            return App.GetService<ILogger<SettingsSectionTemplateSelector>>();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SettingsSectionTemplateSelector.StaticInit] Failed to get ILogger: {ex.Message}");
            return null;
        }
    });

    private static ILogger<SettingsSectionTemplateSelector>? Logger => s_lazyLogger.Value;

    /// <summary>
    /// Gets or sets the DataTemplate for the 'Credentials' settings section.
    /// </summary>
    public DataTemplate? CredentialsTemplate { get; set; }

    /// <summary>
    /// Gets or sets the DataTemplate for the 'Accounts' settings section.
    /// </summary>
    public DataTemplate? AccountsTemplate { get; set; }

    /// <summary>
    /// Gets or sets the DataTemplate for the 'Overlays' settings section.
    /// </summary>
    public DataTemplate? OverlaysTemplate { get; set; }

    /// <summary>
    /// Gets or sets the DataTemplate for the 'TTS' (Text-to-Speech) settings section.
    /// </summary>
    public DataTemplate? TTSTemplate { get; set; }

    /// <summary>
    /// Gets or sets the DataTemplate for the 'Modules' settings section.
    /// </summary>
    public DataTemplate? ModulesTemplate { get; set; }

    /// <summary>
    /// Gets or sets the fallback DataTemplate to use if no specific template matches or the item is invalid.
    /// </summary>
    public DataTemplate? DefaultTemplate { get; set; }

    /// <summary>
    /// Selects the appropriate DataTemplate based on the provided item's type and tag.
    /// </summary>
    /// <param name="item">The data item (expected to be a <see cref="SettingsSection"/>) for which to select a template.</param>
    /// <returns>The selected <see cref="DataTemplate"/>, or a fallback/default template.</returns>
    protected override DataTemplate? SelectTemplateCore(object item)
    {
        if (item is SettingsSection section && section.Tag != null)
        {
            DataTemplate? selectedTemplate = section.Tag switch
            {
                "Credentials" => CredentialsTemplate,
                "Accounts" => AccountsTemplate,
                "Overlays" => OverlaysTemplate,
                "TTS" => TTSTemplate,
                "Modules" => ModulesTemplate,
                _ => DefaultTemplate,
            };

            return selectedTemplate ?? DefaultTemplate ?? base.SelectTemplateCore(item);
        }

        Logger?.LogWarning(
            "Item is not a SettingsSection or its Tag is null. Item type: {ItemType}. Using fallback template.",
            item?.GetType().Name ?? "null"
        );

        return DefaultTemplate ?? base.SelectTemplateCore(item);
    }

    /// <summary>
    /// Selects the appropriate DataTemplate based on the provided item and container.
    /// This override simply calls the simpler overload.
    /// </summary>
    /// <param name="item">The data item for which to select a template.</param>
    /// <param name="container">The container element (not used in this implementation).</param>
    /// <returns>The selected <see cref="DataTemplate"/>, or a fallback/default template.</returns>
    protected override DataTemplate? SelectTemplateCore(object item, DependencyObject container) => SelectTemplateCore(item);
}
