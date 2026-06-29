using CommunityToolkit.Mvvm.ComponentModel;

using LAMG.Domain.Enums;

namespace LAMG.UI.ViewModels;

/// <summary>
/// One rendered mix shown in the Results region of the Processing screen.
/// </summary>
public sealed partial class CompletedMixRow : ObservableObject
{
    public long MixId { get; init; }

    public int IndexInProject { get; init; }

    public MixMode Mode { get; init; }

    public string FileName { get; init; } = string.Empty;

    public string OutputPath { get; init; } = string.Empty;

    public int ActualDurationSec { get; init; }

    public OutputFormat OutputFormat { get; init; }

    [ObservableProperty]
    private MixStatus _status = MixStatus.Completed;
}
