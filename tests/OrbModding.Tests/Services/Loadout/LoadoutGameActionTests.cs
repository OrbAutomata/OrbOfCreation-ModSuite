using System;
using System.Linq;
using OrbAutomata;
using OrbModding.Common.Runtime.World;
using OrbModding.Tests.Services.TestSupport;
using Xunit;

namespace OrbModding.Tests.Services.Loadout;

public sealed class LoadoutGameActionTests : IDisposable
{
    private const long Epoch = 131;

    public LoadoutGameActionTests()
    {
        IdScriptableObject.RuntimeLookup.Clear();
        EquipmentSO.All.Clear();
        SpellRecipeSO.All.Clear();
        SpellManager.instance = new SpellManager();
        EquipmentManager.instance = new EquipmentManager();
        AlchemyManager.instance = new AlchemyManager();
        LoadoutManager.instance = new LoadoutManager();
        LoadoutManager.instance.activeEquipment.Maximum = 4;
        LoadoutManager.instance.activeAlchemy.Maximum = 4;
        LoadoutManager.instance.activeSpells.Maximum = 4;
        EntityIdentityCatalogPublication.Publish(EntityIdentityCatalogSnapshot.Unbound(Epoch));
    }

    [Fact]
    public void SelectUsesTheWholeNativeSwitchAndVerifiesSelectedIdentity()
    {
        var current = Player("Current", selected: true);
        var target = Player("Adventure");
        LoadoutManager.instance.playerLoadouts.value.Add(current);
        LoadoutManager.instance.playerLoadouts.value.Add(target);
        using var boundary = Boundary();

        var result = Submit(boundary, target.GetGuid(), LoadoutActionKind.Select);

        Assert.True(result.Verified, result.Reason);
        Assert.False(current.IsSelected());
        Assert.True(target.IsSelected());
        Assert.Equal(1, LoadoutManager.instance.SetLoadoutCalls);
    }

    [Fact]
    public void SelectAcceptsAnAuthoredSavedCoreThatIsNotAnOwnedConstructionChoice()
    {
        var current = Player("Current", selected: true);
        var target = Player("Legacy elemental");
        var glyph = new GlyphSO
        {
            DisplayName = "Psionic",
            level = 0,
            discovered = false,
            NativeAvailable = false,
            maxUsages = new ValueModifierRecord(new BigDouble(1)),
        };
        glyph.SetGuid(Guid.NewGuid());
        var recipe = new SpellRecipeSO { discovered = true };
        recipe.uuid = Guid.NewGuid().ToString("D");
        recipe.coreRecipe.Add(glyph);
        target.spells.Add(new Spell(recipe));
        IdScriptableObject.RuntimeLookup[glyph.GetGuid()] = glyph;
        IdScriptableObject.RuntimeLookup[recipe.GetGuid()] = recipe;
        SpellRecipeSO.All.Add(recipe);
        LoadoutManager.instance.playerLoadouts.value.Add(current);
        LoadoutManager.instance.playerLoadouts.value.Add(target);
        using var boundary = Boundary();

        var result = Submit(boundary, target.GetGuid(), LoadoutActionKind.Select);

        Assert.True(result.Verified, result.Reason);
        Assert.True(target.IsSelected());
    }

    [Fact]
    public void SelectAdmitsASavedLoadoutTheGameItselfNeverInspects()
    {
        var current = Player("Current", selected: true);
        var target = Player("Overloaded");
        var glyph = new GlyphSO
        {
            DisplayName = "Psionic",
            level = 0,
            discovered = false,
            NativeAvailable = false,
            maxUsages = new ValueModifierRecord(new BigDouble(1)),
        };
        glyph.SetGuid(Guid.NewGuid());
        var recipe = new SpellRecipeSO { discovered = true };
        recipe.uuid = Guid.NewGuid().ToString("D");
        recipe.coreRecipe.Add(glyph);
        IdScriptableObject.RuntimeLookup[glyph.GetGuid()] = glyph;
        IdScriptableObject.RuntimeLookup[recipe.GetGuid()] = recipe;
        SpellRecipeSO.All.Add(recipe);
        for (var index = 0; index <= LoadoutManager.instance.activeSpells.Maximum; index++)
            target.spells.Add(new Spell(recipe));
        LoadoutManager.instance.playerLoadouts.value.Add(current);
        LoadoutManager.instance.playerLoadouts.value.Add(target);
        using var boundary = Boundary();

        var result = Submit(boundary, target.GetGuid(), LoadoutActionKind.Select);

        Assert.True(result.Verified, result.Reason);
        Assert.True(target.IsSelected());
    }

