using System.Windows;

using LAMG.Application.UseCases.DetectDuplicates;
using LAMG.Common;
using LAMG.Domain.Enums;
using LAMG.Domain.Models;
using LAMG.UI.ViewModels.Dialogs;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace LAMG.UI.Services;

/// <inheritdoc cref="IDialogService"/>
/// <remarks>
/// Each call marshals to the UI dispatcher and shows the matching
/// <see cref="Window"/> as a modal child of <see cref="Application.MainWindow"/>.
/// </remarks>
public sealed class DialogService : IDialogService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DialogService> _logger;

    public DialogService(
        IServiceProvider serviceProvider,
        ILogger<DialogService> logger)
    {
        _serviceProvider = Guard.NotNull(serviceProvider);
        _logger = Guard.NotNull(logger);
    }

    public Task<string?> ShowFolderPickerAsync(string? initialDirectory = null)
    {
        return DispatchAsync<string?>(() =>
        {
            OpenFolderDialog dialog = new()
            {
                Multiselect = false,
                Title = "Select folder",
                InitialDirectory = initialDirectory ?? string.Empty,
            };

            bool? result = dialog.ShowDialog(Application.Current.MainWindow);
            return result == true ? dialog.FolderName : null;
        });
    }

    public Task<bool> ConfirmAsync(string title, string message)
    {
        return DispatchAsync(() =>
        {
            MessageBoxResult result = MessageBox.Show(
                Application.Current.MainWindow,
                message,
                title,
                MessageBoxButton.YesNo,
                MessageBoxImage.Question,
                MessageBoxResult.No);
            return result == MessageBoxResult.Yes;
        });
    }

    public Task ShowInfoAsync(string title, string message)
    {
        return DispatchAsync(() =>
        {
            MessageBox.Show(
                Application.Current.MainWindow,
                message,
                title,
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        });
    }

    public Task ShowErrorAsync(string title, string message)
    {
        return DispatchAsync(() =>
        {
            MessageBox.Show(
                Application.Current.MainWindow,
                message,
                title,
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        });
    }

    public Task<DuplicateResolution?> ShowDuplicateResolutionAsync(DuplicateDetectionReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return DispatchAsync<DuplicateResolution?>(() =>
        {
            DuplicateResolutionDialogViewModel vm = new(report);
            Views.Dialogs.DuplicateResolutionDialog dialog = new(vm)
            {
                Owner = Application.Current.MainWindow,
            };
            dialog.ShowDialog();
            return dialog.SelectedResolution;
        });
    }

    public Task<ResumeJobChoice> ShowResumeJobAsync(Job job)
    {
        ArgumentNullException.ThrowIfNull(job);
        return DispatchAsync<ResumeJobChoice>(() =>
        {
            ResumeJobDialogViewModel vm = new(job);
            Views.Dialogs.ResumeJobDialog dialog = new(vm)
            {
                Owner = Application.Current.MainWindow,
            };
            dialog.ShowDialog();
            return dialog.SelectedChoice;
        });
    }

    public Task<IReadOnlyCollection<long>?> ShowReusePoolSelectionAsync(IReadOnlyList<Batch> batches)
    {
        ArgumentNullException.ThrowIfNull(batches);
        return DispatchAsync<IReadOnlyCollection<long>?>(() =>
        {
            ReusePoolSelectionDialogViewModel vm = new(batches);
            Views.Dialogs.ReusePoolSelectionDialog dialog = new(vm)
            {
                Owner = Application.Current.MainWindow,
            };
            dialog.ShowDialog();
            return dialog.SelectedBatchIds;
        });
    }

    private static Task DispatchAsync(Action action)
    {
        TaskCompletionSource<bool> tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            try
            {
                action();
                tcs.TrySetResult(true);
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        });
        return tcs.Task;
    }

    private static Task<T> DispatchAsync<T>(Func<T> func)
    {
        TaskCompletionSource<T> tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            try
            {
                tcs.TrySetResult(func());
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        });
        return tcs.Task;
    }
}
