#if SERVICE_CYCLE_PROFILE
using System;
using System.Collections.Generic;
using System.Linq;
using OrbModConfig;
using OrbModding.Common;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OrbAutomata.GameMcp;

internal sealed class GameMcpScreenReader
{
    /// <summary>
    /// What a caller is told when the suite cannot read the game's tooltips. The binding layer's
    /// own account of which accessor was missing goes to the log, where it is actionable; on the
    /// wire it named types no caller can look up and gave them nothing to do.
    /// </summary>
    private const string TooltipsUnreadable =
        "The suite could not attach to the game's tooltips in this run, so no tooltip can be " +
        "read until the run restarts.";

    private readonly Func<ModConfigUiShell?> _uiShell;
    private readonly Func<GameMcpTooltipNativeAccess?> _tooltipNativeAccess;
    private readonly Func<string> _tooltipContractFailure;
    private readonly Func<ModalOpenGameAction?> _modalOpenGameAction;
    private readonly Action<string> _logWarning;
    private readonly GameMcpGadgetResults _gadgetResults;
    private readonly GameMcpNavigator _navigator;

    internal GameMcpScreenReader(
        Func<ModConfigUiShell?> uiShell,
        Func<GameMcpTooltipNativeAccess?> tooltipNativeAccess,
        Func<string> tooltipContractFailure,
        Func<ModalOpenGameAction?> modalOpenGameAction,
        Action<string> logWarning,
        GameMcpGadgetResults gadgetResults,
        GameMcpNavigator navigator)
    {
        _uiShell = uiShell ?? throw new ArgumentNullException(nameof(uiShell));
        _tooltipNativeAccess = tooltipNativeAccess ??
            throw new ArgumentNullException(nameof(tooltipNativeAccess));
        _tooltipContractFailure = tooltipContractFailure ??
            throw new ArgumentNullException(nameof(tooltipContractFailure));
        _modalOpenGameAction = modalOpenGameAction ??
            throw new ArgumentNullException(nameof(modalOpenGameAction));
        _logWarning = logWarning ?? throw new ArgumentNullException(nameof(logWarning));
        _gadgetResults = gadgetResults ?? throw new ArgumentNullException(nameof(gadgetResults));
        _navigator = navigator ?? throw new ArgumentNullException(nameof(navigator));
    }

    internal GameMcpValue CaptureScreenCatalogGameMcp()
    {
        var scene = SceneManager.GetActiveScene().name;
        var uiShell = _uiShell();
        if (scene != "Main" || uiShell is null || !uiShell.IsAlive)
            return GameMcpScreenCatalogProjection.ProjectGameMcpScreenCatalog(
                scene,
                navigationAvailable: false,
                Array.Empty<(string Label, bool Active, bool Locked)>(),
                Array.Empty<(string Strip, string Label, bool Active)>());
        var tabs = uiShell.CaptureNativeTabsForGameMcp();
        var subtabs = _navigator.CaptureSubtabs();
        return GameMcpScreenCatalogProjection.ProjectGameMcpScreenCatalog(
            scene,
            navigationAvailable: true,
            tabs.Select(tab => (tab.Label, tab.Active, tab.Locked)).ToArray(),
            subtabs.Select(subtab => (subtab.StripKey, subtab.Label, subtab.Active)).ToArray());
    }

