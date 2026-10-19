using System;
using System.Reflection;
using System.Threading;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Lifecycle;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.Tests.Runtime.ServiceCycle.TestSupport;
using RuntimeLifecycleGeneration = OrbModding.Common.Runtime.LifecycleGeneration;
using Xunit;

namespace OrbModding.Tests.Runtime.ServiceCycle.Registration;

public sealed class ServiceCycleRegistryTests
{
    [Fact]
    public void RegistrationIsTypedStableAndStartsOneSleepingWorkerPerService()
    {
        using var registry = new ServiceCycleRegistry(2);
        using var first = registry.Register(
            new SyntheticServiceDefinition("test.first"), new RuntimeLifecycleGeneration(1));
        using var second = registry.Register(
            new SyntheticServiceDefinition("test.second"), new RuntimeLifecycleGeneration(1));

        Assert.IsType<ServiceRunner<SyntheticState, SyntheticAction>>(first.Runner);
        Assert.Equal("test.first", registry.GetServiceId(0).Value);
        Assert.Equal("test.second", registry.GetServiceId(1).Value);
        Assert.Equal(OrbModding.Common.Runtime.ServiceCycle.Contracts.ServiceCyclePhase.Waiting, first.Runner.Phase);
        Assert.True(SpinWait.SpinUntil(
            () => first.Runner.Snapshot.WorkerThreadId != 0,
            ServiceCycleTestDeadline.Value));
        Assert.True(first.Runner.Snapshot.WorkerIsBackground);
    }

    [Fact]
    public void CapacityAndDuplicateRejectionOccurBeforeConstruction()
    {
        using var registry = new ServiceCycleRegistry(2);
        using var first = registry.Register(
            new SyntheticServiceDefinition("test.one"), new RuntimeLifecycleGeneration(1));
        var duplicate = new SyntheticServiceDefinition("test.one");

        Assert.Throws<InvalidOperationException>(() => registry.Register(
            duplicate, new RuntimeLifecycleGeneration(1)));
        Assert.Equal(0, duplicate.WorkerDefinitionCreateCount);

        using var second = registry.Register(
            new SyntheticServiceDefinition("test.two"), new RuntimeLifecycleGeneration(1));
        var overflow = new SyntheticServiceDefinition("test.three");
        Assert.Throws<InvalidOperationException>(() => registry.Register(
            overflow, new RuntimeLifecycleGeneration(1)));
        Assert.Equal(0, overflow.WorkerDefinitionCreateCount);
    }

    [Fact]
    public void WorkerDefinitionFailureDoesNotAcquireResources()
    {
        using var registry = new ServiceCycleRegistry(1);
        var definition = new SyntheticServiceDefinition("test.rollback") { ThrowFromWorkerFactory = true };

        Assert.Throws<InvalidOperationException>(() => registry.Register(
            definition, new RuntimeLifecycleGeneration(1)));

        Assert.Equal(1, definition.WorkerDefinitionCreateCount);
        Assert.Equal(0, definition.StateCreateCount);
        Assert.Equal(0, definition.StateReleaseCount);
        Assert.Equal(0, registry.Count);
    }

    [Fact]
    public void WorkerDefinitionFailureIsTheRegistrationFailure()
    {
        using var registry = new ServiceCycleRegistry(1);
        var definition = new SyntheticServiceDefinition("test.rollback-original")
        {
            ThrowFromWorkerFactory = true,
        };

        var exception = Assert.Throws<InvalidOperationException>(() => registry.Register(
            definition, new RuntimeLifecycleGeneration(1)));

        Assert.Contains("worker construction", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, registry.Count);
    }

