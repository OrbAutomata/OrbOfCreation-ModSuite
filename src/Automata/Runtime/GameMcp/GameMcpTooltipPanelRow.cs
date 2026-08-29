#if SERVICE_CYCLE_PROFILE
using System;
using System.Collections.Generic;
using System.Globalization;
using OrbModConfig;
using OrbModding.Common.Runtime.World;

namespace OrbAutomata.GameMcp;

/// <summary>
/// One element of one tooltip panel, in the columns every panel on the page is read in.
/// </summary>
/// <remarks>
/// <para>
/// A panel row says what the thing is (<c>uuid</c>, <c>name</c>), where the screen keeps it
/// (<c>path</c>), and — for a spell the player has equipped — which slot the cast verbs address it
/// by. Before this, the two panels holding what a player actually casts said only path and name,
/// and reaching an id cost a search while the bracket index in the path read as a slot number one
/// too small.
/// </para>
/// <para>
/// The slot is a join, never the path. The bracket index is a Unity sibling ordinal and matched
/// <c>slot − 1</c> only because nothing above it had been removed that session; deriving a
/// gameplay number from it is precisely the inference the boundary doctrine forbids. What answers
/// instead is the published loadout: the recipe the panel is about, matched against the recipe each
/// occupied slot holds.
/// </para>
/// </remarks>
internal static class GameMcpTooltipPanelRow
{
    internal static GameMcpObjectBuilder Project(
        string path,
        string name,
        Guid entityId,
        GameWorldState? world)
    {
        var row = new GameMcpObjectBuilder
        {
            ["path"] = path,
            ["name"] = name,
        };
        if (entityId == Guid.Empty) return row;
        row["uuid"] = entityId.ToString("D");
        if (world is not null && TrySoleSpellSlot(world, entityId, out var slotIndex))
            row["slot"] = GameMcpSlotNumbering.Wire(slotIndex);
        return row;
    }

    /// <summary>
    /// The shortest tail of this element's path that no other live element answers to.
    /// </summary>
    /// <remarks>
    /// The address a caller quotes back only has to name one element, and this is the computation
    /// the ambiguous <c>game_tooltip</c> refusal already performs to decide that it does not. A
    /// live round spent 23% of this verb on Canvas-rooted ancestry no caller ever quoted: the chain
    /// was there so that a row plus its page's prefix would resolve, and a tail that resolves on
    /// its own needs neither. It lengthens one segment at a time and only where uniqueness
    /// requires it, so the address is always a valid <c>path</c> argument and never longer than the
    /// whole chain.
    /// </remarks>
    internal static string ShortestUnique(string path, IReadOnlyList<string> livePaths)
    {
        if (path is null) throw new ArgumentNullException(nameof(path));
        if (livePaths is null) throw new ArgumentNullException(nameof(livePaths));
        var cut = path.Length;
        while (true)
        {
            cut = path.LastIndexOf('/', cut - 1);
            if (cut <= 0) return path;
            var tail = path.Substring(cut + 1);
            var found = 0;
            for (var index = 0; index < livePaths.Count && found < 2; index++)
                if (NativeObjectPath.Addresses(livePaths[index], tail)) found++;
            if (found == 1) return tail;
        }
    }

