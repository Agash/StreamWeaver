using System.Text.Json.Serialization;

namespace StreamWeaver.Core.Plugins;

/// <summary>
/// Represents the structure of the manifest.json file for a web overlay plugin.
/// </summary>
public class WebPluginManifest
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("version")]
    public string? Version { get; set; }

    [JsonPropertyName("author")]
    public string? Author { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; } // Optional

    [JsonPropertyName("entryScript")]
    public string? EntryScript { get; set; } // Relative path to main JS file

    [JsonPropertyName("entryStyle")]
    public string? EntryStyle { get; set; } // Optional: Relative path to CSS file

    // Optional: List of core components this plugin provides overrides for
    [JsonPropertyName("providesComponents")]
    public List<string>? ProvidesComponents { get; set; } = [];

    // Optional: List of custom web components registered by this plugin
    [JsonPropertyName("registersWebComponents")]
    public List<WebComponentRegistration>? RegistersWebComponents { get; set; } = [];

    // --- Internal properties set during discovery ---
    [JsonIgnore] // Don't serialize these when sending to client
    public string? BasePath { get; set; } // Calculated request path (e.g., /plugins/plugin-id/)

    [JsonIgnore] // Don't serialize these when sending to client
    public string? DirectoryPath { get; set; } // Absolute physical path to the plugin directory
}

/// <summary>
/// Defines the registration details for a custom web component provided by a plugin.
/// </summary>
public class WebComponentRegistration
{
    [JsonPropertyName("tagName")]
    public string? TagName { get; set; } // The custom element tag name (e.g., 'my-widget')

    [JsonPropertyName("scriptPath")]
    public string? ScriptPath { get; set; } // Relative path to the script that defines the component
}
