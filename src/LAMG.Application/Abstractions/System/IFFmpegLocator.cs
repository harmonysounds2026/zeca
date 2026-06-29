namespace LAMG.Application.Abstractions.System;

/// <summary>
/// Resolves the absolute paths of the <c>ffmpeg</c> and <c>ffprobe</c>
/// executables. Resolution order is:
///  1. <c>tools/ffmpeg/</c> next to the application (bundled).
///  2. A user-configured override path.
///  3. The system <c>PATH</c>.
/// </summary>
public interface IFFmpegLocator
{
    /// <summary>
    /// Absolute path to <c>ffmpeg</c>. Returns <c>null</c> when no
    /// candidate can be located.
    /// </summary>
    string? GetFFmpegPath();

    /// <summary>Absolute path to <c>ffprobe</c>, or <c>null</c>.</summary>
    string? GetFFprobePath();

    /// <summary>
    /// Verifies that both executables run and report a usable version.
    /// Should be called once at startup.
    /// </summary>
    Task<bool> VerifyAvailableAsync(CancellationToken cancellationToken = default);
}
