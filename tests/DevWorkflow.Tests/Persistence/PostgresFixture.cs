using DevWorkflow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace DevWorkflow.Tests.Persistence;

/// <summary>
/// Starts one disposable PostgreSQL container for the test run and applies all migrations.
/// Tests share the database and stay independent by using unique data.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        await using var context = CreateContext();
        await context.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    public DevWorkflowDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<DevWorkflowDbContext>();
        PersistenceServiceCollectionExtensions.Configure(options, ConnectionString);
        return new DevWorkflowDbContext(options.Options);
    }
}

[CollectionDefinition(Name)]
public sealed class PostgresTestGroup : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
