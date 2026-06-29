using LAMG.Application.Abstractions.Audio;
using LAMG.Application.Abstractions.Persistence;
using LAMG.Common;
using LAMG.Domain.Enums;
using LAMG.Domain.Models;

using Microsoft.Extensions.Logging;

namespace LAMG.Application.UseCases.ImportBatches;

/// <summary>
/// Enumerates folders, creates one <see cref="Batch"/> per folder and
/// inserts stub <see cref="Track"/> rows in <c>Pending</c> status.
/// Analysis is performed by <see cref="AnalyzeTracks.AnalyzeTracksUseCase"/>.
/// </summary>
/// <remarks>
/// The stub-first design lets the job resume cleanly after a crash:
/// the per-track work is persisted, so we never re-enumerate a folder
/// after partial analysis.
/// </remarks>
public sealed class ImportBatchesUseCase
{
    private readonly IBatchImportService _batchImportService;
    private readonly IBatchRepository _batchRepository;
    private readonly ITrackRepository _trackRepository;
    private readonly ILogger<ImportBatchesUseCase> _logger;

    public ImportBatchesUseCase(
        IBatchImportService batchImportService,
        IBatchRepository batchRepository,
        ITrackRepository trackRepository,
        ILogger<ImportBatchesUseCase> logger)
    {
        _batchImportService = Guard.NotNull(batchImportService);
        _batchRepository = Guard.NotNull(batchRepository);
        _trackRepository = Guard.NotNull(trackRepository);
        _logger = Guard.NotNull(logger);
    }

    public async Task<IReadOnlyList<long>> ExecuteAsync(
        ImportBatchesRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        Guard.Positive(request.ProjectId);
        ArgumentNullException.ThrowIfNull(request.Folders);

        DateTimeOffset now = DateTimeOffset.UtcNow;
        List<long> batchIds = new(capacity: request.Folders.Count);

        foreach (string folder in request.Folders)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(folder))
            {
                _logger.LogWarning("Skipping empty folder entry in import request.");
                continue;
            }

            IReadOnlyList<string> filePaths;
            try
            {
                filePaths = await _batchImportService
                    .EnumerateAsync(folder, request.Recursive, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is DirectoryNotFoundException or UnauthorizedAccessException)
            {
                _logger.LogWarning(
                    ex,
                    "Could not enumerate folder '{Folder}'; creating an empty batch.",
                    folder);
                filePaths = Array.Empty<string>();
            }

            Batch batch = new()
            {
                ProjectId = request.ProjectId,
                SourceFolder = folder,
                ImportedAt = now,
                TrackCount = filePaths.Count,
            };

            long batchId = await _batchRepository
                .AddAsync(batch, cancellationToken)
                .ConfigureAwait(false);
            batchIds.Add(batchId);

            if (filePaths.Count > 0)
            {
                List<Track> stubs = new(filePaths.Count);
                foreach (string path in filePaths)
                {
                    stubs.Add(BuildPendingStub(batchId, path));
                }

                await _trackRepository
                    .AddRangeAsync(stubs, cancellationToken)
                    .ConfigureAwait(false);
            }

            _logger.LogInformation(
                "Imported batch {BatchId} from '{Folder}' with {Count} files.",
                batchId, folder, filePaths.Count);
        }

        return batchIds;
    }

    private static Track BuildPendingStub(long batchId, string filePath)
    {
        long fileSize = 0;
        try
        {
            fileSize = new FileInfo(filePath).Length;
        }
        catch (Exception)
        {
            // Size is not critical for the stub; analysis will overwrite it.
        }

        return new Track
        {
            BatchId = batchId,
            FullPath = filePath,
            FileName = Path.GetFileName(filePath),
            Format = DetectFormatFromExtension(filePath),
            FileSizeBytes = fileSize,
            DurationMs = 0,
            SampleRate = 0,
            Channels = 0,
            BitrateKbps = null,
            FileHash = string.Empty,
            AudioHash = string.Empty,
            SilenceLeadMs = 0,
            SilenceTailMs = 0,
            IntegratedLufs = null,
            TruePeakDb = null,
            Status = TrackStatus.Pending,
            TimesUsed = 0,
            LastUsedAt = null,
        };
    }

    private static AudioFormat DetectFormatFromExtension(string filePath)
    {
        string ext = Path.GetExtension(filePath);
        return string.Equals(ext, ".mp3", StringComparison.OrdinalIgnoreCase)
            ? AudioFormat.Mp3
            : AudioFormat.Wav;
    }
}
