using System;
using System.Collections.Generic;
using OrbModding.Common;
using OrbModding.Common.Runtime.World;
using OrbModding.GameStubs;

namespace OrbModding.Tests.Runtime.World;

public abstract partial class GameWorldCollectorTestBase
{
    protected GameWorldCollectorTestBase()
    {
        // Each test's assembly hook resets the native model. Seed only the authored identities this
        // collector fixture needs; every valid type still resolves through the shipping resolver.
        Player.ResourceOverflow = new DoubleVariable { value = new ValueModifierRecord(200d) };
        Player.ResourceOverflowLoss = new DoubleVariable { value = new ValueModifierRecord(100d) };
        Player.ResetTimePassed = new DoubleVariable { value = new ValueModifierRecord(60d) };
        GlobalVariables.MultiBuy.Value = 1;
        GlobalValues.instance.spellTypeXpPerMastery = 30d;
        AlchemyManager.instance = new AlchemyManager();
        RitualManager.instance = new RitualManager();
        SeedScribeRelations();
        SeedHarvestLifecycle();
        SeedResourceBar();
    }

    private protected static GameWorldCollector Collector(params (string TypeName, Type? Type)[] overrides)
    {
        if (ConsumableSO.All.Count > 0)
        {
            var maximumId = Guid.NewGuid();
            IntVariable.All.Add(new IntVariable
            {
                Identity = maximumId,
                Value = ConsumableSO.All[0].MaximumCarryLoad,
            });
            IdScriptableObject.RuntimeLookup[
                new Guid("315471ca-0d15-455d-92da-f9d5f95a3c33")] =
                new ConsumableTypeSO
                {
                    Identity = new Guid("315471ca-0d15-455d-92da-f9d5f95a3c33"),
                    maximumCarryLoad = new IntVariable { Identity = maximumId },
                };
        }
        if (overrides.Length == 0) return new GameWorldCollector();
        return new GameWorldCollector(name =>
        {
            foreach (var (typeName, type) in overrides)
                if (typeName == name) return type;
            return WorldNativeTypes.Resolve(name);
        });
    }

    private protected static PersistentEffectBlock Block(object script)
    {
        var block = new PersistentEffectBlock();
        block.effectScripts.Add((IPersistentEffectScript)script);
        return block;
    }
    private static void SeedResourceBar()
    {
        IdScriptableObject.RuntimeLookup[KnownEntities.Mana.Uuid] =
            new ResourceSO { Identity = KnownEntities.Mana.Uuid };
        IdScriptableObject.RuntimeLookup[KnownEntities.ResourceBarCore.Uuid] = new ResourceListVariable();
        IdScriptableObject.RuntimeLookup[KnownEntities.ResourceBarMaterials.Uuid] =
            new ResourceListVariable();
    }

