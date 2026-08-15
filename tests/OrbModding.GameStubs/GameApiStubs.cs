using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;

public sealed class IntVariable
{
    // The game keeps every global integer in one static registry, and world collection walks it
    // rather than naming the hundred-odd accessors that read out of it.
    public static List<IntVariable> All = new List<IntVariable>();
    public Guid uuid = Guid.NewGuid();
    public ValueModifierRecord value = new ValueModifierRecord(new BigDouble(0.0, 0));
    public bool isPercentVariable;

    public Guid GetGuid() => uuid;

    // The game's AsInt() reads out of `value`; storing the answer separately let a fixture set a
    // count the collector could never see. Setting either keeps both in step.
    public int Value
    {
        get => count;
        set
        {
            count = value;
            this.value = new ValueModifierRecord(new BigDouble(value, 0));
        }
    }

    private int count = 1;

    public int SetCalls { get; private set; }

    public int? ThrowBeforeWriteFor { get; set; }

    public int? ThrowAfterWriteFor { get; set; }

    public int AsInt() => Value;

    public void SetValue(int value)
    {
        SetCalls++;
        if (ThrowBeforeWriteFor == value)
        {
            throw new InvalidOperationException($"setter rejected {value} before write");
        }

        Value = value;
        if (ThrowAfterWriteFor == value)
        {
            throw new InvalidOperationException($"setter rejected {value} after write");
        }
    }

    internal static IntVariable Register(Guid id)
    {
        var variable = new IntVariable { uuid = id };
        All.Add(variable);
        return variable;
    }

    /// <summary>
    /// Swaps a well-known global, keeping the registry to one entry per identity. Tests replace
    /// these wholesale between cases; the game never does, and a registry that grew one entry per
    /// reset would hand world collection a different answer on every pass.
    /// </summary>
    internal static IntVariable Replace(IntVariable current, IntVariable replacement, Guid id)
    {
        All.Remove(current);
        replacement.uuid = id;
        All.Add(replacement);
        return replacement;
    }
}

public static class GlobalVariables
{
    private static IntVariable multiBuy = IntVariable.Register(KnownVariableIds.MultiBuy);
    private static readonly StructureTypeSO GlobalStructureType = new StructureTypeSO();
    private static readonly AttributeSO CastingSpeedAttribute = new AttributeSO();
    private static readonly AttributeSO HarvestSpeedAttribute = new AttributeSO();
    private static readonly AttributeSO MasteryExperienceAttribute = new AttributeSO();

    // Registered in IntVariable.All under the uuid the game ships, because world collection finds
    // these by identity in the registry rather than through the accessor beside them.
    public static IntVariable MultiBuy
    {
        get => multiBuy;
        set => multiBuy = IntVariable.Replace(multiBuy, value, KnownVariableIds.MultiBuy);
    }

    public static IntVariable GetMultiBuy() => MultiBuy;
    public static StructureTypeSO GetGlobalStructureType() => GlobalStructureType;
    public static AttributeSO GetCastingSpeedAttr() => CastingSpeedAttribute;
    public static AttributeSO GetHarvestSpeedAttr() => HarvestSpeedAttribute;
    public static AttributeSO GetMasteryExpAttr() => MasteryExperienceAttribute;
    public static List<UnityEngine.Sprite> CustomSprites { get; } = new List<UnityEngine.Sprite>
    {
        new UnityEngine.Sprite(),
        new UnityEngine.Sprite(),
    };
    public static List<UnityEngine.Color> CustomColors { get; } = new List<UnityEngine.Color>
    {
        new UnityEngine.Color(),
        new UnityEngine.Color(),
    };
    public static List<UnityEngine.Sprite> GetCustomSprites() => CustomSprites;
    public static List<UnityEngine.Color> GetCustomColors() => CustomColors;
}

public sealed class SettingsManager
{
    public static SettingsManager instance = new SettingsManager();

    public BoolVariable enableQueueResearch = new BoolVariable();
    public BoolVariable cancellableSpells = new BoolVariable { value = true };
    public StringVariable numDisplay = new StringVariable { value = "Named" };

    public static bool ResearchQueueMode
    {
        get => instance.enableQueueResearch.GetValue();
        set => instance.enableQueueResearch.SetValue(value);
    }

    public static bool CancellableSpells
    {
        get => instance.cancellableSpells.GetValue();
        set => instance.cancellableSpells.SetValue(value);
    }

    public static bool IsResearchQueueMode() => ResearchQueueMode;
    public static bool CanCancelSpells() => CancellableSpells;
    public static string GetNumberDisplayOption() => instance.numDisplay.GetValue();
}

public static class KnownVariableIds
{
    public static readonly Guid MultiBuy = new Guid("37a84399-98b5-463c-b858-c1ecf2f9bf34");
    public static readonly Guid BulkDevelopment = new Guid("0ed119bf-0449-4d64-9989-1a3f68c7b8a2");
}

public sealed class ActionableListVariable : StackableListVariable<IActionable>
{
    public IntVariable maxQueuedItems = new IntVariable();
}

public sealed class ActionManager
{
    public static ActionManager instance = new ActionManager();

    public ActionableListVariable actionableItems = new ActionableListVariable();

    public static int RemainingRoom { get; set; }

    public static int GetRemainingRoom() => RemainingRoom;

    /// <summary>
    /// The queue admission term used by both purchase kinds. Fixtures keep the answer on the
    /// candidate so two candidates can disagree in one test, while the call still has the native
    /// <c>ActionManager.CanLoadAction(IActionable)</c> shape.
    /// </summary>
    public static bool CanLoadAction(IActionable actionable) => actionable switch
    {
        StructureSO structure => structure.purchasable,
        UpgradeSO upgrade => upgrade.purchasable,
        _ => false,
    };
}

public static class AutoBuyManager
{
    public static int RemainingRoom { get; set; }

    public static int GetRemainingRoom() => RemainingRoom;
}

public class SpellRecipeSO : IdScriptableObject, IDiscoverable, ILevelable
{
#pragma warning disable CS0414 // Native private field read by the collector through a bound accessor.
    private List<SpellTypeSO> notSpellTypes = new List<SpellTypeSO>();
#pragma warning restore CS0414
    public static List<SpellRecipeSO> All = new List<SpellRecipeSO>();
    private string stableUuid;

    public SpellRecipeSO() => stableUuid = base.GetGuid().ToString("D");
    /// <summary>
    /// Fixture-facing string form of the inherited identity. The game declares <c>GetGuid()</c> on
    /// <see cref="IdScriptableObject"/>, so this surface keeps that base identity synchronized and
    /// deliberately does not redeclare the method.
    /// </summary>
    public new string uuid
    {
        get => stableUuid;
        set
        {
            stableUuid = value;
            if (Guid.TryParse(value, out var guid)) base.SetGuid(guid);
        }
    }
    public int masteryLevel;
    public BigDouble masteryExperience;
    public ExperienceContainer masteryXpContainer = new ExperienceContainer();
    public bool discovered;
    public int discRarityLevel;
    public bool readyToLevel;
    public bool SuppressMasteryGain { get; set; }
    public bool SuppressLevelMutation { get; set; }
    public int MasteryGrantCalls { get; private set; }
    public List<BigDouble> GrantedMasteryExperience { get; } = new List<BigDouble>();
    public Prerequisites.Container levelingPrerequisites = new Prerequisites.Container();
    public ResourceCostList levelCost = new ResourceCostList();
    public List<GlyphSO> coreRecipe = new List<GlyphSO>();
    public ResourceCostList baseDiscoveryCost = new ResourceCostList();
    public ResourceCostList baseLevelingCost = new ResourceCostList();
    public ResourceCostList baseResourceCost = new ResourceCostList();
    public ResourceCostList baseUsageCost = new ResourceCostList();
    public ResourceCostList holdDrain = new ResourceCostList();
    public List<SpellTypeSO> spellTypes = new List<SpellTypeSO>();
    public RecipeBookList recipeBookList = new RecipeBookList();
    public Prerequisites.Container usagePrerequisites = new Prerequisites.Container();
    public SpellCastType castType;
    public Duration.Entry baseRecharge = new Duration.Entry();
    public ScalingWeightRef.Value maxChannel = new ScalingWeightRef.Value();
    public Scaling.Value repeatInstantEffectRate = new Scaling.Value();
    public bool NativeCanDiscover { get; set; } = true;
    public bool NativeDiscoverVisible { get; set; } = true;
    public bool NativeDiscoveryRequired { get; set; }
    public bool SuppressDiscovery { get; set; }
    public bool ThrowBeforeDiscovery { get; set; }
    public bool ThrowAfterDiscovery { get; set; }
    public int DiscoverCalls { get; private set; }
    public bool NativeIsCreatable { get; set; } = true;
    public bool NativeUsageRequirementsMet { get; set; } = true;
    public int NativeSelectedSpellLevel { get; set; } = 1;
    public bool NativeUniqueSpell { get; set; }
    public bool CandidateDurationSpell { get; set; }
    public bool CandidateToggledSpell { get; set; }
    public void GainMasteryExp(BigDouble exp)
    {
        MasteryGrantCalls++;
        if (SuppressMasteryGain) return;
        GrantedMasteryExperience.Add(exp);
        masteryExperience = Add(masteryExperience, exp);
    }
    public new Guid GetGuid() => Guid.Parse(uuid);
    public new Guid GetId() => GetGuid();
    public bool IsDiscovered() => discovered;
    public bool CanDiscover() => NativeCanDiscover;
    public bool IsDiscoverVisible() => NativeDiscoverVisible;
    public bool IsDiscoverRequired() => NativeDiscoveryRequired;
    public bool IsCreatable() => NativeIsCreatable;
    public List<GlyphSO> GetGlyphRecipe() => new List<GlyphSO>(coreRecipe);
    public List<ResourceSO> GetResourceRecipe() => new List<ResourceSO>();
    public ResourceCostList GetDiscoverCost() => baseDiscoveryCost;
    public ResourceCostList GetUsageCost() => baseUsageCost;
    public Spell CreateEmpty(int _) => new Spell(this)
    {
        NativeUnique = NativeUniqueSpell,
        DurationSpell = CandidateDurationSpell,
        ToggledSpell = CandidateToggledSpell,
    };
    public int GetSelectedSpellLevel() => NativeSelectedSpellLevel;
    public bool HasMetUsageRequirements() => NativeUsageRequirementsMet;
    public void Discover()
    {
        DiscoverCalls++;
        if (ThrowBeforeDiscovery)
            throw new InvalidOperationException("injected failure before discovery");
        if (!SuppressDiscovery) discovered = true;
        SpellManager.instance?.PostDiscoverRecipe(this);
        if (ThrowAfterDiscovery)
            throw new InvalidOperationException("injected failure after discovery");
    }
    public bool IsReadyToLevelMastery() => readyToLevel;
    public ResourceCostList GetLevelCost() => levelCost;
    public void PurchaseLevel()
    {
        if (!readyToLevel || SuppressLevelMutation) return;
        masteryLevel++;
        readyToLevel = false;
    }
    public string GetName() => "Spell";

    private static BigDouble Add(BigDouble left, BigDouble right) => left + right;
    public bool hiddenDiscovery;
    public bool isRequiredDiscovery;
    public int penaltyUsageCost;
    public double castSpeed;
    public int baseCharges;
    public bool repeatInstantEffects;
    public ValueModifierRecord spellPowerMod = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord spellCostMod = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord spellCdSpeedMod = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord spellDurationMod = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord spellSpecialMod = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord spellXpMod = new ValueModifierRecord(new BigDouble(0.0, 0));
    private bool hasAlertedThisMastery;
}

public static class Duration
{
    public sealed class Entry
    {
        public double duration;
        public double mult;
        public ProcessorType type;
    }

    public enum ProcessorType { Fixed }
}

public enum SpellCastType { Instant }

public static class Scaling
{
    public sealed class Value
    {
        public double baseValue;
    }
}

public sealed class RecipeBookList
{
    public List<RecipeBookSO> recipeBooks = new List<RecipeBookSO>();
}

public class IdScriptableObject : UnityEngine.ScriptableObject
{
    public static Dictionary<Guid, IdScriptableObject> RuntimeLookup = new();
    public Guid uuid = Guid.NewGuid();
    public static object? GetInstance(Guid guid) => RuntimeLookup.TryGetValue(guid, out var value) ? value : null;
    public Guid GetGuid() => uuid;
    public Guid GetId() => uuid;
    public void SetGuid(Guid guid) => uuid = guid;
}

/// <summary>The game's global scalars. Same shape as <see cref="IntVariable"/>; separate registry.</summary>
public sealed class DoubleVariable
{
    public static List<DoubleVariable> All = new List<DoubleVariable>();
    public Guid uuid = Guid.NewGuid();
    public ValueModifierRecord value = new ValueModifierRecord(new BigDouble(0.0, 0));
    public bool isPercentVariable;

    public Guid GetGuid() => uuid;
}

/// <summary>
/// The game's global flags. Not a NumberVariable — it holds a plain field with nothing to calculate.
/// </summary>
public sealed class BoolVariable
{
    public static List<BoolVariable> All = new List<BoolVariable>();
    public Guid uuid = Guid.NewGuid();
    public bool value;

    public Guid GetGuid() => uuid;
    public bool GetValue() => value;
    public int SetCalls { get; private set; }
    public bool SuppressSet { get; set; }
    public bool ThrowAfterSet { get; set; }
    public void SetValue(bool next)
    {
        SetCalls++;
        if (!SuppressSet) value = next;
        if (ThrowAfterSet) throw new InvalidOperationException("injected failure after bool write");
    }
    public bool initialValue;
    public bool isSaved;
    private int observerId;
}

public sealed class StringVariable
{
    public static List<StringVariable> All = new List<StringVariable>();
    public string value = string.Empty;

    public string GetValue() => value;
    public int SetCalls { get; private set; }
    public bool SuppressSet { get; set; }
    public void SetValue(string next)
    {
        SetCalls++;
        if (!SuppressSet) value = next;
    }
}

public class ViewSO : IdScriptableObject
{
    public static List<ViewSO> All = new List<ViewSO>();
    public bool available;
    public bool active;
    public bool IsAvailable() => available;
    public bool alwaysActive;
    public List<AbstractListVariable> relevantLists = new List<AbstractListVariable>();
    public List<AbstractListVariable> availableLists = new List<AbstractListVariable>();
}

public sealed class AlchemyTypeSO : IdScriptableObject
{
    public static List<AlchemyTypeSO> All = new List<AlchemyTypeSO>();

    // The type's level is a modifier record rather than a persisted integer, which is why world
    // collection reads it through the record's cached field.
    public ValueModifierRecord level = new ValueModifierRecord(new BigDouble(0.0, 0));

    /// <summary>The chosen level, held in the shared variable registry rather than on the type.</summary>
    public IntVariable selectedLevel;

    // Composed records: no cached value of their own, so collection counts their active set.
    public bool maxUsageByMastery;
    public ModifierRecord power = new ModifierRecord();
    public ModifierRecord speed = new ModifierRecord();
    public ModifierRecord special = new ModifierRecord();
    public ModifierRecord drainCostMod = new ModifierRecord();
    public ModifierRecord experienceRate = new ModifierRecord();
    public ModifierRecord overdrivePower = new ModifierRecord();
    public ModifierRecord overdriveSpeed = new ModifierRecord();
    public ModifierRecord overdriveDrainCostMod = new ModifierRecord();
    public ModifierRecord overdriveXpRate = new ModifierRecord();
    public ModifierRecord timeReqMod = new ModifierRecord();
    public ModifierRecord timeScalingMod = new ModifierRecord();
    public ModifierRecord freeUsageSlots = new ModifierRecord();
    public ModifierRecord effectLevels = new ModifierRecord();
    public ValueModifier reqCostPenalty;
    public ValueModifier reqSpeedPenalty;

    public AlchemyTypeSO()
    {
        uuid = base.GetGuid().ToString();
    }

    public AlchemyTypeSO(string uuid)
    {
        this.uuid = uuid;
        if (Guid.TryParse(uuid, out var guid))
        {
            base.SetGuid(guid);
        }
    }

    public new string uuid;

    public new void SetGuid(Guid guid)
    {
        base.SetGuid(guid);
        uuid = guid.ToString();
    }
}

public sealed class AlchemyRecipeSO : IdScriptableObject, IDiscoverable
{
    public static List<AlchemyRecipeSO> All = new List<AlchemyRecipeSO>();
    private readonly ExperienceContainer experienceContainer = new ExperienceContainer();

    public AlchemyRecipeSO()
    {
        uuid = base.GetGuid().ToString();
    }

    public AlchemyRecipeSO(string uuid, string name, IEnumerable<AlchemyTypeSO> types)
    {
        this.uuid = uuid;
        this.name = name;
        alchemyTypes.AddRange(types);
        if (Guid.TryParse(uuid, out var guid))
        {
            base.SetGuid(guid);
        }
    }

    public new string uuid;
    public new string name = "Alchemy";
    public bool discovered = true;

    /// <summary>
    /// Which gate this recipe's row is behind. <c>Discover</c> makes <c>IsAvailable()</c> read
    /// <c>discovered</c>; <c>Prerequisite</c> makes it run the visibility container instead. Every
    /// authored recipe on the pinned build is <c>Discover</c>, which is the default here too.
    /// </summary>
    public VisibilityTypeKind visibilityType = VisibilityTypeKind.Discover;

    public enum VisibilityTypeKind
    {
        Discover = 0,
        Prerequisite = 1,
    }

    public int masteryLevel;
    public BigDouble masteryXp;
    public int maxLevel = 1;
    public int advancementLevel;
    public int discRarityLevel;
    public BigDouble recipeTime;
    public readonly List<AlchemyTypeSO> alchemyTypes = new List<AlchemyTypeSO>();
    public ConceptCostVector drainCost = new ConceptCostVector();
    public ResourceCostList baseDiscoveryCost = new ResourceCostList();
    public ResourceCostList usageCost = new ResourceCostList();
    public ConceptCostVector bandwidthCost = new ConceptCostVector();
    public ModifierListRef completionCostAdvanceMod = new ModifierListRef();
    public ModifierListRef drainCostLevelMod = new ModifierListRef();
    public Prerequisites.Container usagePrerequisites = new Prerequisites.Container();
    public InstanceScalingRef instanceScaling = new InstanceScalingRef();
    public List<GlyphSO> glyphRecipe = new List<GlyphSO>();
    public List<ResourceSO> resourceRecipe = new List<ResourceSO>();
    public bool NativeDiscoverVisible { get; set; } = true;
    public bool NativeCanDiscover { get; set; } = true;
    public bool NativeDiscoveryRequired { get; set; }
    public bool SuppressDiscovery { get; set; }
    public bool ThrowBeforeDiscovery { get; set; }
    public bool ThrowAfterDiscovery { get; set; }
    public int DiscoverCalls { get; private set; }
    public AlchemyTypeSO coreType = new AlchemyTypeSO("scholar-slot");
    public List<BigDouble> GrantedMasteryExperience { get; } = new List<BigDouble>();