    internal GameMcpCommandResult CaptureTooltipCatalogGameMcp(GameMcpCommand command)
    {
        var nativeAccess = _tooltipNativeAccess();
        if (nativeAccess is null)
        {
            return TooltipsUnreadableBecause(_tooltipContractFailure());
        }
        if (!TryCaptureScreenElements(out var entries, out var chromeFailure))
        {
            return TooltipsUnreadableBecause(chromeFailure);
        }
        if (!int.TryParse(
                command.PayloadValue,
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out var offset))
        {
            return _gadgetResults.Rejected(
                "tooltip_offset_invalid",
                "That page marker is not one game_screen_elements printed; page it again — the " +
                "markers change when the screen does.");
        }
        // A page is a stretch of panels, not a stretch of elements: offset, limit, total and
        // nextOffset all count panels, so a page never cuts one in half.
        var paths = new string[entries.Length];
        for (var index = 0; index < entries.Length; index++) paths[index] = entries[index].Path;
        var panels = NativeObjectPath.Runs(paths);
        var first = Math.Max(offset, 0);
        var end = (int)Math.Min(panels.Count, (long)offset + command.Amount);

        // Every element is addressed by the shortest tail of its path that no other live element
        // answers to. The Canvas-rooted chain existed so a row plus its page's prefix would
        // resolve; a tail that resolves on its own needs neither, and a round measured 23% of this
        // verb as ancestry no caller ever quoted back. The address lengthens one segment at a time
        // and only where uniqueness requires it, so it is always a valid `path` argument.
        var projected = new GameMcpArrayBuilder();
        for (var index = first; index < end; index++)
        {
            var panel = panels[index];
            var members = new List<GameMcpObjectBuilder>(panel.Count);
            var segments = new List<string>(panel.Count);
            for (var member = panel.Start; member < panel.Start + panel.Count; member++)
            {
                var entry = entries[member];
                var segment = GameMcpTooltipPanelRow.ShortestUnique(entry.Path, paths);
                GameMcpObjectBuilder tooltip;
                if (entry.Hover is { } hover)
                {
                    var item = hover.tooltipItem!;
                    if (!nativeAccess.TryReadSubTooltips(hover, out _, out var readFailure))
                    {
                        return TooltipsUnreadableBecause(readFailure);
                    }
                    if (!nativeAccess.TryReadEntityId(item, out var entityId, out var identityFailure))
                    {
                        return TooltipsUnreadableBecause(identityFailure);
                    }
                    tooltip = GameMcpTooltipPanelRow.Project(
                        segment,
                        item.GetName(),
                        entityId,
                        command.FrameContext?.World?.Snapshot);
                }
                else
                {
                    tooltip = GameMcpTooltipPanelRow.Opens(segment, entry.ModalTitle);
                }
                members.Add(tooltip);
                segments.Add(segment);
            }
            projected.Add(GameMcpTooltipPanelRow.Panel(members, segments));
        }
        var details = new GameMcpObjectBuilder
        {
            ["scene"] = SceneManager.GetActiveScene().name,
        };
        details["total"] = panels.Count;
        details["rows"] = projected;
        if (end < panels.Count) details["nextOffset"] = end;
        return _gadgetResults.Committed(
            "tooltip_catalog_read",
            details);
    }

    /// <summary>
    /// The panel one chrome control opens, when the path names exactly one chrome control.
    /// </summary>
    private bool TryNameChromePanel(string path, out string title, out string failure)
    {
        title = string.Empty;
        failure = string.Empty;
        var modalOpenGameAction = _modalOpenGameAction();
        if (modalOpenGameAction is null)
        {
            failure = ModalOpenGameAction.NoActivator;
            return false;
        }
        if (!modalOpenGameAction.TryReadActivators(out var activators, out failure)) return false;
        foreach (var activator in activators)
        {
            var located = NativeObjectPath.Locate(activator.Control).Path;
            if (!NativeObjectPath.Addresses(located, path)) continue;
            if (title.Length > 0)
            {
                title = string.Empty;
                return false;
            }
            title = activator.Title;
        }
        return title.Length > 0;
    }

