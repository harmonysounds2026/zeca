using Dapper;

using LAMG.Domain.Enums;
using LAMG.Infrastructure.Persistence.TypeHandlers;

namespace LAMG.Infrastructure.Persistence;

/// <summary>
/// One-shot Dapper configuration: register custom type handlers,
/// switch column matching to be case-insensitive, etc. Must be called
/// exactly once, before any repository runs.
/// </summary>
public static class DapperConfiguration
{
    private static int _configured;

    public static void EnsureConfigured()
    {
        if (Interlocked.Exchange(ref _configured, 1) == 1)
        {
            return;
        }

        SqlMapper.AddTypeHandler(new DateTimeOffsetUnixMsTypeHandler());
        SqlMapper.AddTypeHandler(new NullableDateTimeOffsetUnixMsTypeHandler());

        // Domain enums are stored as TEXT (their member name).
        SqlMapper.AddTypeHandler(new EnumStringTypeHandler<AudioFormat>());
        SqlMapper.AddTypeHandler(new EnumStringTypeHandler<MixMode>());
        SqlMapper.AddTypeHandler(new EnumStringTypeHandler<OutputFormat>());
        SqlMapper.AddTypeHandler(new EnumStringTypeHandler<CpuMode>());
        SqlMapper.AddTypeHandler(new EnumStringTypeHandler<JobType>());
        SqlMapper.AddTypeHandler(new EnumStringTypeHandler<JobStatus>());
        SqlMapper.AddTypeHandler(new EnumStringTypeHandler<JobStage>());
        SqlMapper.AddTypeHandler(new EnumStringTypeHandler<TrackStatus>());
        SqlMapper.AddTypeHandler(new EnumStringTypeHandler<MixStatus>());
        SqlMapper.AddTypeHandler(new EnumStringTypeHandler<DuplicateResolution>());
        SqlMapper.AddTypeHandler(new EnumStringTypeHandler<SettingScope>());

        // Underscore-cased columns map to PascalCase properties cleanly.
        DefaultTypeMap.MatchNamesWithUnderscores = true;
    }
}