    public new Guid GetGuid() => Guid.TryParse(uuid, out var guid) ? guid : base.GetGuid();
    public new Guid GetId() => GetGuid();
    public new void SetGuid(Guid guid)
    {
        base.SetGuid(guid);
        uuid = guid.ToString();
    }
    public bool IsDiscovered() => discovered;
    public ResourceCostList GetDiscoverCost() => baseDiscoveryCost;
    public ResourceCostList GetUsageCost() => usageCost;
    public int GetFreeUsageSlots() => freeUsageSlots.GetValue().ToInt();
    public List<GlyphSO> GetGlyphRecipe() => new List<GlyphSO>(glyphRecipe);
    public List<ResourceSO> GetResourceRecipe() => new List<ResourceSO>(resourceRecipe);
    public bool IsDiscoverVisible() => NativeDiscoverVisible;
    public bool CanDiscover() => NativeCanDiscover;
    public bool IsDiscoverRequired() => NativeDiscoveryRequired;
    public bool IsAvailable() => discovered;
    public int GetExperienceLevel() => masteryLevel;
    public BigDouble GetExperience() => masteryXp;
    public BigDouble GetRequiredExperience() => experienceContainer.GetRequiredExperience();
    public int GetMaxUsageSlots() => maxUsageSlots.GetValue().ToInt();
    public AlchemyTypeSO GetCoreType() => coreType;
    public string GetName() => name;
    public void Discover()
    {
        DiscoverCalls++;
        if (ThrowBeforeDiscovery)
            throw new InvalidOperationException("injected failure before discovery");
        if (!SuppressDiscovery) discovered = true;
        if (ThrowAfterDiscovery)
            throw new InvalidOperationException("injected failure after discovery");
    }
    public void ApplyMastery() => masteryLevel++;

    public void GainMasteryXp(BigDouble amount)
    {
        GrantedMasteryExperience.Add(amount);
        masteryXp = Add(masteryXp, amount);
    }

    private static BigDouble Add(BigDouble left, BigDouble right) => left + right;
    public bool isRequiredDiscovery;
    public bool isCompletionRecipe;
    public bool isAdvancementRecipe;
    public double completionTime;
    public bool isDebugAlchemy;
    public ValueModifierRecord power = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord speed = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord drainCostMod = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord special = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord timeReqMod = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord timeScalingMod = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord masteryXpRate = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord effectLevels = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord overdrivePower = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord overdriveSpeed = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord overdriveDrainCostMod = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord overdriveXpRate = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord freeUsageSlots = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord maxUsageSlots = new ValueModifierRecord(new BigDouble(0.0, 0));
    private BigDouble cachedCompletionTime;
    private BigDouble cachedRequiredXp;
}

/// <summary>
/// The list-variable hierarchy, shaped as the shipped assembly declares it: the value list and the
/// per-element-type registry sit on the generic base, occupancy on the generic list, and a concrete
/// list variable adds only its own members.
/// </summary>
/// <remarks>
/// The shape is why <c>All</c> is no use for reaching a queue. It is declared on the generic base, so
/// it holds every list variable over one element type rather than every instance of one concrete
/// type — and reflection does not find a base type's static on a derived one at all.
/// </remarks>
public abstract class AbstractListVariable : IdScriptableObject
{
}

public class AbstractListVariable<T> : AbstractListVariable
{
    public static List<AbstractListVariable<T>> All = new List<AbstractListVariable<T>>();
    public List<T> value = new List<T>();

    /// <summary>Whether the list is authored rather than played into, as the game marks it.</summary>
    public bool isStatic;

    /// <summary>
    /// Whether the list keeps a fixed run of spots rather than growing, as the game marks it. A
    /// filled list writes into an empty index instead of appending, and every mutation the game
    /// performs on it goes through that index.
    /// </summary>
    public bool isFilled;
    public int Maximum = 4;
    public IntVariable? maxSizeVariable;
    public int GetMax() => maxSizeVariable?.AsInt() ?? Maximum;
    public bool HasMax() => maxSizeVariable is not null;
    public bool IsImmutable() => isStatic;
    public virtual bool IsFilledType() => isFilled;
    public List<T> ToList() => new List<T>(value);
    public int Count => value.Count;
    public int GetCount() => value.Count;
    public virtual void Empty()
    {
        if (IsImmutable()) return;
        value = new List<T>();
        if (IsFilledType()) FillSpots();
        UpdateObservable();
    }

    public bool IsAtMax() => value.Count >= GetMax();

    protected virtual T GetEmptyElement() => default!;

    protected void FillSpots()
    {
        if (!HasMax() || !IsFilledType() || GetCount() >= GetMax()) return;
        while (value.Count < GetMax()) value.Add(GetEmptyElement());
        UpdateObservable();
    }

    protected void EnsureCorrectSpots()
    {
        if (!HasMax() || !IsFilledType() || GetMax() == GetCount()) return;
        FillSpots();
        value = value.Take(GetMax()).ToList();
        UpdateObservable();
    }

    public bool Contains(T element) => value.Contains(element);
    public virtual void Remove(T element) => value.Remove(element);
    public bool SuppressSwap { get; set; }
    public bool ThrowBeforeSwap { get; set; }
    public bool ThrowAfterSwap { get; set; }
    public bool SuppressSetAt { get; set; }
    public bool ThrowAfterSetAt { get; set; }
    public int SwapCalls { get; private set; }
    public int SetAtCalls { get; private set; }
    public int UpdateObservableCalls { get; private set; }

    public void SwapPositions(int first, int second)
    {
        SwapCalls++;
        if (ThrowBeforeSwap) throw new InvalidOperationException("injected failure before slot swap");
        if (!SuppressSwap)
            (value[first], value[second]) = (value[second], value[first]);
        if (ThrowAfterSwap) throw new InvalidOperationException("injected failure after slot swap");
    }

    public void SetAt(int index, T valueN)
    {
        SetAtCalls++;
        if (!SuppressSetAt) value[index] = valueN;
        if (ThrowAfterSetAt) throw new InvalidOperationException("injected failure after list set");
    }

    public void UpdateObservable() => UpdateObservableCalls++;
}

public interface IPassiveObservable
{
    int GetObservableId();
}

public sealed class PassiveObservable : IPassiveObservable
{
    private int _observedId;

    public int GetObservableId() => _observedId;
    public void UpdateObservable() => _observedId++;

    public sealed class Channel
    {
        private readonly Dictionary<string, PassiveObservable> _values = new();

        public Channel(params string[] names)
        {
            foreach (var name in names) _values[name] = new PassiveObservable();
        }

        public IPassiveObservable Observe(string name) => _values[name];
        public void Update(string name) => _values[name].UpdateObservable();
    }
}

public class GenericListVariable<T> : AbstractListVariable<T>
{
    public bool SuppressAdd;
    public bool ThrowAfterAdd;
    public int AddCalls;

    public int GetUsedSpots()
    {
        var used = 0;
        foreach (var element in value)
        {
            if (IsFilledElement(element)) used++;
        }

        return used;
    }

    /// <summary>
    /// Whether one more element fits. A filled-type list answers the game's own question — is there
    /// a spot holding nothing — which is the branch that makes <c>Add</c> a permanent no-op on a
    /// list that was never padded out to its maximum.
    /// </summary>
    /// <remarks>
    /// The non-filled arm counts used spots rather than the game's <c>!IsAtMax()</c> because the
    /// concrete stub lists that keep an emptied placeholder — the spell loadout above all — model
    /// fixed slots without declaring themselves filled, and re-modelling them is a change to every
    /// loadout test rather than to the staging defect this shape exists for.
    /// </remarks>
    public bool HasEmptySpot() =>
        IsFilledType() ? FindEmptyIndex() >= 0 : GetUsedSpots() < GetMax();

    public List<T> GetFilledElements() => value.FindAll(IsFilledElement);

    public int FindEmptyIndex() => value.FindIndex(element => !IsFilledElement(element));

    /// <summary>
    /// The game's own <c>Add</c>, branch for branch: three of its paths return without writing and
    /// without saying so, which is why a caller that does not read the list back cannot know
    /// whether its element landed.
    /// </summary>
    public virtual void Add(T element)
    {
        AddCalls++;
        if (SuppressAdd)
        {
            if (ThrowAfterAdd) throw new InvalidOperationException("injected failure after admission");
            return;
        }
        if (IsImmutable()) return;
        if (!HasEmptySpot()) return;
        if (IsFilledType())
        {
            EnsureCorrectSpots();
            var index = FindEmptyIndex();
            if (index < 0) return;
            value[index] = element;
        }
        else value.Add(element);
        UpdateObservable();
        if (ThrowAfterAdd) throw new InvalidOperationException("injected failure after admission");
    }

    /// <summary>
    /// The setter the game itself uses to stage a recipe's core, gated only on immutability and on
    /// being handed the list it already holds.
    /// </summary>
    public virtual void SetValue(List<T> next)
    {
        if (IsImmutable() || ReferenceEquals(value, next)) return;
        if (HasMax()) next = next.Take(GetMax()).ToList();
        if (IsFilledType())
        {
            EnsureCorrectSpots();
            value = new List<T>(next);
            FillSpots();
        }
        else value = new List<T>(next);
        UpdateObservable();
    }

    public bool SuppressToggle { get; set; }
    public bool ThrowAfterToggle { get; set; }
    public int ToggleCalls { get; private set; }
    public void Toggle(T element)
    {
        ToggleCalls++;
        if (!SuppressToggle)
        {
            if (value.Contains(element)) value.Remove(element);
            else value.Add(element);
        }
        if (ThrowAfterToggle) throw new InvalidOperationException("injected failure after list toggle");
    }

    protected virtual bool IsFilledElement(T element) => element is not null;

    public class AdditionTuple<TList>
        where TList : GenericListVariable<T>
    {
        public TList list = null!;
        public T element = default!;
        public void Add() => list.Add(element);
        public void Remove() => list.value.Remove(element);
    }
}

public class EmptyTypeListVariable<T> : GenericListVariable<T>
{
}

public class StackableListVariable<T> : GenericListVariable<T>
{
    public bool isStackable = true;
    public Stacked.StackedIdRecord<T> itemStack = new Stacked.StackedIdRecord<T>();

    public int GetStacks(T item) => itemStack.GetQuantity(item);

    public int GetTotalStacks() => itemStack.GetTotalStacks();

    /// <summary>
    /// The multiplicities the game bakes a spell from. It is null on a list the author did not mark
    /// stackable, and plain <c>Add</c> never writes it — the two facts that make an element landing
    /// in <see cref="AbstractListVariable{T}.value"/> no evidence at all that the stack carries it.
    /// </summary>
    public virtual Stacked.StackedIdRecord<T>? GetStackedRecord() => isStackable ? itemStack : null;

    /// <summary>Writes the stack and then the value list from it, the way the game does.</summary>
    public virtual void SetStack(Stacked.StackedIdRecord<T> record)
    {
        itemStack.ImportItems(record.ToList());
        SetValue(itemStack.GetItems());
    }

    public override void Empty()
    {
        base.Empty();
        itemStack.Empty();
    }

    public void Stack(T item, int quantity)
    {
        if (!value.Contains(item))
        {
            if (!HasEmptySpot()) return;
            Add(item);
        }
        itemStack.Set(item, itemStack.GetQuantity(item) + quantity);
    }

    public void Unstack(T item, int quantity)
    {
        if (!value.Contains(item)) return;
        itemStack.Set(item, Math.Max(itemStack.GetQuantity(item) - quantity, 0));
        if (itemStack.GetQuantity(item) == 0) value.Remove(item);
    }
}

public interface IActionable
{
}

/// <summary>The plot-action queue: one instance, reached by uuid rather than through a registry.</summary>
public sealed class PlotNodeActionInstanceListVariable : EmptyTypeListVariable<PlotNodeActionInstance>
{
    public bool SuppressMutation { get; set; }

    protected override bool IsFilledElement(PlotNodeActionInstance element) =>
        element is not null && !element.IsEmpty();

    public PlotNodeActionInstance? FindInstance(PlotNodeActionInstance prototype)
    {
        foreach (var candidate in value)
        {
            if (candidate is not null &&
                ReferenceEquals(candidate.GetElement(), prototype.GetElement()) &&
                ReferenceEquals(candidate.GetAction(), prototype.GetAction()))
                return candidate;
        }
        return null;
    }

    public bool HasInstance(PlotNodeActionInstance prototype) => FindInstance(prototype) is not null;

    public void AddInstance(PlotNodeActionInstance prototype, int quantity)
    {
        if (SuppressMutation) return;
        var existing = FindInstance(prototype);
        if (existing is not null && !existing.IsEmpty())
        {
            existing.PlayerChangeInstanceQuantity(quantity);
            return;
        }
        if (!HasEmptySpot()) return;

        prototype.PlayerChangeInstanceQuantity(quantity);
        prototype.Engage();
        for (var index = 0; index < value.Count; index++)
        {
            if (value[index] is null || value[index].IsEmpty())
            {
                value[index] = prototype;
                return;
            }
        }
        value.Add(prototype);
    }

    public void RemoveInstance(PlotNodeActionInstance prototype, int quantity)
    {
        if (SuppressMutation) return;
        var existing = FindInstance(prototype);
        if (existing is null || existing.IsEmpty()) return;
        existing.PlayerChangeInstanceQuantity(-quantity);
    }
}

public sealed class AlchemyRecipeListVariable : AbstractListVariable<AlchemyRecipeSO>
{
}

public sealed class StructureListVariable : GenericListVariable<StructureSO>
{
    public List<StructureSO> GetAll() => StructureSO.All;
}

public sealed class UpgradeListVariable : GenericListVariable<UpgradeSO>
{
    public List<UpgradeSO> GetAll() => UpgradeSO.All;
}

public sealed class ViewListVariable : GenericListVariable<ViewSO>
{
    public List<ViewSO> GetAll() => value;

    public sealed class ListTuple : AdditionTuple<ViewListVariable>
    {
    }
}

public class UpgradeSO : IdScriptableObject, IActionable
{
    public static List<UpgradeSO> All = new List<UpgradeSO>();
    private string stableUuid = Guid.NewGuid().ToString();
    public new string uuid
    {
        get => stableUuid;
        set
        {
            stableUuid = value;
            if (Guid.TryParse(value, out var guid)) base.SetGuid(guid);
        }
    }
    // The game stores one owned level and one in-flight count; every level question it answers is
    // derived from those two and maxLevel. Storing the answers independently let a fixture describe
    // an upgrade the game could never produce, which the suite then read straight out of the fields.
    public int level;
    public int queuedLevels;
    public int maxLevel;
    private int cachedCostLevel = -1;
    public bool available = true;
    public bool purchasable = true;
    public ResourceCostList purchaseCost = new ResourceCostList();

    // The authored cost and the list it grows by per level, which together are what the suite
    // computes GetPurchaseCost() from instead of calling it. An upgrade prices on an entirely
    // different chain than a structure, so it names entirely different fields.
    public ResourceCostList resourceCost = new ResourceCostList();
    public ModifierListRef resourceCostModPerLevel = new ModifierListRef();

    // The gate on the specific level being bought, as distinct from the whole-upgrade gate. The game
    // checks it as prerequisitesPerLevel.Check(level + queuedLevels + 1), which takes a level and so
    // cannot be a latched boolean the way `available` is.
    public Prerequisites.Container prerequisitesPerLevel = new Prerequisites.Container();
    public List<ViewListVariable.ListTuple> viewListAdditions = new List<ViewListVariable.ListTuple>();
    public BigDouble buildTime;
    public double developmentTime = 5.0;
    public string GetName() => "Upgrade";
    public bool IsAvailable() => available;
    public bool CanPurchase() =>
        !IsMaxQueuedLevel() &&
        purchaseCost.HasEnough() &&
        IsAvailable() &&
        HasMetQueuedLevelRequirements() &&
        ActionManager.CanLoadAction(this);
    public ResourceCostList GetPurchaseCost() => purchaseCost;
    public int GetPurchaseLevel() => level;
    public int GetQueuedPurchaseLevel() => level + queuedLevels;
    public bool HasFiniteLevels() => maxLevel > 0;

    /// <summary>
    /// The curve accessor, modelled by its two observable behaviours: which level a given offset
    /// resolves to, and that the answer is memoised against that level.
    /// </summary>
    /// <remarks>
    /// The prices themselves come from <see cref="LeveledCosts"/> rather than from a reimplementation
    /// of <c>SetToLevel</c>. Recomputing them here would put the arithmetic under test on both sides
    /// of the differential comparison, which is the one thing that comparison exists to avoid. What a
    /// fixture can pin is the clamp, the level asked for, and where the cache is left afterwards.
    /// </remarks>
    public readonly List<int> LeveledCostLevelsAsked = new List<int>();
    public readonly Dictionary<int, ResourceCostList> LeveledCosts = new Dictionary<int, ResourceCostList>();
    public int CachedCostLevel => cachedCostLevel;

    public ResourceCostList GetLeveledCostList(int addedLevels)
    {
        var priced = Math.Min(
            level + queuedLevels + addedLevels,
            HasFiniteLevels() ? maxLevel - 1 : int.MaxValue);
        LeveledCostLevelsAsked.Add(priced);
        cachedCostLevel = priced;
        return LeveledCosts.TryGetValue(priced, out var list) ? list : resourceCost;
    }
    public bool IsMaxLevel() => HasFiniteLevels() && level >= maxLevel;
    public bool IsMaxQueuedLevel() => HasFiniteLevels() && level + queuedLevels >= maxLevel;
    public bool HasMetQueuedLevelRequirements() =>
        prerequisitesPerLevel.Check(new Requirements.ConditionInfo(level + queuedLevels + 1));
    public void Purchase()
    {
        // The real upgrade Purchase() honours the global multi-buy multiplier, buying up to that
        // many levels in a single call, each still bounded by CanPurchase() (so a finite maxLevel
        // yields a partial "bought X of N" purchase). A multiplier of 1 keeps the single-level path.
        var target = GlobalVariables.MultiBuy?.Value ?? 1;
        if (target < 1) target = 1;
        for (var bought = 0; bought < target && CanPurchase(); bought++)
        {
            queuedLevels++;
            purchaseCost.PerformCost();
        }
    }
    public void CompleteAction()
    {
        if (queuedLevels <= 0) return;
        queuedLevels--;
        level++;
    }
}

public class StructureSO : UpgradeableObject, Targeting.ITargetable, IActionable
{
    public static List<StructureSO> All = new List<StructureSO>();
    public StructureTypeSO structureType = new StructureTypeSO();
    public List<StructureTypeSO> structureSubTypes = new List<StructureTypeSO>();
    public EnchantmentSO.EnchantTable enchantTable = new EnchantmentSO.EnchantTable();

    /// <summary>The standing effects the structure applies once built.</summary>
    public List<PersistentEffectDeprecated.Property> structureProperties =
        new List<PersistentEffectDeprecated.Property>();

    public int queuedQuantity;
    public int quantity;
    public bool available = true;
    public bool visible = true;
    public bool purchasable = true;
    public ResourceCostList purchaseCost = new ResourceCostList();

    // The authored cost and the modifier it scales by, which together are what the suite computes
    // GetPurchaseCost() from instead of calling it.
    public ResourceCostList baseCost = new ResourceCostList();
    public ValueModifierRef costPerQuantity = new ValueModifierRef();

    // The structure's own per-level gate, checked at its quantity rather than at a level count.
    public Prerequisites.Container prerequisitesPerLevel = new Prerequisites.Container();