    internal GameMcpCommandResult ReadTooltipGameMcp(GameMcpCommand command)
    {
        var requestedPath = command.PayloadValue;
        var active = CaptureActiveHoverTooltips();
        var nativeAccess = _tooltipNativeAccess();
        if (nativeAccess is null)
        {
            return TooltipsUnreadableBecause(_tooltipContractFailure());
        }

        HoverTooltip hover;
        if (command.TargetId != Guid.Empty)
        {
            if (!TryAddressTooltipByEntity(
                    nativeAccess, active, command.TargetId, out var drawn, out var entityRefusal))
            {
                return entityRefusal;
            }
            if (drawn is null) return ReadEntityTooltipGameMcp(command, nativeAccess);
            hover = drawn;
        }
        else
        {
            // The catalog hands out the part of the path its page's shared prefix does not already
            // say, and which prefix that was depends on which page the row came from. So a row
            // resolves by the tail it was given: the whole path, or any path ending in it at an
            // element boundary.
            var matches = active
                .Where(entry => NativeObjectPath.Addresses(entry.Path, requestedPath))
                .ToArray();
            if (matches.Length == 0)
            {
                // The chrome controls are on the screen and in the element list, and none of them
                // draws a tooltip. "Matched 0 elements" would have read as a stale path for a path
                // the catalog printed one call ago, so the answer names the panel instead.
                if (!TryNameChromePanel(requestedPath, out var panel, out var chromeFailure))
                {
                    if (chromeFailure.Length > 0) return TooltipsUnreadableBecause(chromeFailure);
                }
                else
                {
                    return _gadgetResults.Rejected(
                        "tooltip_content_unavailable",
                        "This control opens the " + panel + " panel and draws no tooltip of its " +
                        "own; put the panel up with game_modal mode open, title " + panel + ".");
                }
            }
            if (matches.Length != 1)
            {
                // Naming the next step is the point. A tail is only as unique as the page it came
                // from, and two scroll lists on one screen hand out colliding tails routinely; the
                // catalog already published the prefix that separates them, so the refusal says to
                // put it back on rather than leaving a caller to guess that a longer path exists.
                return _gadgetResults.Rejected(
                    "tooltip_match_failed",
                    "tooltip path '" + requestedPath + "' matched " +
                    matches.Length + " active current-screen elements" +
                    "; re-read game_screen_elements for this screen's current paths, which name " +
                    "one element each");
            }
            hover = matches[0].Hover;
        }
        if (hover.tooltipItem is null)
        {
            return _gadgetResults.Rejected(
                "tooltip_content_unavailable",
                "The game draws no tooltip for this element.");
        }
        if (!nativeAccess.TryReadSubTooltips(hover, out var children, out var readFailure))
        {
            return TooltipsUnreadableBecause(readFailure);
        }

        // A panel draws one node list, and which one is the live more-info key the player is or is
        // not holding. The container the screen already has up for this item answers with the
        // game's own IsUsingAltTooltip(); with no container up the screen is drawing nothing for
        // this element and the panel's resting state is its main list.
        var usingAlt = UITooltipContainer.globalTooltips?
            .Any(panel => panel is not null &&
                ReferenceEquals(panel.item, hover.tooltipItem) &&
                panel.IsUsingAltTooltip()) == true;
        if (!nativeAccess.TryReadEntityId(hover.tooltipItem, out var entityId, out var identityFailure))
        {
            return TooltipsUnreadableBecause(identityFailure);
        }
        return ProjectTooltipGameMcp(nativeAccess, hover.tooltipItem, children, usingAlt, entityId);
    }

