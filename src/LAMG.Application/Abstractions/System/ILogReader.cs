namespace LAMG.Application.Abstractions.System;

/// <summary>
/// A single in-memory log entry surfaced to the Processing screen.
/// </summary>
public sealed record LogLine(
    DateTimeOffset Timestamp,
    string Level,
    string Source,
    string Message,
    string? Exception);

/// <summary>
/// Exposes the ring buffer of recent log lines maintained by the
/// in-memory Serilog sink. The UI reads <see cref="Snapshot"/> on
/// load and subscribes to <see cref="LineWritten"/> for live updates.
/// </summary>
public interface ILogReader
{
    /// <summary>
    /// Returns a snapshot of the current ring buffer, oldest first.
    /// Safe to call from any thread.
    /// </summary>
    IReadOnlyList<LogLine> Snapshot();

    /// <summary>
    /// Raised every time a new log line is appended. Handlers must
    /// marshal to the UI thread themselves; the event fires on a
    /// background thread.
    /// </summary>
    event EventHandler<LogLine> LineWritten;
}
