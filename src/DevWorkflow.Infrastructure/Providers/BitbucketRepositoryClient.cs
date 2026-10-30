using System.Globalization;
using DevWorkflow.Application.Errors;
using DevWorkflow.Application.Providers;
using DevWorkflow.Domain.PullRequests;
using DevWorkflow.Domain.Repositories;

namespace DevWorkflow.Infrastructure.Providers;

/// <summary>
/// Bitbucket Cloud REST API 2.0 client for one repository. Build status comes from commit
/// statuses reported by any CI system; nothing assumes a particular pipeline.
/// </summary>
internal sealed class BitbucketRepositoryClient(ProviderHttp http, RepositoryCoordinates repository) : IRepositoryClient
{
    private const int PageSize = 100;

    private readonly string _path = $"repositories/{ProviderHttp.Segment(repository.Owner)}/{ProviderHttp.Segment(repository.Name)}";

    public RepositoryProvider Provider => RepositoryProvider.Bitbucket;

    public async Task<RemoteRepository?> GetRepositoryAsync(CancellationToken cancellationToken = default)
    {
        var repo = await http.GetAsync<BitbucketRepository>(_path, cancellationToken);
        if (repo is null)
        {
            return null;
        }

        // A repository with no commits yet has no main branch.
        return new RemoteRepository(repo.FullName, repo.Mainbranch?.Name ?? "main", repo.Links.Html.Href);
    }

    public async Task<RemoteBranch?> GetBranchAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var branch = await http.GetAsync<BitbucketBranch>($"{_path}/refs/branches/{ProviderHttp.Segment(name)}", cancellationToken);
        return branch is null ? null : new RemoteBranch(branch.Name, branch.Target.Hash);
    }

    public async Task<RemoteBranch> CreateBranchAsync(string name, string fromCommit, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(fromCommit);

        var branch = await http.PostAsync<BitbucketBranch>(
            $"{_path}/refs/branches",
            new { name, target = new { hash = fromCommit } },
            cancellationToken);
        return new RemoteBranch(branch.Name, branch.Target.Hash);
    }

    public async Task<RemotePullRequest> OpenPullRequestAsync(NewPullRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var pull = await http.PostAsync<BitbucketPull>(
            $"{_path}/pullrequests",
            new
            {
                title = request.Title,
                description = request.Description,
                source = new { branch = new { name = request.HeadBranch } },
                destination = new { branch = new { name = request.BaseBranch } },
                draft = request.Draft,
            },
            cancellationToken);
        return ToRemote(pull, checks: null);
    }

    public async Task<RemotePullRequest?> GetPullRequestAsync(string externalId, CancellationToken cancellationToken = default)
    {
        var id = PullId(externalId);
        var pull = await http.GetAsync<BitbucketPull>($"{_path}/pullrequests/{id}", cancellationToken);
        if (pull is null)
        {
            return null;
        }

        var statuses = await http.GetAsync<BitbucketPage<BitbucketStatus>>($"{_path}/pullrequests/{id}/statuses?pagelen={PageSize}", cancellationToken);
        return ToRemote(pull, StatusSummary.Checks((statuses?.Values ?? []).Select(s => StatusOutcome(s.State))));
    }

    private static CheckOutcome StatusOutcome(string state) => state switch
    {
        "SUCCESSFUL" => CheckOutcome.Passed,
        "INPROGRESS" => CheckOutcome.Pending,
        "STOPPED" => CheckOutcome.Skipped,
        _ => CheckOutcome.Failed,
    };

    private static RemotePullRequest ToRemote(BitbucketPull pull, string? checks)
    {
        var participants = pull.Participants ?? [];
        return new RemotePullRequest(
            pull.Id.ToString(CultureInfo.InvariantCulture),
            pull.Links.Html.Href,
            pull.State switch
            {
                "OPEN" => PullRequestState.Open,
                "MERGED" => PullRequestState.Merged,
                _ => PullRequestState.Closed,
            },
            pull.Draft,
            pull.Source.Branch.Name,
            pull.Source.Commit?.Hash,
            pull.Destination.Branch.Name,
            checks,
            StatusSummary.Reviews(
                participants.Count(p => p.Approved),
                participants.Count(p => p.State == "changes_requested")));
    }

    private static int PullId(string externalId) =>
        int.TryParse(externalId, NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0
            ? id
            : throw WorkflowException.Validation("invalid_pull_request_id", $"Bitbucket pull request IDs are numbers, not '{externalId}'.");

    private sealed record BitbucketRepository(string FullName, BitbucketBranchName? Mainbranch, BitbucketLinks Links);

    private sealed record BitbucketLinks(BitbucketLink Html);

    private sealed record BitbucketLink(Uri Href);

    private sealed record BitbucketBranchName(string Name);

    private sealed record BitbucketBranch(string Name, BitbucketCommit Target);

    private sealed record BitbucketCommit(string Hash);

    private sealed record BitbucketPull(
        int Id,
        string State,
        bool Draft,
        BitbucketLinks Links,
        BitbucketEndpoint Source,
        BitbucketEndpoint Destination,
        List<BitbucketParticipant>? Participants);

    private sealed record BitbucketEndpoint(BitbucketBranchName Branch, BitbucketCommit? Commit);

    private sealed record BitbucketParticipant(bool Approved, string? State);

    private sealed record BitbucketStatus(string State);

    private sealed record BitbucketPage<T>(List<T> Values);
}