    // World collection's reading. Field names mirror the game's.
    public int queuedEchos;
    public int completedEchos;
    public int selfBonusLevels;
    public BigDouble queueTimeLeft;
    private BigDouble currentBuildTime;
    public bool flagged;
    public int baseLevel;
    public float queueTimeTotal = 1f;
    public bool debugStructure;
    public bool disabled;
    private int observableId;
    private bool insufficientReqPenaltyActive;
    private int bufferDevelopedQuantity;
    public ValueModifierRecord power = new ValueModifierRecord(new BigDouble(1.0, 2));
    public ValueModifierRecord powerScaling = new ValueModifierRecord(new BigDouble(1.0, 2));
    public ValueModifierRecord speed = new ValueModifierRecord(new BigDouble(1.0, 2));
    public ValueModifierRecord passiveCostMod = new ValueModifierRecord(new BigDouble(1.0, 2));
    public ValueModifierRecord activeCostMod = new ValueModifierRecord(new BigDouble(1.0, 2));
    public ValueModifierRecord costScalingMod = new ValueModifierRecord(new BigDouble(1.0, 2));
    public ValueModifierRecord attributeRankEffectMod = new ValueModifierRecord(new BigDouble(1.0, 2));
    public ValueModifierRecord drainCostMod = new ValueModifierRecord(new BigDouble(1.0, 2));
    public ValueModifierRecord bonusLevels = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord effectLevels = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord buildSpeed = new ValueModifierRecord(new BigDouble(1.0, 2));
    public ValueModifierRecord echoBuildRating = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord powerBuildRating = new ValueModifierRecord(new BigDouble(0.0, 0));
    public bool ApplyPurchaseMutation { get; set; } = true;
    public bool ApplyToggleMutation { get; set; } = true;
    public int ApplyEffectsCalls { get; private set; }
    public int RemoveEffectsCalls { get; private set; }
    public int GetPurchaseCostCalls { get; private set; }
    public bool Available { get => available; set => available = value; }
    public bool Purchasable { get => purchasable; set => purchasable = value; }
    public int PurchaseLevel { get => quantity; set => quantity = value; }
    public int QueuedQuantity { get => queuedQuantity; set => queuedQuantity = value; }
    public ResourceCostList Cost { get => purchaseCost; set => purchaseCost = value; }

    public StructureSO()
    {
        // Portable Auto Buy fixtures normally use one synthetic global list for view ownership. A
        // default structure category points at that same exact registry so the owning-view resolver
        // can prove category membership without each unrelated test hand-authoring a second copy.
        structureType.SetStructuresForTests(All);
    }

    public override string GetName() => "Structure";
    public bool IsAvailable() => available;
    public bool IsVisible() => visible;

    // The shipped StructureSO.CanPurchase() is deliberately thin: it checks only the next-level
    // requirement and ActionManager queue admission. IsAvailable() and affordability are separate
    // player-path terms; Purchase(bool) performs the price check for each submitted level.
    public bool CanPurchase() => HasMetLevelRequirements() && ActionManager.CanLoadAction(this);
    public bool HasMetLevelRequirements() =>
        prerequisitesPerLevel.Check(new Requirements.ConditionInfo(quantity));
    public ResourceCostList GetPurchaseCost()
    {
        GetPurchaseCostCalls++;
        return purchaseCost;
    }
    public int GetPurchaseLevel() => quantity;
    public int GetQueuedQuantity() => queuedQuantity;
    public void ToggleDisabled()
    {
        if (!ApplyToggleMutation) return;
        if (disabled) EnableStructure();
        else DisableStructure();
    }
    public void DisableStructure()
    {
        disabled = true;
        RemoveEffects();
    }
    public void EnableStructure()
    {
        disabled = false;
        ApplyEffects();
    }
    public void ApplyEffects() => ApplyEffectsCalls++;
    public void RemoveEffects() => RemoveEffectsCalls++;
    public void Purchase(bool forceOne)
    {
        if (!forceOne || !CanPurchase() || !purchaseCost.HasEnough() || !ApplyPurchaseMutation) return;
        queuedQuantity++;
        // The game charges when a level is queued, not when it completes.
        purchaseCost.PerformCost();
    }
    public void QueueBuild(int amount) => queuedQuantity += amount;
    public void CompleteAction()
    {
        if (queuedQuantity <= 0) return;
        queuedQuantity--;
        quantity++;
    }
}

public class Player
{
    private static Player _instance = new Player(initializeOutputVariables: true);
    public IntVariable spellOutputLevel;
    public IntVariable maxSpellOutputLevel;
    public IntVariable reserveLevel;
    public IntVariable maxReserveLevel;

    public Player() : this(initializeOutputVariables: true)
    {
    }

    private Player(bool initializeOutputVariables)
    {
        spellOutputLevel = new IntVariable { Value = initializeOutputVariables ? 1 : 0 };
        maxSpellOutputLevel = new IntVariable { Value = initializeOutputVariables ? 100 : 0 };
        reserveLevel = new IntVariable { Value = initializeOutputVariables ? 1 : 0 };
        maxReserveLevel = new IntVariable { Value = initializeOutputVariables ? 100 : 0 };
    }

    public static Player Current
    {
        get => _instance;
        set => _instance = value;
    }

    public static IntVariable GetSpellOutputLevel() => _instance.spellOutputLevel;
    public static IntVariable GetReserveLevel() => _instance.reserveLevel;

    private static IntVariable bulkDevelopment =
        IntVariable.Register(KnownVariableIds.BulkDevelopment);

    public static IntVariable BulkDevelopment
    {
        get => bulkDevelopment;
        set => bulkDevelopment =
            IntVariable.Replace(bulkDevelopment, value, KnownVariableIds.BulkDevelopment);
    }

    public static IntVariable GetBulkDevelopment() => BulkDevelopment;

    // The three frame-wide terms the resource rate chain needs. Each is a DoubleVariable whose
    // `value` record carries the number, exactly as the game holds them, so a reader that walks
    // variable -> value -> calculatedValue works identically against the stub and the game.
    public static DoubleVariable ResourceOverflow { get; set; } = new DoubleVariable();

    public static DoubleVariable ResourceOverflowLoss { get; set; } = new DoubleVariable();

    public static DoubleVariable ResetTimePassed { get; set; } = new DoubleVariable();

    public static DoubleVariable GetResourceOverflow() => ResourceOverflow;

    public static DoubleVariable GetResourceOverflowLoss() => ResourceOverflowLoss;

    public static DoubleVariable GetResetTimePassed() => ResetTimePassed;

    // The frame-wide structure-cost multiplier, authored at parity like every other percent the
    // game holds.
    public static DoubleVariable StructureCost { get; set; } =
        new DoubleVariable { value = new ValueModifierRecord(new BigDouble(1.0, 2)) };

    public static DoubleVariable GetStructureCost() => StructureCost;

    // The fifth frame-wide term, and the exponent the game raises a resource's quality to when
    // discounting that resource's attribute cost. Same accessor shape as the other four —
    // `static DoubleVariable GetX()` over an instance field on the singleton — so a reader that
    // binds five accessors of one shape binds all five here too. Authored at zero, which is what
    // the game ships it at until the research that raises it: an exponent of zero leaves quality
    // out of attribute prices entirely.
    public static DoubleVariable AttributeQualityBonus { get; set; } = new DoubleVariable();

    public static DoubleVariable GetAttributeQualityBonus() => AttributeQualityBonus;

    public void ManagerStart()
    {
    }
}

public class VoidEventChannel : UnityEngine.ScriptableObject
{
    public int RaiseCalls { get; private set; }

    public void Raise() => RaiseCalls++;
}

public class UIScreenFlash : UnityEngine.MonoBehaviour
{
    public static UIScreenFlash instance = new();
    private bool isActive;

    public UIScreenFlash FadeIn(float delay, float duration)
    {
        if (!SuppressFade) isActive = true;
        return this;
    }

    public static bool SuppressFade { get; set; }
    public bool ActiveForTests => isActive;

    public static void ResetForTests()
    {
        instance = new UIScreenFlash();
        SuppressFade = false;
    }
}

public class UIBackToMenuButton : UnityEngine.MonoBehaviour
{
    private readonly UnityEngine.UI.Button button;
    public VoidEventChannel manualSave = new();

    public UIBackToMenuButton()
    {
        var root = new UnityEngine.GameObject("Back to Menu");
        gameObject = root;
        transform = root.transform;
        name = root.name;
        button = root.AddComponent<UnityEngine.UI.Button>();
    }

    public void SetLiveForTest(bool value)
    {
        gameObject.SetActive(value);
        button.interactable = value;
    }

    /// <summary>The instantiated copy the game parks inside a modal's content area.</summary>
    public void PlaceInPanelForTest(UIModal panel)
    {
        transform.SetParent(panel.transform, false);
        gameObject.SetActive(false);
    }

    public void BackToMenu()
    {
        if (manualSave is not null) manualSave.Raise();
        SaveStateManager.instance.BackToMainMenu();
    }
}

public class SaveStateManager
{
    public static SaveStateManager instance = new();

    public void ImplementLoadedJson()
    {
    }

    public void StartGame()
    {
    }

    public void BackToMainMenu()
    {
        UIScreenFlash.instance.FadeIn(0f, 0f);
    }
}

public class GameManager
{
    public static long currentFrame;
    public static int PersistentResetCalls { get; set; }
    public static int CleanGameCalls { get; set; }

    public static void ResetGameState()
    {
    }

    public static void PersistentResetGameState() => PersistentResetCalls++;

    public static void CleanGame() => CleanGameCalls++;

    public void InitGame()
    {
    }
}

public class PersistentResetManager
{
    public static PersistentResetManager instance = new PersistentResetManager();
    public ResourceSO persistentResource = new ResourceSO();
    public IntVariable persistValue = new IntVariable { Value = 0 };
    public IntVariable persistValueNew = new IntVariable { Value = 0 };
    public IntVariable persistValueLast = new IntVariable { Value = 0 };
    public IntVariable persistentResetCount = new IntVariable { Value = 0 };
    public IntVariable challengeRerollsLeft = new IntVariable();
    public IntVariable challengeRerollsMax = new IntVariable();
    public BoolVariable hasCompleteWorldCycle = new BoolVariable();
    public BoolVariable hasFetchedChallenges = new BoolVariable();
    public ChallengeListVariable activeChallenges = new ChallengeListVariable();
    public ChallengeListVariable allChallenges = new ChallengeListVariable();
    public List<ChallengeSO> NextChallenges { get; } = new List<ChallengeSO>();
    public bool SuppressFetch { get; set; }
    public bool ThrowAfterFetch { get; set; }
    public int FetchCalls { get; private set; }
    public int ResetCalls { get; private set; }
    public bool SuppressReset { get; set; }
    public bool ThrowAfterReset { get; set; }
    public static Action? PersistentResetSignal { get; set; }

    public void FetchNewChallenges()
    {
        FetchCalls++;
        if (!SuppressFetch)
        {
            activeChallenges.CycleOut();
            activeChallenges.value = new List<ChallengeSO>(NextChallenges);
            activeChallenges.Instantiate();
        }
        if (ThrowAfterFetch) throw new InvalidOperationException("injected failure after prestige challenge fetch");
    }

    private void PersistentResetLogic()
    {
        ResetCalls++;
        PersistentResetSignal?.Invoke();
        if (!SuppressReset)
        {
            persistValueLast.Value = persistValue.Value;
            persistentResetCount.Value++;
            hasCompleteWorldCycle.value = false;
            hasFetchedChallenges.value = false;
            foreach (var challenge in allChallenges.value) challenge.rewardQueued = false;
            foreach (var challenge in activeChallenges.value)
                if (challenge.state == ChallengeSO.ChallengeState.QueuedStart)
                    challenge.state = ChallengeSO.ChallengeState.CurrentlyActive;
            GameManager.PersistentResetGameState();
            GameManager.CleanGame();
        }
        if (ThrowAfterReset) throw new InvalidOperationException("injected failure after persistent reset");
    }
}

public class Spell : ITooltipable
{
    public List<SpellTypeSO> augmentedSpellTypes = new List<SpellTypeSO>();
    private readonly SpellRecipeSO? reference;
    public static Action? FireSignal { get; set; }
    public string DisplayName { get; set; } = "Spell";
    public bool Channeled { get; set; }
    public bool EmitFireSignal { get; set; } = true;
    public bool HoldingCharge { get; private set; }
    public int FireCalls { get; private set; }
    public int CurrentCharges { get; set; }
    public int MaximumCharges { get; set; }
    public BigDouble CooldownRemaining { get; set; }
    public UnityEngine.Sprite Icon { get; set; } = new UnityEngine.Sprite();
    public ResourceCostList Cost { get; } = new ResourceCostList();
    public GuidContainer guidContainer = new GuidContainer(Guid.NewGuid());
    public Stacked.StackedIdRecord<GlyphSO> augmentGlyphRefs = new Stacked.StackedIdRecord<GlyphSO>();
    private List<GlyphSO> augmentGlyphs = new List<GlyphSO>();
    public int BaseEffectLevel { get; set; } = 1;
    public bool DurationSpell { get; set; }
    public bool ToggledSpell { get; set; }
    public bool NativeUsageRequirementsMet { get; set; } = true;
    public bool NativeUnique { get; set; }
    public bool NativeEmpty { get; set; }
    public bool NativeCasting { get; set; }
    public bool NativeReadyingCast { get; set; }
    public bool NativeChargeAvailable { get; set; } = true;

    /// <summary>
    /// The game's own answer to whether this spell has a charged cast, settable so a boundary can be
    /// shown refusing a hold on a spell that offers none.
    /// </summary>
    public bool NativeCanCharge { get; set; } = true;

    public bool SuppressToggleOff { get; set; }
    public bool SuppressAugmentMutation { get; set; }
    public bool ThrowBeforeAugmentMutation { get; set; }
    public bool ThrowAfterAugmentMutation { get; set; }
    public int SetAugmentCalls { get; private set; }

    /// <summary>
    /// The game's own manual-cast counter, named and shaped exactly as <c>Spell.numCasts</c> is,
    /// because the world reader binds it as a private field. <c>Spell.ExecuteSpell()</c> adds one
    /// per manual cast, so the stub's fire path does too.
    /// </summary>
    private int numCasts;

    public int NumCasts
    {
        get => numCasts;
        set => numCasts = value;
    }

    public Spell()
    {
    }

    public Spell(SpellRecipeSO reference)
    {
        this.reference = reference;
    }

    public SpellRecipeSO? get_reference() => reference;
    public Guid GetId() => guidContainer.guid;

    public string GetName() => DisplayName;
    public UnityEngine.Sprite GetIcon() => Icon;
    public string GetDisplayType() => "Spell";
    public UnityEngine.Color GetColor() => UnityEngine.Color.white;
    public bool IsColoredIcon() => false;
    public bool HasAltTooltips() => false;
    public string GetDescription() => string.Empty;
    public List<TooltipNode> GetTooltipNodes() => new List<TooltipNode>();
    public List<TooltipNode> GetAltTooltipNodes() => new List<TooltipNode>();
    public bool IsChanneled() => Channeled;
    public bool IsToggledSpell() => ToggledSpell;
    public bool IsEmpty() => NativeEmpty;
    public bool CanCharge() => NativeCanCharge;
    public bool IsCasting() => NativeCasting;
    public bool IsReadyingCast() => NativeReadyingCast;

    /// <summary>
    /// The game's own composite readiness answer, settable so a boundary can be shown refusing a cast
    /// the plan believed was ready — which is the ordinary case for a planner working off a snapshot.
    /// </summary>
    public bool NativeCanCast { get; set; } = true;

    public bool CanCast() => NativeCanCast;
    public bool CanFire() => NativeCasting || NativeCanCast;
    public bool IsAttuning() => false;
    public bool IsChargeAvailable() => NativeChargeAvailable;
    public bool CanRemove() => IsChargeAvailable() && !IsCasting();
    public bool HasEnoughResources() => true;
    public int GetCurrSpellCharges() => CurrentCharges;
    public int GetMaxSpellCharges() => MaximumCharges;
    public BigDouble GetCooldownTimeRemaining() => CooldownRemaining;
    public ResourceCostList GetCost() => Cost;
    public ResourceCostList GetDrainCost() => new ResourceCostList();
    public int GetOutputLevel() => Player.GetSpellOutputLevel().AsInt();
    public int GetLevel() => Math.Max(GetOutputLevel(), BaseEffectLevel);
    public int GetRequiredLevel() => GlyphSO.GetMasterReqOfList(augmentGlyphs);
    public int GetRecipeMasteryLevel() => reference?.masteryLevel ?? 0;
    public bool IsDurationSpell() => DurationSpell;
    public bool HasMetUsageRequirements() => NativeUsageRequirementsMet;
    public bool IsUniqueSpell() => NativeUnique;
    public List<GlyphSO> GetAugmentGlyphs() => new List<GlyphSO>(augmentGlyphs);
    public int GetQuantityOfGlyph(GlyphSO glyph) => augmentGlyphRefs.GetQuantity(glyph);
    public int GetNonFreeUsesOfGlyph(GlyphSO glyph) =>
        Math.Max(GetQuantityOfGlyph(glyph) - (int)glyph.freeUsages.GetValue().ToDouble(), 0);
    public int GetTotalAugGlyphs() => augmentGlyphRefs.GetTotalStacks();
    public void SetLevel(int _) => ComputeCost();
    public void SetAugmentGlyphs(Stacked.StackedIdRecord<GlyphSO> value)
    {
        SetAugmentCalls++;
        if (ThrowBeforeAugmentMutation) throw new InvalidOperationException("injected failure before augment mutation");
        if (!SuppressAugmentMutation)
        {
            augmentGlyphRefs = new Stacked.StackedIdRecord<GlyphSO>(value);
            augmentGlyphs = augmentGlyphRefs.GetItemList();
            ComputeCost();
        }
        if (ThrowAfterAugmentMutation) throw new InvalidOperationException("injected failure after augment mutation");
    }
    private void ComputeCost()
    {
    }
    public object GetScalingInfo() => new object();
    public void SetChargeInput(string source, bool holding) => HoldingCharge = holding;

    /// <summary>How many target requests this spell's effects open when it fires.</summary>
    public int RequestsOnFire { get; set; }

    public void Fire()
    {
        if (EmitFireSignal) FireSignal?.Invoke();
        FireCalls++;
        if (NativeCasting)
        {
            if (SettingsManager.CanCancelSpells() && !SuppressToggleOff)
                NativeCasting = false;
            return;
        }
        numCasts++;
        TargetingManager.OpenRequests += RequestsOnFire;
    }
}

public class Prerequisites
{
    /// <summary>
    /// Named as the game names it. `available` is a latch, not a question: Check() evaluates the
    /// conditions and leaves it set once they pass. Collection reads the latch; action boundaries
    /// that require current native truth may call Check().
    /// </summary>
    public class Container
    {
        public bool available;
        public bool NativeCheckResult { get; set; }
        public bool? ParameterizedCheckResult { get; set; }
        public int CheckCalls { get; private set; }
        public int ParameterizedCheckCalls { get; private set; }

        /// <summary>
        /// The conditions themselves. The suite reads how many there are — none is what an
        /// unconditional action authors — and never what they are or whether they hold.
        /// </summary>
        public List<object> prerequisites = new List<object>();

        public bool Check()
        {
            CheckCalls++;
            if (available) return true;
            if (NativeCheckResult) available = true;
            return available;
        }

        /// <summary>
        /// The per-level overload, which takes the level being bought and neither stamps nor latches.
        /// </summary>
        /// <remarks>
        /// Present so that the two overloads can be told apart, which is the one thing about reaching
        /// this oracle that a portable test can check — picking the parameterless one by name would
        /// turn a diagnostic into a mutation.
        /// <para>
        /// It answers only for the empty container, which is the case the game answers without
        /// consulting anything. It does not evaluate conditions and never will: a stand-in that did
        /// would be a second implementation of the arithmetic under test, and agreeing with it would
        /// mean nothing.
        /// </para>
        /// </remarks>
        public bool Check(Requirements.ConditionInfo conditionInfo)
        {
            ParameterizedCheckCalls++;
            return ParameterizedCheckResult ?? prerequisites.Count == 0;
        }
    }
}

/// <summary>
/// The base every entity an effect can point at derives from, modelled only as far as the reference
/// edge reads it: the identity a published row carries instead of the object.
/// </summary>
public abstract class UpgradeableObject : TooltipableObject
{
    private string stableUuid;

    protected UpgradeableObject()
    {
        stableUuid = base.GetGuid().ToString("D");
    }

