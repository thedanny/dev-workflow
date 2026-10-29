using DevWorkflow.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString(PersistenceServiceCollectionExtensions.ConnectionStringName)
    ?? throw new InvalidOperationException(
        $"Connection string '{PersistenceServiceCollectionExtensions.ConnectionStringName}' is not configured. " +
        "Set ConnectionStrings__DevWorkflow.");

builder.Services.AddDevWorkflowPersistence(connectionString);
builder.Services.AddHealthChecks().AddDbContextCheck<DevWorkflowDbContext>("database");

var app = builder.Build();

app.MapHealthChecks("/health");

app.Run();
