using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.World;

/// <summary>
/// The tables derived from structural rows alone, kept on the frame for as long as those rows are the
/// ones the collector last read.
/// </summary>
/// <remarks>
/// <para>
/// Structural rows are re-read only when a lifecycle epoch moves, so between reads the same rows would
/// derive to the same tables on every pass. Deriving them once per read keeps their copies and sorts
/// out of the worker's garbage. Every published world from one read shares these tables, which is safe
/// because a publication table is never written after it is created.
/// </para>
/// <para>
/// A table that joins a structural buffer to anything read every pass is not here: it is derived on
/// every pass in <see cref="GameWorldFrameDeriver"/>.
/// </para>
/// </remarks>
internal sealed class WorldAuthoredTables
{
    private WorldAuthoredTables(GameWorldCycleFrame frame)
    {
        StructuralGeneration = frame.StructuralGeneration;
        SpellRecipeAuthoring = WorldRelationTableDeriver.Build(
            frame.SpellRecipeAuthoring,
            static (left, right) => left.RecipeId.CompareTo(right.RecipeId));
        SpellAuthoredCosts = WorldRelationTableDeriver.Build(
            frame.SpellAuthoredCosts,
            static (left, right) =>
            {
                var recipe = left.RecipeId.CompareTo(right.RecipeId);
                if (recipe != 0) return recipe;
                var kind = ((int)left.Kind).CompareTo((int)right.Kind);
                return kind != 0 ? kind : left.Ordinal.CompareTo(right.Ordinal);
            });
        SpellRelations = WorldRelationTableDeriver.Build(
            frame.SpellRelations,
            static (left, right) =>
            {
                var recipe = left.RecipeId.CompareTo(right.RecipeId);
                if (recipe != 0) return recipe;
                var kind = ((int)left.Kind).CompareTo((int)right.Kind);
                return kind != 0 ? kind : left.Ordinal.CompareTo(right.Ordinal);
            });
        EntityKeywords = WorldRelationTableDeriver.Build(
            frame.EntityKeywords,
            static (left, right) =>
            {
                var owner = left.OwnerId.CompareTo(right.OwnerId);
                if (owner != 0) return owner;
                var source = ((int)left.Source).CompareTo((int)right.Source);
                return source != 0 ? source : left.Ordinal.CompareTo(right.Ordinal);
            });
        CraftingRecipeTypes =
            frame.CraftingRecipeTypes.Build(WorldIdentityDeriver<WorldCraftingRecipeType>.Shared);
        PurchaseViews = WorldPurchaseViewRelationDeriver.Build(
            frame.PurchaseViewRelations,
            frame.PurchaseViewRoutes);
        UpgradeListMemberships = WorldUpgradeListMembershipDeriver.Build(frame.UpgradeListMemberships);
        RecipeBookGlyphs = WorldRecipeBookGlyphDeriver.Build(frame.RecipeBookGlyphs);
        DiscoveryTreeBooks = WorldDiscoveryTreeBookDeriver.Build(frame.DiscoveryTreeBooks);
        ResourceBar = WorldResourceBarDeriver.Build(frame.ResourceBar);
        LevelEffects = WorldLevelEffectDeriver.Build(frame.LevelEffects);
        PlotAuthoring = WorldPlotAuthoringDeriver.Build(frame.PlotAuthoring);
        PlotPhaseDescriptors = WorldPlotPhaseDescriptorDeriver.Build(frame.PlotPhaseDescriptors);
        EffectBlocks = WorldEffectBlockDeriver.Build(frame.EffectBlocks);
        EntityRequirements = WorldEntityRequirementDeriver.Build(frame.EntityRequirements);
        RequirementListMembers = WorldRequirementListDeriver.Build(frame.RequirementLists);
    }

    internal long StructuralGeneration { get; }
    internal PublicationTable<WorldSpellRecipeAuthoring> SpellRecipeAuthoring { get; }
    internal PublicationTable<WorldSpellAuthoredCost> SpellAuthoredCosts { get; }
    internal PublicationTable<WorldSpellRelation> SpellRelations { get; }
    internal PublicationTable<WorldEntityKeyword> EntityKeywords { get; }
    internal PublicationTable<WorldCraftingRecipeType> CraftingRecipeTypes { get; }
    internal WorldPurchaseViewPublication PurchaseViews { get; }
    internal PublicationTable<WorldUpgradeListMembership> UpgradeListMemberships { get; }
    internal PublicationTable<WorldRecipeBookGlyph> RecipeBookGlyphs { get; }
    internal PublicationTable<WorldDiscoveryTreeBook> DiscoveryTreeBooks { get; }
    internal PublicationTable<WorldResourceBarEntry> ResourceBar { get; }
    internal PublicationTable<WorldLevelEffect> LevelEffects { get; }
    internal PublicationTable<WorldPlotAuthoring> PlotAuthoring { get; }
    internal PublicationTable<WorldPlotPhaseDescriptor> PlotPhaseDescriptors { get; }
    internal PublicationTable<WorldEffectBlock> EffectBlocks { get; }
    internal PublicationTable<WorldEntityRequirement> EntityRequirements { get; }
    internal PublicationTable<WorldRequirementListMember> RequirementListMembers { get; }

    /// <summary>The frame's tables, derived again only when its structural rows were read since.</summary>
    internal static WorldAuthoredTables Of(GameWorldCycleFrame frame)
    {
        if (frame.AuthoredTables is { } kept && kept.StructuralGeneration == frame.StructuralGeneration)
            return kept;
        var derived = new WorldAuthoredTables(frame);
        frame.AuthoredTables = derived;
        return derived;
    }
}
