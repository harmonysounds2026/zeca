using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

using LAMG.Application.Abstractions;
using LAMG.Application.Abstractions.Persistence;
using LAMG.Application.Abstractions.System;
using LAMG.Application.Settings;
using LAMG.Application.UseCases.AnalyzeTracks;
using LAMG.Application.UseCases.DetectDuplicates;
using LAMG.Application.UseCases.ImportBatches;
using LAMG.Application.UseCases.PlanMixes;
using LAMG.Common;
using LAMG.Domain.Enums;
using LAMG.Domain.Models;

using Microsoft.Extensions.Logging;

namespace LAMG.Application.Jobs;

/// <summary>
/// Single entry point for the long-running job that imports folders,
/// analyzes tracks, and resolves duplicates. STEP 3 ends at
/// <see cref="JobStage.Done"/> after duplicate resolution; STEP 4 will
/// extend it into planning and rendering inside this same class.
/// </summary>
/// <remarks>
/// Concurrency model: one active job at a time. State is protected by
/// a single object lock; the work itself runs on a background task and
/// is cancelled via a linked <see cref="CancellationTokenSource"/>.
/// A separate background task heartbeats the Jobs row every few
/// seconds so the crash recovery service can detect interrupted runs.
/// </remarks>
public sealed class JobOrchestrator : IJobOrchestrator
{
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(5);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly ImportBatchesUseCase _importBatches;
    private readonly AnalyzeTracksUseCase _analyzeTracks;
    private readonly DetectDuplicatesUseCase _detectDuplicates;
    private readonly PlanMixesUseCase _planMixes;
    private readonly Pipelines.RenderPipeline _renderPipeline;
    private readonly IJobRepository _jobRepository;
    private readonly IProjectRepository _projectRepository;
    private readonly IBatchRepository _batchRepository;
    private readonly IMixRepository _mixRepository;
    private readonly ISettingsService _settings;
    private readonly ICpuModeApplier _cpuModeApplier;
    private readonly ISystemAwakeKeeper _systemAwakeKeeper;
    private readonly IFFmpegLocator _ffmpegLocator;
    private readonly ILogger<JobOrchestrator> _logger;

    private readonly object _lock = new();
    private long? _activeJobId;
    private CancellationTokenSource? _activeCts;
    private CancellationTokenSource? _heartbeatCts;
    private TaskCompletionSource<DuplicateResolution>? _duplicateTcs;
    private TaskCompletionSource<IReadOnlyCollection<long>>? _reusePoolTcs;

    // Live counters owned by the single background work task. Only the
    // pipeline writes them; Report() reads them under no lock because
    // it runs on the same thread. The UI sees them via the Progress<T>
    // captured on the dispatcher.
    private int _jobMixesCompleted;
    private int _jobMixesPlanned;
    private int _jobTracksSkipped;
    private string? _jobCurrentMixName;

    public JobOrchestrator(
        ImportBatchesUseCase importBatches,
        AnalyzeTracksUseCase analyzeTracks,
        DetectDuplicatesUseCase detectDuplicates,
        PlanMixesUseCase planMixes,
        Pipelines.RenderPipeline renderPipeline,
        IJobRepository jobRepository,
        IProjectRepository projectRepository,
        IBatchRepository batchRepository,
        IMixRepository mixRepository,
        ISettingsService settings,
        ICpuModeApplier cpuModeApplier,
        ISystemAwakeKeeper systemAwakeKeeper,
        IFFmpegLocator ffmpegLocator,
        ILogger<JobOrchestrator> logger)
    {
        _importBatches = Guard.NotNull(importBatches);
        _analyzeTracks = Guard.NotNull(analyzeTracks);
        _detectDuplicates = Guard.NotNull(detectDuplicates);
        _planMixes = Guard.NotNull(planMixes);
        _renderPipeline = Guard.NotNull(renderPipeline);
        _jobRepository = Guard.NotNull(jobRepository);
        _projectRepository = Guard.NotNull(projectRepository);
        _batchRepository = Guard.NotNull(batchRepository);
        _mixRepository = Guard.NotNull(mixRepository);
        _settings = Guard.NotNull(settings);
        _cpuModeApplier = Guard.NotNull(cpuModeApplier);
        _systemAwakeKeeper = Guard.NotNull(systemAwakeKeeper);
        _ffmpegLocator = Guard.NotNull(ffmpegLocator);
        _logger = Guard.NotNull(logger);
    }

    public event EventHandler<JobLifecycleEvent>? LifecycleChanged;

    public event EventHandler<DuplicateResolutionRequestedEventArgs>? DuplicateResolutionRequested;

    public event EventHandler<ReusePoolRequestedEventArgs>? ReusePoolRequested;

    public event EventHandler<MixRenderedEventArgs>? MixRendered;

    // ============================================================
    // Public surface (IJobOrchestrator)
    // ============================================================

