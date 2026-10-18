using System;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal enum ConsumablePlayerActionKind
{
    Use = 0,
    Cancel = 1,
    Discard = 2,
    SetRandomization = 3,
    Move = 4,
}

internal enum ConsumablePlayerListKind
{
    None = 0,
    Inventory = 1,
    Hotbar = 2,
}

internal readonly struct ConsumablePlayerAction
{
    internal ConsumablePlayerAction(
        ConsumablePlayerActionKind kind,
        Guid consumableId,
        long lifecycleEpoch,
        int amount = 1,
        bool randomized = false,
        ConsumablePlayerListKind list = ConsumablePlayerListKind.None,
        int destination = -1)
    {
        if (consumableId == Guid.Empty)
            throw new ArgumentException("A consumable UUID is required.", nameof(consumableId));
        if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount));
        if (kind == ConsumablePlayerActionKind.Move)
        {
            if (list == ConsumablePlayerListKind.None)
                throw new ArgumentException("Move requires a consumable list.", nameof(list));
            if (destination < 0)
                throw new ArgumentOutOfRangeException(nameof(destination));
        }
        else if (list != ConsumablePlayerListKind.None || destination >= 0)
        {
            throw new ArgumentException("Only move accepts a list and destination.");
        }
        Kind = kind;
        ConsumableId = consumableId;
        LifecycleEpoch = lifecycleEpoch;
        Amount = amount;
        Randomized = randomized;
        List = list;
        Destination = destination;
    }

    internal ConsumablePlayerActionKind Kind { get; }
    internal Guid ConsumableId { get; }
    internal long LifecycleEpoch { get; }
    internal int Amount { get; }
    internal bool Randomized { get; }
    internal ConsumablePlayerListKind List { get; }
    internal int Destination { get; }
}

internal static class ConsumablePlayerActionResultCodes
{
    internal static readonly ServiceActionResultCode ContractUnavailable = new(7601);
    internal static readonly ServiceActionResultCode WrongThread = new(7603);
    internal static readonly ServiceActionResultCode ItemUnavailable = new(7604);
    internal static readonly ServiceActionResultCode NotVisible = new(7605);
    internal static readonly ServiceActionResultCode TargetingInProgress = new(7606);
    internal static readonly ServiceActionResultCode InventoryBusy = new(7607);
    internal static readonly ServiceActionResultCode CanFireRefused = new(7608);
    internal static readonly ServiceActionResultCode NoCancellableUsage = new(7609);
    internal static readonly ServiceActionResultCode NothingToDiscard = new(7610);
    internal static readonly ServiceActionResultCode RandomizationUnavailable = new(7611);
    internal static readonly ServiceActionResultCode AlreadyInRequestedState = new(7612);
    internal static readonly ServiceActionResultCode ListUnavailable = new(7613);
    internal static readonly ServiceActionResultCode SourceUnavailable = new(7614);
    internal static readonly ServiceActionResultCode DestinationOutOfRange = new(7615);
    internal static readonly ServiceActionResultCode MutationPermitUnavailable = new(7616);
    internal static readonly ServiceActionResultCode MultiBuyUnavailable = new(7617);
    internal static readonly ServiceActionResultCode PostCommitFault = new(7618);
    internal static readonly ServiceActionResultCode VerificationFailed = new(7619);
}
