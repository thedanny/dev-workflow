using DevWorkflow.Application.Security;
using DevWorkflow.Infrastructure.Security;
using DevWorkflow.Tests.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace DevWorkflow.Tests.Hosting;

/// <summary>Fictional API keys, one per role, configured on both test hosts by hash only.</summary>
internal static class TestKeys
{
    public static readonly (string Name, CallerRole Role, string Key)[] All =
    [
        ("test-listener", CallerRole.Listener, "listener-key-0000000000000000"),
        ("test-evaluator", CallerRole.Evaluator, "evaluator-key-000000000000000"),
        ("test-approver", CallerRole.Approver, "approver-key-0000000000000000"),
        ("test-executor-a", CallerRole.Executor, "executor-a-key-00000000000000"),
        ("test-executor-b", CallerRole.Executor, "executor-b-key-00000000000000"),
    ];

    public static string Listener => All[0].Key;

    public static string Evaluator => All[1].Key;

    public static string Approver => All[2].Key;

    public static string ExecutorA => All[3].Key;

    public static string ExecutorB => All[4].Key;

    public static void Configure(IWebHostBuilder builder, PostgresFixture database)
    {
        // A dedicated environment, so local Development settings never leak into tests.
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:DevWorkflow", database.ConnectionString);
        builder.UseSetting("Leases:SweepIntervalSeconds", "0");
        builder.UseSetting("PullRequests:SyncIntervalSeconds", "0");
        for (var i = 0; i < All.Length; i++)
        {
            builder.UseSetting($"Authentication:ApiKeys:{i}:Name", All[i].Name);
            builder.UseSetting($"Authentication:ApiKeys:{i}:Role", All[i].Role.ToString());
            builder.UseSetting($"Authentication:ApiKeys:{i}:KeyHash", ApiKeys.Hash(All[i].Key));
        }
    }
}

internal sealed class TestHost<TEntryPoint>(PostgresFixture database) : WebApplicationFactory<TEntryPoint>
    where TEntryPoint : class
{
    protected override void ConfigureWebHost(IWebHostBuilder builder) => TestKeys.Configure(builder, database);
}
