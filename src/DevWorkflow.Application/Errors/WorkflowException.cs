using DevWorkflow.Domain;
using Microsoft.EntityFrameworkCore;

namespace DevWorkflow.Application.Errors;

public enum ErrorKind
{
    Validation,
    NotFound,
    Conflict,
    Forbidden,

    /// <summary>A repository provider failed or refused the server's credential.</summary>
    Unavailable,
}

/// <summary>An expected failure with a stable code, translated to HTTP problems and MCP tool errors.</summary>
public sealed class WorkflowException : Exception
{
    public WorkflowException(ErrorKind kind, string code, string message)
        : base(message)
    {
        Kind = kind;
        Code = code;
    }

    public WorkflowException()
        : this(ErrorKind.Conflict, "workflow_error", "The operation could not be completed.")
    {
    }

    public WorkflowException(string message)
        : this(ErrorKind.Conflict, "workflow_error", message)
    {
    }

    public WorkflowException(string message, Exception innerException)
        : base(message, innerException)
    {
        Kind = ErrorKind.Conflict;
        Code = "workflow_error";
    }

    public ErrorKind Kind { get; }

    public string Code { get; } = "workflow_error";

    public static WorkflowException Validation(string code, string message) => new(ErrorKind.Validation, code, message);

    public static WorkflowException NotFound(string code, string message) => new(ErrorKind.NotFound, code, message);

    public static WorkflowException Conflict(string code, string message) => new(ErrorKind.Conflict, code, message);

    public static WorkflowException Forbidden(string code, string message) => new(ErrorKind.Forbidden, code, message);

    public static WorkflowException Unavailable(string code, string message) => new(ErrorKind.Unavailable, code, message);
}

/// <summary>A failure as returned to a client. Never contains secrets or stack traces.</summary>
public sealed record WorkflowError(ErrorKind Kind, string Code, string Message)
{
    // Domain codes that describe bad input rather than a conflicting state.
    private static readonly HashSet<string> ValidationCodes =
    [
        "invalid_remote_url",
        "invalid_lease",
        "signal_payload_missing",
        "signal_payload_too_large",
    ];

    /// <summary>Classifies an exception as an expected error, or returns null for a real fault.</summary>
    public static WorkflowError? From(Exception exception) => exception switch
    {
        WorkflowException workflow => new WorkflowError(workflow.Kind, workflow.Code, workflow.Message),
        DomainException domain => new WorkflowError(
            ValidationCodes.Contains(domain.Code) ? ErrorKind.Validation : ErrorKind.Conflict,
            domain.Code,
            domain.Message),
        DbUpdateConcurrencyException => new WorkflowError(
            ErrorKind.Conflict,
            "concurrent_update",
            "The record changed since it was read. Reload it and retry."),
        ArgumentException argument => new WorkflowError(ErrorKind.Validation, "invalid_argument", argument.Message),
        _ => null,
    };
}
