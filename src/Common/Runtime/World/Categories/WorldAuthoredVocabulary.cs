using System;

namespace OrbModding.Common.Runtime.World;

/// <summary>
/// One enchantment a scribed scroll applies to a structure, under the word the scroll's own tooltip
/// prints.
/// </summary>
/// <remarks>
/// <para>
/// The world already publishes both edges that point here — <c>WorldStructureEnchantment</c> says
/// which enchantment a structure carries and at what level, and <c>WorldScrollTarget</c> says which
/// one a scroll would apply — and until now both of them named a node nothing could enumerate. A
/// reader was told <i>this structure carries `0796ee25…` at level 3</i> and had no surface that
/// would say the word "Advancment".
/// </para>
/// <para>
/// The persistent effects the enchantment applies are not on the row. They are a
/// <c>PersistentEffectBlock</c> list — the same shape <c>effect-blocks</c> publishes for the owners
/// that reader binds — so carrying them here would be a second spelling of a table the world
/// already has a place for.
/// </para>
/// </remarks>
internal readonly struct WorldEnchantment : IWorldEntity
{
    internal WorldEnchantment(Guid enchantmentId, string description)
    {
        EnchantmentId = enchantmentId;
        Description = description ?? string.Empty;
    }

    internal Guid EnchantmentId { get; }

    public Guid EntityId => EnchantmentId;

    internal string Description { get; }
}

/// <summary>What scribing applies: <c>EnchantmentSO.All</c>.</summary>
internal sealed class WorldEnchantmentBinder : WorldPlainBinder<WorldEnchantment>
{
    private Func<object, Guid>? _id;
    private Func<object, string>? _description;

    internal override string Category => "enchantments";

    internal override string TypeName => "EnchantmentSO";

    internal override string Bind(Type type)
    {
        var bind = new WorldMemberBinding(type, TypeName);
        _id = bind.Call<Guid>("GetGuid");
        _description = bind.Field<string>("description");
        return bind.Failure;
    }

    internal override WorldEnchantment Read(object entity) =>
        new(_id!(entity), _description!(entity));
}

/// <summary>
/// One glyph family — Elemental, Alchemical, Manifestation — the word that decides which page of
/// the spell screen a glyph appears on.
/// </summary>
/// <remarks>
/// This is the far side of the keyword line every <c>glyphs</c> row already prints:
/// <c>GlyphSO.glyphTypes</c> is one of the keyword walk's thirteen owners, so the word travels, and
/// the id behind it answered no category. The class stores its two sprites and nothing else, so the
/// row is the handle and the sentence.
/// </remarks>
internal readonly struct WorldGlyphType : IWorldEntity
{
    internal WorldGlyphType(Guid glyphTypeId, string description)
    {
        GlyphTypeId = glyphTypeId;
        Description = description ?? string.Empty;
    }

    internal Guid GlyphTypeId { get; }

    public Guid EntityId => GlyphTypeId;

    internal string Description { get; }
}

/// <summary>The glyph families: <c>GlyphTypeSO.All</c>.</summary>
internal sealed class WorldGlyphTypeBinder : WorldPlainBinder<WorldGlyphType>
{
    private Func<object, Guid>? _id;
    private Func<object, string>? _description;

    internal override string Category => "glyph types";

    internal override string TypeName => "GlyphTypeSO";

    internal override string Bind(Type type)
    {
        var bind = new WorldMemberBinding(type, TypeName);
        _id = bind.Call<Guid>("GetGuid");
        _description = bind.Field<string>("description");
        return bind.Failure;
    }

    internal override WorldGlyphType Read(object entity) =>
        new(_id!(entity), _description!(entity));
}

/// <summary>
/// One of the four rune stones — Compulsion, Life, Principle, Spirit — the named stones the time
/// layer hands the player.
/// </summary>
/// <remarks>
/// Each stone gates itself behind a <c>Prerequisites.Container</c>, and the row does not answer it.
/// <c>RuneStoneSO.IsAvailable()</c> is one call to <c>Prerequisites.Container.Check()</c> — and
/// <c>IsVisible()</c> is one call to <c>IsAvailable()</c>, the same fact twice — and that
/// <c>Check()</c> latches <c>available</c>: it is the per-pass capture write the manifest already
/// carries ten rows of debt for, all of which leave together when the whole-entity container is
/// published. Adding an eleventh to reach four booleans is the wrong trade, and the consequence is
/// stated rather than hidden: a stone's row says what the stone is and not yet whether it is
/// unlocked.
/// </remarks>
internal readonly struct WorldRuneStone : IWorldEntity
{
    internal WorldRuneStone(Guid runeStoneId, string description)
    {
        RuneStoneId = runeStoneId;
        Description = description ?? string.Empty;
    }

    internal Guid RuneStoneId { get; }

    public Guid EntityId => RuneStoneId;

    internal string Description { get; }
}

/// <summary>The rune stones: <c>RuneStoneSO.All</c>.</summary>
internal sealed class WorldRuneStoneBinder : WorldPlainBinder<WorldRuneStone>
{
    private Func<object, Guid>? _id;
    private Func<object, string>? _description;

    internal override string Category => "rune stones";

    internal override string TypeName => "RuneStoneSO";

    internal override string Bind(Type type)
    {
        var bind = new WorldMemberBinding(type, TypeName);
        _id = bind.Call<Guid>("GetGuid");
        _description = bind.Field<string>("description");
        return bind.Failure;
    }

    internal override WorldRuneStone Read(object entity) =>
        new(_id!(entity), _description!(entity));
}

/// <summary>
/// One of the three words a tooltip splits its sections under: Statistic, Information or Action.
/// </summary>
/// <remarks>
/// <c>statistics</c> already prints the word on every one of its 211 rows, read through the same
/// two references the game reads it through. What it could not do was answer for the word's own id:
/// the three assets are loaded, they carry uuids, and asking one of them what it was got its
/// identity back and nothing else. The row is the handle, and the word stays where a reader meets
/// it — on the statistic that is tagged with it.
/// </remarks>
internal readonly struct WorldDisplayType : IWorldEntity
{
    internal WorldDisplayType(Guid displayTypeId, string description)
    {
        DisplayTypeId = displayTypeId;
        Description = description ?? string.Empty;
    }

    internal Guid DisplayTypeId { get; }

    public Guid EntityId => DisplayTypeId;

    internal string Description { get; }
}

/// <summary>The tooltip sections: <c>DisplayTypeSO.All</c>.</summary>
internal sealed class WorldDisplayTypeBinder : WorldPlainBinder<WorldDisplayType>
{
    private Func<object, Guid>? _id;
    private Func<object, string>? _description;

    internal override string Category => "display types";

    internal override string TypeName => "DisplayTypeSO";

    internal override string Bind(Type type)
    {
        var bind = new WorldMemberBinding(type, TypeName);
        _id = bind.Call<Guid>("GetGuid");
        _description = bind.Field<string>("description");
        return bind.Failure;
    }

    internal override WorldDisplayType Read(object entity) =>
        new(_id!(entity), _description!(entity));
}
