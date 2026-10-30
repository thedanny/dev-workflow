using DevWorkflow.Application.Abstractions;
using DevWorkflow.Application.Contracts;
using DevWorkflow.Application.Security;
using DevWorkflow.Application.Services;
using DevWorkflow.Domain.Repositories;
using DevWorkflow.Domain.Signals;
using DevWorkflow.Domain.WorkItems;
using Microsoft.EntityFrameworkCore;

namespace DevWorkflow.Application.Samples;

/// <summary>
/// Fictional demonstration data: two sample repositories, signals from three source types, and
/// three approved work items linked to those signals. Everything goes through the normal
/// services, so roles, redaction and the audit trail apply. Running it again does nothing.
/// </summary>
public sealed class SampleDataSeeder(
    IDevWorkflowDb db,
    RepositoryService repositories,
    SignalService signals,
    WorkItemService workItems)
{
    public const string StorefrontKey = "sample-storefront";
    public const string BillingKey = "sample-billing";

    private const string Reason = "Sample data: confirmed low-risk defect for local demonstration.";

    private static readonly Caller Listener = new("sample-listener", CallerRole.Listener);
    private static readonly Caller Evaluator = new("sample-evaluator", CallerRole.Evaluator);
    private static readonly Caller Approver = new("sample-approver", CallerRole.Approver);

    /// <summary>Returns false when the sample data already exists.</summary>
    public async Task<bool> SeedAsync(CancellationToken cancellationToken = default)
    {
        if (await db.Repositories.AnyAsync(r => r.Key == StorefrontKey || r.Key == BillingKey, cancellationToken))
        {
            return false;
        }

        await repositories.RegisterAsync(Approver, new RegisterRepositoryRequest(
            StorefrontKey, RepositoryProvider.GitHub, "https://github.com/example-org/sample-storefront.git", "main"), cancellationToken);
        await repositories.RegisterAsync(Approver, new RegisterRepositoryRequest(
            BillingKey, RepositoryProvider.Bitbucket, "https://bitbucket.org/example-team/sample-billing.git", "main"), cancellationToken);

        var ticket = await SignalAsync("jira", "PROJ-101", """
            {"key":"PROJ-101","type":"Bug","summary":"Checkout button does nothing on small screens","reporter":"customer@example.com","labels":["mobile","checkout"]}
            """, cancellationToken);
        var support = await SignalAsync("support", "TICKET-2042", """
            {"ticket":"TICKET-2042","subject":"Order export is missing the order ID column","channel":"support-board","priority":"normal"}
            """, cancellationToken);
        var alarm = await SignalAsync("cloudwatch", "alarm/sample-billing-invoice-errors", """
            {"alarm":"sample-billing-invoice-errors","state":"ALARM","metric":"InvoiceJobErrors","threshold":5,"value":17,"sample_log":"Invoice total 10.005 rounded to 10.00, expected 10.01"}
            """, cancellationToken);

        await ProposeAndApproveAsync(new ProposeWorkItemRequest(
            "Fix checkout button on small screens",
            "The checkout button does not respond to taps on viewports narrower than 400px.",
            StorefrontKey,
            [ticket],
            ["Checkout completes on a 375px-wide viewport", "A UI test covers the small-screen layout"],
            WorkItemPriority.High,
            WorkItemRisk.Low,
            ["jira:PROJ-101"]), cancellationToken);
        await ProposeAndApproveAsync(new ProposeWorkItemRequest(
            "Add the order ID column to the CSV export",
            "Exported orders cannot be matched back to the store because the order ID is missing.",
            StorefrontKey,
            [support],
            ["The CSV export's first column is the order ID", "Existing columns keep their order"],
            WorkItemPriority.Normal,
            WorkItemRisk.Low,
            ["support:TICKET-2042"]), cancellationToken);
        await ProposeAndApproveAsync(new ProposeWorkItemRequest(
            "Round invoice totals half away from zero",
            "Invoice totals are rounded half to even, so some invoices are one cent short.",
            BillingKey,
            [alarm],
            ["10.005 rounds to 10.01", "Unit tests cover the rounding of positive and negative totals"],
            WorkItemPriority.High,
            WorkItemRisk.Low,
            ["cloudwatch:alarm/sample-billing-invoice-errors"]), cancellationToken);

        foreach (var signal in new[] { ticket, support, alarm })
        {
            await signals.ResolveAsync(Evaluator, signal, SignalStatus.Evaluated, "Sample data: proposed as work.", cancellationToken);
        }

        return true;
    }

    private async Task<Guid> SignalAsync(string sourceType, string reference, string payload, CancellationToken cancellationToken)
    {
        var result = await signals.SubmitAsync(
            Listener,
            new SubmitSignalRequest(sourceType, reference, $"sample:{sourceType}:{reference}", payload.Trim()),
            cancellationToken);
        return result.Signal.Id;
    }

    private async Task ProposeAndApproveAsync(ProposeWorkItemRequest request, CancellationToken cancellationToken)
    {
        var proposed = await workItems.ProposeAsync(Evaluator, request, cancellationToken);
        await workItems.ApproveAsync(Approver, proposed.Id, Reason, null, cancellationToken);
    }
}
