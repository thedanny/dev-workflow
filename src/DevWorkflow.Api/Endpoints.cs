using DevWorkflow.Application.Contracts;
using DevWorkflow.Application.Errors;
using DevWorkflow.Application.Security;
using DevWorkflow.Application.Services;
using DevWorkflow.Domain.Signals;
using DevWorkflow.Domain.WorkItems;
using DevWorkflow.Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DevWorkflow.Api;

/// <summary>Marks the API assembly for test hosts.</summary>
public sealed class ApiAssemblyMarker;

public sealed record ReasonBody(string Reason);

public sealed record ApproveBody(string Reason, string? Scope = null);

public sealed record RouteBody(string Repository);

public sealed record ResolveSignalBody(SignalStatus Status, string? Reason = null);

public sealed record HeartbeatBody(int? LeaseSeconds = null);

public sealed record ReleaseBody(string? Reason = null);

/// <summary>
/// HTTP equivalents of the MCP tools. Every route requires an API key, and the role attribute
/// matches the role check inside the application service.
/// </summary>
internal static class Endpoints
{
    public static void MapDevWorkflowApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/v1");

        // Signal intake.
        api.MapPost("/signals", async (SubmitSignalRequest request, HttpContext http, SignalService signals, CancellationToken ct) =>
        {
            var result = await signals.SubmitAsync(http.User.ToCaller(), request, ct);
            return result.Created
                ? Results.Created($"/api/v1/signals/{result.Signal.Id}", result)
                : Results.Ok(result);
        }).RequireRoles(RoleNames.Listener);

        api.MapGet("/signals", (string? status, [FromQuery(Name = "source_type")] string? sourceType, int? limit, HttpContext http, SignalService signals, CancellationToken ct) =>
            signals.ListAsync(http.User.ToCaller(), ParseOptional<SignalStatus>(status, "status"), sourceType, limit, ct))
            .RequireRoles(RoleNames.Evaluator, RoleNames.Approver);

        api.MapPost("/signals/{id:guid}/resolve", (Guid id, ResolveSignalBody body, HttpContext http, SignalService signals, CancellationToken ct) =>
            signals.ResolveAsync(http.User.ToCaller(), id, body.Status, body.Reason, ct))
            .RequireRoles(RoleNames.Evaluator);

        // Repository registry.
        api.MapGet("/repositories", (HttpContext http, RepositoryService repositories, CancellationToken ct) =>
            repositories.ListAsync(http.User.ToCaller(), ct))
            .RequireRoles(RoleNames.Evaluator, RoleNames.Executor, RoleNames.Approver);

        api.MapPost("/repositories", async (RegisterRepositoryRequest request, HttpContext http, RepositoryService repositories, CancellationToken ct) =>
        {
            var repository = await repositories.RegisterAsync(http.User.ToCaller(), request, ct);
            return Results.Created($"/api/v1/repositories/{repository.Id}", repository);
        }).RequireRoles(RoleNames.Approver);

        // Work items: proposing, routing and authorization.
        api.MapPost("/work-items", async (ProposeWorkItemRequest request, HttpContext http, WorkItemService workItems, CancellationToken ct) =>
        {
            var item = await workItems.ProposeAsync(http.User.ToCaller(), request, ct);
            return Results.Created($"/api/v1/work-items/{item.Id}", item);
        }).RequireRoles(RoleNames.Evaluator);

        api.MapGet("/work-items", (string? status, string? repository, int? limit, HttpContext http, WorkItemService workItems, CancellationToken ct) =>
            workItems.ListAsync(http.User.ToCaller(), ParseOptional<WorkItemStatus>(status, "status"), repository, limit, ct))
            .RequireRoles(RoleNames.Evaluator, RoleNames.Approver);

        api.MapGet("/work-items/{id:guid}", (Guid id, string? repository, HttpContext http, WorkItemService workItems, CancellationToken ct) =>
            workItems.GetAsync(http.User.ToCaller(), id, repository, ct))
            .RequireRoles(RoleNames.Evaluator, RoleNames.Approver, RoleNames.Executor);

        api.MapPost("/work-items/{id:guid}/route", (Guid id, RouteBody body, HttpContext http, WorkItemService workItems, CancellationToken ct) =>
            workItems.RouteAsync(http.User.ToCaller(), id, body.Repository, ct))
            .RequireRoles(RoleNames.Evaluator, RoleNames.Approver);