    /// <summary>One effect's modification of one named property of one upgradeable object.</summary>
    /// <remarks>
    /// The property is a string here because it is a string in the game: the names come from each
    /// type's authored property record rather than from a shared enum, so two types can name
    /// different sets and the same name can mean the same thing across them.
    /// </remarks>
    public sealed class UpgradeEffectModifier
    {
        public UpgradeableObject? upgradeableObject;
        public string propertyType = string.Empty;
        public ValueModifier modifier;
        public bool useTargetRef;
    }

    public new string uuid
    {
        get => stableUuid;
        set
        {
            stableUuid = value;
            if (Guid.TryParse(value, out var guid)) base.SetGuid(guid);
        }
    }

}

/// <summary>
/// One authored effect block. Effects hang off this rather than off the entity, so a structure's
/// standing effects are a list of lists.
/// </summary>
public class PersistentEffectDeprecated
{
    public sealed class Property : PersistentEffectDeprecated
    {
    }

    public List<ResourceSO.PersistentEffect> resourceEffects =
        new List<ResourceSO.PersistentEffect>();

    public List<UpgradeableObject.UpgradeEffectModifier> upgradeableObjectEffects =
        new List<UpgradeableObject.UpgradeEffectModifier>();
}

public class ResourceCostList
{
    public List<ResourceTuple> costs = new List<ResourceTuple>();
    public bool WithinCapacity = true;
    public int CostPrintReads { get; private set; }
    private string _costPrint
    {
        get
        {
            CostPrintReads++;
            return "diagnostic-only";
        }
    }
    public bool affordable = true;
    public bool ResourcesVisible = true;

    public bool IsWithinCapacity() => WithinCapacity;
    public bool IsEmpty() => costs.Count == 0;
    public bool AllResourcesVisible() => ResourcesVisible;

    // How many more purchases the holdings cover. Spending is what makes a price unaffordable in the
    // game, so a fixture that wants a purchase to stop being possible partway says how far the
    // holdings go rather than reaching in and flipping a flag mid-call.
    public int AffordableLevels = int.MaxValue;
    public int PerformCalls { get; private set; }
    public int? ThrowAfterCostRows { get; set; }
    public bool HasEnough()
    {
        if (!affordable || AffordableLevels <= 0) return false;
        var totals = new Dictionary<ResourceSO, BigDouble>();
        for (var index = 0; index < costs.Count; index++)
        {
            var row = costs[index];
            if (row.resource is null) return false;
            totals.TryGetValue(row.resource, out var current);
            totals[row.resource] = current + row.GetValue();
        }
        foreach (var pair in totals)
            if (!pair.Key.HasAmount(pair.Value)) return false;
        return true;
    }
    public List<ResourceTuple> GetEntries() => costs;
    public BigDouble MaximumCostTimes()
    {
        if (costs.Count == 0) return new BigDouble(int.MaxValue);
        var result = new BigDouble(int.MaxValue);
        foreach (var row in costs)
        {
            if (row.resource is null || row.GetValue() <= BigDouble.Zero) return BigDouble.Zero;
            var available = row.resource.bandwidthResource
                ? row.resource.GetMissing()
                : row.resource.GetTrueQuantity();
            result = BigDouble.Min(result, BigDouble.Floor(available / row.GetValue()));
        }
        return BigDouble.Max(result, BigDouble.Zero);
    }
    public ResourceCostList Multiply(BigDouble factor)
    {
        var result = new ResourceCostList
        {
            WithinCapacity = WithinCapacity,
            affordable = affordable,
            AffordableLevels = AffordableLevels,
        };
        for (var index = 0; index < costs.Count; index++)
            result.costs.Add(new ResourceTuple(
                costs[index].resource,
                costs[index].GetValue() * factor));
        return result;
    }
    public ResourceCostList Add(ResourceCostList other)
    {
        foreach (var row in other.costs)
        {
            var index = costs.FindIndex(existing => ReferenceEquals(existing.resource, row.resource));
            if (index < 0) costs.Add(row);
            else costs[index] = new ResourceTuple(row.resource,
                costs[index].GetValue() + row.GetValue());
        }
        affordable &= other.affordable;
        return this;
    }
    public ResourceCostList Subtract(ResourceCostList other)
    {
        var result = new ResourceCostList();
        foreach (var row in costs)
        {
            var previous = other.costs.FirstOrDefault(
                candidate => ReferenceEquals(candidate.resource, row.resource));
            result.costs.Add(new ResourceTuple(
                row.resource,
                row.GetValue() - (previous.resource is null
                    ? BigDouble.Zero
                    : previous.GetValue())));
        }
        return result;
    }
    public ResourceCostList Apply(ValueModifier modifier)
    {
        var result = new ResourceCostList
        {
            WithinCapacity = WithinCapacity,
            affordable = affordable,
            AffordableLevels = AffordableLevels,
        };
        for (var index = 0; index < costs.Count; index++)
            result.costs.Add(new ResourceTuple(
                costs[index].resource,
                modifier.Adjust(costs[index].GetValue())));
        return result;
    }
    public void PerformCost()
    {
        PerformCalls++;
        for (var index = 0; index < costs.Count; index++)
        {
            var row = costs[index];
            row.resource?.Spend(row.GetValue());
            if (ThrowAfterCostRows == index + 1)
                throw new InvalidOperationException($"injected failure after {index + 1} cost rows");
        }
        if (AffordableLevels is > 0 and < int.MaxValue) AffordableLevels--;
    }

    public void PerformUsage(Guid guid)
    {
        foreach (var row in costs) row.resource?.AddUsage(guid, row.GetValue());
    }

    public void RemoveUsage(Guid guid)
    {
        foreach (var row in costs) row.resource?.RemoveUsage(guid);
    }
}

public class ResourceSO : UpgradeableObject
{
    private readonly Dictionary<Guid, BigDouble> activeUsage = new Dictionary<Guid, BigDouble>();
    /// <summary>The game's closed vocabulary of resource properties an effect can modify.</summary>
    public enum ModifiableType
    {
        Rate,
        MaxQuantity,
        MaxQuantityRate,
        Quality,
        GainRate,
        LossPercent,
        RestingRate,
        RateMaxPercent,
        AttributeCostMod,
        ReservationMod,
        RateInterestPercent,
        RateMissingPercent,
        MaxQuantityFunctional,
        RateLifetimePercent,
        RallyThreshold,
        RallyMod,
    }

    /// <summary>One structure's standing effect on one property of one resource.</summary>
    public sealed class PersistentEffect
    {
        public ResourceSO? resource;
        public ModifiableType upgradeType;
        public ValueModifier modifier;
    }

    // The per-type registry the game keeps for every entity category, and the traversal entry point
    // world collection uses. Tests populate it directly.
    public static List<ResourceSO> All = new List<ResourceSO>();
    public List<ResourceTypeSO> resourceTypes = new List<ResourceTypeSO>();
    public new string name = "Resource";
    public BigDouble quantity = new BigDouble(1.0, 3);
    public BigDouble trueRate = new BigDouble(0.0, 0);
    public bool available = true;
    public bool visible = true;
    public ValueModifierRecord quality = new ValueModifierRecord(new BigDouble(1.0, 2));
    public ValueModifierRecord maxQuantity = new ValueModifierRecord(new BigDouble(1.0, 4));

    // The rest of what world collection reads. Named exactly as the game names them, because the
    // collector binds by name and this file is the cheap early warning when a name moves.
    public BigDouble lifetimeQuantity;
    public BigDouble discoveryTime;
    public ValueModifierRecord gainRate = new ValueModifierRecord(new BigDouble(1.0, 2));
    public ValueModifierRecord drain = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord reservationMod = new ValueModifierRecord(new BigDouble(1.0, 2));
    public ValueModifierRecord usage = new ValueModifierRecord(new BigDouble(0.0, 0));

    public long appliedLevels;

    /// <summary>Where an experience resource pushes the levels it grants. Null for every other one.</summary>
    public IntVariable levelVariable;

    // The rate chain's own arguments.
    public ValueModifierRecord rate = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord rateSplash = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord rateMaxPercent = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord rateInterestPercent = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord rateMissingPercent = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord rateLifetimePercent = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord lossPercent = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord displayRate = new ValueModifierRecord(new BigDouble(0.0, 0));
    public double baseLoss = 0.5;

    /// <summary>Private in the game too: it is recomputed rather than persisted.</summary>
    private BigDouble calcRarityValue;

    // The rest of what the runtime type carries. Enumerated from the ScriptableObject rather than
    // the save record, and not filtered by what writes them; see D17.
    public double rarityValue;
    public double rarityValueEnd;
    public double restEngageTime;
    public bool pauseLossOnChange;
    public bool canOverflow;
    public bool noOverflowRubberBand;
    public bool bandwidthResource;
    public bool invertedResource;
    public bool excludeFromGlobals;
    public bool startVisible;
    public BigDouble appliedMaxQuantity;
    public int quantitySoftCapOrder;
    public int quantitySoftCapMagnitude;
    public double quantitySoftCapRatio;
    public bool debugResource;
    private double currentLossRate;
    private BigDouble lastReservation;
    private BigDouble debouncedReplenish;
    private BigDouble debouncedReverberate;
    private BigDouble debouncedDecay;
    private bool firstIncrement;
    public ValueModifierRecord maxQuantityRate = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord maxQuantityFunctional = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord restingRateMod = new ValueModifierRecord(new BigDouble(0.0, 0));
    // Authored at parity in the game, not at zero. A zero here reads as "the game has never
    // calculated this record" and withholds every price paid in this resource.
    public ValueModifierRecord attributeCostMod = new ValueModifierRecord(new BigDouble(1.0, 2));
    public ValueModifierRecord decayRatio = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord decayTimeMod = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord replenishRatio = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord replenishTimeMod = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord reverberateMod = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord reverberateTimeMod = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord rallyThreshold = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord rallyMod = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord usageDrainPenalty = new ValueModifierRecord(new BigDouble(0.0, 0));

    private bool inLossMode;
    private bool inRestMode;
    private bool inRallyMode;
    public override string GetName() => name;
    public BigDouble GetQuantity() => quantity;
    // The game's own formula. A settable field here would let the stub's GetTrueQuantity()
    // disagree with its own quantity and quality, which is precisely the drift that makes a
    // passing test mean nothing.
    public BigDouble GetTrueQuantity() =>
        quantity * BigDouble.Normalize(quality.GetValue().Mantissa, quality.GetValue().Exponent - 2);
    public BigDouble GetAttributeCostMod() => attributeCostMod.GetValue();
    public BigDouble GetTrueRate() => trueRate;
    public bool IsAvailable() => available;
    public bool IsVisible() => visible;
    public bool IsBandwidthResource() => bandwidthResource;
    public BigDouble GetMissing() => BigDouble.Max(maxQuantity.GetValue() - quantity, 0);
    public bool HasAmount(BigDouble amount) =>
        bandwidthResource ? GetMissing() >= amount : GetTrueQuantity() >= amount;
    public void AddUsage(Guid guid, BigDouble amount)
    {
        activeUsage[guid] = amount;
        if (bandwidthResource)
            quantity = activeUsage.Values.Aggregate(BigDouble.Zero, static (sum, value) => sum + value);
    }
    public void RemoveUsage(Guid guid)
    {
        activeUsage.Remove(guid);
        if (bandwidthResource)
            quantity = activeUsage.Values.Aggregate(BigDouble.Zero, static (sum, value) => sum + value);
    }
    public void Spend(BigDouble amount)
    {
        if (bandwidthResource)
        {
            quantity += amount;
            return;
        }

        var normalizedQuality = BigDouble.Normalize(
            quality.GetValue().Mantissa,
            quality.GetValue().Exponent - 2);
        quantity -= amount / normalizedQuality;
    }
    public BigDouble GetTrueAmount(BigDouble amount) => amount;
}

/// <summary>
/// Stands in for the game's research entries. Modelled only as far as world collection reads it:
/// identity, level, whether it is developing, and whether it is available.
/// </summary>
public class ResearchSO : ILevelable
{
    public static List<ResearchSO> All = new List<ResearchSO>();
    public string uuid = Guid.NewGuid().ToString();
    public int level;
    public int queuedLevels;
    public int researchStage;
    public int selfBonusLevels;
    public int maxLevel = 1;
    public double researchTime = 60.0;
    public bool isDeveloping;
    public bool isActive;
    public bool flagged;
    public bool available = true;
    public List<ResearchTypeSO> researchTypes = new List<ResearchTypeSO>();
    public ResourceCostList researchCost = new ResourceCostList();
    public ResourceFillList resourceFillList = new ResourceFillList();
    public Prerequisites.Container levelPrerequisites = new Prerequisites.Container();
    public bool hiddenLevel;
    public int levelVisibilityRange = 2;
    public ModifierRecord requirementsAdjust = new ModifierRecord();
    private int requiredStagesCached;
    private BigDouble requiredTimeCached;
    public ValueModifierRecord bonusLevels = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord baseLevels = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord power = new ValueModifierRecord(new BigDouble(1.0, 2));
    public ValueModifierRecord maxLevelCap = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord leewayPoints = new ValueModifierRecord(new BigDouble(0.0, 0));
    public Guid GetGuid() => Guid.Parse(uuid);
    public string GetName() => "Research";
    public bool IsDeveloping() => isDeveloping;
    public bool IsActive() => isActive;
    public bool IsVisible() => available;
    public bool IsAvailable() => IsVisible() && !IsComplete();
    public bool IsComplete() => IsMaxLevel();
    public bool HasMaxLevel() => maxLevel > 0;
    public bool IsMaxLevel() => HasMaxLevel() && GetBaseLevel() >= maxLevel;
    public bool MeetsLevelRequirements() =>
        levelPrerequisites.Check(new Requirements.ConditionInfo(GetRequirementLevel()));
    public bool stillHasLeeway = true;
    public bool belowArtificialMaxLevel = true;
    public bool StillHasLeeway() => stillHasLeeway;
    public bool IsBelowArtificialMaxLevel() => belowArtificialMaxLevel;
    public bool IsBelowMaxInvestmentLevel() => !IsComplete();

    // The game asks the price before the level requirements, and develops on leeway OR on being
    // below both caps, never on all three together.
    public bool IsWithinDevelopRange() =>
        !IsComplete() && GetDevelopmentCost().HasEnough() && MeetsLevelRequirements() &&
        (StillHasLeeway() || (IsBelowArtificialMaxLevel() && IsBelowMaxInvestmentLevel()));
    public bool CanDevelop() => IsWithinDevelopRange() && !IsDeveloping();
    public void PurchaseLevel()
    {
        if (SettingsManager.IsResearchQueueMode()) QueueDevelopment();
        else Develop();
    }
    public void Develop()
    {
        if (SuppressAction) return;
        isActive = true;
        isDeveloping = true;
        if (queuedLevels > 0) queuedLevels--;
        researchCost.PerformUsage(GetGuid());
        if (ThrowAfterAction) throw new InvalidOperationException("injected research action failure");
    }
    public void QueueDevelopment()
    {
        if (SuppressAction) return;
        var limit = Math.Max(GlobalVariables.GetMultiBuy().AsInt(), 0);
        if (maxLevel > 0) limit = Math.Min(limit, Math.Max(maxLevel - level - GetQueuedLevels(), 0));
        var aggregate = new ResourceCostList();
        var accepted = 0;
        for (var index = 0; index < limit; index++)
        {
            var atLevel = level + GetQueuedLevels() + index;
            aggregate.Add(GetDevelopmentCostAtLevel(atLevel + 1));
            if (!aggregate.HasEnough() || !IsWithinDevelopRangeAt(atLevel)) break;
            accepted++;
        }
        if (accepted <= 0) return;
        queuedLevels += accepted;
        if (!isDeveloping) Develop();
        if (ThrowAfterAction) throw new InvalidOperationException("injected research action failure");
    }
    public void CancelDevelopment()
    {
        if (!isDeveloping || SuppressAction) return;
        isActive = false;
        isDeveloping = false;
        queuedLevels = 0;
        resourceFillList.ClearInvestment();
        if (ThrowAfterAction) throw new InvalidOperationException("injected research action failure");
    }
    public void PauseResearch()
    {
        if (!isDeveloping || SuppressAction) return;
        isActive = false;
        if (ThrowAfterAction) throw new InvalidOperationException("injected research action failure");
    }
    public void ResumeResearch()
    {
        if (!isDeveloping || SuppressAction) return;
        isActive = true;
        if (ThrowAfterAction) throw new InvalidOperationException("injected research action failure");
    }
    public void SubmitBonusLevel()
    {
        if (SuppressAction) return;
        selfBonusLevels++;
        if (researchTypes.Count > 0) researchTypes[0].UseBonusLevel();
        if (ThrowAfterAction) throw new InvalidOperationException("injected research action failure");
    }
    public bool SuppressAction { get; set; }
    public bool ThrowAfterAction { get; set; }
    public bool CanApplyBonusLevels() => researchTypes.Any(type => type.HasFreeBonusLevelsLeft());
    public int GetFreeBonusLevelsLeft() => researchTypes.Count == 0
        ? 0
        : researchTypes.Max(type => type.GetRemainingFreeBonusLevels());
    public ResourceCostList GetDevelopmentCost() => researchCost;
    public ResourceCostList GetDevelopmentCostAtLevel(int level) =>
        researchCost.Multiply(new BigDouble(1));
    public bool IsWithinDevelopRangeAt(int level) => IsWithinDevelopRange();
    public int GetQueuedLevels() => queuedLevels + (isDeveloping ? 1 : 0);
    public int GetCurrentInvestmentLevel() => Math.Max(GetQueuedLevels() + level, 0);
    public BigDouble GetCurrentTime() => resourceFillList.GetAverageRatio() * GetRequiredTime();
    public BigDouble GetRemainingTime() => (new BigDouble(1) - resourceFillList.GetLowestRatio()) * GetRequiredTime();
    public BigDouble GetTimeRatio() => GetCurrentTime() / GetRequiredTime();
    public BigDouble GetRequiredTime() => requiredTimeCached == BigDouble.Zero
        ? new BigDouble(researchTime)
        : requiredTimeCached;
    public int GetPurchasedLevels() => level;
    public int GetBaseLevel() => level + baseLevels.GetValue().ToInt();
    public int GetBonusLevels() => bonusLevels.GetValue().ToInt();
    public int GetLevel() => GetBaseLevel() + GetBonusLevels();
    public int GetMaxLevel() => maxLevel;
    public int GetArtificialMaxLevel() => maxLevelCap.GetValue().ToInt();
    public int GetRequirementLevel() => requirementsAdjust.Adjust(new BigDouble(GetBaseLevel())).ToInt();
}

public class ResearchTypeSO : IdScriptableObject
{
    public static List<ResearchTypeSO> All = new List<ResearchTypeSO>();
    public bool linkedDevelopCost;
    public bool linkedResourceCost;
    public bool linkedResearchTime;
    public bool ignoreWhenLevelDependent;
    public bool persistThroughReset;
    public int cachedTotalLevel;
    public int cachedPeakLevel;
    public int cachedQueuedLevel;
    public int cachedDevelopingLevel;
    public int cachedQueuedValue;
    public int cachedInvestmentLevel;
    public int cachedPurchasedLevel;
    // The pinned build declares this one a ValueModifierRecord: it holds a number of its own
    // rather than distributing one into members. See docs/reverse-engineering/type-model.md.
    public ValueModifierRecord freeBonusLevels = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ModifierRecord levelRequirementAdjust = new ModifierRecord();
    public ValueModifierRecord maxInvestmentLevel = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ModifierRecord maxLevelCap = new ModifierRecord();
    public ModifierRecord power = new ModifierRecord();
    public ValueModifierRecord usedBonusLevels = new ValueModifierRecord(new BigDouble(0.0, 0));
    public int FreeBonusLevels { get; set; }
    public int UsedBonusLevels { get; set; }
    public int CurrentInvestmentLevel { get; set; }
    public int MaximumInvestmentLevel { get; set; }

