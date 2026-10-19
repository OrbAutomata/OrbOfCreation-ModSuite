using System;
using System.Collections.Generic;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.GameMath;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.World;

namespace OrbAutomata;

internal static class AutoConceptCycleEvaluator
{
    private sealed class Candidate
    {
        internal Guid Id;
        internal string Key = string.Empty;
        internal Guid CoreTypeId;
        internal int MasteryLevel;
        internal double MasteryProgress;
        internal int MaximumQuantity;
        internal int Quantity;
        internal int QueuedQuantity;
        internal int AuthoredDrainResources;
        internal bool HasInstance;
        internal bool DrainSafe;
        internal bool CanAddNow;
        internal bool IsSettled => Quantity == QueuedQuantity;
        internal ConceptProgress Progress =>
            new(Key, MasteryLevel, MasteryProgress, MaximumQuantity > 0);
    }

    internal static WakePolicy Evaluate(
        GameWorldState world,
        in SuiteRuntimeConfiguration config,
        in ServiceCycleContext context,
        ref AutoConceptCycleState state,
        ServiceActionWriter<AutoConceptCycleAction> actions,
        out AutoConceptDecisionMetrics metrics)
    {
        metrics = default;
        if (!AutoConceptConfigurationPolicy.IsOperational(config))
            return WakePolicy.OnPublication;

        var candidates = Project(world, in config);
        SettleRetries(candidates, in context, actions);
        ReconcileReceipt(candidates, in config, in context, ref state);
        if (state.HasPendingReceipt)
        {
            var pendingActive = 0;
            var pendingOwned = 0;
            foreach (var candidate in candidates)
            {
                if (candidate.Quantity > 0) pendingActive++;
                if (state.Ownership.TryGet(candidate.Key, out var ownership) &&
                    ownership.AutomatedDelta > 0) pendingOwned++;
            }
            metrics = new AutoConceptDecisionMetrics(
                world.ConceptRecipes.Count,
                candidates.Count,
                pendingActive,
                pendingOwned,
                0,
                AutoConceptDecisionKind.Idle);
            return WakePolicy.OnPublication;
        }
        ObserveOwnership(candidates, in config, in context, ref state);
        RefreshTrainingPolicy(in config, ref state);
        InitializeTimedSessions(candidates, in config, in context, ref state);
        UpdateTrainingSessions(candidates, in config, in context, ref state);

        var active = 0;
        var owned = 0;
        foreach (var candidate in candidates)
        {
            if (candidate.Quantity > 0) active++;
            if (state.Ownership.TryGet(candidate.Key, out var ownership) &&
                ownership.AutomatedDelta > 0) owned++;
        }

        if (TryUnsafeRollback(candidates, world, in context, ref state, out var action))
            return Plan(action, candidates.Count, active, owned, AutoConceptDecisionKind.UnsafeRollback,
                actions, ref state, out metrics);

        var progress = new List<ConceptProgress>(candidates.Count);
        var byId = new Dictionary<string, Candidate>(candidates.Count, StringComparer.Ordinal);
        foreach (var candidate in candidates)
        {
            progress.Add(candidate.Progress);
            byId.Add(candidate.Key, candidate);
        }
        var ranked = AutoConceptBalancer.Rank(progress);

        if (TryPreferredReplacement(byId, world, in config, in context, ref state, out action))
            return action.RecipeId == Guid.Empty
                ? WakePolicy.OnPublication
                : Plan(action, candidates.Count, active, owned, AutoConceptDecisionKind.PreferredReplacement,
                    actions, ref state, out metrics);

        var blocked = default(AutoConceptIdle);
        if (TryBreadth(ranked, byId, world, in config, in context, ref state, ref blocked, out action))
            return Plan(action, candidates.Count, active, owned, AutoConceptDecisionKind.Breadth,
                actions, ref state, out metrics);

        if (TryRebalance(ranked, byId, world, in config, in context, ref state, ref blocked, out action))
            return Plan(action, candidates.Count, active, owned, AutoConceptDecisionKind.Rebalance,
                actions, ref state, out metrics);

        if (TryDepth(ranked, byId, world, in config, in context, ref state, ref blocked, out action))
            return Plan(action, candidates.Count, active, owned, AutoConceptDecisionKind.Depth,
                actions, ref state, out metrics);

        var idle = blocked.Reason != AutoConceptIdleReason.None
            ? blocked
            : new AutoConceptIdle(ClassifyIdle(candidates, in config, ref state));
        metrics = new AutoConceptDecisionMetrics(
            world.ConceptRecipes.Count, candidates.Count, active, owned, 0,
            AutoConceptDecisionKind.Idle, idle);
        return WakePolicy.OnPublication;
    }

