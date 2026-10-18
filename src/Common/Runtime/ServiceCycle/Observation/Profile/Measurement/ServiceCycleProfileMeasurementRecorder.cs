#if SERVICE_CYCLE_PROFILE
using System;
using System.Threading;

namespace OrbModding.Common.Runtime.ServiceCycle.Observation.Profile;

internal sealed class ServiceCycleProfileMeasurementRecorder : IServiceCycleProfileMeasurementPort
{
    private const int MaximumMeasurementDepth = 64;

    private readonly IServiceCycleProfileRawClock _rawClock;
    private readonly ServiceCycleProfileAllocationCapability _allocationCapability;
    private readonly ServiceCycleProfileAggregator _aggregator;
    private readonly ulong[] _activeTokenSequences;

    /// <summary>
    /// Per open span, how much of its wall time belongs to the suite observing itself rather than to
    /// the work the span names. Subtracted before the span is recorded, so the frame's own span never
    /// reports the cost of recording the frame.
    /// </summary>
    private readonly long[] _excludedChildRawTicks;

    private readonly int _ownerThreadId;
    private int _fault;
    private Exception? _faultException;
    private bool _sealed;
    private int _activeDepth;
    private ulong _nextTokenSequence;

    internal ServiceCycleProfileMeasurementRecorder(
        in ServiceCycleProfileCalibrationPoint calibrationPoint,
        int maximumGroups,
        int samplesPerGroup,
        int maximumMeasurementDepth)
    {
        var calibration = calibrationPoint.Calibration;
        var allocationCapability = calibrationPoint.AllocationCapability;
        if (!calibrationPoint.IsValid)
            throw new ArgumentException("A valid profile calibration point is required.", nameof(calibrationPoint));
        if (calibrationPoint.OwnerThreadId != Environment.CurrentManagedThreadId)
            throw new ArgumentException("The profile recorder must be created on the calibration thread.", nameof(calibrationPoint));
        if (maximumMeasurementDepth is <= 0 or > MaximumMeasurementDepth)
            throw new ArgumentOutOfRangeException(nameof(maximumMeasurementDepth));
        _rawClock = calibrationPoint.RawClock;
        _allocationCapability = allocationCapability;
        _ownerThreadId = calibrationPoint.OwnerThreadId;
        _activeTokenSequences = new ulong[maximumMeasurementDepth];
        _excludedChildRawTicks = new long[maximumMeasurementDepth];
        _aggregator = new ServiceCycleProfileAggregator(
            maximumGroups,
            samplesPerGroup,
            calibration.AllocationAvailable);
    }

    internal ServiceCycleProfileMeasurementFault Fault =>
        (ServiceCycleProfileMeasurementFault)Volatile.Read(ref _fault);
    /// <summary>What a clock, counter or the aggregator threw, when a throw is what faulted.</summary>
    internal Exception? FaultException => Volatile.Read(ref _faultException);
    internal ServiceCycleProfileAggregationFault AggregationFault => _aggregator.Fault;
    internal int GroupCount => _aggregator.GroupCount;
    internal int MaximumOutputRecords => _aggregator.MaximumOutputRecords;

    public bool TryBegin(
        in ServiceCycleProfileContext context,
        out ServiceCycleProfileMeasurementToken token)
    {
        token = default;
        if (!IsOwnerThread()) return Fail(ServiceCycleProfileMeasurementFault.OwnerThreadRejected);
        if (Fault != ServiceCycleProfileMeasurementFault.None) return false;
        if (_sealed) return Fail(ServiceCycleProfileMeasurementFault.AggregatorSealed);
        if (!context.IsValid) return Fail(ServiceCycleProfileMeasurementFault.TokenRejected);
        if (_activeDepth == _activeTokenSequences.Length)
            return Fail(ServiceCycleProfileMeasurementFault.MeasurementDepthExhausted);
        if (_nextTokenSequence == ulong.MaxValue)
            return Fail(ServiceCycleProfileMeasurementFault.TokenSequenceExhausted);

        if (!TryReadAllocation(out var allocatedBytes)) return false;
        if (!TryReadTimestamp(out var startedAtRawTicks)) return false;
        var sequence = ++_nextTokenSequence;
        _excludedChildRawTicks[_activeDepth] = 0;
        _activeTokenSequences[_activeDepth++] = sequence;
        token = new ServiceCycleProfileMeasurementToken(
            this,
            sequence,
            in context,
            startedAtRawTicks,
            allocatedBytes);
        return true;
    }