    public async Task<long> StartAsync(
        JobRequest request,
        IProgress<JobProgress>? progress,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        Guard.NotNullOrWhiteSpace(request.ProjectName);
        ArgumentNullException.ThrowIfNull(request.BatchFolders);

        // Pre-flight: refuse to even create the Project row if ffmpeg
        // is not available. Failing here gives the user a clear
        // actionable error instead of a mid-pipeline render failure.
        bool ffmpegOk = await _ffmpegLocator
            .VerifyAvailableAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!ffmpegOk)
        {
            throw new InvalidOperationException(
                "ffmpeg or ffprobe could not be located. Place ffmpeg.exe and ffprobe.exe " +
                "in the tools/ffmpeg/ folder next to the application, or set an override " +
                "path in Settings.");
        }

        CancellationTokenSource jobCts;
        CancellationTokenSource hbCts;

        lock (_lock)
        {
            if (_activeJobId.HasValue)
            {
                throw new InvalidOperationException(
                    $"A job ({_activeJobId.Value}) is already active. Cancel it first.");
            }

            jobCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            hbCts = new CancellationTokenSource();
            _activeCts = jobCts;
            _heartbeatCts = hbCts;
        }

        // Persist Project then Job (outside the lock).
        Project project = new()
        {
            Name = request.ProjectName,
            CreatedAt = DateTimeOffset.UtcNow,
            SettingsJson = JsonSerializer.Serialize(request.Settings, JsonOptions),
            OutputFolder = request.Settings.OutputFolder,
        };
        long projectId = await _projectRepository.AddAsync(project, jobCts.Token).ConfigureAwait(false);

        JobCheckpoint initial = new(
            ProjectId: projectId,
            Stage: JobStage.NotStarted,
            BatchIds: Array.Empty<long>(),
            ReusePoolBatchIds: null,
            UniqueMixesCompleted: 0,
            ReuseMixesCompleted: 0,
            TracksSkipped: 0);

        Job job = new()
        {
            ProjectId = projectId,
            JobType = JobType.AnalyzeImport,
            Status = JobStatus.Running,
            CurrentStage = JobStage.NotStarted,
            LastHeartbeat = DateTimeOffset.UtcNow,
            PayloadJson = JsonSerializer.Serialize(initial, JsonOptions),
            CreatedAt = DateTimeOffset.UtcNow,
        };
        long jobId = await _jobRepository.AddAsync(job, jobCts.Token).ConfigureAwait(false);

        lock (_lock)
        {
            _activeJobId = jobId;
        }

        StartBackgroundTasks(jobId, projectId, request, fromStage: JobStage.NotStarted,
            batchIds: null, resumedReusePool: null, progress, jobCts.Token, hbCts.Token);