    public bool HasFreeBonusLevelsLeft() => GetRemainingFreeBonusLevels() > 0;
    public int GetRemainingFreeBonusLevels() => Math.Max(FreeBonusLevels - UsedBonusLevels, 0);
    public int GetCurrentInvestmentLevel() => CurrentInvestmentLevel;
    public int GetMaxInvestmentLevel() => MaximumInvestmentLevel;
    public void UseBonusLevel() => UsedBonusLevels++;
}

public class ResourceFillList
{
    public List<ResourceFillEntry> entries = new List<ResourceFillEntry>();

    public BigDouble GetAverageRatio() => entries.Count == 0
        ? BigDouble.Zero
        : entries.Aggregate(BigDouble.Zero, (sum, entry) => sum + entry.FillPercent()) /
          new BigDouble(entries.Count);
    public BigDouble GetLowestRatio() => entries.Count == 0
        ? BigDouble.Zero
        : entries.Min(entry => entry.FillPercent());
    public ResourceFillList ClearInvestment()
    {
        foreach (var entry in entries) entry.Clear();
        return this;
    }

    public sealed class ResourceFillEntry
    {
        public ResourceFillEntry(ResourceSO resource, BigDouble quantity, BigDouble capacity)
        {
            this.resource = resource;
            Quantity = quantity;
            Capacity = capacity;
        }

        private readonly ResourceSO resource;
        public BigDouble Quantity { get; private set; }
        public BigDouble Capacity { get; }
        public ResourceSO get_resource() => resource;
        public BigDouble GetQuantity() => Quantity;
        public BigDouble GetCapacity() => Capacity;
        public BigDouble GetRemaining() => BigDouble.Max(Capacity - Quantity, BigDouble.Zero);
        public BigDouble FillPercent() => Capacity <= BigDouble.Zero ? BigDouble.Zero : Quantity / Capacity;
        public ResourceFillEntry Clear() { Quantity = BigDouble.Zero; return this; }
    }
}

public struct ResourceTuple
{
    // Both magnitudes, named as the game names them. `value` is the serialized double Unity writes to
    // disk; `valueBig` is the one the arithmetic uses and the one world collection reads.
    private readonly double value;
    private readonly BigDouble valueBig;

    public ResourceTuple(ResourceSO resource, BigDouble value)
    {
        this.resource = resource;
        valueBig = value;
        this.value = value.ToDouble();
    }

    public ResourceSO resource;

    public BigDouble GetValue() => valueBig;
}

/// <summary>
/// A reference to a global modifier. A structure holds one of these rather than a modifier, which is
/// why world collection carries the referenced variable's identity.
/// </summary>
public sealed class ValueModifierRef
{
    public ValueModifierVariable? variable;

    public ValueModifier GetMod() => variable is null ? default : variable.GetValue();

    public ValueModifier GetModifier() => GetMod();
}

/// <summary>
/// The game's identity wrapper. Modelled only as far as the private field a reference edge reads,
/// spelled as the game spells it.
/// </summary>
public sealed class GuidContainer
{
    private Guid _guid;

    public GuidContainer()
    {
    }

    public GuidContainer(Guid guid) => _guid = guid;

    public Guid guid => _guid;
}

/// <summary>
/// The base every modifier record derives from in the game. Modelled only as far as the active set
/// world collection counts, since a plain ModifierRecord has no cached value of its own.
/// </summary>
public class ModifierRecord
{
    public Dictionary<Guid, ValueModifier> passiveModifiers = new Dictionary<Guid, ValueModifier>();

    public Dictionary<Guid, ValueModifier> activeModifiers = new Dictionary<Guid, ValueModifier>();

    public bool HasActiveElements() => activeModifiers.Count > 0;

    public BigDouble Adjust(BigDouble value)
    {
        foreach (var modifier in passiveModifiers.Values.Concat(activeModifiers.Values)
                     .OrderBy(static modifier => modifier.order))
        {
            if (modifier.type != ValueModifier.ValueModifierType.Raw)
                throw new InvalidOperationException("The portable research stub models raw requirement adjustments only.");
            value += modifier.adjustReal;
        }
        return value;
    }
}

/// <summary>
/// One modifier. Named and shaped as the game shapes it — a struct whose three arithmetic fields
/// world collection reads directly, plus enough identity to be countable inside a record's set.
/// </summary>
public struct ValueModifier
{
    public enum ValueModifierType
    {
        Raw,
        MultiDiminishing,
        MultiStacking,
        Reduction,
        Exponent,
    }

    public BigDouble adjustReal;
    public ValueModifierType type;
    public int order;
#pragma warning disable CS0414 // Native private field read by the collector through a bound accessor.
    private IdScriptableObject? reference;
#pragma warning restore CS0414

    public ValueModifier(ValueModifierType type, BigDouble amount, int order = 0)
    {
        this.type = type;
        adjustReal = amount;
        this.order = order;
        reference = null;
    }

    public BigDouble Adjust(BigDouble value) => type switch
    {
        ValueModifierType.Raw => value + adjustReal,
        ValueModifierType.MultiDiminishing => value * (BigDouble.One + adjustReal),
        ValueModifierType.MultiStacking => value * adjustReal,
        ValueModifierType.Reduction => value / (BigDouble.One + adjustReal),
        ValueModifierType.Exponent => BigDouble.Pow(
            value,
            value > BigDouble.Zero && value < BigDouble.One
                ? BigDouble.One / adjustReal
                : adjustReal),
        _ => value,
    };
}

/// <summary>
/// Two modifier lists, not one. The exponents strengthen the modifiers before any of them touches a
/// value, so the suite reads both and keeps them apart.
/// </summary>
public sealed class ValueModifierList
{
    public List<ValueModifier> modifiers = new List<ValueModifier>();
    public List<ValueModifier> exponents = new List<ValueModifier>();
}

/// <summary>
/// The global modifier-list registry, the counterpart to <see cref="ValueModifierVariable"/> for
/// whole lists.
/// </summary>
public sealed class ModifierListVariable
{
    public static List<ModifierListVariable> All = new List<ModifierListVariable>();

    public Guid uuid = Guid.NewGuid();
    public ValueModifierList value = new ValueModifierList();

    public Guid GetGuid() => uuid;

    public ValueModifierList GetValue() => value;
}

/// <summary>
/// A reference to a modifier list. The game's Standard subclass resolves nine shared lists off
/// GlobalValues instead of the named variable, which is why the suite reads the resolved list's
/// contents through the virtual accessor rather than carrying an identity.
/// </summary>
public class ModifierListRef
{
    private static readonly ValueModifierList EmptyList = new ValueModifierList();

    public ModifierListVariable? variable;

    public virtual ValueModifierList GetValue() => variable is null ? EmptyList : variable.GetValue();
}

public sealed class GlobalValues
{
    public static GlobalValues instance = new GlobalValues();
    public ModifierListVariable spellLevelingStandard = new ModifierListVariable();
}

public enum ScalingType
{
    CostMod = 4,
    Speed = 6,
}

public class ScalingMap<T>
{
    protected Dictionary<ScalingType, T> values = new Dictionary<ScalingType, T>();

    public void Set(ScalingType kind, T value) => values[kind] = value;
}

public sealed class ScalingConversion : ScalingMap<ValueModifierList>
{
}

public sealed class InstanceScalingSO : IdScriptableObject
{
    public static List<InstanceScalingSO> All = new List<InstanceScalingSO>();
    public ScalingConversion instanceScaling = new ScalingConversion();
    public bool useRarity;
    public List<ScalingType> rarityAttributeBlacklist = new List<ScalingType>();
}

public sealed class InstanceScalingRef
{
    public InstanceScalingSO scaling = new InstanceScalingSO();
}

/// <summary>
/// The global modifier registry. Entities hold a reference to one of these rather than owning a
/// modifier, which is why the suite collects the registry and carries identities.
/// </summary>
public sealed class ValueModifierVariable
{
    public static List<ValueModifierVariable> All = new List<ValueModifierVariable>();

    public Guid uuid = Guid.NewGuid();
    public ValueModifier value;

    public Guid GetGuid() => uuid;

    public ValueModifier GetValue() => value;
}

public sealed class ValueModifierRecord
{
    /// <summary>
    /// The value the game memoises. [NonSerialized] in the game, so it is zero for every record a save
    /// has not touched — and it is exactly what GetValue() answers whenever the record is clean, which
    /// is why world collection reads it rather than only recomputing.
    /// </summary>
    private BigDouble calculatedValue;

    /// <summary>
    /// Whether the memo is out of date. The game sets it when a modifier is added or removed and
    /// clears it inside Calculate(); nothing else moves it, so a record that never carries a modifier
    /// is never dirty and its memo is permanent.
    /// </summary>
    private bool calculationDirty;

    /// <summary>The number the record is built from. World collection reads this and folds.</summary>
    public double baseValue;

    /// <summary>
    /// The two modifier sets, named and shaped as the game names them on ModifierRecord. World
    /// collection reads both and evaluates Calculate() itself when the record is dirty.
    /// </summary>
    public Dictionary<Guid, ValueModifier> passiveModifiers = new Dictionary<Guid, ValueModifier>();

    public Dictionary<Guid, ValueModifier> activeModifiers = new Dictionary<Guid, ValueModifier>();

    public ValueModifierRecord(BigDouble value)
    {
        baseValue = value.ToDouble();
        calculatedValue = value;
    }

    /// <summary>
    /// Marks the memo out of date, the way the game does when a modifier is added or removed. A test
    /// that puts a modifier into one of the sets without this describes a record the game cannot
    /// produce.
    /// </summary>
    public ValueModifierRecord Dirty()
    {
        calculationDirty = true;
        return this;
    }

    /// <summary>Puts the memo somewhere a recomputation would not, the way a save load does.</summary>
    public ValueModifierRecord WithMemo(BigDouble memo)
    {
        calculatedValue = memo;
        return this;
    }

    /// <summary>Whether the memo is out of date, so a test can state the shape it built.</summary>
    public bool IsCalculationDirty => calculationDirty;

    /// <summary>
    /// The memo, which is what the game answers for a clean record. A dirty one recomputes, and this
    /// deliberately does not: a stand-in that reimplemented Calculate() would be a second copy of the
    /// arithmetic under test, and agreeing with it would prove nothing.
    /// </summary>
    public BigDouble GetValue() => calculatedValue;

    /// <summary>The whole-number reading, which the game defines as <c>GetValue().ToInt()</c>.</summary>
    public int AsInt() => GetValue().ToInt();

    public bool HasActiveElements() => activeModifiers.Count > 0;
}

/// <summary>The spell-weight resources a loadout's usage cost is charged against.</summary>
public sealed class ResourceListVariable : GenericListVariable<ResourceSO>
{
}

public class SpellRecipeListVariable
{
    public List<SpellRecipeSO> value = new List<SpellRecipeSO>();
}

/// <summary>
/// The staged glyph lists, stackable exactly as the shipped assembly declares them: the value list
/// carries one element per distinct glyph and the stack beside it carries how many of each.
/// </summary>
public sealed class GlyphListVariable : StackableListVariable<GlyphSO>
{
}

public class SpellManager
{
    public static SpellManager? instance;
    public static bool NativeCanCast { get; set; } = true;
    public SpellRecipeListVariable availableSpellRecipes = new SpellRecipeListVariable();
    public ResourceListVariable spellWeightVariables = new ResourceListVariable();
    public GlyphListVariable selectedCoreGlyphs = new GlyphListVariable();
    public GlyphListVariable selectedAugmentGlyphs = new GlyphListVariable();
    public SpellListVariable activeSpells = new SpellListVariable();
    public bool SuppressSelectionResolution { get; set; }
    public bool SuppressDiscovery { get; set; }
    public bool SuppressCreation { get; set; }
    public bool CreateEmptyIdentity { get; set; }
    public bool ThrowAfterDiscovery { get; set; }
    public bool ThrowAfterCreation { get; set; }
    public bool SuppressRemoval { get; set; }
    public bool ThrowBeforeRemoval { get; set; }
    public bool ThrowAfterRemoval { get; set; }
    public int RemoveCalls { get; private set; }
    public int TryLevelAllCalls { get; private set; }
    public ResourceCostList? CreateCostOverride { get; set; }
    public Func<IReadOnlyList<GlyphSO>, ResourceCostList>? CreateCostResolver { get; set; }

    public static bool CanCastASpell() => NativeCanCast;
    public static ResourceCostList GetUsageCostOfSpell(Spell spell) =>
        spell.get_reference()?.GetUsageCost() ?? new ResourceCostList();

    /// <summary>
    /// The game's own matcher: an empty list resolves to nothing, augments are stripped, candidates
    /// are the recipes whose core is the same length, each is then filtered by whether it contains
    /// the glyph — membership, not position and not multiplicity — and the first survivor wins.
    /// </summary>
    /// <remarks>
    /// Ordered reference equality, which this stub used to apply, cannot produce the case the wire
    /// actually met: two recipes are candidates for one layout and the earlier one in the registry
    /// takes it, so the caller's recipe is unreachable through a layout that looks exactly right.
    /// </remarks>
    public SpellRecipeSO? GetSpellFromRecipe(List<GlyphSO> glyphs)
    {
        if (SuppressSelectionResolution || glyphs.Count == 0) return null;
        var coreGlyphs = glyphs.Where(glyph => !glyph.IsSpellAugment()).ToList();
        var candidates = availableSpellRecipes.value
            .Where(recipe => recipe.coreRecipe.Count == coreGlyphs.Count)
            .ToList();
        foreach (var glyph in coreGlyphs)
            candidates = candidates.Where(recipe => recipe.coreRecipe.Contains(glyph)).ToList();
        return candidates.Count > 0 ? candidates[0] : null;
    }

    public ResourceCostList GetSpellCreateCost(List<GlyphSO> glyphs)
    {
        var recipe = GetSpellFromRecipe(glyphs);
        if (recipe is null) return new ResourceCostList();
        return CreateCostResolver?.Invoke(glyphs) ?? CreateCostOverride ?? GlyphSO.GetCreationCostOfList(
            new ResourceCostList(), glyphs.Where(glyph => glyph.IsSpellAugment()));
    }

    public void DiscoverSpell()
    {
        var recipe = GetSpellFromRecipe(selectedCoreGlyphs.GetFilledElements());
        if (recipe is null || recipe.IsDiscovered() || SuppressDiscovery) return;
        recipe.Discover();
        recipe.baseDiscoveryCost.PerformCost();
        selectedCoreGlyphs.Empty();
        if (ThrowAfterDiscovery) throw new InvalidOperationException("injected failure after discovery");
    }

    public void CreateSpell()
    {
        var recipe = GetSpellFromRecipe(selectedCoreGlyphs.GetFilledElements());
        if (recipe is null) return;
        CreateRecipe(recipe);
    }

    public void CreateRecipe(SpellRecipeSO recipe)
    {
        if (!recipe.IsDiscovered() || SuppressCreation || !activeSpells.HasEmptySpot()) return;
        var spell = recipe.CreateEmpty(0);
        spell.SetLevel(recipe.GetSelectedSpellLevel());
        // The stack, not the value list: an augment that only ever reached `value` is not in the
        // spell the game bakes, however plainly it shows on the staging screen.
        var staged = selectedAugmentGlyphs.GetStackedRecord();
        spell.SetAugmentGlyphs(staged is null
            ? new Stacked.StackedIdRecord<GlyphSO>()
            : new Stacked.StackedIdRecord<GlyphSO>(staged));
        if (CreateEmptyIdentity) spell.guidContainer = new GuidContainer(Guid.Empty);
        AddSpell(spell);
        selectedCoreGlyphs.Empty();
        if (ThrowAfterCreation) throw new InvalidOperationException("injected failure after creation");
    }

    public void PostDiscoverRecipe(SpellRecipeSO recipe)
    {
        var spell = recipe.CreateEmpty(0);
        if (activeSpells.HasEmptySpot() && spell.get_reference()!.GetUsageCost().HasEnough())
            AddSpell(spell);
    }

    private void AddSpell(Spell spell) => activeSpells.Add(spell);

    public void RemoveSpell(Spell spell)
    {
        RemoveCalls++;
        if (ThrowBeforeRemoval) throw new InvalidOperationException("injected failure before spell removal");
        if (!SuppressRemoval) activeSpells.Remove(spell);
        if (ThrowAfterRemoval) throw new InvalidOperationException("injected failure after spell removal");
    }

    public void FireSpellIndex(int index)
    {
        var spell = activeSpells[index];
        spell.GetType().GetMethod(
            "Fire",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            Type.EmptyTypes,
            null)?.Invoke(spell, Array.Empty<object>());
    }

    public void TryLevelAllSpells()
    {
        TryLevelAllCalls++;
        foreach (var recipe in availableSpellRecipes.value)
        {
            while (recipe.IsDiscovered() && recipe.levelingPrerequisites.Check() &&
                   recipe.IsReadyToLevelMastery() && recipe.GetLevelCost().HasEnough())
            {
                recipe.GetLevelCost().PerformCost();
                recipe.PurchaseLevel();
            }
        }
    }
}

/// <summary>
/// The equipped loadout. A list variable like every other, which is what lets world collection reach
/// it by uuid through the identity registry rather than through the spell manager singleton.
/// </summary>
public sealed class SpellListVariable : GenericListVariable<Spell>, IEnumerable
{
    public bool PreserveSlotsOnRemove { get; set; } = true;

    public Spell this[int index] => value[index];

    protected override bool IsFilledElement(Spell element) =>
        element is not null && !element.IsEmpty();

    public override void Remove(Spell element)
    {
        var index = value.IndexOf(element);
        if (index < 0) return;
        if (PreserveSlotsOnRemove)
        {
            value[index] = new Spell
            {
                NativeEmpty = true,
                guidContainer = new GuidContainer(Guid.Empty),
            };
            return;
        }
        value.RemoveAt(index);
    }

    public IEnumerator GetEnumerator() => value.GetEnumerator();

    public List<Spell> GetAll() => new List<Spell>(value);
}

public sealed class PlayerLoadout
{
    public sealed class LoadoutLabel
    {
        private string labelName;
        private int iconIndex;
        private int colorIndex;

        public LoadoutLabel(string name = "Loadout") => labelName = name;
        public string GetName() => labelName;
        public int GetIconIndex() => iconIndex;
        public int GetColorIndex() => colorIndex;
        public void SetName(string value) => labelName = value ?? string.Empty;
        public void SetIconIndex(int value) => iconIndex = value;
        public void SetColorIndex(int value) => colorIndex = value;
    }

    public GuidContainer guidContainer;
    public LoadoutLabel label;
    public bool isSelected;
    public bool saveEquipment;
    public bool saveAlchemy;
    public List<Spell> spells = new List<Spell>();
    public Stacked.StackedIdRecord<EquipmentSO> equipment =
        new Stacked.StackedIdRecord<EquipmentSO>();
    public Stacked.StackedIdRecord<AlchemyRecipeSO> alchemy =
        new Stacked.StackedIdRecord<AlchemyRecipeSO>();
    public bool SuppressSectionMutation { get; set; }
    public bool SuppressLabelMutation { get; set; }

    public PlayerLoadout() : this(Guid.NewGuid(), "Loadout")
    {
    }

    public PlayerLoadout(Guid id, string name)
    {
        guidContainer = new GuidContainer(id);
        label = new LoadoutLabel(name);
    }

    public Guid GetGuid() => guidContainer.guid;
    public string GetName() => label.GetName();
    public bool IsSelected() => isSelected;
    public bool HasEquipment() => saveEquipment;
    public bool HasAlchemy() => saveAlchemy;
    public List<Spell> GetSpells() => new List<Spell>(spells);
    public LoadoutLabel GetLabel() => label;
    public void SetSaveEquipment(bool value)
    {
        if (!SuppressSectionMutation) saveEquipment = value;
    }
    public void SetSaveAlchemy(bool value)
    {
        if (!SuppressSectionMutation) saveAlchemy = value;
    }
}