    private static List<Candidate> Project(
        GameWorldState world,
        in SuiteRuntimeConfiguration config)
    {
        var result = new List<Candidate>(world.ConceptRecipes.Count);
        var rows = world.ConceptRecipes.AsSpan();
        for (var index = 0; index < rows.Length; index++)
        {
            ref readonly var concept = ref rows[index];
            var key = concept.RecipeId.ToString();
            if (!AutoConceptPlanBeliefProjection.TryCreate(
                    world,
                    concept.RecipeId,
                    out var belief,
                    out _))
                continue;
            if (!WorldLookup.TryFind(
                    world.AlchemyRecipes,
                    concept.RecipeId,
                    out var recipe))
                continue;
            var instanceFound = WorldAlchemyInstanceLookup.TryFind(
                world.AlchemyInstances, concept.RecipeId, out var instance);
            var required = recipe.RequiredExperience.ToDouble();
            var progress = required > 0
                ? Math.Clamp(recipe.MasteryXp.ToDouble() / required, 0.0, 1.0)
                : 1.0;
            result.Add(new Candidate
            {
                Id = concept.RecipeId,
                Key = key,
                CoreTypeId = belief.CoreTypeId,
                MasteryLevel = recipe.MasteryLevel,
                MasteryProgress = progress,
                MaximumQuantity = belief.MaximumQuantity,
                Quantity = belief.Quantity,
                QueuedQuantity = belief.QueuedQuantity,
                AuthoredDrainResources = belief.AuthoredDrainResources,
                HasInstance = instanceFound,
                DrainSafe = !instanceFound || IsDrainSafe(world, in instance, config.AutoConcept.MinimumDrainRatio),
                CanAddNow = concept.CanAddNow,
            });
        }
        return result;
    }

    private static bool IsDrainSafe(
        GameWorldState world,
        in WorldAlchemyInstance instance,
        float minimumRatio)
    {
        if (!instance.DrainReadable ||
            instance.DrainRatio.CompareTo(new BigDouble(Math.Max(0f, minimumRatio))) < 0)
            return false;
        if (!WorldAlchemyCostLookup.TryFindRange(
                world.AlchemyCosts, instance.RecipeId, WorldAlchemyCostKind.CurrentDrain,
                out var start, out var count))
            return true;
        for (var index = 0; index < count; index++)
        {
            var cost = world.AlchemyCosts[start + index];
            if (cost.Amount.CompareTo(default) <= 0) continue;
            if (!WorldLookup.TryFind(world.Resources, cost.ResourceId, out var resource) ||
                resource.Reading.Quantity.CompareTo(default) <= 0 ||
                resource.TrueRate.CompareTo(default) < 0)
                return false;
        }
        return true;
    }

    private static void ReconcileReceipt(
        IReadOnlyList<Candidate> candidates,
        in SuiteRuntimeConfiguration config,
        in ServiceCycleContext context,
        ref AutoConceptCycleState state)
    {
        if (!state.HasPendingReceipt) return;
        if (!state.PendingReceiptCommitted)
        {
            if (!context.PreviousReceipt.IsPresent) return;
            if (context.PreviousReceipt.CommittedCount != 1)
            {
                state.ClearPendingReceipt();
                return;
            }
            state.PendingReceiptCommitted = true;
        }

        var planned = state.PendingReceiptAction;
        Candidate? current = null;
        foreach (var candidate in candidates)
            if (candidate.Id == planned.RecipeId) { current = candidate; break; }
        if (current is null) return;

        if (planned.Kind == AutoConceptActionKind.Add)
        {
            // The native mutation changes queued quantity before quantity settles. Account for
            // that accepted queued target now so settlement cannot look like a manual edit.
            var delta = Math.Max(
                0,
                current.QueuedQuantity - planned.Belief.QueuedQuantity);
            if (delta <= 0) return;
            state.Ownership.RecordAutomatedDelta(
                current.Key,
                current.QueuedQuantity,
                delta);
            if (planned.Belief.Quantity <= 0 &&
                planned.Belief.QueuedQuantity <= 0)
                BeginTraining(current, candidates, in config, ref state);
        }
        else
        {
            var delta = Math.Max(
                0,
                planned.Belief.QueuedQuantity - current.QueuedQuantity);
            if (delta <= 0) return;
            if (planned.Kind == AutoConceptActionKind.RemoveOwned)
            {
                state.Ownership.RecordAutomatedDelta(
                    current.Key,
                    current.QueuedQuantity,
                    -delta);
            }
            else
            {
                state.Ownership.ObserveBaseline(current.Key, current.QueuedQuantity);
                state.PreferredReplacement = planned.ReplacementId;
                state.PreferredReplacementExpiresAtTicks =
                    checked(context.DecisionAt.Ticks + TimeSpan.FromSeconds(5).Ticks);
            }
        }
        state.ClearPendingReceipt();
    }

