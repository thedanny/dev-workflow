using DevWorkflow.Domain;
using DevWorkflow.Domain.Executions;
using DevWorkflow.Domain.Signals;

namespace DevWorkflow.Tests.Domain;

public class ExecutionTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(5);

    [Fact]
    public void Heartbeat_extends_the_lease_from_now()
    {
        var execution = Execution.Start(Guid.NewGuid(), "executor-1", Lease, Now);

        execution.Heartbeat(Lease, Now.AddMinutes(4));

        Assert.Equal(Now.AddMinutes(9), execution.LeaseExpiresAt);
    }

    [Fact]
    public void An_expired_lease_cannot_be_renewed()
    {
        var execution = Execution.Start(Guid.NewGuid(), "executor-1", Lease, Now);

        var error = Assert.Throws<DomainException>(() => execution.Heartbeat(Lease, Now.AddMinutes(6)));

        Assert.Equal("lease_expired", error.Code);
        Assert.True(execution.IsLeaseExpired(Now.AddMinutes(6)));
    }

    [Fact]
    public void Only_an_expired_lease_can_be_marked_expired()
    {
        var execution = Execution.Start(Guid.NewGuid(), "executor-1", Lease, Now);

        Assert.Throws<DomainException>(() => execution.Expire(Now.AddMinutes(1)));

        execution.Expire(Now.AddMinutes(5));
        Assert.Equal(ExecutionState.Expired, execution.State);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(5 * 60 * 60)]
    public void Lease_length_is_bounded(int seconds)
    {
        var error = Assert.Throws<DomainException>(
            () => Execution.Start(Guid.NewGuid(), "executor-1", TimeSpan.FromSeconds(seconds), Now));

        Assert.Equal("invalid_lease", error.Code);
    }
}

public class SignalTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_signal_needs_a_payload_or_a_reference()
    {
        var error = Assert.Throws<DomainException>(() => Signal.Receive(
            "email", "msg-1", "msg-1", payload: null, payloadReference: null, Now, RedactionStatus.Clean, Now));

        Assert.Equal("signal_payload_missing", error.Code);
    }

    [Fact]
    public void Oversized_inline_payloads_must_be_stored_by_reference()
    {
        var payload = new string('x', Signal.MaxInlinePayloadLength + 1);

        var error = Assert.Throws<DomainException>(() => Signal.Receive(
            "cloudwatch", "log-1", "log-1", payload, payloadReference: null, Now, RedactionStatus.Clean, Now));

        Assert.Equal("signal_payload_too_large", error.Code);
    }

    [Fact]
    public void Source_type_is_normalized_to_lower_case()
    {
        var signal = TestData.ReceiveSignal(sourceType: "Slack");

        Assert.Equal("slack", signal.SourceType);
    }

    [Fact]
    public void A_signal_is_processed_once()
    {
        var signal = TestData.ReceiveSignal();
        signal.MarkEvaluated();

        Assert.Throws<DomainException>(signal.Discard);
    }
}
