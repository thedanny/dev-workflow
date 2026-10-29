using System.ComponentModel;
using DevWorkflow.Application.Contracts;
using DevWorkflow.Application.Security;
using DevWorkflow.Application.Services;
using DevWorkflow.Domain.Signals;
using Microsoft.AspNetCore.Authorization;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace DevWorkflow.Mcp.Tools;

[McpServerToolType]
public sealed class SignalTools(IHttpContextAccessor http, SignalService signals)
{
    [McpServerTool(Name = "submit_signal", Idempotent = true)]
    [Description("Submit raw inbound data from any source (Jira, CloudWatch, Grafana, email, Slack, support board). " +
                 "Resubmitting the same source_type and idempotency_key returns the existing signal.")]
    [Authorize(Roles = RoleNames.Listener)]
    public Task<CallToolResult> SubmitSignal(
        [Description("Source system, for example jira, cloudwatch, grafana, email, slack.")] string source_type,
        [Description("Identifier or link for the item in the source system.")] string source_reference,
        [Description("Stable key for deduplication, unique per source type.")] string idempotency_key,
        [Description("The raw content. Secrets and email addresses are redacted before storage.")] string? payload = null,
        [Description("Where a large payload is stored, instead of sending it inline.")] string? payload_reference = null,
        [Description("When the event happened in the source system (ISO 8601).")] DateTimeOffset? observed_at = null,
        [Description("True if the listener already redacted the payload.")] bool already_redacted = false,
        CancellationToken cancellationToken = default) =>
        ToolResults.RunAsync(() => signals.SubmitAsync(
            ToolResults.CallerFrom(http),
            new SubmitSignalRequest(source_type, source_reference, idempotency_key, payload, payload_reference, observed_at, already_redacted),
            cancellationToken));

    [McpServerTool(Name = "list_signals", ReadOnly = true)]
    [Description("List signals waiting for evaluation, oldest first.")]
    [Authorize(Roles = $"{RoleNames.Evaluator},{RoleNames.Approver}")]
    public Task<CallToolResult> ListSignals(
        [Description("received, evaluated or discarded.")] SignalStatus? status = null,
        [Description("Only signals from this source type.")] string? source_type = null,
        [Description("Maximum results, 1 to 100.")] int? limit = null,
        CancellationToken cancellationToken = default) =>
        ToolResults.RunAsync(() => signals.ListAsync(ToolResults.CallerFrom(http), status, source_type, limit, cancellationToken));

    [McpServerTool(Name = "resolve_signal")]
    [Description("Mark a signal as evaluated or discarded after deciding what to do with it.")]
    [Authorize(Roles = RoleNames.Evaluator)]
    public Task<CallToolResult> ResolveSignal(
        Guid signal_id,
        [Description("evaluated or discarded.")] SignalStatus status,
        string? reason = null,
        CancellationToken cancellationToken = default) =>
        ToolResults.RunAsync(() => signals.ResolveAsync(ToolResults.CallerFrom(http), signal_id, status, reason, cancellationToken));
}
