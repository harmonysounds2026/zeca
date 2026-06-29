using CommunityToolkit.Mvvm.ComponentModel;

using LAMG.Common;
using LAMG.UI.ViewModels;

using Microsoft.Extensions.Logging;

namespace LAMG.UI.Services;

/// <inheritdoc cref="INavigationService"/>
public sealed class NavigationService : INavigationService
{
    private readonly ImportViewModel _import;
    private readonly SettingsViewModel _settings;
    private readonly ProcessingViewModel _processing;
    private readonly ILogger<NavigationService> _logger;

    public NavigationService(
        ImportViewModel import,
        SettingsViewModel settings,
        ProcessingViewModel processing,
        ILogger<NavigationService> logger)
    {
        _import = Guard.NotNull(import);
        _settings = Guard.NotNull(settings);
        _processing = Guard.NotNull(processing);
        _logger = Guard.NotNull(logger);

        CurrentPage = AppPage.Import;
        CurrentViewModel = _import;
    }

    public AppPage CurrentPage { get; private set; }

    public ObservableObject CurrentViewModel { get; private set; }

    public event EventHandler<AppPage>? NavigationChanged;

    public void NavigateTo(AppPage page)
    {
        if (page == CurrentPage)
        {
            return;
        }

        CurrentPage = page;
        CurrentViewModel = page switch
        {
            AppPage.Import => _import,
            AppPage.Settings => _settings,
            AppPage.Processing => _processing,
            _ => throw new ArgumentOutOfRangeException(nameof(page), page, "Unknown page."),
        };

        _logger.LogDebug("Navigated to {Page}.", page);
        NavigationChanged?.Invoke(this, page);
    }
}
