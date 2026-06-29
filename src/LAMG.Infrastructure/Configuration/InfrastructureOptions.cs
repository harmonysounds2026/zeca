namespace LAMG.Infrastructure.Configuration;

/// <summary>
/// Filesystem layout the Infrastructure layer needs. Populated at host
/// build time by the UI startup code from <see cref="LamgPaths"/>.
/// </summary>
public sealed record InfrastructureOptions
{
    /// <summary>Absolute path to the SQLite database file.</summary>
    public string DatabasePath { get; init; } = string.Empty;

    /// <summary>Folder where rolling log files are written.</summary>
    public string LogsFolder { get; init; } = string.Empty;

    /// <summary>Default output folder for rendered mixes.</summary>
    public string DefaultOutputFolder { get; init; } = string.Empty;

    /// <summary>Folder containing the bundled <c>ffmpeg</c> / <c>ffprobe</c>.</summary>
    public string FFmpegBundledFolder { get; init; } = string.Empty;

    /// <summary>
    /// Number of log entries kept in the in-memory ring buffer for the
    /// Processing screen. Older entries are evicted; the rolling file
    /// sink retains the full history.
    /// </summary>
    public int InMemoryLogCapacity { get; init; } = 5000;

    /// <summary>
    /// Resumable-job age threshold. Jobs in <c>Running</c> / <c>Paused</c>
    /// status with a heartbeat older than this are considered crashed.
    /// Kept short (30 s) so a quick restart after a crash surfaces the
    /// resume prompt right away, while still tolerating the
    /// 5 s heartbeat interval plus normal jitter.
    /// </summary>
    public TimeSpan StaleHeartbeatThreshold { get; init; } = TimeSpan.FromSeconds(30);
}
