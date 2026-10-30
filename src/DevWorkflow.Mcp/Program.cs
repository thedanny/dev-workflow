using DevWorkflow.Application;
using DevWorkflow.Application.Services;
using DevWorkflow.Infrastructure.Persistence;
using DevWorkflow.Infrastructure.Providers;
using DevWorkflow.Infrastructure.Security;
using DevWorkflow.Mcp.Tools;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString(PersistenceServiceCollectionExtensions.ConnectionStringName)
    ?? throw new InvalidOperationException(
        $"Connection string '{PersistenceServiceCollectionExtensions.ConnectionStringName}' is not configured. " +
        "Set ConnectionStrings__DevWorkflow.");

builder.Services.AddDevWorkflowPersistence(connectionString);
builder.Services.AddDevWorkflowApplication();
builder.Services.AddDevWorkflowAuthentication(builder.Configuration);
builder.Services.AddDevWorkflowRepositoryProviders(builder.Configuration);
builder.Services.AddHealthChecks().AddDbContextCheck<DevWorkflowDbContext>("database");
builder.Services.AddHttpContextAccessor();

builder.Services.AddMcpServer()
    .WithHttpTransport()
    .AddAuthorizationFilters()
    .WithTools<SignalTools>(WorkflowJson.Options)
    .WithTools<WorkItemTools>(WorkflowJson.Options)
    .WithTools<ExecutorTools>(WorkflowJson.Options);

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health").AllowAnonymous();
app.MapMcp("/mcp");

app.Run();
