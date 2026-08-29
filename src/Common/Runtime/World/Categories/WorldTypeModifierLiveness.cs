using System;

namespace OrbModding.Common.Runtime.World;

/// <summary>
/// Whether the game's own code can read a type-level modifier record into a computation at all.
/// </summary>
/// <remarks>
/// <para>
/// A record is <b>live</b> when at least one path exists for the game to reach it: an accessor arm
/// resolves the authored ref name onto it, a reachable getter or pull site reads it, or a
/// <c>Register*</c> site reads it to push its modifiers into member records. This is capability
/// rather than current usage — a record an authored upgrade <i>could</i> reach through a router arm
/// is live even when nothing targets it today, because the arm is what makes targeting possible.
/// </para>
/// <para>
/// A record with no such path is <b>dead</b>, and the suite mirrors the game exactly: it stays
/// captured as a raw fact, because publication carries what the build holds, and it is never folded
/// into a total, a contribution list or a keyword closure, and never presented on a decision surface
/// as something investment can move. Printing a number for machinery no purchase can touch is
/// offering a lever that is not connected.
/// </para>
/// <para>
/// The four below are dead on the pinned build (<c>sha256 46b723ad…</c>, <c>mvid 2756e06e…</c>),
/// each because every place its field is touched is either a store in the constructor, a load handed
/// straight to <c>ModifierRecord.Clear()</c> in <c>ResetData</c>, or a load inside a method nothing
/// in the assembly dispatches to:
/// </para>
/// <list type="bullet">
/// <item><c>SpellTypeSO.bonusFlashRate</c> and <c>SpellTypeSO.flashEffectMod</c> — the class's
/// <c>GetValueModifierRecord</c> routes twenty of its twenty-two refs and drops both flash names to
/// <c>ldnull</c>, so no authored effect can name them and
/// <c>UpgradeableObject.GetFilteredPropertyNames</c> drops them from the tooltip as
/// <c>HasNoInfo()</c>. Their getters have no callers, and
/// <c>Spell.GetSpellTypeBonusFlashRate</c> reads <c>GetBonusCritRate()</c> instead.</item>
/// <item><c>EquipmentTypeSO.masteryLevel</c> — no accessor arm ("Power" and "TypeSlots" are the
/// class's whole router), no getter, and no registration site; nothing in the assembly loads it.</item>
/// <item><c>PlotNodeTypeSO.totalLevel</c> — its only reader is <c>AddToLevel</c>, which is
/// non-virtual with no callers, and the class's own <c>GetLevel()</c> returns a constant 1.</item>
/// </list>
/// <para>
/// The default is live, and deliberately so. IL can prove a path exists; it cannot prove one does
/// not exist through a reflective or data-driven route it never sees. So anything the census cannot
/// decide keeps its number, and only a record with no path at all loses one. The classification is
/// re-derived from the pinned assembly by the contract census rather than trusted here, which is
/// what makes a game build that adds the missing arms flip the record live by re-census instead of
/// by hand.
/// </para>
/// </remarks>
internal static class WorldTypeModifierLiveness
{
    /// <summary>Whether the game can read this record into a computation on the pinned build.</summary>
    internal static bool IsLive(WorldTypeModifierOwnerKind kind, string property)
    {
        if (property is null) throw new ArgumentNullException(nameof(property));
        return (kind, property) switch
        {
            (WorldTypeModifierOwnerKind.SpellType, "bonusFlashRate") => false,
            (WorldTypeModifierOwnerKind.SpellType, "flashEffectMod") => false,
            (WorldTypeModifierOwnerKind.EquipmentType, "masteryLevel") => false,
            (WorldTypeModifierOwnerKind.PlotNodeType, "totalLevel") => false,
            _ => true,
        };
    }
}
