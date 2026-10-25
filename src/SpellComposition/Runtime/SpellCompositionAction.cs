using OrbModding.Common.Runtime.World;
using System;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

/// <summary>One level-dial change: a −/+ adjuster that moves a level inside its raised cap.</summary>
internal readonly struct SpellCompositionAction
{
    internal SpellCompositionAction(LevelDial dial, int value, long lifecycleEpoch, int? expectedCurrentLevel = null,
        Guid? expectedIdentity = null)
    {
        if (!Enum.IsDefined(typeof(LevelDial), dial))
            throw new ArgumentOutOfRangeException(nameof(dial));
        if (value <= 0) throw new ArgumentOutOfRangeException(nameof(value));
        if (expectedCurrentLevel is <= 0) throw new ArgumentOutOfRangeException(nameof(expectedCurrentLevel));
        if (expectedIdentity == Guid.Empty) throw new ArgumentOutOfRangeException(nameof(expectedIdentity));
        ExpectedIdentity = expectedIdentity;
        ExpectedCurrentLevel = expectedCurrentLevel;
        Dial = dial;
        Value = value;
        LifecycleEpoch = lifecycleEpoch;
    }

    internal Guid? ExpectedIdentity { get; }
    internal int? ExpectedCurrentLevel { get; }
    internal LevelDial Dial { get; }
    internal int Value { get; }
    internal long LifecycleEpoch { get; }
}

internal readonly struct SpellCompositionSubmission
{
    internal SpellCompositionSubmission(
        NativeActionOutcome outcome,
        int minimum = -1,
        int maximum = -1)
    {
        Outcome = outcome;
        Minimum = minimum;
        Maximum = maximum;
    }

    internal NativeActionOutcome Outcome { get; }

    /// <summary>
    /// The live native range this dial admits, read from the same capture the refusal sentence was
    /// written from. Negative where the refusal never read one, so nothing invents a range.
    /// </summary>
    internal int Minimum { get; }
    internal int Maximum { get; }

    internal static SpellCompositionSubmission Reject(
        ServiceActionResultCode code,
        string reason) =>
        new(NativeActionOutcome.Reject(code, reason));

    internal static SpellCompositionSubmission OutOfRange(
        string reason,
        int minimum,
        int maximum) =>
        new(
            NativeActionOutcome.Reject(SpellCompositionActionResultCodes.LevelOutOfRange, reason),
            minimum,
            maximum);
}

internal static class SpellCompositionActionResultCodes
{
    internal static readonly ServiceActionResultCode ContractUnavailable = new(7301, "contract_unavailable");
    internal static readonly ServiceActionResultCode WrongThread = new(7303, "wrong_thread");
    internal static readonly ServiceActionResultCode LevelOutOfRange = new(7305, "level_out_of_range");
    internal static readonly ServiceActionResultCode AlreadyInRequestedState = new(7306, "already_in_requested_state");
    internal static readonly ServiceActionResultCode MutationPermitUnavailable = new(7314, "action_family_unavailable");
    internal static readonly ServiceActionResultCode PostCommitFault = new(7315, "post_commit_fault");
    internal static readonly ServiceActionResultCode VerificationFailed = new(7316, "verification_failed");
    internal static readonly ServiceActionResultCode StateChanged = new(7318, "dial_changed");
    internal static readonly ServiceActionResultCode ControlLocked = new(7317, "screen_locked");
}
