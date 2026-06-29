namespace LAMG.Domain.Models;

/// <summary>
/// Top-level container that groups one job's input batches, settings
/// snapshot, planned mixes and rendered outputs.
/// </summary>
public sealed class Project
{
    public long Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Serialized snapshot of the settings active when the project
    /// started. Settings edited mid-run never affect this project.
    /// </summary>
    public string SettingsJson { get; set; } = "{}";

    /// <summary>
    /// Absolute folder path where rendered mixes are written.
    /// </summary>
    public string OutputFolder { get; set; } = string.Empty;
}
