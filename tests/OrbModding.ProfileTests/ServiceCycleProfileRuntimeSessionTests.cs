using System;
using System.Threading;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Profile;
using Xunit;

namespace OrbModding.ProfileTests;

public sealed class ServiceCycleProfileRuntimeSessionTests
{
    /// <summary>
    /// A profile that stopped because its measurements could not be trusted says which check
    /// failed. "ProbeFailed" alone was every one of them, so a stage measured off the main thread
    /// and a clock that ran backwards read the same in the log.
    /// </summary>
    [Fact]
    public void AProfileStoppedByItsProbeNamesTheFailedCheck()
    {
        using var storage = new BufferedServiceCycleProfileSinkTests.ProfileStorage();
        var probe = new ServiceCycleProfileProbe();
        using var session = new ServiceCycleProfileRuntimeSession(
            storage,
            new ServiceCycleProfileSessionId(31),
            new FixedProfileMonotonicClock(1),
            Guid.NewGuid(),
            traceActive: false,
            probe);
        var context = ServiceCycleProfileAggregatorTests.Context(stage: 1, lifecycle: 1);
        var worker = new Thread(() => probe.Begin(in context).Abandon());
        worker.Start();
        Assert.True(worker.Join(TimeSpan.FromSeconds(2)), "The foreign-thread stage did not finish.");

        session.Stop(ServiceCycleProfileTerminalReason.UserStopped);

        Assert.Equal(
            "the probe saw MeasurementPortRejected after the recorder faulted with OwnerThreadRejected",
            session.FailureCause);
    }
}
