using System;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;

namespace OrbAutomata;

/// <summary>
/// Lifecycle-bound mutation boundary for every level dial: Output Lv and Reserve Lv on Magic >
/// Casting, Druidry Lv on World > Agromancy and Alchemy Lv on Alchemy > Alchemy > Loadout. All four are a
/// <c>Player</c> <c>IntVariable</c> beside the cap a Raise upgrade lifts, worked by the same
/// <c>UINumberVariableAdjuster</c>.
/// </summary>
internal sealed class SpellCompositionGameAction : NativeGameAction<SpellCompositionNativeBindings>
{
    private static readonly GameActionCodes Codes = new(
        SpellCompositionActionResultCodes.WrongThread,
        SpellCompositionActionResultCodes.ContractUnavailable,
        SpellCompositionActionResultCodes.MutationPermitUnavailable,
        SpellCompositionActionResultCodes.PostCommitFault,
        SpellCompositionActionResultCodes.VerificationFailed);

    private readonly TypedRegistryResolver _registry;

    internal SpellCompositionGameAction(
        Func<long> readLifecycleEpoch,
        Func<bool> tryCaptureMutationPermit,
        Func<string> readOwnershipFailure,
        Func<string, Type?>? resolveType = null,
        Func<string, bool>? includeContract = null,
        TypedRegistryResolver? registry = null)
        : base("Level dial", Screen(LevelDial.Output), Codes, readLifecycleEpoch,
            tryCaptureMutationPermit, readOwnershipFailure,
            (out SpellCompositionNativeBindings? bindings, out string reason) =>
                SpellCompositionNativeBindings.TryCreate(resolveType ?? ReflectionUtil.FindLoadedType,
                    includeContract ?? (_ => true), out bindings, out reason))
    {
        _registry = registry ?? TypedRegistryResolver.Shared;
    }

    internal SpellCompositionSubmission Submit(in SpellCompositionAction action)
    {
        var request = action;
        return Admit(action.LifecycleEpoch, static outcome => new SpellCompositionSubmission(outcome),
            native => SetDial(in request, native), Screen(action.Dial));
    }

    private SpellCompositionSubmission SetDial(
        in SpellCompositionAction action,
        SpellCompositionNativeBindings native)
    {
        if (!TryAdmitControl(native, action.Dial, out var lockedPreflight, out var lockedReason))
            return SpellCompositionSubmission.Reject(lockedPreflight, lockedReason);
        var player = native.ReadPlayer();
        var variable = player is null ? null : ReadVariable(native, player, action.Dial);
        if (player is null || variable is null)
            return SpellCompositionSubmission.Reject(
                SpellCompositionActionResultCodes.ContractUnavailable,
                "The game has not set up " + Name(action.Dial) + " yet.");
        if (action.ExpectedIdentity is { } identity && native.ReadVariableIdentity(variable) != identity)
            return SpellCompositionSubmission.Reject(SpellCompositionActionResultCodes.StateChanged,
                GameActionAnswer.Replaced(Name(action.Dial)));
        var current = native.ReadInt(variable);
        if (action.ExpectedCurrentLevel is { } expected && current != expected)
            return SpellCompositionSubmission.Reject(SpellCompositionActionResultCodes.StateChanged,
                Name(action.Dial) + " changed since the automation reading; waiting for a fresh world.");
        var maximum = native.ReadInt(ReadMaximumVariable(native, player, action.Dial));
        var minimum = WorldSpellWorkbench.MinimumDialLevel;
        if (action.Value < minimum || action.Value > maximum)
            return SpellCompositionSubmission.OutOfRange(
                Name(action.Dial) + " " + action.Value + " is outside the range the game allows, " +
                minimum + " to " + maximum + ".",
                minimum,
                maximum);
        if (current == action.Value)
            return SpellCompositionSubmission.Reject(
                SpellCompositionActionResultCodes.AlreadyInRequestedState,
                "The " + Name(action.Dial) + " is already " + current + ".");
        if (!TryPermit(out var refusal)) return new SpellCompositionSubmission(refusal);

        var value = action.Value;
        return new SpellCompositionSubmission(Execute(Name(action.Dial),
            "The " + Name(action.Dial) + " is now " + value + ".", current,
            () => native.ReadInt(variable),
            () => native.SetInt(variable, value),
            (_, after) => after == value,
            Screen(action.Dial)));
    }

    /// <summary>
    /// Druidry Lv and Alchemy Lv are drawn only once their unlock is bought: each adjuster sits in
    /// a view whose one prerequisite is that upgrade. <c>ViewSO.IsAvailable()</c> is the game's
    /// own question, so the lock is read, never inferred. The Casting dials have no such view.
    /// </summary>
    private bool TryAdmitControl(
        SpellCompositionNativeBindings native,
        LevelDial dial,
        out ServiceActionResultCode refusal,
        out string reason)
    {
        refusal = CommonActionResultCodes.Committed;
        reason = string.Empty;
        var (viewId, unlock) = dial switch
        {
            LevelDial.Druidry => (KnownEntities.WorldAgromancyDruidryLvs.Uuid, "Unlock Druidry Levels"),
            LevelDial.Alchemy => (KnownEntities.AlchAlchemyLevel.Uuid, "Level Alchemy"),
            _ => (Guid.Empty, string.Empty),
        };
        if (viewId == Guid.Empty) return true;
        var resolution = _registry.Resolve(viewId, native.ViewType);
        if (!resolution.IsResolved || !_registry.IsCurrent(resolution) ||
            resolution.Value is not { } view)
        {
            refusal = SpellCompositionActionResultCodes.ContractUnavailable;
            reason = "The game's " + Name(dial) + " adjuster could not be read, so whether " +
                Screen(dial) + " draws it at all is unknown.";
            return false;
        }
        if (native.IsViewAvailable(view)) return true;
        refusal = SpellCompositionActionResultCodes.ControlLocked;
        reason = Name(dial) + " is locked: " + Screen(dial) + " draws no " + Name(dial) +
            " adjuster until " + unlock + " is bought.";
        return false;
    }

    private static object? ReadVariable(
        SpellCompositionNativeBindings native,
        object player,
        LevelDial dial) => dial switch
        {
            LevelDial.Output => native.ReadOutputVariable(),
            LevelDial.Reserve => native.ReadReserveVariable(),
            LevelDial.Druidry => native.ReadDruidryVariable(player),
            _ => native.ReadAlchemyVariable(player),
        };

    private static object ReadMaximumVariable(
        SpellCompositionNativeBindings native,
        object player,
        LevelDial dial) => dial switch
        {
            LevelDial.Output => native.ReadMaximumOutputVariable(player),
            LevelDial.Reserve => native.ReadMaximumReserveVariable(player),
            LevelDial.Druidry => native.ReadMaximumDruidryVariable(player),
            _ => native.ReadMaximumAlchemyVariable(player),
        };

    private static string Name(LevelDial dial) => dial switch
    {
        LevelDial.Output => "Output Level",
        LevelDial.Reserve => "Reserve Level",
        LevelDial.Druidry => "Druidry Lv",
        _ => "Alchemy Lv",
    };

    private static string Screen(LevelDial dial) => dial switch
    {
        LevelDial.Druidry => "World > Agromancy",
        LevelDial.Alchemy => "Alchemy > Alchemy > Loadout",
        _ => "Magic > Casting",
    };
}
