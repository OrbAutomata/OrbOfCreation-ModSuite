using System;
using System.Collections;
using System.Diagnostics;
using System.Reflection;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.GameMath;
using OrbModding.Common.Runtime.Verification;
using OrbModding.Common.Runtime.World;

namespace OrbAutomata;

/// <summary>
/// The player-facing trigger for verification: click the Runtime action, the suite checks itself against the
/// running game, and reports one verdict per thing checked.
/// </summary>
/// <remarks>
/// <para>
/// Requested explicitly from the Mods Runtime page rather than polled from a keyboard shortcut,
/// because it is a rare, deliberately frame-stalling diagnostic action rather than runtime policy.
/// </para>
/// <para>
/// Passes still report separately — "cost passed, rate failed" is immediately actionable where a
/// single combined verdict would not be — but they all run to completion inside the frame the action was
/// pressed in. Spreading the work across ticks was the earlier design and was wrong for a manual
/// diagnostic twice over: it left every pass reading a different frame's game state, and it hid the
/// run. <b>The stall is the acknowledgement.</b> A player who clicks the action and sees the game hitch
/// knows it happened, without needing to go and read a log to find out.
/// </para>
/// <para>
/// Nothing here is bounded any more, because nothing here needs to be: the entity budget existed to
/// cap per-frame cost, and there is now exactly one frame. Every entity in every registry is checked.
/// </para>
/// <para>
/// Earlier keyboard defaults either raced Mentor or held native gameplay modifiers. The permanent
/// Runtime-page action removes that input surface entirely.
/// </para>
/// </remarks>
internal sealed class AutomataDifferentialVerificationControl : IDifferentialVerificationControl
{
    private readonly Action<string> _report;
    private readonly Action? _runOverride;
    private bool _runRequested;
    private long _revision;

    internal AutomataDifferentialVerificationControl(
        Action<string> report,
        Action? runOverride = null)
    {
        _report = report ?? throw new ArgumentNullException(nameof(report));
        _runOverride = runOverride;
    }

    public bool RunRequested => _runRequested;

    public long Revision => _revision;

    public bool RequestRun()
    {
        if (_runRequested) return false;
        _runRequested = true;
        _revision = checked(_revision + 1);
        return true;
    }

    /// <summary>Runs a requested diagnostic in one frame on the Unity main thread.</summary>
    internal void Tick()
    {
        if (!_runRequested) return;
        _runRequested = false;
        _revision = checked(_revision + 1);
        if (_runOverride is not null)
        {
            _runOverride();
            return;
        }
        RunEverything();
    }

    private void RunEverything()
    {
        _report("Verification started. Everything runs in this frame, so the game will hitch.");

        var whole = Stopwatch.StartNew();

        // Order matters, and only in this direction. The world check is a pure reader, while both
        // ported-math passes deliberately settle dirty flags first so that the two sides compare the
        // same inputs — which leaves every record they touched freshly recalculated. Running the
        // check afterwards would have it survey a cache the verifier had just warmed, and report a
        // staleness figure that says more about the verifier than about the game.
        RunWorldCollectionCheck();
        RunPass(new ConceptDrainPass());
        RunPass(new SpellLevelPass(compareAffordability: false));
        RunPass(new SpellLevelPass(compareAffordability: true));
        RunPass(new CostPass());
        RunPass(new RatePass());
        RunPass(new RequirementPass(
            "Upgrade requirement", "UpgradeSO", RequirementOwnerShape.UpgradeQueuedLevel));
        RunPass(new RequirementPass(
            "Structure requirement", "StructureSO", RequirementOwnerShape.StructureQuantity));
        RunPass(new RequirementPass(
            "Research requirement", "ResearchSO", RequirementOwnerShape.ResearchRequirementLevel));
        RunPass(new RequirementPass(
            "Prerequisite link tier",
            "PrerequisiteLinkSO",
            RequirementOwnerShape.PrerequisiteLinkTier));
        RunPass(new UsagePrerequisitePass());

        whole.Stop();
        _report($"Verification finished in {whole.Elapsed.TotalMilliseconds:0.###} ms.");
    }

