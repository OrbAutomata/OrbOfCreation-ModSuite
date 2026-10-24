#if SERVICE_CYCLE_PROFILE
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using OrbModConfig;
using OrbModding.Common;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace OrbAutomata.GameMcp;

internal sealed class GameMcpNavigator
{
    private readonly MonoBehaviour _host;
    private readonly Func<ModConfigUiShell?> _uiShell;
    private readonly Action<GameMcpCommand, GameMcpCommandResult> _complete;
    private readonly GameMcpGadgetResults _gadgetResults;
    private readonly GameMcpOpenModals _openModals;

    internal GameMcpNavigator(
        MonoBehaviour host,
        Func<ModConfigUiShell?> uiShell,
        Action<GameMcpCommand, GameMcpCommandResult> complete,
        GameMcpGadgetResults gadgetResults,
        GameMcpOpenModals openModals)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _uiShell = uiShell ?? throw new ArgumentNullException(nameof(uiShell));
        _complete = complete ?? throw new ArgumentNullException(nameof(complete));
        _gadgetResults = gadgetResults ?? throw new ArgumentNullException(nameof(gadgetResults));
        _openModals = openModals ?? throw new ArgumentNullException(nameof(openModals));
    }

    internal void Navigate(GameMcpCommand command) =>
        _host.StartCoroutine(NavigateGameMcpAcrossFrames(command));

    private bool TryBeginNavigateGameMcp(
        GameMcpCommand command,
        out GameMcpNavigationSelector? subtabSelector,
        out GameMcpObjectBuilder details,
        out GameMcpCommandResult failure)
    {
        subtabSelector = null;
        details = new GameMcpObjectBuilder();
        failure = null!;
        var scene = SceneManager.GetActiveScene().name;
        var uiShell = _uiShell();
        if (scene != "Main" || uiShell is null || !uiShell.IsAlive)
        {
            failure = _gadgetResults.Rejected(
                "native_navigation_unavailable",
                "The game is not showing its screen tabs right now — this answers only while a " +
                "save is open.");
            return false;
        }

        var request = command.SourceOperation?.Request;
        if (request?.Tab is null)
        {
            failure = _gadgetResults.Failed(
                "navigation_request_invalid",
                "The suite sent a navigation that named no screen, so nothing was applied.");
            return false;
        }
        if (command.TargetId != Guid.Empty && TileOf(command) == GameMcpTile.None)
        {
            // One spelling for one destination: the catalog path this tool takes, the same way
            // every screen cell on the surface prints it.
            var requestedDestination = request.Subtab is null
                ? request.Tab.Label
                : request.Tab.Label + "/" + request.Subtab.Label;
            failure = _gadgetResults.Rejected(
                "tile_destination_mismatch",
                "A tile can be picked on the page that draws it — " +
                GameMcpGadgetPolicy.TileDestinations + " — not on " + requestedDestination);
            return false;
        }
        subtabSelector = request.Subtab;
        var tabs = uiShell.CaptureNativeTabsForGameMcp();
        if (!TryResolveTabSelector(request.Tab, tabs, out var tab, out var tabReason))
        {
            // A locked screen is not a candidate. Offering the five a young run does not draw is
            // how a caller spends a call arriving somewhere the game will not show it.
            failure = NavigationRefusal(
                "screen_match_failed",
                tabReason,
                null,
                "screenCandidates",
                tabs.Where(candidate => !candidate.Locked).Select(candidate => candidate.Label));
            return false;
        }
        if (tab.Locked)
        {
            failure = _gadgetResults.Rejected(
                "screen_locked",
                tab.Label + " is not unlocked yet, so the game draws no such screen.");
            return false;
        }
        if (!uiShell.TrySelectNativeTabForGameMcp(tab.Index, out var selectReason))
        {
            failure = _gadgetResults.Rejected("native_tab_rejected", selectReason);
            return false;
        }

        details = new GameMcpObjectBuilder { ["activeScreen"] = tab.Label };
        return true;
    }

    private IEnumerator NavigateGameMcpAcrossFrames(GameMcpCommand command)
    {
        if (!TryBeginNavigateGameMcp(
                command,
                out var subtabSelector,
                out var details,
                out var failure))
        {
            _complete(command, failure);
            yield break;
        }
        // Native tab selection changes the active content hierarchy over the following frames.
        // Resolving a subtab against a half-built hierarchy is what made one screen's strip match a
        // name that belongs to another: the candidates the matcher searched were not the ones the
        // catalog advertises for the screen the caller asked for.
        var settledScreen = new bool[1];
        yield return SettleNavigation(settledScreen);

        if (subtabSelector is not null)
        {
            var segments = subtabSelector.Labels;
            for (var step = 0; step < segments.Count; step++)
            {
                // A strip appears only once its parent is active, and the parent's click rebuilds
                // the content area over the following frames. Settling between the clicks is what
                // makes the child's strip present to match against — the same reason the screen
                // settles above, one level down.
                if (step > 0) yield return SettleNavigation(settledScreen);
                var subtabs = CaptureSubtabs();
                if (!TryResolveSubtabSelector(
                        segments[step],
                        subtabs,
                        out var subtab,
                        out var subtabReason))
                {
                    yield return CompleteNavigateGameMcpAfterSettlement(
                        command,
                        NavigationRefusal(
                            "subtab_match_failed",
                            SubtabRefusalReason(
                                subtabReason,
                                settledScreen[0],
                                segments.Count > 1 ? subtabSelector.Label : null),
                            details,
                            "subtabCandidates",
                            subtabs.Select(candidate => candidate.Label)));
                    yield break;
                }
                if (!subtab.TrySelect(out var selectionReason))
                {
                    yield return CompleteNavigateGameMcpAfterSettlement(
                        command,
                        _gadgetResults.Rejected("subtab_selection_failed", selectionReason)
                            .WithDetails(details.Freeze()));
                    yield break;
                }
                yield return null;
            }
        }
        if (command.TargetId != Guid.Empty)
        {
            // The tiles are drawn by that same rebuild, and `subtab.TrySelect` presses the tab
            // whether or not it was already active, so being on the page already did not help:
            // every pick read a page mid-rebuild.
            yield return SettleNavigation(settledScreen);
            var scene = SceneManager.GetActiveScene().name;
            var tileResult = TileOf(command) == GameMcpTile.Spell
                ? NavigateExactSpell(command.TargetId, scene)
                : NavigateExactPlot(command.TargetId, scene);
            if (!string.Equals(tileResult.Status, "committed", StringComparison.Ordinal))
            {
                yield return CompleteNavigateGameMcpAfterSettlement(
                    command,
                    tileResult.WithDetails(details.Freeze()));
                yield break;
            }
        }
        // The strips are read once, after arrival settles, in one place. Reading them here — a
        // single frame after the subtab click, with the destination still assembling — is what let
        // the departed screen's strip ride along and made identical navigations disagree.
        var result = _gadgetResults.Committed(
            "navigation_arrived",
            details);
        yield return CompleteNavigateGameMcpAfterSettlement(command, result);
    }

    /// <summary>
    /// Runs until the navigation shell reports the same screen and strips two frames running, or
    /// until one second has passed. <paramref name="settled"/>'s single slot records which it was.
    /// </summary>
    private IEnumerator SettleNavigation(bool[] settled)
    {
        var deadline = Time.realtimeSinceStartup + 1f;
        var stableFrames = 0;
        string? previous = null;
        while (Time.realtimeSinceStartup < deadline && stableFrames < 2)
        {
            yield return null;
            var current = NavigationSettlementSignature();
            stableFrames = string.Equals(previous, current, StringComparison.Ordinal)
                ? stableFrames + 1
                : 0;
            previous = current;
        }
        settled[0] = stableFrames >= 2;
    }

    private IEnumerator CompleteNavigateGameMcpAfterSettlement(
        GameMcpCommand command,
        GameMcpCommandResult result)
    {
        var settled = new bool[1];
        yield return SettleNavigation(settled);
        var uiShell = _uiShell();
        if (string.Equals(result.Status, "committed", StringComparison.Ordinal) &&
            !settled[0])
        {
            result = _gadgetResults.Committed(
                "navigation_arrived",
                new GameMcpObjectBuilder
                {
                    ["postStateUnavailable"] = new GameMcpObjectBuilder
                    {
                        ["reasonCode"] = "post_state_timeout",
                        ["reason"] = "the destination did not settle within one second",
                    }.Freeze(),
                });
        }
        else if (string.Equals(result.Status, "committed", StringComparison.Ordinal) &&
                 (uiShell is null || !uiShell.IsAlive))
        {
            result = _gadgetResults.Committed(
                "navigation_arrived",
                new GameMcpObjectBuilder
                {
                    ["scene"] = SceneManager.GetActiveScene().name,
                    ["subtabStripsUnavailable"] =
                        "The navigation shell was no longer alive after the destination settled.",
                });
        }
        else if (string.Equals(result.Status, "committed", StringComparison.Ordinal))
        {
            var tabs = uiShell!.CaptureNativeTabsForGameMcp();
            var activeTab = tabs.FirstOrDefault(tab => tab.Active);
            var subtabs = CaptureSubtabs();
            var details = new GameMcpObjectBuilder
            {
                ["scene"] = SceneManager.GetActiveScene().name,
                ["subtabStrips"] = GameMcpScreenCatalogProjection.ProjectGameMcpSubtabStrips(
                    subtabs.Select(value =>
                        (value.StripKey, value.Label, value.Active))),
            };
            if (!string.IsNullOrWhiteSpace(activeTab.Label))
                details["activeScreen"] = activeTab.Label;
            if (command.TargetId != Guid.Empty)
            {
                details[TileOf(command) == GameMcpTile.Spell ? "selectedSpell" : "selectedPlot"] =
                    command.TargetId.ToString("D");
            }
            _openModals.AppendTo(details);
            result = _gadgetResults.Committed("navigation_arrived", details);
        }
        _complete(command, result);
    }

    private string NavigationSettlementSignature()
    {
        var uiShell = _uiShell();
        var values = CaptureSubtabs();
        var activeTab = uiShell is not null && uiShell.IsAlive
            ? uiShell.CaptureNativeTabsForGameMcp().FirstOrDefault(tab => tab.Active).Label
            : string.Empty;
        return SceneManager.GetActiveScene().name + "|" + activeTab + "|" +
            string.Join("|", values.Select(value =>
                value.StripKey + ":" + value.Label + ":" + (value.Active ? "1" : "0")));
    }

    private static GameMcpTile TileOf(GameMcpCommand command)
    {
        var request = command.SourceOperation?.Request;
        return request?.Tab is null
            ? GameMcpTile.None
            : GameMcpGadgetPolicy.TileAt(request.Tab.Label, request.Subtab?.Leaf);
    }

    /// <summary>
    /// Opens one spell's card on Magic > Spellbook > Spells, the way clicking its icon does.
    /// </summary>
    /// <remarks>
    /// <c>UISpellRecipeList.ClickRecipe</c> toggles the page's selected-recipe variable, so a click
    /// on the spell whose card is already open closes it. The card is the variable, so the click is
    /// made only when the variable does not already hold this spell, and the variable holding it
    /// afterwards is the postcondition.
    /// </remarks>
    private GameMcpCommandResult NavigateExactSpell(Guid stableUuid, string scene)
    {
        if (scene != "Main")
        {
            return _gadgetResults.Rejected(
                "wrong_scene",
                "Spells can only be picked while a save is open.");
        }

        var recipeType = AccessTools.TypeByName("SpellRecipeSO");
        var listType = AccessTools.TypeByName("UISpellRecipeList");
        var selectedField = listType?.GetField(
            "selectedSpellRecipe", BindingFlags.Instance | BindingFlags.Public);
        var clickRecipe = listType?.GetMethod(
            "ClickRecipe",
            BindingFlags.Instance | BindingFlags.Public,
            binder: null,
            types: recipeType is null ? Type.EmptyTypes : new[] { recipeType },
            modifiers: null);
        var isItem = recipeType is null
            ? null
            : selectedField?.FieldType.GetMethod(
                "IsItem",
                BindingFlags.Instance | BindingFlags.Public,
                binder: null,
                types: new[] { recipeType },
                modifiers: null);
        if (recipeType is null || listType is null || selectedField is null || clickRecipe is null ||
            clickRecipe.ReturnType != typeof(void) || isItem is null ||
            isItem.ReturnType != typeof(bool))
        {
            return _gadgetResults.Rejected(
                "native_spell_navigation_unavailable",
                "This build does not expose the spell list, so a spell cannot be selected.");
        }

        var recipe = TypedRegistryResolver.Shared.Resolve(stableUuid, recipeType);
        if (!recipe.IsResolved)
        {
            return _gadgetResults.Rejected(
                "native_spell_not_resolved",
                "No spell in this run carries that id; world_list category=spell-recipes names " +
                "the ones it does.");
        }

        var lists = Resources.FindObjectsOfTypeAll(listType)
            .OfType<MonoBehaviour>()
            .Where(list =>
                list.enabled && list.gameObject.activeInHierarchy &&
                GameMcpTooltipNativeAccess.OnScreen(list) &&
                selectedField.GetValue(list) is not null)
            .ToArray();
        if (lists.Length != 1)
        {
            return _gadgetResults.Rejected(
                "native_spell_list_unavailable",
                "The game is showing " + (lists.Length == 0 ? "no spell list" : "more than " +
                "one spell list") + " right now, so which spell card was meant is unclear.");
        }

        var selection = selectedField.GetValue(lists[0]);
        var arguments = new[] { recipe.Value };
        if (isItem.Invoke(selection, arguments) is not true)
            clickRecipe.Invoke(lists[0], arguments);
        if (isItem.Invoke(selection, arguments) is not true)
        {
            return _gadgetResults.Failed(
                "verification_failed",
                "The spell was clicked and its card did not open, so the suite cannot say what " +
                "the Spells page shows.");
        }
        return _gadgetResults.Committed(
            "navigation_invoked",
            new GameMcpObjectBuilder
            {
                ["spellUuid"] = stableUuid.ToString("D"),
                ["nativeMethod"] = "UISpellRecipeList.ClickRecipe",
                ["sceneBefore"] = scene,
            });
    }

    private GameMcpCommandResult NavigateExactPlot(
        Guid stableUuid,
        string scene)
    {
        if (scene != "Main")
        {
            return _gadgetResults.Rejected(
                "wrong_scene",
                "Plots can only be picked while a save is open.");
        }

        const string plotNativeType = "PlotNodeSO";
        var plotType = AccessTools.TypeByName(plotNativeType);
        var listType = AccessTools.TypeByName("UIPlotNodeList");
        if (plotType is null || listType is null)
        {
            return _gadgetResults.Rejected(
                "native_plot_navigation_unavailable",
                "This build does not expose the plot list, so a plot cannot be selected.");
        }

        var plot = TypedRegistryResolver.Shared.Resolve(stableUuid, plotType);
        if (!plot.IsResolved)
        {
            return _gadgetResults.Rejected(
                "native_plot_not_resolved",
                "No plot in this run carries that id; page World > Agromancy for the plots it " +
                "does draw.");
        }

        var activeLists = Resources.FindObjectsOfTypeAll(listType)
            .OfType<MonoBehaviour>()
            .Where(list => list.enabled && list.gameObject.activeInHierarchy)
            .ToArray();
        if (activeLists.Length != 1)
        {
            return _gadgetResults.Rejected(
                "native_plot_list_unavailable",
                "The game is showing " + (activeLists.Length == 0 ? "no plot list" : "more than " +
                "one plot list") + " right now, so which plot was meant is unclear.");
        }

        var onNodeClick = listType.GetMethod(
            "OnNodeClick",
            BindingFlags.Instance | BindingFlags.Public,
            binder: null,
            types: new[] { plotType },
            modifiers: null);
        if (onNodeClick is null || onNodeClick.ReturnType != typeof(void))
        {
            return _gadgetResults.Rejected(
                "native_plot_navigation_unavailable",
                "This build does not expose the plot list, so a plot cannot be selected.");
        }

        onNodeClick.Invoke(activeLists[0], new[] { plot.Value });
        return _gadgetResults.Committed(
            "navigation_invoked",
            new GameMcpObjectBuilder
            {
                ["plotUuid"] = stableUuid.ToString("D"),
                ["nativeMethod"] = "UIPlotNodeList.OnNodeClick",
                ["sceneBefore"] = scene,
            });
    }

    internal IReadOnlyList<GameMcpSubtab> CaptureSubtabs()
    {
        var uiShell = _uiShell();
        if (uiShell is null || !uiShell.IsAlive)
            return Array.Empty<GameMcpSubtab>();
        if (uiShell.IsOpenForGameMcp)
        {
            var pages = uiShell.CapturePagesForGameMcp();
            var suiteResult = new GameMcpSubtab[pages.Count];
            for (var index = 0; index < pages.Count; index++)
            {
                var pageIndex = index;
                suiteResult[index] = new GameMcpSubtab(
                    index,
                    pages[index],
                    "Mods/Page[" + index + "]",
                    "Mods",
                    index == uiShell.SelectedPageIndexForGameMcp,
                    () => uiShell.TrySelectPageForGameMcp(pageIndex, out var reason)
                        ? string.Empty
                        : reason);
            }
            return suiteResult;
        }
        var viewRadioType = AccessTools.TypeByName("UIViewRadioButton");
        if (viewRadioType is null) return Array.Empty<GameMcpSubtab>();
        var candidates = Resources.FindObjectsOfTypeAll(viewRadioType)
            .OfType<Component>()
            .Where(component =>
                component.gameObject.activeInHierarchy &&
                // Liveness is not membership. This game leaves a departed screen's strip alive in
                // the hierarchy, so the same predicate the tooltip catalog uses asks the game
                // whether the group this button hangs on is the one being drawn — otherwise Time's
                // strip publishes itself on Magic, and settlement, which is built from this very
                // capture, finds a stale strip perfectly stable and declares arrival on it.
                GameMcpTooltipNativeAccess.OnScreen(component) &&
                !uiShell.IsNativeTabForGameMcp(component))
            .Select(component => new
            {
                Component = component,
                Button = component.GetComponent<Button>(),
                Label = component.GetComponentInChildren<TextMeshProUGUI>(includeInactive: true),
                Path = NativeObjectPath.BuildIndexed(component),
            })
            .Where(candidate =>
                candidate.Button is not null &&
                candidate.Button.enabled &&
                candidate.Button.interactable &&
                candidate.Label is not null &&
                GameMcpGadgetPolicy.IsCurrentContentSubtabPath(candidate.Path))
            .OrderBy(candidate => candidate.Path, StringComparer.Ordinal)
            .ToArray();
        var result = new GameMcpSubtab[candidates.Length];
        for (var index = 0; index < candidates.Length; index++)
        {
            var candidate = candidates[index];
            result[index] = new GameMcpSubtab(
                index,
                candidate.Label!.text?.Trim() ?? string.Empty,
                candidate.Path,
                ParentPath(candidate.Path),
                NativeObjectLiveness.IsAlive(NativeViewAdapter.ReadView(candidate.Component)) &&
                    NativeViewAdapter.IsActive(NativeViewAdapter.ReadView(candidate.Component)!),
                () =>
                {
                    candidate.Button!.onClick.Invoke();
                    return string.Empty;
                });
        }
        return result;
    }

    private static string ParentPath(string path)
    {
        var separator = path.LastIndexOf('/');
        return separator <= 0 ? path : path.Substring(0, separator);
    }

    private static bool TryResolveTabSelector(
        GameMcpNavigationSelector? selector,
        IReadOnlyList<GameMcpNativeTab> entries,
        out GameMcpNativeTab selected,
        out string reason)
    {
        if (selector is null)
        {
            selected = default;
            reason = "tab selector is absent";
            return false;
        }
        if (selector.Label.Length > 0)
        {
            var requested = selector.Label;
            var matches = new List<GameMcpNativeTab>();
            for (var index = 0; index < entries.Count; index++)
                if (string.Equals(entries[index].Label, requested, StringComparison.Ordinal))
                    matches.Add(entries[index]);
            if (matches.Count == 1)
            {
                selected = matches[0];
                reason = string.Empty;
                return true;
            }
            selected = default;
            reason = "exact tab name '" + requested + "' matched " +
                matches.Count + " live catalog entries";
            return false;
        }
        selected = default;
        reason = "tab name is empty";
        return false;
    }

    private static bool TryResolveSubtabSelector(
        string requested,
        IReadOnlyList<GameMcpSubtab> entries,
        out GameMcpSubtab selected,
        out string reason)
    {
        var matches = new List<GameMcpSubtab>();
        if (requested.Length > 0)
        {
            for (var index = 0; index < entries.Count; index++)
                if (string.Equals(entries[index].Label, requested, StringComparison.Ordinal))
                    matches.Add(entries[index]);
            reason = "exact subtab name '" + requested + "'";
        }
        else
        {
            selected = null!;
            reason = "subtab name is empty";
            return false;
        }
        if (matches.Count == 1)
        {
            selected = matches[0];
            reason = string.Empty;
            return true;
        }
        selected = null!;
        reason += " matched " + matches.Count + " live catalog entries";
        return false;
    }

    /// <summary>
    /// A navigate that reached the requested screen and then failed on its subtab moved the board.
    /// The sentence says so, because a bare "refused" reads as "nothing happened" and the caller's
    /// next read finds a screen it did not ask to be on.
    /// </summary>
    private static string SubtabRefusalReason(
        string subtabReason,
        bool settled,
        string? path = null)
    {
        var arrived = settled
            ? "The requested screen is now active"
            : "The requested screen was selected but did not settle within one second";
        var onTheWay = path is null ? string.Empty : " on the way to " + path;
        return arrived + " and was not left; " + subtabReason + onTheWay + ".";
    }

    private GameMcpCommandResult NavigationRefusal(
        string code,
        string reason,
        GameMcpObjectBuilder? state,
        string candidateField,
        IEnumerable<string> candidates)
    {
        var values = new GameMcpArrayBuilder();
        foreach (var candidate in candidates) values.Add(candidate);
        var details = new GameMcpObjectBuilder();
        if (state is not null) details.CopyFrom(state);
        if (values.Count > 0) details[candidateField] = values;
        return _gadgetResults.Rejected(code, reason).WithDetails(details.Freeze());
    }

    internal sealed class GameMcpSubtab
    {
        private readonly Func<string> _select;

        internal GameMcpSubtab(
            int index,
            string label,
            string path,
            string stripKey,
            bool active,
            Func<string> select)
        {
            Index = index;
            Label = label ?? string.Empty;
            Path = path ?? string.Empty;
            StripKey = stripKey ?? string.Empty;
            Active = active;
            _select = select ?? throw new ArgumentNullException(nameof(select));
        }

        internal int Index { get; }
        internal string Label { get; }
        internal string Path { get; }
        internal string StripKey { get; }
        internal bool Active { get; }
        internal bool TrySelect(out string reason)
        {
            reason = _select();
            return reason.Length == 0;
        }
    }
}
#endif
