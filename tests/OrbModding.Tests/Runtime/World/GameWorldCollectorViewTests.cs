using System;
using System.Linq;
using Xunit;
using OrbModding.Common.Runtime.World;

namespace OrbModding.Tests.Runtime.World;

public sealed class GameWorldCollectorViewTests : GameWorldCollectorTestBase
{
    [Fact]
    public void AViewsComposedAvailabilityDiscoveryTreeAndTabsArePublished()
    {
        var unlocked = Guid.NewGuid();
        var locked = Guid.NewGuid();
        var tree = new DiscoveryTreeSO();
        var lockedView = new ViewSO
        {
            Identity = locked,
            active = false,
            alwaysActive = false,
        };
        var tabs = new ViewListVariable();
        tabs.value.Add(lockedView);
        ViewSO.All.Add(new ViewSO
        {
            Identity = unlocked,
            active = true,
            available = true,
            discoveryTree = tree,
            subViews = tabs,
        });
        ViewSO.All.Add(lockedView);

        var collector = Collector();
        collector.Collect();
        var world = collector.Build();

        Assert.True(WorldLookup.TryFind(world.Views, unlocked, out var unlockedView));
        Assert.True(unlockedView.Available);
        Assert.Equal(tree.Identity, unlockedView.DiscoveryTreeId);
        Assert.Equal(locked, Assert.Single(unlockedView.SubViewIds.AsSpan().ToArray()));
        Assert.True(WorldLookup.TryFind(world.Views, locked, out var lockedRow));
        Assert.False(lockedRow.Available);
        Assert.Equal(Guid.Empty, lockedRow.DiscoveryTreeId);
        Assert.Equal(0, lockedRow.SubViewIds.Count);
    }

    [Fact]
    public void TheGlobalVariableRegistriesAreCollectedAsRegistries()
    {
        // Player and GlobalVariables expose well over a hundred accessors between them, and every one
        // is a lookup into one of these lists. Walking the lists collects the lot without the
        // suite declaring — and then having to keep declaring — a contract per accessor.
        var multiBuy = Guid.NewGuid();
        var costScaling = Guid.NewGuid();
        var offlineProgress = Guid.NewGuid();

        IntVariable.All.Add(new IntVariable { Identity = multiBuy, value = new ValueModifierRecord(3d) });
        DoubleVariable.All.Add(new DoubleVariable
        {
            Identity = costScaling,
            value = new ValueModifierRecord(150d),
            isPercentVariable = true,
        });
        BoolVariable.All.Add(new BoolVariable { Identity = offlineProgress, value = true });

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        Assert.True(report.IsComplete, report.Describe());

        // The two number registries stay separate tables, because the game keeps them separate and a
        // caller asking for a count wants the one the game converts to an integer.
        Assert.True(WorldLookup.TryFind(world.IntVariables, multiBuy, out var buy));
        Assert.Equal(3d, buy.Value.ToDouble());
        Assert.False(buy.IsPercent);
        Assert.False(WorldLookup.TryFind(world.DoubleVariables, multiBuy, out _));

        Assert.True(WorldLookup.TryFind(world.DoubleVariables, costScaling, out var scaling));
        Assert.Equal(150d, scaling.Value.ToDouble());

        // Not presentation: a percent variable holds 100 for parity, so a consumer reading one as a
        // plain number is out by two orders of magnitude.
        Assert.True(scaling.IsPercent);

        Assert.True(WorldLookup.TryFind(world.BoolVariables, offlineProgress, out var flag));
        Assert.True(flag.Value);
    }

    [Fact]
    public void TheModifierRegistryTravelsAsArithmeticRatherThanAsAReference()
    {
        // A structure's costPerQuantity does not hold a modifier, it holds a reference to one of
        // these. Collecting the registry is what lets an entity row carry an identity instead of a
        // copy, and lets one modifier shared by twenty structures be read once.
        var perQuantity = Guid.NewGuid();
        ValueModifierVariable.All.Add(new ValueModifierVariable
        {
            Identity = perQuantity,
            value = new ValueModifier(ValueModifier.ValueModifierType.MultiStacking, 1.15d, order: 2),
        });

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        Assert.True(report.IsComplete, report.Describe());
        Assert.True(WorldLookup.TryFind(world.ModifierVariables, perQuantity, out var row));

        // The enum travels as its integer, for the reason every other game enum does.
        Assert.Equal((int)ValueModifier.ValueModifierType.MultiStacking, row.ModifierType);
        Assert.Equal(1.15d, row.Amount.ToDouble(), 10);

        // Order is not decoration: modifiers sharing an order merge with each other before any of
        // them is applied, so dropping it changes the arithmetic rather than the presentation.
        Assert.Equal(2, row.Order);
    }

