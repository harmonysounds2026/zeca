namespace LAMG.Application.Abstractions.Audio;

/// <summary>
/// Reads container-level metadata from an audio file using ffprobe.
/// </summary>
public interface IFFprobeService
{
    Task<ProbeResult> ProbeAsync(
        string filePath,
        CancellationToken cancellationToken = default);
}
