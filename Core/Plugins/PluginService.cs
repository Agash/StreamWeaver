// StreamWeaver.Core/Plugins/PluginService.cs
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics; // For Debug.WriteLine
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StreamWeaver.Core.Models.Events;

namespace StreamWeaver.Core.Plugins;

public record PluginUIPageInfo(string DisplayName, Type PageType, IPlugin Plugin);

public class PluginService
{
    private readonly IServiceProvider _serviceProvider;
    private IPluginHost? _pluginHost;
    private readonly Dictionary<string, IChatCommandPlugin> _commandRegistry = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<PluginService> _logger; // Instance logger

    public ObservableCollection<IPlugin> LoadedPlugins { get; } = [];
    public ObservableCollection<PluginUIPageInfo> PluginSettingsPageProviders { get; } = [];

    private static readonly JsonSerializerOptions s_jsonSerializerOptions = new() { PropertyNameCaseInsensitive = true };
    private List<Type> _discoveredPluginTypesForInstance = []; // Instance field for this service instance

    public PluginService(IServiceProvider serviceProvider, ILogger<PluginService> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _logger.LogInformation("PluginService instance created. Ready for plugin initialization.");
    }

    /// <summary>
    /// Discovers plugin types from assemblies and registers them and their configurations with the DI container.
    /// This method MUST be called DURING the main application's ConfigureServices phase,
    /// before the final IServiceProvider is built.
    /// </summary>
    /// <returns>A list of discovered plugin Types that implement IPlugin.</returns>
    public static List<Type> DiscoverAndRegisterPlugins(string pluginDirectory, IServiceCollection services, IConfiguration applicationConfiguration)
    {
        var discoveredTypes = new List<Type>();
        // Use Debug.WriteLine for very early logging if needed, as ILogger might not be fully available.
        Debug.WriteLine($"[PluginService.StaticDiscovery] Starting plugin type discovery in: {pluginDirectory}");

        if (!Directory.Exists(pluginDirectory))
        {
            Debug.WriteLine($"[PluginService.StaticDiscovery] Plugin directory not found: {pluginDirectory}. Creating it.");
            try
            {
                Directory.CreateDirectory(pluginDirectory);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PluginService.StaticDiscovery] Failed to create plugin directory: {ex.Message}");
            }
            return discoveredTypes;
        }

        foreach (string subDir in Directory.GetDirectories(pluginDirectory))
        {
            string manifestPath = Path.Combine(subDir, "manifest.json");
            if (!File.Exists(manifestPath))
                continue;

            Debug.WriteLine($"[PluginService.StaticDiscovery] Found manifest: {manifestPath}");
            try
            {
                string manifestJson = File.ReadAllText(manifestPath);
                PluginManifest? manifest = JsonSerializer.Deserialize<PluginManifest>(manifestJson, s_jsonSerializerOptions);

                if (!ValidateManifestInternalStatic(manifest, manifestPath) || manifest!.EntryPoint == null)
                    continue;

                if (manifest.Type?.Equals("dotnet", StringComparison.OrdinalIgnoreCase) == true)
                {
                    string assemblyPath = Path.GetFullPath(Path.Combine(subDir, manifest.EntryPoint.Assembly!));
                    if (!File.Exists(assemblyPath))
                    {
                        Debug.WriteLine($"[PluginService.StaticDiscovery] Assembly file not found: {assemblyPath}");
                        continue;
                    }

                    Assembly pluginAssembly = Assembly.LoadFrom(assemblyPath);
                    Type? pluginType = pluginAssembly.GetType(manifest.EntryPoint.FullClassName!, throwOnError: false);

                    if (pluginType == null || !typeof(IPlugin).IsAssignableFrom(pluginType) || pluginType.IsInterface || pluginType.IsAbstract)
                    {
                        Debug.WriteLine(
                            $"[PluginService.StaticDiscovery] Invalid plugin type '{manifest.EntryPoint.FullClassName}' in {assemblyPath}."
                        );
                        continue;
                    }

                    services.AddSingleton(pluginType); // Register plugin type itself for DI
                    discoveredTypes.Add(pluginType);
                    Debug.WriteLine($"[PluginService.StaticDiscovery] Registered plugin type for DI: {pluginType.FullName}");

                    // --- New: Handle IConfigurablePlugin by reflecting static properties ---
                    if (typeof(IConfigurablePlugin).IsAssignableFrom(pluginType))
                    {
                        Debug.WriteLine(
                            $"[PluginService.StaticDiscovery] Plugin type {pluginType.FullName} implements IConfigurablePlugin. Configuring options..."
                        );
                        ConfigurePluginOptionsInternal(pluginType, services, applicationConfiguration);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PluginService.StaticDiscovery] Error processing manifest {manifestPath}: {ex.Message}");
            }
        }
        Debug.WriteLine(
            $"[PluginService.StaticDiscovery] Plugin type discovery and DI registration complete. {discoveredTypes.Count} types registered."
        );
        return discoveredTypes;
    }

