using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using OrbAutomata.GameMcp;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.ProfileTests;

public sealed class GameMcpCastTests
{
    private static readonly Guid RecipeId =
        Guid.Parse("d8c42ced-12de-4bc7-bf3a-f11a13318e42");
    private static readonly Guid InstanceId =
        Guid.Parse("f9ec2758-ce33-4fcb-883a-5283035254a6");

    [Fact]
    public void ToolOffersTheNativeFireReleaseAndToggleOffButtonPaths()
    {
        var tool = Assert.Single(
            GameMcpAcceptanceFixture.Tools(),
            candidate => (string?)candidate["name"] == "game_cast");

        Assert.Equal(
            new[] { "fire", "release", "toggle_off" },
            tool["inputSchema"]!["properties"]!["mode"]!["enum"]!
                .Values<string>()
                .ToArray());
    }

    [Theory]
    [InlineData(true, true, null, null)]
    [InlineData(false, false, "ERR_STATE",
        "Cancellable spells are switched off, so this cast cannot be toggled off.")]
    public void ActiveToggleRowsPublishWhetherThePlayersSettingAllowsTheNextPress(
        bool cancellationEnabled,
        bool available,
        string? reasonCode,
        string? reason)
    {
        var world = World(casting: true, cancellationEnabled);

        var row = GameMcpTestHarness.Json(GameMcpWorldQuery.ProjectEntityState(
            world,
            "spell-slots",
            world.SpellSlots[0]));

        Assert.Equal(available, (bool)row["toggleOff"]!["available"]!);
        Assert.Equal(reasonCode, (string?)row["toggleOff"]!["reasonCode"]);
        Assert.Equal(reason, (string?)row["toggleOff"]!["reason"]);
    }

    [Fact]
    public void SettledToggleOffRequiresAndReturnsTheObservedActiveToInactiveTransition()
    {
        var completedAt = DateTime.UtcNow.Ticks;
        var before = World(
            casting: true,
            cancellationEnabled: true,
            collectedAtUtcTicks: completedAt - 1);
        var after = World(
            casting: false,
            cancellationEnabled: true,
            collectedAtUtcTicks: completedAt + 1);
        var command = Command(GameMcpTestHarness.Context(before, generation: 41));
        var settled = GameMcpTestHarness.Context(after, generation: 42);

        Assert.True(GameMcpPostStateSettlement.IsReady(
            settled,
            mutationWorld: 41,
            actionCompletedAtUtcTicks: completedAt,
            command));

        var delta = GameMcpTestHarness.Json(GameMcpWorldQuery.ProjectGameplayPostState(
            settled,
            command,
            GameMcpCommandResult.Committed("committed", 9, 3)));
        Assert.Equal(GameMcpTestHarness.Handle(RecipeId), (string?)delta["uuid"]);
        Assert.Equal(0, (int)delta["slot"]!);
        Assert.False((bool)delta["active"]!);

        var unchanged = GameMcpTestHarness.Context(
            World(
                casting: true,
                cancellationEnabled: true,
                collectedAtUtcTicks: completedAt + 1),
            generation: 42);
        Assert.False(GameMcpPostStateSettlement.IsReady(
            unchanged,
            mutationWorld: 41,
            actionCompletedAtUtcTicks: completedAt,
            command));
    }

    [Fact]
    public void Fire_returns_the_published_price_and_observed_slot_change()
    {
        var resourceId = Guid.Parse("19999999-9999-4999-8999-999999999999");
        var before = World(casting: false, cancellationEnabled: true, charges: 2, castCount: 7);
        var after = World(
            casting: true,
            cancellationEnabled: true,
            charges: 1,
            immediateCostResource: resourceId,
            immediateCost: new BigDouble(25),
            castCount: 8);
        var command = new GameMcpCommand(
            1, GameMcpCommandKind.Cast, 9, 3, "fire", RecipeId, Guid.Empty,
            "SpellRecipeSO", 1, string.Empty, string.Empty, false, false,
            frameContext: GameMcpTestHarness.Context(before, generation: 51));

        var delta = GameMcpTestHarness.Json(GameMcpWorldQuery.ProjectGameplayPostState(
            GameMcpTestHarness.Context(after, generation: 52),
            command,
            GameMcpCommandResult.Committed("committed", 9, 3)));

        Assert.Equal("25", (string?)delta["costs"]![0]!["cost"]);
        Assert.True((bool)delta["active"]!);
        Assert.Equal(2, (int)delta["charges"]!["before"]!);
        Assert.Equal(1, (int)delta["charges"]!["after"]!);
        Assert.Equal(8, (int)delta["casts"]!);
    }

