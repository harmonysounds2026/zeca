using LAMG.Domain.Enums;

namespace LAMG.Domain.Models;

/// <summary>
/// A single typed setting persisted in the database. Type coercion
/// happens in <c>ISettingsService</c>; here it is always a string.
/// </summary>
public sealed class SettingEntry
{
    public string Key { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;

    public SettingScope Scope { get; set; } = SettingScope.User;

    /// <summary>Null for <see cref="SettingScope.User"/> entries.</summary>
    public long? ProjectId { get; set; }
}