    /// <summary>
    /// An entity's own tooltip, for an id nothing on the screen is drawing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A tooltip's link lines name what the player sees by hovering a row inside it — the resource
    /// a cost is paid in, the statistic an effect raises, the glossary word a number is measured
    /// in — and none of those is drawn on the screen itself. Answering only for what the screen
    /// draws made every such link a call that refused, so the panel a link opens is read where it
    /// lives: on the entity, exactly as hovering the link would put it up. Every tooltip builder in
    /// the pinned build is held to doing nothing but answer, by the installed-game side-effect
    /// contract over the whole call graph below them.
    /// </para>
    /// <para>
    /// No container is up for a panel the screen is not drawing, so the more-info key has nothing
    /// to toggle and the panel is read in its resting state, its main node list. The panel is the
    /// entity's alone: the sub-panels an element adds beside it are the element's, and a caller who
    /// wants those reads the element.
    /// </para>
    /// </remarks>
    private GameMcpCommandResult ReadEntityTooltipGameMcp(
        GameMcpCommand command,
        GameMcpTooltipNativeAccess nativeAccess)
    {
        var world = command.FrameContext!.World?.Snapshot;
        var catalog = command.FrameContext!.EntityIdentities;
        if (world is null || !catalog.IsBound)
        {
            return _gadgetResults.Rejected(
                "entity_catalog_unavailable",
                "Nothing this screen draws is about this id, and no save is loaded to read its " +
                "tooltip from; continue a save first.");
        }
        if (!catalog.TryGet(command.TargetId, out var identity))
        {
            return _gadgetResults.Rejected("unknown_uuid", GameMcpTooltipPanelRow.UnknownEntity);
        }
        if (GameMcpEntityExplainer.IsHidden(world, command.TargetId))
        {
            return _gadgetResults.Rejected("not_on_screen", GameMcpTooltipPanelRow.HiddenEntity);
        }
        if (!GameMcpEntityIdentity.TryResolveTooltip(
                command.TargetId, identity.RuntimeType, out var tooltip))
        {
            return _gadgetResults.Rejected(
                "tooltip_content_unavailable",
                identity.DisplayName + " is a " + identity.RuntimeType + " the game draws no " +
                "tooltip for, and nothing this screen draws is about it.");
        }
        return ProjectTooltipGameMcp(
            nativeAccess, tooltip, Array.Empty<ITooltipable>(), false, command.TargetId);
    }

    private GameMcpCommandResult ProjectTooltipGameMcp(
        GameMcpTooltipNativeAccess nativeAccess,
        ITooltipable item,
        ITooltipable[] children,
        bool usingAlt,
        Guid entityId)
    {
        // A link pointer names the entity it points at, so it needs that entity's id; a tooltipable
        // about nothing answers empty and its pointer carries the name alone. A bound accessor that
        // throws is a native fault rather than an absent id, and it refuses the read with the same
        // account the hovered element's own identity would have given.
        var linkFailure = string.Empty;
        Guid Identify(ITooltipable link)
        {
            if (nativeAccess.TryReadEntityId(link, out var linkId, out var linkDetail)) return linkId;
            linkFailure = linkDetail;
            throw new InvalidOperationException(nameof(Identify));
        }

        GameMcpObjectBuilder details;
        try
        {
            if (!GameMcpTooltipProjector.TryProject(
                    item,
                    children,
                    usingAlt,
                    GameMcpTooltipLayout.Read(),
                    Identify,
                    out details))
            {
                return _gadgetResults.Rejected(
                    "tooltip_depth_exceeded",
                    "This tooltip leads on through more of the game than the suite will read in " +
                    "one answer, so none of its text is returned; the screen still shows it.");
            }
            if (entityId != Guid.Empty) details["uuid"] = entityId.ToString("D");
        }
        catch (Exception exception)
        {
            if (linkFailure.Length > 0) return TooltipsUnreadableBecause(linkFailure);
            return _gadgetResults.Rejected(
                "tooltip_read_faulted",
                "Reading this tooltip failed, so its text cannot be read." +
                GameActionFaultLog.Record(exception, "tooltip"));
        }
        var result = _gadgetResults.Committed(
            "tooltip_read",
            details);
        return result;
    }

