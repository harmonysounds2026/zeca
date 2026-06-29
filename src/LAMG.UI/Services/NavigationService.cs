using CommunityToolkit.Mvvm.ComponentModel;

using LAMG.Common;
using LAMG.UI.ViewModels;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LAMG.UI.Services;

/// <inheritdoc cref="INavigationService"/>
/// <remarks>
/// Resolves the page view-models lazily via <see cref="IServiceProvider"/>
/// rather than taking them as constructor parameters. This is what
/// breaks the dependency cycle:
/// <list type="bullet">
///   <item>ShellViewModel → INavigationService</item>
///   <item>NavigationService (this) → IServiceProvider only</item>
///   <item>ImportViewModel → INavigationService (already constructed)</item>
/// </list>
/// Direct constructor injection of <c>ImportViewModel</c> would close
/// the loop and DI would refuse to build the graph.
/// </remarks>
public sealed class NavigationService : INavigationService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<NavigationService> _logger;

    private ObservableObject? _currentViewModel;

    public NavigationService(
        IServiceProvider serviceProvider,
        ILogger<NavigationService> logger)
    {
        _serviceProvider = Guard.NotNull(serviceProvider);
        _logger = Guard.NotNull(logger);

        CurrentPage = AppPage.Import;
    }

    public AppPage CurrentPage { get; private set; }

    /// <summary>
    /// View-model for the active page. Resolved lazily on first access
    /// (or after a navigation) so the constructor of this service can
    /// run before any view-model is built.
    /// </summary>
    public ObservableObject CurrentViewModel
    {
        get
        {
            _currentViewModel ??= ResolveViewModel(CurrentPage);
            return _currentViewModel;
        }
    }

    public event EventHandler<AppPage>? NavigationChanged;

    public void NavigateTo(AppPage page)
    {
        if (page == CurrentPage && _currentViewModel is not null)
        {
            return;
        }

        CurrentPage = page;
        _currentViewModel = ResolveViewModel(page);

        _logger.LogDebug("Navigated to {Page}.", page);
        NavigationChanged?.Invoke(this, page);
    }

    private ObservableObject ResolveViewModel(AppPage page) => page switch
    {
        AppPage.Import => _serviceProvider.GetRequiredService<ImportViewModel>(),
        AppPage.Settings => _serviceProvider.GetRequiredService<SettingsViewModel>(),
        AppPage.Processing => _serviceProvider.GetRequiredService<ProcessingViewModel>(),
        _ => throw new ArgumentOutOfRangeException(nameof(page), page, "Unknown page."),
    };
}
