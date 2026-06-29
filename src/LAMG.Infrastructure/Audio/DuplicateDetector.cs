using LAMG.Application.Abstractions.Audio;
using LAMG.Common;
using LAMG.Domain.Enums;
using LAMG.Domain.Models;

using Microsoft.Extensions.Logging;

namespace LAMG.Infrastructure.Audio;

/// <inheritdoc cref="IDuplicateDetector"/>
/// <remarks>
/// Pure in-memory grouping over <c>candidates ∪ existing</c>. Three
/// passes (filename / file_hash / audio_hash) each emit one
/// <see cref="DuplicateGroup"/> per shared key with two or more tracks.
/// A single track may appear in several groups — for example, two
/// copies that share a filename AND a hash produce two groups; the
/// resolution step deduplicates them when it applies the user's choice.
/// </remarks>
public sealed class DuplicateDetector : IDuplicateDetector
{
    private readonly ILogger<DuplicateDetector> _logger;

    public DuplicateDetector(ILogger<DuplicateDetector> logger)
    {
        _logger = Guard.NotNull(logger);
    }

    public Task<IReadOnlyList<DuplicateGroup>> FindAsync(
        IReadOnlyList<Track> candidates,
        IReadOnlyList<Track> existing,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(existing);

        cancellationToken.ThrowIfCancellationRequested();

        // Combine into one stream, skip Corrupted (they're not usable
        // anyway) and de-duplicate by track id so a track that appears
        // in both lists does not artificially create a group.
        Dictionary<long, Track> uniqueById = new(candidates.Count + existing.Count);
        AddRange(uniqueById, candidates);
        AddRange(uniqueById, existing);

        List<DuplicateGroup> result = new();

        AddGroups(
            result,
            uniqueById.Values,
            DuplicateMatchKind.FileName,
            static t => string.IsNullOrEmpty(t.FileName) ? null : t.FileName,
            StringComparer.OrdinalIgnoreCase);

        AddGroups(
            result,
            uniqueById.Values,
            DuplicateMatchKind.FileHash,
            static t => string.IsNullOrWhiteSpace(t.FileHash) ? null : t.FileHash,
            StringComparer.Ordinal);

        AddGroups(
            result,
            uniqueById.Values,
            DuplicateMatchKind.AudioHash,
            static t => string.IsNullOrWhiteSpace(t.AudioHash) ? null : t.AudioHash,
            StringComparer.Ordinal);

        _logger.LogDebug(
            "Duplicate detector inspected {Count} unique tracks; produced {Groups} groups.",
            uniqueById.Count, result.Count);

        return Task.FromResult<IReadOnlyList<DuplicateGroup>>(result);
    }

    private static void AddRange(Dictionary<long, Track> dict, IReadOnlyList<Track> tracks)
    {
        foreach (Track t in tracks)
        {
            if (t.Status == TrackStatus.Corrupted)
            {
                continue;
            }

            // Track id 0 means "not yet persisted" — ignore for safety.
            if (t.Id <= 0)
            {
                continue;
            }

            dict.TryAdd(t.Id, t);
        }
    }

    private static void AddGroups(
        List<DuplicateGroup> result,
        IEnumerable<Track> tracks,
        DuplicateMatchKind kind,
        Func<Track, string?> keySelector,
        IEqualityComparer<string> comparer)
    {
        Dictionary<string, List<long>> buckets = new(comparer);

        foreach (Track t in tracks)
        {
            string? key = keySelector(t);
            if (key is null)
            {
                continue;
            }

            if (!buckets.TryGetValue(key, out List<long>? ids))
            {
                ids = new List<long>(capacity: 2);
                buckets[key] = ids;
            }

            ids.Add(t.Id);
        }

        foreach (KeyValuePair<string, List<long>> bucket in buckets)
        {
            if (bucket.Value.Count < 2)
            {
                continue;
            }

            bucket.Value.Sort();
            result.Add(new DuplicateGroup(kind, bucket.Value));
        }
    }
}
