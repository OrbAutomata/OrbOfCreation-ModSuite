#if SERVICE_CYCLE_PROFILE
namespace OrbModding.Common.Runtime.ServiceCycle.Observation.Profile;

/// <summary>
/// Every measured span in the suite, in one enumeration.
/// </summary>
/// <remarks>
/// <para>
/// The numbers are wire values: a profile record carries the span id and nothing else that names
/// the span, so an artifact recorded by an older build must keep decoding as the same measurement.
/// </para>
/// <para>
/// Collecting them here is what makes the ids globally enumerated rather than globally unique by
/// agreement: a duplicate is a compile error, and <see cref="ServiceCycleProfileSpans"/> can answer
/// questions about the set as a whole.
/// </para>
/// </remarks>
internal enum ServiceCycleProfileSpan
{
    // 1 DetachedInputConstruction and 2 DetachedInputBridgePublication are burned. They went with the
    // replay input bridge: nothing constructs a detached cycle input or publishes one into a
    // worker-side recording bridge any more, so there is no main-thread cost left to attribute.
    SemanticStart = 3,
    SemanticTerminal = 4,
    SemanticPumpSummary = 5,
    OverallPump = 6,
    AcquireResponses = 7,
    DispatchActions = 8,
    StartCycles = 9,
    ReconcileLifecycle = 10,

    AutoHarvestBindingAndCoherence = 1001,

    // 1002-1006 are burned.
    //
    //   1002 ActiveActionTraversal              1005 FrameAssemblyAndOwnershipProjection
    //   1003 FruitFactCapture                   1006 ActionFactRevalidation
    //   1004 TreasureFactCapture
    //
    // 1002 went with the capture-side traversal it timed. 1003-1005 went when the projection they
    // timed moved to the worker, which cannot hold the profile probe: a worker definition may not
    // hold runtime-owned storage. They were attributing main-thread cost, and there is none left to
    // attribute — the runtime times the whole evaluation. See W51.
    //
    // 1006 went when the boundary stopped re-deriving the pair's facts off the live game and began
    // deciding on the ones the action carries. What is left at that point in the action is resolving
    // the instance to submit into, which is a different measurement over a third of the calls — hence
    // 1011 rather than a narrower 1006.
    AutoHarvestActionBeforeSnapshot = 1007,
    AutoHarvestActionNativeSubmission = 1008,
    AutoHarvestActionAfterSnapshot = 1009,
    AutoHarvestActionPostconditionVerification = 1010,
    AutoHarvestActionPrototypeResolution = 1011,

    // 2001-2003 and 2008-2014 are burned.
    //
    //   2001 CaptureGlobals                     2008 CandidateAvailabilityAdmission
    //   2002 CaptureStructureScan               2009 CandidateLevelReads
    //   2003 CaptureUpgradeScan                 2010 CandidateCostInvoke
    //   2012 CandidatePriorityClassify          2011 CandidateCostDecode
    //   2013 CandidateResourceStateCapture
    //   2014 CandidateCostLookup
    //
    // The right-hand four went when levels and prices started arriving from the shared snapshot and
    // the game's CanPurchase() moved to the action boundary (W39). The left-hand six went when the
    // projection they timed moved to the worker, for the same reason Auto Harvest's did. See W50.
    AutoBuyActionQueueRoomRead = 2004,
    AutoBuyActionCandidateResolution = 2005,
    AutoBuyActionAdmissionRevalidation = 2006,
    AutoBuyActionNativeSubmission = 2007,
}
#endif
