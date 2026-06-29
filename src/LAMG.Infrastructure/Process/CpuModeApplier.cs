using System.Diagnostics;

using LAMG.Application.Abstractions.System;
using LAMG.Common;
using LAMG.Domain.Enums;

using Microsoft.Extensions.Logging;

namespace LAMG.Infrastructure.Process;

/// <inheritdoc cref="ICpuModeApplier"/>
public sealed class CpuModeApplier : ICpuModeApplier
{
    private readonly ILogger<CpuModeApplier> _logger;

    public CpuModeApplier(ILogger<CpuModeApplier> logger)
    {
        _logger = Guard.NotNull(logger);
    }

    public void Apply(System.Diagnostics.Process process, CpuMode mode)
    {
        ArgumentNullException.ThrowIfNull(process);

        ProcessPriorityClass priority = mode switch
        {
            CpuMode.Eco => ProcessPriorityClass.BelowNormal,
            CpuMode.Normal => ProcessPriorityClass.Normal,
            CpuMode.High => ProcessPriorityClass.AboveNormal,
            _ => ProcessPriorityClass.Normal,
        };

        // Setting priority is best-effort: the process may have already
        // exited, the OS may deny the change, or we may not be running
        // on a platform that honours priority classes. Either way the
        // job must continue.
        try
        {
            if (!process.HasExited)
            {
                process.PriorityClass = priority;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(
                ex,
                "Could not apply CPU priority {Priority} to process; continuing.",
                priority);
        }
    }

    public int GetFFmpegThreadCount(CpuMode mode) => mode switch
    {
        CpuMode.Eco => 1,
        CpuMode.Normal => Math.Max(2, Environment.ProcessorCount / 2),
        CpuMode.High => 0, // 0 = let ffmpeg choose
        _ => Math.Max(2, Environment.ProcessorCount / 2),
    };

    public int GetConcurrentRenderCount(CpuMode mode) => mode switch
    {
        CpuMode.Eco => 1,
        CpuMode.Normal => 1,
        CpuMode.High => Math.Clamp(Environment.ProcessorCount / 4, 1, 2),
        _ => 1,
    };

    public int GetAnalysisParallelism(CpuMode mode) => mode switch
    {
        CpuMode.Eco => 1,
        CpuMode.Normal => Math.Max(2, Environment.ProcessorCount / 2),
        CpuMode.High => Math.Max(2, Environment.ProcessorCount),
        _ => Math.Max(2, Environment.ProcessorCount / 2),
    };
}