    [Fact]
    public void TheStatisticGlossaryTravelsAsTheWordsTheScreenPrints()
    {
        var cost = Guid.NewGuid();
        AttributeSO.All.Add(new AttributeSO
        {
            Identity = cost,
            displayTypeRef = new DisplayTypeSO.Reference
            {
                displayType = new DisplayTypeSO { displayName = "Statistic" },
            },
            globalDefinition = "Cost",
            isPercent = false,
            description = "The amount of resources a component costs to cast, use or purchase.",
        });

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        Assert.True(report.IsComplete, report.Describe());
        Assert.True(WorldLookup.TryFind(world.Statistics, cost, out var row));

        // Two references deep: the record holds a reference that holds the display type, and the
        // word is that type's display name rather than the colour-tagged string ToType() builds.
        Assert.Equal("Statistic", row.DisplayType);
        Assert.False(row.IsPercent);
        Assert.Equal(
            "The amount of resources a component costs to cast, use or purchase.",
            row.Description);

        // The join key effect scripts name this statistic by. Collected because the next reader of
        // the world needs it; kept off the wire because no screen prints it.
        Assert.Equal("Cost", row.GlobalDefinition);
    }

    [Fact]
    public void AStatisticThatNamesNoDisplayTypeStillTravels()
    {
        // One shipped record — Starting Level — references no display type at all. The chain yields
        // the member default rather than dropping the row or inventing a fourth word.
        var starting = Guid.NewGuid();
        AttributeSO.All.Add(new AttributeSO
        {
            Identity = starting,
            displayTypeRef = new DisplayTypeSO.Reference { displayType = null! },
            globalDefinition = "CraftingStartingLevel",
            description = "The level this component starts at.",
        });

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        Assert.True(report.IsComplete, report.Describe());
        Assert.True(WorldLookup.TryFind(world.Statistics, starting, out var row));
        Assert.Equal(string.Empty, row.DisplayType);
        Assert.Equal("The level this component starts at.", row.Description);
    }

    [Fact]
    public void AStatisticTheGameAuthorsNoSentenceForIsStillARow()
    {
        // Eight of the 211 carry no description. An absent sentence is the absence of one, not a
        // reason to drop the word the screen still prints.
        var plumbing = Guid.NewGuid();
        AttributeSO.All.Add(new AttributeSO
        {
            Identity = plumbing,
            displayTypeRef = new DisplayTypeSO.Reference
            {
                displayType = new DisplayTypeSO { displayName = "Information" },
            },
            globalDefinition = "Tooltip:ChallengeActive",
            description = string.Empty,
        });

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        Assert.True(report.IsComplete, report.Describe());
        Assert.True(WorldLookup.TryFind(world.Statistics, plumbing, out var row));
        Assert.Equal("Information", row.DisplayType);
        Assert.Equal(string.Empty, row.Description);
    }

