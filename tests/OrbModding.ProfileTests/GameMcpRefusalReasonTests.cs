using System;
using System.Linq;
using OrbAutomata;
using OrbAutomata.GameMcp;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.ProfileTests;

public sealed class GameMcpRefusalReasonTests
{
    [Fact]
    public void Skipped_unaffordable_purchase_names_every_short_resource_and_amounts()
    {
        var target = Guid.Parse("31111111-1111-4111-8111-111111111111");
        var resource = Guid.Parse("32222222-2222-4222-8222-222222222222");
        var second = Guid.Parse("32222222-2222-4222-8222-222222222223");
        var rateInputs = default(RawResourceRateInputs);
        var traits = default(RawResourceTraits);
        var modifiers = default(RawResourceModifiers);
        var reading = new RawResourceSample(
            resource, new BigDouble(2), new BigDouble(10), true,
            BigDouble.Zero, BigDouble.Zero, new BigDouble(200),
            new BigDouble(100), BigDouble.Zero, BigDouble.Zero, BigDouble.Zero,
            false, false, false, 0, Guid.Empty, in rateInputs, in traits, in modifiers);
        var secondReading = new RawResourceSample(
            second, new BigDouble(1), new BigDouble(10), true,
            BigDouble.Zero, BigDouble.Zero, new BigDouble(200),
            new BigDouble(100), BigDouble.Zero, BigDouble.Zero, BigDouble.Zero,
            false, false, false, 0, Guid.Empty, in rateInputs, in traits, in modifiers);
        var world = new GameWorldState
        {
            EntityIdentities = EntityIdentityCatalogSnapshot.Bound(1, new[]
            {
                new EntityIdentityName(resource, "ResourceSO", "Knowledge", "knowledge"),
                new EntityIdentityName(second, "ResourceSO", "Mana", "mana"),
            }),
            PurchaseCosts = PublicationTable<WorldPurchaseCost>.Create(new[]
            {
                new WorldPurchaseCost(
                    target, resource, new BigDouble(100), new BigDouble(100), 1,
                    new BigDouble(100),
                    PublicationTable<WorldPurchaseCostModifierSource>.Empty,
                    affordabilityEvaluated: true,
                    availableAmount: new BigDouble(2),
                    combinedEffectiveAmount: new BigDouble(100),
                    resourceAffordable: false,
                    resourceAffordabilityReasonCode: "insufficient_resource",
                    affordable: false,
                    affordabilityReasonCode: "unaffordable"),
                new WorldPurchaseCost(
                    target, second, new BigDouble(100), new BigDouble(100), 1,
                    new BigDouble(100),
                    PublicationTable<WorldPurchaseCostModifierSource>.Empty,
                    affordabilityEvaluated: true,
                    availableAmount: new BigDouble(1),
                    combinedEffectiveAmount: new BigDouble(100),
                    resourceAffordable: false,
                    resourceAffordabilityReasonCode: "insufficient_resource",
                    affordable: false,
                    affordabilityReasonCode: "unaffordable"),
            }),
            Resources = PublicationTable<WorldResource>.Create(new[]
            {
                new WorldResource(in reading, true, new BigDouble(8), 0.2d, false,
                    new BigDouble(4), BigDouble.Zero),
                new WorldResource(in secondReading, true, new BigDouble(9), 0.2d, false,
                    new BigDouble(4), BigDouble.Zero),
            }),
        };
        var command = new GameMcpCommand(
            1, GameMcpCommandKind.Purchase, 1, 1, "upgrade", target, Guid.Empty,
            "UpgradeSO", 1, string.Empty, string.Empty, false);
        var action = ServiceActionResult.Skipped(CommonActionResultCodes.Skipped);

        var result = GameMcpPurchaseRefusal.ProjectPurchaseRefusal(
            command, world, in action, 1, 1);

        Assert.NotNull(result);
        Assert.Equal("unaffordable", result!.Code);
        Assert.Equal(
            "Needs 50 Knowledge (have 2); 50 Mana (have 1).",
            result.Reason);
    }

