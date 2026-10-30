using System.Net.Http.Headers;
using DevWorkflow.Application.Providers;
using DevWorkflow.Domain.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DevWorkflow.Infrastructure.Providers;

public static class RepositoryProviders
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    public static string HttpClientName(RepositoryProvider provider) => provider switch
    {
        RepositoryProvider.GitHub => "repository-provider-github",
        _ => "repository-provider-bitbucket",
    };

    /// <summary>Registers the GitHub and Bitbucket adapters, configured from the <c>RepositoryProviders</c> section.</summary>
    public static IServiceCollection AddDevWorkflowRepositoryProviders(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<RepositoryProviderOptions>(configuration.GetSection(RepositoryProviderOptions.SectionName));
        services.AddHttpClient(HttpClientName(RepositoryProvider.GitHub), http =>
        {
            Defaults(http);
            http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        });
        services.AddHttpClient(HttpClientName(RepositoryProvider.Bitbucket), http =>
        {
            Defaults(http);
            http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        });
        services.Replace(ServiceDescriptor.Singleton<IRepositoryClientFactory, RepositoryClientFactory>());
        return services;
    }

    private static void Defaults(HttpClient http)
    {
        http.Timeout = Timeout;
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("dev-workflow", "1.0"));
    }
}
