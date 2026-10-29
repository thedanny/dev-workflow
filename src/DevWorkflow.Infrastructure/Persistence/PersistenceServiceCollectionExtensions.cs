using DevWorkflow.Application.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DevWorkflow.Infrastructure.Persistence;

public static class PersistenceServiceCollectionExtensions
{
    public const string ConnectionStringName = "DevWorkflow";

    public static IServiceCollection AddDevWorkflowPersistence(this IServiceCollection services, string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddDbContext<DevWorkflowDbContext>(options => Configure(options, connectionString));
        services.AddScoped<IDevWorkflowDb>(provider => provider.GetRequiredService<DevWorkflowDbContext>());
        services.TryAddSingleton(TimeProvider.System);

        return services;
    }

    internal static DbContextOptionsBuilder Configure(DbContextOptionsBuilder options, string connectionString) =>
        options
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history"))
            .UseSnakeCaseNamingConvention();
}