    /// <summary>
    /// The combat glossary travels as ten vocabularies rather than one, and each carries the fields
    /// its own screen reads. One row per registry here, because the failure this guards is a binder
    /// that binds nothing and reports a clean empty category.
    /// </summary>
    [Fact]
    public void TheRitualLayersGlossariesTravelAsTheirOwnVocabularies()
    {
        var burning = Guid.NewGuid();
        var fire = Guid.NewGuid();
        var strength = Guid.NewGuid();
        var berserk = Guid.NewGuid();
        var strike = Guid.NewGuid();

        CombatStatusSO.All.Add(new CombatStatusSO
        {
            Identity = burning,
            isBuff = false,
            maxDuration = 12d,
            stacksSeparately = true,
            resetDurationOnApplication = true,
            effectTimer = 1.5d,
            description = "Deals fire damage over time.",
        });
        var fireType = new DamageTypeSO
        {
            Identity = fire,
            damageReductionRate = 0.25d,
            ignoreEntrenched = true,
            description = "Burns through wards.",
        };
        DamageTypeSO.All.Add(fireType);
        CharacterAttributeSO.All.Add(new CharacterAttributeSO
        {
            Identity = strength,
            damageType = fireType,
            description = "How hard this creature hits.",
        });
        CharacterModifierSO.All.Add(new CharacterModifierSO
        {
            Identity = berserk,
            weightChance = 0.4f,
            description = "Attacks faster and defends worse.",
        });
        CharacterActionSO.All.Add(new CharacterActionSO
        {
            Identity = strike,
            prepTime = 2d,
            actionTime = 0.5d,
            speedMod = 1.25d,
            description = "A single heavy blow.",
        });

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        Assert.True(report.IsComplete, report.Describe());

        Assert.True(WorldLookup.TryFind(world.StatusEffects, burning, out var status));
        Assert.False(status.IsBuff);
        Assert.Equal(12d, status.MaxDuration);
        Assert.True(status.StacksSeparately);
        Assert.True(status.ResetDurationOnApplication);
        Assert.Equal(1.5d, status.EffectTimer);
        Assert.Equal("Deals fire damage over time.", status.Description);

        Assert.True(WorldLookup.TryFind(world.DamageTypes, fire, out var damage));
        Assert.Equal(0.25d, damage.DamageReductionRate);
        Assert.True(damage.IgnoreEntrenched);

        // The one edge inside the glossary: an attribute names the damage type it deals in, and
        // that type is a row of its own, so a reader can follow it rather than read a word twice.
        Assert.True(WorldLookup.TryFind(world.CharacterAttributes, strength, out var attribute));
        Assert.Equal(fire, attribute.DamageTypeId);

        Assert.True(WorldLookup.TryFind(world.CharacterModifiers, berserk, out var modifier));
        Assert.Equal(0.4d, modifier.WeightChance, 5);

        Assert.True(WorldLookup.TryFind(world.CharacterActions, strike, out var action));
        Assert.Equal(2d, action.PrepTime);
        Assert.Equal(0.5d, action.ActTime);
        Assert.Equal(1.25d, action.SpeedMod);
    }

    /// <summary>
    /// The four vocabularies whose whole published fact is the word and the sentence. A category
    /// that carries only identity is still a category: the alternative is that the game's own words
    /// for enchantments, glyph kinds, rune stones and display types stay unreadable.
    /// </summary>
    [Fact]
    public void TheAuthoredVocabulariesTravelAsTheirWordAndTheirSentence()
    {
        var enchantment = Guid.NewGuid();
        var stone = Guid.NewGuid();

        EnchantmentSO.All.Add(new EnchantmentSO
        {
            Identity = enchantment,
            description = "Adds fire damage to the weapon.",
        });
        RuneStoneSO.All.Add(new RuneStoneSO
        {
            Identity = stone,
            description = "Holds one time rune.",
        });

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        Assert.True(report.IsComplete, report.Describe());
        Assert.True(WorldLookup.TryFind(world.Enchantments, enchantment, out var enchantRow));
        Assert.Equal("Adds fire damage to the weapon.", enchantRow.Description);
        Assert.True(WorldLookup.TryFind(world.RuneStones, stone, out var stoneRow));
        Assert.Equal("Holds one time rune.", stoneRow.Description);
    }

    /// <summary>
    /// A stat group and the distribution it carries, in the direction the game stores it: the group
    /// names its targets, and each row says which record on that target it merges into and at what
    /// ratio. Nothing on the far side names the group, so no reverse edge is derived.
    /// </summary>
    [Fact]
    public void AStatGroupCarriesTheDistributionTheGameStoresOnIt()
    {
        var group = Guid.NewGuid();
        var target = Guid.NewGuid();
        var other = Guid.NewGuid();
        var targetStatistic = new AttributeSO { Identity = target };
        var otherStatistic = new AttributeSO { Identity = other };
        AttributeSO.All.Add(targetStatistic);
        AttributeSO.All.Add(otherStatistic);
        AttributeGroupSO.All.Add(new AttributeGroupSO
        {
            Identity = group,
            description = "Everything that raises agromancy power.",
            recordReferences =
            {
                new AttributeGroupSO.MergingRecordReference
                {
                    upgradeableObject = targetStatistic,
                    propertyType = "Power",
                    propertyIndex = 0,
                    ratio = 2d,
                    ratioExp = 1d,
                    orderAdjust = 0,
                },
                new AttributeGroupSO.MergingRecordReference
                {
                    upgradeableObject = otherStatistic,
                    propertyType = "Speed",
                    propertyIndex = 1,
                    ratio = 0.5d,
                    ratioExp = 2d,
                    orderAdjust = 3,
                },
            },
        });

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        Assert.True(report.IsComplete, report.Describe());
        Assert.True(WorldLookup.TryFind(world.AttributeGroups, group, out var groupRow));
        Assert.Equal("Everything that raises agromancy power.", groupRow.Description);

        Assert.True(WorldAttributeGroupMemberLookup.TryFindRange(
            world.AttributeGroupMembers, group, out var start, out var count));
        Assert.Equal(2, count);

        var members = world.AttributeGroupMembers.AsSpan().Slice(start, count).ToArray();
        Assert.Equal(new[] { 0, 1 }, members.Select(member => member.Ordinal).ToArray());
        Assert.Equal(new[] { target, other }, members.Select(member => member.TargetId).ToArray());
        Assert.Equal(
            new[] { "Power", "Speed" }, members.Select(member => member.Property).ToArray());
        Assert.Equal(new[] { 0, 1 }, members.Select(member => member.PropertyIndex).ToArray());
        Assert.Equal(new[] { 2d, 0.5d }, members.Select(member => member.Ratio).ToArray());
        Assert.Equal(new[] { 1d, 2d }, members.Select(member => member.RatioExp).ToArray());
        Assert.Equal(new[] { 0, 3 }, members.Select(member => member.OrderAdjust).ToArray());
    }

