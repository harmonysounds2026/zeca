namespace LAMG.Domain.Enums;

/// <summary>
/// CPU usage profile applied to child ffmpeg processes and the
/// internal pipeline parallelism.
/// </summary>
public enum CpuMode
{
    Eco = 1,
    Normal = 2,
    High = 3,
}
