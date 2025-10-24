namespace StreamWeaver.Core.Plugins;

/// <summary>
/// Defines the contract for a plugin that exposes user-configurable settings.
/// Implementing this interface allows the main application to discover the plugin's
/// options type, its preferred configuration section name, and to provide the plugin
/// with a chance to register its options with the dependency injection container.
/// </summary>
/// <remarks>
/// Plugins implementing this interface must provide static implementations for
/// <see cref="OptionsType"/> and <see cref="DefaultConfigurationSectionName"/>.
/// The main application will use these static members to register the plugin's options
/// with the DI container and bind them to the user's configuration.
/// </remarks>
public interface IConfigurablePlugin
{
    /// <summary>
    /// Gets the <see cref="Type"/> of the class used to store this plugin's configuration options.
    /// This type will be used by the main application to bind the plugin's configuration section.
    /// </summary>
    /// <example><code>public static Type OptionsType => typeof(MyPluginOptions);</code></example>
    static abstract Type OptionsType { get; }

    /// <summary>
    /// Gets the default top-level configuration section name under which this plugin's settings
    /// are expected to be found in the user's settings file.
    /// The main application might prefix this further (e.g., "Plugins:{DefaultConfigurationSectionName}").
    /// </summary>
    /// <example><code>public static string DefaultConfigurationSectionName => "MyAwesomePlugin";</code></example>
    static abstract string DefaultConfigurationSectionName { get; }
}
