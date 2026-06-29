using LAMG.Application.Settings;

namespace LAMG.Application.Jobs;

/// <summary>
/// Inputs for a new job. The orchestrator snapshots the supplied
/// settings into the project record so subsequent setting edits do
/// not influence this run.
/// </summary>
public sealed record JobRequest(
    string ProjectName,
    IReadOnlyList<string> BatchFolders,
    AppSettings Settings);