    /// <summary>
    /// The one live element a published entity id addresses, null when the screen draws nothing
    /// about it, or the refusal that answers instead.
    /// </summary>
    /// <remarks>
    /// The element-to-entity binding is the catalog's own, taken per element, so a uuid addresses
    /// exactly the elements the catalog would have printed that id on. A binding that cannot be
    /// taken at all refuses the whole call rather than quietly narrowing the set the answer was
    /// chosen from.
    /// </remarks>
    private bool TryAddressTooltipByEntity(
        GameMcpTooltipNativeAccess nativeAccess,
        IReadOnlyList<TooltipElement> active,
        Guid requested,
        out HoverTooltip? hover,
        out GameMcpCommandResult refusal)
    {
        hover = null;
        refusal = null!;
        var entities = new Guid[active.Count];
        var paths = new string[active.Count];
        for (var index = 0; index < active.Count; index++)
        {
            var entry = active[index];
            paths[index] = entry.Path;
            if (entry.Hover.tooltipItem is null) continue;
            if (!nativeAccess.TryReadEntityId(
                    entry.Hover.tooltipItem, out var entityId, out var identityFailure))
            {
                refusal = TooltipsUnreadableBecause(identityFailure);
                return false;
            }
            entities[index] = entityId;
        }

        var address = GameMcpTooltipPanelRow.AddressEntity(entities, paths, requested);
        if (address.Resolved)
        {
            hover = active[address.Element].Hover;
            return true;
        }
        if (address.Undrawn) return true;

        var listed = new GameMcpArrayBuilder();
        for (var index = 0; index < address.Paths.Count; index++) listed.Add(address.Paths[index]);
        refusal = _gadgetResults.Rejected(
            address.Code, address.Reason, new GameMcpObjectBuilder { ["paths"] = listed });
        return false;
    }

    /// <summary>
    /// One thing the current screen draws that a caller can address: a hover element, or a chrome
    /// control that puts a panel up.
    /// </summary>
    /// <remarks>
    /// The chrome controls carry no <c>HoverTooltip</c>, which is the whole reason the Player,
    /// settings and achievement icons were absent from a verb whose job is to say what is on the
    /// screen. They are not entities and never carry an id; what they carry is the name of the
    /// panel they open, which is the argument that opens it.
    /// </remarks>
    private readonly struct ScreenElement
    {
        internal ScreenElement(
            HoverTooltip? hover,
            string modalTitle,
            NativeObjectPath.Placement placement)
        {
            Hover = hover;
            ModalTitle = modalTitle ?? string.Empty;
            Placement = placement;
        }

        internal HoverTooltip? Hover { get; }

        /// <summary>The panel this control opens, empty on every hover element.</summary>
        internal string ModalTitle { get; }

        internal NativeObjectPath.Placement Placement { get; }
        internal string Path => Placement.Path;
    }

    /// <summary>
    /// Everything the player can currently point at, in screen order: the hover elements and the
    /// chrome controls together, because a caller reading what is on the screen is reading one
    /// screen and not two lists.
    /// </summary>
    private bool TryCaptureScreenElements(out ScreenElement[] elements, out string reason)
    {
        elements = Array.Empty<ScreenElement>();
        reason = string.Empty;
        var merged = new List<ScreenElement>();
        foreach (var entry in CaptureActiveHoverTooltips())
        {
            if (entry.Hover.tooltipItem is null) continue;
            merged.Add(new ScreenElement(entry.Hover, string.Empty, entry.Placement));
        }
        var modalOpenGameAction = _modalOpenGameAction();
        if (modalOpenGameAction is null)
        {
            reason = ModalOpenGameAction.NoActivator;
            return false;
        }
        if (!modalOpenGameAction.TryReadActivators(out var activators, out reason)) return false;
        foreach (var activator in activators)
        {
            merged.Add(new ScreenElement(
                null, activator.Title, NativeObjectPath.Locate(activator.Control)));
        }
        elements = merged
            .OrderBy(static entry => entry.Placement.OrderKey, StringComparer.Ordinal)
            .ToArray();
        return true;
    }

    /// <summary>One live hover element with the hierarchy keys already read off it.</summary>
    internal readonly struct TooltipElement
    {
        internal TooltipElement(HoverTooltip hover, NativeObjectPath.Placement placement)
        {
            Hover = hover;
            Placement = placement;
        }

        internal HoverTooltip Hover { get; }
        internal NativeObjectPath.Placement Placement { get; }
        internal string Path => Placement.Path;
    }

