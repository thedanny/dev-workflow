using DevWorkflow.Application.Services;
using DevWorkflow.Application.Signals;
using Microsoft.Extensions.DependencyInjection;

namespace DevWorkflow.Application;

public static class ApplicationServiceCollectionExtensions
{
    /// <summary>Registers the application services. Requires an <see cref="Abstractions.IDevWorkflowDb"/> registration.</summary>
    public static IServiceCollection AddDevWorkflowApplication(this IServiceCollection services)
    {
        services.AddSingleton<ISignalRedactor, SignalRedactor>();
        services.AddScoped<RepositoryLookup>();
        services.AddScoped<RepositoryService>();
        services.AddScoped<SignalService>();
        services.AddScoped<WorkItemService>();
        services.AddScoped<ExecutionService>();
        return services;
    }
}
