using System.Globalization;
using System.Text.Json.Serialization;
using DevWorkflow.Application.Errors;
using DevWorkflow.Application.Providers;
using DevWorkflow.Domain.PullRequests;
using DevWorkflow.Domain.Repositories;

namespace DevWorkflow.Infrastructure.Providers;

/// <summary>GitHub REST API v3 client for one repository. Works with GitHub Enterprise Server through the API base URL.</summary>
internal sealed class GitHubRepositoryClient(ProviderHttp http, RepositoryCoordinates repository) : IRepositoryClient
{
    private const int PageSize = 100;

    private readonly string _path = $"repos/{ProviderHttp.Segment(repository.Owner)}/{ProviderHttp.Segment(repository.Name)}";

    public RepositoryProvider Provider => RepositoryProvider.GitHub;

    public async Task<RemoteRepository?> GetRepositoryAsync(CancellationToken cancellationToken = default)
    {
        var repo = await http.GetAsync<GitHubRepository>(_path, cancellationToken);
        return repo is null ? null : new RemoteRepository(repo.FullName, repo.DefaultBranch, repo.HtmlUrl);
    }

    public async Task<RemoteBranch?> GetBranchAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var reference = await http.GetAsync<GitHubReference>($"{_path}/git/ref/{HeadsPath(name)}", cancellationToken);

        return reference is null || reference.Ref != $"refs/heads/{name}" ? null : new RemoteBranch(name, reference.Target.Sha);
    }

    public async Task<RemoteBranch> CreateBranchAsync(string name, string fromCommit, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(fromCommit);

        var reference = await http.PostAsync<GitHubReference>(
            $"{_path}/git/refs",
            new { @ref = $"refs/heads/{name}", sha = fromCommit },
            cancellationToken);
        return new RemoteBranch(name, reference.Target.Sha);
    }

    public async Task<RemotePullRequest> OpenPullRequestAsync(NewPullRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var pull = await http.PostAsync<GitHubPull>(
            $"{_path}/pulls",
            new { title = request.Title, head = request.HeadBranch, @base = request.BaseBranch, body = request.Description, draft = request.Draft },
            cancellationToken);
        return ToRemote(pull, checks: null, reviews: null);
    }

    public async Task<RemotePullRequest?> GetPullRequestAsync(string externalId, CancellationToken cancellationToken = default)
    {
        var number = PullNumber(externalId);
        var pull = await http.GetAsync<GitHubPull>($"{_path}/pulls/{number}", cancellationToken);
        if (pull is null)
        {
            return null;
        }

        var checks = await ChecksAsync(pull.Head.Sha, cancellationToken);
        var reviews = await http.GetAsync<List<GitHubReview>>($"{_path}/pulls/{number}/reviews?per_page={PageSize}", cancellationToken) ?? [];
        return ToRemote(pull, checks, ReviewSummary(reviews));
    }

    /// <summary>Combines check runs and commit statuses, since CI systems report through either.</summary>
    private async Task<string?> ChecksAsync(string sha, CancellationToken cancellationToken)
    {
        var commit = $"{_path}/commits/{ProviderHttp.Segment(sha)}";
        var runs = await http.GetAsync<GitHubCheckRuns>($"{commit}/check-runs?per_page={PageSize}", cancellationToken);
        var status = await http.GetAsync<GitHubCombinedStatus>($"{commit}/status?per_page={PageSize}", cancellationToken);

        var outcomes = (runs?.CheckRuns ?? []).Select(CheckRunOutcome)
            .Concat((status?.Statuses ?? []).Select(s => StatusOutcome(s.State)));
        return StatusSummary.Checks(outcomes);
    }

    private static CheckOutcome CheckRunOutcome(GitHubCheckRun run) =>
        run.Status != "completed" ? CheckOutcome.Pending
        : run.Conclusion switch
        {
            "success" => CheckOutcome.Passed,
            "neutral" or "skipped" => CheckOutcome.Skipped,
            _ => CheckOutcome.Failed,
        };

    private static CheckOutcome StatusOutcome(string state) => state switch
    {
        "success" => CheckOutcome.Passed,
        "pending" => CheckOutcome.Pending,
        _ => CheckOutcome.Failed,
    };

    /// <summary>Counts each reviewer's latest decision. A dismissed review no longer counts.</summary>
    private static string? ReviewSummary(IEnumerable<GitHubReview> reviews)
    {
        var latest = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var review in reviews)
        {
            if (review.User?.Login is { } login && review.State is "APPROVED" or "CHANGES_REQUESTED" or "DISMISSED")
            {
                latest[login] = review.State;
            }
        }

        return StatusSummary.Reviews(
            latest.Values.Count(s => s == "APPROVED"),
            latest.Values.Count(s => s == "CHANGES_REQUESTED"));
    }

    private static RemotePullRequest ToRemote(GitHubPull pull, string? checks, string? reviews) => new(
        pull.Number.ToString(CultureInfo.InvariantCulture),
        pull.HtmlUrl,
        pull.Merged || pull.MergedAt is not null ? PullRequestState.Merged
            : pull.State == "closed" ? PullRequestState.Closed
            : PullRequestState.Open,
        pull.Draft,
        pull.Head.Ref,
        pull.Head.Sha,
        pull.Base.Ref,
        checks,
        reviews);

    private static string HeadsPath(string branch) =>
        "heads/" + string.Join('/', branch.Split('/').Select(ProviderHttp.Segment));

    private static int PullNumber(string externalId) =>
        int.TryParse(externalId, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number > 0
            ? number
            : throw WorkflowException.Validation("invalid_pull_request_id", $"GitHub pull request IDs are numbers, not '{externalId}'.");

    private sealed record GitHubRepository(string FullName, string DefaultBranch, Uri HtmlUrl);

    private sealed record GitHubReference(string Ref, [property: JsonPropertyName("object")] GitHubObject Target);

    private sealed record GitHubObject(string Sha);

    private sealed record GitHubPull(
        int Number,
        Uri HtmlUrl,
        string State,
        bool Merged,
        DateTimeOffset? MergedAt,
        bool Draft,
        GitHubPullRef Head,
        GitHubPullRef Base);

    private sealed record GitHubPullRef(string Ref, string Sha);

    private sealed record GitHubCheckRuns(List<GitHubCheckRun> CheckRuns);

    private sealed record GitHubCheckRun(string Status, string? Conclusion);

    private sealed record GitHubCombinedStatus(List<GitHubStatus> Statuses);

    private sealed record GitHubStatus(string State);

    private sealed record GitHubReview(string State, GitHubUser? User);

    private sealed record GitHubUser(string Login);
}