    /// <summary>
    /// Every hover element the player can currently see, in screen order, each carrying its own
    /// hierarchy path.
    /// </summary>
    /// <remarks>
    /// The ancestry is walked once per element. Sorting, the shared prefix, and each row's printed
    /// path all read that one result, where they previously walked the same chain three times over.
    /// </remarks>
    private static IReadOnlyList<TooltipElement> CaptureActiveHoverTooltips() =>
        Resources.FindObjectsOfTypeAll(typeof(HoverTooltip))
            .OfType<HoverTooltip>()
            .Where(hover =>
                hover.enabled &&
                hover.gameObject.activeInHierarchy &&
                GameMcpTooltipNativeAccess.OnScreen(hover))
            .Select(static hover => new TooltipElement(hover, NativeObjectPath.Locate(hover)))
            .OrderBy(static entry => entry.Placement.OrderKey, StringComparer.Ordinal)
            .ToArray();

    internal GameMcpCommandResult ProbeGameMcp(GameMcpCommand command)
    {
        var uiShell = _uiShell();
        GameMcpObjectBuilder details;
        switch (command.Mode)
        {
            case "runtime":
                var lifecycle = GameLifecycleMonitor.Shared.Current;
                details = new GameMcpObjectBuilder
                {
                    ["scene"] = SceneManager.GetActiveScene().name,
                    ["frame"] = Time.frameCount,
                    ["timeScale"] = Time.timeScale,
                    ["lifecycleGeneration"] = lifecycle.Generation,
                    ["lifecycleState"] = lifecycle.State.ToString(),
                    ["gameplayReady"] = lifecycle.IsGameplayReady,
                    ["modsShellAlive"] = uiShell?.IsAlive == true,
                };
                break;
            case "action_queue_room":
                var queue = new AutoBuyNativeQueueRoomAdapter();
                if (!queue.TryReadRemainingRoom(out var remaining, out var queueFailure))
                    return _gadgetResults.Rejected(
                        "native_probe_unavailable",
                        "The action queue's free room could not be read: " + queueFailure.Text);
                // The game's own upgrade button queues past the maximum, so the reading goes
                // negative. Nought free slots is the answer either way; the overshoot rides beside
                // it as the fact it is, and is absent when there is none.
                details = new GameMcpObjectBuilder
                {
                    ["remainingRoom"] = Math.Max(0, remaining),
                };
                if (remaining < 0)
                    details["entriesBeyondCapacity"] = -remaining;
                break;
            case "navigation":
                var tabs = uiShell is not null && uiShell.IsAlive
                    ? uiShell.CaptureNativeTabsForGameMcp()
                    : Array.Empty<GameMcpNativeTab>();
                details = new GameMcpObjectBuilder
                {
                    ["scene"] = SceneManager.GetActiveScene().name,
                    ["nativeTabCount"] = tabs.Count,
                    ["activeNativeSubtabCount"] = _navigator.CaptureSubtabs().Count,
                };
                break;
            default:
                return _gadgetResults.Failed(
                    "unsupported_probe",
                    "The suite asked for a reading this build does not take, so nothing was read.");
        }
        return _gadgetResults.Committed(
            "probe_read",
            details);
    }

    /// <summary>
    /// The one tooltip-unreadable answer, with the binding layer's own account of what was missing
    /// written to the suite log rather than dropped.
    /// </summary>
    private GameMcpCommandResult TooltipsUnreadableBecause(string nativeDetail)
    {
        if (nativeDetail.Length > 0)
            _logWarning("Game MCP could not read a tooltip: " + nativeDetail);
        return _gadgetResults.Failed("tooltip_contract_unavailable", TooltipsUnreadable);
    }
}
#endif
