using DevWorkflow.Api;
using DevWorkflow.Application;
using DevWorkflow.Application.Services;
using DevWorkflow.Infrastructure.Persistence;
using DevWorkflow.Infrastructure.Providers;
using DevWorkflow.Infrastructure.Security;

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
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<WorkflowExceptionHandler>();
builder.Services.ConfigureHttpJsonOptions(options => WorkflowJson.Configure(options.SerializerOptions));
builder.Services.Configure<LeaseExpiryOptions>(builder.Configuration.GetSection(LeaseExpiryOptions.SectionName));
builder.Services.AddHostedService<LeaseExpiryWorker>();
builder.Services.Configure<PullRequestSyncOptions>(builder.Configuration.GetSection(PullRequestSyncOptions.SectionName));
builder.Services.AddHostedService<PullRequestSyncWorker>();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health").AllowAnonymous();
app.MapDevWorkflowApi();

app.Run();
