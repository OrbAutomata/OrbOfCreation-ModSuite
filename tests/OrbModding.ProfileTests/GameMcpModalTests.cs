using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using OrbAutomata.GameMcp;
using OrbModding.Common;
using Xunit;

namespace OrbModding.ProfileTests;

public sealed class GameMcpModalTests : IDisposable
{
    private const long Epoch = 41;

    public GameMcpModalTests() => UnityEngine.Resources.Objects.Clear();

    public void Dispose() => UnityEngine.Resources.Objects.Clear();

    [Fact]
    public void Tool_is_one_targetless_ui_state_control()
    {
        var tool = Assert.Single(GameMcpAcceptanceFixture.Tools(),
            candidate => (string?)candidate["name"] == "game_modal");

        Assert.Equal(new[] { "mode" }, tool["inputSchema"]!["required"]!.Values<string>());
        Assert.Equal(new[] { "dismiss" },
            tool["inputSchema"]!["properties"]!["mode"]!["enum"]!.Values<string>());
        var operation = GameMcpProtocolRouter.BuildOperation("game_modal", new JObject
        {
            ["mode"] = "dismiss",
        });
        Assert.Equal(GameMcpOperationClass.UiState, operation.Classification);
        Assert.Equal(Guid.Empty, operation.Uuid);
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
    public void Dismiss_refuses_zero_multiple_grace_and_native_no_op_states()
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

        grace.FinishCloseForTest();
        var noOp = new UIModal { SuppressClose = true };
        noOp.OpenForTest();
        UnityEngine.Resources.Objects.Add(noOp);
        Assert.Equal("requested_state_not_reached", action.Submit().Code);
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
