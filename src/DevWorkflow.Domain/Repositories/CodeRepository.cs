namespace DevWorkflow.Domain.Repositories;

public enum RepositoryProvider
{
    GitHub,
    Bitbucket,
}

/// <summary>
/// A registered repository that work can be routed to. Named CodeRepository to avoid
/// confusion with the repository data-access pattern.
/// </summary>
public sealed class CodeRepository
{
    private CodeRepository()
    {
        Key = string.Empty;
        RemoteUrl = string.Empty;
        DefaultBranch = string.Empty;
    }

    public Guid Id { get; private set; }

    /// <summary>Short, unique, human-friendly identifier such as <c>sample-api</c>.</summary>
    public string Key { get; private set; }

    public RepositoryProvider Provider { get; private set; }

    /// <summary>Normalized remote URL; see <see cref="Repositories.RemoteUrl.Normalize"/>.</summary>
    public string RemoteUrl { get; private set; }

    public string DefaultBranch { get; private set; }

    public bool Enabled { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static CodeRepository Register(
        string key,
        RepositoryProvider provider,
        string remoteUrl,
        string defaultBranch,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultBranch);

        return new CodeRepository
        {
            Id = Guid.CreateVersion7(now),
            Key = key.Trim().ToLowerInvariant(),
            Provider = provider,
            RemoteUrl = Repositories.RemoteUrl.Normalize(remoteUrl),
            DefaultBranch = defaultBranch.Trim(),
            Enabled = true,
            CreatedAt = now,
        };
    }

    public void Disable() => Enabled = false;

    public void Enable() => Enabled = true;
}
