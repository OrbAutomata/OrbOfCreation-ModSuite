using System;
using System.Linq;
using OrbAutomata;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Tests.Services.TestSupport;
using Xunit;

namespace OrbModding.Tests.Services.TargetingActions;

public sealed class TargetingGameActionTests : IDisposable
{
    private const long Epoch = 17;
    public TargetingGameActionTests() => TargetingManager.Reset();
    public void Dispose() => TargetingManager.Reset();

    [Fact]
    public void SubmitCommitsOnlyTheExactUuidResolvedEligibleTarget()
    {
        var target = Target();
        Open(target);
        using var action = Action();

        var result = action.Submit(new TargetingAction(TargetingActionKind.Submit, target.GetGuid(), Epoch));

        Assert.True(result.Verified);
        Assert.Same(target, Assert.Single(TargetingManager.SubmittedTargets));
        Assert.False(TargetingManager.IsTargeting());
    }

    [Fact]
    public void RandomizeUsesNativeRandomSelectionAndSubmitsItAsOneTransaction()
    {
        var target = Target();
        Open(target);
        using var action = Action();

        var result = action.Submit(new TargetingAction(TargetingActionKind.Randomize, Guid.Empty, Epoch));

        Assert.True(result.Verified);
        Assert.Same(target, Assert.Single(TargetingManager.SubmittedTargets));
    }

    [Fact]
    public void CancelUsesTheOwningEffectResultAndRetiresItsExactRequest()
    {
        Open(Target());
        using var action = Action();

        var result = action.Submit(new TargetingAction(TargetingActionKind.Cancel, Guid.Empty, Epoch));

        Assert.True(result.Verified);
        Assert.False(TargetingManager.IsTargeting());
        Assert.Empty(TargetingManager.SubmittedTargets);
    }

    [Fact]
    public void AbsentTargetRefusesBeforePermitOrMutation()
    {
        Open(Target());
        var permits = 0;
        using var action = Action(permit: () => { permits++; return true; });

        var result = action.Submit(new TargetingAction(TargetingActionKind.Submit, Guid.NewGuid(), Epoch));

        Assert.Equal(TargetingPreflight.TargetUnavailable, result.Preflight);
        Assert.Equal(0, permits);
        Assert.True(TargetingManager.IsTargeting());
    }

    [Fact]
    public void MissingRequestLifecycleAndOwnershipAllRefuseBeforeNativeMutation()
    {
        using var noRequest = Action();
        Assert.Equal(TargetingPreflight.NoPendingRequest,
            noRequest.Submit(new TargetingAction(TargetingActionKind.Cancel, Guid.Empty, Epoch)).Preflight);

        var target = Target();
        Open(target);
        using var stale = Action(epoch: Epoch + 1);
        Assert.Equal(TargetingPreflight.LifecycleReplaced,
            stale.Submit(new TargetingAction(TargetingActionKind.Submit, target.GetGuid(), Epoch)).Preflight);

        using var unowned = Action(permit: () => false);
        Assert.Equal(TargetingPreflight.MutationPermitUnavailable,
            unowned.Submit(new TargetingAction(TargetingActionKind.Submit, target.GetGuid(), Epoch)).Preflight);
        Assert.Empty(TargetingManager.SubmittedTargets);
    }

    [Fact]
    public void WrongThreadRefuses()
    {
        var target = Target();
        Open(target);
        using var action = Action();
        var result = ForeignThread.Run(() => action.Submit(
            new TargetingAction(TargetingActionKind.Submit, target.GetGuid(), Epoch)));
        Assert.Equal(TargetingPreflight.WrongThread, result.Preflight);
        Assert.True(TargetingManager.IsTargeting());
    }

    [Fact]
    public void EveryMissingBindingRefusesTheWholeLifecycleSet()
    {
        foreach (var missing in TargetingNativeBindings.ContractIds)
        {
            using var action = Action(include: id => id != missing);
            var result = action.Submit(new TargetingAction(TargetingActionKind.Cancel, Guid.Empty, Epoch));
            Assert.Equal(TargetingPreflight.ContractUnavailable, result.Preflight);
        }
    }

    [Fact]
    public void WrongOutcomeFaultsEachAttemptWithoutPersistentActionState()
    {
        var target = Target();
        Open(target);
        TargetingManager.SuppressSubmit = true;
        using var action = Action();

        var failed = action.Submit(new TargetingAction(TargetingActionKind.Submit, target.GetGuid(), Epoch));
        var retry = action.Submit(new TargetingAction(TargetingActionKind.Submit, target.GetGuid(), Epoch));

        Assert.Equal(TargetingPreflight.VerificationFailed, failed.Preflight);
        Assert.Equal(TargetingPreflight.VerificationFailed, retry.Preflight);
    }

    [Fact]
    public void ThrowAfterExactOutcomeStillCommits()
    {
        var target = Target();
        Open(target);
        TargetingManager.ThrowAfterSubmit = true;
        using var action = Action();
        var submit = action.Submit(new TargetingAction(TargetingActionKind.Submit, target.GetGuid(), Epoch));
        Assert.True(submit.Verified);

        TargetingManager.Reset();
        Open(Target());
        EffectResultInfo.ThrowAfterCancel = true;
        using var cancelAction = Action();
        var cancel = cancelAction.Submit(new TargetingAction(TargetingActionKind.Cancel, Guid.Empty, Epoch));
        Assert.True(cancel.Verified);
    }

    [Fact]
    public void ResultMapperPreservesFaultDisposition()
    {
        var submission = new TargetingSubmission(TargetingPreflight.VerificationFailed,
            TargetingNativeStage.Verification, NativeMutationOutcome.PostconditionFailed,
            new NativeMutationCallOutcome(1, 1, 0), Guid.Empty, "failed");
        var mapped = TargetingActionResultMapper.Map(in submission);
        Assert.Equal(ServiceActionDisposition.Faulted, mapped.Disposition);
        Assert.Equal(TargetingActionResultCodes.VerificationFailed, mapped.Code);
        Assert.True(mapped.HasNativeEvidence);
    }

    private static StructureSO Target()
    {
        var target = new StructureSO { displayName = "Test target" };
        target.SetGuid(Guid.NewGuid());
        return target;
    }
    private static void Open(StructureSO target)
    {
        TargetingManager.AvailableTarget = target;
        TargetingManager.OpenRequests = 1;
    }
    private static TargetingGameAction Action(long epoch = Epoch, Func<bool>? permit = null,
        Func<string, bool>? include = null)
    {
        var action = new TargetingGameAction(() => epoch, permit ?? (() => true),
            () => "test ownership unavailable",
            name => typeof(TargetingManager).Assembly.GetTypes()
                .FirstOrDefault(type => type.Name == name || type.FullName == name),
            include ?? (_ => true));
        if (include is null) Assert.True(action.BindingsAvailable, action.BindingFailure);
        return action;
    }
}
