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

/// <summary>
/// The tools a local coding agent uses. Every call is scoped to the agent's repository, and
/// executions can only be acted on by the credential that claimed them.
/// </summary>
[McpServerToolType]
[Authorize(Roles = RoleNames.Executor)]
public sealed class ExecutorTools(IHttpContextAccessor http, ExecutionService executions)
{
    [McpServerTool(Name = "list_approved_work", ReadOnly = true)]
    [Description("List approved work routed to your repository, highest priority first.")]
    public Task<CallToolResult> ListApprovedWork(
        [Description("Your repository: its key, or the git remote URL of your working directory.")] string repository,
        [Description("Maximum results, 1 to 100.")] int? limit = null,
        [Description("Only work at or above this priority: low, normal, high or critical.")] WorkItemPriority? min_priority = null,
        CancellationToken cancellationToken = default) =>
        ToolResults.RunAsync(() => executions.ListApprovedWorkAsync(ToolResults.CallerFrom(http), repository, limit, min_priority, cancellationToken));

    [McpServerTool(Name = "claim_work_item")]
    [Description("Claim an approved work item in your repository and start a lease. " +
                 "Renew it with heartbeat_execution before it expires, or the work returns to the queue.")]
    public Task<CallToolResult> ClaimWorkItem(
        Guid work_item_id,
        [Description("A stable name for this agent session, for example laptop-claude-1.")] string executor_id,
        [Description("Your repository: its key, or the git remote URL of your working directory.")] string repository,
        [Description("Lease length in seconds, 30 to 14400. Defaults to 900.")] int? lease_seconds = null,
        CancellationToken cancellationToken = default) =>
        ToolResults.RunAsync(() => executions.ClaimAsync(
            ToolResults.CallerFrom(http),
            work_item_id,
            new ClaimWorkItemRequest(executor_id, repository, lease_seconds),
            cancellationToken));

    [McpServerTool(Name = "heartbeat_execution", Idempotent = true)]
    [Description("Renew the lease on your execution.")]
    public Task<CallToolResult> HeartbeatExecution(
        Guid execution_id,
        [Description("New lease length in seconds from now. Defaults to 900.")] int? lease_seconds = null,
        CancellationToken cancellationToken = default) =>
        ToolResults.RunAsync(() => executions.HeartbeatAsync(ToolResults.CallerFrom(http), execution_id, lease_seconds, cancellationToken));

    [McpServerTool(Name = "update_execution")]
    [Description("Report progress. in_progress records work under way; blocked returns the item for a human to unblock; " +
                 "failed ends it; completed finishes it after a pull request was reported.")]
    public Task<CallToolResult> UpdateExecution(
        Guid execution_id,
        [Description("in_progress, blocked, failed or completed.")] ExecutionUpdateState state,
        string? summary = null,
        [Description("Links or short notes supporting the update, such as test results.")] string[]? evidence = null,
        string? branch = null,
        string? worktree_path = null,
        CancellationToken cancellationToken = default) =>
        ToolResults.RunAsync(() => executions.UpdateAsync(
            ToolResults.CallerFrom(http),
            execution_id,
            new UpdateExecutionRequest(state, summary, evidence, branch, worktree_path),
            cancellationToken));

    [McpServerTool(Name = "report_pull_request")]
    [Description("Report the pull request you opened. The URL must be in the work item's repository. This never merges anything.")]
    public Task<CallToolResult> ReportPullRequest(
        Guid execution_id,
        Uri url,
        [Description("The pull request's head branch.")] string branch,
        [Description("github or bitbucket. Defaults to the repository's provider.")] RepositoryProvider? provider = null,
        [Description("Defaults to the repository's default branch.")] string? base_branch = null,
        [Description("The PR number. Read from the URL when omitted.")] string? external_id = null,
        [Description("Summary of check or test results.")] string? checks = null,
        CancellationToken cancellationToken = default) =>
        ToolResults.RunAsync(() => executions.ReportPullRequestAsync(
            ToolResults.CallerFrom(http),
            execution_id,
            new ReportPullRequestRequest(url, branch, provider, base_branch, external_id, checks),
            cancellationToken));

    [McpServerTool(Name = "release_claim")]
    [Description("Give up your claim and return the work item to the queue.")]
    public Task<CallToolResult> ReleaseClaim(Guid execution_id, string reason, CancellationToken cancellationToken = default) =>
        ToolResults.RunAsync(() => executions.ReleaseAsync(ToolResults.CallerFrom(http), execution_id, reason, cancellationToken));
}