    /// <summary>
    /// No result code's own sentence ever prints a native result number.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A feature result number names no axis a caller can act on, and round 8 shipped it as prose
    /// nine separate ways — "the native action boundary returned Rejected with exact result code
    /// 2051" was the whole answer a caller got for a purchase that had stopped working. The sweep
    /// covers every row of the table rather than the codes that leak today, because every result
    /// code answers through a row.
    /// </para>
    /// <para>
    /// Its scope is the channel it names and no more: the sentence a result <em>code</em> answers
    /// with. A boundary's own <c>exactReason</c> overrides that sentence without passing through
    /// here, and it carries digits on purpose — the epoch a purchase topology was stamped at, the
    /// slot number a spell moved to. A blanket digit assert on that channel would delete facts a
    /// caller acts on, so the honest scope is this one, stated in the name rather than implied.
    /// </para>
    /// </remarks>
    [Fact]
    public void No_result_code_sentence_prints_a_native_result_number()
    {
        Assert.DoesNotContain(GameMcpDecisionReason.NoAccount, char.IsDigit);
        Assert.All(GameMcpDecisionReason.Codes, code =>
            Assert.DoesNotContain(GameMcpDecisionReason.For(code), char.IsDigit));
    }

    /// <summary>
    /// The five ways the purchase-screen gate says no are five answers, not one number.
    /// </summary>
    [Fact]
    public void Every_purchase_screen_refusal_answers_in_its_own_words()
    {
        var codes = new[]
        {
            AutoBuyActionResultCodes.OwningViewRelationUnreadable,
            AutoBuyActionResultCodes.OwningViewTopologyUnbound,
            AutoBuyActionResultCodes.OwningViewTopologyUncaptured,
            AutoBuyActionResultCodes.OwningViewRelationStatusUnmodeled,
            AutoBuyActionResultCodes.OwningViewAvailabilityUnreadable,
        };

        var names = codes.Select(code => code.Name).ToArray();
        var reasons = codes
            .Select(code => GameMcpCommandResult.FromAction(
                ServiceActionResult.Rejected(code), 1, 1).Reason)
            .ToArray();

        Assert.Equal(codes.Length, names.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(codes.Length, reasons.Distinct(StringComparer.Ordinal).Count());
        Assert.DoesNotContain("native_rejected", names);
    }

    /// <summary>
    /// The harvest and plot lifecycle refusals answer with the same names every other lifecycle
    /// verb uses for the same stop, rather than as the game's own no.
    /// </summary>
    [Fact]
    public void A_harvest_lifecycle_refusal_answers_with_its_own_name()
    {
        var expected = new (ServiceActionResultCode Code, GameMcpCommandKind Kind, string Name)[]
        {
            (HarvestLifecycleActionResultCodes.ContractUnavailable, GameMcpCommandKind.HarvestLifecycle, "contract_unavailable"),
            (HarvestLifecycleActionResultCodes.WrongThread, GameMcpCommandKind.HarvestLifecycle, "wrong_thread"),
            (HarvestLifecycleActionResultCodes.IdentityUnavailable, GameMcpCommandKind.HarvestLifecycle, "identity_unavailable"),
            (HarvestLifecycleActionResultCodes.NotVisible, GameMcpCommandKind.HarvestLifecycle, "not_visible"),
            (HarvestLifecycleActionResultCodes.ElementListFull, GameMcpCommandKind.HarvestLifecycle, "harvest_list_full"),
            (HarvestLifecycleActionResultCodes.ActionListFull, GameMcpCommandKind.HarvestLifecycle, "plot_action_list_full"),
            (HarvestLifecycleActionResultCodes.MutationPermitUnavailable, GameMcpCommandKind.HarvestLifecycle, "action_family_unavailable"),
            (HarvestLifecycleActionResultCodes.PostCommitFault, GameMcpCommandKind.HarvestLifecycle, "post_commit_fault"),
            (HarvestLifecycleActionResultCodes.VerificationFailed, GameMcpCommandKind.HarvestLifecycle, "verification_failed"),
            (PlotLifecycleActionResultCodes.ContractUnavailable, GameMcpCommandKind.Harvest, "contract_unavailable"),
            (PlotLifecycleActionResultCodes.WrongThread, GameMcpCommandKind.Harvest, "wrong_thread"),
            (PlotLifecycleActionResultCodes.IdentityUnavailable, GameMcpCommandKind.Harvest, "identity_unavailable"),
            (PlotLifecycleActionResultCodes.PlotUnavailable, GameMcpCommandKind.Harvest, "not_visible"),
            (PlotLifecycleActionResultCodes.ActionListFull, GameMcpCommandKind.Harvest, "plot_action_list_full"),
            (PlotLifecycleActionResultCodes.MutationPermitUnavailable, GameMcpCommandKind.Harvest, "action_family_unavailable"),
            (PlotLifecycleActionResultCodes.PostCommitFault, GameMcpCommandKind.Harvest, "post_commit_fault"),
            (PlotLifecycleActionResultCodes.VerificationFailed, GameMcpCommandKind.Harvest, "verification_failed"),
        };

        Assert.All(expected, row => Assert.Equal(
            row.Name,
            GameMcpCommandResult.FromAction(
                ServiceActionResult.Rejected(row.Code, "x"), 1, 1).Code));
    }

    /// <summary>
    /// Two features may share a result number, and neither answers with the other's sentence.
    /// </summary>
    [Fact]
    public void A_result_number_two_features_share_never_borrows_the_other_features_sentence()
    {
        var pairs = new (ServiceActionResultCode Code, GameMcpCommandKind Kind,
            ServiceActionResultCode Other, GameMcpCommandKind OtherKind)[]
        {
            (LoadoutActionResultCodes.SlotOutOfRange, GameMcpCommandKind.Loadout,
                SpellLevelActionResultCodes.ProgressionLocked, GameMcpCommandKind.SpellLevel),
            (LoadoutActionResultCodes.SlotEmpty, GameMcpCommandKind.Loadout,
                SpellLevelActionResultCodes.LevelNotAffordable, GameMcpCommandKind.SpellLevel),
            (LoadoutActionResultCodes.EntryUnavailable, GameMcpCommandKind.Loadout,
                AutoCastActionResultCodes.ActionFamilyUnavailable, GameMcpCommandKind.Cast),
            (AutoBuyActionResultCodes.OwningViewUnavailable, GameMcpCommandKind.Purchase,
                SpellLevelActionResultCodes.ProgressionLocked, GameMcpCommandKind.SpellLevel),
            (AutoBuyActionResultCodes.OwningViewRelationMissing, GameMcpCommandKind.Purchase,
                SpellLevelActionResultCodes.LevelNotAffordable, GameMcpCommandKind.SpellLevel),
        };

        Assert.All(pairs, pair => Assert.NotEqual(
            GameMcpCommandResult.FromAction(
                ServiceActionResult.Rejected(pair.Other), 1, 1).Reason,
            GameMcpCommandResult.FromAction(
                ServiceActionResult.Rejected(pair.Code), 1, 1).Reason));
    }

    /// <summary>
    /// The multi-buy multiplier is the suite's own pin. When it will not hold, the game is never
    /// shown the press, so the answer must not be the game's no.
    /// </summary>
    /// <remarks>
    /// This shipped as <c>native_rejected</c> — "The game refused this at the moment it was asked,
    /// and gave no reason of its own." — on the surface's most-used spending verb, blaming the game
    /// for a suite failure while throwing away the sentence the pin had already composed saying
    /// which of its steps refused.
    /// </remarks>
    [Fact]
    public void A_multi_buy_pin_that_will_not_hold_is_the_suites_failure_and_carries_its_reason()
    {
        const string pinned =
            "The suite could not set the game's multi-buy multiplier to 5, so no purchase was " +
            "attempted: global multi-buy mutation is quarantined: restore failed.";
        var result = ServiceActionResult.Rejected(AutoBuyActionResultCodes.SingleBuyUnavailable);

        var answer = GameMcpCommandResult.FromAction(
            result.WithReason(pinned), 1, 1);

        Assert.Equal("single_buy_unavailable", answer.Code);
        Assert.Equal("failed", answer.Status);
        Assert.Equal(pinned, answer.Reason);
        Assert.Equal(
            GameMcpDecisionReason.ClassUnavailable,
            GameMcpDecisionReason.Class(answer.Code));
    }

    [Fact]
    public void Native_rejection_the_read_side_can_explain_never_answers_native_rejected()
    {
        var target = Guid.Parse("34444444-4444-4444-8444-444444444444");
        var reading = new RawUpgradeSample(
            target, level: 10, maxLevel: 10, available: true, queuedLevels: 0,
            buildTime: BigDouble.Zero, developmentTime: 1d, cachedCostLevel: 10);
        var world = new GameWorldState
        {
            Upgrades = PublicationTable<WorldUpgrade>.Create(new[]
            {
                new WorldUpgrade(in reading, true, true, 0, 10, false, 0d),
            }),
        };
        var command = new GameMcpCommand(
            1, GameMcpCommandKind.Purchase, 1, 1, "upgrade", target, Guid.Empty,
            "UpgradeSO", 1, string.Empty, string.Empty, false);

        var result = GameMcpPurchaseRefusal.ProjectPurchaseRefusal(
            command,
            world,
            ServiceActionResult.Rejected(CommonActionResultCodes.NativeRejected),
            1,
            1);

        Assert.NotNull(result);
        Assert.Equal("already_maxed", result!.Code);
    }

    [Fact]
    public void Native_rejection_the_read_side_cannot_explain_stays_native_rejected()
    {
        var target = Guid.Parse("34444444-4444-4444-8444-444444444445");
        var command = new GameMcpCommand(
            1, GameMcpCommandKind.Purchase, 1, 1, "upgrade", target, Guid.Empty,
            "UpgradeSO", 1, string.Empty, string.Empty, false);

        Assert.Null(GameMcpPurchaseRefusal.ProjectPurchaseRefusal(
            command,
            new GameWorldState(),
            ServiceActionResult.Rejected(CommonActionResultCodes.NativeRejected),
            1,
            1));
    }

    /// <summary>
    /// A live round pressed Level All Spells with four ready, affordable spells and was told none
    /// was affordable. The button is gated on the Unlock Level All Spells upgrade, which had not
    /// been bought, so the refusal names that upgrade and its price.
    /// </summary>
    [Fact]
    public void Level_all_without_its_unlock_names_the_upgrade_and_its_price()
    {
        var unlock = KnownEntities.UnlockLevelAllSpells.Uuid;
        var skill = Guid.Parse("32222222-2222-4222-8222-222222222225");
        GameWorldState Build(int queuedLevels)
        {
            var reading = new RawUpgradeSample(
                unlock, level: 0, maxLevel: 1, available: true, queuedLevels: queuedLevels,
                buildTime: BigDouble.Zero, developmentTime: 5, cachedCostLevel: 0);
            return new GameWorldState
            {
                EntityIdentities = EntityIdentityCatalogSnapshot.Bound(1, new[]
                {
                    new EntityIdentityName(skill, "ResourceSO", "Skill", "Skill"),
                    new EntityIdentityName(
                        unlock, "UpgradeSO", "Unlock Level All Spells", "UnlockLevelAllSpells"),
                }),
                Upgrades = PublicationTable<WorldUpgrade>.Create(new[]
                {
                    new WorldUpgrade(in reading, true, false, 1, 0, queuedLevels > 0, 0d),
                }),
                PurchaseCosts = PublicationTable<WorldPurchaseCost>.Create(new[]
                {
                    new WorldPurchaseCost(
                        unlock, skill, new BigDouble(5), new BigDouble(5), 1, new BigDouble(5),
                        PublicationTable<WorldPurchaseCostModifierSource>.Empty,
                        affordabilityEvaluated: true,
                        availableAmount: new BigDouble(7),
                        combinedEffectiveAmount: new BigDouble(5),
                        resourceAffordable: true,
                        resourceAffordabilityReasonCode: string.Empty,
                        affordable: true,
                        affordabilityReasonCode: string.Empty),
                }),
            };
        }

        Assert.Equal(
            "Level All Spells is locked until Unlock Level All Spells is bought (5 Skill).",
            GameMcpWorldQuery.LevelAllLockedReason(Build(queuedLevels: 0)));
        Assert.Equal(
            "Level All Spells is locked until Unlock Level All Spells finishes developing in " +
            "the action queue.",
            GameMcpWorldQuery.LevelAllLockedReason(Build(queuedLevels: 1)));
        Assert.Equal("ERR_LOCKED", GameMcpDecisionReason.Class(
            SpellLevelActionResultCodes.LevelAllLocked.Name));
    }

    /// <summary>
    /// A live round pressed Wisdom (41 Knowledge) right after a Resilience press spent Knowledge.
    /// The published world still showed the price met, the game's live price check said no, and
    /// the answer was "nothing left for it to do". The live cost list read at that refusal names
    /// the shortfall.
    /// </summary>
    [Fact]
    public void A_price_the_game_finds_unmet_at_the_press_names_the_live_shortfall()
    {
        var target = Guid.Parse("34444444-4444-4444-8444-444444444446");
        var knowledge = Guid.Parse("32222222-2222-4222-8222-222222222224");
        var world = new GameWorldState
        {
            EntityIdentities = EntityIdentityCatalogSnapshot.Bound(1, new[]
            {
                new EntityIdentityName(knowledge, "ResourceSO", "Knowledge", "knowledge"),
            }),
        };
        var command = new GameMcpCommand(
            1, GameMcpCommandKind.Purchase, 1, 1, "structure", target, Guid.Empty,
            "StructureSO", 1, string.Empty, string.Empty, false);
        var live = new AutoBuyAdmissionDiagnosis(
            AutoBuyAdmissionTerm.Passed,
            AutoBuyAdmissionTerm.Passed,
            AutoBuyAdmissionTerm.Passed,
            AutoBuyAdmissionTerm.Refused,
            AutoBuyLiveCostSnapshot.Complete(new[]
            {
                new AutoBuyLiveCostRow(knowledge, false, new BigDouble(41), new BigDouble(39.6)),
            }));

        var result = GameMcpPurchaseRefusal.ProjectPurchaseRefusal(
            command, world, ServiceActionResult.Skipped(CommonActionResultCodes.Skipped),
            1, 1, live);

        Assert.NotNull(result);
        Assert.Equal("unaffordable", result!.Code);
        Assert.Equal("Needs 41 Knowledge (have 39.6).", result.Reason);
    }

    [Fact]
    public void Maxed_purchase_refusal_uses_the_same_semantic_reason_as_the_read()
    {
        var target = Guid.Parse("34444444-4444-4444-8444-444444444444");
        var reading = new RawUpgradeSample(
            target, level: 10, maxLevel: 10, available: true, queuedLevels: 0,
            buildTime: BigDouble.Zero, developmentTime: 1d, cachedCostLevel: 10);
        var world = new GameWorldState
        {
            Upgrades = PublicationTable<WorldUpgrade>.Create(new[]
            {
                new WorldUpgrade(in reading, true, true, 0, 10, false, 0d),
            }),
        };
        var command = new GameMcpCommand(
            1, GameMcpCommandKind.Purchase, 1, 1, "upgrade", target, Guid.Empty,
            "UpgradeSO", 1, string.Empty, string.Empty, false);

        var result = GameMcpPurchaseRefusal.ProjectPurchaseRefusal(
            command, world, ServiceActionResult.Skipped(CommonActionResultCodes.Skipped),
            1, 1);

        Assert.NotNull(result);
        Assert.Equal("already_maxed", result!.Code);
        Assert.Contains("maximum level", result.Reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// A live round read this refusal as `Aspect: Workshop [AspectWorkshop]
    /// (d9f1a5c3-d7de-4819-b234-a15781190650) is already at its maximum level.` — a log line served
    /// as player prose. The sentence says the player's word for the thing; the asset name and the
    /// id are identity and ride as the response's own fields.
    /// </summary>
    [Fact]
    public void Maxed_purchase_refusal_names_the_player_word_and_leaks_no_asset_name_or_uuid()
    {
        var target = Guid.Parse("d9f1a5c3-d7de-4819-b234-a15781190650");
        var reading = new RawUpgradeSample(
            target, level: 10, maxLevel: 10, available: true, queuedLevels: 0,
            buildTime: BigDouble.Zero, developmentTime: 1d, cachedCostLevel: 10);
        var world = new GameWorldState
        {
            EntityIdentities = EntityIdentityCatalogSnapshot.Bound(1, new[]
            {
                new EntityIdentityName(
                    target, "UpgradeSO", "Aspect: Workshop", "AspectWorkshop"),
            }),
            Upgrades = PublicationTable<WorldUpgrade>.Create(new[]
            {
                new WorldUpgrade(in reading, true, true, 0, 10, false, 0d),
            }),
        };
        var command = new GameMcpCommand(
            1, GameMcpCommandKind.Purchase, 1, 1, "upgrade", target, Guid.Empty,
            "UpgradeSO", 1, string.Empty, string.Empty, false);

        var result = GameMcpPurchaseRefusal.ProjectPurchaseRefusal(
            command, world, ServiceActionResult.Skipped(CommonActionResultCodes.Skipped),
            1, 1);

        Assert.NotNull(result);
        Assert.Equal(
            "Aspect: Workshop is already at its maximum level.", result!.Reason);
    }

    [Fact]
    public void Wrong_tool_redirect_uses_the_created_equipments_player_action()
    {
        var target = Guid.Parse("35555555-5555-4555-8555-555555555555");
        var equipment = new WorldEquipment(
            target, isCreated: true, discRarityLevel: 0, masteryXp: BigDouble.Zero,
            masteryLevel: 0, isRequiredDiscovery: false, power: BigDouble.One,
            baseLevel: BigDouble.One, experienceRateMod: BigDouble.One,
            equippedLevel: 0, attuningLevel: 0, attunementTimeLeft: 0d,
            baseXpRate: BigDouble.Zero);
        var world = new GameWorldState
        {
            EntityIdentities = EntityIdentityCatalogSnapshot.Bound(1, new[]
            {
                new EntityIdentityName(target, "EquipmentSO", "Static Gauntlets", "static"),
            }),
            Equipment = PublicationTable<WorldEquipment>.Create(new[] { equipment }),
        };

        Assert.True(GameMcpEntityCapabilityMap.TryOwningTool(
            world, target, out var category, out var nativeType, out var tool));
        Assert.Equal("equipment", category);
        Assert.Equal("EquipmentSO", nativeType);
        Assert.Equal("game_equipment", tool);
    }
}
