#if SERVICE_CYCLE_PROFILE
using System;
using System.Collections;
using OrbModding.Common.Runtime.World;
using UnityEngine;

namespace OrbAutomata.GameMcp;

/// <summary>
/// Process-lifetime native layout binding for the debug tooltip reader. It caches only a compiled
/// field accessor, never a Unity object; live tooltip objects are still resolved per command.
/// </summary>
internal sealed class GameMcpTooltipNativeAccess
{
    private readonly Func<object, IList?> _subTooltips;
    private readonly Type _spellType;
    private readonly Func<object, Guid> _spellRecipeId;
    private readonly Type _passiveAbilityType;
    private readonly Func<object, Guid> _passiveAbilityId;
    private readonly int _mainThreadId;

    private GameMcpTooltipNativeAccess(
        Func<object, IList?> subTooltips,
        Type spellType,
        Func<object, Guid> spellRecipeId,
        Type passiveAbilityType,
        Func<object, Guid> passiveAbilityId)
    {
        _subTooltips = subTooltips;
        _spellType = spellType;
        _spellRecipeId = spellRecipeId;
        _passiveAbilityType = passiveAbilityType;
        _passiveAbilityId = passiveAbilityId;
        _mainThreadId = Environment.CurrentManagedThreadId;
    }

    internal static bool TryCreate(
        Type? hoverTooltipType,
        Type? spellType,
        Type? passiveAbilityType,
        out GameMcpTooltipNativeAccess access,
        out string reason)
    {
        access = null!;
        if (hoverTooltipType is null)
        {
            reason = "HoverTooltip type was unavailable during MCP startup binding";
            return false;
        }
        var elementType = NativeAccessorBinder.CollectionElementType(
            hoverTooltipType,
            "subTooltips");
        var read = NativeAccessorBinder.CollectionField(hoverTooltipType, "subTooltips");
        if (elementType != typeof(ITooltipable) || read is null)
        {
            reason = "HoverTooltip.subTooltips was not the exact audited List<ITooltipable> field";
            return false;
        }

        var spellRecipeId = NativeAccessorBinder.CallReferenceGuid(spellType, "get_reference");
        if (spellType is null || spellRecipeId is null)
        {
            reason = "Spell.get_reference was not the exact audited recipe accessor";
            return false;
        }
        var passiveAbilityId =
            NativeAccessorBinder.CallReferenceGuid(passiveAbilityType, "get_reference");
        if (passiveAbilityType is null || passiveAbilityId is null)
        {
            reason = "PassiveAbility.get_reference was not the exact audited passive accessor";
            return false;
        }

        access = new GameMcpTooltipNativeAccess(
            read, spellType, spellRecipeId, passiveAbilityType, passiveAbilityId);
        reason = string.Empty;
        return true;
    }

    /// <summary>
    /// The stable UUID of the entity a hovered element is about, or <see cref="Guid.Empty"/> when
    /// the element is about no entity at all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Most panels assign the asset itself, which carries its own id. The two panels holding the
    /// things a player casts do not: the casting bar assigns the live <c>Spell</c> and the passive
    /// bar the live <c>PassiveAbility</c>, and neither is an <c>IdScriptableObject</c>. Both are
    /// one accessor away from the recipe asset they were built from — the same asset whose name
    /// they already print, because <c>GetName()</c> forwards to it — so the id on those rows is the
    /// game's own reference rather than anything reconstructed from the screen.
    /// </para>
    /// <para>
    /// A tooltipable of any other shape, and a live instance whose reference is null, answer empty:
    /// the row then carries no id rather than an id nothing answers to. A binding that cannot be
    /// taken at all is a contract failure and refuses the whole call, which is why that is
    /// <see langword="false"/> here and an empty id is not.
    /// </para>
    /// </remarks>
    internal bool TryReadEntityId(ITooltipable? item, out Guid uuid, out string reason)
    {
        uuid = Guid.Empty;
        if (Environment.CurrentManagedThreadId != _mainThreadId)
        {
            reason = "tooltip native access was rejected off the Unity startup thread";
            return false;
        }
        reason = string.Empty;
        if (item is null) return true;
        if (item is IdScriptableObject entity)
        {
            uuid = entity.GetGuid();
            return true;
        }

        try
        {
            if (_spellType.IsInstanceOfType(item)) uuid = _spellRecipeId(item);
            else if (_passiveAbilityType.IsInstanceOfType(item)) uuid = _passiveAbilityId(item);
        }
        catch (Exception exception)
        {
            uuid = Guid.Empty;
            reason = "reading the bound tooltip item's recipe reference failed: " +
                exception.GetBaseException().Message;
            return false;
        }
        return true;
    }

    /// <summary>
    /// Whether the player can actually hover this element.
    /// </summary>
    /// <remarks>
    /// Closing a modal never deactivates anything: <c>UIModal.SetElementVisibility(false)</c> drops
    /// the canvas group's alpha, interactivity, and raycasts, and that is the whole of it. Every
    /// modal the session has ever opened therefore stays active in the hierarchy forever, so a
    /// catalog filtered on Unity liveness alone answers with panels the player closed an hour ago.
    /// The game's own <c>IsOpen</c> is what separates the panel on screen from the ones behind it.
    /// </remarks>
    internal static bool OnScreen(Component element)
    {
        if (element is null) return false;
        for (var node = element.transform; node is not null; node = node.parent)
        {
            if (node.GetComponent<UIModal>() is { } modal && !modal.IsOpen()) return false;
        }
        return true;
    }

    internal bool TryReadSubTooltips(
        object hoverTooltip,
        out ITooltipable[] subTooltips,
        out string reason)
    {
        if (Environment.CurrentManagedThreadId != _mainThreadId)
        {
            subTooltips = Array.Empty<ITooltipable>();
            reason = "tooltip native access was rejected off the Unity startup thread";
            return false;
        }
        if (hoverTooltip is null)
        {
            subTooltips = Array.Empty<ITooltipable>();
            reason = "the live HoverTooltip reference was null";
            return false;
        }

        IList? values;
        try
        {
            values = _subTooltips(hoverTooltip);
        }
        catch (Exception exception)
        {
            subTooltips = Array.Empty<ITooltipable>();
            reason = "reading bound HoverTooltip.subTooltips failed: " +
                exception.GetBaseException().Message;
            return false;
        }
        if (values is null || values.Count == 0)
        {
            subTooltips = Array.Empty<ITooltipable>();
            reason = string.Empty;
            return true;
        }

        var result = new ITooltipable[values.Count];
        for (var index = 0; index < values.Count; index++)
        {
            if (values[index] is not ITooltipable item)
            {
                subTooltips = Array.Empty<ITooltipable>();
                reason = "bound HoverTooltip.subTooltips contained a non-ITooltipable entry";
                return false;
            }
            result[index] = item;
        }
        subTooltips = result;
        reason = string.Empty;
        return true;
    }
}
#endif
