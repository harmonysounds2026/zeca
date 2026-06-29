using System.Runtime.CompilerServices;

using LAMG.Application.Abstractions.Persistence;
using LAMG.Application.UseCases.RenderMix;
using LAMG.Common;
using LAMG.Domain.Enums;
using LAMG.Domain.Models;

using Microsoft.Extensions.Logging;

namespace LAMG.Application.Pipelines;

/// <summary>
/// Outcome of rendering one mix inside <see cref="RenderPipeline"/>.
/// Emitted by the pipeline's <see cref="IAsyncEnumerable{T}"/> so the
/// orchestrator can raise per-mix UI events and update progress as
/// work happens, without coupling the pipeline to the event bus.
/// </summary>
public sealed record RenderOutcome(
    long MixId,
    int IndexInProject,
    MixMode Mode,
    MixStatus Status,
    OutputFormat OutputFormat,
    string? OutputPath,
    int ActualDurationSeconds,
    string? Error,
    int CompletedIndex,
    int TotalCount);

/// <summary>
/// Iterates the Planned mixes for a project (filtered by mode), runs
/// each through <see cref="RenderMixUseCase"/>, and yields a
/// <see cref="RenderOutcome"/> per finished mix. Per-mix failures are
/// isolated: an exception or Result.Failure becomes a yielded outcome
/// with <see cref="MixStatus.Failed"/>, and the loop moves on to the
/// next mix.
/// </summary>
/// <remarks>
/// Cancellation aborts the loop cleanly — already-completed mixes
/// remain Completed in the database (the renderer persists them
/// before returning). The orchestrator's OCE handler finalizes the
/// job and the resume path picks up at the next Planned mix.
/// </remarks>
public sealed class RenderPipeline
{
    private readonly RenderMixUseCase _renderMixUseCase;
    private readonly IMixRepository _mixRepository;
    private readonly ILogger<RenderPipeline> _logger;

    public RenderPipeline(
        RenderMixUseCase renderMixUseCase,
        IMixRepository mixRepository,
        ILogger<RenderPipeline> logger)
    {
        _renderMixUseCase = Guard.NotNull(renderMixUseCase);
        _mixRepository = Guard.NotNull(mixRepository);
        _logger = Guard.NotNull(logger);
    }

    /// <summary>
    /// Streams render outcomes one at a time. The caller iterates with
    /// <c>await foreach</c> and decides what to do with each result.
    /// </summary>
    public async IAsyncEnumerable<RenderOutcome> RunAsync(
        long projectId,
        MixMode mode,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Guard.Positive(projectId);

        IReadOnlyList<Mix> allMixes = await _mixRepository
            .GetByProjectAsync(projectId, cancellationToken)
            .ConfigureAwait(false);

        // Only Planned mixes need work. Completed and Failed stay as
        // they are, which makes the pipeline naturally idempotent on
        // resume: a previously-completed mix is just not in the queue.
        List<Mix> queue = allMixes
            .Where(m => m.Mode == mode && m.Status == MixStatus.Planned)
            .OrderBy(m => m.IndexInProject)
            .ThenBy(m => m.Id)
            .ToList();

        int total = queue.Count;
        _logger.LogInformation(
            "RenderPipeline: project {ProjectId} mode {Mode} has {Count} planned mix(es) to render.",
            projectId, mode, total);

        for (int i = 0; i < queue.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Mix mix = queue[i];

            Result result = await RenderOneAsync(mix.Id, cancellationToken).ConfigureAwait(false);

            // Reload the mix so OutputPath / ActualSec reflect what
            // the renderer persisted. The renderer writes both
            // atomically before returning success.
            Mix? finalState = await _mixRepository
                .GetByIdAsync(mix.Id, cancellationToken)
                .ConfigureAwait(false);

            MixStatus status = result.IsSuccess
                ? MixStatus.Completed
                : MixStatus.Failed;

            yield return new RenderOutcome(
                MixId: mix.Id,
                IndexInProject: mix.IndexInProject,
                Mode: mix.Mode,
                Status: status,
                OutputFormat: mix.OutputFormat,
                OutputPath: finalState?.OutputPath,
                ActualDurationSeconds: finalState?.ActualSec ?? 0,
                Error: result.IsSuccess ? null : result.Error,
                CompletedIndex: i + 1,
                TotalCount: total);
        }
    }

    /// <summary>
    /// Wraps a single use-case call so an unexpected exception turns
    /// into a Result.Failure rather than tearing down the iterator.
    /// OperationCanceledException is re-thrown so the orchestrator can
    /// distinguish cancellation from per-mix failure.
    /// </summary>
    private async Task<Result> RenderOneAsync(long mixId, CancellationToken cancellationToken)
    {
        try
        {
            return await _renderMixUseCase
                .ExecuteAsync(
                    new RenderMixRequest(mixId),
                    progress: null,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "RenderPipeline: mix {Id} threw unexpectedly; treating as Failed and moving on.",
                mixId);
            return Result.Failure(ex.Message, ex);
        }
    }
}
