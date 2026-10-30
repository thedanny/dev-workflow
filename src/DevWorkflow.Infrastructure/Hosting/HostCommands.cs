using DevWorkflow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DevWorkflow.Infrastructure.Hosting;

/// <summary>
/// One-shot commands the host executables accept as their first argument, so containers need
/// no extra tools: <c>healthcheck</c>, plus <c>migrate</c> and <c>seed</c> on the API.
/// </summary>
public static class HostCommands
{
    public const string HealthCheck = "healthcheck";
    public const string Migrate = "migrate";
    public const string Seed = "seed";

    /// <summary>Splits a leading command from the arguments that configure the host.</summary>
    public static (string? Command, string[] HostArgs) Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        return args is [var first, ..] && !first.StartsWith('-') ? (first, args[1..]) : (null, args);
    }

    /// <summary>Returns 0 when this machine's host answers <c>/health</c> successfully, otherwise 1.</summary>
    public static async Task<int> CheckHealthAsync()
    {
        var ports = Environment.GetEnvironmentVariable("ASPNETCORE_HTTP_PORTS") ?? "8080";
        var port = ports.Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)[0];
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        try
        {
            using var response = await http.GetAsync(new Uri($"http://localhost:{port}/health"));
            return response.IsSuccessStatusCode ? 0 : 1;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            return 1;
        }
    }

    /// <summary>Applies pending migrations. Safe to repeat; concurrent runs are serialized by a database lock.</summary>
    public static async Task MigrateDatabaseAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<DevWorkflowDbContext>().Database.MigrateAsync(cancellationToken);
    }
}
