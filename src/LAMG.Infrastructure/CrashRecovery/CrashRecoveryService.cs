using LAMG.Application.Abstractions;
using LAMG.Application.Abstractions.Persistence;
using LAMG.Common;
using LAMG.Domain.Enums;
using LAMG.Domain.Models;
using LAMG.Infrastructure.Configuration;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LAMG.Infrastructure.CrashRecovery;

/// <inheritdoc cref="ICrashRecoveryService"/>
public sealed class CrashRecoveryService : ICrashRecoveryService
{
    private readonly IJobRepository _jobs;
    private readonly IMixRepository _mixes;
    private readonly IProjectRepository _projects;
    private readonly InfrastructureOptions _options;
    private readonly ILogger<CrashRecoveryService> _logger;

    public CrashRecoveryService(
        IJobRepository jobs,
        IMixRepository mixes,
        IProjectRepository projects,
        IOptions<InfrastructureOptions> options,
        ILogger<CrashRecoveryService> logger)
    {
        _jobs = Guard.NotNull(jobs);
        _mixes = Guard.NotNull(mixes);
        _projects = Guard.NotNull(projects);
        ArgumentNullException.ThrowIfNull(options);
        _options = Guard.NotNull(options.Value);
        _logger = Guard.NotNull(logger);
    }

    public async Task<IReadOnlyList<Job>> FindResumableJobsAsync(
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset cutoff = DateTimeOffset.UtcNow - _options.StaleHeartbeatThreshold;

        IReadOnlyList<Job> resumable = await _jobs
            .GetResumableAsync(cutoff, cancellationToken)
            .ConfigureAwait(false);

        if (resumable.Count > 0)
        {
            _logger.LogInformation(
                "Found {Count} resumable job(s) with heartbeat older than {Cutoff:O}.",
                resumable.Count, cutoff);
        }

        return resumable;
    }

    /// <summary>
    /// Cleans up leftover state from a job that was interrupted while
    /// the render pipeline was running.
    /// <list type="number">
    ///   <item>Deletes <c>*.tmp</c> files in the project's output
    ///         folder — these are partially-written mix files left
    ///         behind when ffmpeg was killed.</item>
    ///   <item>Resets every mix still in
    ///         <see cref="MixStatus.Rendering"/> back to
    ///         <see cref="MixStatus.Planned"/> so the resumed render
    ///         pipeline picks them up.</item>
    /// </list>
    /// Each step is independently failure-tolerant: a problem deleting
    /// one file or updating one mix is logged but doesn't abort the
    /// rest of the cleanup.
    /// </summary>
    public async Task CleanupOrphansAsync(Job job, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);
        if (job.ProjectId is not long projectId)
        {
            _logger.LogDebug("Job {JobId} has no project; nothing to clean.", job.Id);
            return;
        }

        Project? project = await _projects
            .GetByIdAsync(projectId, cancellationToken)
            .ConfigureAwait(false);

        await DeleteTempFilesAsync(project, cancellationToken).ConfigureAwait(false);
        await ResetRenderingMixesAsync(projectId, cancellationToken).ConfigureAwait(false);
    }

    private Task DeleteTempFilesAsync(Project? project, CancellationToken cancellationToken)
    {
        if (project is null
            || string.IsNullOrWhiteSpace(project.OutputFolder)
            || !Directory.Exists(project.OutputFolder))
        {
            return Task.CompletedTask;
        }

        // Synchronous filesystem operations off the dispatcher / job
        // thread; offload to a worker so cancellation propagates.
        return Task.Run(() =>
        {
            int removed = 0;
            try
            {
                foreach (string tmpFile in Directory.EnumerateFiles(
                    project.OutputFolder, "*.tmp", SearchOption.TopDirectoryOnly))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        File.Delete(tmpFile);
                        removed++;
                        _logger.LogInformation(
                            "Deleted orphan temp file: {Path}", tmpFile);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(
                            ex,
                            "Could not delete orphan temp file '{Path}'; will retry on next startup.",
                            tmpFile);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Could not enumerate output folder '{Folder}' for orphans.",
                    project.OutputFolder);
            }

            if (removed > 0)
            {
                _logger.LogInformation(
                    "Removed {Count} orphan temp file(s) from '{Folder}'.",
                    removed, project.OutputFolder);
            }
        }, cancellationToken);
    }

    private async Task ResetRenderingMixesAsync(long projectId, CancellationToken cancellationToken)
    {
        IReadOnlyList<Mix> mixes;
        try
        {
            mixes = await _mixes
                .GetByProjectAsync(projectId, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Could not enumerate mixes for project {ProjectId}; skipping render-status reset.",
                projectId);
            return;
        }

        int reset = 0;
        foreach (Mix mix in mixes)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (mix.Status != MixStatus.Rendering) continue;

            try
            {
                await _mixes
                    .UpdateStatusAsync(mix.Id, MixStatus.Planned, cancellationToken)
                    .ConfigureAwait(false);
                reset++;
                _logger.LogInformation(
                    "Mix {Id}: was Rendering when job stopped; reset to Planned for retry.",
                    mix.Id);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Could not reset Rendering mix {Id} back to Planned.",
                    mix.Id);
            }
        }

        if (reset > 0)
        {
            _logger.LogInformation(
                "Reset {Count} mix(es) from Rendering to Planned for project {ProjectId}.",
                reset, projectId);
        }
    }
}
