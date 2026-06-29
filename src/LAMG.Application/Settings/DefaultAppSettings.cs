namespace LAMG.Application.Settings;

/// <summary>
/// Holds the factory-default <see cref="AppSettings"/>. The defaults
/// reflect the v1 design: 90 min target, mp3 192 kbps, 1 s crossfade,
/// -14 LUFS normalization, Normal CPU mode.
/// </summary>
public static class DefaultAppSettings
{
    public static AppSettings Value { get; } = new AppSettings();
}