public sealed class PlayerLoadoutListVariable : AbstractListVariable<PlayerLoadout>
{
    public List<PlayerLoadout> GetAll() => new List<PlayerLoadout>();
    public PlayerLoadout? GetActiveLoadout() => value.FirstOrDefault(loadout => loadout.isSelected);
}

public class SnapshotLoadout<T>
{
    protected Stacked.StackedIdRecord<T> record = new Stacked.StackedIdRecord<T>();
    public bool SuppressSave { get; set; }
    public bool SuppressClear { get; set; }
    public bool IsEmpty() => record.GetTotalStacks() == 0;
    public void Clear()
    {
        if (!SuppressClear) record = new Stacked.StackedIdRecord<T>();
    }
    public Stacked.StackedIdRecord<T> GetRecord() =>
        new Stacked.StackedIdRecord<T>(record);
    public void SaveSnapshot(Stacked.StackedIdRecord<T> value)
    {
        if (!SuppressSave) record = new Stacked.StackedIdRecord<T>(value);
    }
}

public sealed class AlchemySnapshot : SnapshotLoadout<AlchemyRecipeSO>
{
}

public sealed class EquipmentSnapshot : SnapshotLoadout<EquipmentSO>
{
}

public sealed class AlchemySnapshotListVariable : AbstractListVariable<AlchemySnapshot>
{
    public List<AlchemySnapshot> GetAll() => new List<AlchemySnapshot>();
}

public sealed class EquipmentSnapshotListVariable : AbstractListVariable<EquipmentSnapshot>
{
    public List<EquipmentSnapshot> GetAll() => new List<EquipmentSnapshot>();
}

public sealed class LoadoutManager
{
    public static LoadoutManager instance = new LoadoutManager();
    public PlayerLoadoutListVariable playerLoadouts = new PlayerLoadoutListVariable();
    public AlchemySnapshotListVariable alchemyLoadouts = new AlchemySnapshotListVariable();
    public EquipmentSnapshotListVariable equipmentLoadouts = new EquipmentSnapshotListVariable();
    public AlchemyInstanceListVariable activeAlchemy = new AlchemyInstanceListVariable();
    public EquipmentListVariable activeEquipment = new EquipmentListVariable();
    public SpellListVariable activeSpells = new SpellListVariable();
    public bool SuppressSetLoadout { get; set; }
    public bool ThrowAfterSetLoadout { get; set; }
    public int SetLoadoutCalls { get; private set; }
    public int SaveActiveCalls { get; private set; }

    private bool CanSwapLoadouts() =>
        activeSpells.value.All(spell => spell is null ||
            !spell.IsCasting() && !spell.IsReadyingCast());

    public void SetLoadout(PlayerLoadout target)
    {
        SetLoadoutCalls++;
        if (!CanSwapLoadouts()) return;
        if (!SuppressSetLoadout)
        {
            SaveActiveLoadout();
            foreach (var loadout in playerLoadouts.value) loadout.isSelected = false;
            target.isSelected = true;
            activeSpells.value = new List<Spell>(target.spells);
            if (target.saveEquipment) activeEquipment.SetStack(target.equipment);
            if (target.saveAlchemy) activeAlchemy.FromStackedRecord(target.alchemy);
        }
        if (ThrowAfterSetLoadout)
            throw new InvalidOperationException("injected failure after loadout switch");
    }

    public void SaveActiveLoadout()
    {
        SaveActiveCalls++;
        var active = playerLoadouts.GetActiveLoadout();
        if (active is null) return;
        active.spells = new List<Spell>(activeSpells.value);
        if (active.saveEquipment) active.equipment = activeEquipment.GetStackedRecord();
        if (active.saveAlchemy) active.alchemy = activeAlchemy.CreateStackedRecord();
    }
}

namespace Stacked
{
    public class StackedIdEntry<T>
    {
        public T item = default!;
        public int quantity;
    }

    public class AbstractStackedRecord<T, TEntry>
        where TEntry : StackedIdEntry<T>, new()
    {
        protected readonly List<TEntry> entries = new List<TEntry>();

        public void Set(T item, int quantity)
        {
            var entry = entries.FirstOrDefault(candidate => EqualityComparer<T>.Default.Equals(candidate.item, item));
            if (quantity <= 0)
            {
                if (entry is not null) entries.Remove(entry);
                return;
            }
            if (entry is null)
            {
                entry = new TEntry { item = item };
                entries.Add(entry);
            }
            entry.quantity = quantity;
        }

        public int GetQuantity(T item) =>
            entries.FirstOrDefault(candidate => EqualityComparer<T>.Default.Equals(candidate.item, item))?.quantity ?? 0;

        public int GetTotalStacks() => entries.Sum(entry => entry.quantity);

        public void Empty() => entries.Clear();

        public List<TEntry> ToList() => new List<TEntry>(entries);

        /// <summary>One element per distinct item, which is what the game writes back into the list.</summary>
        public List<T> GetItems() => entries.Select(static entry => entry.item).ToList();

        public void ImportItems(List<TEntry> items)
        {
            entries.Clear();
            foreach (var entry in items) Set(entry.item, entry.quantity);
        }

        public List<T> GetItemList()
        {
            var result = new List<T>();
            foreach (var entry in entries)
                for (var index = 0; index < entry.quantity; index++) result.Add(entry.item);
            return result;
        }

        public List<(T, int)> GetEntries() =>
            entries.Select(static entry => (entry.item, entry.quantity)).ToList();
    }

    public sealed class StackedIdRecord<T> : AbstractStackedRecord<T, StackedIdEntry<T>>
    {
        public StackedIdRecord()
        {
        }

        public StackedIdRecord(StackedIdRecord<T> source)
        {
            foreach (var entry in source.GetEntries()) Set(entry.Item1, entry.Item2);
        }

        public StackedIdRecord(List<T> values)
        {
            foreach (var value in values) Set(value, GetQuantity(value) + 1);
        }
    }
}

public sealed class EquipmentSO : IdScriptableObject, IDiscoverable
{
    public static List<EquipmentSO> All = new List<EquipmentSO>();
    private readonly ExperienceContainer experienceContainer = new ExperienceContainer();
    private string stableUuid = Guid.NewGuid().ToString();
    public new string uuid
    {
        get => stableUuid;
        set
        {
            stableUuid = value;
            if (Guid.TryParse(value, out var guid)) base.SetGuid(guid);
        }
    }
    public new string name = "Equipment";
    public int masteryLevel;
    public BigDouble masteryXp;
    public int discRarityLevel;
    public bool isCreated = true;
    public EquipmentTypeSO equipmentType = new EquipmentTypeSO();
    public List<EquipmentTypeSO> subEquipmentTypes = new List<EquipmentTypeSO>();
    public ResourceCostList createCost = new ResourceCostList();
    public List<GlyphSO> glyphRecipe = new List<GlyphSO>();
    public List<ResourceSO> resourceRecipe = new List<ResourceSO>();
    public ResourceCostList usageCost = new ResourceCostList();
    public int NativeMaximumStacks { get; set; } = 4;
    public bool NativeDiscoverVisible { get; set; } = true;
    public bool NativeCanDiscover { get; set; } = true;
    public bool SuppressDiscovery { get; set; }
    public bool ThrowBeforeDiscovery { get; set; }
    public bool ThrowAfterDiscovery { get; set; }
    public bool ThrowOnStateRead { get; set; }
    public int DiscoverCalls { get; private set; }
    public new Guid GetGuid() => Guid.Parse(uuid);
    public new Guid GetId() => GetGuid();
    public string GetName() => name;
    public bool IsCreated() => isCreated;
    public bool IsEquipped() => equippedLevel > 0;
    public int GetMaxLevel() => ThrowOnStateRead
        ? throw new InvalidOperationException("injected equipment state-read failure")
        : NativeMaximumStacks;
    public ResourceCostList GetUsageCost() => usageCost;
    public void Equip(int atLevel)
    {
        if (atLevel == equippedLevel) return;
        equippedLevel = atLevel;
        if (atLevel > 0)
        {
            usageCost.Multiply(new BigDouble(atLevel)).PerformUsage(GetGuid());
            attuningLevel = Math.Min(attuningLevel, atLevel);
            attunementTimeLeft = 1d;
        }
        else
        {
            usageCost.RemoveUsage(GetGuid());
            attuningLevel = 0;
            attunementTimeLeft = -1d;
        }
    }
    public ResourceCostList GetDiscoverCost() => createCost;
    public List<GlyphSO> GetGlyphRecipe() => new List<GlyphSO>(glyphRecipe);
    public List<ResourceSO> GetResourceRecipe() => new List<ResourceSO>(resourceRecipe);
    public bool IsDiscoverVisible() => NativeDiscoverVisible;
    public bool CanDiscover() => NativeCanDiscover;
    public bool IsDiscovered() => isCreated;
    public bool IsDiscoverRequired() => isRequiredDiscovery;
    public void Discover()
    {
        DiscoverCalls++;
        if (ThrowBeforeDiscovery)
            throw new InvalidOperationException("injected failure before discovery");
        if (!SuppressDiscovery) isCreated = true;
        if (ThrowAfterDiscovery)
            throw new InvalidOperationException("injected failure after discovery");
    }
    public ExperienceContainer GetExperienceElement() => experienceContainer;
    public void IncrementActive(double deltaTime)
    {
    }
    public void SetMasteryState(int level, BigDouble experience, BigDouble experiencePerLevel)
    {
        masteryLevel = level;
        masteryXp = experience;
        experienceContainer.SetState(level, experience, experiencePerLevel);
    }
    private void GainMasteryLevels(int levels) => masteryLevel += levels;
    public bool isRequiredDiscovery;
    public ValueModifierRecord power = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord baseLevel = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord experienceRateMod = new ValueModifierRecord(new BigDouble(0.0, 0));
    private int equippedLevel;
    private int attuningLevel;
    private double attunementTimeLeft;
    private BigDouble baseXpRate;
}

public sealed class EquipmentListVariable : StackableListVariable<EquipmentSO>
{
    public int GetTypesEquipped(EquipmentTypeSO equipmentType) =>
        value.Count(item => ReferenceEquals(item.equipmentType, equipmentType));

    /// <summary>
    /// The base write, plus the equip bookkeeping this stub does for the game's own equip side of
    /// the same call. The shipped type declares neither member; both come from the stackable base.
    /// </summary>
    public override void SetStack(Stacked.StackedIdRecord<EquipmentSO> record)
    {
        base.SetStack(record);
        foreach (var item in EquipmentSO.All)
            item.Equip(itemStack.GetQuantity(item));
    }
}

public sealed class EquipmentManager
{
    public static EquipmentManager instance = new EquipmentManager();
    public EquipmentListVariable allEquipment = new EquipmentListVariable { Maximum = int.MaxValue };
    public EquipmentListVariable equippedEquipment = new EquipmentListVariable();
    public bool SuppressMutation { get; set; }
    public bool ThrowBeforeMutation { get; set; }
    public bool ThrowAfterMutation { get; set; }
    public bool ThrowAfterMutationWithoutReadablePostState { get; set; }
    public int EquipCalls { get; private set; }
    public int UnEquipCalls { get; private set; }

    public void EquipItem(EquipmentSO equipment)
    {
        EquipCalls++;
        if (ThrowBeforeMutation) throw new InvalidOperationException("injected equip failure before mutation");
        var cost = equipment.GetUsageCost();
        if (!cost.HasEnough() ||
            (!equippedEquipment.value.Contains(equipment) && equippedEquipment.IsAtMax())) return;
        var amount = Math.Min(
            GlobalVariables.GetMultiBuy().AsInt(),
            Math.Min(
                equipment.GetMaxLevel() - equippedEquipment.GetStacks(equipment),
                cost.MaximumCostTimes().ToInt()));
        if (!SuppressMutation && amount > 0)
        {
            equippedEquipment.Stack(equipment, amount);
            equipment.Equip(equippedEquipment.GetStacks(equipment));
        }
        if (ThrowAfterMutationWithoutReadablePostState)
        {
            equipment.ThrowOnStateRead = true;
            throw new InvalidOperationException("injected equip failure with unreadable post-state");
        }
        if (ThrowAfterMutation) throw new InvalidOperationException("injected equip failure after mutation");
    }

    public void UnEquipItem(EquipmentSO equipment)
    {
        UnEquipCalls++;
        if (ThrowBeforeMutation) throw new InvalidOperationException("injected unequip failure before mutation");
        if (!equipment.IsEquipped()) return;
        var amount = Math.Min(GlobalVariables.GetMultiBuy().AsInt(), equippedEquipment.GetStacks(equipment));
        if (!SuppressMutation && amount > 0)
        {
            equippedEquipment.Unstack(equipment, amount);
            equipment.Equip(equippedEquipment.GetStacks(equipment));
        }
        if (ThrowAfterMutationWithoutReadablePostState)
        {
            equipment.ThrowOnStateRead = true;
            throw new InvalidOperationException("injected unequip failure with unreadable post-state");
        }
        if (ThrowAfterMutation) throw new InvalidOperationException("injected unequip failure after mutation");
    }
}

public sealed class ExperienceContainer
{
    private BigDouble experience;
    private int currentLevel;
    private BigDouble cachedRequiredXp = new BigDouble(1, 100);
    public List<BigDouble> Grants { get; } = new List<BigDouble>();
    public Action? AfterGainExperience { get; set; }
    public bool SuppressGain { get; set; }

    public void SetState(int level, BigDouble currentExperience, BigDouble requiredPerLevel)
    {
        currentLevel = level;
        experience = currentExperience;
        cachedRequiredXp = requiredPerLevel;
    }

    public void GainExperience(BigDouble amount)
    {
        Grants.Add(amount);
        if (!SuppressGain) experience = Add(experience, amount);
        AfterGainExperience?.Invoke();
    }

    public int GetGainedLevels()
    {
        var gained = 0;
        while (Compare(experience, cachedRequiredXp) >= 0 && gained < 10000)
        {
            experience = Subtract(experience, cachedRequiredXp);
            currentLevel++;
            gained++;
        }
        return gained;
    }

    public BigDouble GetExperience() => experience;

    public BigDouble GetRequiredExperience() => cachedRequiredXp;

    public int GetLevel() => currentLevel;

    public ExperienceContainer Clone()
    {
        var clone = new ExperienceContainer();
        clone.SetState(currentLevel, experience, cachedRequiredXp);
        return clone;
    }

    private static BigDouble Add(BigDouble left, BigDouble right) => left + right;

    private static BigDouble Subtract(BigDouble left, BigDouble right)
    {
        var result = left - right;
        return result.Mantissa <= 0 ? default : result;
    }

    private static int Compare(BigDouble left, BigDouble right) => left.CompareTo(right);
}

public sealed class AlchemyInstance : AbstractRefInstance<AlchemyRecipeSO>
{
    public AlchemyInstance()
    {
    }

    public AlchemyInstance(AlchemyRecipeSO reference)
    {
        this.reference = reference;
    }

    public int quantity;
    public int queuedQuantity;
    public ConceptDrainState resourceDrain = new ConceptDrainState();

    public BigDouble GetDrainCostMod() => new BigDouble(quantity * 100d);
    public int GetQueuedQuantity() => queuedQuantity;
    public int GetRemainingFreeUsageSlots() =>
        Math.Max(reference.GetFreeUsageSlots() - queuedQuantity, 0);
    public int GetRemainingMaxUsageSlots() =>
        Math.Max(reference.GetMaxUsageSlots() - queuedQuantity, 0);
}

public sealed class AlchemyInstanceListVariable : AbstractListVariable<AlchemyInstance>
{
    public bool SuppressAddMutation { get; set; }
    public bool SuppressRemoveMutation { get; set; }
    public bool ThrowOnCanAdd { get; set; }
    public int TypelessSlots { get; set; } = 16;
    public Dictionary<AlchemyTypeSO, int> TypeSlots { get; } =
        new Dictionary<AlchemyTypeSO, int>();

    public bool CanAddInstance(AlchemyRecipeSO recipe)
    {
        if (ThrowOnCanAdd) throw new InvalidOperationException("CanAddInstance failed");
        var instance = value.SingleOrDefault(item => ReferenceEquals(item.reference, recipe));
        if (instance is not null && instance.queuedQuantity >= recipe.GetMaxUsageSlots()) return false;
        if (instance is not null) return true;
        return recipe.alchemyTypes.Any(type =>
            GetNumEmptyTypelessSlots() + Math.Max(GetSlotsOnlyForType(type) - GetNumOfType(type), 0) > 0);
    }

    public int GetNumOfType(AlchemyTypeSO type) =>
        value.Count(item =>
            Math.Max(item.quantity, item.queuedQuantity) > 0 &&
            item.reference is not null &&
            item.reference.alchemyTypes.Contains(type));

    public int GetSlotsOnlyForType(AlchemyTypeSO type) =>
        TypeSlots.TryGetValue(type, out var slots) ? slots : 0;

    public int GetNumEmptyTypelessSlots()
    {
        var totalSlots = TypelessSlots + TypeSlots.Values.Sum();
        var emptySlots = Math.Max(0, totalSlots - value.Count(item =>
            Math.Max(item.quantity, item.queuedQuantity) > 0));
        var reservedTyped = TypeSlots.Sum(entry =>
            Math.Max(entry.Value - GetNumOfType(entry.Key), 0));
        return emptySlots - reservedTyped;
    }

    public void AddAlchemyInstances(AlchemyRecipeSO recipe, int delta)
    {
        if (SuppressAddMutation) return;
        var instance = value.SingleOrDefault(item => ReferenceEquals(item.reference, recipe));
        if (instance is null)
        {
            instance = new AlchemyInstance(recipe);
            value.Add(instance);
        }
        instance.queuedQuantity += delta;
    }

    public void RemoveAlchemyInstances(AlchemyRecipeSO recipe, int delta)
    {
        if (SuppressRemoveMutation) return;
        var instance = value.Single(item => ReferenceEquals(item.reference, recipe));
        instance.queuedQuantity = Math.Max(instance.queuedQuantity - delta, 0);
        if (instance.queuedQuantity == 0) value.Remove(instance);
    }

    public void EngageAlchemy(AlchemyRecipeSO recipe)
    {
        if (!CanAddInstance(recipe)) return;
        var instance = value.SingleOrDefault(item => ReferenceEquals(item.reference, recipe));
        var free = instance?.GetRemainingFreeUsageSlots() ?? recipe.GetFreeUsageSlots();
        var remaining = instance?.GetRemainingMaxUsageSlots() ?? recipe.GetMaxUsageSlots();
        var cost = recipe.GetUsageCost();
        var maximum = cost.IsEmpty()
            ? int.MaxValue
            : (cost.MaximumCostTimes() + new BigDouble(Math.Max(free, 0))).ToInt();
        var amount = Math.Min(GlobalVariables.GetMultiBuy().AsInt(), Math.Min(remaining, maximum));
        if (amount > 0) AddAlchemyInstances(recipe, amount);
    }

    public void DisengageAlchemy(AlchemyRecipeSO recipe) =>
        RemoveAlchemyInstances(recipe, GlobalVariables.GetMultiBuy().AsInt());

    public void RebuildCounts()
    {
        foreach (var instance in value) instance.quantity = instance.queuedQuantity;
    }

    public void SetupMaxSlotsValue()
    {
    }

    public Stacked.StackedIdRecord<AlchemyRecipeSO> CreateStackedRecord()
    {
        var result = new Stacked.StackedIdRecord<AlchemyRecipeSO>();
        foreach (var instance in value)
            if (instance.reference is not null && instance.quantity > 0)
                result.Set(instance.reference, instance.quantity);
        return result;
    }

