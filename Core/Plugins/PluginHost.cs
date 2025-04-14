using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StreamWeaver.Core.Services;

namespace StreamWeaver.Core.Plugins;

/// <summary>
/// Concrete implementation of IPluginHost provided to plugins during initialization.
/// </summary>
internal class PluginHost : IPluginHost
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IMessenger _messenger;
    private readonly UnifiedEventService _unifiedEventService;
    private readonly ILogger<PluginHost> _logger;

    public PluginHost(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));

        // Resolve required services immediately. If resolution fails, it indicates a setup error.
        _messenger = _serviceProvider.GetRequiredService<IMessenger>();
        _unifiedEventService = _serviceProvider.GetRequiredService<UnifiedEventService>();
        _logger = _serviceProvider.GetRequiredService<ILogger<PluginHost>>();

        _logger.LogDebug("Instance created and core services resolved.");
    }

    /// <summary>
    /// Gets the application's main messenger instance.
    /// </summary>
    public IMessenger GetMessenger() => _messenger;

    /// <summary>
    /// Sends a chat message through the StreamWeaver core.
    /// </summary>
    public Task SendChatMessageAsync(string platform, string senderAccountId, string target, string message) =>
        // Delegate the call to the core UnifiedEventService.
        _unifiedEventService.SendChatMessageAsync(platform, senderAccountId, target, message);
}
