using CommunityToolkit.Mvvm.ComponentModel;

namespace LAMG.UI.Services;

/// <summary>
/// The three top-level pages of the application shell. Mapped to a
/// concrete <see cref="ObservableObject"/> ViewModel by
/// <see cref="INavigationService"/>.
/// </summary>
public enum AppPage
{
    Import = 1,
    Settings = 2,
    Processing = 3,
}

/// <summary>
/// Swaps the current child ViewModel inside <c>ShellWindow</c>.
/// </summary>
public interface INavigationService
{
    /// <summary>Currently active page.</summary>
    AppPage CurrentPage { get; }

    /// <summary>ViewModel matching <see cref="CurrentPage"/>.</summary>
    ObservableObject CurrentViewModel { get; }

    /// <summary>Switches to the supplied page. No-op if already active.</summary>
    void NavigateTo(AppPage page);

    /// <summary>Raised after a successful navigation.</summary>
    event EventHandler<AppPage>? NavigationChanged;
}
