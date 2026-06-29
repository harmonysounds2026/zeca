using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using LAMG.Common;
using LAMG.UI.Services;

namespace LAMG.UI.ViewModels;

/// <summary>
/// Root ViewModel for the shell window. Owns the three child VMs and
/// exposes the navigation commands wired to the sidebar buttons.
/// </summary>
public sealed partial class ShellViewModel : ObservableObject
{
    private readonly INavigationService _navigation;

    public ShellViewModel(
        INavigationService navigation,
        ImportViewModel import,
        SettingsViewModel settings,
        ProcessingViewModel processing)
    {
        _navigation = Guard.NotNull(navigation);
        Import = Guard.NotNull(import);
        Settings = Guard.NotNull(settings);
        Processing = Guard.NotNull(processing);

        _currentViewModel = _navigation.CurrentViewModel;
        _currentPage = _navigation.CurrentPage;
        _navigation.NavigationChanged += OnNavigationChanged;
    }

    public ImportViewModel Import { get; }

    public SettingsViewModel Settings { get; }

    public ProcessingViewModel Processing { get; }

    [ObservableProperty]
    private ObservableObject _currentViewModel;

    [ObservableProperty]
    private AppPage _currentPage;

    [ObservableProperty]
    private string _statusBarText = "Ready.";

    [RelayCommand]
    private void NavigateToImport() => _navigation.NavigateTo(AppPage.Import);

    [RelayCommand]
    private void NavigateToSettings() => _navigation.NavigateTo(AppPage.Settings);

    [RelayCommand]
    private void NavigateToProcessing() => _navigation.NavigateTo(AppPage.Processing);

    private void OnNavigationChanged(object? sender, AppPage page)
    {
        CurrentPage = page;
        CurrentViewModel = _navigation.CurrentViewModel;
    }
}
