using DevWorkflow.Domain.PullRequests;
using DevWorkflow.Domain.Repositories;

namespace DevWorkflow.Application.Providers;

public sealed record RemoteRepository(string FullName, string DefaultBranch, Uri WebUrl);

public sealed record RemoteBranch(string Name, string HeadCommit);

public sealed record NewPullRequest(
    string Title,
    string HeadBranch,
    string BaseBranch,
    string? Description = null,
    bool Draft = false);

public sealed record RemotePullRequest(
    string ExternalId,
    Uri WebUrl,
    PullRequestState State,
    bool Draft,
    string HeadBranch,
    string? HeadCommit,
    string BaseBranch,
    string? ChecksSummary,
    string? ReviewSummary);

/// <summary>
/// A provider client bound to one registered repository and the credential scoped to it, so it
/// cannot reach any other repository. Executors push commits with git; <see cref="GetBranchAsync"/>
/// confirms a push landed. There is deliberately no merge, approve or deploy operation.
/// </summary>
public interface IRepositoryClient
{
    RepositoryProvider Provider { get; }

    /// <summary>Returns the repository as the provider sees it, or null if it does not exist or is not visible.</summary>
    Task<RemoteRepository?> GetRepositoryAsync(CancellationToken cancellationToken = default);

    Task<RemoteBranch?> GetBranchAsync(string name, CancellationToken cancellationToken = default);

    Task<RemoteBranch> CreateBranchAsync(string name, string fromCommit, CancellationToken cancellationToken = default);

    Task<RemotePullRequest> OpenPullRequestAsync(NewPullRequest request, CancellationToken cancellationToken = default);

    /// <summary>Returns the pull request with its checks and reviews summarized, or null if it does not exist.</summary>
    Task<RemotePullRequest?> GetPullRequestAsync(string externalId, CancellationToken cancellationToken = default);
}

public interface IRepositoryClientFactory
{
    /// <summary>Returns a client for the repository, or null when no credential is configured for it.</summary>
    IRepositoryClient? Create(CodeRepository repository);
}

/// <summary>The default when no provider is configured: nothing is checked against a provider.</summary>
internal sealed class NoRepositoryClients : IRepositoryClientFactory
{
    public IRepositoryClient? Create(CodeRepository repository) => null;
}
