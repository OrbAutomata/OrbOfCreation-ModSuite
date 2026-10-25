using System;
using System.Collections.Generic;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Profile;
using Xunit;

namespace OrbModding.ProfileTests;

public sealed class ServiceCycleProfileSpanTests
{
    /// <summary>
    /// The whole enumeration, written out by hand.
    /// </summary>
    private static readonly (ServiceCycleProfileSpan Span, int Code)[] Expected =
    {
        (ServiceCycleProfileSpan.SemanticStart, 3),
        (ServiceCycleProfileSpan.SemanticTerminal, 4),
        (ServiceCycleProfileSpan.SemanticPumpSummary, 5),
        (ServiceCycleProfileSpan.OverallPump, 6),
        (ServiceCycleProfileSpan.AcquireResponses, 7),
        (ServiceCycleProfileSpan.DispatchActions, 8),
        (ServiceCycleProfileSpan.StartCycles, 9),
        (ServiceCycleProfileSpan.ReconcileLifecycle, 10),
        (ServiceCycleProfileSpan.AutoHarvestBindingAndCoherence, 1001),
        (ServiceCycleProfileSpan.AutoHarvestActionBeforeSnapshot, 1007),
        (ServiceCycleProfileSpan.AutoHarvestActionNativeSubmission, 1008),
        (ServiceCycleProfileSpan.AutoHarvestActionAfterSnapshot, 1009),
        (ServiceCycleProfileSpan.AutoHarvestActionPostconditionVerification, 1010),
        (ServiceCycleProfileSpan.AutoHarvestActionPrototypeResolution, 1011),
        (ServiceCycleProfileSpan.AutoBuyActionQueueRoomRead, 2004),
        (ServiceCycleProfileSpan.AutoBuyActionCandidateResolution, 2005),
        (ServiceCycleProfileSpan.AutoBuyActionAdmissionRevalidation, 2006),
        (ServiceCycleProfileSpan.AutoBuyActionNativeSubmission, 2007),
    };

    private static readonly int[] Burned =
    {
        1, 2,
        1002, 1003, 1004, 1005, 1006,
        2001, 2002, 2003, 2008, 2009, 2010, 2011, 2012, 2013, 2014,
    };

    [Fact]
    public void TheEnumerationIsExactlyTheSpansTheSuiteMeasures()
    {
        var declared = ServiceCycleProfileSpans.All();

        Assert.Equal(Expected.Length, declared.Length);
        for (var index = 0; index < Expected.Length; index++)
        {
            Assert.Equal(Expected[index].Span, declared[index]);
            Assert.Equal(Expected[index].Code, (int)declared[index]);
        }
    }

    [Fact]
    public void NoTwoSpansShareANumberOrAName()
    {
        var codes = new HashSet<int>();
        var names = new HashSet<string>(StringComparer.Ordinal);

        foreach (var span in ServiceCycleProfileSpans.All())
        {
            Assert.True(codes.Add((int)span), "Span number reused by " + span + ".");
            Assert.True(names.Add(ServiceCycleProfileSpans.Name((int)span)), "Span name reused by " + span + ".");
        }
    }

    [Fact]
    public void ABurnedNumberDecodesAsItselfRatherThanAsALiveSpan()
    {
        var live = new HashSet<int>();
        foreach (var span in ServiceCycleProfileSpans.All()) live.Add((int)span);

        foreach (var burned in Burned)
        {
            Assert.DoesNotContain(burned, live);
            Assert.Equal("Stage " + burned, ServiceCycleProfileSpans.Name(burned));
        }
    }

    [Fact]
    public void OnlyTheSemanticEmissionSpansAreFencedOutOfWhatEnclosesThem()
    {
        foreach (var span in ServiceCycleProfileSpans.All())
        {
            var expected = span is ServiceCycleProfileSpan.SemanticStart or
                ServiceCycleProfileSpan.SemanticTerminal or
                ServiceCycleProfileSpan.SemanticPumpSummary;
            Assert.Equal(expected, ServiceCycleProfileSpans.IsObserverOverhead((int)span));
        }
    }
}
