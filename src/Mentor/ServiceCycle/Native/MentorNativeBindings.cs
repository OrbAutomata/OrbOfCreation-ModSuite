using System;
using OrbModding.Common;

namespace OrbMentor;

/// <summary>
/// Complete binding set for Mentor's grants: each recipient type's mastery level, saved XP, grant
/// and availability gate, and the artifact's experience container with the members that roll its
/// levels into the equipment.
/// </summary>
/// <remarks>
/// <c>EquipmentSO.GetExperienceElement()</c> is declared as <c>IExperienceElement</c>, which names
/// neither the level nor the gained-levels read, so the container members bind on
/// <c>ExperienceContainer</c>, the type of the field that method returns.
/// </remarks>
internal sealed class MentorNativeBindings
{
    internal static readonly string[] ContractIds =
    {
        "loadout.spell-recipe.type-action",
        "alchemy-loadout.recipe.type-action",
        "equipment-loadout.equipment.type-action",
        "mentor.experience-element.type-action",
        "mentor.experience-container.type-action",
        "spell-recipe.mastery-level-action",
        "spell-recipe.mastery-experience-action",
        "spell-recipe.gain-mastery-action",
        "spell-recipe.is-discovered",
        "alchemy-recipe.mastery-level-action",
        "alchemy-recipe.mastery-xp-action",
        "alchemy-recipe.gain-mastery-runtime",
        "alchemy-recipe.is-available-action",
        "equipment.mastery-level-action",
        "equipment.mastery-xp-action",
        "equipment.is-created-action",
        "equipment.get-experience-element",
        "equipment.gain-levels",
        "experience-container.gain-runtime",
        "experience-container.gained-levels",
        "experience-container.get-level",
        "experience-container.get-experience",
    };

    private MentorNativeBindings(
        Type spellType,
        Type alchemyType,
        Type equipmentType,
        Func<object, int> spellMastery,
        Func<object, BigDouble> spellExperience,
        Action<object, BigDouble> spellGain,
        Func<object, bool> spellDiscovered,
        Func<object, int> alchemyMastery,
        Func<object, BigDouble> alchemyExperience,
        Action<object, BigDouble> alchemyGain,
        Func<object, bool> alchemyAvailable,
        Func<object, int> equipmentMastery,
        Func<object, BigDouble> equipmentXp,
        Action<object, BigDouble> setEquipmentXp,
        Func<object, bool> equipmentCreated,
        Func<object, object?> experienceElement,
        Action<object, int> gainMasteryLevels,
        Action<object, BigDouble> containerGain,
        Func<object, int> containerGainedLevels,
        Func<object, int> containerLevel,
        Func<object, BigDouble> containerExperience)
    {
        SpellType = spellType;
        AlchemyType = alchemyType;
        EquipmentType = equipmentType;
        SpellMastery = spellMastery;
        SpellExperience = spellExperience;
        SpellGain = spellGain;
        SpellDiscovered = spellDiscovered;
        AlchemyMastery = alchemyMastery;
        AlchemyExperience = alchemyExperience;
        AlchemyGain = alchemyGain;
        AlchemyAvailable = alchemyAvailable;
        EquipmentMastery = equipmentMastery;
        EquipmentXp = equipmentXp;
        SetEquipmentXp = setEquipmentXp;
        EquipmentCreated = equipmentCreated;
        ExperienceElement = experienceElement;
        GainMasteryLevels = gainMasteryLevels;
        ContainerGain = containerGain;
        ContainerGainedLevels = containerGainedLevels;
        ContainerLevel = containerLevel;
        ContainerExperience = containerExperience;
    }

