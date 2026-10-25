using System.Collections.Generic;
using OrbModding.Common;

namespace OrbModding.Tests.Runtime.World;

internal static class WorldCaptureTestContext
{
    internal static IReadOnlyList<System.Guid> SeedScribeRelations()
    {
        global::AlchemyManager.instance = new global::AlchemyManager();
        var identities = new List<System.Guid>();
        void Register(System.Guid identity, global::IdScriptableObject value)
        {
            value.SetGuid(identity);
            global::IdScriptableObject.RuntimeLookup[identity] = value;
            identities.Add(identity);
        }

        Register(KnownEntities.ScribeCraftingRecipes.Uuid, new global::CraftingRecipeListVariable());
        Register(KnownEntities.ActiveScribeInstances.Uuid, new global::CraftingInstanceListVariable());
        Register(
            KnownEntities.AutoScribeInstances.Uuid,
            new global::CraftingInstanceListVariable { isAutoList = true });
        Register(
            KnownEntities.ScribeCrafting.Uuid,
            new global::CraftingRecipeTypeSO { maxStartingLevel = 1, isLevelType = true });
        Register(KnownEntities.ActiveHarvestElements.Uuid,
            new global::HarvestElementListVariable());
        Register(KnownEntities.ActiveHarvestActions.Uuid,
            new global::HarvestActionInstanceListVariable());
        Register(KnownEntities.Mana.Uuid, new global::ResourceSO());
        Register(KnownEntities.ResourceBarCore.Uuid, new global::ResourceListVariable());
        Register(KnownEntities.ResourceBarMaterials.Uuid, new global::ResourceListVariable());

        foreach (var (scrollId, enchantmentId) in new[]
                 {
                     (KnownEntities.ScrollAdvancement.Uuid, KnownEntities.EnchantAdvancement.Uuid),
                     (KnownEntities.ScrollDevelopment.Uuid, KnownEntities.EnchantDevelopment.Uuid),
                     (KnownEntities.ScrollEcho.Uuid, KnownEntities.EnchantEcho.Uuid),
                     (KnownEntities.ScrollExcellence.Uuid, KnownEntities.EnchantExcellence.Uuid),
                     (KnownEntities.ScrollInvestment.Uuid, KnownEntities.EnchantInvestment.Uuid),
                     (KnownEntities.ScrollLearning.Uuid, KnownEntities.EnchantLearning.Uuid),
                     (KnownEntities.ScrollPower.Uuid, KnownEntities.EnchantPower.Uuid),
                     (KnownEntities.ScrollSpeed.Uuid, KnownEntities.EnchantSpeed.Uuid),
                 })
        {
            var enchantment = new global::EnchantmentSO();
            Register(enchantmentId, enchantment);
            var targetBlock = new global::InstantEffectBlock();
            targetBlock.effectScripts.Add(new global::RequestTargetEffectScript
            {
                targetOptions = new global::Targeting.TargetSelectOptions
                {
                    Targeting = new global::Targeting.TargetStructure(),
                },
            });
            targetBlock.effectScripts.Add(new global::EnchantmentSO.EnchantItemScript
            {
                enchantment = enchantment,
            });
            var scroll = new global::ConsumableSO();
            scroll.onUseEffects.Add(targetBlock);
            Register(scrollId, scroll);
        }
        return identities;
    }
}
