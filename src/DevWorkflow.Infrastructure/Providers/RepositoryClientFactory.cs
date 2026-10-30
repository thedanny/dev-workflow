using System.Net.Http.Headers;
using System.Text;
using DevWorkflow.Application.Errors;
using DevWorkflow.Application.Providers;
using DevWorkflow.Domain.Repositories;
using Microsoft.Extensions.Options;

namespace DevWorkflow.Infrastructure.Providers;

/// <summary>
/// Picks the credential for a repository, its own first and then its provider's, and binds a
/// client to that one repository. Without a credential there is no client.
/// </summary>
internal sealed class RepositoryClientFactory(IHttpClientFactory httpClients, IOptionsMonitor<RepositoryProviderOptions> options)
    : IRepositoryClientFactory
{
    private static readonly Uri GitHubCloudApi = new("https://api.github.com/");
    private static readonly Uri BitbucketCloudApi = new("https://api.bitbucket.org/2.0/");

    public IRepositoryClient? Create(CodeRepository repository)
    {
        ArgumentNullException.ThrowIfNull(repository);

        var settings = options.CurrentValue;
        var provider = repository.Provider == RepositoryProvider.GitHub ? settings.GitHub : settings.Bitbucket;
        var own = settings.Credentials.FirstOrDefault(c => string.Equals(c.Repository, repository.Key, StringComparison.OrdinalIgnoreCase));
        var (token, username) = own is not null ? (own.Token, own.Username) : (provider.Token, provider.Username);
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var coordinates = RepositoryCoordinates.From(repository);
        var http = httpClients.CreateClient(RepositoryProviders.HttpClientName(repository.Provider));
        http.BaseAddress = WithTrailingSlash(provider.ApiBaseUrl ?? DefaultApi(repository.Provider, coordinates.Host));

        if (repository.Provider == RepositoryProvider.GitHub)
        {
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return new GitHubRepositoryClient(new ProviderHttp(http, repository.Provider), coordinates);
        }

        // Bitbucket access tokens use bearer authentication; API tokens use basic with the account's user.
        http.DefaultRequestHeaders.Authorization = string.IsNullOrWhiteSpace(username)
            ? new AuthenticationHeaderValue("Bearer", token)
            : new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{token}")));
        return new BitbucketRepositoryClient(new ProviderHttp(http, repository.Provider), coordinates);
    }

    private static Uri DefaultApi(RepositoryProvider provider, string host) => provider switch
    {
        RepositoryProvider.GitHub when host == "github.com" => GitHubCloudApi,
        RepositoryProvider.GitHub => new Uri($"https://{host}/api/v3/"),
        _ when host == "bitbucket.org" => BitbucketCloudApi,
        _ => throw WorkflowException.Validation(
            "unsupported_remote_url",
            $"Only Bitbucket Cloud (bitbucket.org) is supported, not {host}."),
    };

    private static Uri WithTrailingSlash(Uri uri) =>
        uri.AbsoluteUri.EndsWith('/') ? uri : new Uri(uri.AbsoluteUri + "/");
}
