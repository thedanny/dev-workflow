using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DevWorkflow.Infrastructure.Persistence;

/// <summary>
/// Used only by <c>dotnet ef</c>. Reads the connection string from the
/// <c>ConnectionStrings__DevWorkflow</c> environment variable, falling back to the local
/// development database. Creating migrations does not need a running database.
/// </summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<DevWorkflowDbContext>
{
    private const string LocalDevelopment =
        "Host=localhost;Port=5432;Database=devworkflow;Username=devworkflow;Password=devworkflow";

    public DevWorkflowDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DevWorkflow") ?? LocalDevelopment;

        var options = new DbContextOptionsBuilder<DevWorkflowDbContext>();
        PersistenceServiceCollectionExtensions.Configure(options, connectionString);

        return new DevWorkflowDbContext(options.Options);
    }
}
