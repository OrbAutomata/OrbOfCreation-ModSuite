using System;

namespace OrbAutomata;

internal readonly struct SpellWorkbenchPricePreviewRequest
{
    internal SpellWorkbenchPricePreviewRequest(
        Guid spellRecipeId,
        long lifecycleEpoch,
        SpellWorkbenchGlyphStack[] augmentGlyphs)
    {
        if (spellRecipeId == Guid.Empty)
            throw new ArgumentException("A spell recipe identity is required.", nameof(spellRecipeId));
        if (augmentGlyphs is null) throw new ArgumentNullException(nameof(augmentGlyphs));
        SpellRecipeId = spellRecipeId;
        LifecycleEpoch = lifecycleEpoch;
        AugmentGlyphs = new SpellWorkbenchGlyphStack[augmentGlyphs.Length];
        Array.Copy(augmentGlyphs, AugmentGlyphs, augmentGlyphs.Length);
    }

    internal Guid SpellRecipeId { get; }
    internal long LifecycleEpoch { get; }
    internal SpellWorkbenchGlyphStack[] AugmentGlyphs { get; }
}

internal readonly struct SpellWorkbenchPricePreviewCost
{
    internal SpellWorkbenchPricePreviewCost(Guid resourceId, BigDouble cost)
    {
        if (resourceId == Guid.Empty)
            throw new ArgumentException("A creation-cost resource identity is required.", nameof(resourceId));
        ResourceId = resourceId;
        Cost = cost;
    }

    internal Guid ResourceId { get; }
    internal BigDouble Cost { get; }
}

internal readonly struct SpellWorkbenchPricePreview
{
    private SpellWorkbenchPricePreview(
        SpellWorkbenchPreflight preflight,
        Guid recipeId,
        Guid resolvedRecipeId,
        SpellWorkbenchPricePreviewCost[] costs,
        bool affordable,
        Guid shortResourceId,
        string reason)
    {
        Preflight = preflight;
        RecipeId = recipeId;
        ResolvedRecipeId = resolvedRecipeId;
        Costs = costs ?? throw new ArgumentNullException(nameof(costs));
        Affordable = affordable;
        ShortResourceId = shortResourceId;
        Reason = reason ?? string.Empty;
    }

    internal SpellWorkbenchPreflight Preflight { get; }
    internal Guid RecipeId { get; }

    /// <summary>
    /// The spell the live glyph layout resolves to. It is the fact a price is worth nothing
    /// without: an empty augment layout prices an empty cost list, so <c>affordable</c> alone read
    /// as "this will work" on a call that could not have worked.
    /// </summary>
    internal Guid ResolvedRecipeId { get; }
    internal SpellWorkbenchPricePreviewCost[] Costs { get; }
    internal bool Affordable { get; }
    internal Guid ShortResourceId { get; }
    internal string Reason { get; }
    internal bool Available => Preflight == SpellWorkbenchPreflight.Proceeded;

    internal static SpellWorkbenchPricePreview Priced(
        Guid recipeId,
        Guid resolvedRecipeId,
        SpellWorkbenchPricePreviewCost[] costs,
        bool affordable,
        Guid shortResourceId) =>
        new(
            SpellWorkbenchPreflight.Proceeded,
            recipeId,
            resolvedRecipeId,
            costs,
            affordable,
            shortResourceId,
            string.Empty);

    internal static SpellWorkbenchPricePreview Refused(
        SpellWorkbenchPreflight preflight,
        string reason) =>
        new(preflight, Guid.Empty, Guid.Empty,
            Array.Empty<SpellWorkbenchPricePreviewCost>(), false,
            Guid.Empty, reason);
}