    /// <summary>
    /// The counter is one number, not a pair. The game increments <c>numCasts</c> in
    /// <c>Spell.ExecuteSpell</c> — where a cast finishes — so the world settled a frame after a
    /// press has correctly not counted the press, and a pair of it read identical on sixteen of
    /// seventeen live fires. Whether the press landed is the answer's own verdict now.
    /// </summary>
    [Fact]
    public void The_cast_counter_is_a_settled_total_rather_than_a_pair_that_never_moves()
    {
        var before = World(casting: false, cancellationEnabled: true, charges: 2, castCount: 12);
        var after = World(casting: false, cancellationEnabled: true, charges: 2, castCount: 12);
        var command = new GameMcpCommand(
            1, GameMcpCommandKind.Cast, 9, 3, "fire", RecipeId, Guid.Empty,
            "SpellRecipeSO", 1, string.Empty, string.Empty, false, false,
            frameContext: GameMcpTestHarness.Context(before, generation: 59));

        var delta = GameMcpTestHarness.Json(GameMcpWorldQuery.ProjectGameplayPostState(
            GameMcpTestHarness.Context(after, generation: 60),
            command,
            GameMcpCommandResult.Committed("committed", 9, 3)));

        Assert.Equal(JTokenType.Integer, delta["casts"]!.Type);
        Assert.Equal(12, (int)delta["casts"]!);
        Assert.Equal("casts: 12", Assert.Single(
            GameMcpTextPage.Render(delta).Split('\n'), line => line.StartsWith("casts")));
    }

    [Fact]
    public void A_spell_slot_row_reads_the_same_cast_counter_the_fire_response_moves()
    {
        var world = World(casting: false, cancellationEnabled: true, castCount: 4);

        var row = GameMcpTestHarness.Json(GameMcpWorldQuery.ProjectEntityState(
            world,
            "spell-slots",
            world.SpellSlots[0]));

        Assert.Equal(4, (int)row["casts"]!);
    }

    [Fact]
    public void A_toggle_spell_says_whether_it_is_running_as_the_boolean_the_read_surface_uses()
    {
        var before = World(casting: true, cancellationEnabled: true, charges: 2);
        var after = World(casting: true, cancellationEnabled: true, charges: 2);
        var command = new GameMcpCommand(
            1, GameMcpCommandKind.Cast, 9, 3, "toggle_off", RecipeId, Guid.Empty,
            "SpellRecipeSO", 1, string.Empty, string.Empty, false, false,
            frameContext: GameMcpTestHarness.Context(before, generation: 53));

        var delta = GameMcpTestHarness.Json(GameMcpWorldQuery.ProjectGameplayPostState(
            GameMcpTestHarness.Context(after, generation: 54),
            command,
            GameMcpCommandResult.Committed("committed", 9, 3)));

        Assert.Equal(JTokenType.Boolean, delta["active"]!.Type);
        Assert.True((bool)delta["active"]!);
    }

    [Fact]
    public void A_non_toggle_spell_that_is_running_says_so_under_the_same_name()
    {
        var before = World(casting: false, cancellationEnabled: true, charges: 2, toggled: false);
        var after = World(casting: true, cancellationEnabled: true, charges: 2, toggled: false);
        var command = new GameMcpCommand(
            1, GameMcpCommandKind.Cast, 9, 3, "fire", RecipeId, Guid.Empty,
            "SpellRecipeSO", 1, string.Empty, string.Empty, false, false,
            frameContext: GameMcpTestHarness.Context(before, generation: 55));

        var delta = GameMcpTestHarness.Json(GameMcpWorldQuery.ProjectGameplayPostState(
            GameMcpTestHarness.Context(after, generation: 56),
            command,
            GameMcpCommandResult.Committed("committed", 9, 3)));

        Assert.True((bool)delta["active"]!);
    }