    /// <summary>
    /// Which live element one entity id addresses, or the refusal that answers instead.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A uuid is an address alongside a path, never a replacement for one. Roughly nine in ten
    /// hoverable elements are bound to an entity, so a caller holding an id from a search, a list
    /// or an action response can read the screen's words about it with no catalog detour — but the
    /// remaining tenth is chrome with no id at all, including the suite's own controls, and a path
    /// is the only address those will ever have.
    /// </para>
    /// <para>
    /// One entity shown by several elements is the common case rather than the corner: the Magic
    /// screen draws every equipped spell twice, once in its own list and once in the persistent
    /// casting bar, and the two are different objects with separately read sub-tooltips that may
    /// legitimately print different text. Nothing on the wire says which the caller meant, so this
    /// hands back the addresses and refuses — the same answer, inverted, that an ambiguous path
    /// already gets, and the same rule as <see cref="TrySoleSpellSlot"/>: two answers mean no
    /// answer. Each address is the shortest form that resolves, by <see cref="Address"/>, so the
    /// duplicate pair comes back as two short segments rather than two full ancestries.
    /// </para>
    /// </remarks>
    internal static EntityAddress AddressEntity(
        IReadOnlyList<Guid> entities,
        IReadOnlyList<string> paths,
        Guid requested,
        bool loaded,
        string publishedScreen,
        string activeScreen = "")
    {
        if (entities is null) throw new ArgumentNullException(nameof(entities));
        if (paths is null) throw new ArgumentNullException(nameof(paths));
        if (entities.Count != paths.Count)
            throw new ArgumentException("one entity id per live element", nameof(entities));

        // An element bound to nothing carries the empty id, and the empty id names nothing: chrome
        // is reachable by path and by path only, whatever a caller passes here.
        var matches = new List<int>();
        for (var index = 0; index < entities.Count; index++)
            if (entities[index] != Guid.Empty && entities[index] == requested) matches.Add(index);

        if (matches.Count == 1) return EntityAddress.Found(matches[0]);
        if (matches.Count == 0)
        {
            return loaded
                ? EntityAddress.Refused(
                    "not_on_screen", NotOnScreen(publishedScreen, activeScreen))
                : EntityAddress.Refused(
                    "unknown_uuid",
                    "No entity in this build carries this id, so no element on any screen is " +
                    "about it; check the id you sent, or find the thing with world_search.");
        }

        var addresses = new string[matches.Count];
        for (var index = 0; index < addresses.Length; index++)
            addresses[index] = ShortestUnique(paths[matches[index]], paths);
        return EntityAddress.Ambiguous(
            "ambiguous_element",
            matches.Count.ToString(CultureInfo.InvariantCulture) +
            " elements on this screen show this entity, and two elements about one thing may " +
            "print different text; name one of the paths listed here with path.",
            addresses);
    }

    /// <summary>
    /// Why no element answered, told apart by where the caller already is.
    /// </summary>
    /// <remarks>
    /// Sending a caller to the screen it is standing on is the defect this exists to stop: a live
    /// round read "the world publishes it on Workshop, so navigate there" while standing on
    /// Workshop. Both facts were true — the screen does draw the thing, and nothing drawn *right
    /// now* is about it — and the sentence stated only the first, so it read as a contradiction of
    /// the row the caller had just read. Where they agree the answer says both, and where the
    /// destination is a subtab of the screen the caller is on it says that instead of "navigate to
    /// where you are". The comparison is the navigation vocabulary's own: a screen word is the
    /// game's tab label spelled exactly, matched the way <c>game_navigate</c> matches it.
    /// </remarks>
    private static string NotOnScreen(string publishedScreen, string activeScreen)
    {
        if (string.IsNullOrEmpty(publishedScreen))
        {
            return "Nothing this screen draws is about this entity; page game_screen_catalog for " +
                "the screens this build offers and navigate to the one that draws it.";
        }
        var separator = publishedScreen.IndexOf('/');
        var screen = separator < 0 ? publishedScreen : publishedScreen.Substring(0, separator);
        if (string.IsNullOrEmpty(activeScreen) ||
            !string.Equals(screen, activeScreen, StringComparison.Ordinal))
        {
            return "Nothing this screen draws is about this entity; the world publishes it on " +
                publishedScreen + ", so navigate there and read it again.";
        }
        if (separator >= 0)
        {
            return "Nothing this screen draws is about this entity. The world publishes it on " +
                publishedScreen + ", which is a subtab of the " + screen +
                " screen you are already on, so navigate there and read it again.";
        }
        return "This is the screen the world publishes it on, and nothing it is drawing right now " +
            "is about this entity: the panel holding it is closed, on another subtab, or scrolled " +
            "out of view. Open it and read again, or address the element by path.";
    }

