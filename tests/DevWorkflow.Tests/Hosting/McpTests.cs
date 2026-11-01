using System.Text.Json;
using DevWorkflow.Application.Contracts;
using DevWorkflow.Application.Security;
using DevWorkflow.Application.Services;
using DevWorkflow.Domain.Repositories;
using DevWorkflow.Mcp.Tools;
using DevWorkflow.Tests.Persistence;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace DevWorkflow.Tests.Hosting;

[Collection(PostgresTestGroup.Name)]
public sealed class McpTests(PostgresFixture database) : IAsyncLifetime
{
    private static readonly string[] ExecutorToolNames =
    [
        "claim_work_item", "get_work_item", "heartbeat_execution", "list_approved_work",
        "list_my_executions", "list_repositories", "release_claim", "report_pull_request", "update_execution",
    ];

    private readonly TestHost<McpAssemblyMarker> _host = new(database);

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task Each_role_only_sees_its_own_tools()
    {
        await using var executor = await ConnectAsync(TestKeys.ExecutorA);
        await using var listener = await ConnectAsync(TestKeys.Listener);

        var executorTools = (await executor.ListToolsAsync()).Select(t => t.Name).Order(StringComparer.Ordinal);
        var listenerTools = (await listener.ListToolsAsync()).Select(t => t.Name);

        Assert.Equal(ExecutorToolNames, executorTools);
        Assert.Equal(["submit_signal"], listenerTools);
    }

    [Fact]
    public async Task A_listener_cannot_call_an_executor_tool()
    {
        await using var listener = await ConnectAsync(TestKeys.Listener);

        var outcome = await Record.ExceptionAsync(async () =>
        {
            var result = await listener.CallToolAsync("claim_work_item", new Dictionary<string, object?>
            {
                ["work_item_id"] = Guid.NewGuid(),
                ["executor_id"] = "agent-x",
                ["repository"] = "anything",
            });
            Assert.True(result.IsError, "An unauthorized tool call must not succeed.");
        });

        Assert.True(outcome is null or McpException, $"Unexpected failure: {outcome}");
    }

    [Fact]
    public async Task An_agent_can_list_and_claim_work_and_gets_structured_errors()
    {
        var (repository, workItemId) = await SeedApprovedWorkAsync();
        await using var executorA = await ConnectAsync(TestKeys.ExecutorA);
        await using var executorB = await ConnectAsync(TestKeys.ExecutorB);

        var queue = await executorA.CallToolAsync("list_approved_work", new Dictionary<string, object?> { ["repository"] = repository });
        var claim = await executorA.CallToolAsync("claim_work_item", Claim(workItemId, "agent-a", repository));
        var contested = await executorB.CallToolAsync("claim_work_item", Claim(workItemId, "agent-b", repository));

        Assert.NotEqual(true, queue.IsError);
        Assert.Equal(workItemId, Structured(queue).GetProperty("items")[0].GetProperty("id").GetGuid());
        Assert.NotEqual(true, claim.IsError);
        Assert.Equal("agent-a", Structured(claim).GetProperty("execution").GetProperty("executor_id").GetString());
        Assert.Equal("claimed", Structured(claim).GetProperty("work_item").GetProperty("status").GetString());
        Assert.True(contested.IsError);
        Assert.Equal("already_claimed", Structured(contested).GetProperty("error").GetProperty("code").GetString());
        Assert.Equal("conflict", Structured(contested).GetProperty("error").GetProperty("kind").GetString());
    }

