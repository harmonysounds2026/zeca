namespace LAMG.Infrastructure.Configuration;

/// <summary>
/// Filesystem layout the Infrastructure layer needs. Populated at host
/// build time by the UI startup code from <see cref="LamgPaths"/>.
/// </summary>
public sealed record InfrastructureOptions
{
    // Properties are 'set' (not 'init') because the
    // Microsoft.Extensions.Options pattern hydrates them from inside a
    // configuration lambda after the instance has been constructed
    // (see AddLamgInfrastructure(IServiceCollection)).

    /// <summary>Absolute path to the SQLite database file.</summary>
    public string DatabasePath { get; set; } = string.Empty;

    /// <summary>Folder where rolling log files are written.</summary>
    public string LogsFolder { get; set; } = string.Empty;

    /// <summary>Default output folder for rendered mixes.</summary>
    public string DefaultOutputFolder { get; set; } = string.Empty;

    /// <summary>Folder containing the bundled <c>ffmpeg</c> / <c>ffprobe</c>.</summary>
    public string FFmpegBundledFolder { get; set; } = string.Empty;

    /// <summary>
    /// Number of log entries kept in the in-memory ring buffer for the
    /// Processing screen. Older entries are evicted; the rolling file
    /// sink retains the full history.
    /// </summary>
    public int InMemoryLogCapacity { get; set; } = 5000;

    /// <summary>
    /// Resumable-job age threshold. Jobs in <c>Running</c> / <c>Paused</c>
    /// status with a heartbeat older than this are considered crashed.
    /// Kept short (30 s) so a quick restart after a crash surfaces the
    /// resume prompt right away, while still tolerating the
    /// 5 s heartbeat interval plus normal jitter.
    /// </summary>
    public TimeSpan StaleHeartbeatThreshold { get; set; } = TimeSpan.FromSeconds(30);
}
