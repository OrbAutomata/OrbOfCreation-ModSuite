using System;
using OrbModding.Common;

namespace OrbAutomata;

/// <summary>
/// Reads the live native action-queue room on the main thread at execution time. The worker does not
/// bound its plan by the queue at all, and the room can change before an action runs (the player
/// queued something manually, or an earlier action in this batch committed), so this is the only
/// reading that decides admission: the action adapter re-reads it per submission, to bound every
/// press by the room the game actually has and to honour <c>LeaveQueueSlots</c> on the automated
/// cycle that owns that reserve.
/// </summary>
/// <remarks>
/// The reading is signed. The game's own upgrade button queues every level it bought without ever
/// consulting the queue, so a player pressing a multi-buy against a nearly full queue drives the
/// room below zero; that is a full queue with entries stacked past its maximum, not a contract that
/// failed to answer. The port hands the raw reading over and callers clamp it to "no free slots",
/// keeping <c>false</c> for the one case it means: no reading could be taken at all, and then
/// <c>reason</c> says why.
/// </remarks>
internal interface IAutoBuyQueueRoomPort
{
    bool TryReadRemainingRoom(out int remainingRoom, out GameActionReason reason);
}

internal sealed class AutoBuyNativeQueueRoomAdapter : IAutoBuyQueueRoomPort
{
    internal static readonly string[] ContractIds =
    {
        "auto-buy.action-manager.type-action",
        "action-manager.instance",
        "action-manager.get-remaining-room",
    };

    private readonly Bindings? _bindings;
    private readonly string _bindingFailure;

    public AutoBuyNativeQueueRoomAdapter(Func<string, bool>? includeContract = null) =>
        NativeBindingSet.TryBind("auto-buy queue room", ContractIds, null, includeContract, set =>
        {
            var actionManager = set.Type("auto-buy.action-manager.type-action", "ActionManager");
            return new Bindings(
                set.StaticField<Func<object?>>("action-manager.instance", actionManager, "instance", actionManager),
                set.StaticCall<Func<int>>(
                    "action-manager.get-remaining-room", actionManager, "GetRemainingRoom", typeof(int)));
        }, out _bindings, out _bindingFailure);

    /// <remarks>
    /// The game sets <c>ActionManager.instance</c> once, when the <c>Main</c> scene loads, and never
    /// clears it: before the first game it is null and <c>GetRemainingRoom</c> throws, and after
    /// leaving a game it is destroyed and the call answers the abandoned scene's number. Neither is a
    /// reading, so both answer that no game is loaded.
    /// </remarks>
    public bool TryReadRemainingRoom(out int remainingRoom, out GameActionReason reason)
    {
        remainingRoom = 0;
        reason = _bindingFailure;
        if (_bindings is not { } native)
            return false;
        if (!NativeObjectLiveness.IsAlive(native.Instance()))
        {
            reason = "no game is loaded: ActionManager.instance is absent or destroyed";
            return false;
        }

        try
        {
            remainingRoom = native.GetRemainingRoom();
            return true;
        }
        catch (Exception ex) when (NativeFailureClassifier.IsExpected(ex))
        {
            reason = GameActionFaultLog.Reason("The remaining queue room could not be read.", ex, "Auto Buy queue room");
            return false;
        }
    }

    private sealed record Bindings(Func<object?> Instance, Func<int> GetRemainingRoom);
}
