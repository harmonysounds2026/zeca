namespace LAMG.Domain.Enums;

/// <summary>
/// Strategy used to select tracks for a mix.
/// </summary>
public enum MixMode
{
    /// <summary>
    /// Tracks are taken from a single batch without repetition.
    /// One Unique mix is generated per imported batch.
    /// </summary>
    Unique = 1,

    /// <summary>
    /// Tracks may be reused across mixes. The pool of source batches is
    /// chosen explicitly by the user before this mode runs.
    /// </summary>
    Reuse = 2,
}
