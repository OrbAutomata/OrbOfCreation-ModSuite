using System;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal readonly struct SpellWorkbenchLoadPreviewRequest
{
    internal SpellWorkbenchLoadPreviewRequest(
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

/// <summary>One resource of the usage allocation a loaded spell holds while it is loaded.</summary>
internal readonly struct SpellWorkbenchUsageAllocation
{
    internal SpellWorkbenchUsageAllocation(Guid resourceId, BigDouble amount)
    {
        if (resourceId == Guid.Empty)
            throw new ArgumentException("A usage-allocation resource identity is required.", nameof(resourceId));
        ResourceId = resourceId;
        Amount = amount;
    }

    internal Guid ResourceId { get; }
    internal BigDouble Amount { get; }
}

/// <summary>
/// What loading this spell with this augment layout would hold, and whether the game would allow
/// the load at all.
/// </summary>
/// <remarks>
/// There is no price for putting a spell in a slot. The only budget the game weighs a load against
/// is the usage allocation the loaded spell holds — so an available preview means the row's own
/// button is pressable and these are the resources it will draw against, and every refusal here is
/// a refusal the add on identical arguments makes too. A preview that replaces a loaded copy holds
/// once that copy is removed.
/// </remarks>
internal readonly struct SpellWorkbenchLoadPreview
{
    private SpellWorkbenchLoadPreview(
        ServiceActionResultCode code,
        Guid recipeId,
        SpellWorkbenchUsageAllocation[] usage,
        string replaces,
        string reason)
    {
        Code = code;
        RecipeId = recipeId;
        Usage = usage ?? throw new ArgumentNullException(nameof(usage));
        Replaces = replaces ?? string.Empty;
        Reason = reason ?? string.Empty;
    }

    internal ServiceActionResultCode Code { get; }
    internal Guid RecipeId { get; }
    internal SpellWorkbenchUsageAllocation[] Usage { get; }

    /// <summary>
    /// The loaded copy this preview replaces and the removal it holds after, or empty when it
    /// prices another copy.
    /// </summary>
    internal string Replaces { get; }
    internal string Reason { get; }
    internal bool Available => Code == CommonActionResultCodes.Committed;

    internal static SpellWorkbenchLoadPreview Admitted(
        Guid recipeId,
        SpellWorkbenchUsageAllocation[] usage,
        string replaces = "") =>
        new(CommonActionResultCodes.Committed, recipeId, usage, replaces, string.Empty);

    internal static SpellWorkbenchLoadPreview Refused(
        ServiceActionResultCode code,
        string reason) =>
        new(code, Guid.Empty, Array.Empty<SpellWorkbenchUsageAllocation>(), string.Empty, reason);
}