    private static void ObserveOwnership(
        IReadOnlyList<Candidate> candidates,
        in SuiteRuntimeConfiguration config,
        in ServiceCycleContext context,
        ref AutoConceptCycleState state)
    {
        if (!state.BaselineCaptured)
        {
            foreach (var candidate in candidates)
                state.Ownership.ObserveBaseline(candidate.Key, candidate.Quantity);
            state.BaselineCaptured = true;
            return;
        }
        foreach (var candidate in candidates)
        {
            if (!candidate.IsSettled) continue;
            var changed = state.Ownership.RebaselineIfUnexpected(candidate.Key, candidate.Quantity);
            if (changed && candidate.Quantity > 0 &&
                config.AutoConcept.SlotManagement == AutoConceptSlotManagementMode.TimedCycle)
                BeginTraining(candidate, candidates, in config, ref state);
        }
    }

    private static bool TryUnsafeRollback(
        IReadOnlyList<Candidate> candidates,
        GameWorldState world,
        in ServiceCycleContext context,
        ref AutoConceptCycleState state,
        out AutoConceptCycleAction action)
    {
        foreach (var candidate in candidates)
        {
            if (!state.Ownership.TryGet(candidate.Key, out var ownership) ||
                ownership.AutomatedDelta <= 0 || candidate.DrainSafe ||
                IsWaiting(in context, AutoConceptActionKind.RemoveOwned, candidate)) continue;
            action = Action(AutoConceptActionKind.RemoveOwned, candidate, ownership.AutomatedDelta,
                Guid.Empty, world.CollectedAtEpoch);
            return true;
        }
        action = default;
        return false;
    }

    private static bool TryBreadth(
        IReadOnlyList<ConceptProgress> ranked,
        IReadOnlyDictionary<string, Candidate> byId,
        GameWorldState world,
        in SuiteRuntimeConfiguration config,
        in ServiceCycleContext context,
        ref AutoConceptCycleState state,
        ref AutoConceptIdle blocked,
        out AutoConceptCycleAction action)
    {
        for (var offset = 0; offset < ranked.Count; offset++)
        {
            var index = Normalize(state.CandidateCursor + offset, ranked.Count);
            var candidate = byId[ranked[index].Uuid];
            if (!candidate.IsSettled || candidate.Quantity != 0 || !CanAdd(candidate) ||
                IsWaiting(in context, AutoConceptActionKind.Add, candidate)) continue;
            if (!TryFindSafeTarget(world, candidate, 1, in config, out var target, out var refusal, out var resource))
            {
                Block(ref blocked, refusal, AutoConceptIdleMove.Slot, candidate.Id, Guid.Empty, resource);
                continue;
            }
            action = Action(AutoConceptActionKind.Add, candidate, target, Guid.Empty, world.CollectedAtEpoch);
            return true;
        }
        action = default;
        return false;
    }