    [Fact]
    public void SelectNoOpFailsTheSingleGameWrittenSentinel()
    {
        var current = Player("Current", selected: true);
        var target = Player("Adventure");
        LoadoutManager.instance.playerLoadouts.value.Add(current);
        LoadoutManager.instance.playerLoadouts.value.Add(target);
        LoadoutManager.instance.SuppressSetLoadout = true;
        using var boundary = Boundary();

        var result = Submit(boundary, target.GetGuid(), LoadoutActionKind.Select);

        Assert.Equal(LoadoutPreflight.VerificationFailed, result.Preflight);
        Assert.False(target.IsSelected());
    }

    [Fact]
    public void ActiveLoadoutSectionAndLabelControlsMirrorTheVisibleEditor()
    {
        var target = Player("Current", selected: true);
        LoadoutManager.instance.playerLoadouts.value.Add(target);
        using var boundary = Boundary();

        var equipment = Submit(boundary, target.GetGuid(),
            LoadoutActionKind.SetEquipmentSection, enabled: true);
        var renamed = Submit(boundary, target.GetGuid(),
            LoadoutActionKind.Rename, name: "Boss setup");
        var icon = Submit(boundary, target.GetGuid(), LoadoutActionKind.NextIcon);
        var color = Submit(boundary, target.GetGuid(), LoadoutActionKind.NextColor);

        Assert.True(equipment.Verified, equipment.Reason);
        Assert.True(renamed.Verified, renamed.Reason);
        Assert.True(icon.Verified, icon.Reason);
        Assert.True(color.Verified, color.Reason);
        Assert.True(target.HasEquipment());
        Assert.Equal("Boss setup", target.GetName());
        Assert.Equal(1, target.GetLabel().GetIconIndex());
        Assert.Equal(1, target.GetLabel().GetColorIndex());
        Assert.Equal(1, LoadoutManager.instance.SaveActiveCalls);
    }

    [Fact]
    public void EquipmentSnapshotSaveLoadAndClearUseSlotStateAsTheSentinel()
    {
        var equipment = Equipment("Ward", 2);
        LoadoutManager.instance.activeEquipment.Stack(equipment, 2);
        equipment.Equip(2);
        var owner = new EquipmentSnapshotListVariable();
        owner.SetGuid(Guid.NewGuid());
        owner.value.Add(new EquipmentSnapshot());
        LoadoutManager.instance.equipmentLoadouts = owner;
        using var boundary = Boundary();

        var saved = Submit(boundary, owner.GetGuid(), LoadoutActionKind.SnapshotSave, slot: 0);
        LoadoutManager.instance.activeEquipment.SetStack(
            new Stacked.StackedIdRecord<EquipmentSO>());
        var loaded = Submit(boundary, owner.GetGuid(), LoadoutActionKind.SnapshotLoad, slot: 0);
        var cleared = Submit(boundary, owner.GetGuid(), LoadoutActionKind.SnapshotClear, slot: 0);

        Assert.True(saved.Verified, saved.Reason);
        Assert.True(loaded.Verified, loaded.Reason);
        Assert.Equal(2, LoadoutManager.instance.activeEquipment.GetStacks(equipment));
        Assert.True(cleared.Verified, cleared.Reason);
        Assert.True(owner.value[0].IsEmpty());
    }

    [Fact]
    public void SnapshotSaveRefusesAnOccupiedSlotWithoutOverwritingIt()
    {
        var equipment = Equipment("Ward", 1);
        var owner = new EquipmentSnapshotListVariable();
        owner.SetGuid(Guid.NewGuid());
        var slot = new EquipmentSnapshot();
        var existing = new Stacked.StackedIdRecord<EquipmentSO>();
        existing.Set(equipment, 1);
        slot.SaveSnapshot(existing);
        owner.value.Add(slot);
        LoadoutManager.instance.equipmentLoadouts = owner;
        using var boundary = Boundary();

        var result = Submit(boundary, owner.GetGuid(), LoadoutActionKind.SnapshotSave, slot: 0);

        Assert.Equal(LoadoutPreflight.SlotOccupied, result.Preflight);
        Assert.Equal(1, slot.GetRecord().GetQuantity(equipment));
    }

