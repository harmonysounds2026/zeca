using System.Runtime.InteropServices;
using System.Runtime.Versioning;

using LAMG.Application.Abstractions.System;
using LAMG.Common;

using Microsoft.Extensions.Logging;

namespace LAMG.Infrastructure.Power;

/// <inheritdoc cref="ISystemAwakeKeeper"/>
public sealed class SystemAwakeKeeper : ISystemAwakeKeeper
{
    private readonly ILogger<SystemAwakeKeeper> _logger;
    private readonly object _lock = new();
    private int _refCount;

    public SystemAwakeKeeper(ILogger<SystemAwakeKeeper> logger)
    {
        _logger = Guard.NotNull(logger);
    }

    public IDisposable KeepAwake()
    {
        lock (_lock)
        {
            if (_refCount == 0)
            {
                TrySetAwakeState(continuous: true);
            }

            _refCount++;
        }

        return new DisposableAction(Release);
    }

    private void Release()
    {
        lock (_lock)
        {
            if (_refCount == 0)
            {
                return;
            }

            _refCount--;
            if (_refCount == 0)
            {
                TrySetAwakeState(continuous: false);
            }
        }
    }

    private void TrySetAwakeState(bool continuous)
    {
        if (!OperatingSystem.IsWindows())
        {
            // No-op on non-Windows: WPF runs on Windows only, but the
            // assembly is multi-platform-buildable so the type works
            // anywhere.
            return;
        }

        try
        {
            uint flags = continuous
                ? EXECUTION_STATE.ES_CONTINUOUS | EXECUTION_STATE.ES_SYSTEM_REQUIRED | EXECUTION_STATE.ES_AWAYMODE_REQUIRED
                : EXECUTION_STATE.ES_CONTINUOUS;
            _ = SetThreadExecutionState(flags);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SetThreadExecutionState failed; system may still sleep.");
        }
    }

    [SupportedOSPlatform("windows")]
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint SetThreadExecutionState(uint esFlags);

    private static class EXECUTION_STATE
    {
        public const uint ES_CONTINUOUS = 0x80000000;
        public const uint ES_SYSTEM_REQUIRED = 0x00000001;
        public const uint ES_AWAYMODE_REQUIRED = 0x00000040;
    }
}
