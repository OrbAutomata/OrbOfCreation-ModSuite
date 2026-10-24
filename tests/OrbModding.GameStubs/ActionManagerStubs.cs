using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

public sealed class ActionManager : UnityEngine.MonoBehaviour
{
    public static ActionManager instance = new ActionManager();

    public ActionableListVariable actionableItems = new ActionableListVariable();

    public static int RemainingRoom { get; set; }

    /// <summary>Thrown by <see cref="GetRemainingRoom"/> when set; no game state does this.</summary>
    public static Exception? RemainingRoomFault { get; set; }

    /// <summary>Thrown by <see cref="CanLoadAction"/> when set; no game state does this.</summary>
    public static Exception? AdmissionFault { get; set; }

    public static int GetRemainingRoom()
    {
        if (RemainingRoomFault is { } fault) throw fault;
        _ = instance.actionableItems;
        return RemainingRoom;
    }

    /// <summary>
    /// The queue admission term used by both purchase kinds. Fixtures keep the answer on the
    /// candidate so two candidates can disagree in one test, while the call still has the native
    /// <c>ActionManager.CanLoadAction(IActionable)</c> shape. Like the game, both reads go through
    /// <c>instance</c>, so they throw while no manager is loaded.
    /// </summary>
    public static bool CanLoadAction(IActionable actionable)
    {
        if (AdmissionFault is { } fault) throw fault;
        _ = instance.actionableItems;
        return actionable switch
        {
            StructureSO structure => structure.purchasable,
            UpgradeSO upgrade => upgrade.purchasable,
            _ => false,
        };
    }
}