    /// <summary>
    /// A group the game authors no references on is still a heading. The Statistics tab prints its
    /// word, so the world publishes its row, and the member table simply holds nothing for it.
    /// </summary>
    [Fact]
    public void AStatGroupThatDistributesNothingIsStillAHeading()
    {
        var NativeEmpty = Guid.NewGuid();
        AttributeGroupSO.All.Add(new AttributeGroupSO
        {
            Identity = NativeEmpty,
            description = "A heading with nothing under it yet.",
        });

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        Assert.True(report.IsComplete, report.Describe());
        Assert.True(WorldLookup.TryFind(world.AttributeGroups, NativeEmpty, out _));
        Assert.False(WorldAttributeGroupMemberLookup.TryFindRange(
            world.AttributeGroupMembers, NativeEmpty, out _, out var count));
        Assert.Equal(0, count);
    }

    [Fact]
    public void AGlyphPublishesOnlyTheFactorSlotsTheGameWouldPrint()
    {
        // Quick, verbatim off the pinned build: two of fifteen slots filled, both MultiStacking,
        // authored 0.15 and -0.30, which the game's own ConvertToReal turns into the 1.15 and 0.700
        // its tooltip prints. Thirteen empty slots publish nothing.
        var cost = Guid.NewGuid();
        var cooldown = Guid.NewGuid();
        AttributeSO.All.Add(new AttributeSO { Identity = cost, globalDefinition = "Cost" });
        AttributeSO.All.Add(new AttributeSO
        {
            Identity = cooldown,
            globalDefinition = "Cooldown",
        });

        var quick = Guid.NewGuid();
        GlyphSO.All.Add(new GlyphSO
        {
            Identity = quick,
            spellCost = new ValueModifier(ValueModifier.ValueModifierType.MultiStacking, 1.15d, 0),
            spellCooldown = new ValueModifier(ValueModifier.ValueModifierType.MultiStacking, 0.7d, 0),
        });

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        Assert.True(report.IsComplete, report.Describe());
        Assert.True(
            WorldGlyphFactorLookup.TryFindRange(world.GlyphEffects, quick, out var start,
                out var count));
        Assert.Equal(2, count);

        // Slot order is the game's, not the alphabet's: spellCost precedes spellCooldown in the one
        // method that prints them.
        var first = world.GlyphEffects[start];
        Assert.Equal("spellCost", first.Property);
        Assert.Equal(cost, first.StatisticId);
        Assert.Equal((int)ValueModifier.ValueModifierType.MultiStacking, first.ModifierType);
        Assert.Equal(1.15d, first.Amount.ToDouble(), 6);
        Assert.Equal(0, first.Order);

        var second = world.GlyphEffects[start + 1];
        Assert.Equal("spellCooldown", second.Property);
        Assert.Equal(cooldown, second.StatisticId);
        Assert.Equal((int)ValueModifier.ValueModifierType.MultiStacking, second.ModifierType);
        Assert.Equal(0.7d, second.Amount.ToDouble(), 6);
    }

