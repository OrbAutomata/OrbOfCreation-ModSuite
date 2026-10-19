using System;
using System.Collections.Generic;
using OrbMentor;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.Tests.Services.Mentor.Runtime.ServiceCycle;

public sealed class MentorCycleEvaluatorTests
{
    private static readonly Guid Source = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Lower = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Peer = Guid.Parse("33333333-3333-3333-3333-333333333333");

    [Fact]
    public void EquippedSpellSharesWithEveryLowerDiscoveredRecipient()
    {
        var world = BaseWorld() with
        {
            SpellRecipes = Table(Spell(Source, 5), Spell(Lower, 2), Spell(Peer, 5)),
            SpellSlots = Table(Slot(Source)),
            MasteryExperience = Table(Input(1, MasteryExperienceDomain.Spell, Source, 5, 100)),
        };
        var state = MentorCycleState.Create(new LifecycleGeneration(1));

        var actions = Plan(world, Config(), ref state);

        var action = Assert.Single(actions);
        Assert.Equal(Lower, action.RecipientId);
        Assert.Equal(new MentorAmount(1, 1), action.Amount);
        Assert.Equal(5, action.MasteryCeilingExclusive);
    }

    [Fact]
    public void SharedPoolDividesTheConfiguredBonusAcrossRecipients()
    {
        var third = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var world = BaseWorld() with
        {
            SpellRecipes = Table(Spell(Source, 5), Spell(Lower, 2), Spell(third, 1)),
            SpellSlots = Table(Slot(Source)),
            MasteryExperience = Table(Input(1, MasteryExperienceDomain.Spell, Source, 5, 100)),
        };
        var state = MentorCycleState.Create(new LifecycleGeneration(1));

        var actions = Plan(world, Config(economy: MentorEconomyMode.SharedPool), ref state);

        Assert.Equal(2, actions.Count);
        Assert.All(actions, action => Assert.Equal(new MentorAmount(5, 0), action.Amount));
    }

    [Fact]
    public void HighestDiscoveredRejectsAFormerLeaderAfterAnotherSpellPassesIt()
    {
        var world = BaseWorld() with
        {
            SpellRecipes = Table(Spell(Source, 5), Spell(Lower, 2), Spell(Peer, 6)),
            MasteryExperience = Table(Input(1, MasteryExperienceDomain.Spell, Source, 5, 100)),
        };
        var state = MentorCycleState.Create(new LifecycleGeneration(1));

        var actions = Plan(
            world,
            Config(spellPolicy: MentorSpellSourcePolicy.HighestDiscovered),
            ref state);

        Assert.Empty(actions);
    }

    [Fact]
    public void ArtifactSharingIsOptInAndUsesTheHighestCreatedSource()
    {
        var world = BaseWorld(MasteryExperienceDomain.Artifact) with
        {
            Equipment = Table(Equipment(Source, 5), Equipment(Lower, 2)),
            MasteryExperience = Table(Input(1, MasteryExperienceDomain.Artifact, Source, 5, 100)),
        };
        var state = MentorCycleState.Create(new LifecycleGeneration(1));

        Assert.Empty(Plan(world, Config(), ref state));
        world = world with
        {
            MasteryExperience = Table(Input(2, MasteryExperienceDomain.Artifact, Source, 5, 100)),
        };
        Assert.Single(Plan(world, Config(artifacts: true), ref state));
    }

    [Fact]
    public void AlchemyExcludesConceptMembersEvenWhenTheirTypeLooksOrdinary()
    {
        var world = BaseWorld(MasteryExperienceDomain.Alchemy) with
        {
            AlchemyRecipes = Table(
                Alchemy(Source, 5, AlchemyGameplayDomainClassifier.AlchemyTypeUuid),
                Alchemy(Lower, 2, AlchemyGameplayDomainClassifier.BrewingTypeUuid),
                Alchemy(Peer, 1, AlchemyGameplayDomainClassifier.AlchemyTypeUuid)),
            ConceptRecipes = Table(new WorldConceptRecipe(
                Peer, AlchemyGameplayDomainClassifier.ReductiveConceptTypeUuid)),
            MasteryExperience = Table(Input(1, MasteryExperienceDomain.Alchemy, Source, 5, 100)),
        };
        var state = MentorCycleState.Create(new LifecycleGeneration(1));

        var action = Assert.Single(Plan(world, Config(alchemy: true), ref state));

        Assert.Equal(Lower, action.RecipientId);
    }