    [Fact]
    public async Task An_agent_works_a_task_from_claim_to_completion_through_mcp_tools()
    {
        var (repository, workItemId) = await SeedApprovedWorkAsync();
        await using var agent = await ConnectAsync(TestKeys.ExecutorA);
        var remote = $"git@github.com:example-org/{repository}.git";

        var before = await agent.CallToolAsync("list_my_executions", new Dictionary<string, object?> { ["repository"] = remote });
        var claim = Structured(await agent.CallToolAsync("claim_work_item", Claim(workItemId, "agent-a", remote)));
        var executionId = claim.GetProperty("execution").GetProperty("id").GetGuid();
        var held = await agent.CallToolAsync("list_my_executions", new Dictionary<string, object?> { ["repository"] = remote });
        var steps = new[]
        {
            await agent.CallToolAsync("heartbeat_execution", new Dictionary<string, object?> { ["execution_id"] = executionId, ["lease_seconds"] = 1800 }),
            await agent.CallToolAsync("update_execution", new Dictionary<string, object?>
            {
                ["execution_id"] = executionId,
                ["state"] = "in_progress",
                ["summary"] = "Reproduced; fixing",
                ["branch"] = "dw/fix-export",
                ["worktree_path"] = "../worktrees/fix-export",
            }),
            await agent.CallToolAsync("report_pull_request", new Dictionary<string, object?>
            {
                ["execution_id"] = executionId,
                ["url"] = $"https://github.com/example-org/{repository}/pull/12",
                ["branch"] = "dw/fix-export",
                ["checks"] = "build ok; tests passed",
            }),
        };
        var completed = await agent.CallToolAsync("update_execution", new Dictionary<string, object?>
        {
            ["execution_id"] = executionId,
            ["state"] = "completed",
            ["summary"] = "PR opened for review",
            ["evidence"] = new[] { $"https://github.com/example-org/{repository}/pull/12" },
        });
        var after = await agent.CallToolAsync("list_my_executions", new Dictionary<string, object?> { ["repository"] = remote });

        Assert.Equal(0, Structured(before).GetProperty("items").GetArrayLength());
        Assert.Equal(executionId, Structured(held).GetProperty("items")[0].GetProperty("execution").GetProperty("id").GetGuid());
        Assert.All(steps, s => Assert.NotEqual(true, s.IsError));
        Assert.Equal("pr_ready", Structured(steps[2]).GetProperty("work_item").GetProperty("status").GetString());
        Assert.Equal("completed", Structured(completed).GetProperty("work_item").GetProperty("status").GetString());
        Assert.Equal("dw/fix-export", Structured(completed).GetProperty("execution").GetProperty("branch").GetString());
        Assert.Equal(0, Structured(after).GetProperty("items").GetArrayLength());
    }

    private async Task<McpClient> ConnectAsync(string key)
    {
        var http = _host.CreateClient();
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Endpoint = new Uri(http.BaseAddress!, "/mcp"),
                TransportMode = HttpTransportMode.StreamableHttp,
                AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = $"Bearer {key}" },
            },
            http,
            loggerFactory: null,
            ownsHttpClient: true);
        return await McpClient.CreateAsync(transport);
    }

    private async Task<(string Repository, Guid WorkItemId)> SeedApprovedWorkAsync()
    {
        await using var scope = _host.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var approver = new Caller("seed-approver", CallerRole.Approver);
        var evaluator = new Caller("seed-evaluator", CallerRole.Evaluator);
        var key = $"sample-{Guid.NewGuid():N}"[..20];

        await services.GetRequiredService<RepositoryService>().RegisterAsync(
            approver, new RegisterRepositoryRequest(key, RepositoryProvider.GitHub, $"git@github.com:example-org/{key}.git"));
        var item = await services.GetRequiredService<WorkItemService>().ProposeAsync(evaluator, new ProposeWorkItemRequest("Fix export", Repository: key));
        await services.GetRequiredService<WorkItemService>().ApproveAsync(approver, item.Id, "Confirmed defect", null);

        return (key, item.Id);
    }

    private static Dictionary<string, object?> Claim(Guid workItemId, string executorId, string repository) => new()
    {
        ["work_item_id"] = workItemId,
        ["executor_id"] = executorId,
        ["repository"] = repository,
    };

    private static JsonElement Structured(CallToolResult result)
    {
        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        using var document = JsonDocument.Parse(text);
        var root = document.RootElement.Clone();
        return root.ValueKind == JsonValueKind.Array
            ? JsonSerializer.SerializeToElement(new { items = root })
            : root;
    }
}