    /// <summary>
    /// The four slots the game prints against a variable it reads off the player carry the edge to
    /// that variable, and the one slot that names neither carries no edge at all.
    /// </summary>
    /// <remarks>
    /// The variable is a serialized field on the singleton rather than an asset in a registry, so
    /// the accessor the game reads it through is the only way to learn which variable a slot means.
    /// <c>creationCostMod</c> is applied to a resource cost list, which is not an entity this suite
    /// publishes, so it stays blank in both columns rather than borrowing one.
    /// </remarks>
    [Fact]
    public void AGlyphFactorPrintedAgainstAPlayerVariableCarriesThatEdge()
    {
        var glyph = Guid.NewGuid();
        GlyphSO.All.Add(new GlyphSO
        {
            Identity = glyph,
            spellCriticalRating = new ValueModifier(ValueModifier.ValueModifierType.MultiDiminishing, 0.17d, 0),
            creationCostMod = new ValueModifier(ValueModifier.ValueModifierType.MultiStacking, 4d, 0),
        });

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        Assert.True(report.IsComplete, report.Describe());
        Assert.True(
            WorldGlyphFactorLookup.TryFindRange(world.GlyphEffects, glyph, out var start,
                out var count));
        Assert.Equal(2, count);
        Assert.Equal("spellCriticalRating", world.GlyphEffects[start].Property);
        Assert.Equal(Guid.Empty, world.GlyphEffects[start].StatisticId);
        Assert.Equal(
            Player.Current.spellCriticalCastRating.Identity,
            world.GlyphEffects[start].VariableId);

        Assert.Equal("creationCostMod", world.GlyphEffects[start + 1].Property);
        Assert.Equal(Guid.Empty, world.GlyphEffects[start + 1].StatisticId);
        Assert.Equal(Guid.Empty, world.GlyphEffects[start + 1].VariableId);
    }

    /// <summary>
    /// An upgrade's <c>permanentEffects</c> reach the wire as one tuple column, in the authored
    /// order of the three lists the game stores them in.
    /// </summary>
    /// <remarks>
    /// The worked case is RaiseMaxDruidryLevel's, whose tooltip prints <c>+1 Max Druidry Lv</c>,
    /// <c>x1.02 All Plot Yield</c> and <c>x1.75 All Plot Recovery Size</c>. A number-variable tuple
    /// names no property because the variable is the whole of what moves; an upgradeable-object
    /// tuple's property is the authored <c>propertyType</c> string; a resource tuple's property is
    /// its <c>ModifiableType</c> member name, read off the game's own enum rather than a table
    /// copied here.
    /// </remarks>
    [Fact]
    public void AnUpgradesPermanentEffectsPublishOneTupleColumnFromThreeVocabularies()
    {
        var maxDruidryLevel = new NumberVariable();
        var allPlot = new ResourceSO();
        var plotCapacity = new ResourceSO { Identity = Guid.NewGuid() };
        ResourceSO.All.Add(plotCapacity);

        var upgrade = Guid.NewGuid();
        var raise = new UpgradeSO { Identity = upgrade, maxLevel = -1 };
        raise.permanentEffects.numberVariableEffects.Add(new TupleMod<NumberVariable>
        {
            item = maxDruidryLevel,
            modifier = new ValueModifier(ValueModifier.ValueModifierType.Raw, 1d, 0),
        });
        raise.permanentEffects.upgradeableObjectEffects.Add(new UpgradeableObject.UpgradeEffectModifier
        {
            upgradeableObject = allPlot,
            propertyType = "Yield",
            modifier = new ValueModifier(ValueModifier.ValueModifierType.MultiStacking, 1.02d, 0),
        });
        raise.permanentEffects.resourceEffects.Add(new ResourceSO.PersistentEffect
        {
            resource = plotCapacity,
            upgradeType = ResourceSO.ModifiableType.MaxQuantity,
            modifier = new ValueModifier(ValueModifier.ValueModifierType.Raw, 5d, 0),
        });
        UpgradeSO.All.Add(raise);

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        Assert.True(report.IsComplete, report.Describe());
        Assert.True(
            WorldLevelEffectLookup.TryFindRange(
                world.LevelEffects, upgrade, out var start, out var count));
        Assert.Equal(3, count);

        var variable = world.LevelEffects[start];
        Assert.Equal(string.Empty, variable.Property);
        Assert.Equal(maxDruidryLevel.Identity, variable.TargetId);
        Assert.Equal((int)ValueModifier.ValueModifierType.Raw, variable.ModifierType);
        Assert.Equal(1d, variable.Amount.ToDouble(), 6);
        Assert.Equal(0, variable.Order);

        var property = world.LevelEffects[start + 1];
        Assert.Equal("Yield", property.Property);
        Assert.Equal(allPlot.Identity, property.TargetId);
        Assert.Equal((int)ValueModifier.ValueModifierType.MultiStacking, property.ModifierType);
        Assert.Equal(1.02d, property.Amount.ToDouble(), 6);

        var resource = world.LevelEffects[start + 2];
        Assert.Equal("MaxQuantity", resource.Property);
        Assert.Equal(plotCapacity.Identity, resource.TargetId);
        Assert.Equal(5d, resource.Amount.ToDouble(), 6);
    }

