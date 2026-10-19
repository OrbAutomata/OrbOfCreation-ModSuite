using System;
using System.Collections;
using System.Collections.Generic;
using OrbAutomata;
using OrbAutomata.GameMcp;
using OrbModding.Common;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using Xunit;

namespace OrbModding.ProfileTests;

/// <summary>
/// A change that landed although the game threw answers its caller a plain commit, so health is
/// where a maintainer learns it happened: one line per feature, carrying the latest log reference.
/// </summary>
[Collection(GameActionFaultLogCollection.Name)]
public sealed class GameMcpCommittedAfterThrowTests : IDisposable
{
    private const long Epoch = 91;
    private readonly IDictionary _registry = new Hashtable();
    private readonly List<string> _logged = new();

    public GameMcpCommittedAfterThrowTests()
    {
        GameActionFaultLog.ConfigureLog(_logged.Add);
        SettingsManager.ResearchQueueMode = false;
        GlobalVariables.MultiBuy = new IntVariable { Value = 1 };
        ResetSpellState();
    }

    public void Dispose()
    {
        GameActionFaultLog.ConfigureLog(null);
        GlobalVariables.MultiBuy = new IntVariable { Value = 1 };
        ResetSpellState();
    }

    [Fact]
    public void A_press_that_landed_although_the_game_threw_names_its_reference_and_the_next_replaces_it()
    {
        var target = Research();
        target.ThrowAfterAction = true;
        using var research = ResearchAction();

        var developed = Submit(research, target, ResearchActionKind.Develop);
        var first = Reference(Assert.Single(_logged));
        var afterFirst = Health();
        var paused = Submit(research, target, ResearchActionKind.Pause);
        var second = Reference(_logged[1]);
        var afterSecond = Health();

        Assert.True(developed.Outcome.Verified, developed.Outcome.Reason);
        Assert.DoesNotContain("MCP-", developed.Outcome.Reason, StringComparison.Ordinal);
        Assert.True(paused.Outcome.Verified, paused.Outcome.Reason);
        Assert.Contains(
            "\nResearch: committed, but the game threw; reference " + first + " is in the suite log",
            afterFirst,
            StringComparison.Ordinal);
        Assert.Contains(
            "\nResearch: committed, but the game threw; reference " + second + " is in the suite log",
            afterSecond,
            StringComparison.Ordinal);
        Assert.DoesNotContain(first, afterSecond, StringComparison.Ordinal);
    }

    [Fact]
    public void An_automation_commit_after_a_throw_appears_on_health_too()
    {
        var spell = Spell();
        spell.ThrowAfterLevelMutation = new NullReferenceException("injected defect after the level landed");

        var result = LevelSpell(spell);
        var reference = Reference(Assert.Single(_logged));

        Assert.Equal(ServiceActionDisposition.Committed, result.Disposition);
        Assert.Contains(
            "\nSpell level single: committed, but the game threw; reference " + reference +
            " is in the suite log",
            Health(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_press_that_committed_cleanly_adds_nothing()
    {
        var target = Research();
        using var research = ResearchAction();
        var before = Health();

        var developed = Submit(research, target, ResearchActionKind.Develop);

        Assert.True(developed.Outcome.Verified, developed.Outcome.Reason);
        Assert.Empty(_logged);
        Assert.Equal(before, Health());
        Assert.DoesNotContain("committed, but the game threw", before, StringComparison.Ordinal);
    }

    private static string Health() =>
        GameMcpHealthProjection.ProjectGameMcpHealthText(GameMcpTestHarness.Context());

    private static string Reference(string logged) =>
        System.Text.RegularExpressions.Regex.Match(logged, "MCP-[0-9A-F]{8}").Value;

    private ResearchGameAction ResearchAction()
    {
        var resolver = new TypedRegistryResolver(() => Epoch,
            () => TypedRegistrySourceSnapshot.Ready(_registry),
            value => value is ResearchSO research ? research.GetGuid() : Guid.Empty);
        return new ResearchGameAction(() => Epoch, () => true,
            static () => "ResearchLifecycle ownership was revoked.", registry: resolver);
    }

    private static ResearchSubmission Submit(
        ResearchGameAction research, ResearchSO target, ResearchActionKind kind)
    {
        var action = new ResearchAction(kind, target.GetGuid(), 1, Epoch);
        return research.Submit(in action);
    }

    private ResearchSO Research()
    {
        var target = new ResearchSO
        {
            uuid = Guid.NewGuid().ToString("D"),
            maxLevel = 4,
            available = true,
        };
        _registry.Add(target.GetGuid(), target);
        return target;
    }

    private static ServiceActionResult LevelSpell(global::SpellRecipeSO spell)
    {
        var adapter = new SpellLevelCycleActionAdapter(
            new SpellLevelNativeAdapter(), () => Epoch, () => true);
        var action = new SpellLevelCycleAction(SpellLevelActionKind.Single, Guid.Parse(spell.uuid), Epoch);
        var config = new SuiteRuntimeConfiguration
        {
            General = new SuiteGeneralConfiguration { Enabled = true },
            AutoBuy = new AutoBuyConfiguration
            {
                Mode = AutoBuyOperationMode.Active,
                AutoLevelSpells = true,
            },
        };
        return adapter.TryExecute(in action, in config, default);
    }

    private static global::SpellRecipeSO Spell()
    {
        var spell = new global::SpellRecipeSO
        {
            uuid = Guid.NewGuid().ToString(),
            masteryLevel = 1,
            discovered = true,
            readyToLevel = true,
        };
        spell.levelingPrerequisites.available = true;
        global::SpellRecipeSO.All.Add(spell);
        global::SpellManager.instance!.availableSpellRecipes.value.Add(spell);
        return spell;
    }

    private static void ResetSpellState()
    {
        global::IdScriptableObject.RuntimeLookup.Clear();
        global::SpellRecipeSO.All.Clear();
        global::UpgradeSO.All.Clear();
        global::SpellManager.instance = new global::SpellManager();
        var levelAll = new global::UpgradeSO
        {
            uuid = KnownEntities.UnlockLevelAllSpells.Uuid.ToString("D"),
            level = 0,
            available = true,
        };
        global::UpgradeSO.All.Add(levelAll);
        global::IdScriptableObject.RuntimeLookup.Add(KnownEntities.UnlockLevelAllSpells.Uuid, levelAll);
    }
}
