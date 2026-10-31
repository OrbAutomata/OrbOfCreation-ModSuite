using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

public class Spell : ITooltipable
{
    public List<SpellTypeSO> augmentedSpellTypes = new List<SpellTypeSO>();
    private SpellRecipeSO? reference;
    public static Action? FireSignal { get; set; }
    public string DisplayName { get; set; } = "Spell";
    public bool Channeled { get; set; }
    public bool EmitFireSignal { get; set; } = true;
    public bool HoldingCharge { get; private set; }

    public void InitializePersistence()
    {
    }

    public void StartCharging()
    {
    }
    public int FireCalls { get; private set; }
    public int CurrentCharges { get; set; }
    public int MaximumCharges { get; set; }
    public BigDouble CooldownRemaining { get; set; }
    public BigDouble Recharge { get; set; }

    /// <summary>
    /// What the refused branch of <c>SpellManager.RemoveSpell</c> leaves behind, so a test can see
    /// that a call the game would refuse was never made.
    /// </summary>
    public bool SwitchedToTimeBasedCooldown { get; set; }
    public UnityEngine.Sprite Icon { get; set; } = new UnityEngine.Sprite();
    public ResourceCostList Cost { get; set; } = new ResourceCostList();
    public GuidContainer guidContainer = new GuidContainer(Guid.NewGuid());
    public Stacked.StackedIdRecord<GlyphSO> augmentGlyphRefs = new Stacked.StackedIdRecord<GlyphSO>();
    private List<GlyphSO> augmentGlyphs = new List<GlyphSO>();
    public ValueModifierRecord costMod = new ValueModifierRecord(new BigDouble(100));
    public int BaseEffectLevel { get; set; } = 1;
    public bool DurationSpell { get; set; }
    public bool ToggledSpell { get; set; }
    public bool NativeUsageRequirementsMet { get; set; } = true;
    public bool NativeUnique { get; set; }
    public bool NativeEmpty { get; set; }
    public bool NativeCasting { get; set; }
    public Exception? CastingReadFailure { get; set; }
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
    public bool IsCasting() => CastingReadFailure is { } failure ? throw failure : NativeCasting;
    public bool IsReadyingCast() => NativeReadyingCast;

    /// <summary>
    /// The game's own composite readiness answer, settable so a boundary can be shown refusing a cast
    /// the plan believed was ready — which is the ordinary case for a planner working off a snapshot.
    /// </summary>
    public bool NativeCanCast { get; set; } = true;

    public bool CanCast() => NativeCanCast;
    public bool CanFire() => NativeCasting || NativeCanCast;
    public bool IsAttuning() => NativeAttuning;
    public bool IsChargeAvailable() => NativeChargeAvailable;
    public bool CanRemove() { CanRemoveCalls++; return IsChargeAvailable() && !IsCasting(); }

    /// <summary>
    /// <c>charges &gt;= GetMaxSpellCharges()</c>, which is the first of the three facts
    /// <c>SpellManager.RemoveSpell</c> gates itself on. <c>CanRemove()</c> is a neighbouring
    /// predicate the removal path never reads.
    /// </summary>
    public bool IsAtMaxCharges() => CurrentCharges >= MaximumCharges;
    public bool HasEnoughResources() => NativeHasEnoughResources;
    public int GetCurrSpellCharges() => CurrentCharges;
    public int GetMaxSpellCharges() => MaximumCharges;
    public BigDouble GetCooldownTimeRemaining() => CooldownRemaining;

    /// <summary>
    /// A full recharge after cooldown speed — the number the casting bar counts down from,
    /// and the one the recipe tooltip heads with.
    /// </summary>
    public BigDouble GetRecharge() => Recharge;
    public ResourceCostList GetCost() => Cost;
    public bool ThrowOnDrainCost { get; set; }
    public ResourceCostList GetDrainCost() =>
        ThrowOnDrainCost ? throw new InvalidOperationException("GetDrainCost failed") : DrainCost;
    public int GetBaseEffectLevel() => BaseEffectLevel;
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

    /// <summary>
    /// The game's shape: the recipe's usage plus each augment's usage once per copy it carries.
    /// </summary>
    public ResourceCostList GetUsageCost()
    {
        var recipeCost = reference?.GetUsageCost() ?? new ResourceCostList();
        var cost = new ResourceCostList();
        cost.costs.AddRange(recipeCost.costs);
        foreach (var glyph in augmentGlyphs)
            foreach (var row in glyph.usageCost.costs)
                cost.costs.Add(new ResourceTuple(
                    row.resource, row.GetValue() * new BigDouble(GetQuantityOfGlyph(glyph))));
        return cost;
    }

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
    public ScalingInfo GetScalingInfo() => new ScalingInfo();
    public Exception? ChargeReleaseFailure { get; set; }
    public void SetChargeInput(string source, bool holding) =>
        HoldingCharge = !holding && ChargeReleaseFailure is { } failure ? throw failure : holding;

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
    public SpellRecipeSO? Reference { get => reference; set => reference = value; }
    public bool NativeAttuning { get; set; }
    public bool NativeHasEnoughResources { get; set; } = true;
    public ResourceCostList DrainCost { get; set; } = new ResourceCostList();
    public int CanRemoveCalls { get; private set; }

}
