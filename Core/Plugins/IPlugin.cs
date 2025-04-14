namespace StreamWeaver.Core.Plugins;

/// <summary>
/// Base interface for all StreamWeaver plugins.
/// </summary>
public interface IPlugin
{
    /// <summary>
    /// A unique identifier for the plugin.
    /// </summary>
    Guid Id { get; }

    /// <summary>
    /// The display name of the plugin.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// The author(s) of the plugin.
    /// </summary>
    string Author { get; }

    /// <summary>
    /// The version of the plugin.
    /// </summary>
    Version Version { get; }

    /// <summary>
    /// Called once when the plugin is loaded and initialized by StreamWeaver.
    /// </summary>
    /// <param name="host">Provides access points to interact with StreamWeaver core functionalities.</param>
    /// <returns>A task representing the asynchronous initialization process.</returns>
    Task InitializeAsync(IPluginHost host);

    /// <summary>
    /// Called once when StreamWeaver is shutting down or the plugin is being unloaded.
    /// Used for cleaning up resources.
    /// </summary>
    /// <returns>A task representing the asynchronous shutdown process.</returns>
    Task ShutdownAsync();
}
