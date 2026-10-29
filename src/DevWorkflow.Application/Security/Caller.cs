using DevWorkflow.Application.Errors;

namespace DevWorkflow.Application.Security;

/// <summary>
/// The roles a credential can hold. Each credential has exactly one role, and a role
/// cannot perform another role's operations.
/// </summary>
public enum CallerRole
{
    /// <summary>Submits raw signals. Nothing else.</summary>
    Listener,

    /// <summary>Reads signals, proposes and routes work items.</summary>
    Evaluator,

    /// <summary>Lists and claims approved work for its repository, and reports progress.</summary>
    Executor,

    /// <summary>Records authorization decisions and registers repositories.</summary>
    Approver,
}

/// <summary>Role names for ASP.NET Core authorization attributes.</summary>
public static class RoleNames
{
    public const string Listener = nameof(CallerRole.Listener);
    public const string Evaluator = nameof(CallerRole.Evaluator);
    public const string Executor = nameof(CallerRole.Executor);
    public const string Approver = nameof(CallerRole.Approver);
}

/// <summary>The authenticated client making a request.</summary>
public sealed record Caller(string Name, CallerRole Role)
{
    /// <summary>
    /// Enforces the role inside the application layer, so the API and MCP behave the same
    /// even if a host forgets an authorization attribute.
    /// </summary>
    public void Require(params CallerRole[] allowed)
    {
        if (!allowed.Contains(Role))
        {
            throw WorkflowException.Forbidden("role_not_permitted", $"The {Role} role cannot perform this operation.");
        }
    }
}
