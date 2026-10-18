using System;
using System.Threading;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Profile;
using Xunit;

namespace OrbModding.ProfileTests;

public sealed class ServiceCycleProfileProbeTests
{
    [Fact]
    public void AttachedProbeRecordsExactFrameContextAndOperations()
    {
        var rawClock = new ScriptedProfileRawClock(
            1_000,
            new long[] { 100, 102, 110, 125 });
        var allocation = new ScriptedProfileAllocationCounter(
            new long[] { 0, 100, 400, 1_000, 1_064 });
        var recorder = ServiceCycleProfileMeasurementRecorderTests.Recorder(rawClock, allocation);
        var probe = new ServiceCycleProfileProbe();
        probe.Attach(recorder);
        var coordinates = new ServiceCycleProfileCoordinates(serviceOrdinal: 4, frameIdentity: 91);
        var context = CreateContext(coordinates,
            ServiceCycleProfileSpan.SemanticPumpSummary,
            lifecycle: 7,
            cycle: 12,
            ServiceCycleProfileTemperature.LifecycleRebind);

        var measurement = probe.Begin(in context);
        try
        {
            measurement.AddListEntries(3);
            measurement.AddStableIdReads(2);
            Assert.Equal(ServiceCycleProfileMeasurementResult.Accepted, measurement.Complete());
        }
        finally
        {
            measurement.Abandon();
        }

        Assert.Same(recorder, probe.Detach());
        Assert.True(recorder.Seal());
        var sample = recorder.GetSample(0, 0);
        Assert.Equal((int)ServiceCycleProfileSpan.SemanticPumpSummary, sample.StageCode);
        Assert.Equal(4, sample.ServiceOrdinal);
        Assert.Equal((ulong)7, sample.Lifecycle);
        Assert.Equal((ulong)12, sample.Cycle);
        Assert.Equal((ulong)91, sample.Frame);
        Assert.Equal(ServiceCycleProfileTemperature.LifecycleRebind, sample.Temperature);
        Assert.Equal((ulong)3, sample.Operations.ListEntries);
        Assert.Equal((ulong)2, sample.Operations.StableIdReads);
    }

    [Fact]
    public void InactiveProbeDoesNotReadMeasurementSources()
    {
        var probe = new ServiceCycleProfileProbe();
        var coordinates = new ServiceCycleProfileCoordinates(serviceOrdinal: 0, frameIdentity: 1);
        var context = CreateContext(coordinates,
            ServiceCycleProfileSpan.OverallPump,
            lifecycle: 1,
            cycle: 0,
            ServiceCycleProfileTemperature.ColdProcess);

        var measurement = probe.Begin(in context);
        try
        {
            measurement.AddListEntries();
            Assert.False(measurement.IsActive);
            Assert.Equal(ServiceCycleProfileMeasurementResult.Accepted, measurement.Complete());
        }
        finally
        {
            measurement.Abandon();
        }
    }

    [Fact]
    public void DefaultCoordinatesCannotBecomePlausibleProfileEvidence()
    {
        var coordinates = default(ServiceCycleProfileCoordinates);

        Assert.False(coordinates.IsValid);
        Assert.False(coordinates.TryCreateContext(
            ServiceCycleProfileSpan.OverallPump,
            lifecycle: 1,
            cycle: 0,
            ServiceCycleProfileTemperature.ColdProcess,
            out _));
    }

    [Fact]
    public void ForeignThreadBeginIsInertOrFaultsTheRecorderWithoutThrowing()
    {
        var coordinates = new ServiceCycleProfileCoordinates(serviceOrdinal: 0, frameIdentity: 1);
        var context = CreateContext(coordinates,
            ServiceCycleProfileSpan.OverallPump,
            lifecycle: 1,
            cycle: 0,
            ServiceCycleProfileTemperature.Warm);
        var unattached = new ServiceCycleProfileProbe();

        var unattachedResult = BeginOnThread(unattached, in context);

        Assert.Null(unattachedResult.Failure);
        Assert.False(unattachedResult.Active);

        var recorder = ServiceCycleProfileMeasurementRecorderTests.Recorder(
            new IncrementingProfileRawClock(),
            new ProvenIncrementingProfileAllocationCounter());
        var attached = new ServiceCycleProfileProbe();
        attached.Attach(recorder);

        var attachedResult = BeginOnThread(attached, in context);

        Assert.Null(attachedResult.Failure);
        Assert.False(attachedResult.Active);
        Assert.Equal(ServiceCycleProfileProbeFault.MeasurementPortRejected, attached.Fault);
        Assert.Equal(ServiceCycleProfileMeasurementFault.OwnerThreadRejected, recorder.Fault);
        Assert.Same(recorder, attached.Detach());
    }

