using LAMG.Application.Abstractions.Audio;
using LAMG.Common;

using Microsoft.Extensions.Logging;

namespace LAMG.Application.UseCases.RenderMix;

/// <summary>
/// Renders a single planned mix via <see cref="IMixRenderer"/>. The
/// renderer already handles per-track skip-on-missing, atomic writes,
/// and DB status updates; this use case adds a single retry around
/// the call so a transient AV/indexer file lock or one-off ffmpeg
/// hiccup doesn't permanently fail a mix in a long unattended job.
/// </summary>
public sealed class RenderMixUseCase
{
    private readonly IMixRenderer _mixRenderer;
    private readonly ILogger<RenderMixUseCase> _logger;

    public RenderMixUseCase(
        IMixRenderer mixRenderer,
        ILogger<RenderMixUseCase> logger)
    {
        _mixRenderer = Guard.NotNull(mixRenderer);
        _logger = Guard.NotNull(logger);
    }

    public async Task<Result> ExecuteAsync(
        RenderMixRequest request,
        IProgress<RenderProgress>? progress,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        Guard.Positive(request.MixId);

        _logger.LogDebug("RenderMixUseCase: rendering mix {Id}.", request.MixId);

        Result firstAttempt = await _mixRenderer
            .RenderAsync(request.MixId, progress, cancellationToken)
            .ConfigureAwait(false);

        if (firstAttempt.IsSuccess)
        {
            return firstAttempt;
        }

        // OperationCanceledException would have bubbled up already, so
        // a Failure here means the renderer logged + marked the mix
        // Failed and returned. The renderer is idempotent enough on
        // re-entry to re-attempt the same call (it flips the mix back
        // from Failed to Rendering at the start of each call).
        _logger.LogWarning(
            "Mix {Id}: first render attempt failed ({Error}); retrying once.",
            request.MixId,
            firstAttempt.Error ?? "no detail");

        Result retry = await _mixRenderer
            .RenderAsync(request.MixId, progress, cancellationToken)
            .ConfigureAwait(false);

        if (retry.IsSuccess)
        {
            _logger.LogInformation(
                "Mix {Id}: succeeded on retry after first attempt failed.",
                request.MixId);
            return retry;
        }

        _logger.LogError(
            "Mix {Id}: both render attempts failed; pipeline will continue with the next mix.",
            request.MixId);

        return retry;
    }
}