    private static bool TryRebalance(
        IReadOnlyList<ConceptProgress> ranked,
        IReadOnlyDictionary<string, Candidate> byId,
        GameWorldState world,
        in SuiteRuntimeConfiguration config,
        in ServiceCycleContext context,
        ref AutoConceptCycleState state,
        ref AutoConceptIdle blocked,
        out AutoConceptCycleAction action)
    {
        var timedCycle =
            config.AutoConcept.SlotManagement == AutoConceptSlotManagementMode.TimedCycle;
        for (var desiredOffset = 0; desiredOffset < ranked.Count; desiredOffset++)
        {
            var desiredIndex = Normalize(state.CandidateCursor + desiredOffset, ranked.Count);
            var desiredProgress = ranked[desiredIndex];
            var desired = byId[desiredProgress.Uuid];
            if (!desired.IsSettled || desired.Quantity != 0 ||
                desired.CoreTypeId == Guid.Empty || desired.MaximumQuantity <= 0 ||
                IsWaiting(in context, AutoConceptActionKind.Add, desired)) continue;
            var safe = TryFindSafeTarget(world, desired, 1, in config, out _, out var refusal, out var resource);
            if (!safe && blocked.Reason != AutoConceptIdleReason.None) continue;
            if (safe && timedCycle &&
                !IsNextTimed(desired, ranked, byId, ref state)) continue;
            if (!TryFindDisplaced(desired, desiredProgress, ranked, byId, in config, in context, ref state,
                    out var active, out var kind, out var amount)) continue;
            if (!safe)
            {
                Block(ref blocked, refusal, AutoConceptIdleMove.Rotate, desired.Id, active.Id, resource);
                continue;
            }
            action = Action(kind, active, amount, desired.Id, world.CollectedAtEpoch);
            return true;
        }
        action = default;
        return false;
    }

    private static bool TryFindDisplaced(
        Candidate desired,
        ConceptProgress desiredProgress,
        IReadOnlyList<ConceptProgress> ranked,
        IReadOnlyDictionary<string, Candidate> byId,
        in SuiteRuntimeConfiguration config,
        in ServiceCycleContext context,
        ref AutoConceptCycleState state,
        out Candidate active,
        out AutoConceptActionKind kind,
        out int amount)
    {
        var mode = config.AutoConcept.SlotManagement;
        var timedCycle = mode == AutoConceptSlotManagementMode.TimedCycle;
        for (var activeIndex = ranked.Count - 1; activeIndex >= 0; activeIndex--)
        {
            var activeProgress = ranked[activeIndex];
            if (AutoConceptBalancer.RequiresLowerMastery(mode) &&
                !AutoConceptBalancer.HasStrictlyLowerMastery(desiredProgress, activeProgress)) continue;
            active = byId[activeProgress.Uuid];
            if (!active.IsSettled || active.Quantity <= 0 ||
                (!timedCycle && active.CoreTypeId != desired.CoreTypeId) ||
                state.TrainingSessions.Contains(active.Key) ||
                IsWaiting(in context, AutoConceptActionKind.RotateOut, active)) continue;

            if (AutoConceptBalancer.UsesFullRotation(mode))
            {
                kind = AutoConceptActionKind.RotateOut;
                amount = active.Quantity;
                return true;
            }
            if (!state.Ownership.TryGet(active.Key, out var ownership) ||
                ownership.ManualBaseline != 0 || ownership.AutomatedDelta <= 0) continue;
            kind = AutoConceptActionKind.RemoveOwned;
            amount = ownership.AutomatedDelta;
            return true;
        }
        active = null!;
        kind = default;
        amount = 0;
        return false;
    }

    private static bool TryDepth(
        IReadOnlyList<ConceptProgress> ranked,
        IReadOnlyDictionary<string, Candidate> byId,
        GameWorldState world,
        in SuiteRuntimeConfiguration config,
        in ServiceCycleContext context,
        ref AutoConceptCycleState state,
        ref AutoConceptIdle blocked,
        out AutoConceptCycleAction action)
    {
        for (var offset = 0; offset < ranked.Count; offset++)
        {
            var index = Normalize(state.CandidateCursor + offset, ranked.Count);
            var candidate = byId[ranked[index].Uuid];
            if (!candidate.IsSettled || candidate.Quantity <= 0 ||
                candidate.Quantity >= candidate.MaximumQuantity ||
                IsWaiting(in context, AutoConceptActionKind.Add, candidate)) continue;
            var desired = candidate.MaximumQuantity;
            if (!TryFindSafeTarget(world, candidate, desired, in config, out var target, out var refusal, out var resource))
            {
                Block(ref blocked, refusal, AutoConceptIdleMove.Deepen, candidate.Id, Guid.Empty, resource);
                continue;
            }
            action = Action(AutoConceptActionKind.Add, candidate, target, Guid.Empty, world.CollectedAtEpoch);
            return true;
        }
        action = default;
        return false;
    }