    /// <summary>
    /// The one live element an entity id names, or the code, sentence and addresses that answer
    /// instead. Kept as one value because the three refusals and the hit are one decision.
    /// </summary>
    internal readonly struct EntityAddress
    {
        private readonly string[]? _paths;

        private EntityAddress(int element, string code, string reason, string[]? paths)
        {
            Element = element;
            Code = code;
            Reason = reason;
            _paths = paths;
        }

        internal static EntityAddress Found(int index) =>
            new(index, string.Empty, string.Empty, null);

        internal static EntityAddress Refused(string code, string reason) =>
            new(-1, code, reason, null);

        internal static EntityAddress Ambiguous(string code, string reason, string[] paths) =>
            new(-1, code, reason, paths);

        /// <summary>The index of the one element that answers, or -1 where none does.</summary>
        internal int Element { get; }

        internal bool Resolved => Element >= 0;

        internal string Code { get; }

        internal string Reason { get; }

        /// <summary>The addresses a caller picks one of, empty on every other outcome.</summary>
        internal IReadOnlyList<string> Paths => _paths ?? Array.Empty<string>();
    }

    /// <summary>
    /// The component every element of one panel is an indexed instance of, when there is one and
    /// saying it once is shorter than saying it on every row.
    /// </summary>
    /// <remarks>
    /// Only a whole component counts. A character-wise prefix would cut
    /// <c>PlotNodeItem(Clone)[1]</c> and <c>PlotNodeItem(Clone)[11]</c> mid-index and name a
    /// component that does not exist, so a segment qualifies only as text followed by a bracketed
    /// run of digits, and every element of the panel must carry the same text before it. The line
    /// costs its own label and one copy of the component; every row after the first is what it buys
    /// back, so a short component on a two-row panel does not pay for the line.
    /// </remarks>
    internal static bool TrySharedComponent(IReadOnlyList<string> segments, out string component)
    {
        if (segments is null) throw new ArgumentNullException(nameof(segments));
        component = string.Empty;
        if (segments.Count < 2) return false;
        for (var index = 0; index < segments.Count; index++)
        {
            if (!TryIndexedComponent(segments[index], out var candidate)) return false;
            if (index == 0) component = candidate;
            else if (!string.Equals(component, candidate, StringComparison.Ordinal)) return false;
        }
        return component.Length > 0 &&
            (segments.Count - 1) * component.Length > ComponentLine.Length;
    }

    private const string ComponentLine = "pathComponent: ";

    /// <summary>The text a segment's trailing <c>[index]</c> hangs off, when it has one.</summary>
    private static bool TryIndexedComponent(string segment, out string component)
    {
        component = string.Empty;
        if (segment is null || segment.Length < 3 || segment[segment.Length - 1] != ']')
            return false;
        var open = segment.LastIndexOf('[');
        if (open <= 0 || open + 1 == segment.Length - 1) return false;
        for (var index = open + 1; index < segment.Length - 1; index++)
            if (segment[index] is < '0' or > '9') return false;
        component = segment.Substring(0, open);
        return true;
    }

    /// <summary>
    /// The one occupied slot holding this recipe, when exactly one does.
    /// </summary>
    /// <remarks>
    /// Two answers mean no answer. A loadout may hold the same recipe in two slots, and then every
    /// button showing it joins to both — so naming either would be a guess about which button the
    /// reader is looking at, and the row says nothing rather than sending a cast to the wrong
    /// position. A recipe no slot holds — every spell shown outside the casting bar — is the same
    /// silence for the same reason.
    /// </remarks>
    internal static bool TrySoleSpellSlot(GameWorldState world, Guid spellRecipeId, out int slotIndex)
    {
        if (world is null) throw new ArgumentNullException(nameof(world));
        slotIndex = 0;
        var found = false;
        for (var index = 0; index < world.SpellSlots.Count; index++)
        {
            var slot = world.SpellSlots[index];
            if (!slot.Occupied || slot.SpellRecipeId != spellRecipeId) continue;
            if (found)
            {
                slotIndex = 0;
                return false;
            }
            slotIndex = slot.SlotIndex;
            found = true;
        }
        return found;
    }
}
#endif
