using System;
using OrbAutomata;
using OrbModding.Tests.Services.TestSupport;
using Xunit;

namespace OrbModding.Tests.Services.RunTransition;

public sealed class ReturnToMenuGameActionTests
{
    private const long Epoch = 141;
    private readonly UIBackToMenuButton _button = new();

    public ReturnToMenuGameActionTests()
    {
        UIScreenFlash.ResetForTests();
        SaveStateManager.instance = new SaveStateManager();
    }

    [Fact]
    public void NativeUiCallbackRaisesManualSaveAndStartsTheSceneTransition()
    {
        using var boundary = Boundary();

        var result = Submit(boundary);

        Assert.True(result.Verified, result.Reason);
        Assert.Equal(1, _button.manualSave.RaiseCalls);
        Assert.True(UIScreenFlash.instance.ActiveForTests);
        Assert.Equal(ReturnToMenuNativeStage.Verification, result.Stage);
        Assert.Equal("Back to Menu", result.PressedControl);
        Assert.Equal(string.Empty, result.OpenedPanel);
    }

    [Fact]
    public void MutableAdmissionRefusesWrongSceneActiveTransitionMissingControlAndRevokedPermit()
    {
        using var wrongScene = Boundary(scene: "Start");
        Assert.Equal(ReturnToMenuPreflight.WrongScene, Submit(wrongScene).Preflight);

        UIScreenFlash.instance.FadeIn(0f, 0f);
        using var active = Boundary();
        Assert.Equal(ReturnToMenuPreflight.TransitionInProgress, Submit(active).Preflight);
        UIScreenFlash.ResetForTests();

        using var missing = Boundary(buttons: Array.Empty<object>());
        Assert.Equal(ReturnToMenuPreflight.ControlUnavailable, Submit(missing).Preflight);

        using var revoked = Boundary(permit: false);
        Assert.Equal(ReturnToMenuPreflight.MutationPermitUnavailable, Submit(revoked).Preflight);
        Assert.Equal(0, _button.manualSave.RaiseCalls);
    }