    internal Type SpellType { get; }
    internal Type AlchemyType { get; }
    internal Type EquipmentType { get; }
    internal Func<object, int> SpellMastery { get; }
    internal Func<object, BigDouble> SpellExperience { get; }
    internal Action<object, BigDouble> SpellGain { get; }
    internal Func<object, bool> SpellDiscovered { get; }
    internal Func<object, int> AlchemyMastery { get; }
    internal Func<object, BigDouble> AlchemyExperience { get; }
    internal Action<object, BigDouble> AlchemyGain { get; }
    internal Func<object, bool> AlchemyAvailable { get; }
    internal Func<object, int> EquipmentMastery { get; }
    internal Func<object, BigDouble> EquipmentXp { get; }
    internal Action<object, BigDouble> SetEquipmentXp { get; }
    internal Func<object, bool> EquipmentCreated { get; }
    internal Func<object, object?> ExperienceElement { get; }
    internal Action<object, int> GainMasteryLevels { get; }
    internal Action<object, BigDouble> ContainerGain { get; }
    internal Func<object, int> ContainerGainedLevels { get; }
    internal Func<object, int> ContainerLevel { get; }
    internal Func<object, BigDouble> ContainerExperience { get; }

    internal static bool TryCreate(out MentorNativeBindings? bindings, out string reason,
        Func<string, Type?>? resolveType = null, Func<string, bool>? includeContract = null) =>
        NativeBindingSet.TryBind("mentor", ContractIds, resolveType, includeContract, set =>
        {
            var spell = set.Type("loadout.spell-recipe.type-action", "SpellRecipeSO");
            var alchemy = set.Type("alchemy-loadout.recipe.type-action", "AlchemyRecipeSO");
            var equipment = set.Type("equipment-loadout.equipment.type-action", "EquipmentSO");
            var element = set.Type("mentor.experience-element.type-action", "IExperienceElement");
            var container = set.Type("mentor.experience-container.type-action", "ExperienceContainer");

            return new MentorNativeBindings(
                spell,
                alchemy,
                equipment,
                set.Field<Func<object, int>>("spell-recipe.mastery-level-action", spell, "masteryLevel", typeof(int)),
                set.Field<Func<object, BigDouble>>(
                    "spell-recipe.mastery-experience-action", spell, "masteryExperience", typeof(BigDouble)),
                set.Call<Action<object, BigDouble>>(
                    "spell-recipe.gain-mastery-action", spell, "GainMasteryExp", typeof(void), typeof(BigDouble)),
                set.Call<Func<object, bool>>("spell-recipe.is-discovered", spell, "IsDiscovered", typeof(bool)),
                set.Field<Func<object, int>>(
                    "alchemy-recipe.mastery-level-action", alchemy, "masteryLevel", typeof(int)),
                set.Field<Func<object, BigDouble>>(
                    "alchemy-recipe.mastery-xp-action", alchemy, "masteryXp", typeof(BigDouble)),
                set.Call<Action<object, BigDouble>>(
                    "alchemy-recipe.gain-mastery-runtime", alchemy, "GainMasteryXp", typeof(void), typeof(BigDouble)),
                set.Call<Func<object, bool>>(
                    "alchemy-recipe.is-available-action", alchemy, "IsAvailable", typeof(bool)),
                set.Field<Func<object, int>>("equipment.mastery-level-action", equipment, "masteryLevel", typeof(int)),
                set.Field<Func<object, BigDouble>>(
                    "equipment.mastery-xp-action", equipment, "masteryXp", typeof(BigDouble)),
                set.Setter<Action<object, BigDouble>>(
                    "equipment.mastery-xp-action", equipment, "masteryXp", typeof(BigDouble)),
                set.Field<Func<object, bool>>("equipment.is-created-action", equipment, "isCreated", typeof(bool)),
                set.Call<Func<object, object?>>(
                    "equipment.get-experience-element", equipment, "GetExperienceElement", element),
                set.Call<Action<object, int>>(
                    "equipment.gain-levels", equipment, "GainMasteryLevels", typeof(void), typeof(int)),
                set.Call<Action<object, BigDouble>>(
                    "experience-container.gain-runtime", container, "GainExperience", typeof(void), typeof(BigDouble)),
                set.Call<Func<object, int>>(
                    "experience-container.gained-levels", container, "GetGainedLevels", typeof(int)),
                set.Call<Func<object, int>>("experience-container.get-level", container, "GetLevel", typeof(int)),
                set.Call<Func<object, BigDouble>>(
                    "experience-container.get-experience", container, "GetExperience", typeof(BigDouble)));
        }, out bindings, out reason);
}
