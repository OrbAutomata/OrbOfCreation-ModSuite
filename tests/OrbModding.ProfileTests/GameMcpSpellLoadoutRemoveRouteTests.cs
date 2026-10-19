using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using OrbAutomata;
using OrbAutomata.GameMcp;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.ProfileTests;

/// <summary>
/// A removal refused for recharging, pressed through the router into the real spell loadout
/// action, with Auto Cast on and off.
/// </summary>
/// <remarks>
/// Auto Cast re-casts a spell as soon as it can, so a removal pressed from any screen but the
/// Loadout page never found the spell at full charges, and a round flipped the breaker to get one
/// swap through. Auto Cast already casts nothing while that page is on screen; the refusal is
/// where a caller learns the way there.
/// </remarks>
[Collection(GameActionFaultLogCollection.Name)]
public sealed class GameMcpSpellLoadoutRemoveRouteTests : IDisposable
{
    private static readonly Guid InstanceId = Guid.Parse("13b37dd5-44f7-4eb5-af6b-168454578466");

    public GameMcpSpellLoadoutRemoveRouteTests()
    {
        IdScriptableObject.RuntimeLookup.Clear();
        var view = new ViewSO { available = true };
        view.SetGuid(KnownEntities.MagicSpellbookLoadout.Uuid);
        IdScriptableObject.RuntimeLookup[view.GetGuid()] = view;
        SpellManager.instance = new SpellManager();
    }

    [Fact]
    public void A_recharging_spell_under_Auto_Cast_is_refused_with_the_way_to_the_Loadout_screen()
    {
        var recipe = Equip(currentCharges: 0, maximumCharges: 1, casting: false);

        var refusal = Press(autoCastOn: true);

        Assert.Equal("refused", (string?)refusal["status"]);
        Assert.Equal("ERR_STATE", (string?)refusal["reasonCode"]);
        Assert.Equal(
            recipe + " is still recharging: it holds 0 of 1 charges, next in 31.0s. The game " +
            "only removes a spell at full charges — \"Cannot remove a spell that is still " +
            "recharging.\" — so remove it once it reads 1 of 1. Auto Cast is on and casts it " +
            "again as soon as it can, except while Magic > Spellbook > Loadout is on screen: " +
            "game_navigate(screen=\"Magic\", subtab=\"Spellbook/Loadout\"), wait there until it " +
            "reads 1 of 1 charges, then remove it.",
            (string?)refusal["reason"]);
        Assert.Equal(0, SpellManager.instance!.RemoveCalls);
    }

    [Fact]
    public void A_spell_mid_cast_under_Auto_Cast_is_refused_with_the_way_to_the_Loadout_screen()
    {
        var recipe = Equip(currentCharges: 1, maximumCharges: 1, casting: true);

        var refusal = Press(autoCastOn: true);

        Assert.Equal(
            recipe + " is still recharging: it is mid-cast, and the game answers a removal now " +
            "with \"Cannot remove a spell that is still recharging.\" Wait for the cast to " +
            "finish. Auto Cast is on and casts it again as soon as it can, except while Magic > " +
            "Spellbook > Loadout is on screen: game_navigate(screen=\"Magic\", " +
            "subtab=\"Spellbook/Loadout\"), wait there until it reads 1 of 1 charges, then " +
            "remove it.",
            (string?)refusal["reason"]);
    }

    [Fact]
    public void With_Auto_Cast_off_the_refusal_carries_no_route()
    {
        Equip(currentCharges: 0, maximumCharges: 1, casting: false);

        var reason = (string?)Press(autoCastOn: false)["reason"];

        Assert.EndsWith("so remove it once it reads 1 of 1.", reason);
    }

    private static string Equip(int currentCharges, int maximumCharges, bool casting)
    {
        var recipe = new SpellRecipeSO();
        SpellManager.instance!.activeSpells.value.Add(new Spell(recipe)
        {
            guidContainer = new GuidContainer(InstanceId),
            CurrentCharges = currentCharges,
            MaximumCharges = maximumCharges,
            CooldownRemaining = new BigDouble(31d),
            NativeCasting = casting,
        });
        return recipe.GetGuid().ToString("D");
    }

    private static JObject Press(bool autoCastOn)
    {
        var operation = GameMcpProtocolRouter.BuildOperation(
            "game_spell_loadout", new JObject { ["mode"] = "remove", ["slot"] = 1 });
        Assert.True(GameMcpCommandPreparation.TryPrepareGameMcpCommand(
            new GameMcpFrameOperation(1, operation),
            GameMcpTestHarness.Context(World(), generation: 41),
            out var command,
            out var failure), failure?.Reason);
        using var action = new SpellLoadoutGameAction(
            () => command.ExpectedLifecycleGeneration,
            () => true,
            () => string.Empty,
            name => typeof(SpellManager).Assembly.GetTypes()
                .FirstOrDefault(type => type.Name == name || type.FullName == name),
            readAutoCastOn: () => autoCastOn);
        Assert.True(action.BindingsAvailable, action.BindingFailure);

        return GameMcpTestHarness.Json(AutomataServiceCycleRuntime
            .SubmitSpellLoadout(action, command, 9, 3)
            .Project(command));
    }

    private static GameWorldState World() => new()
    {
        CollectedAtEpoch = 9,
        CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
        CollectionCategories = PublicationTable<WorldCollectionCategoryStatus>.Create(new[]
        {
            new WorldCollectionCategoryStatus(
                "spell slots", WorldCategoryOutcome.Collected, 1, 0, string.Empty),
        }),
        SpellSlots = PublicationTable<WorldSpellSlot>.Create(new[]
        {
            new WorldSpellSlot(
                0, InstanceId, Guid.NewGuid(), true, false, false, false, false, false, false,
                true, true, true, true, 1, 1, default, 4, 4, 0, 4, false, true,
                PublicationTable<WorldSpellSlotGlyph>.Empty),
        }),
    };

    public void Dispose()
    {
        IdScriptableObject.RuntimeLookup.Clear();
        SpellManager.instance = null;
    }
}