    /// <summary>Runs one ported-math pass over every entity it can reach, then reports its verdict.</summary>
    private void RunPass(IVerificationPass pass)
    {
        if (!pass.TryBegin(out var entities, out var failure))
        {
            _report($"{pass.Subject} verification unavailable: {failure}");
            return;
        }

        // One tick, no entity ceiling: the budgets existed to spread work across frames, and there is
        // no longer anything to spread it across.
        var session = new DifferentialVerificationSession(
            pass.Subject, tickBudget: 1, entityBudget: int.MaxValue);
        session.Start();

        for (var index = 0; index < entities.Count; index++)
        {
            var entity = entities[index];
            if (entity is null)
            {
                session.RecordUnverifiable("a registry entry was null");
                continue;
            }

            if (pass.TryVerify(entity, session.Run, session, out var entityFailure))
            {
                session.RecordVerified();
            }
            else
            {
                session.RecordUnverifiable(entityFailure);
            }
        }

        session.EndTick();
        _report(session.Complete());
    }

    /// <summary>
    /// Checks world collection itself — binding, traversal, identity, edges, accessor parity, and
    /// cache warmth — against the live game. Reports several lines rather than one verdict, because
    /// the answers are measurements as much as they are pass or fail.
    /// </summary>
    private void RunWorldCollectionCheck()
    {
        try
        {
            foreach (var line in new AutomataWorldCollectionCheck().Run()) _report(line);
        }
        catch (Exception ex)
        {
            // A throw here is itself the finding — the collector reached something on a live object
            // that no stub reproduces — so it is reported rather than allowed to take down the frame
            // the player is standing in.
            _report($"World collection check threw: {ex.GetBaseException().Message}");
        }
    }

    /// <summary>One ported chain to check, its entity source, and how to compare a single entity.</summary>
    private interface IVerificationPass
    {
        string Subject { get; }

        bool TryBegin(out IList entities, out string failure);

        bool TryVerify(
            object entity,
            DifferentialRun run,
            DifferentialVerificationSession session,
            out string failure);
    }

    private sealed class CostPass : IVerificationPass
    {
        private AutomataCostVerifier? _verifier;

        public string Subject => "Cost";

        public bool TryBegin(out IList entities, out string failure)
        {
            entities = Array.Empty<object>();

            var structureType = FindType("StructureSO");
            if (structureType is null)
            {
                failure = "the StructureSO type could not be resolved.";
                return false;
            }

            _verifier = new AutomataCostVerifier(structureType);
            if (!_verifier.IsAvailable)
            {
                failure = "this build does not expose the expected cost contract.";
                return false;
            }

            var all = ReadStaticList(structureType, "All");
            if (all is null || all.Count == 0)
            {
                failure = "no structures were available. Load a save first.";
                return false;
            }

            entities = all;
            failure = string.Empty;
            return true;
        }

        public bool TryVerify(
            object entity,
            DifferentialRun run,
            DifferentialVerificationSession session,
            out string failure)
        {
            if (_verifier is null)
            {
                failure = "the cost verifier was not started.";
                return false;
            }

            return _verifier.TryVerify(entity, run, session, out failure);
        }
    }

    private sealed class RatePass : IVerificationPass
    {
        private AutomataRateVerifier? _verifier;

        public string Subject => "Rate";

        public bool TryBegin(out IList entities, out string failure)
        {
            entities = Array.Empty<object>();

            var resourceType = FindType("ResourceSO");
            if (resourceType is null)
            {
                failure = "the ResourceSO type could not be resolved.";
                return false;
            }

            _verifier = new AutomataRateVerifier(resourceType, FindType("Player"));
            if (!_verifier.IsAvailable)
            {
                failure = "this build does not expose the expected rate contract.";
                return false;
            }

            var all = ReadStaticList(resourceType, "All");
            if (all is null || all.Count == 0)
            {
                failure = "no resources were available. Load a save first.";
                return false;
            }

            entities = all;
            failure = string.Empty;
            return true;
        }

