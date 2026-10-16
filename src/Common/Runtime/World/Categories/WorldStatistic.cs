using System;

namespace OrbModding.Common.Runtime.World;

/// <summary>
/// One entry of the game's own statistic glossary: the word the screen prints above a number, and
/// the sentence it prints under it.
/// </summary>
/// <remarks>
/// <para>
/// These are the <c>AttributeSO</c> records — 211 on the pinned build — that head every tooltip
/// section and fill the Statistics tab. They are not the thing a player calls an attribute: nothing
/// buys one, nothing levels one, and none of them carries a cost, a prerequisite or a level. A
/// record is authored text and two flags, and it never changes while the game runs.
/// </para>
/// <para>
/// Until this category existed the game's own definitions reached a reader through exactly one
/// route — hovering an element that happened to hang one — so the meaning of <c>Recovery Size</c>
/// was readable only where something already showed a Recovery Size. Publishing them makes the
/// glossary addressable: <c>world_search</c> finds the word, <c>world_get</c> reads the sentence,
/// and 211 rows stop reading as projected by nothing.
/// </para>
/// <para>
/// <see cref="GlobalDefinition"/> is captured and deliberately not published. It is the string key
/// effect scripts use as their property type, which is how a glyph or upgrade factor is joined back
/// to the statistic it moves; it is an authoring key the player never reads, and several of them
/// spell themselves <c>Tooltip:ChallengeActive</c> or <c>Alert:Research</c>. A reader who met that
/// on the wire would have met a word from no screen.
/// </para>
/// </remarks>
internal readonly struct WorldStatistic : IWorldEntity
{
    internal WorldStatistic(
        Guid statisticId,
        string displayType,
        bool isPercent,
        string description,
        string globalDefinition)
    {
        StatisticId = statisticId;
        DisplayType = displayType ?? string.Empty;
        IsPercent = isPercent;
        Description = description ?? string.Empty;
        GlobalDefinition = globalDefinition ?? string.Empty;
    }

    internal Guid StatisticId { get; }

    public Guid EntityId => StatisticId;

    /// <summary>
    /// Which of the three sections of a tooltip this row heads, in the game's own word: Statistic,
    /// Information or Action.
    /// </summary>
    /// <remarks>
    /// The word is the referenced <c>DisplayTypeSO</c>'s own display name rather than
    /// <c>Reference.ToType()</c>, which wraps the same word in the rich-text colour tags the screen
    /// paints it with. One record on the pinned build — Starting Level — references no display type
    /// at all, and its cell is empty rather than a fourth word this suite made up.
    /// </remarks>
    internal string DisplayType { get; }

    /// <summary>Whether the number this word heads is a percentage rather than a count.</summary>
    internal bool IsPercent { get; }

    /// <summary>
    /// The sentence the tooltip prints under the word. Eight of the 211 are UI plumbing the game
    /// authors none for, and an empty description is the absence of one rather than a dropped row.
    /// </summary>
    internal string Description { get; }

    /// <summary>The authoring key effect scripts name this statistic by. Captured, never published.</summary>
    internal string GlobalDefinition { get; }
}

/// <summary>The statistic glossary: <c>AttributeSO.All</c>.</summary>
internal sealed class WorldStatisticBinder : WorldPlainBinder<WorldStatistic>
{
    private Func<object, Guid>? _id;
    private Func<object, string>? _displayType;
    private Func<object, bool>? _isPercent;
    private Func<object, string>? _description;
    private Func<object, string>? _globalDefinition;

    internal override string Category => "statistics";

    internal override string TypeName => "AttributeSO";

    internal override string Bind(Type type)
    {
        var bind = new WorldMemberBinding(type, TypeName);
        _id = bind.Call<Guid>("GetGuid");
        _isPercent = bind.Field<bool>("isPercent");
        _description = bind.Field<string>("description");
        _globalDefinition = bind.Field<string>("globalDefinition");

        // The display type is a reference holding a reference, and the record that references
        // nothing reads as the empty string rather than failing the whole category.
        _displayType = bind
            .Through("displayTypeRef")
            .Through("displayType")
            .Field<string>("displayName");
        return bind.Failure;
    }

    internal override WorldStatistic Read(object entity) =>
        new(
            _id!(entity),
            _displayType!(entity),
            _isPercent!(entity),
            _description!(entity),
            _globalDefinition!(entity));
}
