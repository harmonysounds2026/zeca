namespace LAMG.Application.Abstractions.Audio;

/// <summary>
/// Fine-grained progress reported by <see cref="IMixRenderer"/> while
/// a single mix is being rendered.
/// </summary>
public sealed record RenderProgress(
    string Stage,
    int CurrentTrackIndex,
    int TotalTracks,
    double FractionComplete);
