namespace LAMG.Domain.Models;

/// <summary>
/// Persisted log row. Only events at Warning level or above are written
/// to the database; everything else goes to the rolling file sink and
/// the in-memory ring buffer used by the Processing screen.
/// </summary>
public sealed class LogEvent
{
    public long Id { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public string Level { get; set; } = string.Empty;

    public string Source { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public string? Exception { get; set; }

    public string? ContextJson { get; set; }
}
