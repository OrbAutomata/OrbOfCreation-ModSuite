using System;
using OrbAutomata;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Tests.Services.TestSupport;
using Xunit;

namespace OrbModding.Tests.Services.RunTransition;

public sealed class ReturnToMenuGameActionTests
{
    private const long Epoch = 141;
    private readonly UIBackToMenuButton _button;

    public ReturnToMenuGameActionTests()
    {
        OrbModding.GameStubs.GameStubState.Reset();
        _button = new UIBackToMenuButton();
        SaveStateManager.instance = new SaveStateManager();
    }

    [Fact]
    public void NativeUiCallbackRaisesManualSaveAndStartsTheSceneTransition()
    {
        using var boundary = Boundary();

        var result = Submit(boundary);

        Assert.True(result.Outcome.Verified, result.Outcome.Reason);
        Assert.Equal(1, _button.manualSave.RaiseCalls);
        Assert.True(UIScreenFlash.instance.ActiveForTests);
    }

    [Fact]
    public void MutableAdmissionRefusesWrongSceneActiveTransitionMissingControlAndRevokedPermit()
    {
        using var wrongScene = Boundary(scene: "Start");
        Assert.Equal(ReturnToMenuActionResultCodes.WrongScene, Submit(wrongScene).Outcome.Code);

        UIScreenFlash.instance.FadeIn(0f, 0f);
        using var active = Boundary();
        Assert.Equal(ReturnToMenuActionResultCodes.TransitionInProgress, Submit(active).Outcome.Code);
        UIScreenFlash.instance = new UIScreenFlash();

        using var missing = Boundary(buttons: Array.Empty<object>());
        Assert.Equal(ReturnToMenuActionResultCodes.ControlUnavailable, Submit(missing).Outcome.Code);

        using var revoked = Boundary(permit: false);
        Assert.Equal(ReturnToMenuActionResultCodes.MutationPermitUnavailable, Submit(revoked).Outcome.Code);
        Assert.Equal(0, _button.manualSave.RaiseCalls);
    }

    [Fact]
    public void OnlyTheVisibleInteractableControlParticipatesInAdmission()
    {
        var hidden = new UIBackToMenuButton { name = "Hidden template" };
        hidden.SetLiveForTest(false);
        using var oneLive = Boundary(buttons: new object[] { hidden, _button });

        var accepted = Submit(oneLive);

        Assert.True(accepted.Outcome.Verified, accepted.Outcome.Reason);

        UIScreenFlash.instance = new UIScreenFlash();
        var second = new UIBackToMenuButton { name = "Second live control" };
        using var ambiguous = Boundary(buttons: new object[] { _button, second });
        var refused = Submit(ambiguous);
        Assert.Equal(ReturnToMenuActionResultCodes.ControlUnavailable, refused.Outcome.Code);
        Assert.Contains("Back to Main Menu", refused.Outcome.Reason, StringComparison.Ordinal);
        Assert.Contains("Second live control", refused.Outcome.Reason, StringComparison.Ordinal);
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

        Assert.True(result.Outcome.Verified, result.Outcome.Reason);
        Assert.True(panel.IsOpen());
        Assert.Equal(1, control.manualSave.RaiseCalls);
        Assert.True(UIScreenFlash.instance.ActiveForTests);
    }

    /// <summary>
    /// Pressing the panel's own button is the first thing this verb does to the game, so every
    /// outcome after it is an attempt a caller must not retry blind, never a refusal.
    /// </summary>
    [Fact]
    public void APanelThatDoesNotOpenIsAnAttemptAndSaysTheControlIsStillOutOfReach()
    {
        var panel = new UIModal { SuppressOpen = true };
        var control = new UIBackToMenuButton { name = "Back to Main Menu" };
        control.PlaceInPanelForTest(panel);
        var activator = new UIModalActivator(panel, "Settings");
        using var boundary = Panel(panel, control, activator);

        var result = Submit(boundary);

        Assert.Equal(ReturnToMenuActionResultCodes.ControlUnavailable, result.Outcome.Code);
        Assert.Contains("did not open", result.Outcome.Reason, StringComparison.Ordinal);
        Assert.Equal(1, result.Outcome.CallOutcome.MutationAttempts);
        Assert.Equal(ServiceActionDisposition.Faulted, result.Outcome.ToResult().Disposition);
        Assert.Equal(0, control.manualSave.RaiseCalls);
    }

