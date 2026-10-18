using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using OrbAutomata.GameMcp;
using OrbModding.Common;
using Xunit;

namespace OrbModding.ProfileTests;

[Collection(GameActionFaultLogCollection.Name)]
public sealed class GameMcpModalTests : IDisposable
{
    private const long Epoch = 41;
    private readonly System.Collections.Generic.List<string> _faults = new();

    public GameMcpModalTests()
    {
        UnityEngine.Resources.Objects.Clear();
        GameActionFaultLog.ConfigureLog(_faults.Add);
    }

    public void Dispose()
    {
        UnityEngine.Resources.Objects.Clear();
        GameActionFaultLog.ConfigureLog(null);
    }

    [Fact]
    public void Tool_is_one_targetless_ui_state_control()
    {
        var tool = Assert.Single(GameMcpAcceptanceFixture.Tools(),
            candidate => (string?)candidate["name"] == "game_modal");

        Assert.Equal(new[] { "mode" }, tool["inputSchema"]!["required"]!.Values<string>());
        Assert.Equal(new[] { "dismiss", "open" },
            tool["inputSchema"]!["properties"]!["mode"]!["enum"]!.Values<string>());
        var operation = GameMcpProtocolRouter.BuildOperation("game_modal", new JObject
        {
            ["mode"] = "dismiss",
        });
        Assert.Equal(GameMcpOperationClass.UiState, operation.Classification);
        Assert.Equal(Guid.Empty, operation.Uuid);
    }

    /// <summary>
    /// Opening names the panel, because the panel is what the player sees and the control that
    /// puts it up carries no name of its own.
    /// </summary>
    [Fact]
    public void An_open_is_addressed_by_the_panel_it_puts_up()
    {
        var operation = GameMcpProtocolRouter.BuildOperation("game_modal", new JObject
        {
            ["mode"] = "open",
            ["title"] = "Statistics",
        });

        Assert.Equal("open", operation.Mode);
        Assert.Equal("Statistics", operation.Title);
        Assert.Equal(GameMcpOperationClass.UiState, operation.Classification);
        Assert.Throws<GameMcpInvalidParamsException>(() =>
            GameMcpProtocolRouter.BuildOperation("game_modal", new JObject { ["mode"] = "open" }));
    }

    [Fact]
    public void Opening_presses_the_one_control_that_puts_up_the_named_panel()
    {
        var player = new UIModal();
        var chrome = Activator(player, "PlayerStatsButton", "Player");
        using var action = new ModalOpenGameAction();

        Assert.True(action.TryReadActivators(out var activators, out var reason), reason);
        var submission = action.Submit("Player", activators);

        Assert.True(submission.Committed, submission.Reason);
        Assert.Equal("Player", submission.Title);
        Assert.True(player.IsOpen());
        Assert.Equal("Player", Assert.Single(activators).Title);
        Assert.Same(chrome, Assert.Single(activators).Control);
    }