    private static bool TryPreferredReplacement(
        IReadOnlyDictionary<string, Candidate> byId,
        GameWorldState world,
        in SuiteRuntimeConfiguration config,
        in ServiceCycleContext context,
        ref AutoConceptCycleState state,
        out AutoConceptCycleAction action)
    {
        action = default;
        if (state.PreferredReplacement == Guid.Empty) return false;
        if (context.DecisionAt.Ticks >= state.PreferredReplacementExpiresAtTicks)
        {
            state.PreferredReplacement = Guid.Empty;
            return false;
        }
        if (!byId.TryGetValue(state.PreferredReplacement.ToString(), out var candidate) ||
            candidate.Quantity > 0 ||
            IsWaiting(in context, AutoConceptActionKind.Add, candidate))
        {
            state.PreferredReplacement = Guid.Empty;
            return false;
        }
        if (!candidate.IsSettled || !CanAdd(candidate) ||
            !TryFindSafeTarget(world, candidate, 1, in config, out var target, out _, out _)) return true;
        action = Action(AutoConceptActionKind.Add, candidate, target, Guid.Empty, world.CollectedAtEpoch);
        return true;
    }

    /// <summary>
    /// Chooses the same halving ladder as the native boundary, but from the immutable publication.
    /// A configured reserve is ordinary backpressure and therefore prevents the
    /// action from being scheduled; the boundary repeats this check against live native state (M3).
    /// </summary>
    private static bool TryFindSafeTarget(
        GameWorldState world,
        Candidate candidate,
        int desiredTarget,
        in SuiteRuntimeConfiguration config,
        out int safeTarget,
        out AutoConceptIdleReason refusal,
        out Guid resource)
    {
        safeTarget = candidate.Quantity;
        refusal = AutoConceptIdleReason.None;
        resource = Guid.Empty;
        desiredTarget = Math.Min(desiredTarget, candidate.MaximumQuantity);
        if (desiredTarget <= candidate.Quantity) return false;
        if (candidate.AuthoredDrainResources == 0)
        {
            safeTarget = desiredTarget;
            return true;
        }

        var delta = desiredTarget - candidate.Quantity;
        while (delta > 0)
        {
            var target = candidate.Quantity + delta;
            refusal = ProjectedDrainRefusal(world, candidate, target, in config, out resource);
            if (refusal == AutoConceptIdleReason.None)
            {
                safeTarget = target;
                return true;
            }
            delta /= 2;
        }
        return false;
    }

    private static AutoConceptIdleReason ProjectedDrainRefusal(
        GameWorldState world,
        Candidate candidate,
        int target,
        in SuiteRuntimeConfiguration config,
        out Guid resourceId)
    {
        resourceId = Guid.Empty;
        if (!WorldAlchemyCostLookup.TryFindRange(
                world.AlchemyCosts, candidate.Id, WorldAlchemyCostKind.RecipeDrain,
                out var start, out var count))
            return AutoConceptIdleReason.DrainUnknown;

        WorldAlchemyCostLookup.TryFindRange(
            world.AlchemyCosts, candidate.Id, WorldAlchemyCostKind.CurrentDrain,
            out var currentStart, out var currentCount);

        for (var offset = 0; offset < count; offset++)
        {
            var authored = world.AlchemyCosts[start + offset];
            resourceId = authored.ResourceId;
            if (!OwnedConceptDrainMath.TryComputeCost(
                    world,
                    candidate.Id,
                    target,
                    authored.ResourceId,
                    authored.Amount,
                    out var prospectiveAmount))
                return AutoConceptIdleReason.DrainUnknown;
            var current = default(BigDouble);
            for (var currentOffset = 0; currentOffset < currentCount; currentOffset++)
            {
                var row = world.AlchemyCosts[currentStart + currentOffset];
                if (row.ResourceId != authored.ResourceId) continue;
                current = row.Amount;
                break;
            }

            var incremental = prospectiveAmount - current;
            if (incremental.CompareTo(default) <= 0) continue;
            if (!WorldLookup.TryFind(world.Resources, authored.ResourceId, out var resource))
                return AutoConceptIdleReason.DrainUnknown;
            if (resource.Reading.Quantity.CompareTo(default) <= 0)
                return AutoConceptIdleReason.ResourceAtZero;

            var quality = OrbGameMath.AsPercent(resource.Reading.Quality);
            if (quality.CompareTo(default) <= 0) return AutoConceptIdleReason.ResourceAtZero;
            if (AutoConceptResourcePolicy.BreaksRateReserve(
                    resource.TrueRate,
                    resource.Reading.Drain / quality,
                    incremental / quality,
                    config.AutoConcept.RateReservePercent))
                return AutoConceptIdleReason.RateReserve;
        }
        resourceId = Guid.Empty;
        return AutoConceptIdleReason.None;
    }