    [Fact]
    public void SnapshotSaveRefusesAnEmptyActiveSectionBeforeAnyNativeWrite()
    {
        var owner = new EquipmentSnapshotListVariable();
        owner.SetGuid(Guid.NewGuid());
        var slot = new EquipmentSnapshot();
        owner.value.Add(slot);
        LoadoutManager.instance.equipmentLoadouts = owner;
        using var boundary = Boundary();

        var result = Submit(boundary, owner.GetGuid(), LoadoutActionKind.SnapshotSave, slot: 0);

        Assert.Equal(LoadoutPreflight.ActiveSectionEmpty, result.Preflight);
        Assert.Contains("nothing staged", result.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, result.CallOutcome.MutationAttempts);
        Assert.True(slot.IsEmpty());
    }

    [Fact]
    public void SnapshotActionNamesAZeroSlotSaveInsteadOfInventingAValidRange()
    {
        var owner = new EquipmentSnapshotListVariable();
        owner.SetGuid(Guid.NewGuid());
        LoadoutManager.instance.equipmentLoadouts = owner;
        using var boundary = Boundary();

        var result = Submit(boundary, owner.GetGuid(), LoadoutActionKind.SnapshotSave, slot: 0);

        Assert.Equal(LoadoutPreflight.SlotOutOfRange, result.Preflight);
        Assert.Equal("This save owns zero snapshot slots.", result.Reason);
        Assert.Equal(0, result.CallOutcome.MutationAttempts);
    }

    [Fact]
    public void OffThreadSubmissionRefusesBeforeNativeState()
    {
        var target = Player("Current", selected: true);
        LoadoutManager.instance.playerLoadouts.value.Add(target);
        using var boundary = Boundary();

        var result = ForeignThread.Run(() =>
            Submit(boundary, target.GetGuid(), LoadoutActionKind.NextIcon));

        Assert.Equal(LoadoutPreflight.WrongThread, result.Preflight);
        Assert.Equal(0, target.GetLabel().GetIconIndex());
    }

    [Fact]
    public void EveryMissingMemberDisablesTheCompleteLifecycleBindingSet()
    {
        foreach (var missing in LoadoutNativeBindings.ContractIds)
        {
            using var boundary = Boundary(id => id != missing);
            Assert.False(boundary.BindingsAvailable);
            Assert.Contains(missing, boundary.BindingFailure, StringComparison.Ordinal);
        }
    }

    private static LoadoutGameAction Boundary(Func<string, bool>? include = null)
    {
        var resolve = new Func<string, Type?>(name => typeof(LoadoutManager).Assembly.GetTypes()
            .FirstOrDefault(type => type.Name == name || type.FullName == name));
        var equipment = new EquipmentLoadoutGameAction(() => Epoch, static () => true,
            static () => "Equipment ownership unavailable", resolve);
        var alchemy = new AlchemyLoadoutGameAction(() => Epoch, static () => true,
            static () => "Alchemy ownership unavailable", resolve);
        var result = new LoadoutGameAction(() => Epoch, static () => true,
            static () => "loadout ownership unavailable", equipment, alchemy,
            resolve, include);
        if (include is null) Assert.True(result.BindingsAvailable, result.BindingFailure);
        return result;
    }

    private static LoadoutSubmission Submit(LoadoutGameAction boundary, Guid id,
        LoadoutActionKind kind, int slot = -1, bool enabled = false, string name = "")
    {
        var action = new LoadoutAction(kind, id, slot, enabled, name, Epoch);
        return boundary.Submit(in action);
    }

    private static PlayerLoadout Player(string name, bool selected = false) =>
        new(Guid.NewGuid(), name) { isSelected = selected };

    private static EquipmentSO Equipment(string name, int maximum)
    {
        var type = new EquipmentTypeSO
        {
            maxTypeSlots = new ValueModifierRecord(new BigDouble(maximum)),
        };
        type.SetGuid(Guid.NewGuid());
        var item = new EquipmentSO
        {
            name = name,
            isCreated = true,
            NativeMaximumStacks = maximum,
            equipmentType = type,
        };
        item.uuid = Guid.NewGuid().ToString("D");
        EquipmentSO.All.Add(item);
        IdScriptableObject.RuntimeLookup[item.GetGuid()] = item;
        return item;
    }

    public void Dispose()
    {
        IdScriptableObject.RuntimeLookup.Clear();
        EquipmentSO.All.Clear();
        SpellRecipeSO.All.Clear();
        SpellManager.instance = null;
        EquipmentManager.instance = new EquipmentManager();
        AlchemyManager.instance = null;
        LoadoutManager.instance = new LoadoutManager();
        EntityIdentityCatalogPublication.Publish(EntityIdentityCatalogSnapshot.Unbound(Epoch));
    }
}