    /// <summary>
    /// Internal static helper to configure options for a specific plugin type.
    /// </summary>
    private static void ConfigurePluginOptionsInternal(Type pluginType, IServiceCollection services, IConfiguration applicationConfiguration)
    {
        // This method will now be called with the *concrete* pluginType.
        // We need to get the static properties from this concrete type.
        try
        {
            PropertyInfo? optionsTypeProp = pluginType.GetProperty(
                "OptionsType",
                BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy
            );
            PropertyInfo? sectionNameProp = pluginType.GetProperty(
                "DefaultConfigurationSectionName",
                BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy
            );

            if (optionsTypeProp?.GetValue(null) is Type actualOptionsType && sectionNameProp?.GetValue(null) is string actualSectionName)
            {
                string fullSectionPath = $"Plugins:{actualSectionName}";
                IConfigurationSection pluginSpecificConfigSection = applicationConfiguration.GetSection(fullSectionPath);

                // The services.Configure<T>(IConfiguration) method is an extension method.
                // We find it on OptionsConfigurationServiceCollectionExtensions and make it generic.
                MethodInfo? configureMethod = typeof(OptionsConfigurationServiceCollectionExtensions)
                    .GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .FirstOrDefault(m =>
                        m.Name == "Configure"
                        && m.IsGenericMethodDefinition
                        && m.GetParameters().Length == 2
                        && m.GetParameters()[1].ParameterType == typeof(IConfiguration)
                    );

                if (configureMethod != null)
                {
                    MethodInfo genericConfigureMethod = configureMethod.MakeGenericMethod(actualOptionsType);
                    genericConfigureMethod.Invoke(null, [services, pluginSpecificConfigSection]);
                    Debug.WriteLine(
                        $"[PluginService.StaticDiscovery] Configured options for {pluginType.FullName} (Type: {actualOptionsType.Name}, Section: {fullSectionPath})"
                    );
                }
                else
                {
                    Debug.WriteLine(
                        $"[PluginService.StaticDiscovery] Could not find services.Configure<T>(IConfiguration) method for plugin {pluginType.FullName}."
                    );
                }
            }
            else
            {
                Debug.WriteLine(
                    $"[PluginService.StaticDiscovery] Plugin type {pluginType.FullName} implements IConfigurablePlugin but static 'OptionsType' or 'DefaultConfigurationSectionName' properties were not found or returned null."
                );
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine(
                $"[PluginService.StaticDiscovery] Error while trying to configure options for plugin {pluginType.FullName} using static members: {ex.Message}"
            );
        }
    }

    // Helper for static manifest validation (no instance logger)
    private static bool ValidateManifestInternalStatic(PluginManifest? manifest, string manifestPath, ILogger? logger = null)
    {
        Action<string> logError = msg =>
        {
            logger?.LogError(msg);
            Debug.WriteLine($"[ValidateManifestStatic] {msg}");
        };
        Action<string> logWarning = msg =>
        {
            logger?.LogWarning(msg);
            Debug.WriteLine($"[ValidateManifestStatic] {msg}");
        };

        if (manifest == null)
        {
            logError($"Error in manifest {manifestPath}: Failed to deserialize.");
            return false;
        }
        if (!Guid.TryParse(manifest.Id, out _))
        {
            logError($"Error in manifest {manifestPath}: 'id' is missing or not a valid GUID.");
            return false;
        }
        if (string.IsNullOrWhiteSpace(manifest.Name))
        {
            logError($"Error in manifest {manifestPath}: 'name' is missing.");
            return false;
        }
        // ... (other validations, using logError or logWarning) ...
        return true;
    }

    public async Task InitializeLoadedPluginsAsync(List<Type> discoveredPluginTypes, IConfiguration applicationConfiguration)
    {
        if (LoadedPlugins.Any())
        {
            _logger.LogWarning("InitializeLoadedPluginsAsync called, but plugins seem already initialized. Skipping.");
            return;
        }
        _discoveredPluginTypesForInstance = discoveredPluginTypes ?? throw new ArgumentNullException(nameof(discoveredPluginTypes));
        if (!_discoveredPluginTypesForInstance.Any())
        {
            _logger.LogInformation("No plugin types provided to initialize. Skipping initialization phase.");
            return;
        }

        _logger.LogInformation("Initializing {Count} discovered plugin instances...", _discoveredPluginTypesForInstance.Count);
        _pluginHost = new PluginHost(_serviceProvider); // _serviceProvider is now fully built

        foreach (Type pluginType in _discoveredPluginTypesForInstance)
        {
            try
            {
                if (_serviceProvider.GetService(pluginType) is not IPlugin pluginInstance)
                {
                    _logger.LogError("Failed to resolve plugin instance from DI for type {PluginTypeName}. Skipping.", pluginType.FullName);
                    continue;
                }

                // IConfigurablePlugin options are now handled by IOptionsMonitor injected into the plugin's constructor.
                // The registration happened in the static DiscoverAndRegisterPlugins method.

                if (pluginInstance is IPluginUIPageProvider uiProvider)
                {
                    _logger.LogDebug("Plugin {PluginName} implements IPluginUIPageProvider. Registering UI page...", pluginInstance.Name);
                    PluginSettingsPageProviders.Add(
                        new PluginUIPageInfo(uiProvider.SettingsPageDisplayName, uiProvider.SettingsPageType, pluginInstance)
                    );
                    _logger.LogDebug(
                        "Registered UI page '{PageName}' for plugin {PluginName}.",
                        uiProvider.SettingsPageType.Name,
                        pluginInstance.Name
                    );
                }

                await pluginInstance.InitializeAsync(_pluginHost);
                LoadedPlugins.Add(pluginInstance);
                _logger.LogInformation(
                    "Successfully initialized plugin: {PluginName} (Version: {PluginVersion}, ID: {PluginId})",
                    pluginInstance.Name,
                    pluginInstance.Version,
                    pluginInstance.Id
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error initializing plugin instance of type {PluginTypeName}", pluginType.FullName);
            }
        }

        _logger.LogInformation("Plugin instance initialization complete. {Count} plugins fully loaded.", LoadedPlugins.Count);
        ProcessChatCommandPlugins();
        ProcessEventProcessorPlugins();
    }

    // ... ValidateManifest, VerifyInstanceMetadata (use instance _logger), ShutdownPluginsAsync, etc. ...
    private bool ValidateManifest(PluginManifest? manifest, string manifestPath)
    {
        return ValidateManifestInternalStatic(manifest, manifestPath, _logger);
    }

    // ... (rest of the class remains the same: VerifyInstanceMetadata, ShutdownPluginsAsync, etc.)
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
        PluginSettingsPageProviders.Clear();
        _commandRegistry.Clear();
        _pluginHost = null;
        _logger.LogInformation("All plugins shut down and lists cleared.");
    }

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

    private void ProcessEventProcessorPlugins()
    {
        int count = LoadedPlugins.OfType<IEventProcessorPlugin>().Count();
        _logger.LogInformation("Found {Count} event processor plugins.", count);
    }

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
                _logger.LogError(
                    "Cannot handle command '{Command}': PluginHost is null (was InitializeLoadedPluginsAsync called after DI build?).",
                    command
                );
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
