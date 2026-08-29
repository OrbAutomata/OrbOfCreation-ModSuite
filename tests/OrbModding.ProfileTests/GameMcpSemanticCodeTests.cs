using OrbAutomata.GameMcp;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbAutomata;
using Xunit;

namespace OrbModding.ProfileTests;

public sealed class GameMcpSemanticCodeTests
{
    [Fact]
    public void Player_facing_action_codes_are_semantic()
    {
        var cases = new[]
        {
            (GameMcpCommandKind.RitualLifecycle, RitualLifecycleActionResultCodes.LevelOutOfRange, "level_out_of_range"),
            (GameMcpCommandKind.RitualLifecycle, RitualLifecycleActionResultCodes.BattleAlreadyActive, "ritual_battle_active"),
            (GameMcpCommandKind.RitualLifecycle, RitualLifecycleActionResultCodes.NoBattleActive, "no_ritual_battle_active"),
            (GameMcpCommandKind.RitualLifecycle, RitualLifecycleActionResultCodes.WrongActiveRitual, "wrong_active_ritual"),
            (GameMcpCommandKind.HarvestLifecycle, HarvestLifecycleActionResultCodes.ElementUsageUnavailable, "element_capacity_unavailable"),
            (GameMcpCommandKind.HarvestLifecycle, HarvestLifecycleActionResultCodes.ActionUnavailable, "action_not_available"),
            (GameMcpCommandKind.HarvestLifecycle, HarvestLifecycleActionResultCodes.AmountUnavailable, "amount_unavailable"),
            (GameMcpCommandKind.Harvest, PlotLifecycleActionResultCodes.ActionUnavailable, "action_not_available"),
            (GameMcpCommandKind.Harvest, PlotLifecycleActionResultCodes.QuantityUnavailable, "amount_unavailable"),

            // A target with nothing active is not an amount problem: it is the one case where
            // retrying with a smaller amount is guaranteed useless, and it had the same code as
            // three refusals a smaller amount does fix.
            (GameMcpCommandKind.Harvest, PlotLifecycleActionResultCodes.NotActive, "not_active"),
            (GameMcpCommandKind.HarvestLifecycle, HarvestLifecycleActionResultCodes.NotActive, "not_active"),
            (GameMcpCommandKind.Research, ResearchActionResultCodes.AmountUnavailable, "amount_unavailable"),
            (GameMcpCommandKind.Concept, AutoConceptActionResultCodes.AmountUnavailable, "amount_unavailable"),

            // Feature code numbers repeat across features, so a name is only right beside the kind
            // that owns it. These share 2048-2050 with spell leveling, and an empty snapshot slot
            // once answered "level_not_affordable" on a surface with no levels and no costs.
            (GameMcpCommandKind.Loadout, LoadoutActionResultCodes.SlotEmpty, "slot_empty"),
            (GameMcpCommandKind.Loadout, LoadoutActionResultCodes.SlotOutOfRange, "slot_out_of_range"),
            (GameMcpCommandKind.Loadout, LoadoutActionResultCodes.EntryUnavailable, "saved_entry_unavailable"),
            (GameMcpCommandKind.SpellLevel, SpellLevelActionResultCodes.LevelNotAffordable, "level_not_affordable"),
            (GameMcpCommandKind.SpellLevel, SpellLevelActionResultCodes.ProgressionLocked, "progression_locked"),

            // The research action answers with the same words the research row publishes.
            (GameMcpCommandKind.Research, ResearchActionResultCodes.AlreadyMaxed, "already_maxed"),
            (GameMcpCommandKind.Research, ResearchActionResultCodes.Unaffordable, "unaffordable"),
            (GameMcpCommandKind.Research, ResearchActionResultCodes.RequirementsUnmet, "requirements_unmet"),
            (GameMcpCommandKind.Research, ResearchActionResultCodes.LeewayExhausted, "research_leeway_exhausted"),
            (GameMcpCommandKind.Research, ResearchActionResultCodes.AlreadyDeveloping, "already_developing"),
        };
        foreach (var (kind, code, expected) in cases)
        {
            var action = ServiceActionResult.Rejected(code);
            var result = GameMcpCommandResult.FromAction(in action, kind, 1, 1);

            Assert.Equal(expected, result.Code);
            Assert.DoesNotContain("feature_", result.Code, System.StringComparison.Ordinal);
        }
    }
}