    public ServiceCycleProfileMeasurementResult Complete(
        in ServiceCycleProfileMeasurementToken token,
        in ServiceCycleProfileOperationCounters operations)
    {
        if (!IsOwnerThread())
            return FailResult(ServiceCycleProfileMeasurementFault.OwnerThreadRejected);
        if (Fault != ServiceCycleProfileMeasurementFault.None)
            return ServiceCycleProfileMeasurementResult.Faulted;
        if (!token.IsOwnedBy(this) || _activeDepth == 0 ||
            _activeTokenSequences[_activeDepth - 1] != token.Sequence)
            return FailResult(ServiceCycleProfileMeasurementFault.TokenRejected);

        if (!TryReadTimestamp(out var completedAtRawTicks))
            return ServiceCycleProfileMeasurementResult.Faulted;
        if (completedAtRawTicks < token.StartedAtRawTicks)
            return FailResult(ServiceCycleProfileMeasurementFault.RawClockRegressed);
        if (!TryReadAllocation(out var completedAllocatedBytes))
            return ServiceCycleProfileMeasurementResult.Faulted;
        if (completedAllocatedBytes < token.AllocatedBytes)
            return FailResult(ServiceCycleProfileMeasurementFault.AllocationCounterRegressed);
        if (!operations.TrySnapshot(out var snapshot))
            return FailResult(ServiceCycleProfileMeasurementFault.OperationCounterExhausted);

        long elapsedRawTicks;
        long allocatedBytes;
        try
        {
            elapsedRawTicks = checked(completedAtRawTicks - token.StartedAtRawTicks);
            allocatedBytes = checked(completedAllocatedBytes - token.AllocatedBytes);
        }
        catch (OverflowException)
        {
            return FailResult(ServiceCycleProfileMeasurementFault.MeasurementArithmeticExhausted);
        }

        var excludedRawTicks = _excludedChildRawTicks[_activeDepth - 1];
        var attributedRawTicks = elapsedRawTicks > excludedRawTicks
            ? elapsedRawTicks - excludedRawTicks
            : 0;
        try
        {
            var context = token.Context;
            var measurement = new ServiceCycleProfileMeasurement(
                in context,
                token.StartedAtRawTicks,
                attributedRawTicks,
                allocatedBytes,
                in snapshot);
            var result = _aggregator.Record(in measurement) switch
            {
                ServiceCycleProfileAggregationResult.Accepted => ServiceCycleProfileMeasurementResult.Accepted,
                ServiceCycleProfileAggregationResult.Sealed =>
                    FailResult(ServiceCycleProfileMeasurementFault.AggregatorSealed),
                _ => FailResult(ServiceCycleProfileMeasurementFault.AggregationFailed),
            };
            if (result == ServiceCycleProfileMeasurementResult.Accepted)
            {
                _activeDepth--;
                _activeTokenSequences[_activeDepth] = 0;
                // Overhead rises until nothing encloses it: an observation span hands up the whole
                // of itself, and any other span hands up whatever observation it contained. A fence
                // that stopped at the first parent would leave the frame's own span inflated, which
                // is the reading this exists to protect.
                if (_activeDepth != 0)
                {
                    _excludedChildRawTicks[_activeDepth - 1] +=
                        ServiceCycleProfileSpans.IsObserverOverhead(context.StageCode)
                            ? elapsedRawTicks
                            : excludedRawTicks;
                }
            }
            return result;
        }
        catch (Exception exception) when (!IsProcessFatal(exception))
        {
            return FailResult(ServiceCycleProfileMeasurementFault.AggregationFailed, exception);
        }
    }

