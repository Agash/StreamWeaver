using System.Text.Json.Serialization;

namespace StreamWeaver.Core.Plugins;

/// <summary>
/// Represents the structure of the manifest.json file for a plugin.
/// </summary>
public class PluginManifest
{
    [JsonPropertyName("id")]
    public string? Id { get; set; } // Expecting a GUID string

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("author")]
    public string? Author { get; set; }

    [JsonPropertyName("version")]
    public string? Version { get; set; } // Expecting SemVer string

    [JsonPropertyName("description")]
    public string? Description { get; set; } // Optional

    [JsonPropertyName("type")]
    public string? Type { get; set; } // "dotnet" or "javascript" (future)

    [JsonPropertyName("entryPoint")]
    public PluginEntryPoint? EntryPoint { get; set; }

    // Future: Add dependencies, etc.
    // [JsonPropertyName("dependencies")]
    // public List<string>? Dependencies { get; set; }
}

/// <summary>
/// Represents the entry point details within the manifest.
/// Contains fields relevant for different plugin types.
/// </summary>
public class PluginEntryPoint
{
    // --- .NET Specific ---
    [JsonPropertyName("assembly")]
    public string? Assembly { get; set; } // Relative path to DLL

    [JsonPropertyName("fullClassName")]
    public string? FullClassName { get; set; } // Namespace.ClassName

    // --- JavaScript Specific (Future) ---
    // [JsonPropertyName("scriptFile")]
    // public string? ScriptFile { get; set; }
}