    /// <summary>
    /// A name the chrome does not have is a name to correct, so the refusal spends its sentence on
    /// the names this screen does offer rather than on the one that missed.
    /// </summary>
    [Fact]
    public void A_panel_no_control_opens_is_refused_naming_the_ones_this_screen_has()
    {
        Activator(new UIModal(), "SettingsButton", "Settings");
        using var action = new ModalOpenGameAction();

        Assert.True(action.TryReadActivators(out var activators, out var reason), reason);
        var submission = action.Submit("Player", activators);

        Assert.False(submission.Committed);
        Assert.Equal("no_modal_named", submission.Code);
        Assert.Contains("this screen offers Settings", submission.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Two_controls_for_one_panel_name_are_refused_rather_than_guessed_between()
    {
        Activator(new UIModal(), "PlayerStatsButton", "Player");
        Activator(new UIModal(), "PlayerShortcut", "Player");
        using var action = new ModalOpenGameAction();

        Assert.True(action.TryReadActivators(out var activators, out var reason), reason);

        Assert.Equal("ambiguous_modal", action.Submit("Player", activators).Code);
    }

    /// <summary>
    /// A control the player cannot press is not on the screen, and an unprepared or untitled one
    /// has no panel to name, so none of the three reaches the element list.
    /// </summary>
    [Fact]
    public void Only_live_prepared_and_titled_controls_reach_the_element_list()
    {
        var hidden = Activator(new UIModal(), "HiddenButton", "Player");
        hidden.SetLiveForTest(false);
        Activator(null, "UnpreparedButton", "Player");
        Activator(new UIModal(), "NamelessButton", string.Empty);
        var live = Activator(new UIModal(), "PlayerStatsButton", "Player");
        using var action = new ModalOpenGameAction();

        Assert.True(action.TryReadActivators(out var activators, out var reason), reason);

        Assert.Same(live, Assert.Single(activators).Control);
    }

    /// <summary>
    /// A closed panel is alpha-0, not inactive, so the control inside it stays active in the
    /// hierarchy forever. The catalog said "this screen" while enumerating the whole scene.
    /// </summary>
    [Fact]
    public void A_control_inside_a_closed_panel_is_not_on_this_screen()
    {
        var closed = new UnityEngine.GameObject("PlayerModal(Clone)");
        var modal = closed.AddComponent<UIModal>();
        var chrome = Activator(new UIModal(), "PlayerStatsButton", "Player");
        chrome.transform.SetParent(closed.transform, false);
        using var action = new ModalOpenGameAction();

        Assert.True(chrome.gameObject.activeInHierarchy);
        Assert.True(action.TryReadActivators(out var hidden, out var reason), reason);
        Assert.Empty(hidden);

        modal.OpenForTest();

        Assert.True(action.TryReadActivators(out var shown, out reason), reason);
        Assert.Equal("Player", Assert.Single(shown).Title);
    }

    /// <summary>
    /// What an open does is scene-wired: <c>UIModal.PerformOpen</c> invokes two UnityEvents no read
    /// of the build can follow. So the suite presses a named set and nothing else — and the three
    /// the game draws beside the player's own chrome are a world reset, an ending screen and a
    /// developer console.
    /// </summary>
    [Fact]
    public void The_chrome_the_suite_presses_is_the_players_own_and_nothing_else()
    {
        Activator(new UIModal(), "ResetWorldButton", "Reset World");
        Activator(new UIModal(), "GameCompleteButton", "Game Complete!");
        Activator(new UIModal(), "DevConsoleButton", "Dev Console");
        Activator(new UIModal(), "JukeboxButton", "Jukebox");
        Activator(new UIModal(), "SettingsButton", "Settings");
        using var action = new ModalOpenGameAction();

        Assert.True(action.TryReadActivators(out var activators, out var reason), reason);
        Assert.Equal("Settings", Assert.Single(activators).Title);

        var refusal = action.Submit("Reset World", activators);

        Assert.False(refusal.Committed);
        Assert.Equal("modal_not_offered", refusal.Code);
        Assert.Equal(
            "The suite does not open the 'Reset World' panel; it opens only the player's own " +
            "chrome: Player, Settings.",
            refusal.Reason);
        Assert.Equal(
            GameMcpDecisionReason.ClassUnavailable,
            GameMcpDecisionReason.Class("modal_not_offered"));
    }

    /// <summary>
    /// The open flag is the game's own answer to whether the press landed, read off the panel the
    /// pressed control prepared, so a press the game swallowed is never reported as a panel up.
    /// </summary>
    [Fact]
    public void A_press_that_puts_no_panel_up_is_an_attempted_fault()
    {
        var player = new UIModal { SuppressOpen = true };
        Activator(player, "PlayerStatsButton", "Player");
        using var action = new ModalOpenGameAction();

        Assert.True(action.TryReadActivators(out var activators, out var reason), reason);
        var submission = action.Submit("Player", activators);

        Assert.False(submission.Committed);
        Assert.Equal("verification_failed", submission.Code);
        Assert.Equal(GameActionAnswer.ChangeNotSeen("the Player panel"), submission.Reason);
        Assert.True(submission.Faulted);
    }

    [Fact]
    public void A_throw_after_the_panel_went_up_still_commits()
    {
        var player = new UIModal();
        Activator(player, "PlayerStatsButton", "Player").ThrowAfterOpen = true;
        using var action = new ModalOpenGameAction();

        Assert.True(action.TryReadActivators(out var activators, out var reason), reason);
        var submission = action.Submit("Player", activators);

        Assert.True(submission.Committed, submission.Reason);
        Assert.Equal("Player", submission.Title);
        Assert.True(player.IsOpen());
    }

    [Fact]
    public void A_throw_that_leaves_no_panel_up_is_a_fault_naming_the_log_reference()
    {
        var player = new UIModal { SuppressOpen = true };
        Activator(player, "PlayerStatsButton", "Player").ThrowAfterOpen = true;
        using var action = new ModalOpenGameAction();

        Assert.True(action.TryReadActivators(out var activators, out var reason), reason);
        var submission = action.Submit("Player", activators);

        Assert.False(submission.Committed);
        Assert.Equal("post_commit_fault", submission.Code);
        Assert.StartsWith(GameActionAnswer.GameErrored("the Player panel"), submission.Reason,
            StringComparison.Ordinal);
        Assert.Contains("Reference MCP-", submission.Reason, StringComparison.Ordinal);
        Assert.Contains(_faults, fault =>
            fault.Contains("injected failure after opening the panel", StringComparison.Ordinal));
        Assert.True(submission.Faulted);
    }

    [Fact]
    public void Dispose_forgets_the_binding_failure_on_both_halves()
    {
        var open = new ModalOpenGameAction(includeContract: id => id != ModalOpenGameAction.ContractIds[0]);
        var dismiss = new ModalDismissGameAction(
            () => Epoch,
            includeContract: id => id != ModalDismissGameAction.ContractIds[0]);
        Assert.NotEmpty(open.BindingFailure);
        Assert.NotEmpty(dismiss.BindingFailure);

        open.Dispose();
        dismiss.Dispose();

        Assert.Empty(open.BindingFailure);
        Assert.Empty(dismiss.BindingFailure);
    }

    [Fact]
    public void Every_missing_member_disables_the_complete_panel_open_binding_set()
    {
        foreach (var missing in ModalOpenGameAction.ContractIds)
        {
            using var action = new ModalOpenGameAction(includeContract: id => id != missing);
            Assert.False(action.BindingsAvailable);
            Assert.Contains(missing, action.BindingFailure, StringComparison.Ordinal);
        }
    }

    private static UIModalActivator Activator(UIModal? modal, string label, string title)
    {
        var chrome = new UIModalActivator(modal!, label) { modalTitle = title };
        chrome.SetLiveForTest(true);
        UnityEngine.Resources.Objects.Add(chrome);
        return chrome;
    }

    [Fact]
    public void Dismiss_uses_the_only_open_modal_and_observes_native_closing()
    {
        var modal = new UIModal();
        modal.OpenForTest();
        UnityEngine.Resources.Objects.Add(modal);
        using var action = new ModalDismissGameAction(() => Epoch);

        var result = action.Submit();
        var observedBeforeAnimation = action.TryObserveDismissed(out var before, out var reason);
        var unrelated = new UIModal();
        unrelated.OpenForTest();
        UnityEngine.Resources.Objects.Add(unrelated);
        modal.FinishCloseForTest();
        var observedAfterAnimation = action.TryObserveDismissed(out var after, out reason);

        Assert.True(result.Committed, result.Reason);
        Assert.True(observedBeforeAnimation, reason);
        Assert.False(before);
        Assert.True(observedAfterAnimation, reason);
        Assert.True(after);
    }

    /// <summary>
    /// A dismissal names the modal it shut. The answer was zero bytes, so the only way to learn
    /// the press had landed was a screenshot — the most expensive object on the surface — while
    /// the title was readable off the control the whole time.
    /// </summary>
    [Fact]
    public void A_committed_dismissal_carries_the_title_of_the_modal_it_shut()
    {
        var titled = new UIModal();
        titled.OpenForTest(title: "Ritual Results");
        UnityEngine.Resources.Objects.Add(titled);
        using var action = new ModalDismissGameAction(() => Epoch);

        var result = action.Submit();

        Assert.True(result.Committed, result.Reason);
        Assert.Equal("Ritual Results", result.Title);
    }

    /// <summary>
    /// The game's close sets its closing flag before anything else, and only when the modal is open,
    /// not closing and out of its grace period — so a close that returns without it did nothing the
    /// suite can see. The only way back is <c>UIModal.Open</c>, which runs the panel's scene-wired
    /// open events again, so nothing can undo the press and the answer is a fault.
    /// </summary>
    [Fact]
    public void A_close_the_game_does_not_begin_is_an_attempted_fault_not_a_refusal()
    {
        var modal = new UIModal { SuppressClose = true };
        modal.OpenForTest(title: "Ritual Results");
        UnityEngine.Resources.Objects.Add(modal);
        using var action = new ModalDismissGameAction(() => Epoch);

        var result = action.Submit();

        Assert.False(result.Committed);
        Assert.Equal("verification_failed", result.Code);
        Assert.Equal(GameActionAnswer.ChangeNotSeen("the Ritual Results panel"), result.Reason);
        Assert.True(result.Faulted);
    }

    [Fact]
    public void A_throw_after_the_modal_began_closing_still_commits()
    {
        var modal = new UIModal { ThrowAfterClose = true };
        modal.OpenForTest(title: "Ritual Results");
        UnityEngine.Resources.Objects.Add(modal);
        using var action = new ModalDismissGameAction(() => Epoch);

        var result = action.Submit();
        modal.FinishCloseForTest();

        Assert.True(result.Committed, result.Reason);
        Assert.Equal("Ritual Results", result.Title);
        Assert.True(action.TryObserveDismissed(out var dismissed, out var reason), reason);
        Assert.True(dismissed);
    }

    [Fact]
    public void A_throw_that_leaves_the_modal_open_is_a_fault_naming_the_log_reference()
    {
        var modal = new UIModal { SuppressClose = true, ThrowAfterClose = true };
        modal.OpenForTest(title: "Ritual Results");
        UnityEngine.Resources.Objects.Add(modal);
        using var action = new ModalDismissGameAction(() => Epoch);

        var result = action.Submit();

        Assert.False(result.Committed);
        Assert.Equal("post_commit_fault", result.Code);
        Assert.StartsWith(GameActionAnswer.GameErrored("the Ritual Results panel"), result.Reason,
            StringComparison.Ordinal);
        Assert.Contains("Reference MCP-", result.Reason, StringComparison.Ordinal);
        Assert.Contains(_faults, fault =>
            fault.Contains("injected failure after closing the modal", StringComparison.Ordinal));
        Assert.True(result.Faulted);
    }

    /// <summary>
    /// A lifecycle the settled read cannot answer is a failed read. It is neither a dismissal nor a
    /// modal still open, and it never escapes the watch that is waiting on it.
    /// </summary>
    [Fact]
    public void An_unreadable_lifecycle_fails_the_settled_read()
    {
        var modal = new UIModal();
        modal.OpenForTest();
        UnityEngine.Resources.Objects.Add(modal);
        var unreadable = false;
        using var action = new ModalDismissGameAction(
            () => unreadable ? throw new InvalidOperationException("lifecycle unreadable") : Epoch);
        Assert.True(action.Submit().Committed);
        modal.FinishCloseForTest();
        unreadable = true;

        Assert.False(action.TryObserveDismissed(out var dismissed, out var reason));
        Assert.False(dismissed);
        Assert.StartsWith("The modal settled state could not be read.", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Dismiss_reads_the_live_lifecycle_the_caller_cannot_submit()
    {
        var modal = new UIModal();
        modal.OpenForTest();
        UnityEngine.Resources.Objects.Add(modal);
        var epoch = Epoch;
        using var action = new ModalDismissGameAction(() => epoch);

        // The tool is targetless, so its command carries no lifecycle at all.
        var result = action.Submit();
        Assert.True(result.Committed, result.Reason);

        epoch++;
        Assert.False(action.TryObserveDismissed(out _, out var reason));
        Assert.Contains("lifecycle", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Open_modals_are_named_for_the_screen_reads_that_publish_them()
    {
        using var action = new ModalDismissGameAction(() => Epoch);
        Assert.True(action.TryReadOpenModals(out var none, out var reason), reason);
        Assert.Empty(none);

        var closed = new UIModal();
        closed.OpenForTest(title: "Ledger");
        closed.FinishCloseForTest();
        var settings = new UIModal();
        settings.OpenForTest(title: "Settings");
        UnityEngine.Resources.Objects.Add(closed);
        UnityEngine.Resources.Objects.Add(settings);

        Assert.True(action.TryReadOpenModals(out var open, out reason), reason);
        Assert.Equal(new[] { "Settings" }, open);
    }

    [Fact]
    public void Unity_framework_types_do_not_depend_on_the_game_type_resolver()
    {
        using var action = new ModalDismissGameAction(
            () => Epoch,
            resolveType: name => name.StartsWith("UnityEngine.", StringComparison.Ordinal)
                ? null
                : ReflectionUtil.FindLoadedType(name));

        Assert.True(action.BindingsAvailable, action.BindingFailure);
    }

    [Fact]
    public void Dismiss_refuses_zero_multiple_and_grace_states()
    {
        using var action = new ModalDismissGameAction(() => Epoch);
        Assert.Equal("no_open_modal", action.Submit().Code);

        var first = new UIModal();
        first.OpenForTest();
        var second = new UIModal();
        second.OpenForTest();
        UnityEngine.Resources.Objects.Add(first);
        UnityEngine.Resources.Objects.Add(second);
        Assert.Equal("multiple_modals_open", action.Submit().Code);

        UnityEngine.Resources.Objects.Remove(second);
        first.FinishCloseForTest();
        var grace = new UIModal();
        grace.OpenForTest(0.2f);
        UnityEngine.Resources.Objects.Add(grace);
        Assert.Equal("modal_close_not_ready", action.Submit().Code);
    }

    [Fact]
    public void Every_missing_member_disables_the_complete_modal_binding_set()
    {
        foreach (var missing in ModalDismissGameAction.ContractIds)
        {
            using var action = new ModalDismissGameAction(
                () => Epoch,
                includeContract: id => id != missing);
            Assert.False(action.BindingsAvailable);
            Assert.Contains(missing, action.BindingFailure, StringComparison.Ordinal);
        }
    }
}
