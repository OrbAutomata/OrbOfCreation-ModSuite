using System;
using System.Reflection;
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
/// keeping <c>false</c> for the one case it means: no reading could be taken at all.
/// </remarks>
internal interface IAutoBuyQueueRoomPort
{
    bool TryReadRemainingRoom(out int remainingRoom);
}

internal sealed class AutoBuyNativeQueueRoomAdapter : IAutoBuyQueueRoomPort
{
    private MethodInfo? _getRemainingRoom;

    public bool TryReadRemainingRoom(out int remainingRoom)
    {
        remainingRoom = 0;
        if (!TryResolveContract())
            return false;

        try
        {
            if (_getRemainingRoom!.Invoke(null, Array.Empty<object>()) is int room)
            {
                remainingRoom = room;
                return true;
            }
        }
        catch (Exception ex) when (
            ex is TargetInvocationException || ex is ArgumentException ||
            ex is InvalidOperationException || ex is TargetException || ex is MemberAccessException)
        {
        }

        return false;
    }

    private bool TryResolveContract()
    {
        if (_getRemainingRoom is not null)
            return true;

        var actionManagerType = ReflectionUtil.FindLoadedType("ActionManager");
        var getRemainingRoom = actionManagerType?.GetMethod(
            "GetRemainingRoom", BindingFlags.Static | BindingFlags.Public, null, Type.EmptyTypes, null);
        if (getRemainingRoom is null || getRemainingRoom.ReturnType != typeof(int))
            return false;

        _getRemainingRoom = getRemainingRoom;
        return true;
    }
}
