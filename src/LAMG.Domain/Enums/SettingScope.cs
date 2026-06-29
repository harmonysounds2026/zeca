namespace LAMG.Domain.Enums;

/// <summary>
/// Persistence scope of a setting entry.
/// </summary>
public enum SettingScope
{
    /// <summary>Global, per-user setting (default).</summary>
    User = 1,

    /// <summary>Snapshot scoped to a specific project run.</summary>
    Project = 2,
}