    [Fact]
    public void MeasurementPortExceptionStopsObservationWithoutEscaping()
    {
        var port = new ThrowingBeginPort();
        var probe = new ServiceCycleProfileProbe();
        probe.Attach(port);
        var coordinates = new ServiceCycleProfileCoordinates(serviceOrdinal: 0, frameIdentity: 1);
        var context = CreateContext(coordinates,
            ServiceCycleProfileSpan.OverallPump,
            lifecycle: 1,
            cycle: 0,
            ServiceCycleProfileTemperature.Warm);

        var exception = Record.Exception(() =>
        {
            var measurement = probe.Begin(in context);
            Assert.False(measurement.IsActive);
        });

        Assert.Null(exception);
        Assert.Equal(ServiceCycleProfileProbeFault.MeasurementPortFailed, probe.Fault);
        Assert.Same(port, probe.Detach());
    }

    [Fact]
    public void UncompletedScopeIsAbandonedWithoutPublishing()
    {
        var recorder = ServiceCycleProfileMeasurementRecorderTests.Recorder(
            new IncrementingProfileRawClock(),
            new ProvenIncrementingProfileAllocationCounter());
        var probe = new ServiceCycleProfileProbe();
        probe.Attach(recorder);
        var coordinates = new ServiceCycleProfileCoordinates(serviceOrdinal: 0, frameIdentity: 1);
        var context = CreateContext(coordinates,
            ServiceCycleProfileSpan.SemanticStart,
            lifecycle: 1,
            cycle: 1,
            ServiceCycleProfileTemperature.Warm);

        var expected = new InvalidOperationException("gameplay failed");
        var abandoned = probe.Begin(in context);
        Exception? actual = null;
        try
        {
            try
            {
                abandoned.AddListEntries();
                throw expected;
            }
            finally
            {
                abandoned.Abandon();
            }
        }
        catch (InvalidOperationException exception)
        {
            actual = exception;
        }
        Assert.Same(expected, actual);

        var completed = probe.Begin(in context);
        try
        {
            Assert.Equal(ServiceCycleProfileMeasurementResult.Accepted, completed.Complete());
        }
        finally
        {
            completed.Abandon();
        }

        Assert.Same(recorder, probe.Detach());
        Assert.True(recorder.Seal());
        Assert.Equal((ulong)1, recorder.GetAggregate(0).OccurrenceCount);
    }

    private static ServiceCycleProfileContext CreateContext(
        in ServiceCycleProfileCoordinates coordinates,
        ServiceCycleProfileSpan span,
        ulong lifecycle,
        ulong cycle,
        ServiceCycleProfileTemperature temperature)
    {
        Assert.True(coordinates.TryCreateContext(
            span,
            lifecycle,
            cycle,
            temperature,
            out var context));
        return context;
    }

    private static ThreadBeginResult BeginOnThread(
        ServiceCycleProfileProbe probe,
        in ServiceCycleProfileContext context)
    {
        var result = default(ThreadBeginResult);
        var copy = context;
        var thread = new Thread(() =>
        {
            try
            {
                var measurement = probe.Begin(in copy);
                result = new ThreadBeginResult(measurement.IsActive, null);
                measurement.Abandon();
            }
            catch (Exception exception)
            {
                result = new ThreadBeginResult(false, exception);
            }
        });
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(1)), "Profile probe thread did not finish.");
        return result;
    }

    private readonly record struct ThreadBeginResult(bool Active, Exception? Failure);

    private sealed class ThrowingBeginPort : IServiceCycleProfileMeasurementPort
    {
        public bool TryBegin(
            in ServiceCycleProfileContext context,
            out ServiceCycleProfileMeasurementToken token)
        {
            token = default;
            throw new InvalidOperationException("profile port failed");
        }

        public ServiceCycleProfileMeasurementResult Complete(
            in ServiceCycleProfileMeasurementToken token,
            in ServiceCycleProfileOperationCounters operations) =>
            ServiceCycleProfileMeasurementResult.Accepted;

        public ServiceCycleProfileMeasurementResult Abandon(
            in ServiceCycleProfileMeasurementToken token) =>
            ServiceCycleProfileMeasurementResult.Accepted;
    }

}
