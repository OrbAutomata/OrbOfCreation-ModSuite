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

public sealed class GameMcpCastTests
{
    private static readonly Guid RecipeId =
        Guid.Parse("d8c42ced-12de-4bc7-bf3a-f11a13318e42");
    private static readonly Guid InstanceId =
        Guid.Parse("f9ec2758-ce33-4fcb-883a-5283035254a6");
    private static readonly Guid SecondRecipeId =
        Guid.Parse("b2f18a44-6d0c-4a7e-9f5b-0c1d2e3f4a5b");
    private static readonly Guid SecondInstanceId =
        Guid.Parse("c3a29b55-7e1d-4b8f-8a6c-1d2e3f4a5b6c");

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

    /// <summary>
    /// The game gives the player a held cast button that charges a spell instead of firing it, so
    /// the tool gives the caller the same choice — offered on the press that starts a cast and
    /// nowhere else.
    /// </summary>
    [Fact]
    public void Charging_is_an_optional_choice_on_the_press_that_starts_a_cast()
    {
        var tool = Assert.Single(
            GameMcpAcceptanceFixture.Tools(),
            candidate => (string?)candidate["name"] == "game_cast");

        Assert.Equal(
            "boolean",
            (string?)tool["inputSchema"]!["properties"]!["charge"]!["type"]);
        Assert.DoesNotContain(
            "charge",
            tool["inputSchema"]!["required"]!.Values<string>());
        var forbidden = tool["inputSchema"]!["allOf"]!
            .Where(rule => rule["then"]!["not"] is not null)
            .Select(rule => (string?)rule["if"]!["properties"]!["mode"]!["const"])
            .ToArray();
        Assert.Equal(new[] { "release", "toggle_off" }, forbidden);

        var charged = GameMcpProtocolRouter.BuildOperation("game_cast", new JObject
        {
            ["mode"] = "fire",
            ["slot"] = 1,
            ["uuid"] = RecipeId.ToString("D"),
            ["charge"] = true,
        });
        var plain = GameMcpProtocolRouter.BuildOperation("game_cast", new JObject
        {
            ["mode"] = "fire",
            ["slot"] = 1,
            ["uuid"] = RecipeId.ToString("D"),
        });

        Assert.Equal("charge", charged.SerializedValue);
        Assert.Equal(string.Empty, plain.SerializedValue);
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
        Assert.Equal(1, (int)delta["slot"]!);
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

    /// <summary>
    /// The price block on a committed fire is the next cast's, priced against the stock the settled
    /// world holds now — both numbers from one instant. Named bare `costs` it read as a statement
    /// about the press that just committed: a live round read `cost: 100 of 7.3 Mana affordable=no`
    /// off a cast that had landed and took it for a failure.
    /// </summary>
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
            castCount: 8,
            maximumCharges: 3,
            cooldownRemaining: new BigDouble(41d));
        var command = new GameMcpCommand(
            1, GameMcpCommandKind.Cast, 9, 3, "fire", RecipeId, Guid.Empty,
            "SpellRecipeSO", 1, string.Empty, string.Empty, false,
            frameContext: GameMcpTestHarness.Context(before, generation: 51));

        var delta = GameMcpTestHarness.Json(GameMcpWorldQuery.ProjectGameplayPostState(
            GameMcpTestHarness.Context(after, generation: 52),
            command,
            GameMcpCommandResult.Committed("committed", 9, 3)));

        Assert.Null(delta["costs"]);
        Assert.Equal("25", (string?)delta["nextCastCosts"]![0]!["cost"]);
        Assert.True((bool)delta["active"]!);
        Assert.True((bool)delta["casting"]!);