        api.MapPost("/work-items/{id:guid}/approve", (Guid id, ApproveBody body, HttpContext http, WorkItemService workItems, CancellationToken ct) =>
            workItems.ApproveAsync(http.User.ToCaller(), id, body.Reason, body.Scope, ct))
            .RequireRoles(RoleNames.Approver);

        api.MapPost("/work-items/{id:guid}/reject", (Guid id, ReasonBody body, HttpContext http, WorkItemService workItems, CancellationToken ct) =>
            workItems.RejectAsync(http.User.ToCaller(), id, body.Reason, ct))
            .RequireRoles(RoleNames.Approver);

        api.MapPost("/work-items/{id:guid}/requeue", (Guid id, ReasonBody body, HttpContext http, WorkItemService workItems, CancellationToken ct) =>
            workItems.RequeueAsync(http.User.ToCaller(), id, body.Reason, ct))
            .RequireRoles(RoleNames.Approver);

        api.MapPost("/work-items/{id:guid}/cancel", (Guid id, ReasonBody body, HttpContext http, WorkItemService workItems, CancellationToken ct) =>
            workItems.CancelAsync(http.User.ToCaller(), id, body.Reason, ct))
            .RequireRoles(RoleNames.Approver);

        // Executor queue and executions.
        api.MapGet("/queue", (string repository, int? limit, [FromQuery(Name = "min_priority")] string? minPriority, HttpContext http, ExecutionService executions, CancellationToken ct) =>
            executions.ListApprovedWorkAsync(http.User.ToCaller(), repository, limit, ParseOptional<WorkItemPriority>(minPriority, "min_priority"), ct))
            .RequireRoles(RoleNames.Executor);

        api.MapPost("/work-items/{id:guid}/claim", (Guid id, ClaimWorkItemRequest request, HttpContext http, ExecutionService executions, CancellationToken ct) =>
            executions.ClaimAsync(http.User.ToCaller(), id, request, ct))
            .RequireRoles(RoleNames.Executor);

        api.MapGet("/executions", (string repository, HttpContext http, ExecutionService executions, CancellationToken ct) =>
            executions.ListActiveAsync(http.User.ToCaller(), repository, ct))
            .RequireRoles(RoleNames.Executor);

        api.MapPost("/executions/{id:guid}/heartbeat", (Guid id, HeartbeatBody? body, HttpContext http, ExecutionService executions, CancellationToken ct) =>
            executions.HeartbeatAsync(http.User.ToCaller(), id, body?.LeaseSeconds, ct))
            .RequireRoles(RoleNames.Executor);

        api.MapPost("/executions/{id:guid}/update", (Guid id, UpdateExecutionRequest request, HttpContext http, ExecutionService executions, CancellationToken ct) =>
            executions.UpdateAsync(http.User.ToCaller(), id, request, ct))
            .RequireRoles(RoleNames.Executor);

        api.MapPost("/executions/{id:guid}/pull-request", (Guid id, ReportPullRequestRequest request, HttpContext http, ExecutionService executions, CancellationToken ct) =>
            executions.ReportPullRequestAsync(http.User.ToCaller(), id, request, ct))
            .RequireRoles(RoleNames.Executor);

        api.MapPost("/executions/{id:guid}/release", (Guid id, ReleaseBody? body, HttpContext http, ExecutionService executions, CancellationToken ct) =>
            executions.ReleaseAsync(http.User.ToCaller(), id, body?.Reason ?? "released", ct))
            .RequireRoles(RoleNames.Executor);
    }

    private static RouteHandlerBuilder RequireRoles(this RouteHandlerBuilder builder, params string[] roles) =>
        builder.RequireAuthorization(new AuthorizeAttribute { Roles = string.Join(',', roles) });

    /// <summary>Parses snake_case enum values such as <c>pr_ready</c> from the query string.</summary>
    private static T? ParseOptional<T>(string? value, string name)
        where T : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return Enum.TryParse<T>(value.Replace("_", string.Empty, StringComparison.Ordinal), ignoreCase: true, out var parsed)
            ? parsed
            : throw WorkflowException.Validation("invalid_query", $"'{value}' is not a valid {name}.");
    }
}
