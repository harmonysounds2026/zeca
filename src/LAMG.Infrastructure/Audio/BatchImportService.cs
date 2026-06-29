using LAMG.Application.Abstractions.Audio;
using LAMG.Common;

using Microsoft.Extensions.Logging;

namespace LAMG.Infrastructure.Audio;

/// <inheritdoc cref="IBatchImportService"/>
public sealed class BatchImportService : IBatchImportService
{
    /// <summary>
    /// Supported audio file extensions. Comparison is always case-insensitive.
    /// </summary>
    private static readonly string[] SupportedExtensions =
    [
        ".mp3",
        ".wav",
    ];

    private readonly ILogger<BatchImportService> _logger;

    public BatchImportService(ILogger<BatchImportService> logger)
    {
        _logger = Guard.NotNull(logger);
    }

    public Task<IReadOnlyList<string>> EnumerateAsync(
        string folder,
        bool recursive,
        CancellationToken cancellationToken = default)
    {
        Guard.NotNullOrWhiteSpace(folder);

        if (!Directory.Exists(folder))
        {
            throw new DirectoryNotFoundException($"Folder not found: {folder}");
        }

        // Enumeration is bound by disk speed; offload to a worker so
        // the caller's context (often the UI thread) stays free.
        return Task.Run(() => EnumerateCore(folder, recursive, cancellationToken), cancellationToken);
    }

    private IReadOnlyList<string> EnumerateCore(
        string folder,
        bool recursive,
        CancellationToken cancellationToken)
    {
        EnumerationOptions options = new()
        {
            RecurseSubdirectories = recursive,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
            ReturnSpecialDirectories = false,
        };

        // A single "*" pattern + post-filter is portable across Windows
        // (case-insensitive matching) and any future cross-platform host.
        List<string> result = new(capacity: 256);
        foreach (string path in Directory.EnumerateFiles(folder, "*", options))
        {
            cancellationToken.ThrowIfCancellationRequested();

            string ext = Path.GetExtension(path);
            if (IsSupportedExtension(ext))
            {
                result.Add(path);
            }
        }

        // Deterministic ordering helps with debugging, repeatable
        // mix-planning seeds, and stable user expectations.
        result.Sort(StringComparer.OrdinalIgnoreCase);

        _logger.LogDebug(
            "Enumerated {Count} audio files in {Folder} (recursive={Recursive}).",
            result.Count, folder, recursive);

        return result;
    }

    private static bool IsSupportedExtension(string extension)
    {
        if (string.IsNullOrEmpty(extension))
        {
            return false;
        }

        foreach (string supported in SupportedExtensions)
        {
            if (string.Equals(extension, supported, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