    /// <summary>
    /// A worker thread that never started releases its claim.
    /// </summary>
    /// <remarks>
    /// The second registration is the proof, not decoration: the same definition registers again and
    /// mints a fresh worker definition, so the failed attempt left nothing behind in the registry or
    /// the ledger.
    /// </remarks>
    [Fact]
    public void WorkerStartFailureReleasesTheClaim()
    {
        var definition = new SyntheticServiceDefinition("test.start-failure");
        using var registry = new ServiceCycleRegistry(
            1,
            new ThreadSafeTestClock(100),
            false,
            new FailFirstWorkerStarter());
        var claims = (ServiceResourceClaimLedger)typeof(ServiceCycleRegistry).GetField(
            "_resourceClaims",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(registry)!;

        var exception = Assert.Throws<InvalidOperationException>(() => registry.Register(
            definition, new RuntimeLifecycleGeneration(1)));

        Assert.Contains("thread start failure", exception.Message, StringComparison.Ordinal);
        Assert.Equal(1, definition.WorkerDefinitionCreateCount);
        Assert.Equal(0, definition.StateCreateCount);
        Assert.Equal(0, definition.StateReleaseCount);
        Assert.Equal(0, claims.LiveClaimCount);
        Assert.Equal(0, registry.Count);

        using var recovered = registry.Register(
            definition, new RuntimeLifecycleGeneration(1));
        Assert.Equal(2, definition.WorkerDefinitionCreateCount);
        Assert.Equal(1, claims.LiveClaimCount);
    }

    private sealed class FailFirstWorkerStarter : IServiceCycleWorkerStarter
    {
        private bool _failed;

        public void Start(Thread thread)
        {
            if (!_failed)
            {
                _failed = true;
                throw new InvalidOperationException("synthetic thread start failure");
            }
            thread.Start();
        }
    }

    [Fact]
    public void RegistrationAndRegistryDisposalAreIdempotent()
    {
        var registry = new ServiceCycleRegistry(1);
        var definition = new SyntheticServiceDefinition("test.dispose");
        var registration = registry.Register(
            definition, new RuntimeLifecycleGeneration(1));

        registration.Dispose();
        registration.Dispose();
        registry.Dispose();
        registry.Dispose();

        Assert.Equal(0, definition.StateReleaseCount);
    }

    [Fact]
    public void RegistryOwnedConfigurationOutlivesEveryRunnerAndRegistration()
    {
        var registry = new ServiceCycleRegistry(1);
        var registration = registry.Register(
            new SyntheticServiceDefinition("test.publisher-owner"),
            new RuntimeLifecycleGeneration(1));
        registry.Configuration.Publish(TestSuiteConfiguration.WithSetting(2));

        registration.Runner.Dispose();
        Assert.Equal(
            2,
            TestSuiteConfiguration.SettingOf(registry.Configuration.ReadLatest().Snapshot));

        registration.Dispose();
        Assert.Equal(
            2,
            TestSuiteConfiguration.SettingOf(registry.Configuration.ReadLatest().Snapshot));

        registry.Dispose();
        Assert.Throws<ObjectDisposedException>(
            () => registry.Configuration.ReadLatest());
    }

    [Fact]
    public void ConfigurationSurvivesARegistrationWhoseRunnerReleaseFaults()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new SyntheticServiceDefinition("test.release-fault") { ThrowFromStateRelease = true };
        var registration = registry.Register(
            definition, new RuntimeLifecycleGeneration(1));
        var publisher = registry.Configuration;
        // Worker state is minted on the first cycle, so a registration that never ran has no state
        // whose release could fault — and nothing for this to be about.
        var runner = registration.Runner;
        ServiceRunnerTestWait.StartCycle(runner, clock.Now);
        ServiceRunnerTestWait.AcquireResponse(runner, clock);
        ServiceRunnerTestWait.ForHandback(runner);

        registration.Dispose();
        Assert.True(SpinWait.SpinUntil(
            () => runner.HandoffPhaseHint == ServiceHandoffPhase.Stopped,
            ServiceCycleTestDeadline.Value));

        Assert.Same(TestSuiteConfiguration.Default, publisher.ReadLatest().Snapshot);
        Assert.Equal(1, definition.StateReleaseCount);
    }

