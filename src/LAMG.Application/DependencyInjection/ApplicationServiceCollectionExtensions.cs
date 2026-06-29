using LAMG.Application.Abstractions;
using LAMG.Application.Abstractions.Planning;
using LAMG.Application.Jobs;
using LAMG.Application.Pipelines;
using LAMG.Application.Planning;
using LAMG.Application.UseCases.AnalyzeTracks;
using LAMG.Application.UseCases.DetectDuplicates;
using LAMG.Application.UseCases.ImportBatches;
using LAMG.Application.UseCases.PlanMixes;
using LAMG.Application.UseCases.RenderMix;

// Standard pattern: extension methods live in the DI namespace so
// callers do not need to import LAMG.Application explicitly.
// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// DI registrations for the LAMG application layer (pure logic; no
/// infrastructure). The host should also call
/// <c>AddLamgInfrastructure</c> from the Infrastructure project.
/// </summary>
public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddLamgApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Planners (pure)
        services.AddSingleton<IUniqueModePlanner, UniqueModePlanner>();
        services.AddSingleton<IReuseModePlanner, ReuseModePlanner>();

        // Use cases (transient: cheap to construct, stateless)
        services.AddTransient<ImportBatchesUseCase>();
        services.AddTransient<AnalyzeTracksUseCase>();
        services.AddTransient<DetectDuplicatesUseCase>();
        services.AddTransient<PlanMixesUseCase>();
        services.AddTransient<RenderMixUseCase>();

        // Pipelines
        services.AddTransient<RenderPipeline>();

        // Orchestrator (single instance so it can host the LifecycleChanged event)
        services.AddSingleton<IJobOrchestrator, JobOrchestrator>();

        return services;
    }
}