    [Fact]
    public void OnlyTheVisibleInteractableControlParticipatesInAdmission()
    {
        var hidden = new UIBackToMenuButton { name = "Hidden template" };
        hidden.SetLiveForTest(false);
        using var oneLive = Boundary(buttons: new object[] { hidden, _button });

        var accepted = Submit(oneLive);

        Assert.True(accepted.Verified, accepted.Reason);

        UIScreenFlash.ResetForTests();
        var second = new UIBackToMenuButton { name = "Second live control" };
        using var ambiguous = Boundary(buttons: new object[] { _button, second });
        var refused = Submit(ambiguous);
        Assert.Equal(ReturnToMenuPreflight.ControlUnavailable, refused.Preflight);
        Assert.Contains("Back to Main Menu", refused.Reason, StringComparison.Ordinal);
        Assert.Contains("Second live control", refused.Reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// The player opens the panel and then presses the control. A closed panel is the ordinary
    /// state, not a refusal, so the boundary performs both steps in the order the player does.
    /// </summary>
    [Fact]
    public void AClosedPanelIsOpenedByItsOwnButtonBeforeTheControlIsPressed()
    {
        var panel = new UIModal();
        var control = new UIBackToMenuButton { name = "Back to Main Menu" };
        control.PlaceInPanelForTest(panel);
        var activator = new UIModalActivator(panel, "Settings");
        using var boundary = Panel(panel, control, activator);

        var result = Submit(boundary);

        Assert.True(result.Verified, result.Reason);
        Assert.True(panel.IsOpen());
        Assert.Equal(1, control.manualSave.RaiseCalls);
        Assert.True(UIScreenFlash.instance.ActiveForTests);

        // Both steps are the caller's to audit: the panel this opened and the control it pressed.
        Assert.Equal("Settings", result.OpenedPanel);
        Assert.Equal("Back to Main Menu", result.PressedControl);
    }

    [Fact]
    public void APanelThatDoesNotOpenRefusesAndSaysTheControlIsStillOutOfReach()
    {
        var panel = new UIModal { SuppressOpen = true };
        var control = new UIBackToMenuButton { name = "Back to Main Menu" };
        control.PlaceInPanelForTest(panel);
        var activator = new UIModalActivator(panel, "Settings");
        using var boundary = Panel(panel, control, activator);

        var result = Submit(boundary);

        Assert.Equal(ReturnToMenuPreflight.ControlUnavailable, result.Preflight);
        Assert.Contains("did not open", result.Reason, StringComparison.Ordinal);
        Assert.Equal(0, control.manualSave.RaiseCalls);
    }

    [Fact]
    public void APanelWhoseOwnButtonIsNotLiveIsNotOpenedOnThePlayersBehalf()
    {
        var panel = new UIModal();
        var control = new UIBackToMenuButton { name = "Back to Main Menu" };
        control.PlaceInPanelForTest(panel);
        var activator = new UIModalActivator(panel, "Settings");
        activator.SetLiveForTest(false);
        using var boundary = Panel(panel, control, activator);

        var result = Submit(boundary);

        Assert.Equal(ReturnToMenuPreflight.ControlUnavailable, result.Preflight);
        Assert.Contains("no closed panel", result.Reason, StringComparison.Ordinal);
        Assert.False(panel.IsOpen());
    }

    /// <summary>
    /// The panel is chosen because it contains the control, never because of what it is called.
    /// </summary>
    [Fact]
    public void APanelThatDoesNotContainTheControlIsNotOpened()
    {
        var panel = new UIModal();
        var unrelated = new UIModal();
        var control = new UIBackToMenuButton { name = "Back to Main Menu" };
        control.PlaceInPanelForTest(panel);
        var activator = new UIModalActivator(unrelated, "Achievements");
        using var boundary = Panel(unrelated, control, activator);

        var result = Submit(boundary);

        Assert.Equal(ReturnToMenuPreflight.ControlUnavailable, result.Preflight);
        Assert.Contains("no closed panel", result.Reason, StringComparison.Ordinal);
        Assert.False(unrelated.IsOpen());
    }

    [Fact]
    public void NativeNoOpFailsTheSingleScreenTransitionSentinel()
    {
        UIScreenFlash.SuppressFade = true;
        using var boundary = Boundary();

        var result = Submit(boundary);

        Assert.Equal(ReturnToMenuPreflight.VerificationFailed, result.Preflight);
        Assert.Equal(1, _button.manualSave.RaiseCalls);
        Assert.False(UIScreenFlash.instance.ActiveForTests);
    }

    [Fact]
    public void OffThreadSubmissionRefusesBeforeTheUiCallback()
    {
        using var boundary = Boundary();

        var result = ForeignThread.Run(() => Submit(boundary));

        Assert.Equal(ReturnToMenuPreflight.WrongThread, result.Preflight);
        Assert.Equal(0, _button.manualSave.RaiseCalls);
    }

    [Fact]
    public void EveryMissingMemberDisablesTheCompleteBindingSet()
    {
        foreach (var missing in ReturnToMenuNativeBindings.ContractIds)
        {
            using var boundary = Boundary(includeContract: id => id != missing);
            Assert.False(boundary.BindingsAvailable);
            Assert.Contains(missing, boundary.BindingFailure, StringComparison.Ordinal);
        }
    }

    private static ReturnToMenuGameAction Panel(
        UIModal panel,
        UIBackToMenuButton control,
        UIModalActivator activator) =>
        new(
            () => Epoch,
            static () => true,
            static () => "RunTransition ownership was revoked.",
            static () => "Main",
            type => type == typeof(UIModalActivator)
                ? new object[] { activator }
                : type == typeof(UIModal)
                    ? new object[] { panel }
                    : new object[] { control });

    private ReturnToMenuGameAction Boundary(
        string scene = "Main",
        object[]? buttons = null,
        bool permit = true,
        Func<string, bool>? includeContract = null) =>
        new(
            () => Epoch,
            () => permit,
            static () => "RunTransition ownership was revoked.",
            () => scene,
            _ => buttons ?? new object[] { _button },
            includeContract: includeContract);

    private static ReturnToMenuSubmission Submit(ReturnToMenuGameAction boundary)
    {
        var action = new ReturnToMenuAction(Epoch);
        return boundary.Submit(in action);
    }
}