    /// <summary>
    /// Every holder that authors effect blocks reads the same scripts through the same reader, and a
    /// block whose scripts apply no modifier publishes no row rather than an empty one.
    /// </summary>
    [Fact]
    public void EveryBlockHolderPublishesItsAuthoredTuplesAndNothingElse()
    {
        var stockRestore = new ResourceSO();
        var glyph = Guid.NewGuid();
        var glyphAsset = new GlyphSO { Identity = glyph };
        glyphAsset.levelingEffects.Add(Block(new UpgradeableObject.UpgradeEffectModifier
        {
            upgradeableObject = stockRestore,
            propertyType = "Value",
            modifier = new ValueModifier(ValueModifier.ValueModifierType.MultiDiminishing, 1.15d, 0),
        }));
        GlyphSO.All.Add(glyphAsset);

        var equipmentType = Guid.NewGuid();
        var amulet = new EquipmentTypeSO { Identity = equipmentType };
        amulet.levelEffects.Add(Block(new UpgradeableObject.UpgradeEffectModifier
        {
            upgradeableObject = new ResourceSO(),
            propertyType = "EffectLevel",
            modifier = new ValueModifier(ValueModifier.ValueModifierType.Raw, 5d, 0),
        }));
        EquipmentTypeSO.All.Add(amulet);

        var plotCapacity = new ResourceSO { Identity = Guid.NewGuid() };
        ResourceSO.All.Add(plotCapacity);
        var resourceType = Guid.NewGuid();
        var blooming = new ResourceTypeSO { Identity = resourceType };
        blooming.levelEffects.Add(Block(new ResourceSO.PersistentEffect
        {
            resource = plotCapacity,
            upgradeType = ResourceSO.ModifiableType.MaxQuantity,
            modifier = new ValueModifier(ValueModifier.ValueModifierType.Raw, 5d, 0),
        }));
        ResourceTypeSO.All.Add(blooming);

        var castTime = new NumberVariable();
        var spellType = Guid.NewGuid();
        var arcane = new SpellTypeSO { Identity = spellType };
        arcane.perLevelEffects.Add(Block(new NumberVariable.PersistentEffect
        {
            numberVariable = castTime,
            modifier = new ValueModifier(ValueModifier.ValueModifierType.MultiDiminishing, 0.9d, 0),
        }));
        SpellTypeSO.All.Add(arcane);

        // A block whose script grants advancement experience rather than applying a modifier is
        // walked and publishes nothing, which is what the pinned build's twenty time-rune
        // level-up blocks all do.
        var rune = Guid.NewGuid();
        var runeAsset = new TimeRuneSO { Identity = rune };
        runeAsset.onLevelEffects.Add(new InstantEffectBlock { effectScripts = { new TreasurePoolSO.TreasurePoolInstantEffect() } });
        TimeRuneSO.All.Add(runeAsset);

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        Assert.True(report.IsComplete, report.Describe());

        Assert.True(
            WorldLevelEffectLookup.TryFindRange(
                world.LevelEffects, glyph, out var glyphStart, out var glyphCount));
        Assert.Equal(1, glyphCount);
        Assert.Equal("Value", world.LevelEffects[glyphStart].Property);
        Assert.Equal(stockRestore.Identity, world.LevelEffects[glyphStart].TargetId);
        Assert.Equal(1.15d, world.LevelEffects[glyphStart].Amount.ToDouble(), 6);

        Assert.True(
            WorldLevelEffectLookup.TryFindRange(
                world.LevelEffects, equipmentType, out var equipmentStart, out var equipmentCount));
        Assert.Equal(1, equipmentCount);
        Assert.Equal("EffectLevel", world.LevelEffects[equipmentStart].Property);

        // The other two script vocabularies, authored inside a block rather than in the upgrade's
        // deprecated container: a resource tuple keys on its ModifiableType member name, and a
        // number-variable tuple names no property at all.
        Assert.True(
            WorldLevelEffectLookup.TryFindRange(
                world.LevelEffects, resourceType, out var resourceStart, out var resourceCount));
        Assert.Equal(1, resourceCount);
        Assert.Equal("MaxQuantity", world.LevelEffects[resourceStart].Property);
        Assert.Equal(plotCapacity.Identity, world.LevelEffects[resourceStart].TargetId);

        Assert.True(
            WorldLevelEffectLookup.TryFindRange(
                world.LevelEffects, spellType, out var spellStart, out var spellCount));
        Assert.Equal(1, spellCount);
        Assert.Equal(string.Empty, world.LevelEffects[spellStart].Property);
        Assert.Equal(castTime.Identity, world.LevelEffects[spellStart].TargetId);
        Assert.Equal(0.9d, world.LevelEffects[spellStart].Amount.ToDouble(), 6);

        Assert.False(
            WorldLevelEffectLookup.TryFindRange(world.LevelEffects, rune, out _, out _));
    }

