namespace LAMG.Application.UseCases.ImportBatches;

/// <summary>
/// Inputs for the <see cref="ImportBatchesUseCase"/>.
/// </summary>
public sealed record ImportBatchesRequest(
    long ProjectId,
    IReadOnlyList<string> Folders,
    bool Recursive);
