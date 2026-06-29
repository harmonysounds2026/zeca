using LAMG.Application.UseCases.DetectDuplicates;
using LAMG.Domain.Enums;
using LAMG.Domain.Models;

namespace LAMG.UI.Services;

/// <summary>
/// User-facing dialogs and pickers used by ViewModels. Keeping this
/// behind an interface lets ViewModels stay testable.
/// </summary>
public interface IDialogService
{
    /// <summary>
    /// Shows a folder picker and returns the selected absolute path,
    /// or <c>null</c> if the user cancelled.
    /// </summary>
    Task<string?> ShowFolderPickerAsync(string? initialDirectory = null);

    /// <summary>
    /// Shows a confirmation prompt with Yes/No and returns the choice.
    /// </summary>
    Task<bool> ConfirmAsync(string title, string message);

    /// <summary>Shows a simple informational prompt with OK.</summary>
    Task ShowInfoAsync(string title, string message);

    /// <summary>Shows an error prompt with OK.</summary>
    Task ShowErrorAsync(string title, string message);

    /// <summary>
    /// Asks the user how to handle the duplicate groups found during
    /// import. Returns <c>null</c> if the user cancelled the dialog.
    /// </summary>
    Task<DuplicateResolution?> ShowDuplicateResolutionAsync(DuplicateDetectionReport report);

    /// <summary>
    /// Asks the user which interrupted job (if any) to resume. Returns
    /// <see cref="ResumeJobChoice.Cancel"/> when the user closes the dialog.
    /// </summary>
    Task<ResumeJobChoice> ShowResumeJobAsync(Job job);

    /// <summary>
    /// Asks the user to pick which batches feed the reuse-mode pool.
    /// Returns the selected batch ids or <c>null</c> when the user
    /// cancels (the orchestrator will keep waiting in that case).
    /// </summary>
    Task<IReadOnlyCollection<long>?> ShowReusePoolSelectionAsync(IReadOnlyList<Batch> batches);
}

/// <summary>
/// Possible outcomes of <see cref="IDialogService.ShowResumeJobAsync"/>.
/// </summary>
public enum ResumeJobChoice
{
    Resume = 1,
    Discard = 2,
    Cancel = 3,
}