    [Fact]
    public void ReleaseAndReregisterPreserveRelativeOrderAndFixedCapacity()
    {
        using var registry = new ServiceCycleRegistry(3);
        using var first = registry.Register(
            new SyntheticServiceDefinition("test.order.a"), new RuntimeLifecycleGeneration(1));
        var second = registry.Register(
            new SyntheticServiceDefinition("test.order.b"), new RuntimeLifecycleGeneration(1));
        using var third = registry.Register(
            new SyntheticServiceDefinition("test.order.c"), new RuntimeLifecycleGeneration(1));

        second.Dispose();
        using var fourth = registry.Register(
            new SyntheticServiceDefinition("test.order.d"), new RuntimeLifecycleGeneration(1));

        Assert.Equal("test.order.a", registry.GetServiceId(0).Value);
        Assert.Equal("test.order.d", registry.GetServiceId(1).Value);
        Assert.Equal("test.order.c", registry.GetServiceId(2).Value);
        Assert.Equal(0, first.Ordinal);
        Assert.Equal(1, fourth.Ordinal);
        Assert.Equal(2, third.Ordinal);
        Assert.Throws<InvalidOperationException>(() => registry.Register(
            new SyntheticServiceDefinition("test.order.e"),
            new RuntimeLifecycleGeneration(1)));
    }

    [Fact]
    public void SealedCompositionRejectsRegistrationAndLeavesStableTombstoneOrdinals()
    {
        using var registry = new ServiceCycleRegistry(3);
        var first = registry.Register(
            new SyntheticServiceDefinition("test.sealed.a"),
            new RuntimeLifecycleGeneration(1));
        using var second = registry.Register(
            new SyntheticServiceDefinition("test.sealed.b"),
            new RuntimeLifecycleGeneration(1));

        registry.Seal();
        registry.Seal();
        first.Dispose();

        Assert.True(registry.IsSealed);
        Assert.Equal(1, registry.Count);
        Assert.Equal(2, registry.OrdinalCount);
        Assert.Equal(0, first.Ordinal);
        Assert.Equal(1, second.Ordinal);
        Assert.Equal("test.sealed.a", registry.GetServiceId(0).Value);
        Assert.Equal("test.sealed.b", registry.GetServiceId(1).Value);
        Assert.True(registry.GetSlot(0).IsDisposed);
        Assert.False(registry.GetSlot(1).IsDisposed);

        var late = new SyntheticServiceDefinition("test.sealed.late");
        Assert.Throws<InvalidOperationException>(() => registry.Register(
            late,
            new RuntimeLifecycleGeneration(1)));
        Assert.Equal(0, late.WorkerDefinitionCreateCount);
    }

    [Fact]
    public void RegistrationMutationsAssertTheOwnerThread()
    {
        using var registry = new ServiceCycleRegistry(1);
        Exception? observed = null;
        var thread = new Thread(() =>
        {
            try
            {
                registry.Register(
                    new SyntheticServiceDefinition("test.foreign"),
                    new RuntimeLifecycleGeneration(1));
            }
            catch (Exception ex)
            {
                observed = ex;
            }
        });

        thread.Start();
        Assert.True(
            thread.Join(ServiceCycleTestDeadline.Value),
            "The foreign-thread registration probe did not complete.");

        Assert.IsType<InvalidOperationException>(observed);
        Assert.Equal(0, registry.Count);
    }

    [Fact]
    public void DisposedRegistryStillAssertsOwnerThreadBeforeIdempotentReturn()
    {
        var registry = new ServiceCycleRegistry(1);
        registry.Dispose();
        Exception? observed = null;
        var thread = new Thread(() =>
        {
            try { registry.Dispose(); }
            catch (Exception ex) { observed = ex; }
        });

        thread.Start();
        Assert.True(
            thread.Join(ServiceCycleTestDeadline.Value),
            "The foreign-thread disposal probe did not complete.");

        Assert.IsType<InvalidOperationException>(observed);
        registry.Dispose();
    }
}
