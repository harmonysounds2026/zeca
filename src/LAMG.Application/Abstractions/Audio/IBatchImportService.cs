namespace LAMG.Application.Abstractions.Audio;

/// <summary>
/// Enumerates supported audio files inside a folder. Pure file-system
/// concern; performs no analysis.
/// </summary>
public interface IBatchImportService
{
    /// <summary>
    /// Returns absolute paths of every supported audio file under
    /// <paramref name="folder"/>. The order is filesystem-dependent.
    /// </summary>
    /// <param name="folder">Absolute folder path.</param>
    /// <param name="recursive">If true, sub-folders are scanned as well.</param>
    Task<IReadOnlyList<string>> EnumerateAsync(
        string folder,
        bool recursive,
        CancellationToken cancellationToken = default);
}
