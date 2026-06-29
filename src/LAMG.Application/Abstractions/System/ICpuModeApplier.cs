using System.Diagnostics;

using LAMG.Domain.Enums;

namespace LAMG.Application.Abstractions.System;

/// <summary>
/// Applies the user-selected <see cref="CpuMode"/> to a child process
/// (ffmpeg, ffprobe). Sets <see cref="ProcessPriorityClass"/> and any
/// affinity hints. Failures should be logged and ignored; CPU mode is
/// best-effort.
/// </summary>
public interface ICpuModeApplier
{
    void Apply(Process process, CpuMode mode);

    /// <summary>
    /// Returns the recommended ffmpeg <c>-threads</c> value for the
    /// supplied mode. <c>0</c> means "ffmpeg auto".
    /// </summary>
    int GetFFmpegThreadCount(CpuMode mode);

    /// <summary>
    /// Returns the maximum number of mixes to render concurrently
    /// at the supplied CPU mode. v1 keeps this small.
    /// </summary>
    int GetConcurrentRenderCount(CpuMode mode);

    /// <summary>
    /// Returns the maximum number of concurrent track analyses for
    /// the supplied CPU mode. Analysis is cheaper than rendering, so
    /// this is generally higher than <see cref="GetConcurrentRenderCount"/>.
    /// </summary>
    int GetAnalysisParallelism(CpuMode mode);
}
