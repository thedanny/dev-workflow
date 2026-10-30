using DevWorkflow.Application.Errors;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace DevWorkflow.Api;

/// <summary>
/// Turns expected workflow errors into RFC 9457 problem responses with a stable <c>code</c>.
/// Anything unexpected falls through to the default 500 handler, which reveals no details.
/// </summary>
internal sealed class WorkflowExceptionHandler(IProblemDetailsService problems) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is BadHttpRequestException badRequest)
        {
            return await WriteAsync(httpContext, StatusCodes.Status400BadRequest, "invalid_request", badRequest.Message);
        }

        var error = WorkflowError.From(exception);
        if (error is null)
        {
            return false;
        }

        var status = error.Kind switch
        {
            ErrorKind.Validation => StatusCodes.Status400BadRequest,
            ErrorKind.NotFound => StatusCodes.Status404NotFound,
            ErrorKind.Forbidden => StatusCodes.Status403Forbidden,
            ErrorKind.Unavailable => StatusCodes.Status502BadGateway,
            _ => StatusCodes.Status409Conflict,
        };

        return await WriteAsync(httpContext, status, error.Code, error.Message);
    }

    private ValueTask<bool> WriteAsync(HttpContext httpContext, int status, string code, string message)
    {
        httpContext.Response.StatusCode = status;
        return problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = new ProblemDetails
            {
                Status = status,
                Title = code,
                Detail = message,
                Extensions = { ["code"] = code },
            },
        });
    }
}
