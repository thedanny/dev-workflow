using System.Text.RegularExpressions;

namespace DevWorkflow.Domain.Repositories;

/// <summary>
/// Normalizes git remote URLs so that SSH and HTTPS forms of the same remote compare equal.
/// The canonical form is <c>host/owner/name</c> in lower case, without scheme, user, port or <c>.git</c>.
/// </summary>
public static partial class RemoteUrl
{
    public static string Normalize(string remote)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(remote);

        var value = remote.Trim();

        var scp = ScpLikeRemote().Match(value);
        if (scp.Success)
        {
            return Canonical(scp.Groups["host"].Value, scp.Groups["path"].Value, remote);
        }

        if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Host))
        {
            return Canonical(uri.Host, uri.AbsolutePath, remote);
        }

        // The canonical form itself, host/owner/name, so values returned by the API can be passed back in.
        if (!value.Contains("://", StringComparison.Ordinal)
            && Uri.TryCreate("https://" + value, UriKind.Absolute, out var bare)
            && bare.Host.Contains('.', StringComparison.Ordinal))
        {
            return Canonical(bare.Host, bare.AbsolutePath, remote);
        }

        throw new DomainException("invalid_remote_url", $"'{remote}' is not a recognizable git remote URL.");
    }

    private static string Canonical(string host, string path, string original)
    {
        var trimmedPath = path.Trim('/');
        if (trimmedPath.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            trimmedPath = trimmedPath[..^4];
        }

        if (trimmedPath.Length == 0 || !trimmedPath.Contains('/', StringComparison.Ordinal))
        {
            throw new DomainException("invalid_remote_url", $"'{original}' does not name an owner and repository.");
        }

        return $"{host}/{trimmedPath}".ToLowerInvariant();
    }

    // Matches scp-like SSH remotes such as git@github.com:owner/name.git.
    [GeneratedRegex(@"^(?:[^@/\s]+@)?(?<host>[^:/\s]+):(?!//)(?<path>[^\s]+)$")]
    private static partial Regex ScpLikeRemote();
}
