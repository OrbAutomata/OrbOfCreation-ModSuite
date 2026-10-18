using System;
using OrbModding.Common;

namespace OrbMentor;

/// <summary>
/// Complete binding set for the mastery hooks: each source's identity, mastery level and eligibility,
/// and the artifact's experience element that the container hook is matched against.
/// </summary>
internal sealed class MentorMasteryObservationBindings
{
    internal static readonly string[] ContractIds =
    {
        "loadout.spell-recipe.type-action",
        "alchemy-loadout.recipe.type-action",
        "equipment-loadout.equipment.type-action",
        "mentor.experience-element.type-action",
        "spell-recipe.mastery-level-action",
        "spell-recipe.is-discovered",
        "alchemy-recipe.mastery-level-action",
        "alchemy-recipe.is-available-action",
        "equipment.mastery-level-action",
        "equipment.is-created-action",
        "equipment.get-experience-element",
        "id-scriptable-object.get-guid-action",
    };

    private MentorMasteryObservationBindings(
        Func<object, Guid> spellId,
        Func<object, int> spellMastery,
        Func<object, bool> spellDiscovered,
        Func<object, Guid> alchemyId,
        Func<object, int> alchemyMastery,
        Func<object, bool> alchemyAvailable,
        Func<object, Guid> equipmentId,
        Func<object, int> equipmentMastery,
        Func<object, bool> equipmentCreated,
        Func<object, object?> experienceElement)
    {
        SpellId = spellId;
        SpellMastery = spellMastery;
        SpellDiscovered = spellDiscovered;
        AlchemyId = alchemyId;
        AlchemyMastery = alchemyMastery;
        AlchemyAvailable = alchemyAvailable;
        EquipmentId = equipmentId;
        EquipmentMastery = equipmentMastery;
        EquipmentCreated = equipmentCreated;
        ExperienceElement = experienceElement;
    }

    internal Func<object, Guid> SpellId { get; }
    internal Func<object, int> SpellMastery { get; }
    internal Func<object, bool> SpellDiscovered { get; }
    internal Func<object, Guid> AlchemyId { get; }
    internal Func<object, int> AlchemyMastery { get; }
    internal Func<object, bool> AlchemyAvailable { get; }
    internal Func<object, Guid> EquipmentId { get; }
    internal Func<object, int> EquipmentMastery { get; }
    internal Func<object, bool> EquipmentCreated { get; }
    internal Func<object, object?> ExperienceElement { get; }

    internal static bool TryCreate(out MentorMasteryObservationBindings? bindings, out string reason,
        Func<string, Type?>? resolveType = null, Func<string, bool>? includeContract = null) =>
        NativeBindingSet.TryBind("mentor mastery observation", ContractIds, resolveType, includeContract, set =>
        {
            var spell = set.Type("loadout.spell-recipe.type-action", "SpellRecipeSO");
            var alchemy = set.Type("alchemy-loadout.recipe.type-action", "AlchemyRecipeSO");
            var equipment = set.Type("equipment-loadout.equipment.type-action", "EquipmentSO");
            var element = set.Type("mentor.experience-element.type-action", "IExperienceElement");

            return new MentorMasteryObservationBindings(
                set.StableUuid("id-scriptable-object.get-guid-action", spell),
                set.Field<Func<object, int>>("spell-recipe.mastery-level-action", spell, "masteryLevel", typeof(int)),
                set.Call<Func<object, bool>>("spell-recipe.is-discovered", spell, "IsDiscovered", typeof(bool)),
                set.StableUuid("id-scriptable-object.get-guid-action", alchemy),
                set.Field<Func<object, int>>(
                    "alchemy-recipe.mastery-level-action", alchemy, "masteryLevel", typeof(int)),
                set.Call<Func<object, bool>>(
                    "alchemy-recipe.is-available-action", alchemy, "IsAvailable", typeof(bool)),
                set.StableUuid("id-scriptable-object.get-guid-action", equipment),
                set.Field<Func<object, int>>("equipment.mastery-level-action", equipment, "masteryLevel", typeof(int)),
                set.Field<Func<object, bool>>("equipment.is-created-action", equipment, "isCreated", typeof(bool)),
                set.Call<Func<object, object?>>(
                    "equipment.get-experience-element", equipment, "GetExperienceElement", element));
        }, out bindings, out reason);
}
