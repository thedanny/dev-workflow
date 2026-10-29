namespace DevWorkflow.Domain;

/// <summary>
/// Raised when an operation would break a domain rule, such as an invalid state transition.
/// </summary>
public sealed class DomainException : Exception
{
    public DomainException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    public DomainException()
        : this("domain_error", "A domain rule was violated.")
    {
    }

    public DomainException(string message)
        : this("domain_error", message)
    {
    }

    public DomainException(string message, Exception innerException)
        : base(message, innerException)
    {
        Code = "domain_error";
    }

    /// <summary>Stable, machine-readable error code for API and MCP responses.</summary>
    public string Code { get; } = "domain_error";
}
