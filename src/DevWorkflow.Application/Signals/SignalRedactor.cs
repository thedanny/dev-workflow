using System.Text.RegularExpressions;

namespace DevWorkflow.Application.Signals;

public interface ISignalRedactor
{
    /// <summary>Returns the text with secrets and personal data replaced, and whether anything changed.</summary>
    (string? Text, bool Changed) Redact(string? text);
}

/// <summary>
/// A server-side safety net that removes common secrets and email addresses before a signal is
/// stored. Listeners should still redact at the source; this catches what they miss.
/// </summary>
public sealed partial class SignalRedactor : ISignalRedactor
{
    public const string Placeholder = "[REDACTED]";

    public (string? Text, bool Changed) Redact(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return (text, false);
        }

        var result = PrivateKeyBlock().Replace(text, Placeholder);
        result = JsonWebToken().Replace(result, Placeholder);
        result = BearerToken().Replace(result, "Bearer " + Placeholder);
        result = AwsAccessKey().Replace(result, Placeholder);
        result = ProviderToken().Replace(result, Placeholder);
        result = SecretAssignment().Replace(result, "${prefix}" + Placeholder);
        result = EmailAddress().Replace(result, "[REDACTED_EMAIL]");

        return (result, !string.Equals(result, text, StringComparison.Ordinal));
    }

    [GeneratedRegex(@"-----BEGIN [A-Z ]*PRIVATE KEY-----[\s\S]*?-----END [A-Z ]*PRIVATE KEY-----")]
    private static partial Regex PrivateKeyBlock();

    [GeneratedRegex(@"\beyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+")]
    private static partial Regex JsonWebToken();

    [GeneratedRegex(@"(?i)\bbearer\s+[A-Za-z0-9\-._~+/]+=*")]
    private static partial Regex BearerToken();

    [GeneratedRegex(@"\b(?:AKIA|ASIA)[0-9A-Z]{16}\b")]
    private static partial Regex AwsAccessKey();

    // GitHub personal/OAuth/app tokens and Slack tokens.
    [GeneratedRegex(@"\b(?:gh[pousr]_[A-Za-z0-9]{30,}|github_pat_[A-Za-z0-9_]{30,}|xox[abprs]-[A-Za-z0-9-]{10,})\b")]
    private static partial Regex ProviderToken();

    // password=..., "api_key": "...", Password=...; in connection strings, and similar.
    [GeneratedRegex(
        @"(?i)(?<prefix>\b(?:password|passwd|pwd|secret|client[_-]?secret|api[_-]?key|access[_-]?token|refresh[_-]?token|auth[_-]?token)\b""?\s*[:=]\s*""?)[^\s""',;}&]+")]
    private static partial Regex SecretAssignment();

    [GeneratedRegex(@"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}\b")]
    private static partial Regex EmailAddress();
}
