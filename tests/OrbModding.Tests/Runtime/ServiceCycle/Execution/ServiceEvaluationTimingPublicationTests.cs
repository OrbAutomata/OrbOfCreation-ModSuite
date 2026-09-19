using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.TestSupport;
using Xunit;

namespace OrbModding.Tests.Runtime.ServiceCycle.Execution;

public sealed class ServiceEvaluationTimingPublicationTests
{
    [Fact]
    public void Read_BeginAndComplete_ReturnOnlyStableFacts()
    {
        var publication = new ServiceEvaluationTimingPublication();

        publication.Begin(2, new MonotonicTimestamp(200));
        Assert.True(publication.TryRead(out var evaluating));
        publication.Complete(new MonotonicTimestamp(250));
        Assert.True(publication.TryRead(out var complete));

        Assert.Equal(2, evaluating.RequestSequence);
        Assert.Equal(new MonotonicTimestamp(200), evaluating.StartedAt);
        Assert.False(evaluating.IsComplete);
        Assert.Equal(2, complete.RequestSequence);
        Assert.Equal(new MonotonicTimestamp(200), complete.StartedAt);
        Assert.Equal(new MonotonicTimestamp(250), complete.CompletedAt);
        Assert.True(complete.IsComplete);
    }

    [Fact]
    public void ReadCandidate_CrossingNextBegin_IsRejectedInsteadOfReturningMixedFact()
    {
        var candidate = new ServiceEvaluationTimingReadCandidate(
            stampBefore: 2,
            requestSequence: 1,
            startedTicks: 200,
            completedTicks: 0,
            complete: false,
            stampAfter: 4,
            trailingRequestSequence: 2);

        var accepted = ServiceEvaluationTimingPublication.TryMaterialize(
            in candidate,
            out var timing);

        Assert.False(accepted);
        Assert.False(timing.IsPresent);
    }

    [Fact]
    public void ReadCandidate_WithWrappedStampAndChangedSequence_IsRejectedAsAba()
    {
        var candidate = new ServiceEvaluationTimingReadCandidate(
            stampBefore: 4,
            requestSequence: 1,
            startedTicks: 200,
            completedTicks: 0,
            complete: false,
            stampAfter: 4,
            trailingRequestSequence: 2);

        var accepted = ServiceEvaluationTimingPublication.TryMaterialize(
            in candidate,
            out var timing);

        Assert.False(accepted);
        Assert.False(timing.IsPresent);
    }

    [Fact]
    public void ReadCandidate_WithStableStamp_ReturnsOneCoherentFact()
    {
        var candidate = new ServiceEvaluationTimingReadCandidate(
            stampBefore: 4,
            requestSequence: 2,
            startedTicks: 200,
            completedTicks: 250,
            complete: true,
            stampAfter: 4,
            trailingRequestSequence: 2);

        var accepted = ServiceEvaluationTimingPublication.TryMaterialize(
            in candidate,
            out var timing);

        Assert.True(accepted);
        Assert.Equal(2, timing.RequestSequence);
        Assert.Equal(new MonotonicTimestamp(200), timing.StartedAt);
        Assert.Equal(new MonotonicTimestamp(250), timing.CompletedAt);
        Assert.True(timing.IsComplete);
    }

    [Fact]
    [Trait("Category", "PerformanceSimulation")]
    public void Read_SteadyState_DoesNotAllocate()
    {
        var publication = new ServiceEvaluationTimingPublication();
        publication.Begin(1, new MonotonicTimestamp(100));
        publication.Complete(new MonotonicTimestamp(150));

        var allocated = AllocationProbe.MeasureRepeated(1_000, () => publication.TryRead(out _));

        Assert.Equal(0, allocated);
    }
}
