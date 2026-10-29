using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using DevWorkflow.Application.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DevWorkflow.Infrastructure.Security;

/// <summary>
/// A configured client. Only the SHA-256 hash of its key is stored, so configuration never
/// holds a usable credential.
/// </summary>
public sealed class ApiKeyClient
{
    public string Name { get; set; } = string.Empty;

    public CallerRole Role { get; set; }

    /// <summary>Lower-case hex SHA-256 of the key.</summary>
    public string KeyHash { get; set; } = string.Empty;
}

public sealed class ApiKeyOptions
{
    public const string SectionName = "Authentication";

    public IList<ApiKeyClient> ApiKeys { get; } = [];
}

public static class ApiKeys
{
    public const string Scheme = "ApiKey";

    /// <summary>Hashes a raw key the same way the handler does. Use it to produce <c>KeyHash</c> values.</summary>
    public static string Hash(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
    }

    /// <summary>Builds the caller identity that the application services authorize against.</summary>
    public static Caller ToCaller(this ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        var name = principal.FindFirstValue(ClaimTypes.Name);
        var role = principal.FindFirstValue(ClaimTypes.Role);
        if (name is null || !Enum.TryParse<CallerRole>(role, out var callerRole))
        {
            throw new InvalidOperationException("The request is not authenticated with an API key.");
        }

        return new Caller(name, callerRole);
    }
}

/// <summary>Authenticates <c>Authorization: Bearer &lt;key&gt;</c> against the configured key hashes.</summary>
internal sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IOptionsMonitor<ApiKeyOptions> keys)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var presented = header["Bearer ".Length..].Trim();
        if (presented.Length == 0)
        {
            return Task.FromResult(AuthenticateResult.Fail("Empty API key."));
        }

        var presentedHash = Encoding.ASCII.GetBytes(ApiKeys.Hash(presented));
        ApiKeyClient? match = null;
        foreach (var client in keys.CurrentValue.ApiKeys)
        {
            // Compare every entry in constant time so timing does not reveal which keys exist.
            var candidate = Encoding.ASCII.GetBytes(client.KeyHash.ToLowerInvariant());
            if (CryptographicOperations.FixedTimeEquals(presentedHash, candidate))
            {
                match = client;
            }
        }

        if (match is null)
        {
            return Task.FromResult(AuthenticateResult.Fail("Unknown API key."));
        }

        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, match.Name), new Claim(ClaimTypes.Role, match.Role.ToString())],
            ApiKeys.Scheme);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), ApiKeys.Scheme)));
    }
}

public static class SecurityServiceCollectionExtensions
{
    /// <summary>
    /// Adds API-key authentication and a fallback policy that requires an authenticated caller
    /// on every endpoint unless it opts out.
    /// </summary>
    public static IServiceCollection AddDevWorkflowAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ApiKeyOptions>()
            .Bind(configuration.GetSection(ApiKeyOptions.SectionName))
            .Validate(Validate, "Each API key needs a unique name and a 64-character hex SHA-256 key hash.")
            .ValidateOnStart();

        services.AddAuthentication(ApiKeys.Scheme)
            .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(ApiKeys.Scheme, null);

        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder(ApiKeys.Scheme).RequireAuthenticatedUser().Build());

        return services;
    }

    private static bool Validate(ApiKeyOptions options) =>
        options.ApiKeys.All(k => !string.IsNullOrWhiteSpace(k.Name) && IsSha256Hex(k.KeyHash))
        && options.ApiKeys.Select(k => k.Name).Distinct(StringComparer.Ordinal).Count() == options.ApiKeys.Count;

    private static bool IsSha256Hex(string value) => value.Length == 64 && value.All(Uri.IsHexDigit);
}
