using System.Net;
using DevWorkflow.Application;
using DevWorkflow.Application.Errors;
using DevWorkflow.Application.Providers;
using DevWorkflow.Domain.PullRequests;
using DevWorkflow.Domain.Repositories;
using DevWorkflow.Infrastructure.Providers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DevWorkflow.Tests.Providers;

public sealed class RepositoryClientTests : IDisposable
{
    private const string Sha = "a1b2c3d4e5f60718293a4b5c6d7e8f9012345678";

    private readonly FakeGitHub _github = new();
    private readonly FakeBitbucket _bitbucket = new();
    private ServiceProvider? _services;

    public void Dispose()
    {
        _services?.Dispose();
        _github.Dispose();
        _bitbucket.Dispose();
    }

    [Fact]
    public async Task GitHub_client_reads_the_repository_and_creates_a_branch()
    {
        _github.AddRepository("example-org", "storefront", "trunk");
        var client = Client(RepositoryProvider.GitHub, "git@github.com:example-org/storefront.git");

        var repository = await client.GetRepositoryAsync();
        var missing = await client.GetBranchAsync("fix/checkout-button");
        var created = await client.CreateBranchAsync("fix/checkout-button", Sha);
        var found = await client.GetBranchAsync("fix/checkout-button");

        Assert.Equal(new RemoteRepository("example-org/storefront", "trunk", new Uri("https://github.com/example-org/storefront")), repository);
        Assert.Null(missing);
        Assert.Equal(new RemoteBranch("fix/checkout-button", Sha), created);
        Assert.Equal(created, found);
        Assert.All(_github.Requests, r => Assert.Equal("Bearer github-test-token", r.Authorization));
        Assert.All(_github.Requests, r => Assert.Equal("api.github.com", r.Uri.Host));
    }

    [Fact]
    public async Task GitHub_client_summarizes_checks_and_each_reviewers_latest_decision()
    {
        var repo = _github.AddRepository("example-org", "storefront");
        var pull = _github.AddPullRequest(repo, "fix/checkout-button");
        pull.CheckRuns.AddRange([("completed", "success"), ("completed", "failure"), ("in_progress", null), ("completed", "skipped")]);
        pull.Statuses.Add("success");
        pull.Reviews.AddRange([
            ("reviewer-a", "CHANGES_REQUESTED"), ("reviewer-a", "APPROVED"),
            ("reviewer-b", "COMMENTED"),
            ("reviewer-c", "APPROVED"), ("reviewer-c", "DISMISSED"),
            ("reviewer-d", "CHANGES_REQUESTED"),
        ]);
        var client = Client(RepositoryProvider.GitHub, "https://github.com/example-org/storefront");

        var open = await client.GetPullRequestAsync("1");
        pull.State = "merged";
        var merged = await client.GetPullRequestAsync("1");

        Assert.NotNull(open);
        Assert.Equal(PullRequestState.Open, open.State);
        Assert.Equal("fix/checkout-button", open.HeadBranch);
        Assert.Equal("main", open.BaseBranch);
        Assert.Equal(Sha, open.HeadCommit);
        Assert.Equal(new Uri("https://github.com/example-org/storefront/pull/1"), open.WebUrl);
        Assert.Equal("2 passed, 1 failed, 1 pending, 1 skipped", open.ChecksSummary);
        Assert.Equal("1 approved, 1 changes requested", open.ReviewSummary);
        Assert.Equal(PullRequestState.Merged, merged!.State);
        Assert.Null(await client.GetPullRequestAsync("99"));
    }

