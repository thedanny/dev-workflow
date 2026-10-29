using DevWorkflow.Domain;
using DevWorkflow.Domain.WorkItems;

namespace DevWorkflow.Tests.Domain;

public class WorkItemTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_work_item_without_a_repository_cannot_be_approved()
    {
        var item = TestData.ProposeWorkItem(repositoryId: null);

        var error = Assert.Throws<DomainException>(() => item.Approve("reviewer", "development-through-pr", "ok", Now));

        Assert.Equal("repository_required", error.Code);
        Assert.Equal(WorkItemStatus.Proposed, item.Status);
    }

    [Fact]
    public void Approval_records_an_authorization_and_bumps_the_version()
    {
        var item = TestData.ProposeWorkItem(repositoryId: Guid.NewGuid());

        var authorization = item.Approve("reviewer", "development-through-pr", "Confirmed defect", Now);

        Assert.Equal(WorkItemStatus.Approved, item.Status);
        Assert.Equal(2, item.Version);
        Assert.Equal(item.Id, authorization.WorkItemId);
        Assert.Equal(AuthorizationDecision.Approved, authorization.Decision);
    }

    [Fact]
    public void Only_approved_work_can_be_claimed()
    {
        var item = TestData.ProposeWorkItem(repositoryId: Guid.NewGuid());

        var error = Assert.Throws<DomainException>(() => item.MarkClaimed(Now));

        Assert.Equal("invalid_transition", error.Code);
    }

    [Fact]
    public void Claimed_work_can_be_requeued_after_a_release_or_expired_lease()
    {
        var item = TestData.ProposeWorkItem(repositoryId: Guid.NewGuid());
        item.Approve("reviewer", "development-through-pr", "ok", Now);
        item.MarkClaimed(Now);
        item.MarkInProgress(Now);

        item.Requeue(Now);

        Assert.Equal(WorkItemStatus.Approved, item.Status);
    }

    [Fact]
    public void Routing_is_locked_once_the_item_is_approved()
    {
        var item = TestData.ProposeWorkItem(repositoryId: Guid.NewGuid());
        item.Approve("reviewer", "development-through-pr", "ok", Now);

        var error = Assert.Throws<DomainException>(() => item.AssignRepository(Guid.NewGuid(), Now));

        Assert.Equal("routing_locked", error.Code);
    }

    [Theory]
    [InlineData(WorkItemStatus.Rejected)]
    [InlineData(WorkItemStatus.Completed)]
    [InlineData(WorkItemStatus.Failed)]
    [InlineData(WorkItemStatus.Cancelled)]
    public void Terminal_states_allow_no_further_transitions(WorkItemStatus terminal)
    {
        Assert.True(WorkItemTransitions.IsTerminal(terminal));
        Assert.All(Enum.GetValues<WorkItemStatus>(), target => Assert.False(WorkItemTransitions.CanMove(terminal, target)));
    }

    [Fact]
    public void Linking_the_same_signal_twice_keeps_one_link()
    {
        var signalId = Guid.NewGuid();
        var item = TestData.ProposeWorkItem(repositoryId: Guid.NewGuid(), signalIds: [signalId]);

        item.LinkSignal(signalId, Now);

        Assert.Single(item.SignalLinks);
    }
}
