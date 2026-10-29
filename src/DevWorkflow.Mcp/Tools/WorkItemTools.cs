using System.ComponentModel;
using DevWorkflow.Application.Contracts;
using DevWorkflow.Application.Security;
using DevWorkflow.Application.Services;
using DevWorkflow.Domain.Repositories;
using DevWorkflow.Domain.WorkItems;
using Microsoft.AspNetCore.Authorization;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace DevWorkflow.Mcp.Tools;

/// <summary>Repository registry, proposing and routing work, and authorization decisions.</summary>
[McpServerToolType]
public sealed class WorkItemTools(
    IHttpContextAccessor http,
    WorkItemService workItems,
    RepositoryService repositories)
{
    [McpServerTool(Name = "list_repositories", ReadOnly = true)]
    [Description("List registered repositories that work can be routed to.")]
    [Authorize(Roles = $"{RoleNames.Evaluator},{RoleNames.Executor},{RoleNames.Approver}")]
    public Task<CallToolResult> ListRepositories(CancellationToken cancellationToken = default) =>
        ToolResults.RunAsync(() => repositories.ListAsync(ToolResults.CallerFrom(http), cancellationToken));

    [McpServerTool(Name = "register_repository")]
    [Description("Register a repository so work can be routed to it.")]
    [Authorize(Roles = RoleNames.Approver)]
    public Task<CallToolResult> RegisterRepository(
        [Description("Short unique key, for example sample-api.")] string key,
        [Description("github or bitbucket.")] RepositoryProvider provider,
        [Description("Git remote URL, SSH or HTTPS.")] string remote_url,
        string? default_branch = null,
        CancellationToken cancellationToken = default) =>
        ToolResults.RunAsync(() => repositories.RegisterAsync(
            ToolResults.CallerFrom(http),
            new RegisterRepositoryRequest(key, provider, remote_url, default_branch),
            cancellationToken));

    [McpServerTool(Name = "propose_work_item")]
    [Description("Propose a work item from evaluated signals. It is not executable until an approver approves it.")]
    [Authorize(Roles = RoleNames.Evaluator)]
    public Task<CallToolResult> ProposeWorkItem(
        string title,
        string? description = null,
        [Description("Target repository key or git remote URL. Required before approval; may be routed later.")] string? repository = null,
        [Description("IDs of the signals this work came from.")] Guid[]? signal_ids = null,
        string[]? acceptance_criteria = null,
        [Description("low, normal, high or critical.")] WorkItemPriority priority = WorkItemPriority.Normal,
        [Description("low, medium or high.")] WorkItemRisk risk = WorkItemRisk.Medium,
        [Description("References that are not signals, such as a ticket key.")] string[]? source_references = null,
        CancellationToken cancellationToken = default) =>
        ToolResults.RunAsync(() => workItems.ProposeAsync(
            ToolResults.CallerFrom(http),
            new ProposeWorkItemRequest(title, description, repository, signal_ids, acceptance_criteria, priority, risk, source_references),
            cancellationToken));

    [McpServerTool(Name = "route_work_item")]
    [Description("Set the target repository of a proposed work item.")]
    [Authorize(Roles = $"{RoleNames.Evaluator},{RoleNames.Approver}")]
    public Task<CallToolResult> RouteWorkItem(Guid work_item_id, string repository, CancellationToken cancellationToken = default) =>
        ToolResults.RunAsync(() => workItems.RouteAsync(ToolResults.CallerFrom(http), work_item_id, repository, cancellationToken));

    [McpServerTool(Name = "list_work_items", ReadOnly = true)]
    [Description("List work items, most recently updated first.")]
    [Authorize(Roles = $"{RoleNames.Evaluator},{RoleNames.Approver}")]
    public Task<CallToolResult> ListWorkItems(
        [Description("proposed, approved, rejected, claimed, in_progress, pr_ready, completed, blocked, failed or cancelled.")] WorkItemStatus? status = null,
        string? repository = null,
        int? limit = null,
        CancellationToken cancellationToken = default) =>
        ToolResults.RunAsync(() => workItems.ListAsync(ToolResults.CallerFrom(http), status, repository, limit, cancellationToken));

    [McpServerTool(Name = "get_work_item", ReadOnly = true)]
    [Description("Get one work item. Executors must pass their repository and can only read work routed to it.")]
    [Authorize(Roles = $"{RoleNames.Evaluator},{RoleNames.Approver},{RoleNames.Executor}")]
    public Task<CallToolResult> GetWorkItem(
        Guid work_item_id,
        [Description("The executor's repository key or git remote URL.")] string? repository = null,
        CancellationToken cancellationToken = default) =>
        ToolResults.RunAsync(() => workItems.GetAsync(ToolResults.CallerFrom(http), work_item_id, repository, cancellationToken));

    [McpServerTool(Name = "approve_work_item")]
    [Description("Authorize a proposed work item for development through pull request creation. Never authorizes merge or deployment.")]
    [Authorize(Roles = RoleNames.Approver)]
    public Task<CallToolResult> ApproveWorkItem(
        Guid work_item_id,
        string reason,
        [Description("What the approval covers. Defaults to development-through-pr.")] string? scope = null,
        CancellationToken cancellationToken = default) =>
        ToolResults.RunAsync(() => workItems.ApproveAsync(ToolResults.CallerFrom(http), work_item_id, reason, scope, cancellationToken));

    [McpServerTool(Name = "reject_work_item")]
    [Authorize(Roles = RoleNames.Approver)]
    [Description("Reject a proposed work item.")]
    public Task<CallToolResult> RejectWorkItem(Guid work_item_id, string reason, CancellationToken cancellationToken = default) =>
        ToolResults.RunAsync(() => workItems.RejectAsync(ToolResults.CallerFrom(http), work_item_id, reason, cancellationToken));

    [McpServerTool(Name = "requeue_work_item")]
    [Authorize(Roles = RoleNames.Approver)]
    [Description("Return blocked work to the executable queue once the blocker is resolved.")]
    public Task<CallToolResult> RequeueWorkItem(Guid work_item_id, string reason, CancellationToken cancellationToken = default) =>
        ToolResults.RunAsync(() => workItems.RequeueAsync(ToolResults.CallerFrom(http), work_item_id, reason, cancellationToken));

    [McpServerTool(Name = "cancel_work_item", Destructive = true)]
    [Authorize(Roles = RoleNames.Approver)]
    [Description("Cancel a work item and release any active claim on it.")]
    public Task<CallToolResult> CancelWorkItem(Guid work_item_id, string reason, CancellationToken cancellationToken = default) =>
        ToolResults.RunAsync(() => workItems.CancelAsync(ToolResults.CallerFrom(http), work_item_id, reason, cancellationToken));
}