    [Fact]
    public void An_idle_non_toggle_spell_reports_no_running_state_it_never_entered()
    {
        var before = World(casting: false, cancellationEnabled: true, charges: 2, toggled: false);
        var after = World(casting: false, cancellationEnabled: true, charges: 2, toggled: false);
        var command = new GameMcpCommand(
            1, GameMcpCommandKind.Cast, 9, 3, "fire", RecipeId, Guid.Empty,
            "SpellRecipeSO", 1, string.Empty, string.Empty, false, false,
            frameContext: GameMcpTestHarness.Context(before, generation: 57));

        var delta = GameMcpTestHarness.Json(GameMcpWorldQuery.ProjectGameplayPostState(
            GameMcpTestHarness.Context(after, generation: 58),
            command,
            GameMcpCommandResult.Committed("committed", 9, 3)));

        Assert.Null(delta["active"]);
    }

    /// <summary>
    /// A press the game would discard is a no, not a commit. <c>Spell.Fire</c> answers a running
    /// spell with a warning popup or by ending the cast, so the boundary refuses before pressing
    /// and the caller reads which kind of no it was and what to press instead.
    /// </summary>
    [Fact]
    public void A_fire_at_a_running_spell_is_refused_with_the_class_and_the_remedy()
    {
        var refusal = GameMcpTestHarness.Json(new GameMcpObjectBuilder
        {
            ["status"] = "refused",
            ["reasonCode"] = "spell_already_casting",
        });

        Assert.Equal("ERR_STATE", (string?)refusal["reasonCode"]);
        Assert.Equal(
            "This spell is already running, so a fire press starts no cast; " +
            "toggle_off ends a running toggle spell.",
            (string?)refusal["reason"]);
    }

    private static GameMcpCommand Command(GameMcpFrameContext before) => new(
        1,
        GameMcpCommandKind.Cast,
        9,
        3,
        "toggle_off",
        RecipeId,
        Guid.Empty,
        "SpellRecipeSO",
        1,
        string.Empty,
        string.Empty,
        false,
        false,
        frameContext: before);

    private static GameWorldState World(
        bool casting,
        bool cancellationEnabled,
        long collectedAtUtcTicks = 0,
        int charges = 1,
        Guid immediateCostResource = default,
        BigDouble immediateCost = default,
        bool toggled = true,
        int castCount = 0) => new()
    {
        CollectedAtEpoch = 9,
        CollectedAtUtcTicks = collectedAtUtcTicks,
        SpellSlots = PublicationTable<WorldSpellSlot>.Create(new[]
        {
            new WorldSpellSlot(
                0,
                InstanceId,
                RecipeId,
                occupied: true,
                casting,
                readyingCast: false,
                attuning: false,
                channeled: false,
                toggled,
                chargeable: false,
                castReady: true,
                chargeAvailable: true,
                canRemove: false,
                resourcesCovered: true,
                currentCharges: charges,
                maximumCharges: 1,
                cooldownRemaining: BigDouble.Zero,
                outputLevel: 1,
                effectiveLevel: 1,
                requiredMasteryLevel: 0,
                recipeMasteryLevel: 1,
                durationSpell: true,
                usageRequirementsMet: true,
                augmentGlyphs: PublicationTable<WorldSpellSlotGlyph>.Empty,
                cancellationEnabled: cancellationEnabled,
                castCount: castCount),
        }),
        SpellCosts = immediateCostResource == Guid.Empty
            ? PublicationTable<WorldSpellCost>.Empty
            : PublicationTable<WorldSpellCost>.Create(new[]
            {
                new WorldSpellCost(
                    0, WorldSpellCostKind.Immediate, immediateCostResource, immediateCost),
            }),
    };
}