    private static void Block(
        ref AutoConceptIdle blocked,
        AutoConceptIdleReason refusal,
        AutoConceptIdleMove move,
        Guid recipe,
        Guid displaced,
        Guid resource)
    {
        if (blocked.Reason != AutoConceptIdleReason.None ||
            refusal == AutoConceptIdleReason.None) return;
        blocked = new AutoConceptIdle(refusal, move, recipe, displaced, resource);
    }

    private static AutoConceptCycleAction Action(
        AutoConceptActionKind kind,
        Candidate candidate,
        int targetOrDelta,
        Guid replacement,
        long epoch)
    {
        var belief = new AutoConceptPlanBelief(
            candidate.Quantity, candidate.QueuedQuantity, candidate.MaximumQuantity,
            candidate.CoreTypeId, candidate.AuthoredDrainResources);
        return new AutoConceptCycleAction(
            kind, candidate.Id, targetOrDelta, replacement, epoch, in belief);
    }

    private static WakePolicy Plan(
        in AutoConceptCycleAction action,
        int candidates,
        int active,
        int owned,
        AutoConceptDecisionKind kind,
        ServiceActionWriter<AutoConceptCycleAction> actions,
        ref AutoConceptCycleState state,
        out AutoConceptDecisionMetrics metrics)
    {
        actions.Add(action);
        state.RecordPlanned(in action);
        metrics = new AutoConceptDecisionMetrics(candidates, candidates, active, owned, 1, kind);
        return WakePolicy.Immediate;
    }

    private static bool IsWaiting(in ServiceCycleContext context, AutoConceptActionKind kind, Candidate candidate) =>
        context.RetryGate.IsWaiting(AutoConceptServicePolicies.RetryTarget(kind, candidate.Id), context.DecisionAt);

    /// <summary>
    /// Ends the retry episode of every Concept action the service no longer considers: a Concept the
    /// world stopped offering, an add to a Concept at its full quantity, a remove from one with no
    /// stack left.
    /// </summary>
    private static void SettleRetries(
        IReadOnlyList<Candidate> candidates,
        in ServiceCycleContext context,
        ServiceActionWriter<AutoConceptCycleAction> actions)
    {
        var gate = context.RetryGate;
        for (var index = 0; index < gate.Count; index++)
        {
            var target = gate[index].Target;
            Candidate? candidate = null;
            foreach (var item in candidates)
                if (item.Id == target.Id) { candidate = item; break; }
            if (candidate is null ||
                (AutoConceptServicePolicies.IsRemove(target)
                    ? candidate.QueuedQuantity <= 0
                    : candidate.QueuedQuantity >= candidate.MaximumQuantity))
                actions.EndRetry(target);
        }
    }

    private static bool CanAdd(Candidate candidate) =>
        candidate.CanAddNow && candidate.MaximumQuantity > candidate.QueuedQuantity;

    private static AutoConceptIdleReason ClassifyIdle(
        IReadOnlyList<Candidate> candidates,
        in SuiteRuntimeConfiguration config,
        ref AutoConceptCycleState state)
    {
        if (state.TrainingSessions.Count > 0)
            return AutoConceptIdleReason.WaitingForTraining;
        if (config.AutoConcept.SlotManagement != AutoConceptSlotManagementMode.TimedCycle)
            return AutoConceptIdleReason.None;

        var hasActive = false;
        foreach (var active in candidates)
        {
            if (!active.IsSettled || active.Quantity <= 0) continue;
            hasActive = true;
            foreach (var replacement in candidates)
            {
                if (replacement.Id != active.Id &&
                    replacement.IsSettled &&
                    replacement.Quantity == 0 &&
                    replacement.MaximumQuantity > 0)
                    return AutoConceptIdleReason.None;
            }
        }

        return hasActive
            ? AutoConceptIdleReason.NoUnlockedAssignableReplacement
            : AutoConceptIdleReason.None;
    }

