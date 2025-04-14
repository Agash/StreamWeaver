using System.Collections.ObjectModel;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using StreamWeaver.Core.Models.Events;

namespace StreamWeaver.Core.Plugins;

/// <summary>
/// Manages the discovery, loading, lifecycle, and routing for application plugins.
/// </summary>
public class PluginService
{
    private readonly IServiceProvider _serviceProvider;
    private IPluginHost? _pluginHost;
    private readonly Dictionary<string, IChatCommandPlugin> _commandRegistry = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<PluginService> _logger;

    /// <summary>
    /// Gets the collection of currently loaded and initialized plugins.
    /// </summary>
    public ObservableCollection<IPlugin> LoadedPlugins { get; } = [];

    private static readonly JsonSerializerOptions s_jsonSerializerOptions = new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// Initializes a new instance of the <see cref="PluginService"/> class.
    /// </summary>
    /// <param name="serviceProvider">The application's service provider.</param>
    /// <param name="logger">The logger instance.</param>
    public PluginService(IServiceProvider serviceProvider, ILogger<PluginService> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));

        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _logger.LogInformation("PluginService initialized.");
    }

    /// <summary>
    /// Discovers, loads, and initializes plugins from subdirectories within the specified plugin directory.
    /// </summary>
    /// <param name="pluginDirectory">The absolute path to the main 'Plugins' directory.</param>
    /// <returns>A Task representing the asynchronous operation.</returns>
    public async Task LoadPluginsAsync(string pluginDirectory)
    {
        if (LoadedPlugins.Count > 0)
        {
            _logger.LogWarning("LoadPluginsAsync called, but plugins are already loaded. Skipping.");
            return;
        }

        if (!Directory.Exists(pluginDirectory))
        {
            _logger.LogWarning(
                "Plugin directory not found: {PluginDirectory}. Creating directory and skipping plugin load for this run.",
                pluginDirectory
            );
            try
            {
                Directory.CreateDirectory(pluginDirectory);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create plugin directory: {PluginDirectory}", pluginDirectory);
            }

            return;
        }

        _logger.LogInformation("Starting plugin discovery in subdirectories of: {PluginDirectory}", pluginDirectory);

        _pluginHost = new PluginHost(_serviceProvider);

        foreach (string subDir in Directory.GetDirectories(pluginDirectory))
        {
            string manifestPath = Path.Combine(subDir, "manifest.json");
            if (!File.Exists(manifestPath))
                continue;

            _logger.LogDebug("Found manifest: {ManifestPath}", manifestPath);
            try
            {
                string manifestJson = await File.ReadAllTextAsync(manifestPath);
                PluginManifest? manifest = JsonSerializer.Deserialize<PluginManifest>(manifestJson, s_jsonSerializerOptions);

                if (!ValidateManifest(manifest, manifestPath))
                    continue;

                if (manifest!.Type?.Equals("dotnet", StringComparison.OrdinalIgnoreCase) == true)
                {
                    if (
                        manifest.EntryPoint == null
                        || string.IsNullOrWhiteSpace(manifest.EntryPoint.Assembly)
                        || string.IsNullOrWhiteSpace(manifest.EntryPoint.FullClassName)
                    )
                    {
                        _logger.LogError("Invalid 'entryPoint' details for dotnet plugin in manifest {ManifestPath}.", manifestPath);
                        continue;
                    }

                    string assemblyPath = Path.GetFullPath(Path.Combine(subDir, manifest.EntryPoint.Assembly));
                    if (!File.Exists(assemblyPath))
                    {
                        _logger.LogError(
                            "Assembly file not found for plugin defined in {ManifestPath}. Expected path: {AssemblyPath}",
                            manifestPath,
                            assemblyPath
                        );
                        continue;
                    }

                    Assembly pluginAssembly = Assembly.LoadFrom(assemblyPath);
                    Type? pluginType = pluginAssembly.GetType(manifest.EntryPoint.FullClassName, throwOnError: false);

                    if (pluginType == null)
                    {
                        _logger.LogError(
                            "Class '{FullClassName}' not found in assembly {AssemblyPath} defined in {ManifestPath}.",
                            manifest.EntryPoint.FullClassName,
                            assemblyPath,
                            manifestPath
                        );
                        continue;
                    }

                    if (!typeof(IPlugin).IsAssignableFrom(pluginType) || pluginType.IsInterface || pluginType.IsAbstract)
                    {
                        _logger.LogError(
                            "Type '{PluginTypeName}' defined in {ManifestPath} does not implement IPlugin or is abstract/interface.",
                            pluginType.FullName,
                            manifestPath
                        );
                        continue;
                    }

                    if (Activator.CreateInstance(pluginType) is IPlugin pluginInstance)
                    {
                        if (!VerifyInstanceMetadata(manifest, pluginInstance))
                            continue;

                        await pluginInstance.InitializeAsync(_pluginHost);
                        LoadedPlugins.Add(pluginInstance);
                        _logger.LogInformation(
                            "Successfully loaded and initialized plugin: {PluginName} (Version: {PluginVersion}, ID: {PluginId})",
                            pluginInstance.Name,
                            pluginInstance.Version,
                            pluginInstance.Id
                        );
                    }
                    else
                    {
                        _logger.LogError(
                            "Failed to create or cast plugin instance to IPlugin for type {PluginTypeName} from {ManifestPath}.",
                            pluginType.FullName,
                            manifestPath
                        );
                    }
                }
                else
                {
                    _logger.LogWarning(
                        "Skipping manifest {ManifestPath}: Unsupported or missing plugin 'type' ('{PluginType}'). Expected 'dotnet'.",
                        manifestPath,
                        manifest.Type
                    );
                }
            }
            catch (FileNotFoundException fnfEx)
            {
                _logger.LogError(
                    fnfEx,
                    "Error loading plugin from {ManifestPath}: Assembly or dependency not found. Ensure all required DLLs are in the plugin's subdirectory.",
                    manifestPath
                );
            }
            catch (TypeLoadException tlEx)
            {
                _logger.LogError(tlEx, "Error loading plugin type from {ManifestPath}. Check FullClassName and assembly dependencies.", manifestPath);
            }
            catch (JsonException jsonEx)
            {
                _logger.LogError(jsonEx, "Error parsing manifest JSON {ManifestPath}", manifestPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error processing plugin from {ManifestPath}", manifestPath);
            }
        }

        _logger.LogInformation("Plugin loading complete. {Count} plugins loaded.", LoadedPlugins.Count);
        ProcessChatCommandPlugins();
        ProcessEventProcessorPlugins();
    }

    /// <summary>
    /// Validates the essential fields of a plugin manifest.
    /// </summary>
    /// <param name="manifest">The deserialized manifest object.</param>
    /// <param name="manifestPath">The path to the manifest file (for logging).</param>
    /// <returns>True if the manifest is valid, false otherwise.</returns>
    private bool ValidateManifest(PluginManifest? manifest, string manifestPath)
    {
        if (manifest == null)
        {
            _logger.LogError("Error in manifest {ManifestPath}: Failed to deserialize.", manifestPath);
            return false;
        }

        if (!Guid.TryParse(manifest.Id, out _))
        {
            _logger.LogError("Error in manifest {ManifestPath}: 'id' is missing or not a valid GUID.", manifestPath);
            return false;
        }

        if (string.IsNullOrWhiteSpace(manifest.Name))
        {
            _logger.LogError("Error in manifest {ManifestPath}: 'name' is missing.", manifestPath);
            return false;
        }

        if (string.IsNullOrWhiteSpace(manifest.Author))
        {
            _logger.LogError("Error in manifest {ManifestPath}: 'author' is missing.", manifestPath);
            return false;
        }

        if (!Version.TryParse(manifest.Version, out _))
        {
            _logger.LogError("Error in manifest {ManifestPath}: 'version' is missing or not a valid format (e.g., 1.0.0).", manifestPath);
            return false;
        }

        if (string.IsNullOrWhiteSpace(manifest.Type))
        {
            _logger.LogError("Error in manifest {ManifestPath}: 'type' is missing.", manifestPath);
            return false;
        }

        return true;
    }

    /// <summary>
    /// Verifies that metadata in the manifest matches the metadata provided by the plugin instance. Logs warnings for mismatches.
    /// </summary>
    /// <param name="manifest">The plugin manifest.</param>
    /// <param name="instance">The loaded plugin instance.</param>
    /// <returns>True (currently always returns true, mismatches are only warnings).</returns>
    private bool VerifyInstanceMetadata(PluginManifest manifest, IPlugin instance)
    {
        if (!Guid.Parse(manifest.Id!).Equals(instance.Id))
        {
            _logger.LogWarning(
                "Manifest/Instance ID mismatch for plugin '{PluginName}'. Manifest: {ManifestId}, Instance: {InstanceId}",
                manifest.Name,
                manifest.Id,
                instance.Id
            );
        }

        if (!manifest.Name!.Equals(instance.Name, StringComparison.Ordinal))
        {
            _logger.LogWarning(
                "Manifest/Instance Name mismatch for plugin '{ManifestName}'. Instance Name: '{InstanceName}'",
                manifest.Name,
                instance.Name
            );
        }

        if (!manifest.Author!.Equals(instance.Author, StringComparison.Ordinal))
        {
            _logger.LogWarning(
                "Manifest/Instance Author mismatch for plugin '{PluginName}'. Manifest: '{ManifestAuthor}', Instance: '{InstanceAuthor}'",
                manifest.Name,
                manifest.Author,
                instance.Author
            );
        }

        if (!Version.Parse(manifest.Version!).Equals(instance.Version))
        {
            _logger.LogWarning(
                "Manifest/Instance Version mismatch for plugin '{PluginName}'. Manifest: {ManifestVersion}, Instance: {InstanceVersion}",
                manifest.Name,
                manifest.Version,
                instance.Version
            );
        }

        return true;
    }

    /// <summary>
    /// Shuts down all loaded plugins asynchronously.
    /// </summary>
    public async Task ShutdownPluginsAsync()
    {
        if (LoadedPlugins.Count == 0)
        {
            _logger.LogInformation("No plugins loaded, shutdown skipped.");
            return;
        }

        _logger.LogInformation("Shutting down {Count} plugins...", LoadedPlugins.Count);

        List<IPlugin> pluginsToShutdown = [.. LoadedPlugins];
        List<Task> shutdownTasks = [];
        foreach (IPlugin plugin in pluginsToShutdown)
        {
            shutdownTasks.Add(
                Task.Run(async () =>
                {
                    try
                    {
                        _logger.LogDebug("Shutting down plugin: {PluginName}...", plugin.Name);
                        await plugin.ShutdownAsync();
                        _logger.LogInformation("Plugin shutdown complete: {PluginName}", plugin.Name);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error shutting down plugin {PluginName}", plugin.Name);
                    }
                })
            );
        }

        try
        {
            await Task.WhenAll(shutdownTasks).WaitAsync(TimeSpan.FromSeconds(10));
            _logger.LogDebug("All plugin shutdown tasks completed or timed out.");
        }
        catch (TimeoutException)
        {
            _logger.LogWarning("Timeout waiting for plugins to shut down.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during plugin shutdown synchronization.");
        }

        LoadedPlugins.Clear();
        _commandRegistry.Clear();
        _pluginHost = null;
        _logger.LogInformation("All plugins shut down and lists cleared.");
    }

    /// <summary>
    /// Registers chat commands exposed by loaded plugins.
    /// </summary>
    private void ProcessChatCommandPlugins()
    {
        _commandRegistry.Clear();
        IEnumerable<IChatCommandPlugin> commandPlugins = LoadedPlugins.OfType<IChatCommandPlugin>();
        _logger.LogInformation("Processing {Count} chat command plugins for registry...", commandPlugins.Count());
        foreach (IChatCommandPlugin plugin in commandPlugins)
        {
            if (plugin.Commands == null)
                continue;
            foreach (string command in plugin.Commands.Select(c => c?.ToLowerInvariant() ?? string.Empty).Where(c => !string.IsNullOrWhiteSpace(c)))
            {
                if (_commandRegistry.TryGetValue(command, out IChatCommandPlugin? existingPlugin))
                {
                    _logger.LogWarning(
                        "Command '{Command}' conflict. Plugin '{NewPlugin}' tried to register, but it's already handled by '{ExistingPlugin}'. Ignoring registration from '{NewPlugin}'.",
                        command,
                        plugin.Name,
                        existingPlugin.Name,
                        plugin.Name
                    );
                }
                else
                {
                    _commandRegistry.Add(command, plugin);
                    _logger.LogDebug("--> Registered command '{Command}' to plugin '{PluginName}'.", command, plugin.Name);
                }
            }
        }

        _logger.LogInformation("Command registry populated with {Count} commands.", _commandRegistry.Count);
    }

    /// <summary>
    /// Logs the count of loaded event processor plugins.
    /// </summary>
    private void ProcessEventProcessorPlugins()
    {
        int count = LoadedPlugins.OfType<IEventProcessorPlugin>().Count();
        _logger.LogInformation("Found {Count} event processor plugins.", count);
    }

    /// <summary>
    /// Routes a base event to all loaded event processor plugins.
    /// </summary>
    /// <param name="eventData">The event to route.</param>
    public async Task RouteEventToProcessorsAsync(BaseEvent eventData)
    {
        List<IEventProcessorPlugin> processors = [.. LoadedPlugins.OfType<IEventProcessorPlugin>()];
        if (processors.Count == 0)
            return;

        _logger.LogTrace(
            "Routing event {EventType} ({EventId}) to {ProcessorCount} processors...",
            eventData.GetType().Name,
            eventData.Id,
            processors.Count
        );

        foreach (IEventProcessorPlugin processor in processors)
        {
            try
            {
                await processor.ProcessEventAsync(eventData);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error routing event to plugin '{PluginName}'", processor.Name);
            }
        }
    }

    /// <summary>
    /// Checks if a chat message is a potentially valid command (starts with '!')
    /// </summary>
    /// <param name="messageEvent">The chat message event.</param>
    /// <returns>True if it might be a command, false otherwise.</returns>
    public static bool IsChatCommand(ChatMessageEvent messageEvent)
    {
        string messageText = messageEvent.RawMessage?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(messageText) || messageText.Length < 2 || messageText[0] != '!')
        {
            return false;
        }

        string[] parts = messageText.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        return parts[0].Length > 1;
    }

    /// <summary>
    /// Attempts to find and execute a plugin handler for a chat command.
    /// </summary>
    /// <param name="messageEvent">The chat message event that might be a command.</param>
    /// <returns>True if a plugin handled the command and the original message should be suppressed, false otherwise.</returns>
    public async Task<bool> TryHandleChatCommandAsync(ChatMessageEvent messageEvent)
    {
        string messageText = messageEvent.RawMessage?.Trim() ?? string.Empty;
        if (!IsChatCommand(messageEvent))
            return false;

        string[] parts = messageText.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        string command = parts[0].ToLowerInvariant();

        if (_commandRegistry.TryGetValue(command, out IChatCommandPlugin? handlerPlugin))
        {
            if (_pluginHost == null)
            {
                _logger.LogError("Cannot handle command '{Command}': PluginHost is null.", command);
                return false;
            }

            string arguments = parts.Length > 1 ? parts[1].Trim() : string.Empty;
            _logger.LogInformation("Matched command '{Command}' to plugin '{PluginName}'. Executing...", command, handlerPlugin.Name);
            try
            {
                ChatCommandContext context = new()
                {
                    Command = command,
                    Arguments = arguments,
                    OriginalEvent = messageEvent,
                    Host = _pluginHost,
                };

                bool suppressOriginal = await handlerPlugin.HandleCommandAsync(context);
                _logger.LogInformation(
                    "Command '{Command}' executed by plugin '{PluginName}'. Suppress original message: {Suppress}",
                    command,
                    handlerPlugin.Name,
                    suppressOriginal
                );
                return suppressOriginal;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing command '{Command}' in plugin '{PluginName}'", command, handlerPlugin.Name);
                return false;
            }
        }

        _logger.LogDebug("No registered handler found for potential command: {Command}", command);
        return false;
    }
}
