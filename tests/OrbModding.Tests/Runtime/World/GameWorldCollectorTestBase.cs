using System;
using System.Collections.Generic;
using OrbModding.Common.Runtime.World;

namespace OrbModding.Tests.Runtime.World;

public abstract partial class GameWorldCollectorTestBase : IDisposable
{
    protected GameWorldCollectorTestBase() => ClearRegistries();

    public void Dispose() => ClearRegistries();

    private static void ClearRegistries()
    {
        FakeResource.All.Clear();
        FakeStructure.All.Clear();
        FakeUpgrade.All.Clear();
        FakeResearch.All.Clear();
        FakePrerequisiteLink.All.Clear();
        FakeGameManager.currentFrame = 0;
        FakeNumber.All.Clear();
        FakeCount.All.Clear();
        FakeFlag.All.Clear();
        FakePlayerGlobals.Reset();
        FakeTargetingManager.Reset();
        FakeConsumableInventory.Reset();
        FakeChallengeManager.instance = new FakeChallengeManager();
        FakePersistentResetManager.instance = new FakePersistentResetManager();
        FakeSettingsManager.QueueMode = false;
        FakeSettingsManager.CancellableSpells = true;
        FakeGlobalVariables.SetMultiBuy(1);
        WorldCategoryFakes.Clear();
        FakeStructureType.All.Clear();
        FakeResearchType.All.Clear();
    }

    private protected static readonly Dictionary<string, Type?> Defaults = Build();

    /// <summary>
    /// Every category the collector walks, resolved to a stand-in. The categories these tests are
    /// about get the purpose-built types in <c>WorldCollectorFakes.cs</c>; the rest come from
    /// <see cref="WorldCategoryFakes"/>, so a complete pass really is complete.
    /// </summary>
    private static Dictionary<string, Type?> Build()
    {
        var byName = new Dictionary<string, Type?>(StringComparer.Ordinal)
        {
            ["ResourceSO"] = typeof(FakeResource),
            ["StructureSO"] = typeof(FakeStructure),
            ["UpgradeSO"] = typeof(FakeUpgrade),
            ["ResearchSO"] = typeof(FakeResearch),
            ["PrerequisiteLinkSO"] = typeof(FakePrerequisiteLink),
            ["GameManager"] = typeof(FakeGameManager),
            ["DoubleVariable"] = typeof(FakeNumber),
            ["IntVariable"] = typeof(FakeCount),
            ["BoolVariable"] = typeof(FakeFlag),
            ["AbstractListVariable"] = typeof(FakeAbstractListVariable),
            ["StructureTypeSO"] = typeof(FakeStructureType),
            ["StructureListVariable"] = typeof(FakeStructureListVariable),
            ["UpgradeListVariable"] = typeof(FakeUpgradeListVariable),

            // Not categories: the frame-wide globals reader resolves these two by name, and a
            // collector that cannot reach them silently prices every structure at parity.
            ["Player"] = typeof(FakePlayerGlobals),
            ["GlobalVariables"] = typeof(FakeGlobalVariables),
            ["ValueModifierRecord"] = typeof(FakeModifierRecord),
            ["TargetingManager"] = typeof(FakeTargetingManager),
            ["TargetingManager+TargetLink"] = typeof(FakeTargetingManager.TargetLink),
            ["ITooltipable"] = typeof(IFakeTooltipable),
            ["EffectResultInfo"] = typeof(FakeTargetingResultInfo),
            ["Inventory"] = typeof(FakeConsumableInventory),
            ["ConsumableRefListVariable"] = typeof(FakeConsumableRefListVariable),
            ["UICraftingPage"] = typeof(FakeCraftingPage),
            ["ChallengeManager"] = typeof(FakeChallengeManager),
            ["PersistentResetManager"] = typeof(FakePersistentResetManager),
            ["ChallengeListVariable"] = typeof(FakeChallengeList),
            ["SettingsManager"] = typeof(FakeSettingsManager),
            ["ResearchTypeSO"] = typeof(FakeResearchType),
            ["ResourceFillList"] = typeof(FakeResearchFillList),
            ["ResourceFillList+ResourceFillEntry"] = typeof(FakeResearchFillList.ResourceFillEntry),
        };

        foreach (var pair in WorldCategoryFakes.ByTypeName) byName[pair.Key] = pair.Value;
        return byName;
    }

    /// <summary>
    /// A collector over the purpose-built types. Overrides are keyed by the game type name the binder
    /// asks for, so a test can say "this category resolves to something else" or "this category
    /// resolves to nothing" — the latter being a distinct case the collector must report differently.
    /// </summary>
    private protected static GameWorldCollector Collector(params (string TypeName, Type? Type)[] overrides)
    {
        if (FakeConsumable.All.Count > 0)
        {
            var maximumId = Guid.NewGuid();
            FakeCount.All.Add(new FakeCount
            {
                Identity = maximumId,
                value = new FakeModifierRecord(FakeConsumable.All[0].maximumCarryLoad),
            });
            FakeIdRegistry.RuntimeLookup[
                new Guid("315471ca-0d15-455d-92da-f9d5f95a3c33")] =
                new FakeConsumableType
                {
                    Identity = new Guid("315471ca-0d15-455d-92da-f9d5f95a3c33"),
                    maximumCarryLoad = new FakeConsumableVariable { Identity = maximumId },
                };
        }
        var byName = new Dictionary<string, Type?>(Defaults, StringComparer.Ordinal);
        foreach (var (typeName, type) in overrides) byName[typeName] = type;
        return new GameWorldCollector(name => byName.TryGetValue(name, out var type) ? type : null);
    }

    private protected static FakeEffectBlock Block(object script)
    {
        var block = new FakeEffectBlock();
        block.effectScripts.Add(script);
        return block;
    }
}