    /// <summary>
    /// A challenge's two lists say which one an effect came from, because its tooltip prints them
    /// under different headers; one owner's rows number on across its lists; and a structure's
    /// deprecated property lists publish like an upgrade's.
    /// </summary>
    [Fact]
    public void ChallengeSectionsAndStructurePropertiesPublishAsTheTooltipGroupsThem()
    {
        var discoveryOptions = new NumberVariable();
        var trial = new ChallengeSO();
        trial.challengeEffects.Add(Block(new NumberVariable.PersistentEffect
        {
            numberVariable = discoveryOptions,
            modifier = new ValueModifier(ValueModifier.ValueModifierType.Raw, -1d, 0),
        }));
        trial.completionRewards.Add(Block(new NumberVariable.PersistentEffect
        {
            numberVariable = discoveryOptions,
            modifier = new ValueModifier(ValueModifier.ValueModifierType.Raw, 2d, 0),
        }));
        ChallengeSO.All.Add(trial);

        var research = new ResearchSO();
        research.researchEffectBlocks.Add(Block(new NumberVariable.PersistentEffect
        {
            numberVariable = discoveryOptions,
            modifier = new ValueModifier(ValueModifier.ValueModifierType.Raw, 1d, 0),
        }));
        ResearchSO.All.Add(research);

        var field = new ResourceSO();
        var plot = new StructureSO { Identity = Guid.NewGuid() };
        var properties = new PersistentEffectDeprecated.Property();
        properties.upgradeableObjectEffects.Add(new UpgradeableObject.UpgradeEffectModifier
        {
            upgradeableObject = field,
            propertyType = "Quality",
            modifier = new ValueModifier(ValueModifier.ValueModifierType.Raw, 0.005d, 0),
        });
        plot.structureProperties.Add(properties);
        StructureSO.All.Add(plot);

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        Assert.True(report.IsComplete, report.Describe());

        Assert.True(
            WorldLevelEffectLookup.TryFindRange(
                world.LevelEffects, trial.Identity, out var trialStart, out var trialCount));
        Assert.Equal(2, trialCount);
        Assert.Equal(0, world.LevelEffects[trialStart].Ordinal);
        Assert.Equal("while", world.LevelEffects[trialStart].When);
        Assert.Equal(-1d, world.LevelEffects[trialStart].Amount.ToDouble(), 6);
        Assert.Equal(1, world.LevelEffects[trialStart + 1].Ordinal);
        Assert.Equal("reward", world.LevelEffects[trialStart + 1].When);

        Assert.True(
            WorldLevelEffectLookup.TryFindRange(
                world.LevelEffects, research.Identity, out var researchStart, out var researchCount));
        Assert.Equal(1, researchCount);
        Assert.Equal(discoveryOptions.Identity, world.LevelEffects[researchStart].TargetId);
        Assert.Equal(string.Empty, world.LevelEffects[researchStart].When);

        Assert.True(
            WorldLevelEffectLookup.TryFindRange(
                world.LevelEffects, plot.Identity, out var plotStart, out var plotCount));
        Assert.Equal(1, plotCount);
        Assert.Equal("Quality", world.LevelEffects[plotStart].Property);
        Assert.Equal(field.Identity, world.LevelEffects[plotStart].TargetId);
    }

