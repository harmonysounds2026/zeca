using System.Buffers;
using System.Security.Cryptography;

using LAMG.Application.Abstractions.Audio;
using LAMG.Common;

using Microsoft.Extensions.Logging;

namespace LAMG.Infrastructure.Audio;

/// <inheritdoc cref="IFileHasher"/>
/// <remarks>
/// Streams the file in 64 KB chunks into an <see cref="IncrementalHash"/>
/// so memory stays flat regardless of file size. Returns the lower-case
/// hex digest of the SHA-256.
/// </remarks>
public sealed class FileHasher : IFileHasher
{
    private const int BufferSize = 64 * 1024;

    private readonly ILogger<FileHasher> _logger;

    public FileHasher(ILogger<FileHasher> logger)
    {
        _logger = Guard.NotNull(logger);
    }

    public async Task<string> HashAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        Guard.NotNullOrWhiteSpace(filePath);

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"File not found: {filePath}", filePath);
        }

        using IncrementalHash hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            await using FileStream stream = new(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                BufferSize,
                FileOptions.SequentialScan | FileOptions.Asynchronous);

            int bytesRead;
            while ((bytesRead = await stream
                .ReadAsync(buffer.AsMemory(0, BufferSize), cancellationToken)
                .ConfigureAwait(false)) > 0)
            {
                hasher.AppendData(buffer, 0, bytesRead);
            }

            byte[] hashBytes = hasher.GetHashAndReset();
            string hex = Convert.ToHexString(hashBytes).ToLowerInvariant();

            _logger.LogTrace("Hashed file {Path} -> {Hash}.", filePath, hex);
            return hex;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer, clearArray: false);
        }
    }
}