    public void FromStackedRecord(Stacked.StackedIdRecord<AlchemyRecipeSO> record)
    {
        value.Clear();
        foreach (var entry in record.GetEntries())
        {
            value.Add(new AlchemyInstance(entry.Item1)
            {
                quantity = entry.Item2,
                queuedQuantity = entry.Item2,
            });
        }
    }
}

public sealed class ConceptDrainState
{
    public bool isDrainApplied = true;
    public BigDouble currentRatio = new BigDouble(1.0, 0);
    public BigDouble usageRatio = new BigDouble(1.0, 0);
    public ConceptCostVector Current { get; set; } = new ConceptCostVector();
    public BigDouble GetRatio() => new BigDouble(1.0, 0);
    public ConceptCostVector GetCurrentDrain() => Current;
}

public sealed class ConceptCostVector
{
    public ConceptCostVector(params ConceptCostEntry[] entries)
    {
        costs = entries.ToList();
    }

    public List<ConceptCostEntry> costs;
    public List<ConceptCostEntry> Entries => costs;
    public IList GetEntries() => Entries;

    public ConceptCostVector Multiply(BigDouble multiplier) => new ConceptCostVector(
        Entries.Select(entry => new ConceptCostEntry(
            entry.resource,
            entry.Value * multiplier)).ToArray());

    public ConceptCostVector Subtract(ConceptCostVector other)
    {
        var remaining = new List<ConceptCostEntry>();
        foreach (var entry in Entries)
        {
            var previous = other.Entries.FirstOrDefault(item => ReferenceEquals(item.resource, entry.resource));
            remaining.Add(new ConceptCostEntry(
                entry.resource,
                entry.Value - (previous?.Value ?? default)));
        }
        return new ConceptCostVector(remaining.ToArray());
    }
}

public sealed class ConceptCostEntry
{
    public ConceptCostEntry(ConceptResource resource, BigDouble value)
    {
        this.resource = resource;
        Value = value;
        valueBig = value;
    }

    public ConceptResource resource;
    private BigDouble valueBig;
    public BigDouble Value { get; }
    public BigDouble GetValue() => Value;
}

public sealed class ConceptResource
{
    public string uuid = Guid.NewGuid().ToString();
    public string name = "Concept resource";
    public bool AtZero { get; set; }
    public BigDouble TrueRate { get; set; } = new BigDouble(100.0, 0);
    public BigDouble ModdedDrain { get; set; } = new BigDouble(0.0, 0);
    public BigDouble Quantity { get; set; } = new BigDouble(100.0, 0);
    public BigDouble SoftCap { get; set; } = new BigDouble(100.0, 0);
    public bool IsAtZero() => AtZero;
    public BigDouble GetTrueSpend(BigDouble amount) => amount;
    public BigDouble GetTrueRate() => TrueRate;
    public BigDouble GetModdedDrain() => ModdedDrain;
    public bool HasMaxQuantity() => true;
    public BigDouble GetQuantity() => Quantity;
    public BigDouble GetTrueSoftCap() => SoftCap;
    public string GetName() => name;
    public Guid GetGuid() => Guid.TryParse(uuid, out var guid) ? guid : Guid.Empty;
}

public sealed class EffectResultInfo
{
    public static bool SuppressCancel { get; set; }
    public static bool ThrowAfterCancel { get; set; }
    private bool cancelled;
    internal readonly List<TargetingManager.TargetLink> Links = new();
    public bool IsCancelled() => cancelled;
    public void Cancel()
    {
        if (SuppressCancel) return;
        cancelled = true;
        foreach (var link in Links.ToArray()) TargetingManager.RemoveRequest(link);
        if (ThrowAfterCancel) throw new InvalidOperationException("stub throw after cancel");
    }
}

public static class TargetingManager
{
    /// <summary>
    /// How many target requests are open. The game opens one per targeted effect a cast triggers and
    /// closes one per target submitted, which is why resolving a cast is a loop and not a single call.
    /// </summary>
    public static int OpenRequests { get; set; }

    /// <summary>What the selector will offer, or null for a request nothing can satisfy.</summary>
    public static Targeting.ITargetable? AvailableTarget { get; set; }

    public static List<object> SubmittedTargets { get; } = new List<object>();
    public static bool SuppressSubmit { get; set; }
    public static bool ThrowAfterSubmit { get; set; }
    private static TargetLink? currentLink;

    public static bool Targeting
    {
        get => OpenRequests > 0;
        set => OpenRequests = value ? 1 : 0;
    }

    public static bool IsTargeting() => OpenRequests > 0;

    public static TargetLink? GetTargetingLink() =>
        !IsTargeting() || AvailableTarget is null
            ? null
            : currentLink ??= new TargetLink(AvailableTarget);

    public static void SubmitTarget(Targeting.ITargetable target)
    {
        if (SuppressSubmit) return;
        var link = GetTargetingLink();
        link?.AssignTarget(target);
        SubmittedTargets.Add(target);
        if (OpenRequests > 0) OpenRequests--;
        currentLink = null;
        if (ThrowAfterSubmit) throw new InvalidOperationException("stub throw after submit");
    }

    public static void RemoveRequest(TargetLink link)
    {
        if (!ReferenceEquals(currentLink, link)) return;
        currentLink = null;
        if (OpenRequests > 0) OpenRequests--;
    }

    public static void Reset()
    {
        OpenRequests = 0;
        AvailableTarget = null;
        currentLink = null;
        SubmittedTargets.Clear();
        SuppressSubmit = false;
        ThrowAfterSubmit = false;
        EffectResultInfo.SuppressCancel = false;
        EffectResultInfo.ThrowAfterCancel = false;
    }

    public sealed class TargetLink
    {
        private readonly Targeting.ITargetable offered;
        private readonly ITooltipable owner;
        private readonly Targeting.BaseTargetSelection targetSelection = new Targeting.BaseTargetSelection();
        private Targeting.ITargetable? target;
        private readonly EffectResultInfo resultInfo;
        private bool targetFound;

        public TargetLink(Targeting.ITargetable offeredTarget)
        {
            offered = offeredTarget;
            owner = offeredTarget as ITooltipable ?? new TooltipableObject { displayName = "Target request" };
            resultInfo = new EffectResultInfo();
            resultInfo.Links.Add(this);
        }

        public Targeting.ITargetable GetRandom() => offered;
        public List<ITooltipable> GetAllTargets() => offered is ITooltipable tooltipable
            ? new List<ITooltipable> { tooltipable }
            : new List<ITooltipable>();
        public ITooltipable GetOwner() => owner;
        public Targeting.BaseTargetSelection GetTargetSelection() => targetSelection;
        public bool CheckTarget(Targeting.ITargetable candidate) => ReferenceEquals(candidate, offered);
        public bool HasTarget() => targetFound;
        internal void AssignTarget(Targeting.ITargetable candidate)
        {
            target = candidate;
            targetFound = true;
        }
    }
}

public interface ITooltipable
{
    string GetName();
    string GetDisplayType();
    UnityEngine.Sprite GetIcon();
    UnityEngine.Color GetColor();
    bool IsColoredIcon();
    bool HasAltTooltips();
    string GetDescription();
    List<TooltipNode> GetTooltipNodes();
    List<TooltipNode> GetAltTooltipNodes();
}

public class TooltipableObject : IdScriptableObject, ITooltipable
{
    public string displayName = string.Empty;
    public UnityEngine.Sprite Icon { get; set; } = new UnityEngine.Sprite();
    public virtual string GetName() => displayName;
    public UnityEngine.Sprite GetIcon() => Icon;
    public virtual string GetDisplayType() => GetType().Name;
    public virtual UnityEngine.Color GetColor() => UnityEngine.Color.white;
    public virtual bool IsColoredIcon() => false;
    public virtual bool HasAltTooltips() => false;
    public virtual string GetDescription() => string.Empty;
    public virtual List<TooltipNode> GetTooltipNodes() => new();
    public virtual List<TooltipNode> GetAltTooltipNodes() => new();
}

public sealed class AttributeSO : TooltipableObject
{
}

public class TooltipNode
{
    public enum NodeType { Text, Icon, IconText, Divider, Parent }
    public enum ParentType { Plain, Boxed, SoftBoxed }

    public TooltipNode(string text)
    {
        this.text = text;
        textColor = UnityEngine.Color.white;
        color = UnityEngine.Color.white;
    }

    public TooltipNode(string text, UnityEngine.Color color)
        : this(text)
    {
        this.color = color;
    }

    public string Text => text;
    public UnityEngine.Color? Color => color;
    public List<TooltipNode> children = new();
    public UnityEngine.Color color;
    public UnityEngine.Sprite? icon;
    public bool isIconBacked;
    public NodeType nodeType;
    public ParentType parentType;
    public float size = 1f;
    public List<ITooltipable> subTooltips = new();
    public string text;
    public UnityEngine.Color textColor;
    public Func<string>? textFn;
    public ITooltipable? tooltipable;
}

public class UITooltipContainer : UnityEngine.MonoBehaviour
{
    public static List<UITooltipContainer> globalTooltips = new();
    public ITooltipable? item;
}

public class HoverTooltip : UnityEngine.MonoBehaviour
{
    public ITooltipable? tooltipItem;
    public TooltipableObject? setupObject;
    private List<ITooltipable> subTooltips = new();
    public void Setup(ITooltipable item, List<ITooltipable>? subTooltips = null)
    {
        tooltipItem = item;
        this.subTooltips = subTooltips ?? new List<ITooltipable>();
    }
    public void OpenTooltip() { }
}

namespace BepInEx
{
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class BepInPlugin : Attribute
    {
        public BepInPlugin(string guid, string name, string version)
        {
            GUID = guid;
            Name = name;
            Version = new Version(version);
        }

        public string GUID { get; }

        public string Name { get; }

        public Version Version { get; }
    }

    public class BaseUnityPlugin : UnityEngine.MonoBehaviour
    {
        public Configuration.ConfigFile Config { get; } = new Configuration.ConfigFile();

        public Logging.ManualLogSource Logger { get; } = new Logging.ManualLogSource();
    }

    public static class Paths
    {
        public static string GameRootPath { get; set; } = string.Empty;
        public static string ConfigPath { get; set; } = string.Empty;
    }

    public sealed class PluginInfo
    {
        public BepInPlugin Metadata { get; set; } = new BepInPlugin("test.plugin", "Test Plugin", "1.0.0");

        public BaseUnityPlugin? Instance { get; set; }
    }
}

namespace BepInEx.Bootstrap
{
    public static class Chainloader
    {
        public static Dictionary<string, BepInEx.PluginInfo> PluginInfos { get; } = new Dictionary<string, BepInEx.PluginInfo>();
    }
}

namespace BepInEx.Configuration
{
    public sealed class ConfigFile : IEnumerable<KeyValuePair<ConfigDefinition, ConfigEntryBase>>
    {
        private readonly Dictionary<ConfigDefinition, ConfigEntryBase> _entries = new Dictionary<ConfigDefinition, ConfigEntryBase>();
        private readonly Dictionary<ConfigDefinition, string> _orphanedEntries = new Dictionary<ConfigDefinition, string>();
        private readonly Dictionary<ConfigDefinition, string> _persistedEntries = new Dictionary<ConfigDefinition, string>();

        public ConfigFile(string configFilePath = "")
        {
            ConfigFilePath = configFilePath;
        }

        public bool SaveOnConfigSet { get; set; } = true;

        public string ConfigFilePath { get; }

        public int SaveCalls { get; private set; }

        public int ReloadCalls { get; private set; }

        public int? ThrowOnSaveCall { get; set; }

        public int? ThrowOnReloadCall { get; set; }

        public event EventHandler<SettingChangedEventArgs>? SettingChanged;

        public bool ThrowOnEveryReload { get; set; }

        public ConfigDefinition? ThrowOnBindDefinition { get; set; }

        public ConfigEntry<T> Bind<T>(string section, string key, T defaultValue, string description)
        {
            return Bind(section, key, defaultValue, new ConfigDescription(description));
        }

        public ConfigEntry<T> Bind<T>(string section, string key, T defaultValue, ConfigDescription description)
        {
            var definition = new ConfigDefinition(section, key);
            if (definition.Equals(ThrowOnBindDefinition))
            {
                throw new InvalidOperationException($"simulated config bind failure for [{section}] {key}");
            }
            if (_entries.TryGetValue(definition, out var existing))
            {
                return (ConfigEntry<T>)existing;
            }

            var entry = new ConfigEntry<T>(this, definition, defaultValue, description);
            if (_orphanedEntries.Remove(definition, out var serialized)) entry.SetSerializedValue(serialized);
            _entries.Add(definition, entry);
            return entry;
        }

        public void Save()
        {
            SaveCalls++;
            if (ThrowOnSaveCall == SaveCalls)
            {
                throw new InvalidOperationException($"simulated config save failure on call {SaveCalls}");
            }

            _persistedEntries.Clear();
            foreach (var pair in _orphanedEntries) _persistedEntries[pair.Key] = pair.Value;
            foreach (var pair in _entries) _persistedEntries[pair.Key] = pair.Value.GetSerializedValue();
        }

        public void Reload()
        {
            ReloadCalls++;
            if (ThrowOnEveryReload || ThrowOnReloadCall == ReloadCalls)
            {
                throw new InvalidOperationException($"simulated config reload failure on call {ReloadCalls}");
            }

            _orphanedEntries.Clear();
            foreach (var pair in _persistedEntries)
            {
                if (_entries.TryGetValue(pair.Key, out var entry)) entry.SetSerializedValue(pair.Value);
                else _orphanedEntries[pair.Key] = pair.Value;
            }
        }

        public void SeedSerialized(string section, string key, string value)
        {
            var definition = new ConfigDefinition(section, key);
            _orphanedEntries[definition] = value;
            _persistedEntries[definition] = value;
        }

        public bool TryGetPersisted(string section, string key, out string value) =>
            _persistedEntries.TryGetValue(new ConfigDefinition(section, key), out value!);

        public bool Remove(ConfigDefinition definition) => _entries.Remove(definition);

        internal void OnSettingChanged(ConfigEntryBase entry) =>
            SettingChanged?.Invoke(this, new SettingChangedEventArgs(entry));

        public IEnumerator<KeyValuePair<ConfigDefinition, ConfigEntryBase>> GetEnumerator() => _entries.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public sealed class SettingChangedEventArgs : EventArgs
    {
        public SettingChangedEventArgs(ConfigEntryBase changedSetting) =>
            ChangedSetting = changedSetting;

        public ConfigEntryBase ChangedSetting { get; }
    }

    public sealed class ConfigDefinition : IEquatable<ConfigDefinition>
    {
        public ConfigDefinition(string section, string key)
        {
            Section = section;
            Key = key;
        }

        public string Section { get; }

        public string Key { get; }

        public bool Equals(ConfigDefinition? other) => other is not null && Section == other.Section && Key == other.Key;

        public override bool Equals(object? obj) => Equals(obj as ConfigDefinition);

        public override int GetHashCode() => HashCode.Combine(Section, Key);
    }

    public abstract class ConfigEntryBase
    {
        protected ConfigEntryBase(ConfigFile configFile, ConfigDefinition definition, Type settingType, object? defaultValue, ConfigDescription description)
        {
            ConfigFile = configFile;
            Definition = definition;
            SettingType = settingType;
            DefaultValue = defaultValue;
            Description = description;
        }

        public ConfigFile ConfigFile { get; }

        public ConfigDefinition Definition { get; }

        public ConfigDescription Description { get; }

        public Type SettingType { get; }

        public object? DefaultValue { get; }

        public abstract object? BoxedValue { get; set; }

        public string GetSerializedValue() => Convert.ToString(BoxedValue, CultureInfo.InvariantCulture) ?? string.Empty;

        public void SetSerializedValue(string value)
        {
            if (SettingType == typeof(string))
            {
                BoxedValue = value;
                return;
            }

            BoxedValue = TomlTypeConverter.ConvertToValue(value, SettingType);
        }
    }

    internal static class TomlTypeConverter
    {
        internal static object ConvertToValue(string value, Type targetType)
        {
            if (targetType == typeof(KeyboardShortcut))
            {
                var keys = value
                    .Split('+')
                    .Select(part =>
                    {
                        var trimmed = part.Trim();
                        if (trimmed.Length == 0)
                            throw new FormatException("A keyboard shortcut key cannot be empty.");
                        return Enum.Parse<UnityEngine.KeyCode>(trimmed, ignoreCase: true);
                    })
                    .ToArray();
                if (keys.Length == 0)
                    throw new FormatException("A keyboard shortcut requires a main key.");
                return new KeyboardShortcut(keys[0], keys.Skip(1).ToArray());
            }

            return targetType.IsEnum
                ? Enum.Parse(targetType, value, true)
                : Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture)!;
        }
    }

    public sealed class ConfigEntry<T> : ConfigEntryBase
    {
        private T _value;

        public ConfigEntry(T value)
            : this(new ConfigFile(), new ConfigDefinition("Test", "Value"), value, new ConfigDescription(string.Empty))
        {
        }

        internal ConfigEntry(ConfigFile configFile, ConfigDefinition definition, T value, ConfigDescription description)
            : base(configFile, definition, typeof(T), value, description)
        {
            _value = value;
        }

        public T Value
        {
            get => _value;
            set
            {
                _value = value;
                SettingChanged?.Invoke(this, EventArgs.Empty);
                ConfigFile.OnSettingChanged(this);
            }
        }

        public override object? BoxedValue
        {
            get => Value;
            set => Value = (T)value!;
        }

        public event EventHandler? SettingChanged;

        public void RaiseChanged()
        {
            SettingChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public sealed class ConfigDescription
    {
        public ConfigDescription(string description, AcceptableValueBase? acceptableValues = null, params object[] tags)
        {
            Description = description;
            AcceptableValues = acceptableValues;
            Tags = tags;
        }

        public string Description { get; }

        public AcceptableValueBase? AcceptableValues { get; }

        public object[] Tags { get; }
    }

    public abstract class AcceptableValueBase
    {
        protected AcceptableValueBase(Type valueType)
        {
            ValueType = valueType;
        }

        public Type ValueType { get; }

        public abstract object Clamp(object value);

        public abstract bool IsValid(object value);

        public abstract string ToDescriptionString();
    }

    public sealed class AcceptableValueRange<T> : AcceptableValueBase
    {
        public AcceptableValueRange(T minValue, T maxValue)
            : base(typeof(T))
        {
            MinValue = minValue;
            MaxValue = maxValue;
        }

        public T MinValue { get; }

        public T MaxValue { get; }

        public override object Clamp(object value)
        {
            if (value is not T typed)
            {
                return MinValue!;
            }

            if (Comparer<T>.Default.Compare(typed, MinValue) < 0)
            {
                return MinValue!;
            }

            return Comparer<T>.Default.Compare(typed, MaxValue) > 0 ? MaxValue! : typed!;
        }

        public override bool IsValid(object value)
        {
            return value is T typed &&
                   Comparer<T>.Default.Compare(typed, MinValue) >= 0 &&
                   Comparer<T>.Default.Compare(typed, MaxValue) <= 0;
        }

        public override string ToDescriptionString() => $"Range: {MinValue} - {MaxValue}";
    }

    public sealed class AcceptableValueList<T> : AcceptableValueBase
    {
        public AcceptableValueList(params T[] acceptableValues)
            : base(typeof(T))
        {
            AcceptableValues = acceptableValues;
        }

        public IReadOnlyList<T> AcceptableValues { get; }

        public override object Clamp(object value) => IsValid(value) ? value : AcceptableValues[0]!;

        public override bool IsValid(object value) => value is T typed && AcceptableValues.Contains(typed);

        public override string ToDescriptionString() => $"# Acceptable values: {string.Join(", ", AcceptableValues)}";
    }

    /// <summary>
    /// Carries the chord it was built from, because code under test compares a persisted shortcut
    /// against the defaults it may have inherited, and a shortcut that remembers nothing makes every
    /// chord equal to every other one.
    /// </summary>
    public readonly struct KeyboardShortcut : IEquatable<KeyboardShortcut>
    {
        private readonly UnityEngine.KeyCode[]? _modifiers;

        public KeyboardShortcut(UnityEngine.KeyCode mainKey, params UnityEngine.KeyCode[] modifiers)
        {
            MainKey = mainKey;
            _modifiers = modifiers;
        }

        public UnityEngine.KeyCode MainKey { get; }

        public IEnumerable<UnityEngine.KeyCode> Modifiers =>
            _modifiers ?? Array.Empty<UnityEngine.KeyCode>();

        public bool IsDown()
        {
            return false;
        }

        public bool Equals(KeyboardShortcut other) =>
            MainKey == other.MainKey &&
            Modifiers.OrderBy(key => key).SequenceEqual(other.Modifiers.OrderBy(key => key));

        public override bool Equals(object? obj) => obj is KeyboardShortcut other && Equals(other);

        public override int GetHashCode()
        {
            var hash = (int)MainKey;
            foreach (var modifier in Modifiers.OrderBy(key => key)) hash = (hash * 397) ^ (int)modifier;
            return hash;
        }

        public override string ToString() =>
            string.Join(" + ", new[] { MainKey }.Concat(Modifiers));
    }
}

namespace BepInEx.Logging
{
    public sealed class ManualLogSource
    {
        public List<object> Entries { get; } = new List<object>();

