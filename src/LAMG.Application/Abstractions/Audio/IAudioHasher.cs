namespace LAMG.Application.Abstractions.Audio;

/// <summary>
/// Computes a stable hash over the <em>decoded</em> PCM stream of an
/// audio file. The hash is independent of the source container, codec
/// and bitrate, which makes it suitable for detecting duplicates whose
/// bytes differ but whose audio content is identical.
/// </summary>
public interface IAudioHasher
{
    /// <summary>
    /// Returns the lower-case hexadecimal SHA-256 of the PCM stream
    /// produced by decoding <paramref name="filePath"/> to a fixed
    /// canonical format (s16le, stereo, 44.1 kHz).
    /// </summary>
    Task<string> HashAsync(
        string filePath,
        CancellationToken cancellationToken = default);
}