        RaiseLifecycle(jobId, JobStage.NotStarted, JobStatus.Running, "Started");
        return jobId;
    }

    public async Task<Result> ResumeAsync(
        long jobId,
        IProgress<JobProgress>? progress,
        CancellationToken cancellationToken = default)
    {
        Guard.Positive(jobId);

        lock (_lock)
        {
            if (_activeJobId.HasValue && _activeJobId != jobId)
            {
                return Result.Failure(
                    $"Job {_activeJobId.Value} is already active; cannot resume {jobId} concurrently.");
            }
        }

        Job? job = await _jobRepository.GetByIdAsync(jobId, cancellationToken).ConfigureAwait(false);
        if (job is null)
        {
            return Result.Failure($"Job {jobId} not found.");
        }

        if (job.Status is not (JobStatus.Running or JobStatus.Paused))
        {
            return Result.Failure(
                $"Job {jobId} is in status {job.Status}; only Running or Paused jobs can be resumed.");
        }

        JobCheckpoint? checkpoint = TryDeserializeCheckpoint(job.PayloadJson);
        if (checkpoint is null)
        {
            return Result.Failure("Job checkpoint is missing or invalid; cannot resume.");
        }

        if (checkpoint.Stage is JobStage.NotStarted or JobStage.Importing)
        {
            return Result.Failure(
                "Cannot resume jobs interrupted during import. Please discard and restart.");
        }

        if (job.ProjectId is null)
        {
            return Result.Failure("Resumable job has no project id.");
        }

        Project? project = await _projectRepository.GetByIdAsync(job.ProjectId.Value, cancellationToken)
            .ConfigureAwait(false);
        if (project is null)
        {
            return Result.Failure("Project for this job is missing.");
        }

        AppSettings settings = DeserializeSettings(project.SettingsJson);
        JobRequest request = new(project.Name, Array.Empty<string>(), settings);

        // Pre-flight: same gate as StartAsync. Surface a friendly
        // Result.Failure so the UI can show it without crashing.
        bool ffmpegOk = await _ffmpegLocator
            .VerifyAvailableAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!ffmpegOk)
        {
            return Result.Failure(
                "ffmpeg or ffprobe could not be located. Place ffmpeg.exe and ffprobe.exe " +
                "in the tools/ffmpeg/ folder next to the application, or set an override " +
                "path in Settings.");
        }

        CancellationTokenSource jobCts;
        CancellationTokenSource hbCts;

        lock (_lock)
        {
            jobCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            hbCts = new CancellationTokenSource();
            _activeJobId = jobId;
            _activeCts = jobCts;
            _heartbeatCts = hbCts;
        }

        job.Status = JobStatus.Running;
        job.LastHeartbeat = DateTimeOffset.UtcNow;
        await _jobRepository.UpdateAsync(job, cancellationToken).ConfigureAwait(false);

        StartBackgroundTasks(jobId, project.Id, request, checkpoint.Stage,
            checkpoint.BatchIds, checkpoint.ReusePoolBatchIds, progress, jobCts.Token, hbCts.Token);

        RaiseLifecycle(jobId, checkpoint.Stage, JobStatus.Running, "Resumed");
        return Result.Success();
    }

    public async Task PauseAsync(long jobId, CancellationToken cancellationToken = default)
    {
        Guard.Positive(jobId);
        await SetTerminalStatusAndCancelAsync(jobId, JobStatus.Paused, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task CancelAsync(long jobId, CancellationToken cancellationToken = default)
    {
        Guard.Positive(jobId);
        await SetTerminalStatusAndCancelAsync(jobId, JobStatus.Cancelled, cancellationToken)
            .ConfigureAwait(false);
    }

    public Task SubmitDuplicateResolutionAsync(
        long jobId,
        DuplicateResolution resolution,
        CancellationToken cancellationToken = default)
    {
        Guard.Positive(jobId);

        TaskCompletionSource<DuplicateResolution>? tcs;
        lock (_lock)
        {
            if (_activeJobId != jobId)
            {
                _logger.LogDebug(
                    "SubmitDuplicateResolutionAsync for {JobId} ignored (active={Active}).",
                    jobId, _activeJobId);
                return Task.CompletedTask;
            }

            tcs = _duplicateTcs;
        }

        tcs?.TrySetResult(resolution);
        return Task.CompletedTask;
    }

    public Task SubmitReusePoolAsync(
        long jobId,
        IReadOnlyCollection<long> selectedBatchIds,
        CancellationToken cancellationToken = default)
    {
        Guard.Positive(jobId);
        ArgumentNullException.ThrowIfNull(selectedBatchIds);

        TaskCompletionSource<IReadOnlyCollection<long>>? tcs;
        lock (_lock)
        {
            if (_activeJobId != jobId)
            {
                _logger.LogDebug(
                    "SubmitReusePoolAsync for {JobId} ignored (active={Active}).",
                    jobId, _activeJobId);
                return Task.CompletedTask;
            }

            tcs = _reusePoolTcs;
        }

        // A defensive copy keeps the orchestrator's internal state
        // independent of whatever collection the caller passed.
        IReadOnlyCollection<long> snapshot = selectedBatchIds.ToArray();
        tcs?.TrySetResult(snapshot);
        return Task.CompletedTask;
    }

    // ============================================================
    // Background pipeline
    // ============================================================

    private void StartBackgroundTasks(
        long jobId,
        long projectId,
        JobRequest request,
        JobStage fromStage,
        IReadOnlyList<long>? batchIds,
        IReadOnlyCollection<long>? resumedReusePool,
        IProgress<JobProgress>? progress,
        CancellationToken workCt,
        CancellationToken heartbeatCt)
    {
        _ = Task.Run(() => HeartbeatLoopAsync(jobId, heartbeatCt), CancellationToken.None);
        _ = Task.Run(
            () => RunPipelineAsync(jobId, projectId, request, fromStage, batchIds, resumedReusePool, progress, workCt),
            CancellationToken.None);
    }

    private async Task RunPipelineAsync(
        long jobId,
        long projectId,
        JobRequest request,
        JobStage fromStage,
        IReadOnlyList<long>? batchIds,
        IReadOnlyCollection<long>? resumedReusePool,
        IProgress<JobProgress>? progress,
        CancellationToken cancellationToken)
    {
        Stopwatch sw = Stopwatch.StartNew();

        // One job runs at a time; safe to wipe the counters here.
        ResetJobState();

        // When resuming from a later stage, the planning and rendering
        // counters already have meaningful values in the DB. Restore
        // them now so the UI doesn't briefly show "0 / 0" while older
        // work is being skipped over.
        if (fromStage > JobStage.NotStarted)
        {
            await RestoreCountersOnResumeAsync(jobId, projectId, cancellationToken)
                .ConfigureAwait(false);
        }

        // Keep the OS awake for the entire job; released on dispose.
        using IDisposable awake = _systemAwakeKeeper.KeepAwake();

        try
        {
            // ---- Stage: Importing ----
            if (fromStage <= JobStage.Importing)
            {
                await SaveCheckpointAsync(
                    jobId,
                    new JobCheckpoint(projectId, JobStage.Importing, Array.Empty<long>(), null, 0, 0, 0),
                    cancellationToken).ConfigureAwait(false);
                RaiseLifecycle(jobId, JobStage.Importing, JobStatus.Running, "Enumerating folders");
                Report(progress, JobStage.Importing, "Enumerating folders…", 0.02, sw);

                ImportBatchesRequest importReq = new(
                    projectId, request.BatchFolders, request.Settings.ImportRecursively);
                IReadOnlyList<long> importedIds = await _importBatches
                    .ExecuteAsync(importReq, cancellationToken)
                    .ConfigureAwait(false);

                batchIds = importedIds;
                await SaveCheckpointAsync(
                    jobId,
                    new JobCheckpoint(projectId, JobStage.Analyzing, importedIds, null, 0, 0, 0),
                    cancellationToken).ConfigureAwait(false);
            }

            if (batchIds is null || batchIds.Count == 0)
            {
                _logger.LogWarning("Job {JobId} has no batches after import; finishing early.", jobId);
                await FinalizeSuccessAsync(jobId, cancellationToken).ConfigureAwait(false);
                Report(progress, JobStage.Done, "No batches imported.", 1.0, sw);
                RaiseLifecycle(jobId, JobStage.Done, JobStatus.Completed, "No content");
                return;
            }

            // ---- Stage: Analyzing ----
            int totalTracks = await ComputeTotalTracksAsync(batchIds, cancellationToken).ConfigureAwait(false);
            int readyCount = 0;

            if (fromStage <= JobStage.Analyzing)
            {
                RaiseLifecycle(jobId, JobStage.Analyzing, JobStatus.Running,
                    $"Analyzing {totalTracks} tracks");

                int parallelism = _cpuModeApplier.GetAnalysisParallelism(_settings.Current.CpuMode);
                AnalyzeTracksRequest analyzeReq = new(
                    batchIds,
                    request.Settings.SilenceThresholdDb,
                    request.Settings.SilenceMinDurationMs,
                    parallelism);

                Progress<int> analyzeProgress = new(done =>
                {
                    if (totalTracks <= 0) return;
                    double frac = 0.05 + 0.80 * ((double)done / totalTracks);
                    Report(progress, JobStage.Analyzing,
                        $"Analyzing track {done} of {totalTracks}", frac, sw);
                });

                readyCount = await _analyzeTracks
                    .ExecuteAsync(analyzeReq, analyzeProgress, cancellationToken)
                    .ConfigureAwait(false);

                int skipped = Math.Max(0, totalTracks - readyCount);
                _jobTracksSkipped = skipped;
                await SaveCheckpointAsync(
                    jobId,
                    new JobCheckpoint(projectId, JobStage.DetectingDuplicates,
                        batchIds, null, 0, 0, skipped),
                    cancellationToken).ConfigureAwait(false);
            }

            // ---- Stage: DetectingDuplicates ----
            if (fromStage <= JobStage.DetectingDuplicates)
            {
                RaiseLifecycle(jobId, JobStage.DetectingDuplicates, JobStatus.Running,
                    "Scanning for duplicates");
                Report(progress, JobStage.DetectingDuplicates, "Detecting duplicates…", 0.90, sw);

                DuplicateDetectionReport report = await _detectDuplicates
                    .DetectAsync(batchIds, cancellationToken)
                    .ConfigureAwait(false);

                if (report.HasDuplicates)
                {
                    Report(progress, JobStage.DetectingDuplicates,
                        $"{report.Groups.Count} duplicate group(s) — awaiting user", 0.92, sw);

                    DuplicateResolution resolution = await WaitForDuplicateResolutionAsync(
                        jobId, report, cancellationToken).ConfigureAwait(false);

                    Report(progress, JobStage.DetectingDuplicates,
                        $"Applying {resolution}", 0.95, sw);

                    await _detectDuplicates
                        .ApplyResolutionAsync(report, resolution, cancellationToken)
                        .ConfigureAwait(false);
                }

                await SaveCheckpointAsync(
                    jobId,
                    new JobCheckpoint(projectId, JobStage.PlanningUniqueMixes,
                        batchIds, null, 0, 0, Math.Max(0, totalTracks - readyCount)),
                    cancellationToken).ConfigureAwait(false);
            }

            // ---- Stage: PlanningUniqueMixes ----
            int uniqueMixesPlanned = 0;
            int reuseMixesPlanned = 0;
            IReadOnlyCollection<long>? selectedReusePool = resumedReusePool;
            int skippedTracks = Math.Max(0, totalTracks - readyCount);

            if (fromStage <= JobStage.PlanningUniqueMixes)
            {
                RaiseLifecycle(jobId, JobStage.PlanningUniqueMixes, JobStatus.Running,
                    "Planning unique mixes");
                Report(progress, JobStage.PlanningUniqueMixes,
                    "Planning unique mixes...", 0.91, sw);

                PlanMixesRequest uniqueReq = new(
                    projectId, MixMode.Unique, request.Settings, ReusePoolBatchIds: null);
                IReadOnlyList<long> createdUnique = await _planMixes
                    .ExecuteAsync(uniqueReq, cancellationToken)
                    .ConfigureAwait(false);
                uniqueMixesPlanned = createdUnique.Count;
                _jobMixesPlanned = uniqueMixesPlanned;

                JobStage nextStage = JobStage.RenderingUniqueMixes;

                await SaveCheckpointAsync(
                    jobId,
                    new JobCheckpoint(projectId, nextStage, batchIds,
                        selectedReusePool?.ToArray(),
                        uniqueMixesPlanned, 0, skippedTracks),
                    cancellationToken).ConfigureAwait(false);

                _logger.LogInformation(
                    "Job {JobId}: planned {Count} unique mix(es).", jobId, uniqueMixesPlanned);
            }

            // ---- Stage: RenderingUniqueMixes ----
            if (fromStage <= JobStage.RenderingUniqueMixes)
            {
                RaiseLifecycle(jobId, JobStage.RenderingUniqueMixes, JobStatus.Running,
                    "Rendering unique mixes");
                Report(progress, JobStage.RenderingUniqueMixes,
                    "Rendering unique mixes...", 0.88, sw);

                await RenderMixesAsync(
                    jobId, projectId, MixMode.Unique,
                    JobStage.RenderingUniqueMixes,
                    progressStart: 0.88, progressEnd: 0.94,
                    progress, sw, cancellationToken).ConfigureAwait(false);

                JobStage afterRenderStage = request.Settings.ReuseMixCount > 0
                    ? JobStage.AwaitingReusePool
                    : JobStage.Done;

                await SaveCheckpointAsync(
                    jobId,
                    new JobCheckpoint(projectId, afterRenderStage, batchIds,
                        selectedReusePool?.ToArray(),
                        uniqueMixesPlanned, 0, skippedTracks),
                    cancellationToken).ConfigureAwait(false);
            }

            if (request.Settings.ReuseMixCount > 0)
            {
                // ---- Stage: AwaitingReusePool ----
                if (fromStage <= JobStage.AwaitingReusePool && selectedReusePool is null)
                {
                    RaiseLifecycle(jobId, JobStage.AwaitingReusePool, JobStatus.Running,
                        "Awaiting reuse pool");
                    Report(progress, JobStage.AwaitingReusePool,
                        "Awaiting reuse pool selection...", 0.93, sw);

                    IReadOnlyList<Batch> eligible = await LoadEligibleBatchesForReusePoolAsync(
                        projectId, cancellationToken).ConfigureAwait(false);

                    selectedReusePool = await WaitForReusePoolAsync(
                        jobId, eligible, cancellationToken).ConfigureAwait(false);

                    await SaveCheckpointAsync(
                        jobId,
                        new JobCheckpoint(projectId, JobStage.PlanningReuseMixes, batchIds,
                            selectedReusePool.ToArray(),
                            uniqueMixesPlanned, 0, skippedTracks),
                        cancellationToken).ConfigureAwait(false);
                }

                // ---- Stage: PlanningReuseMixes ----
                if (fromStage <= JobStage.PlanningReuseMixes
                    && selectedReusePool is { Count: > 0 })
                {
                    RaiseLifecycle(jobId, JobStage.PlanningReuseMixes, JobStatus.Running,
                        $"Planning {request.Settings.ReuseMixCount} reuse mix(es)");
                    Report(progress, JobStage.PlanningReuseMixes,
                        "Planning reuse mixes...", 0.95, sw);

                    PlanMixesRequest reuseReq = new(
                        projectId, MixMode.Reuse, request.Settings, selectedReusePool);
                    IReadOnlyList<long> createdReuse = await _planMixes
                        .ExecuteAsync(reuseReq, cancellationToken)
                        .ConfigureAwait(false);
                    reuseMixesPlanned = createdReuse.Count;
                    _jobMixesPlanned = uniqueMixesPlanned + reuseMixesPlanned;

                    await SaveCheckpointAsync(
                        jobId,
                        new JobCheckpoint(projectId, JobStage.RenderingReuseMixes, batchIds,
                            selectedReusePool.ToArray(),
                            uniqueMixesPlanned, reuseMixesPlanned, skippedTracks),
                        cancellationToken).ConfigureAwait(false);

                    _logger.LogInformation(
                        "Job {JobId}: planned {Count} reuse mix(es).", jobId, reuseMixesPlanned);
                }
                else if (selectedReusePool is { Count: 0 })
                {
                    _logger.LogInformation(
                        "Job {JobId}: reuse pool is empty; skipping reuse planning.", jobId);
                }

                // ---- Stage: RenderingReuseMixes ----
                if (fromStage <= JobStage.RenderingReuseMixes
                    && selectedReusePool is { Count: > 0 })
                {
                    RaiseLifecycle(jobId, JobStage.RenderingReuseMixes, JobStatus.Running,
                        "Rendering reuse mixes");
                    Report(progress, JobStage.RenderingReuseMixes,
                        "Rendering reuse mixes...", 0.95, sw);

                    await RenderMixesAsync(
                        jobId, projectId, MixMode.Reuse,
                        JobStage.RenderingReuseMixes,
                        progressStart: 0.95, progressEnd: 0.99,
                        progress, sw, cancellationToken).ConfigureAwait(false);

                    await SaveCheckpointAsync(
                        jobId,
                        new JobCheckpoint(projectId, JobStage.Done, batchIds,
                            selectedReusePool.ToArray(),
                            uniqueMixesPlanned, reuseMixesPlanned, skippedTracks),
                        cancellationToken).ConfigureAwait(false);
                }
            }

            // ---- Done ----
            await FinalizeSuccessAsync(jobId, cancellationToken).ConfigureAwait(false);
            Report(progress, JobStage.Done, "Done.", 1.0, sw);
            RaiseLifecycle(jobId, JobStage.Done, JobStatus.Completed, "Completed");
        }
        catch (OperationCanceledException)
        {
            JobStatus finalStatus = await ReadCurrentStatusAsync(jobId).ConfigureAwait(false) switch
            {
                JobStatus.Paused => JobStatus.Paused,
                JobStatus.Cancelled => JobStatus.Cancelled,
                _ => JobStatus.Cancelled,
            };
            await FinalizeAsync(jobId, finalStatus).ConfigureAwait(false);
            Report(progress, JobStage.Done,
                finalStatus == JobStatus.Paused ? "Paused." : "Cancelled.", 0.0, sw);
            RaiseLifecycle(jobId, JobStage.Done, finalStatus, finalStatus.ToString());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Job {JobId} failed unexpectedly.", jobId);
            await FinalizeAsync(jobId, JobStatus.Failed).ConfigureAwait(false);
            Report(progress, JobStage.Done, $"Failed: {ex.Message}", 0.0, sw);
            RaiseLifecycle(jobId, JobStage.Done, JobStatus.Failed, ex.Message);
        }
        finally
        {
            StopHeartbeat();
            ClearActiveJob(jobId);
        }
    }

    private async Task<DuplicateResolution> WaitForDuplicateResolutionAsync(
        long jobId,
        DuplicateDetectionReport report,
        CancellationToken cancellationToken)
    {
        TaskCompletionSource<DuplicateResolution> tcs =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        lock (_lock)
        {
            _duplicateTcs = tcs;
        }

        try
        {
            try
            {
                DuplicateResolutionRequested?.Invoke(this,
                    new DuplicateResolutionRequestedEventArgs(jobId, report));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "DuplicateResolutionRequested subscriber threw; defaulting to ImportAll.");
                return DuplicateResolution.ImportAll;
            }

            using CancellationTokenRegistration reg = cancellationToken.Register(
                static state => ((TaskCompletionSource<DuplicateResolution>)state!).TrySetCanceled(),
                tcs);

            return await tcs.Task.ConfigureAwait(false);
        }
        finally
        {
            lock (_lock)
            {
                if (ReferenceEquals(_duplicateTcs, tcs))
                {
                    _duplicateTcs = null;
                }
            }
        }
    }

    private async Task<IReadOnlyCollection<long>> WaitForReusePoolAsync(
        long jobId,
        IReadOnlyList<Batch> eligibleBatches,
        CancellationToken cancellationToken)
    {
        TaskCompletionSource<IReadOnlyCollection<long>> tcs =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        lock (_lock)
        {
            _reusePoolTcs = tcs;
        }

        try
        {
            try
            {
                ReusePoolRequested?.Invoke(this,
                    new ReusePoolRequestedEventArgs(jobId, eligibleBatches));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "ReusePoolRequested subscriber threw; skipping reuse-mode for this job.");
                return Array.Empty<long>();
            }

            using CancellationTokenRegistration reg = cancellationToken.Register(
                static state => ((TaskCompletionSource<IReadOnlyCollection<long>>)state!)
                    .TrySetCanceled(),
                tcs);

            return await tcs.Task.ConfigureAwait(false);
        }
        finally
        {
            lock (_lock)
            {
                if (ReferenceEquals(_reusePoolTcs, tcs))
                {
                    _reusePoolTcs = null;
                }
            }
        }
    }

    private async Task<IReadOnlyList<Batch>> LoadEligibleBatchesForReusePoolAsync(
        long projectId,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<Batch> all = await _batchRepository
            .GetByProjectAsync(projectId, cancellationToken)
            .ConfigureAwait(false);

        // Hide batches with 0 tracks: they cannot contribute anything
        // to the reuse pool and would only clutter the dialog.
        List<Batch> eligible = new(all.Count);
        foreach (Batch b in all)
        {
            if (b.TrackCount > 0) eligible.Add(b);
        }
        return eligible;
    }

    private async Task HeartbeatLoopAsync(long jobId, CancellationToken cancellationToken)
    {
        try
        {
            using PeriodicTimer timer = new(HeartbeatInterval);
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                try
                {
                    await _jobRepository
                        .HeartbeatAsync(jobId, DateTimeOffset.UtcNow, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Heartbeat failed for job {JobId}; will retry on next tick.", jobId);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Heartbeat loop for job {JobId} terminated unexpectedly.", jobId);
        }
    }

    // ============================================================
    // Rendering loop
    // ============================================================

    /// <summary>
    /// Iterates the render pipeline's outcome stream and forwards each
    /// completed mix to subscribers via <see cref="MixRendered"/>,
    /// while updating job-level progress within
    /// <paramref name="progressStart"/>..<paramref name="progressEnd"/>.
    /// </summary>
    private async Task RenderMixesAsync(
        long jobId,
        long projectId,
        MixMode mode,
        JobStage stage,
        double progressStart,
        double progressEnd,
        IProgress<JobProgress>? progress,
        Stopwatch sw,
        CancellationToken cancellationToken)
    {
        int completed = 0;
        int failed = 0;

        await foreach (Pipelines.RenderOutcome outcome in _renderPipeline
            .RunAsync(projectId, mode, cancellationToken)
            .ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (outcome.Status == MixStatus.Completed)
            {
                completed++;
                _jobMixesCompleted++;
                _jobCurrentMixName = !string.IsNullOrEmpty(outcome.OutputPath)
                    ? System.IO.Path.GetFileName(outcome.OutputPath)
                    : string.Format(
                        System.Globalization.CultureInfo.InvariantCulture,
                        "mix_{0:000}",
                        outcome.IndexInProject);
            }
            else
            {
                failed++;
                _jobCurrentMixName = string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    "(failed) mix_{0:000}",
                    outcome.IndexInProject);
            }

            RaiseMixRendered(jobId, outcome);

            double frac = progressStart;
            if (outcome.TotalCount > 0)
            {
                double share = (double)outcome.CompletedIndex / outcome.TotalCount;
                frac = progressStart + ((progressEnd - progressStart) * share);
            }

            string desc = outcome.Status == MixStatus.Completed
                ? string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    "Rendered mix #{0} ({1}) - {2}s",
                    outcome.IndexInProject, outcome.Mode, outcome.ActualDurationSeconds)
                : string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    "Mix #{0} ({1}) failed: {2}",
                    outcome.IndexInProject, outcome.Mode, outcome.Error ?? "no detail");

            Report(progress, stage, desc, frac, sw);
        }

        _logger.LogInformation(
            "Job {JobId}: render pass for {Mode} done. completed={Completed}, failed={Failed}.",
            jobId, mode, completed, failed);
    }

    private void RaiseMixRendered(long jobId, Pipelines.RenderOutcome outcome)
    {
        try
        {
            MixRendered?.Invoke(this, new MixRenderedEventArgs(
                jobId: jobId,
                mixId: outcome.MixId,
                indexInProject: outcome.IndexInProject,
                mode: outcome.Mode,
                status: outcome.Status,
                outputFormat: outcome.OutputFormat,
                outputPath: outcome.OutputPath,
                actualDurationSeconds: outcome.ActualDurationSeconds,
                error: outcome.Error));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MixRendered subscriber threw.");
        }
    }

    // ============================================================
    // Helpers
    // ============================================================

    private async Task SetTerminalStatusAndCancelAsync(
        long jobId,
        JobStatus terminalStatus,
        CancellationToken cancellationToken)
    {
        CancellationTokenSource? cts;
        lock (_lock)
        {
            if (_activeJobId != jobId)
            {
                _logger.LogDebug(
                    "{Status} request for job {JobId} ignored (active={Active}).",
                    terminalStatus, jobId, _activeJobId);
                return;
            }
            cts = _activeCts;
        }

        // Write the terminal status to the DB BEFORE cancelling so the
        // work task's OCE handler reads it correctly.
        try
        {
            Job? job = await _jobRepository.GetByIdAsync(jobId, cancellationToken).ConfigureAwait(false);
            if (job is not null)
            {
                job.Status = terminalStatus;
                job.LastHeartbeat = DateTimeOffset.UtcNow;
                await _jobRepository.UpdateAsync(job, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to mark job {JobId} as {Status} before cancelling; cancelling anyway.",
                jobId, terminalStatus);
        }

        try { cts?.Cancel(); } catch (ObjectDisposedException) { /* race; harmless */ }
    }

    private async Task<int> ComputeTotalTracksAsync(
        IReadOnlyList<long> batchIds,
        CancellationToken cancellationToken)
    {
        int total = 0;
        foreach (long bid in batchIds)
        {
            Batch? b = await _batchRepository.GetByIdAsync(bid, cancellationToken).ConfigureAwait(false);
            if (b is not null) total += b.TrackCount;
        }
        return total;
    }

    private async Task SaveCheckpointAsync(
        long jobId,
        JobCheckpoint checkpoint,
        CancellationToken cancellationToken)
    {
        Job? job = await _jobRepository.GetByIdAsync(jobId, cancellationToken).ConfigureAwait(false);
        if (job is null) return;

        job.CurrentStage = checkpoint.Stage;
        job.PayloadJson = JsonSerializer.Serialize(checkpoint, JsonOptions);
        job.LastHeartbeat = DateTimeOffset.UtcNow;
        await _jobRepository.UpdateAsync(job, cancellationToken).ConfigureAwait(false);
    }

    private async Task FinalizeSuccessAsync(long jobId, CancellationToken cancellationToken)
    {
        Job? job = await _jobRepository.GetByIdAsync(jobId, cancellationToken).ConfigureAwait(false);
        if (job is null) return;

        job.Status = JobStatus.Completed;
        job.CurrentStage = JobStage.Done;
        job.FinishedAt = DateTimeOffset.UtcNow;
        job.LastHeartbeat = DateTimeOffset.UtcNow;
        await _jobRepository.UpdateAsync(job, cancellationToken).ConfigureAwait(false);
    }

    private async Task FinalizeAsync(long jobId, JobStatus status)
    {
        try
        {
            Job? job = await _jobRepository.GetByIdAsync(jobId, CancellationToken.None).ConfigureAwait(false);
            if (job is null) return;

            job.Status = status;
            job.LastHeartbeat = DateTimeOffset.UtcNow;

            // Only set FinishedAt for truly terminal statuses. A
            // Paused job is expected to be resumed later; keeping
            // FinishedAt null preserves the "not yet finished"
            // semantics in any audit log or analytics view.
            if (status is JobStatus.Completed or JobStatus.Cancelled or JobStatus.Failed)
            {
                job.FinishedAt = DateTimeOffset.UtcNow;
            }

            if (status == JobStatus.Completed)
            {
                job.CurrentStage = JobStage.Done;
            }

            await _jobRepository.UpdateAsync(job, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to finalize job {JobId} as {Status}.", jobId, status);
        }
    }

    private async Task<JobStatus> ReadCurrentStatusAsync(long jobId)
    {
        try
        {
            Job? job = await _jobRepository.GetByIdAsync(jobId, CancellationToken.None).ConfigureAwait(false);
            return job?.Status ?? JobStatus.Cancelled;
        }
        catch (Exception)
        {
            return JobStatus.Cancelled;
        }
    }

    private void StopHeartbeat()
    {
        CancellationTokenSource? hb;
        lock (_lock) { hb = _heartbeatCts; _heartbeatCts = null; }
        try { hb?.Cancel(); } catch (ObjectDisposedException) { }
        hb?.Dispose();
    }

    private void ClearActiveJob(long jobId)
    {
        lock (_lock)
        {
            if (_activeJobId != jobId) return;
            _activeJobId = null;
            try { _activeCts?.Dispose(); } catch (Exception) { }
            _activeCts = null;
            _duplicateTcs = null;
            _reusePoolTcs = null;
        }
    }

    private void RaiseLifecycle(long jobId, JobStage stage, JobStatus status, string? message)
    {
        try
        {
            LifecycleChanged?.Invoke(this, new JobLifecycleEvent(jobId, stage, status, message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LifecycleChanged subscriber threw.");
        }
    }

    private void ResetJobState()
    {
        _jobMixesCompleted = 0;
        _jobMixesPlanned = 0;
        _jobTracksSkipped = 0;
        _jobCurrentMixName = null;
    }

    /// <summary>
    /// Rehydrates the live UI counters from durable state at the start
    /// of a resumed job. Mix counts are queried from the DB (the most
    /// accurate source); the skipped-track count comes from the saved
    /// <see cref="JobCheckpoint"/> because analysis is the only thing
    /// that populates it and isn't re-run on resume. Best-effort: any
    /// failure here logs a warning and leaves the counter at zero
    /// rather than blocking the resume.
    /// </summary>
    private async Task RestoreCountersOnResumeAsync(
        long jobId,
        long projectId,
        CancellationToken cancellationToken)
    {
        try
        {
            IReadOnlyList<Mix> mixes = await _mixRepository
                .GetByProjectAsync(projectId, cancellationToken)
                .ConfigureAwait(false);

            int planned = 0;
            int completed = 0;
            foreach (Mix m in mixes)
            {
                planned++;
                if (m.Status == MixStatus.Completed) completed++;
            }

            _jobMixesPlanned = planned;
            _jobMixesCompleted = completed;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Could not restore mix counters from DB on resume of job {JobId}; UI will start at 0.",
                jobId);
        }

        try
        {
            Job? job = await _jobRepository
                .GetByIdAsync(jobId, cancellationToken)
                .ConfigureAwait(false);

            if (job is not null)
            {
                JobCheckpoint? cp = TryDeserializeCheckpoint(job.PayloadJson);
                if (cp is not null)
                {
                    _jobTracksSkipped = cp.TracksSkipped;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Could not restore tracks-skipped counter from checkpoint for job {JobId}.",
                jobId);
        }

        _logger.LogInformation(
            "Resume of job {JobId}: restored counters MixesPlanned={Planned}, MixesCompleted={Completed}, TracksSkipped={Skipped}.",
            jobId, _jobMixesPlanned, _jobMixesCompleted, _jobTracksSkipped);
    }

    private void Report(
        IProgress<JobProgress>? progress,
        JobStage stage,
        string description,
        double fraction,
        Stopwatch sw)
    {
        if (progress is null) return;
        try
        {
            progress.Report(new JobProgress(
                Stage: stage,
                StageDescription: description,
                MixesCompleted: _jobMixesCompleted,
                MixesPlanned: _jobMixesPlanned,
                TracksSkipped: _jobTracksSkipped,
                CurrentMixName: _jobCurrentMixName,
                OverallFraction: Math.Clamp(fraction, 0.0, 1.0),
                Elapsed: sw.Elapsed));
        }
        catch (Exception)
        {
            // Progress subscriber must never interrupt the job.
        }
    }

    private static JobCheckpoint? TryDeserializeCheckpoint(string payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson)) return null;
        try
        {
            return JsonSerializer.Deserialize<JobCheckpoint>(payloadJson, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static AppSettings DeserializeSettings(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return DefaultAppSettings.Value;
        try
        {
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? DefaultAppSettings.Value;
        }
        catch (JsonException)
        {
            return DefaultAppSettings.Value;
        }
    }
}