    private static void SeedScribeRelations()
    {
        IdScriptableObject.RuntimeLookup[KnownEntities.ScribeCraftingRecipes.Uuid] =
            new CraftingRecipeListVariable();
        IdScriptableObject.RuntimeLookup[KnownEntities.ActiveScribeInstances.Uuid] =
            new CraftingInstanceListVariable();
        IdScriptableObject.RuntimeLookup[KnownEntities.AutoScribeInstances.Uuid] =
            new CraftingInstanceListVariable { isAutoList = true };
        IdScriptableObject.RuntimeLookup[KnownEntities.ScribeCrafting.Uuid] =
            new CraftingRecipeTypeSO
            {
                Identity = KnownEntities.ScribeCrafting.Uuid,
                maxStartingLevel = 1,
            };

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
            var enchantment = new EnchantmentSO { Identity = enchantmentId };
            var block = new InstantEffectBlock();
            block.effectScripts.Add(new RequestTargetEffectScript());
            block.effectScripts.Add(new EnchantmentSO.EnchantItemScript
            {
                enchantment = enchantment,
            });
            var scroll = new ConsumableSO { Identity = scrollId };
            scroll.onUseEffects.Add(block);
            IdScriptableObject.RuntimeLookup[enchantmentId] = enchantment;
            IdScriptableObject.RuntimeLookup[scrollId] = scroll;
        }
    }

    internal static HarvestElementListVariable ActiveHarvestElements { get; private set; } = new();
    internal static HarvestActionInstanceListVariable ActiveHarvestActions { get; private set; } = new();

    private static void SeedHarvestLifecycle()
    {
        ActiveHarvestElements = new HarvestElementListVariable();
        ActiveHarvestActions = new HarvestActionInstanceListVariable();
        IdScriptableObject.RuntimeLookup[Guid.Parse("5a9f8001-3ae2-4799-86b6-5198763e0fe2")] =
            ActiveHarvestElements;
        IdScriptableObject.RuntimeLookup[Guid.Parse("e4a9d4c3-61cc-4f94-bab9-7bc8e841cc32")] =
            ActiveHarvestActions;
    }

    private protected sealed class RitualMissingKeywords
    {
        public static readonly List<RitualMissingKeywords> All = new();

        public Guid Identity = Guid.NewGuid();

        public Guid GetGuid() => Identity;
    }

    private protected sealed class PlayerMissingQualityBonus
    {
        private static readonly PlayerMissingQualityBonus _instance = new();
        private IntVariable spellOutputLevel = new() { Value = 1 };
        public IntVariable maxSpellOutputLevel = new() { Value = 100 };
        private IntVariable reserveLevel = new() { Value = 1 };
        public IntVariable maxReserveLevel = new() { Value = 100 };
        public IntVariable druidryOutputLevel = new() { Value = 1 };
        public IntVariable maxDruidryOutputLevel = new() { Value = 1 };
        public IntVariable alchemyOutputLevel = new() { Value = 1 };
        public IntVariable maxAlchemyOutputLevel = new() { Value = 1 };

        private PlayerMissingQualityBonus()
        {
        }

        public static DoubleVariable GetResourceOverflow() =>
            Player.GetResourceOverflow();

        public static DoubleVariable GetResourceOverflowLoss() =>
            Player.GetResourceOverflowLoss();

        public static DoubleVariable GetResetTimePassed() =>
            Player.GetResetTimePassed();

        public static DoubleVariable GetStructureCost() => Player.GetStructureCost();

        public static IntVariable GetSpellOutputLevel() => _instance.spellOutputLevel;

        public static IntVariable GetReserveLevel() => _instance.reserveLevel;

        public static DoubleVariable GetSpellCriticalCastRating() =>
            Player.GetSpellCriticalCastRating();

        public static DoubleVariable GetSpellCriticalCastEffect() =>
            Player.GetSpellCriticalCastEffect();

        public static DoubleVariable GetSpellDoubleCastRating() =>
            Player.GetSpellDoubleCastRating();

        public static DoubleVariable GetSpellDoubleCastEffect() =>
            Player.GetSpellDoubleCastEffect();
    }

    private protected sealed class PartialResource
    {
        public static readonly List<PartialResource> All = new();

        public ValueModifierRecord maxQuantity = new(-1d);

        public Guid GetGuid() => Guid.NewGuid();

        public BigDouble GetQuantity() => default;
    }
    private protected sealed class UnexpectedAlchemyInstance : AlchemyInstance
    {
        internal UnexpectedAlchemyInstance(AlchemyRecipeSO recipe) : base(recipe) { }
    }
    private protected sealed class ForeignQueueSlot : PlotNodeActionInstance { }


    private protected static ResourceCostList CostOf(params (Guid Resource, double Amount)[] entries)
    {
        var list = new ResourceCostList();
        foreach (var (resource, amount) in entries) list.costs.Add(new ResourceTuple(resource, amount));
        return list;
    }

    private protected static ModifierListRef LevelModifiers(
        ValueModifier[] modifiers,
        ValueModifier[]? exponents = null)
    {
        var list = new ValueModifierList();
        list.modifiers.AddRange(modifiers);
        if (exponents is not null) list.exponents.AddRange(exponents);
        return new ModifierListRef { variable = new ModifierListVariable { value = list } };
    }

    /// <summary>Over its cap with an active rate, which is the branch the frame-wide terms feed.</summary>
    private protected static ResourceSO OverflowingResource(Guid identity) =>
        new()
        {
            Identity = identity,
            Quantity = new BigDouble(150d),
            maxQuantity = new ValueModifierRecord(100d),
            rate = new ValueModifierRecord(10d, activeCount: 1),
            lifetimeQuantity = new BigDouble(400d),
            Visible = true,
        };
    private protected static void SetOverflow(double value) => Player.ResourceOverflow.value = new ValueModifierRecord(value);
    private protected static void SetStructureCost(double value) => Player.StructureCost.value = new ValueModifierRecord(value);
    private protected static void SetAttributeQualityBonus(double value) => Player.AttributeQualityBonus.value = new ValueModifierRecord(value);
    private protected static void SetMaximumSpellOutputLevel(IntVariable value) => Player.Current.maxSpellOutputLevel = value;
    private protected static ResourceCostList AuthoredSpellLevelCost() => CostOf((KnownEntities.Mana.Uuid, 9d));
}