        // A charge count reached the wire on two of one round's 144 casts, because it was
        // published only when the settlement window caught the number moving. The rule is now the
        // state itself: short of full charges says so, in the game's own format, with its
        // countdown beside it.
        Assert.Equal("1 of 3", (string?)delta["charges"]);
        Assert.Equal("41.0s", (string?)delta["nextChargeIn"]);
    }

    /// <summary>
    /// A fire says what the press did: a cast is running that was not. The game's finished-cast
    /// counter stood here and could not say it — the increment lives in <c>Spell.ExecuteSpell</c>,
    /// where a cast ends, so a cast shorter than the world cadence left the number untouched and a
    /// landed press read byte-identical to a dropped one.
    /// </summary>
    [Fact]
    public void A_fire_says_a_cast_started_rather_than_quoting_a_finished_cast_total()
    {
        var before = World(casting: false, cancellationEnabled: true, charges: 2, castCount: 12);
        var after = World(casting: false, cancellationEnabled: true, charges: 2, castCount: 12);
        var command = new GameMcpCommand(
            1, GameMcpCommandKind.Cast, 9, 3, "fire", RecipeId, Guid.Empty,
            "SpellRecipeSO", 1, string.Empty, string.Empty, false,
            frameContext: GameMcpTestHarness.Context(before, generation: 59));

        var delta = GameMcpTestHarness.Json(GameMcpWorldQuery.ProjectGameplayPostState(
            GameMcpTestHarness.Context(after, generation: 60),
            command,
            GameMcpCommandResult.Committed("committed", 9, 3)));

        Assert.Equal(JTokenType.Boolean, delta["casting"]!.Type);
        Assert.True((bool)delta["casting"]!);
        Assert.Null(delta["casts"]);
        Assert.False((bool)delta["charging"]!);
    }

    /// <summary>
    /// The readiness term is named after the term it reads, never after the press it cannot promise.
    /// <c>Spell.Fire</c> asks <c>IsCasting()</c> before it asks <c>CanCast()</c>, so a running spell
    /// answers <c>CanCast()</c> with true while the press starts no cast — and a field called
    /// <c>ready</c> beside <c>cooldown: 0</c> invited exactly the press this tool then refused.
    /// </summary>
    [Fact]
    public void A_fire_names_the_readiness_term_it_reads_and_never_promises_the_next_press()
    {
        var before = World(casting: false, cancellationEnabled: true, charges: 2);
        var after = World(casting: true, cancellationEnabled: true, charges: 2);
        var command = new GameMcpCommand(
            1, GameMcpCommandKind.Cast, 9, 3, "fire", RecipeId, Guid.Empty,
            "SpellRecipeSO", 1, string.Empty, string.Empty, false,
            frameContext: GameMcpTestHarness.Context(before, generation: 71));

        var delta = GameMcpTestHarness.Json(GameMcpWorldQuery.ProjectGameplayPostState(
            GameMcpTestHarness.Context(after, generation: 72),
            command,
            GameMcpCommandResult.Committed("committed", 9, 3)));

        Assert.Null(delta["ready"]);
        Assert.True((bool)delta["castReady"]!);
        Assert.True((bool)delta["casting"]!);
    }

    /// <summary>
    /// A charged fire leaves the cast button held down, and nothing in the settled loadout says so.
    /// The caller has to release it, so the answer says the hold is outstanding.
    /// </summary>
    [Fact]
    public void A_charged_fire_says_the_hold_it_left_down()
    {
        var before = World(
            casting: false, cancellationEnabled: true, charges: 2, chargeable: true);
        var after = World(
            casting: true, cancellationEnabled: true, charges: 2, chargeable: true);
        var command = new GameMcpCommand(
            1, GameMcpCommandKind.Cast, 9, 3, "fire", RecipeId, Guid.Empty,
            "SpellRecipeSO", 1, string.Empty, "charge", false,
            frameContext: GameMcpTestHarness.Context(before, generation: 61));

        var delta = GameMcpTestHarness.Json(GameMcpWorldQuery.ProjectGameplayPostState(
            GameMcpTestHarness.Context(after, generation: 62),
            command,
            GameMcpCommandResult.Committed("committed", 9, 3)));

        Assert.True((bool)delta["casting"]!);
        Assert.True((bool)delta["charging"]!);
    }

    /// <summary>
    /// A hold asked for on a spell the game will not charge would set an input the game ignores and
    /// fire an ordinary cast under the name of a charged one, so it is refused with the reason.
    /// </summary>
    /// <remarks>
    /// The fact is observed at the action boundary, of the live spell the position resolves to
    /// (<c>AutoCastCycleActionAdapterTests</c> pins that), because a published loadout is up to a
    /// cadence old and a moved or emptied slot answered here as a claim about the spell. This pins
    /// the class and the sentence the boundary's verdict crosses the wire as.
    /// </remarks>
    [Fact]
    public void A_charge_the_game_does_not_offer_is_refused_rather_than_fired_plain()
    {
        var code = AutoCastActionResultCodes.SpellNotChargeable;

        Assert.Equal(
            "spell_not_chargeable",
            GameMcpActionResultCodeNames.Name(code, GameMcpCommandKind.Cast));
        Assert.Equal(
            "The game offers this spell no charged cast, so it can only be fired outright.",
            GameMcpActionResultCodeNames.Reason(code, GameMcpCommandKind.Cast));

        var refusal = GameMcpTestHarness.Json(new GameMcpObjectBuilder
        {
            ["status"] = "refused",
            ["reasonCode"] = "spell_not_chargeable",
        });

        Assert.Equal("ERR_STATE", (string?)refusal["reasonCode"]);
        Assert.Equal(
            "The game offers this spell no charged cast, so it can only be fired outright.",
            (string?)refusal["reason"]);
    }

    [Fact]
    public void A_spell_slot_row_publishes_the_games_finished_cast_total()
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
            "SpellRecipeSO", 1, string.Empty, string.Empty, false,
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
            "SpellRecipeSO", 1, string.Empty, string.Empty, false,
            frameContext: GameMcpTestHarness.Context(before, generation: 55));

        var delta = GameMcpTestHarness.Json(GameMcpWorldQuery.ProjectGameplayPostState(
            GameMcpTestHarness.Context(after, generation: 56),
            command,
            GameMcpCommandResult.Committed("committed", 9, 3)));

        Assert.True((bool)delta["active"]!);
    }

    /// <summary>
    /// An idle one-shot used to drop <c>active</c> and let the silence carry the answer, which is
    /// the absence-as-value the surface bans everywhere else. The settled slot always knows whether
    /// the spell is running, so the key is always there and says which.
    /// </summary>
    [Fact]
    public void An_idle_non_toggle_spell_says_it_is_not_running_rather_than_dropping_the_key()
    {
        var before = World(casting: false, cancellationEnabled: true, charges: 2, toggled: false);
        var after = World(casting: false, cancellationEnabled: true, charges: 2, toggled: false);
        var command = new GameMcpCommand(
            1, GameMcpCommandKind.Cast, 9, 3, "fire", RecipeId, Guid.Empty,
            "SpellRecipeSO", 1, string.Empty, string.Empty, false,
            frameContext: GameMcpTestHarness.Context(before, generation: 57));

        var delta = GameMcpTestHarness.Json(GameMcpWorldQuery.ProjectGameplayPostState(
            GameMcpTestHarness.Context(after, generation: 58),
            command,
            GameMcpCommandResult.Committed("committed", 9, 3)));

        Assert.Equal(JTokenType.Boolean, delta["active"]!.Type);
        Assert.False((bool)delta["active"]!);
    }

    /// <summary>
    /// One verb, one key set. A live round watched <c>release</c> drop both <c>casting</c> and
    /// <c>charging</c> rather than say <c>no</c>, so the response that exists to end a hold was
    /// silent about the hold — indistinguishable from a mode that does not speak about holds at
    /// all. <c>active</c> and <c>charging</c> ride every mode; <c>casting</c> is fire's own verified
    /// sentinel and no mode without a native delta behind it claims the fact either way.
    /// </summary>
    [Theory]
    [InlineData("fire")]
    [InlineData("release")]
    [InlineData("toggle_off")]
    public void Every_cast_mode_answers_with_the_same_keys(string mode)
    {
        var before = World(
            casting: true, cancellationEnabled: true, charges: 2, chargeable: true);
        var after = World(
            casting: true, cancellationEnabled: true, charges: 2, chargeable: true);
        var command = new GameMcpCommand(
            1, GameMcpCommandKind.Cast, 9, 3, mode, RecipeId, Guid.Empty,
            "SpellRecipeSO", 1, string.Empty, string.Empty, false,
            frameContext: GameMcpTestHarness.Context(before, generation: 63));

        var delta = GameMcpTestHarness.Json(GameMcpWorldQuery.ProjectGameplayPostState(
            GameMcpTestHarness.Context(after, generation: 64),
            command,
            GameMcpCommandResult.Committed("committed", 9, 3)));

        Assert.Equal(JTokenType.Boolean, delta["active"]!.Type);
        Assert.Equal(JTokenType.Boolean, delta["charging"]!.Type);
        Assert.False((bool)delta["charging"]!);
        Assert.NotNull(delta["castReady"]);
        Assert.Equal(mode == "fire", delta["casting"] is not null);

        // `cooldown: 0` rode on every cast of a round while the spell was visibly recharging: the
        // field does not read the game's charge timer, and a zero no screen draws is worse than
        // silence. A spell at full charges now says nothing about recharging.
        Assert.Null(delta["cooldown"]);
        Assert.Null(delta["charges"]);
        Assert.Null(delta["nextChargeIn"]);
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
        frameContext: before);

    /// <summary>
    /// A cast the game refuses on readiness answers with the game's own recharge facts.
    /// </summary>
    /// <remarks>
    /// One round met "the game refused the cast on its own readiness terms" twenty-five times and
    /// never once with a number, while the loadout's removal refusal for the same state was
    /// quoting charges and a countdown. The boundary binds no charge reader; the world publishes
    /// both for every equipped slot, so the sentence is written there.
    /// </remarks>
    [Fact]
    public void A_cast_the_game_calls_unready_names_its_charges_and_its_countdown()
    {
        var world = World(
            casting: false, cancellationEnabled: true, charges: 0, maximumCharges: 3,
            cooldownRemaining: new BigDouble(41d));

        Assert.Equal(
            RecipeId.ToString("D") + " is still recharging: it holds 0 of 3 charges, " +
            "next in 41.0s.",
            GameMcpWorldQuery.CastNotReadyReason(world, 0, RecipeId));

        // At full charges the world holds no fact the sentence could carry, so it says nothing and
        // the boundary's own account stands.
        Assert.Equal(string.Empty, GameMcpWorldQuery.CastNotReadyReason(
            World(casting: false, cancellationEnabled: true), 0, RecipeId));
    }

    /// <summary>
    /// A cast the game refuses on readiness answers with the world's sentence, not the boundary's.
    /// </summary>
    /// <remarks>
    /// The boundary's own string was taken unconditionally, so the charges-and-countdown sentence
    /// the world had already composed was thrown away: a live round read the lowercase,
    /// period-less "the game refused the cast on its own readiness terms" twenty-five times while
    /// the next <c>world_get</c> on the same slot printed "0 of 1 charges, next in 5.66s". This
    /// runs the one producer the live press runs, with the boundary's own live string.
    /// </remarks>
    [Fact]
    public void A_refused_cast_keeps_the_worlds_sentence_over_the_boundarys()
    {
        const string boundary = "the game refused the cast on its own readiness terms";
        var unready = ServiceActionResult.Skipped(AutoCastActionResultCodes.SpellNotReady);
        var command = Fire(World(casting: false, cancellationEnabled: true));

        Assert.Equal(
            RecipeId.ToString("D") + " is still recharging: it holds 0 of 3 charges, " +
            "next in 41.0s.",
            AutomataServiceCycleRuntime.CastRefusalReason(
                command,
                World(
                    casting: false, cancellationEnabled: true, charges: 0, maximumCharges: 3,
                    cooldownRemaining: new BigDouble(41d)),
                in unready,
                boundaryVerified: false,
                boundary));

        // At full charges the world holds no fact its sentence could carry, so the boundary's own
        // account stands rather than the refusal going out wordless.
        Assert.Equal(
            boundary,
            AutomataServiceCycleRuntime.CastRefusalReason(
                command,
                World(casting: false, cancellationEnabled: true),
                in unready,
                boundaryVerified: false,
                boundary));
    }

    /// <summary>
    /// A cast the game refuses because another is running names the spell that is running.
    /// </summary>
    /// <remarks>
    /// <c>SpellManager.CanCastASpell()</c> is <c>activeSpells.Any(spell =&gt; spell.IsReadyingCast())</c>,
    /// so one spell mid-cast refuses every cast in the game. A round firing three spells in one
    /// batch read "the native spell system is busy" for two of them and could not tell which of its
    /// own presses had taken the caster and which were waiting on it.
    /// </remarks>
    [Fact]
    public void A_busy_caster_names_the_spell_that_is_holding_it()
    {
        var busy = ServiceActionResult.Skipped(AutoCastActionResultCodes.NativeCasterBusy);
        var world = TwoSlots(holderIsReadying: true);

        Assert.Equal(
            "Whirling Sorcery is still casting; Firebolt waits for it. " +
            "The game casts one spell at a time.",
            AutomataServiceCycleRuntime.CastRefusalReason(
                Fire(world, slot: 2, recipeId: SecondRecipeId),
                world,
                in busy,
                boundaryVerified: false,
                "another spell is still casting, and the game casts one spell at a time"));

        // Nothing is readying by the time the world was published: the boundary's own account is
        // all there is, and it says the same thing without the name.
        Assert.Equal(
            "another spell is still casting, and the game casts one spell at a time",
            AutomataServiceCycleRuntime.CastRefusalReason(
                Fire(TwoSlots(holderIsReadying: false), slot: 2, recipeId: SecondRecipeId),
                TwoSlots(holderIsReadying: false),
                in busy,
                boundaryVerified: false,
                "another spell is still casting, and the game casts one spell at a time"));
    }

    /// <summary>
    /// A fired cast settles on the cast it started, not on the first world after the press.
    /// </summary>
    /// <remarks>
    /// <c>fire</c> had no settlement sentinel at all, so a committed fire answered
    /// <c>active: no  castReady: yes</c> with no recharge block — the world it described had not
    /// yet seen the cast. The game spends the charge at the END of a cast
    /// (<c>Spell.EndCasting</c>/<c>Spell.ExecuteSpell</c> call <c>Spell.ConsumeCharge</c>), so a
    /// slow spell is still readying when the next world lands and a fast one has already finished
    /// and dropped a charge. Either proves the press started a cast.
    /// </remarks>
    [Fact]
    public void A_fired_cast_settles_on_the_cast_it_started()
    {
        var completedAt = DateTime.UtcNow.Ticks;
        var before = World(
            casting: false, cancellationEnabled: true, collectedAtUtcTicks: completedAt - 1,
            charges: 3, maximumCharges: 3);
        var command = Fire(GameMcpTestHarness.Context(before, generation: 41));

        // The world the round-15 press was answered from: one generation newer, and the game has
        // not touched the spell yet.
        Assert.False(GameMcpPostStateSettlement.IsReady(
            GameMcpTestHarness.Context(
                World(
                    casting: false, cancellationEnabled: true,
                    collectedAtUtcTicks: completedAt + 1, charges: 3, maximumCharges: 3),
                generation: 42),
            mutationWorld: 41,
            actionCompletedAtUtcTicks: completedAt,
            command));

        // Spell.Cast() sets the prep state on the frame it runs, and the prep state is what
        // Spell.IsReadyingCast() reports.
        Assert.True(GameMcpPostStateSettlement.IsReady(
            GameMcpTestHarness.Context(
                World(
                    casting: false, cancellationEnabled: true,
                    collectedAtUtcTicks: completedAt + 1, charges: 3, maximumCharges: 3,
                    readyingCast: true),
                generation: 42),
            mutationWorld: 41,
            actionCompletedAtUtcTicks: completedAt,
            command));

        // A cast shorter than the world cadence is over before the next publication, and the
        // charge it spent is the only trace left.
        var spent = GameMcpTestHarness.Context(
            World(
                casting: false, cancellationEnabled: true,
                collectedAtUtcTicks: completedAt + 1, charges: 2, maximumCharges: 3,
                cooldownRemaining: new BigDouble(41d)),
            generation: 42);
        Assert.True(GameMcpPostStateSettlement.IsReady(
            spent, mutationWorld: 41, actionCompletedAtUtcTicks: completedAt, command));

        // And the recharge block the projector could never reach is on the answer.
        var delta = GameMcpTestHarness.Json(GameMcpWorldQuery.ProjectGameplayPostState(
            spent, command, GameMcpCommandResult.Committed("committed", 9, 3)));
        Assert.Equal("2 of 3", (string?)delta["charges"]);
        Assert.Equal("41.0s", (string?)delta["nextChargeIn"]);
    }

    /// <summary>
    /// The spell row prints how far off full it is.
    /// </summary>
    /// <remarks>
    /// These two numbers reached the wire from one place only — the refusal nested inside this
    /// row's own <c>remove:</c> block — so learning a spell's charge state meant pressing a removal
    /// nobody wanted.
    /// </remarks>
    [Fact]
    public void An_equipped_spell_row_prints_its_charges_and_its_countdown()
    {
        var recharging = GameMcpTestHarness.Json(GameMcpWorldQuery.ProjectEntityState(
            World(
                casting: false, cancellationEnabled: true, charges: 0, maximumCharges: 1,
                cooldownRemaining: new BigDouble(5.66d)),
            "spell-slots",
            World(
                casting: false, cancellationEnabled: true, charges: 0, maximumCharges: 1,
                cooldownRemaining: new BigDouble(5.66d)).SpellSlots[0]));

        Assert.Equal("0 of 1", (string?)recharging["charges"]);
        Assert.Equal("5.66s", (string?)recharging["nextChargeIn"]);

        // At full charges there is nothing to say, and the row says nothing.
        var full = World(casting: false, cancellationEnabled: true);
        var ready = GameMcpTestHarness.Json(GameMcpWorldQuery.ProjectEntityState(
            full, "spell-slots", full.SpellSlots[0]));
        Assert.Null(ready["charges"]);
        Assert.Null(ready["nextChargeIn"]);
    }

    private static GameMcpCommand Fire(GameWorldState before) =>
        Fire(GameMcpTestHarness.Context(before, generation: 41));

    private static GameMcpCommand Fire(
        GameMcpFrameContext before,
        int slot = 1,
        Guid? recipeId = null) => new(
        1,
        GameMcpCommandKind.Cast,
        9,
        3,
        "fire",
        recipeId ?? RecipeId,
        Guid.Empty,
        "SpellRecipeSO",
        slot,
        string.Empty,
        string.Empty,
        false,
        frameContext: before);

    private static GameMcpCommand Fire(
        GameWorldState before,
        int slot,
        Guid recipeId) =>
        Fire(GameMcpTestHarness.Context(before, generation: 41), slot, recipeId);

    private static GameWorldState TwoSlots(bool holderIsReadying) => new()
    {
        CollectedAtEpoch = 9,
        CollectedAtUtcTicks = 0,
        EntityIdentities = EntityIdentityCatalogSnapshot.Bound(9, new[]
        {
            new EntityIdentityName(RecipeId, "SpellRecipeSO", "Whirling Sorcery", "whirling"),
            new EntityIdentityName(SecondRecipeId, "SpellRecipeSO", "Firebolt", "firebolt"),
        }.OrderBy(row => row.EntityId).ToArray()),
        SpellSlots = PublicationTable<WorldSpellSlot>.Create(new[]
        {
            Slot(0, InstanceId, RecipeId, holderIsReadying),
            Slot(1, SecondInstanceId, SecondRecipeId, readyingCast: false),
        }),
    };

    private static WorldSpellSlot Slot(
        int index,
        Guid instanceId,
        Guid recipeId,
        bool readyingCast) =>
        new(
            index,
            instanceId,
            recipeId,
            occupied: true,
            casting: false,
            readyingCast,
            attuning: false,
            channeled: false,
            toggled: false,
            chargeable: false,
            castReady: true,
            chargeAvailable: true,
            canRemove: true,
            resourcesCovered: true,
            currentCharges: 1,
            maximumCharges: 1,
            cooldownRemaining: default,
            outputLevel: 1,
            effectiveLevel: 1,
            requiredMasteryLevel: 0,
            recipeMasteryLevel: 1,
            durationSpell: true,
            usageRequirementsMet: true,
            augmentGlyphs: PublicationTable<WorldSpellSlotGlyph>.Empty,
            cancellationEnabled: true,
            castCount: 0);

    private static GameWorldState World(
        bool casting,
        bool cancellationEnabled,
        long collectedAtUtcTicks = 0,
        int charges = 1,
        Guid immediateCostResource = default,
        BigDouble immediateCost = default,
        bool toggled = true,
        int castCount = 0,
        bool chargeable = false,
        int maximumCharges = 1,
        BigDouble cooldownRemaining = default,
        bool readyingCast = false) => new()
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
                readyingCast,
                attuning: false,
                channeled: false,
                toggled,
                chargeable,
                castReady: true,
                chargeAvailable: true,
                canRemove: false,
                resourcesCovered: true,
                currentCharges: charges,
                maximumCharges: maximumCharges,
                cooldownRemaining: cooldownRemaining,
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
