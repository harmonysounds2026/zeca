namespace LAMG.Domain.Models;

/// <summary>
/// Many-to-many association between a <see cref="Mix"/> and the
/// <see cref="Batch"/> objects that contributed tracks to it.
/// <para>
/// Unique-mode mixes have exactly one entry (the originating batch).
/// Reuse-mode mixes have one entry per batch chosen for the reuse pool.
/// </para>
/// </summary>
public sealed class MixBatch
{
    public long MixId { get; set; }

    public long BatchId { get; set; }
}