    [Fact]
    public void TwoGlyphSlotsNamingOneStatisticStayTwoRows()
    {
        // spellCooldown and spellBaseCooldown are both printed under Cooldown and are different
        // factors. Without the slot on the row a reader meets one statistic twice and cannot say
        // which arithmetic belongs to which.
        var cooldown = Guid.NewGuid();
        AttributeSO.All.Add(new AttributeSO
        {
            Identity = cooldown,
            globalDefinition = "Cooldown",
        });

        var glyph = Guid.NewGuid();
        GlyphSO.All.Add(new GlyphSO
        {
            Identity = glyph,
            spellCooldown = new ValueModifier(ValueModifier.ValueModifierType.MultiStacking, 1.2d, 0),
            spellBaseCooldown = new ValueModifier(ValueModifier.ValueModifierType.Raw, -2d, -1),
        });

        var collector = Collector();
        collector.Collect();
        var world = collector.Build();

        Assert.True(
            WorldGlyphFactorLookup.TryFindRange(world.GlyphEffects, glyph, out var start,
                out var count));
        Assert.Equal(2, count);
        Assert.Equal("spellCooldown", world.GlyphEffects[start].Property);
        Assert.Equal("spellBaseCooldown", world.GlyphEffects[start + 1].Property);
        Assert.Equal(cooldown, world.GlyphEffects[start].StatisticId);
        Assert.Equal(cooldown, world.GlyphEffects[start + 1].StatisticId);

        // The second slot's order is the game's -1, which decides that it merges in a pass of its
        // own rather than with the multiplier above it.
        Assert.Equal(-1, world.GlyphEffects[start + 1].Order);
    }

    [Fact]
    public void AGlyphWithNoAuthoredFactorPublishesNoRow()
    {
        // Emptiness is the game's own predicate, not "the number is zero": a MultiStacking modifier
        // is empty at one, and a Raw one at zero. A glyph that fills nothing reaches the table not
        // at all rather than as fifteen rows of identity values.
        var glyph = Guid.NewGuid();
        GlyphSO.All.Add(new GlyphSO
        {
            Identity = glyph,
            spellPower = new ValueModifier(ValueModifier.ValueModifierType.MultiStacking, 1d, 0),
            spellCost = new ValueModifier(ValueModifier.ValueModifierType.Raw, 0d, 0),
        });

        var collector = Collector();
        collector.Collect();
        var world = collector.Build();

        Assert.False(
            WorldGlyphFactorLookup.TryFindRange(world.GlyphEffects, glyph, out _, out _));
    }

    /// <summary>
    /// An authored effect whose target throws unexpectedly faults the pass from each owner the level
    /// effects are walked on: an upgrade's permanent effects, a structure's per-rank properties and
    /// an effect block.
    /// </summary>
    [Theory]
    [InlineData("upgrade")]
    [InlineData("structure")]
    [InlineData("research")]
    public void AnUnexpectedThrowReadingALevelEffectFaultsThePass(string owner)
    {
        var failure = new NullReferenceException("effect target was destroyed");
        var effect = new UpgradeableObject.UpgradeEffectModifier
        {
            upgradeableObject = new ResourceSO { GetGuidFailure = failure },
            propertyType = "power",
            modifier = new ValueModifier(ValueModifier.ValueModifierType.Raw, 1d, 0),
        };
        switch (owner)
        {
            case "upgrade":
                var upgrade = new UpgradeSO();
                upgrade.permanentEffects.upgradeableObjectEffects.Add(effect);
                UpgradeSO.All.Add(upgrade);
                break;
            case "structure":
                var properties = new PersistentEffectDeprecated.Property();
                properties.upgradeableObjectEffects.Add(effect);
                var structure = new StructureSO();
                structure.structureProperties.Add(properties);
                StructureSO.All.Add(structure);
                break;
            default:
                var block = new PersistentEffectBlock();
                block.effectScripts.Add(effect);
                var research = new ResearchSO();
                research.researchEffectBlocks.Add(block);
                ResearchSO.All.Add(research);
                break;
        }

        var thrown = Assert.Throws<NullReferenceException>(() => Collector().Collect());

        Assert.Same(failure, thrown);
    }

    /// <summary>
    /// A stat group member whose target throws unexpectedly faults the pass rather than costing its
    /// group's rows.
    /// </summary>
    [Fact]
    public void AnUnexpectedThrowReadingAStatGroupMemberFaultsThePass()
    {
        var failure = new NullReferenceException("member statistic was destroyed");
        var group = new AttributeGroupSO();
        group.recordReferences.Add(new AttributeGroupSO.MergingRecordReference
        {
            upgradeableObject = new AttributeSO { GetGuidFailure = failure },
            propertyType = "power",
        });
        AttributeGroupSO.All.Add(group);

        var thrown = Assert.Throws<NullReferenceException>(() => Collector().Collect());

        Assert.Same(failure, thrown);
    }
}