    public ServiceCycleProfileMeasurementResult Abandon(
        in ServiceCycleProfileMeasurementToken token)
    {
        if (!IsOwnerThread())
            return FailResult(ServiceCycleProfileMeasurementFault.OwnerThreadRejected);
        if (!token.IsOwnedBy(this) || _activeDepth == 0 ||
            _activeTokenSequences[_activeDepth - 1] != token.Sequence)
            return FailResult(ServiceCycleProfileMeasurementFault.TokenRejected);
        _activeDepth--;
        _activeTokenSequences[_activeDepth] = 0;
        return Fault == ServiceCycleProfileMeasurementFault.None
            ? ServiceCycleProfileMeasurementResult.Accepted
            : ServiceCycleProfileMeasurementResult.Faulted;
    }

    internal bool Seal()
    {
        if (!IsOwnerThread()) return Fail(ServiceCycleProfileMeasurementFault.OwnerThreadRejected);
        if (Fault != ServiceCycleProfileMeasurementFault.None) return false;
        if (_activeDepth != 0)
            return Fail(ServiceCycleProfileMeasurementFault.ActiveMeasurementAtSeal);
        try
        {
            _aggregator.Seal();
            _sealed = true;
            return true;
        }
        catch (Exception exception) when (!IsProcessFatal(exception))
        {
            return Fail(ServiceCycleProfileMeasurementFault.AggregationFailed, exception);
        }
    }

    internal ServiceCycleProfileRecord GetAggregate(int groupOrdinal)
    {
        EnsurePublishable();
        return _aggregator.GetAggregate(groupOrdinal);
    }

    internal int GetSampleCount(int groupOrdinal)
    {
        EnsurePublishable();
        return _aggregator.GetSampleCount(groupOrdinal);
    }

    internal ServiceCycleProfileRecord GetSample(int groupOrdinal, int sampleOrdinal)
    {
        EnsurePublishable();
        return _aggregator.GetSample(groupOrdinal, sampleOrdinal);
    }

    private bool TryReadTimestamp(out long value)
    {
        try
        {
            value = _rawClock.ReadTimestamp();
            return true;
        }
        catch (Exception exception) when (!IsProcessFatal(exception))
        {
            value = 0;
            return Fail(ServiceCycleProfileMeasurementFault.RawClockFailed, exception);
        }
    }

    private bool TryReadAllocation(out long value)
    {
        if (!_allocationCapability.IsAvailable)
        {
            value = 0;
            return true;
        }
        try
        {
            value = _allocationCapability.ReadAllocatedBytes();
            if (value >= 0) return true;
            value = 0;
            return Fail(ServiceCycleProfileMeasurementFault.AllocationCounterRegressed);
        }
        catch (Exception exception) when (!IsProcessFatal(exception))
        {
            value = 0;
            return Fail(ServiceCycleProfileMeasurementFault.AllocationCounterFailed, exception);
        }
    }

    private bool IsOwnerThread() => Environment.CurrentManagedThreadId == _ownerThreadId;

    private void EnsurePublishable()
    {
        if (Fault != ServiceCycleProfileMeasurementFault.None)
            throw new InvalidOperationException("Faulted profile measurements cannot be published.");
    }

    private bool Fail(ServiceCycleProfileMeasurementFault fault, Exception? exception = null)
    {
        if (Interlocked.CompareExchange(
                ref _fault, (int)fault, (int)ServiceCycleProfileMeasurementFault.None) ==
            (int)ServiceCycleProfileMeasurementFault.None && exception is not null)
            Volatile.Write(ref _faultException, exception);
        return false;
    }

    private ServiceCycleProfileMeasurementResult FailResult(
        ServiceCycleProfileMeasurementFault fault,
        Exception? exception = null)
    {
        Fail(fault, exception);
        return ServiceCycleProfileMeasurementResult.Faulted;
    }

    private static bool IsProcessFatal(Exception exception) =>
        exception is StackOverflowException or OutOfMemoryException or AccessViolationException;
}
#endif
