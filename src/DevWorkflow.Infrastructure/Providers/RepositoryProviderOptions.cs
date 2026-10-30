namespace DevWorkflow.Infrastructure.Providers;

/// <summary>
/// Provider endpoints and credentials, bound from the <c>RepositoryProviders</c> section. Tokens
/// come from environment variables and should only allow reading repositories and pull requests,
/// plus creating branches and pull requests if those operations are used. Never grant merge,
/// admin or deploy rights.
/// </summary>
public sealed class RepositoryProviderOptions
{
    public const string SectionName = "RepositoryProviders";

    public ProviderSettings GitHub { get; } = new();

    public ProviderSettings Bitbucket { get; } = new();

    /// <summary>Credentials for single repositories. They take precedence over a provider-wide token.</summary>
    public IList<RepositoryCredential> Credentials { get; } = [];
}

public sealed class ProviderSettings
{
    /// <summary>Overrides the API address, for example for GitHub Enterprise Server.</summary>
    public Uri? ApiBaseUrl { get; set; }

    /// <summary>Used for every repository on this provider that has no credential of its own.</summary>
    public string? Token { get; set; }

    /// <summary>Bitbucket only: sends <see cref="Token"/> with basic authentication as this user.</summary>
    public string? Username { get; set; }
}

public sealed class RepositoryCredential
{
    /// <summary>The registered repository key.</summary>
    public string Repository { get; set; } = string.Empty;

    public string Token { get; set; } = string.Empty;

    /// <summary>Bitbucket only: sends the token with basic authentication as this user.</summary>
    public string? Username { get; set; }
}
