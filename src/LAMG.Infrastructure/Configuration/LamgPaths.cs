namespace LAMG.Infrastructure.Configuration;

/// <summary>
/// Resolves the standard on-disk locations for the application data,
/// logs, default output and bundled tools. Used by the host at startup
/// to build <see cref="InfrastructureOptions"/>.
/// </summary>
public static class LamgPaths
{
    private const string ApplicationFolderName = "Longform Audio Mix Generator";
    private const string DatabaseFileName = "lamg.db";
    private const string LogsFolderName = "Logs";
    private const string DefaultOutputFolderName = "Mixes";
    private const string BundledToolsRelativePath = "tools/ffmpeg";

    /// <summary>
    /// Root for per-user application data. Resolves to
    /// <c>%LOCALAPPDATA%\Longform Audio Mix Generator</c> on Windows.
    /// </summary>
    public static string GetUserDataFolder()
    {
        string root = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolderOption.Create);

        return Path.Combine(root, ApplicationFolderName);
    }

    public static string GetDatabasePath()
        => Path.Combine(GetUserDataFolder(), DatabaseFileName);

    public static string GetLogsFolder()
        => Path.Combine(GetUserDataFolder(), LogsFolderName);

    public static string GetDefaultOutputFolder()
        => Path.Combine(GetUserDataFolder(), DefaultOutputFolderName);

    /// <summary>
    /// Folder next to the application executable that contains the
    /// bundled ffmpeg/ffprobe binaries.
    /// </summary>
    public static string GetBundledFFmpegFolder()
        => Path.Combine(AppContext.BaseDirectory, BundledToolsRelativePath);

    /// <summary>
    /// Convenience: returns an <see cref="InfrastructureOptions"/>
    /// populated with the default resolved paths.
    /// </summary>
    public static InfrastructureOptions BuildDefaultOptions() => new()
    {
        DatabasePath = GetDatabasePath(),
        LogsFolder = GetLogsFolder(),
        DefaultOutputFolder = GetDefaultOutputFolder(),
        FFmpegBundledFolder = GetBundledFFmpegFolder(),
    };
}