    [Fact]
    public void ARepeatedWorldProcessesEachSequenceOnlyOnceAndAccountsForAGap()
    {
        var world = BaseWorld() with
        {
            SpellRecipes = Table(Spell(Source, 5), Spell(Lower, 2)),
            SpellSlots = Table(Slot(Source)),
            MasteryExperience = Table(Input(3, MasteryExperienceDomain.Spell, Source, 5, 100)),
        };
        var state = MentorCycleState.Create(new LifecycleGeneration(1));

        Assert.Single(Plan(world, Config(), ref state));
        Assert.Empty(Plan(world, Config(), ref state));
        Assert.Equal(2, state.TotalMissedInputs);
    }

    [Fact]
    public void ASecondInputWaitsForANewerWorldEvenWhenTheFirstPlansNoAction()
    {
        var world = BaseWorld() with
        {
            MasteryExperience = Table(
                Input(1, MasteryExperienceDomain.Spell, Source, 5, 100),
                Input(2, MasteryExperienceDomain.Spell, Peer, 5, 100)),
        };
        var state = MentorCycleState.Create(new LifecycleGeneration(1));

        var actions = PlanWithWake(world, Config(), ref state, out var wake);

        Assert.Empty(actions);
        Assert.Equal(1, state.LastInputSequence);
        Assert.Equal(WakePolicyKind.OnPublication, wake.Kind);
    }

    private static GameWorldState BaseWorld(
        MasteryExperienceDomain domain = MasteryExperienceDomain.Spell)
    {
        var domainView = domain switch
        {
            MasteryExperienceDomain.Spell => KnownEntities.MagicSpellbook.Uuid,
            MasteryExperienceDomain.Artifact => KnownEntities.WorkshopArtifact.Uuid,
            _ => KnownEntities.AlchemyScreen.Uuid,
        };
        return new GameWorldState
        {
            CollectedAtEpoch = 1,
            Views = Table(
                new WorldView(KnownEntities.MasteriesEnabled.Uuid, false, false, true),
                new WorldView(domainView, false, false, true)),
        };
    }

    private static SuiteRuntimeConfiguration Config(
        MentorEconomyMode economy = MentorEconomyMode.PerRecipient,
        MentorSpellSourcePolicy spellPolicy = MentorSpellSourcePolicy.EquippedSpells,
        bool artifacts = false,
        bool alchemy = false) =>
        new()
        {
            General = new SuiteGeneralConfiguration { Enabled = true },
            Safety = new SuiteSafetyConfiguration(),
            Mentor = new MentorConfiguration
            {
                Mode = MentorOperationMode.Active,
                EconomyMode = economy,
                SpellSourcePolicy = spellPolicy,
                SpellSharePercent = 10,
                ArtifactsEnabled = artifacts,
                ArtifactSharePercent = 10,
                AlchemyEnabled = alchemy,
                AlchemySharePercent = 10,
            },
        };

    private static IReadOnlyList<MentorCycleAction> Plan(
        GameWorldState world,
        SuiteRuntimeConfiguration config,
        ref MentorCycleState state) =>
        PlanWithWake(world, config, ref state, out _);

    private static IReadOnlyList<MentorCycleAction> PlanWithWake(
        GameWorldState world,
        SuiteRuntimeConfiguration config,
        ref MentorCycleState state,
        out WakePolicy wake)
    {
        var store = new ReusableActionStore<MentorCycleAction>();
        store.BeginWrite();
        var writer = new ServiceActionWriter<MentorCycleAction>(store);
        wake = MentorCycleEvaluator.Evaluate(
            world, in config, ref state, writer);
        var result = new List<MentorCycleAction>();
        while (!store.IsComplete)
        {
            result.Add(store.GetCurrent());
            store.CommitCurrentAndClear();
        }
        return result;
    }

    private static WorldMasteryExperience Input(
        long sequence,
        MasteryExperienceDomain domain,
        Guid id,
        int mastery,
        double amount) =>
        new(sequence, domain, id, mastery, true, new BigDouble(amount));

    private static WorldSpellRecipe Spell(Guid id, int mastery) =>
        new(id, true, 0, default, mastery, false, false, false, false, 0, 0, 0, false,
            default, default, default, default, default, default, false);

    private static WorldSpellSlot Slot(Guid id) =>
        new(0, id, true, false, false, false, false, false, false, true, true, true,
            1, 1, default);

    private static WorldEquipment Equipment(Guid id, int mastery) =>
        new(id, true, 0, default, mastery, false, default, default, default, 0, 0, 0, default);

    private static WorldAlchemyRecipe Alchemy(Guid id, int mastery, Guid type) =>
        new(id, type, true, 0, 0, 0, default, mastery, default, false, false, false, 0,
            false, default, default, default, default, default, default, default, default,
            default, default, default, default, default, default, default, default);

    private static PublicationTable<T> Table<T>(params T[] rows) where T : struct =>
        PublicationTable<T>.Create(rows, rows.Length);
}