    [Fact]
    public async Task GitHub_client_opens_a_pull_request()
    {
        var repo = _github.AddRepository("example-org", "storefront");
        repo.Branches["fix/checkout-button"] = Sha;
        var client = Client(RepositoryProvider.GitHub, "git@github.com:example-org/storefront.git");

        var opened = await client.OpenPullRequestAsync(new NewPullRequest("Fix checkout button", "fix/checkout-button", "main", "Closes PROJ-123"));
        var rejected = await Assert.ThrowsAsync<WorkflowException>(() =>
            client.OpenPullRequestAsync(new NewPullRequest("No branch", "missing-branch", "main")));

        Assert.Equal("1", opened.ExternalId);
        Assert.Equal(PullRequestState.Open, opened.State);
        Assert.Equal("fix/checkout-button", opened.HeadBranch);
        Assert.Equal(ErrorKind.Validation, rejected.Kind);
        Assert.Equal("provider_rejected", rejected.Code);
        Assert.Contains("Validation Failed", rejected.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GitHub_enterprise_hosts_use_their_own_api_address()
    {
        _github.AddRepository("example-org", "storefront");
        var client = Client(RepositoryProvider.GitHub, "git@github.example.com:example-org/storefront.git");

        await client.GetRepositoryAsync();

        Assert.Equal(new Uri("https://github.example.com/api/v3/repos/example-org/storefront"), Assert.Single(_github.Requests).Uri);
    }

    [Fact]
    public async Task Bitbucket_client_reads_the_repository_branches_and_pull_requests()
    {
        var repo = _bitbucket.AddRepository("example-team", "billing-service", "develop");
        var pull = _bitbucket.AddPullRequest(repo, "bugfix/invoice-rounding");
        pull.Statuses.AddRange(["SUCCESSFUL", "FAILED", "INPROGRESS"]);
        pull.Participants.AddRange([(true, "approved"), (false, "changes_requested"), (false, null)]);
        var client = Client(RepositoryProvider.Bitbucket, "git@bitbucket.org:example-team/billing-service.git");

        var repository = await client.GetRepositoryAsync();
        var branch = await client.GetBranchAsync("bugfix/invoice-rounding");
        var created = await client.CreateBranchAsync("feature/new-export", Sha);
        var found = await client.GetPullRequestAsync("1");
        pull.State = "closed";
        var declined = await client.GetPullRequestAsync("1");

        Assert.Equal("develop", repository!.DefaultBranch);
        Assert.Equal(new Uri("https://bitbucket.org/example-team/billing-service"), repository.WebUrl);
        Assert.Equal(Sha, branch!.HeadCommit);
        Assert.Equal("feature/new-export", created.Name);
        Assert.NotNull(found);
        Assert.Equal(PullRequestState.Open, found.State);
        Assert.Equal("bugfix/invoice-rounding", found.HeadBranch);
        Assert.Equal("develop", found.BaseBranch);
        Assert.Equal("1 passed, 1 failed, 1 pending", found.ChecksSummary);
        Assert.Equal("1 approved, 1 changes requested", found.ReviewSummary);
        Assert.Equal(PullRequestState.Closed, declined!.State);
        Assert.All(_bitbucket.Requests, r => Assert.StartsWith("https://api.bitbucket.org/2.0/repositories/example-team/billing-service", r.Uri.AbsoluteUri, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Bitbucket_client_opens_a_pull_request()
    {
        var repo = _bitbucket.AddRepository("example-team", "billing-service");
        repo.Branches["bugfix/invoice-rounding"] = Sha;
        var client = Client(RepositoryProvider.Bitbucket, "https://bitbucket.org/example-team/billing-service");

        var opened = await client.OpenPullRequestAsync(new NewPullRequest("Fix invoice rounding", "bugfix/invoice-rounding", "main"));

        Assert.Equal("1", opened.ExternalId);
        Assert.Equal(new Uri("https://bitbucket.org/example-team/billing-service/pull-requests/1"), opened.WebUrl);
        Assert.Equal("main", opened.BaseBranch);
    }

    [Fact]
    public async Task Bitbucket_sends_basic_authentication_when_a_username_is_configured()
    {
        _bitbucket.ExpectedAuthorization = "Basic " + Convert.ToBase64String("bot@example.com:bitbucket-api-token"u8.ToArray());
        _bitbucket.AddRepository("example-team", "billing-service");
        var client = Client(
            RepositoryProvider.Bitbucket,
            "git@bitbucket.org:example-team/billing-service.git",
            ("RepositoryProviders:Bitbucket:Token", "bitbucket-api-token"),
            ("RepositoryProviders:Bitbucket:Username", "bot@example.com"));

        Assert.NotNull(await client.GetRepositoryAsync());
    }

    [Fact]
    public async Task A_repositorys_own_credential_takes_precedence_over_the_provider_token()
    {
        _github.ExpectedAuthorization = "Bearer storefront-only-token";
        _github.AddRepository("example-org", "storefront");
        var client = Client(
            RepositoryProvider.GitHub,
            "git@github.com:example-org/storefront.git",
            ("RepositoryProviders:Credentials:0:Repository", "storefront"),
            ("RepositoryProviders:Credentials:0:Token", "storefront-only-token"));

        Assert.NotNull(await client.GetRepositoryAsync());
    }

    [Fact]
    public void Without_a_credential_there_is_no_client()
    {
        var factory = Factory(("RepositoryProviders:GitHub:Token", "github-test-token"));
        using var unconfigured = new ServiceCollection().AddDevWorkflowApplication().BuildServiceProvider();

        Assert.Null(factory.Create(Repository(RepositoryProvider.Bitbucket, "git@bitbucket.org:example-team/billing-service.git")));
        Assert.Null(unconfigured.GetRequiredService<IRepositoryClientFactory>()
            .Create(Repository(RepositoryProvider.GitHub, "git@github.com:example-org/storefront.git")));
    }

    [Fact]
    public void Self_hosted_bitbucket_is_rejected()
    {
        var factory = Factory(("RepositoryProviders:Bitbucket:Token", "bitbucket-test-token"));

        var error = Assert.Throws<WorkflowException>(() =>
            factory.Create(Repository(RepositoryProvider.Bitbucket, "ssh://git@git.example.com:7999/team/billing-service.git")));

        Assert.Equal("unsupported_remote_url", error.Code);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "provider_access_denied")]
    [InlineData(HttpStatusCode.Forbidden, "provider_access_denied")]
    [InlineData(HttpStatusCode.InternalServerError, "provider_unavailable")]
    [InlineData(HttpStatusCode.TooManyRequests, "provider_unavailable")]
    public async Task Provider_failures_become_unavailable_errors_without_the_token(HttpStatusCode status, string code)
    {
        _github.FailWith = status;
        var client = Client(RepositoryProvider.GitHub, "git@github.com:example-org/storefront.git");

        var error = await Assert.ThrowsAsync<WorkflowException>(() => client.GetPullRequestAsync("1"));

        Assert.Equal(ErrorKind.Unavailable, error.Kind);
        Assert.Equal(code, error.Code);
        Assert.DoesNotContain("github-test-token", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Repository_clients_cannot_merge_approve_or_deploy()
    {
        var forbidden = new[] { "merge", "approve", "deploy", "delete", "protect" };
        var operations = typeof(IRepositoryClient).GetMethods().Select(m => m.Name.ToUpperInvariant());

        Assert.DoesNotContain(operations, name => forbidden.Any(word => name.Contains(word.ToUpperInvariant(), StringComparison.Ordinal)));
    }

    private IRepositoryClient Client(RepositoryProvider provider, string remote, params (string Key, string Value)[] settings)
    {
        if (settings.Length == 0)
        {
            settings = [("RepositoryProviders:GitHub:Token", "github-test-token"), ("RepositoryProviders:Bitbucket:Token", "bitbucket-test-token")];
        }

        return Factory(settings).Create(Repository(provider, remote))
            ?? throw new InvalidOperationException("Expected a client.");
    }

    private IRepositoryClientFactory Factory(params (string Key, string Value)[] settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value)))
            .Build();
        _services = new ServiceCollection()
            .AddDevWorkflowApplication()
            .AddDevWorkflowRepositoryProviders(configuration)
            .UseFakeProviders(_github, _bitbucket)
            .BuildServiceProvider();
        return _services.GetRequiredService<IRepositoryClientFactory>();
    }

    private static CodeRepository Repository(RepositoryProvider provider, string remote)
    {
        var name = remote.TrimEnd('/').Split('/', ':').Last().Replace(".git", string.Empty, StringComparison.Ordinal);
        return CodeRepository.Register(name, provider, remote, "main", DateTimeOffset.UnixEpoch);
    }
}