    private static void RefreshTrainingPolicy(
        in SuiteRuntimeConfiguration config,
        ref AutoConceptCycleState state)
    {
        var mode = config.AutoConcept.SlotManagement;
        if (state.LastSlotMode != mode)
        {
            state.TrainingSessions.Clear();
            state.LastTimedAssignment.Clear();
            state.TimedAssignmentSequence = 0;
            state.TimedSessionsInitialized = false;
            state.LastSlotMode = mode;
        }
        state.LastTrainingPeriod = config.AutoConcept.TrainingPeriodSeconds;
    }

    private static void InitializeTimedSessions(
        IReadOnlyList<Candidate> candidates,
        in SuiteRuntimeConfiguration config,
        in ServiceCycleContext context,
        ref AutoConceptCycleState state)
    {
        if (config.AutoConcept.SlotManagement != AutoConceptSlotManagementMode.TimedCycle)
        {
            state.TimedSessionsInitialized = false;
            return;
        }
        if (state.TimedSessionsInitialized) return;
        foreach (var candidate in candidates)
            if (candidate.IsSettled && candidate.Quantity > 0 &&
                !state.TrainingSessions.Contains(candidate.Key))
                BeginTraining(candidate, candidates, in config, ref state);
        state.TimedSessionsInitialized = true;
    }

    private static void BeginTraining(
        Candidate candidate,
        IReadOnlyList<Candidate> candidates,
        in SuiteRuntimeConfiguration config,
        ref AutoConceptCycleState state)
    {
        var current = candidate.Progress;
        var target = current;
        foreach (var item in candidates)
        {
            var progress = item.Progress;
            if (!progress.Eligible || AutoConceptBalancer.HasStrictlyLowerMastery(progress, target)) continue;
            target = progress;
        }
        var timed = config.AutoConcept.SlotManagement == AutoConceptSlotManagementMode.TimedCycle;
        if (!timed && !AutoConceptBalancer.HasStrictlyLowerMastery(current, target)) return;
        state.TrainingSessions.Set(candidate.Key, new AutoConceptTrainingSession(target));
        if (timed)
            state.LastTimedAssignment.Set(candidate.Key, ++state.TimedAssignmentSequence);
    }

    private static void UpdateTrainingSessions(
        IReadOnlyList<Candidate> candidates,
        in SuiteRuntimeConfiguration config,
        in ServiceCycleContext context,
        ref AutoConceptCycleState state)
    {
        if (state.TrainingSessions.Count == 0) return;
        var byId = new Dictionary<string, Candidate>(StringComparer.Ordinal);
        foreach (var candidate in candidates) byId[candidate.Key] = candidate;
        var completed = new List<string>();
        for (var index = 0; index < state.TrainingSessions.Count; index++)
        {
            var key = state.TrainingSessions.KeyAt(index);
            var session = state.TrainingSessions.SessionAt(index);
            if (!byId.TryGetValue(key, out var candidate) ||
                candidate.Quantity <= 0 && candidate.QueuedQuantity <= 0)
            {
                completed.Add(key);
                continue;
            }
            if (!candidate.IsSettled) continue;
            session.StartedAtTicks ??= context.DecisionAt.Ticks;
            var elapsed = TimeSpan.FromTicks(context.DecisionAt.Ticks - session.StartedAtTicks.Value).TotalSeconds;
            if (AutoConceptBalancer.HasTrainingSessionCompleted(
                config.AutoConcept.SlotManagement, candidate.Progress, session.Target,
                0, elapsed, AutoConceptConfigurationPolicy.TrainingPeriodSeconds(config)))
                completed.Add(key);
        }
        foreach (var key in completed) state.TrainingSessions.Remove(key);
    }

    private static bool IsNextTimed(
        Candidate candidate,
        IReadOnlyList<ConceptProgress> ranked,
        IReadOnlyDictionary<string, Candidate> byId,
        ref AutoConceptCycleState state)
    {
        long? last = state.LastTimedAssignment.TryGet(candidate.Key, out var sequence)
            ? sequence
            : null;
        foreach (var progress in ranked)
        {
            var other = byId[progress.Uuid];
            if (!other.IsSettled || other.Quantity != 0 || other.MaximumQuantity <= 0) continue;
            long? otherLast = state.LastTimedAssignment.TryGet(other.Key, out var otherSequence)
                ? otherSequence
                : null;
            if (AutoConceptBalancer.CompareTimedCycleOrder(otherLast, other.Key, last, candidate.Key) < 0)
                return false;
        }
        return true;
    }

    private static int Normalize(int value, int count) =>
        count == 0 ? 0 : (int)((uint)value % (uint)count);
}
