using System;
using System.Collections.Generic;
using OrbMentor;
using OrbModding.Common;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.Tests.Services.Mentor.Runtime.ServiceCycle;

public sealed class MentorCycleActionAdapterTests
{
    private static readonly Guid Recipient =
        Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public void CommittedNativeGrantCarriesMutationEvidence()
    {
        var native = new NativePort(new MentorNativeGrant(
            MentorNativeGrantStatus.Committed,
            string.Empty,
            NativeMutationOutcome.Verified,
            new NativeMutationCallOutcome(1, 1, 1)));

        var result = Execute(native);

        Assert.Equal(ServiceActionDisposition.Committed, result.Disposition);
        Assert.Equal(1, result.NativeCallOutcome.MutationsCommitted);
    }

    [Fact]
    public void LifecycleAndOwnershipAreRevalidatedBeforeTheNativePort()
    {
        var native = new NativePort(default);

        var stale = Execute(native, epoch: 2);
        var unowned = Execute(native, permit: false);

        Assert.Equal(CommonActionResultCodes.LifecycleReplaced, stale.Code);
        Assert.Equal(CommonActionResultCodes.PolicyRejected, unowned.Code);
        Assert.Equal(0, native.Calls);
    }

    [Fact]
    public void NativePostconditionFailureFaultsTheService()
    {
        var native = new NativePort(new MentorNativeGrant(
            MentorNativeGrantStatus.PostconditionFailed,
            "no delta",
            NativeMutationOutcome.PostconditionFailed,
            new NativeMutationCallOutcome(1, 1, 0)));

        var result = Execute(native);

        Assert.Equal(ServiceActionDisposition.Faulted, result.Disposition);
        Assert.Equal(NativeMutationOutcome.PostconditionFailed, result.NativeEvidence.Outcome);
    }

    /// <summary>
    /// A grant the game's contract no longer supports stops Mentor with an adapter fault, and the
    /// player reading the log needs the one sentence that says which contract broke.
    /// </summary>
    [Fact]
    public void AnUnavailableContractLogsWhyTheGrantFailed()
    {
        var native = new NativePort(new MentorNativeGrant(
            MentorNativeGrantStatus.ContractUnavailable,
            "native SpellRecipeSO type is unavailable"));
        var warnings = new List<string>();

        var result = Execute(native, warn: warnings.Add);

        Assert.Equal(ServiceActionDisposition.Faulted, result.Disposition);
        var warning = Assert.Single(warnings);
        Assert.Contains("native SpellRecipeSO type is unavailable", warning, StringComparison.Ordinal);
        Assert.Contains(Recipient.ToString(), warning, StringComparison.Ordinal);
    }

    private static ServiceActionResult Execute(
        NativePort native,
        long epoch = 1,
        bool permit = true,
        Action<string>? warn = null)
    {
        var adapter = new MentorCycleActionAdapter(native, () => epoch, _ => permit, warn ?? (_ => { }));
        var action = new MentorCycleAction(
            MasteryExperienceDomain.Spell,
            Recipient,
            new MentorAmount(1, 0),
            5,
            1);
        var config = new SuiteRuntimeConfiguration
        {
            General = new SuiteGeneralConfiguration { Enabled = true },
            Mentor = new MentorConfiguration { Mode = MentorOperationMode.Active },
        };
        return adapter.TryExecute(in action, in config, default);
    }

    private sealed class NativePort : IMentorNativePort
    {
        private readonly MentorNativeGrant _result;

        internal NativePort(MentorNativeGrant result) => _result = result;
        internal int Calls { get; private set; }

        public MentorNativeGrant Grant(in MentorCycleAction action)
        {
            Calls++;
            return _result;
        }
    }
}
