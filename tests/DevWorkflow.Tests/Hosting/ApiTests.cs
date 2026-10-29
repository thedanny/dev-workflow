using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using DevWorkflow.Api;
using DevWorkflow.Tests.Persistence;

namespace DevWorkflow.Tests.Hosting;

[Collection(PostgresTestGroup.Name)]
public sealed class ApiTests(PostgresFixture database) : IAsyncLifetime
{
    private readonly TestHost<ApiAssemblyMarker> _host = new(database);

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task Health_is_anonymous_but_everything_else_needs_a_key()
    {
        using var client = _host.CreateClient();

        var health = await client.GetAsync(new Uri("/health", UriKind.Relative));
        var queue = await client.GetAsync(new Uri("/api/v1/queue?repository=x", UriKind.Relative));
        using var badKey = Client("not-a-real-key");
        var wrongKey = await badKey.GetAsync(new Uri("/api/v1/queue?repository=x", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, queue.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, wrongKey.StatusCode);
    }

    [Fact]
    public async Task A_listener_can_submit_signals_idempotently_but_cannot_read_the_queue()
    {
        using var listener = Client(TestKeys.Listener);
        var signal = new { source_type = "email", source_reference = "msg-1", idempotency_key = $"msg-{Guid.NewGuid():N}", payload = "The export button is broken." };

        var first = await listener.PostAsJsonAsync("/api/v1/signals", signal);
        var second = await listener.PostAsJsonAsync("/api/v1/signals", signal);
        var queue = await listener.GetAsync(new Uri("/api/v1/queue?repository=x", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal((await Json(first))["signal"]!["id"]!.GetValue<Guid>(), (await Json(second))["signal"]!["id"]!.GetValue<Guid>());
        Assert.False((await Json(second))["created"]!.GetValue<bool>());
        Assert.Equal(HttpStatusCode.Forbidden, queue.StatusCode);
    }

    [Fact]
    public async Task Claims_over_http_return_structured_conflict_and_mismatch_errors()
    {
        using var approver = Client(TestKeys.Approver);
        using var evaluator = Client(TestKeys.Evaluator);
        using var executorA = Client(TestKeys.ExecutorA);
        using var executorB = Client(TestKeys.ExecutorB);

        var repoA = await RegisterAsync(approver);
        var repoB = await RegisterAsync(approver);
        var proposed = await Json(await evaluator.PostAsJsonAsync("/api/v1/work-items", new { title = "Fix export", repository = repoA, priority = "high" }));
        var id = proposed["id"]!.GetValue<Guid>();
        var approve = await approver.PostAsJsonAsync($"/api/v1/work-items/{id}/approve", new { reason = "Confirmed defect" });

        var queue = await Json(await executorA.GetAsync(new Uri($"/api/v1/queue?repository={repoA}", UriKind.Relative)));
        var claim = await executorA.PostAsJsonAsync($"/api/v1/work-items/{id}/claim", new { executor_id = "agent-a", repository = repoA });
        var contested = await executorB.PostAsJsonAsync($"/api/v1/work-items/{id}/claim", new { executor_id = "agent-b", repository = repoA });
        var mismatched = await executorB.PostAsJsonAsync($"/api/v1/work-items/{id}/claim", new { executor_id = "agent-b", repository = repoB });
        var badLease = await executorA.PostAsJsonAsync($"/api/v1/executions/{(await Json(claim))["execution"]!["id"]}/heartbeat", new { lease_seconds = 5 });

        Assert.Equal(HttpStatusCode.OK, approve.StatusCode);
        Assert.Equal("approved", (await Json(approve))["status"]!.GetValue<string>());
        Assert.Equal(id, Assert.Single(queue.AsArray())!["id"]!.GetValue<Guid>());
        Assert.Equal(HttpStatusCode.OK, claim.StatusCode);
        Assert.Equal("agent-a", (await Json(claim))["execution"]!["executor_id"]!.GetValue<string>());
        await AssertProblemAsync(contested, HttpStatusCode.Conflict, "already_claimed");
        await AssertProblemAsync(mismatched, HttpStatusCode.Forbidden, "repository_mismatch");
        await AssertProblemAsync(badLease, HttpStatusCode.BadRequest, "invalid_lease");
    }

    private HttpClient Client(string key)
    {
        var client = _host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);
        return client;
    }

    private static async Task<string> RegisterAsync(HttpClient approver)
    {
        var key = $"sample-{Guid.NewGuid():N}"[..20];
        var response = await approver.PostAsJsonAsync("/api/v1/repositories", new
        {
            key,
            provider = "github",
            remote_url = $"https://github.com/example-org/{key}.git",
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return key;
    }

    private static async Task<JsonNode> Json(HttpResponseMessage response) =>
        JsonNode.Parse(await response.Content.ReadAsStringAsync()) ?? throw new JsonException("Empty response body.");

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(code, (await Json(response))["code"]!.GetValue<string>());
    }
}
