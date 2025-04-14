using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace StreamWeaver.Core.Services.Logging;

/// <summary>
/// Provides ObservableLogger instances that write to the LogViewerService.
/// </summary>
[ProviderAlias("Observable")] // Alias for configuration
public sealed partial class ObservableLoggerProvider(LogViewerService logViewerService) : ILoggerProvider
{
    private readonly LogViewerService _logViewerService = logViewerService;
    private readonly ConcurrentDictionary<string, ObservableLogger> _loggers = new(StringComparer.OrdinalIgnoreCase);

    public ILogger CreateLogger(string categoryName) => _loggers.GetOrAdd(categoryName, name => new ObservableLogger(name, _logViewerService));

    public void Dispose()
    {
        _loggers.Clear();
        GC.SuppressFinalize(this);
    }
}