        public void LogDebug(object data) => Entries.Add(data);

        public void LogInfo(object data) => Entries.Add(data);

        public void LogWarning(object data) => Entries.Add(data);

        public void LogError(object data) => Entries.Add(data);
    }
}

namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public sealed class HarmonyPatchAttribute : Attribute
    {
    }

    public sealed class HarmonyMethod : Attribute
    {
        public HarmonyMethod(Type type, string methodName)
        {
        }
    }

    public sealed class Harmony
    {
        public Harmony(string id)
        {
        }

        public void PatchAll(Assembly assembly)
        {
        }

        public PatchClassProcessor CreateClassProcessor(Type type) => new(this, type);

        public void Patch(MethodBase original, HarmonyMethod? prefix = null, HarmonyMethod? postfix = null, HarmonyMethod? transpiler = null, HarmonyMethod? finalizer = null)
        {
        }

        public void UnpatchSelf()
        {
        }
    }

    public sealed class PatchClassProcessor
    {
        public PatchClassProcessor(Harmony instance, Type type)
        {
        }

        public List<MethodInfo> Patch() => new();
    }

    public static class AccessTools
    {
        public static MethodInfo? Method(string typeColonMethod)
        {
            var separator = typeColonMethod.IndexOf(':');
            if (separator <= 0 || separator == typeColonMethod.Length - 1)
            {
                return null;
            }

            var type = TypeByName(typeColonMethod.Substring(0, separator));
            var methodName = typeColonMethod.Substring(separator + 1);
            return type?.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(method => method.Name == methodName);
        }

        public static Type? TypeByName(string name)
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            var preferred = assemblies.FirstOrDefault(assembly =>
                assembly.GetName().Name == "Assembly-CSharp");
            if (preferred is not null)
            {
                var preferredType = FindType(preferred, name);
                if (preferredType is not null)
                {
                    return preferredType;
                }
            }

            foreach (var assembly in assemblies)
            {
                var match = FindType(assembly, name);
                if (match is not null)
                {
                    return match;
                }
            }

            return null;
        }

        private static Type? FindType(Assembly assembly, string name)
        {
            try
            {
                return assembly.GetTypes().FirstOrDefault(type => type.Name == name || type.FullName == name);
            }
            catch (ReflectionTypeLoadException exception)
            {
                return exception.Types
                    .Where(type => type is not null)
                    .FirstOrDefault(type => type!.Name == name || type.FullName == name);
            }
        }

        public static List<MethodInfo> GetDeclaredMethods(Type type)
        {
            return new List<MethodInfo>(type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly));
        }
    }
}

namespace UnityEngine
{
    public class Object
    {
        public string name { get; set; } = string.Empty;

        public static T Instantiate<T>(T original, Transform parent, bool worldPositionStays) where T : Object, new()
        {
            var clone = original is GameObject originalGameObject &&
                        originalGameObject.transform is RectTransform
                ? (T)(Object)new GameObject(original.name, typeof(RectTransform))
                : new T();
            if (clone is GameObject gameObject)
            {
                gameObject.transform.SetParent(parent, worldPositionStays);
            }

            return clone;
        }

        public static void Destroy(Object obj)
        {
        }
    }

    public class Component : Object
    {
        public GameObject gameObject { get; internal set; } = null!;

        public Transform transform { get; internal set; } = null!;

        public T? GetComponent<T>() where T : Component => gameObject?.GetComponent<T>();

        public Component? GetComponent(Type type) => gameObject?.GetComponent(type);

        public T? GetComponentInChildren<T>(bool includeInactive = false) where T : Component => gameObject?.GetComponent<T>();

        public T[] GetComponents<T>() where T : Component => gameObject?.GetComponents<T>() ?? Array.Empty<T>();
    }

    public class Behaviour : Component
    {
        public bool enabled { get; set; } = true;
    }

    public class MonoBehaviour : Behaviour
    {
        public Coroutine StartCoroutine(IEnumerator routine) => new Coroutine(routine);
    }

    public sealed class Coroutine
    {
        public Coroutine(IEnumerator routine) => Routine = routine;
        public IEnumerator Routine { get; }
    }

    public sealed class WaitForEndOfFrame { }

    public class Texture2D : Object
    {
        public Texture2D() : this(1920, 1080) { }
        public Texture2D(int width, int height)
        {
            this.width = width;
            this.height = height;
        }
        public int width { get; }
        public int height { get; }
        public Color GetPixelBilinear(float u, float v) => default;
        public void SetPixel(int x, int y, Color color) { }
        public void Apply(bool updateMipmaps = true, bool makeNoLongerReadable = false) { }
        public byte[] EncodeToPNG() => new byte[] { 137, 80, 78, 71 };
    }

    public class GameObject : Object
    {
        private readonly List<Component> _components = new List<Component>();

        public GameObject()
            : this(string.Empty)
        {
        }

        public GameObject(string name, params Type[] components)
        {
            this.name = name;
            transform = components.Contains(typeof(RectTransform))
                ? new RectTransform { gameObject = this, name = name }
                : new Transform { gameObject = this, name = name };
            transform.transform = transform;
            _components.Add(transform);
            foreach (var type in components.Where(type => type != typeof(RectTransform) && typeof(Component).IsAssignableFrom(type)))
            {
                var component = (Component)Activator.CreateInstance(type)!;
                component.gameObject = this;
                component.transform = transform;
                component.name = name;
                _components.Add(component);
            }
        }

        public Transform transform { get; }

        public bool activeInHierarchy { get; set; } = true;
        public bool activeSelf { get; private set; } = true;
        public UnityEngine.SceneManagement.Scene scene { get; set; } =
            new UnityEngine.SceneManagement.Scene("Main");

        public static GameObject? Find(string name) => null;

        public void SetActive(bool value)
        {
            activeSelf = value;
            activeInHierarchy = value;
        }

        public T? GetComponent<T>() where T : Component => _components.OfType<T>().FirstOrDefault();

        public T? GetComponentInChildren<T>(bool includeInactive = false) where T : Component => GetComponent<T>();

        public Component? GetComponent(Type type) => _components.FirstOrDefault(type.IsInstanceOfType);

        public T[] GetComponents<T>() where T : Component => _components.OfType<T>().ToArray();

        public T AddComponent<T>() where T : Component, new()
        {
            var component = new T { gameObject = this, transform = transform, name = name };
            _components.Add(component);
            return component;
        }
    }

    public class Transform : Component
    {
        private readonly List<Transform> _children = new List<Transform>();

        public Transform? parent { get; private set; }

        public int childCount => _children.Count;

        public void SetParent(Transform parent, bool worldPositionStays)
        {
            if (ReferenceEquals(this.parent, parent)) return;
            this.parent?._children.Remove(this);
            this.parent = parent;
            parent._children.Add(this);
        }

        public int GetSiblingIndex() => parent?._children.IndexOf(this) ?? 0;

        public void SetSiblingIndex(int index)
        {
            if (parent is null) return;
            parent._children.Remove(this);
            parent._children.Insert(Math.Max(0, Math.Min(index, parent._children.Count)), this);
        }

        public void SetAsLastSibling()
        {
            if (parent is null) return;
            parent._children.Remove(this);
            parent._children.Add(this);
        }

        public Transform GetChild(int index) => _children[index];

        public bool IsChildOf(Transform candidate)
        {
            for (var node = this; node is not null; node = node.parent)
                if (ReferenceEquals(node, candidate)) return true;
            return false;
        }
    }

    public class RectTransform : Transform
    {
        public Vector2 anchorMin { get; set; }
        public Vector2 anchorMax { get; set; }
        public Vector2 offsetMin { get; set; }
        public Vector2 offsetMax { get; set; }
        public Vector2 pivot { get; set; }
        public Vector2 anchoredPosition { get; set; }
        public Vector2 sizeDelta { get; set; }
        public Rect rect { get; set; } = new Rect(0f, 0f, 44f, 44f);
    }

    public readonly struct Vector2
    {
        public Vector2(float x, float y)
        {
            this.x = x;
            this.y = y;
        }

        public readonly float x;
        public readonly float y;
        public static Vector2 zero => new Vector2(0f, 0f);
        public static Vector2 one => new Vector2(1f, 1f);
    }

    public readonly struct Vector3
    {
        public Vector3(float x, float y, float z = 0f)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }

        public readonly float x;
        public readonly float y;
        public readonly float z;
    }

    public readonly struct Color
    {
        public Color(float r, float g, float b, float a = 1f)
        {
            this.r = r;
            this.g = g;
            this.b = b;
            this.a = a;
        }

        public readonly float r;
        public readonly float g;
        public readonly float b;
        public readonly float a;
        public static Color white => new Color(1f, 1f, 1f, 1f);

        public static Color Lerp(Color a, Color b, float t) => t < 0.5f ? a : b;
    }

    public readonly struct Color32
    {
        public Color32(byte r, byte g, byte b, byte a)
        {
            this.r = r;
            this.g = g;
            this.b = b;
            this.a = a;
        }

        public readonly byte r;
        public readonly byte g;
        public readonly byte b;
        public readonly byte a;

        public static implicit operator Color32(Color color) => new Color32(
            (byte)(color.r * byte.MaxValue),
            (byte)(color.g * byte.MaxValue),
            (byte)(color.b * byte.MaxValue),
            (byte)(color.a * byte.MaxValue));
    }

    public class CanvasRenderer : Component
    {
    }

    public class CanvasGroup : Behaviour
    {
    }

    public class Material : Object
    {
    }

    public class Sprite : Object
    {
    }

    public class ScriptableObject : Object
    {
    }

    public enum KeyCode
    {
        None = 0,
        Equals = 61,
        Minus = 45,
        Alpha0 = 48,
        Alpha1 = 49,
        Alpha2 = 50,
        Alpha3 = 51,
        Alpha4 = 52,
        Alpha5 = 53,
        Alpha6 = 54,
        Alpha7 = 55,
        Alpha8 = 56,
        Alpha9 = 57,
        Space = 32,
        Q = 113,
        W = 119,
        E = 101,
        R = 114,
        T = 116,
        X = 120,
        Y = 121,
        Z = 122,
        J = 106,
        M = 109,
        Keypad1 = 257,
        Keypad2 = 258,
        Keypad3 = 259,
        Keypad4 = 260,
        Keypad5 = 261,
        Keypad6 = 262,
        Keypad7 = 263,
        Keypad8 = 264,
        Keypad9 = 265,
        UpArrow = 273,
        DownArrow = 274,
        RightArrow = 275,
        LeftArrow = 276,
        F7 = 288,
        F8 = 289,
        RightShift = 303,
        LeftShift = 304,
        RightControl = 305,
        LeftControl = 306,
        RightAlt = 307,
        LeftAlt = 308,
    }

    public static class Time
    {
        public static float timeScale { get; set; } = 1.0f;
        public static float fixedDeltaTime { get; set; } = 0.02f;
        public static float deltaTime { get; set; } = 0.016f;
        public static float unscaledDeltaTime { get; set; } = 0.016f;
        public static float realtimeSinceStartup { get; set; }
        public static int frameCount { get; set; }
    }

    public static class Application
    {
        public static string persistentDataPath { get; set; } = string.Empty;
    }

    public static class ScreenCapture
    {
        public static void CaptureScreenshot(string filename)
        {
        }

        public static Texture2D CaptureScreenshotAsTexture() => new Texture2D();
    }

    public static class Resources
    {
        public static List<Object> Objects { get; } = new List<Object>();
        public static Object[] FindObjectsOfTypeAll(Type type) =>
            Objects.Where(type.IsInstanceOfType).ToArray();
    }

    public readonly struct Rect
    {
        public Rect(float x, float y, float width, float height)
        {
            this.x = x;
            this.y = y;
            this.width = width;
            this.height = height;
        }

        public readonly float x;
        public readonly float y;
        public readonly float width;
        public readonly float height;
        public float xMin => x;
        public float xMax => x + width;
        public float yMin => y;
        public float yMax => y + height;
    }

    public static class GUI
    {
        public static void Box(Rect position, string text)
        {
        }
    }
}

namespace UnityEngine.Events
{
    public delegate void UnityAction();
}

namespace UnityEngine.UI
{
    public class Graphic : UnityEngine.Behaviour
    {
        public UnityEngine.Color color { get; set; } = UnityEngine.Color.white;
        public bool raycastTarget { get; set; } = true;
        public UnityEngine.RectTransform rectTransform =>
            (UnityEngine.RectTransform)transform;

        protected void SetVerticesDirty()
        {
        }
    }

    public class MaskableGraphic : Graphic
    {
        protected virtual void OnPopulateMesh(VertexHelper helper)
        {
        }
    }

    public sealed class VertexHelper
    {
        public int currentVertCount { get; private set; }

        public void Clear() => currentVertCount = 0;

        public void AddVert(
            UnityEngine.Vector3 position,
            UnityEngine.Color32 color,
            UnityEngine.Vector2 uv0) =>
            currentVertCount++;

        public void AddTriangle(int index0, int index1, int index2)
        {
        }
    }

    public class Image : Graphic
    {
        public enum Type
        {
            Simple,
            Sliced,
            Tiled,
            Filled,
        }

        public UnityEngine.Sprite? sprite { get; set; }
        public bool preserveAspect { get; set; }
        public Type type { get; set; }
    }

    public class Selectable : UnityEngine.Behaviour
    {
        public bool interactable { get; set; } = true;

        public bool IsInteractable() => interactable;

        public Graphic? targetGraphic { get; set; }
    }

    public class Button : Selectable
    {
        public ButtonClickedEvent onClick { get; } = new ButtonClickedEvent();

        public sealed class ButtonClickedEvent
        {
            private readonly List<UnityEngine.Events.UnityAction> _listeners = new List<UnityEngine.Events.UnityAction>();

            public void AddListener(UnityEngine.Events.UnityAction listener) => _listeners.Add(listener);

            public void RemoveListener(UnityEngine.Events.UnityAction listener) => _listeners.Remove(listener);

            public void RemoveAllListeners() => _listeners.Clear();

            public void Invoke()
            {
                foreach (var listener in _listeners.ToArray()) listener();
            }
        }
    }

    public class RectMask2D : UnityEngine.Behaviour
    {
    }

    public class LayoutElement : UnityEngine.Behaviour
    {
        public bool ignoreLayout { get; set; }
    }

    public class ScrollRect : UnityEngine.Behaviour
    {
        public enum MovementType
        {
            Unrestricted,
            Elastic,
            Clamped,
        }

        public bool horizontal { get; set; }
        public bool vertical { get; set; }
        public bool inertia { get; set; }
        public MovementType movementType { get; set; }
        public float scrollSensitivity { get; set; }
        public float horizontalNormalizedPosition { get; set; }
        public float verticalNormalizedPosition { get; set; }
        public UnityEngine.RectTransform? viewport { get; set; }
        public UnityEngine.RectTransform? content { get; set; }
    }
}

namespace TMPro
{
    public enum TextAlignmentOptions
    {
        Center,
        Midline,
        MidlineLeft,
        TopLeft,
    }

    public enum TextOverflowModes
    {
        Overflow,
        Ellipsis,
    }

    public enum TextWrappingModes
    {
        NoWrap,
        Normal,
    }

    public class TMP_FontAsset : UnityEngine.Object
    {
    }

    public class TextMeshProUGUI : UnityEngine.UI.Graphic
    {
        public string text { get; set; } = string.Empty;
        public TMP_FontAsset? font { get; set; }
        public UnityEngine.Material? fontSharedMaterial { get; set; }
        public float fontSize { get; set; } = 16f;
        public TextAlignmentOptions alignment { get; set; }
        public TextWrappingModes textWrappingMode { get; set; }
        public TextOverflowModes overflowMode { get; set; }

        public UnityEngine.Vector2 GetPreferredValues(string value, float width, float height)
        {
            var characterWidth = Math.Max(1f, fontSize * 0.5f);
            var charactersPerLine = Math.Max(1, (int)(Math.Max(1f, width) / characterWidth));
            var lineCount = 0;
            var maximumLineLength = 0;
            foreach (var line in (value ?? string.Empty).Split('\n'))
            {
                maximumLineLength = Math.Max(maximumLineLength, line.Length);
                lineCount += Math.Max(1, (line.Length + charactersPerLine - 1) / charactersPerLine);
            }

            return new UnityEngine.Vector2(
                Math.Min(Math.Max(1f, width), maximumLineLength * characterWidth),
                Math.Max(fontSize, lineCount * fontSize * 1.2f));
        }
    }

    public class TMP_InputField : UnityEngine.UI.Selectable
    {
        public enum LineType
        {
            SingleLine,
            MultiLineSubmit,
            MultiLineNewline,
        }

        public UnityEngine.RectTransform? textViewport { get; set; }
        public TextMeshProUGUI? textComponent { get; set; }
        public string text { get; set; } = string.Empty;
          public LineType lineType { get; set; }
          public OnChangeEvent onValueChanged { get; } = new OnChangeEvent();
          public OnChangeEvent onSelect { get; } = new OnChangeEvent();
          public OnChangeEvent onEndEdit { get; } = new OnChangeEvent();

        public sealed class OnChangeEvent
        {
            private readonly List<Action<string>> _listeners = new List<Action<string>>();

            public void AddListener(Action<string> listener) => _listeners.Add(listener);
        }
    }
}

namespace UnityEngine.SceneManagement
{
    public enum LoadSceneMode
    {
        Single = 0,
        Additive = 1,
    }

    public readonly struct Scene
    {
        public Scene(string name)
        {
            this.name = name;
        }

        public readonly string name;

        public bool IsValid() => name is not null;

        public bool isLoaded => IsValid();
    }

    public static class SceneManager
    {
        public static event Action<Scene, Scene>? activeSceneChanged;

        public static event Action<Scene, LoadSceneMode>? sceneLoaded;

        public static Scene ActiveScene { get; set; } = new Scene("Main");

        public static Scene GetActiveScene() => ActiveScene;

        public static void RaiseActiveSceneChanged(Scene previous, Scene next)
        {
            ActiveScene = next;
            activeSceneChanged?.Invoke(previous, next);
        }

        public static void RaiseSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            sceneLoaded?.Invoke(scene, mode);
        }
    }
}