    [Fact]
    public void APanelLeftOpenWithoutALiveControlIsAnAttempt()
    {
        var panel = new UIModal();
        var control = new UIBackToMenuButton { name = "Back to Main Menu" };
        control.PlaceInPanelForTest(panel);
        control.SetLiveForTest(false);
        var activator = new UIModalActivator(panel, "Settings");
        using var boundary = Panel(panel, control, activator);

        var result = Submit(boundary);

        Assert.True(panel.IsOpen());
        Assert.Contains("now open", result.Outcome.Reason, StringComparison.Ordinal);
        Assert.Equal(1, result.Outcome.CallOutcome.MutationAttempts);
        Assert.Equal(ServiceActionDisposition.Faulted, result.Outcome.ToResult().Disposition);
    }

    [Fact]
    public void AnOpenThatThrowsIsAFaultNotANothingWasSentRefusal()
    {
        var panel = new UIModal();
        var control = new UIBackToMenuButton { name = "Back to Main Menu" };
        control.PlaceInPanelForTest(panel);
        var activator = new UIModalActivator(panel, "Settings") { ThrowAfterOpen = true };
        using var boundary = Panel(panel, control, activator);

        var result = Submit(boundary);

        Assert.Equal(ReturnToMenuActionResultCodes.PostCommitFault, result.Outcome.Code);
        Assert.Equal(1, result.Outcome.CallOutcome.MutationAttempts);
        Assert.DoesNotContain("Nothing was sent", result.Outcome.Reason, StringComparison.Ordinal);
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

        Assert.Equal(ReturnToMenuActionResultCodes.ControlUnavailable, result.Outcome.Code);
        Assert.Contains("no closed panel", result.Outcome.Reason, StringComparison.Ordinal);
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

        Assert.Equal(ReturnToMenuActionResultCodes.ControlUnavailable, result.Outcome.Code);
        Assert.Contains("no closed panel", result.Outcome.Reason, StringComparison.Ordinal);
        Assert.False(unrelated.IsOpen());
    }

    [Fact]
    public void NativeNoOpFailsTheSingleScreenTransitionSentinel()
    {
        UIScreenFlash.SuppressFade = true;
        using var boundary = Boundary();

        var result = Submit(boundary);

        Assert.Equal(ReturnToMenuActionResultCodes.VerificationFailed, result.Outcome.Code);
        Assert.Equal(1, _button.manualSave.RaiseCalls);
        Assert.False(UIScreenFlash.instance.ActiveForTests);
    }

    [Fact]
    public void An_unreadable_lifecycle_epoch_faults_before_the_native_call()
    {
        using var boundary = Boundary(epoch: () => throw new InvalidOperationException("lifecycle unavailable"));

        var result = Submit(boundary);

        Assert.True(result.Outcome.IsFault);
        Assert.Equal(ReturnToMenuActionResultCodes.ContractUnavailable, result.Outcome.Code);
        Assert.Equal(0, result.Outcome.CallOutcome.MutationAttempts);
        Assert.Equal(0, _button.manualSave.RaiseCalls);
    }

    [Fact]
    public void OffThreadSubmissionRefusesBeforeTheUiCallback()
    {
        using var boundary = Boundary();

        var result = ForeignThread.Run(() => Submit(boundary));

        Assert.Equal(ReturnToMenuActionResultCodes.WrongThread, result.Outcome.Code);
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
        Func<string, bool>? includeContract = null,
        Func<long>? epoch = null) =>
        new(
            epoch ?? (() => Epoch),
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