        public bool TryVerify(
            object entity,
            DifferentialRun run,
            DifferentialVerificationSession session,
            out string failure)
        {
            if (_verifier is null)
            {
                failure = "the rate verifier was not started.";
                return false;
            }

            return _verifier.TryVerify(entity, run, session, out failure);
        }
    }

    private sealed class ConceptDrainPass : IVerificationPass
    {
        private AutomataConceptDrainVerifier? _verifier;
        private GameWorldState? _world;

        public string Subject => "Concept drain";

        public bool TryBegin(out IList entities, out string failure)
        {
            entities = Array.Empty<object>();
            var recipeType = FindType("AlchemyRecipeSO");
            var instanceType = FindType("AlchemyInstance");
            if (recipeType is null || instanceType is null)
            {
                failure = "the AlchemyRecipeSO or AlchemyInstance type could not be resolved.";
                return false;
            }
            _verifier = new AutomataConceptDrainVerifier(recipeType, instanceType);
            if (!_verifier.IsAvailable)
            {
                failure = "this build does not expose the expected Concept drain oracle.";
                return false;
            }
            var all = ReadStaticList(recipeType, "All");
            if (all is null || all.Count == 0)
            {
                failure = "no Concept recipes were available. Load a save first.";
                return false;
            }
            var collector = new GameWorldCollector();
            collector.Collect();
            _world = collector.Build();
            entities = all;
            failure = string.Empty;
            return true;
        }

        public bool TryVerify(
            object entity,
            DifferentialRun run,
            DifferentialVerificationSession session,
            out string failure)
        {
            if (_verifier is null || _world is null)
            {
                failure = "the Concept drain verifier was not started.";
                return false;
            }
            return _verifier.TryVerify(entity, _world, run, session, out failure);
        }
    }

    private sealed class SpellLevelPass : IVerificationPass
    {
        private readonly bool _compareAffordability;
        private AutomataSpellLevelVerifier? _verifier;
        private GameWorldState? _world;

        internal SpellLevelPass(bool compareAffordability) =>
            _compareAffordability = compareAffordability;

        public string Subject => _compareAffordability
            ? "Spell level affordability"
            : "Spell level cost";

        public bool TryBegin(out IList entities, out string failure)
        {
            entities = Array.Empty<object>();
            var spellType = FindType("SpellRecipeSO");
            var costListType = FindType("ResourceCostList");
            if (spellType is null || costListType is null)
            {
                failure = "the SpellRecipeSO or ResourceCostList type could not be resolved.";
                return false;
            }
            _verifier = new AutomataSpellLevelVerifier(spellType, costListType);
            if (!_verifier.IsAvailable)
            {
                failure = "this build does not expose the expected spell level oracle.";
                return false;
            }
            var all = ReadStaticList(spellType, "All");
            if (all is null || all.Count == 0)
            {
                failure = "no spell recipes were available. Load a save first.";
                return false;
            }
            var collector = new GameWorldCollector();
            collector.Collect();
            _world = collector.Build();
            entities = all;
            failure = string.Empty;
            return true;
        }

        public bool TryVerify(
            object entity,
            DifferentialRun run,
            DifferentialVerificationSession session,
            out string failure)
        {
            if (_verifier is null || _world is null)
            {
                failure = "the spell level verifier was not started.";
                return false;
            }
            return _compareAffordability
                ? _verifier.TryVerifyAffordability(entity, _world, run, session, out failure)
                : _verifier.TryVerifyCost(entity, _world, run, session, out failure);
        }
    }

