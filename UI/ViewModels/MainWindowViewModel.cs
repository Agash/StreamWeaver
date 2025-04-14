using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;

namespace StreamWeaver.UI.ViewModels;

/// <summary>
/// ViewModel for the main application window (<see cref="MainWindow"/>).
/// This ViewModel typically coordinates high-level application state or actions,
/// although much of the navigation logic might reside in the MainWindow's code-behind for simpler cases.
/// </summary>
public partial class MainWindowViewModel : ObservableObject
{
    private readonly ILogger<MainWindowViewModel> _logger;
    private readonly IServiceProvider _serviceProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="MainWindowViewModel"/> class.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="serviceProvider">The service provider for resolving dependencies.</param>
    public MainWindowViewModel(ILogger<MainWindowViewModel> logger, IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;

        _logger.LogInformation("Initialized.");
    }
}
