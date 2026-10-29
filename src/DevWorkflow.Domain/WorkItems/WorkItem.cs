namespace DevWorkflow.Domain.WorkItems;

/// <summary>
/// A unit of development work routed to exactly one repository. Every change increments
/// <see cref="Version"/>, which persistence uses for optimistic concurrency.
/// </summary>
public sealed class WorkItem
{
    private readonly List<string> _acceptanceCriteria = [];
    private readonly List<string> _sourceReferences = [];
    private readonly List<WorkItemSignal> _signalLinks = [];

    private WorkItem()
    {
        Title = string.Empty;
        Description = string.Empty;
    }

    public Guid Id { get; private set; }

    public string Title { get; private set; }

    public string Description { get; private set; }

    /// <summary>Target repository. Required before approval.</summary>
    public Guid? RepositoryId { get; private set; }

    public IReadOnlyList<string> AcceptanceCriteria => _acceptanceCriteria;

    /// <summary>Links to originating systems that are not signals, such as a Jira key.</summary>
    public IReadOnlyList<string> SourceReferences => _sourceReferences;

    public IReadOnlyList<WorkItemSignal> SignalLinks => _signalLinks;

    public WorkItemPriority Priority { get; private set; }

    public WorkItemRisk Risk { get; private set; }

    public WorkItemStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public int Version { get; private set; }

    public static WorkItem Propose(
        string title,
        string description,
        IEnumerable<string> acceptanceCriteria,
        WorkItemPriority priority,
        WorkItemRisk risk,
        Guid? repositoryId,
        IEnumerable<Guid> signalIds,
        IEnumerable<string> sourceReferences,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(acceptanceCriteria);
        ArgumentNullException.ThrowIfNull(signalIds);
        ArgumentNullException.ThrowIfNull(sourceReferences);

        var item = new WorkItem
        {
            Id = Guid.CreateVersion7(now),
            Title = title.Trim(),
            Description = description?.Trim() ?? string.Empty,
            Priority = priority,
            Risk = risk,
            RepositoryId = repositoryId,
            Status = WorkItemStatus.Proposed,
            CreatedAt = now,
            UpdatedAt = now,
            Version = 1,
        };

        item._acceptanceCriteria.AddRange(acceptanceCriteria.Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim()));
        item._sourceReferences.AddRange(sourceReferences.Where(r => !string.IsNullOrWhiteSpace(r)).Select(r => r.Trim()));
        item._signalLinks.AddRange(signalIds.Distinct().Select(signalId => new WorkItemSignal(item.Id, signalId, now)));

        return item;
    }

    public void AssignRepository(Guid repositoryId, DateTimeOffset now)
    {
        if (Status != WorkItemStatus.Proposed)
        {
            throw new DomainException("routing_locked", "The target repository can only change while the work item is Proposed.");
        }

        RepositoryId = repositoryId;
        Touch(now);
    }

    public void LinkSignal(Guid signalId, DateTimeOffset now)
    {
        if (_signalLinks.Exists(link => link.SignalId == signalId))
        {
            return;
        }

        _signalLinks.Add(new WorkItemSignal(Id, signalId, now));
        Touch(now);
    }

    public Authorization Approve(string actor, string scope, string reason, DateTimeOffset now)
    {
        if (RepositoryId is null)
        {
            throw new DomainException("repository_required", "A work item cannot be approved without a target repository.");
        }

        MoveTo(WorkItemStatus.Approved, now);
        return Authorization.Record(Id, AuthorizationDecision.Approved, actor, scope, reason, now);
    }

    public Authorization Reject(string actor, string reason, DateTimeOffset now)
    {
        MoveTo(WorkItemStatus.Rejected, now);
        return Authorization.Record(Id, AuthorizationDecision.Rejected, actor, "none", reason, now);
    }

    public void MarkClaimed(DateTimeOffset now) => MoveTo(WorkItemStatus.Claimed, now);

    public void MarkInProgress(DateTimeOffset now) => MoveTo(WorkItemStatus.InProgress, now);

    public void MarkPullRequestReady(DateTimeOffset now) => MoveTo(WorkItemStatus.PRReady, now);

    public void Complete(DateTimeOffset now) => MoveTo(WorkItemStatus.Completed, now);

    public void Block(DateTimeOffset now) => MoveTo(WorkItemStatus.Blocked, now);

    public void Fail(DateTimeOffset now) => MoveTo(WorkItemStatus.Failed, now);

    public void Cancel(DateTimeOffset now) => MoveTo(WorkItemStatus.Cancelled, now);

    /// <summary>Returns claimed, in-progress or blocked work to the executable queue.</summary>
    public void Requeue(DateTimeOffset now) => MoveTo(WorkItemStatus.Approved, now);

    private void MoveTo(WorkItemStatus target, DateTimeOffset now)
    {
        if (!WorkItemTransitions.CanMove(Status, target))
        {
            throw new DomainException(
                "invalid_transition",
                $"Work item {Id} cannot move from {Status} to {target}.");
        }

        Status = target;
        Touch(now);
    }

    private void Touch(DateTimeOffset now)
    {
        UpdatedAt = now;
        Version++;
    }
}

/// <summary>Links a work item to a signal it came from. Many-to-many.</summary>
public sealed class WorkItemSignal
{
    internal WorkItemSignal(Guid workItemId, Guid signalId, DateTimeOffset linkedAt)
    {
        WorkItemId = workItemId;
        SignalId = signalId;
        LinkedAt = linkedAt;
    }

    public Guid WorkItemId { get; private set; }

    public Guid SignalId { get; private set; }

    public DateTimeOffset LinkedAt { get; private set; }
}