    /// <summary>
    /// Checks the suite's own answer to "may this be bought at its next level" against the game's, for
    /// one kind of owner.
    /// </summary>
    /// <remarks>
    /// One pass per owner kind rather than one combined, because each kind is checked at a level of
    /// its own and a combined verdict would hide which of them disagreed — and because the level
    /// expressions are the likeliest thing to be wrong.
    /// <para>
    /// Its own collector, deliberately. The requirement rows are read once per lifecycle epoch, so a
    /// collector that has already run would skip the read this pass exists to check; a fresh one reads
    /// them for the first time, which is the state a real session's first cycle is in.
    /// </para>
    /// </remarks>
    private sealed class RequirementPass : IVerificationPass
    {
        private readonly string _typeName;
        private readonly RequirementOwnerShape _shape;
        private AutomataRequirementVerifier? _verifier;
        private GameWorldState? _world;

        internal RequirementPass(string subject, string typeName, RequirementOwnerShape shape)
        {
            Subject = subject;
            _typeName = typeName;
            _shape = shape;
        }

        public string Subject { get; }

        public bool TryBegin(out IList entities, out string failure)
        {
            entities = Array.Empty<object>();

            var ownerType = FindType(_typeName);
            if (ownerType is null)
            {
                failure = $"the {_typeName} type could not be resolved.";
                return false;
            }

            _verifier = new AutomataRequirementVerifier(ownerType, _shape);
            if (!_verifier.IsAvailable)
            {
                failure = "this build does not expose the expected prerequisite contract.";
                return false;
            }

            var all = ReadStaticList(ownerType, "All");
            if (all is null || all.Count == 0)
            {
                failure = $"no {_typeName} entities were available. Load a save first.";
                return false;
            }

            var collector = new GameWorldCollector();
            collector.Collect();
            _world = collector.Build();

            entities = all;
            failure = string.Empty;
            return true;
        }

        public bool TryVerify(
            object entity,
            DifferentialRun run,
            DifferentialVerificationSession session,
            out string failure)
        {
            if (_verifier is null || _world is null)
            {
                failure = "the requirement verifier was not started.";
                return false;
            }

            return _verifier.TryVerify(entity, _world, run, session, out failure);
        }
    }

    /// <summary>Checks every captured Concept usage-prerequisite program against its native answer.</summary>
    private sealed class UsagePrerequisitePass : IVerificationPass
    {
        private AutomataUsagePrerequisiteVerifier? _verifier;
        private GameWorldState? _world;

        public string Subject => "Concept usage prerequisite";

        public bool TryBegin(out IList entities, out string failure)
        {
            entities = Array.Empty<object>();
            var ownerType = FindType("AlchemyRecipeSO");
            if (ownerType is null)
            {
                failure = "the AlchemyRecipeSO type could not be resolved.";
                return false;
            }

            _verifier = new AutomataUsagePrerequisiteVerifier(ownerType);
            if (!_verifier.IsAvailable)
            {
                failure = "this build does not expose the expected usage-prerequisite oracle.";
                return false;
            }

            var all = ReadStaticList(ownerType, "All");
            if (all is null || all.Count == 0)
            {
                failure = "no AlchemyRecipeSO entities were available. Load a save first.";
                return false;
            }

            var collector = new GameWorldCollector();
            collector.Collect();
            _world = collector.Build();
            entities = all;
            failure = string.Empty;
            return true;
        }

        public bool TryVerify(
            object entity,
            DifferentialRun run,
            DifferentialVerificationSession session,
            out string failure)
        {
            if (_verifier is null || _world is null)
            {
                failure = "the usage-prerequisite verifier was not started.";
                return false;
            }

            return _verifier.TryVerify(entity, _world, run, out failure);
        }
    }

    private static Type? FindType(string name)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            var candidate = assembly.GetType(name, throwOnError: false);
            if (candidate is not null) return candidate;
        }

        return null;
    }

    /// <summary>
    /// Reads a public static list member, the same discovery mechanism Auto Buy already uses for
    /// candidate enumeration.
    /// </summary>
    private static IList? ReadStaticList(Type type, string memberName)
    {
        const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        var value = type.GetField(memberName, Static)?.GetValue(null) ??
            type.GetProperty(memberName, Static)?.GetValue(null, null);
        return value as IList;
    }
}
