using System;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal enum SpellLoadoutActionKind
{
    Remove = 0,
    Move = 1,
}

internal readonly struct SpellLoadoutAction
{
    internal SpellLoadoutAction(
        SpellLoadoutActionKind kind,
        Guid spellInstanceId,
        int destinationSlot,
        long lifecycleEpoch)
    {
        if (spellInstanceId == Guid.Empty)
            throw new ArgumentException("A runtime spell identity is required.", nameof(spellInstanceId));
        if (kind == SpellLoadoutActionKind.Move && destinationSlot < 0)
            throw new ArgumentOutOfRangeException(nameof(destinationSlot));
        Kind = kind;
        SpellInstanceId = spellInstanceId;
        DestinationSlot = destinationSlot;
        LifecycleEpoch = lifecycleEpoch;
    }

    internal SpellLoadoutActionKind Kind { get; }
    internal Guid SpellInstanceId { get; }
    internal int DestinationSlot { get; }
    internal long LifecycleEpoch { get; }
}

internal static class SpellLoadoutActionResultCodes
{
    internal static readonly ServiceActionResultCode ContractUnavailable = new(7401);
    internal static readonly ServiceActionResultCode WrongThread = new(7403);
    internal static readonly ServiceActionResultCode IdentityUnavailable = new(7404);
    internal static readonly ServiceActionResultCode SpellRecharging = new(7405);
    internal static readonly ServiceActionResultCode DestinationOutOfRange = new(7406);
    internal static readonly ServiceActionResultCode AlreadyInRequestedState = new(7407);
    internal static readonly ServiceActionResultCode MutationPermitUnavailable = new(7408);
    internal static readonly ServiceActionResultCode PostCommitFault = new(7409);
    internal static readonly ServiceActionResultCode VerificationFailed = new(7410);
    internal static readonly ServiceActionResultCode CastInProgress = new(7411);
    internal static readonly ServiceActionResultCode ScreenLocked = new(7412);
}
