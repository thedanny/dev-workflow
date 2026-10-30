using DevWorkflow.Api;
using DevWorkflow.Application;
using DevWorkflow.Application.Samples;
using DevWorkflow.Application.Services;
using DevWorkflow.Infrastructure.Hosting;
using DevWorkflow.Infrastructure.Persistence;
using DevWorkflow.Infrastructure.Providers;
using DevWorkflow.Infrastructure.Security;

var (command, hostArgs) = HostCommands.Parse(args);
if (command == HostCommands.HealthCheck)
{
    return await HostCommands.CheckHealthAsync();
}

var builder = WebApplication.CreateBuilder(hostArgs);

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

switch (command)
{
    case null:
        break;
    case HostCommands.Migrate:
        await HostCommands.MigrateDatabaseAsync(app.Services);
        return 0;
    case HostCommands.Seed:
        await HostCommands.MigrateDatabaseAsync(app.Services);
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var seeded = await scope.ServiceProvider.GetRequiredService<SampleDataSeeder>().SeedAsync();
            await Console.Out.WriteLineAsync(seeded ? "Sample data seeded." : "Sample data already present; nothing to do.");
        }

        return 0;
    default:
        await Console.Error.WriteLineAsync($"Unknown command '{command}'. Use healthcheck, migrate or seed, or no command to run the API.");
        return 2;
}

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health").AllowAnonymous();
app.MapDevWorkflowApi();

await app.RunAsync();
return 0;
