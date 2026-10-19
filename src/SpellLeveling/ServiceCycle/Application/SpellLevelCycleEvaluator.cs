using System;
using OrbModding.Common;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.World;

namespace OrbAutomata;

/// <summary>
/// The pure Spell Leveling worker policy: given the pinned world and the pinned configuration it
/// plans at most one mastery-level purchase and waits for the next publication. It owns no state
/// between cycles — no capability latch, no retry counter, no backoff — because a fresh world every
/// generation is the only input it has ever needed.
/// </summary>
/// <remarks>
/// <para>
/// Discovery, mastery readiness, and current affordability are published facts and are tested here.
/// Leveling prerequisites remain boundary-only, while readiness and affordability are re-read live
/// immediately before mutation. This follows the game-boundary doctrine's capture-then-decide rule:
/// an already-known wait is quiet planning backpressure, while the boundary remains the authority on
/// facts that can move after publication.
/// </para>
/// <para>
/// Ranking is lowest mastery level first, so the spell furthest behind catches up rather than the
/// strongest running away. Ties break on identity order, which is the order every published table is
/// already sorted in. The rule is total and reproducible, and a tie only decides which of two equally
/// ranked spells goes this cycle — the other goes next.
/// </para>
/// </remarks>
internal static class SpellLevelCycleEvaluator
{
    public static WakePolicy Evaluate(
        GameWorldState world,
        in SuiteRuntimeConfiguration config,
        ServiceActionWriter<SpellLevelCycleAction> actions,
        out SpellLevelDecisionMetrics metrics)
    {
        var wake = WakePolicy.OnPublication;
        var spells = world.SpellRecipes;
        metrics = new SpellLevelDecisionMetrics(
            spells.Count,
            readySpells: 0,
            plannedActions: 0,
            AutoSpellLevelCapability.Single);

        // A disabled service plans nothing but still reschedules, so it resumes the moment the
        // operator turns Auto Buy or its spell-leveling switch back on.
        if (!SpellLevelConfigurationPolicy.IsOperational(config)) return wake;

        var capability = ReadCapability(world);

        var undiscovered = 0;
        var notReady = 0;
        var unaffordable = 0;
        var ready = 0;
        var affordableReady = 0;
        var chosen = default(WorldSpellRecipe);
        var hasChoice = false;

        var rows = spells.AsSpan();
        for (var index = 0; index < rows.Length; index++)
        {
            ref readonly var spell = ref rows[index];
            if (!spell.Discovered)
            {
                undiscovered++;
                continue;
            }

            if (!spell.MasteryLevelReady)
            {
                notReady++;
                continue;
            }

            ready++;
            if (!spell.MasteryLevelAffordable)
            {
                unaffordable++;
                continue;
            }

            affordableReady++;
            if (!hasChoice || Outranks(in spell, in chosen))
            {
                chosen = spell;
                hasChoice = true;
            }
        }

        var histogram = new SpellLevelExclusionHistogram(
            undiscovered,
            notReady,
            unaffordable,
            outranked: affordableReady == 0 ? 0 : affordableReady - 1);

        if (!hasChoice)
        {
            metrics = new SpellLevelDecisionMetrics(
                spells.Count, ready, plannedActions: 0, capability, in histogram);
            return wake;
        }

        // One action per cycle, either way. `All` is not "one action per ready spell": the native
        // batch levels every ready spell in a single call, and the identity it carries is only what
        // the mutation evidence is filed under.
        var kind = capability == AutoSpellLevelCapability.All
            ? SpellLevelActionKind.All
            : SpellLevelActionKind.Single;
        actions.Add(new SpellLevelCycleAction(
            kind,
            chosen.SpellRecipeId,
            world.CollectedAtEpoch));

        metrics = new SpellLevelDecisionMetrics(
            spells.Count, ready, plannedActions: 1, capability, in histogram);
        return wake;
    }

    /// <summary>
    /// What the snapshot says this cycle could do. Never <see cref="AutoSpellLevelCapability.Locked"/>:
    /// that answer needs the leveling prerequisite, which is a boundary fact.
    /// </summary>
    internal static AutoSpellLevelCapability ReadCapability(GameWorldState world) =>
        ReadLevelAllUpgradeLevel(world) > 0
            ? AutoSpellLevelCapability.All
            : AutoSpellLevelCapability.Single;

    /// <summary>
    /// Committed levels only. A level that is bought and still developing does not grant the batch —
    /// the game has not applied the upgrade yet, and treating queued as owned would fire a native call
    /// the game refuses.
    /// </summary>
    private static int ReadLevelAllUpgradeLevel(GameWorldState world) =>
        WorldLookup.TryFind(world.Upgrades, KnownEntities.UnlockLevelAllSpells.Uuid, out var upgrade)
            ? upgrade.Reading.Level
            : 0;

    private static bool Outranks(in WorldSpellRecipe candidate, in WorldSpellRecipe incumbent)
    {
        var level = candidate.MasteryLevel.CompareTo(incumbent.MasteryLevel);
        if (level != 0) return level < 0;
        return candidate.SpellRecipeId.CompareTo(incumbent.SpellRecipeId) < 0;
    }
}
