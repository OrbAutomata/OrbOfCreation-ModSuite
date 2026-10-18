#if SERVICE_CYCLE_PROFILE
using System;
using System.Collections.Generic;
using OrbModding.Common;
using UnityEngine;

namespace OrbAutomata.GameMcp;

/// <summary>One chrome control and the panel its press puts on the screen.</summary>
internal readonly struct ModalActivator
{
    internal ModalActivator(Component control, string title)
    {
        Control = control;
        Title = title;
    }

    internal Component Control { get; }

    /// <summary>The name the panel wears once it is open, which is what a caller asks for.</summary>
    internal string Title { get; }
}

internal readonly struct ModalOpenSubmission
{
    internal ModalOpenSubmission(
        bool committed, string code, string reason, string title = "", bool faulted = false)
    {
        Committed = committed;
        Code = code ?? string.Empty;
        Reason = reason ?? string.Empty;
        Title = title ?? string.Empty;
        Faulted = faulted;
    }

    internal bool Committed { get; }

    /// <summary>
    /// The control was pressed and the panel did not read back as open, so whether it landed is
    /// unknown.
    /// </summary>
    internal bool Faulted { get; }
    internal string Code { get; }
    internal string Reason { get; }

    /// <summary>The panel the press was aimed at, so the answer can name what it put up.</summary>
    internal string Title { get; }
}

/// <summary>
/// Lifecycle-scoped Unity-main-thread boundary for the chrome controls that put a panel on the
/// screen.
/// </summary>
/// <remarks>
/// <para>
/// The top-right icons — Player, the settings panel, the achievement list — are not screens.
/// Each is a <c>UIModalActivator</c> holding a title and a prepared <c>UIModal</c>, and the game
/// wires its button to <c>ToggleModal</c>. Toggling is state-dependent, so the press the suite
/// makes is <c>OpenModal</c>: the same modal, the same authored content, and the same answer
/// whether the panel was already up.
/// </para>
/// <para>
/// The activator carries no <c>HoverTooltip</c>, which is why the element walker never saw these
/// controls; they reach the wire through <see cref="TryReadActivators"/> instead, addressed by the
/// title their panel shows rather than by a path that changes with the screen.
/// </para>
/// </remarks>
internal sealed class ModalOpenGameAction : IDisposable
{
    /// <summary>
    /// The activator type, its prepared flag, its open control, its prepared panel and the panel's
    /// open flag are the same members the Back to Main Menu action already binds, so they are the
    /// same contracts; only the authored title is new here.
    /// </summary>
    internal static readonly string[] ContractIds =
    {
        "return-to-menu.modal-activator.type-action",
        "return-to-menu.activator-modal-created-action",
        "return-to-menu.activator-open-action",
        "return-to-menu.modal.type-action",
        "return-to-menu.activator-created-modal-action",
        "return-to-menu.modal-open-action",
        "modal-open.activator-title-action",
    };

    private readonly Func<string, Type?> _resolveType;
    private readonly Func<string, bool> _includeContract;
    private readonly int _mainThreadId;
    private Bindings? _bindings;
    private string _bindingFailure = string.Empty;

    internal ModalOpenGameAction(
        Func<string, Type?>? resolveType = null,
        Func<string, bool>? includeContract = null)
    {
        _resolveType = resolveType ?? ReflectionUtil.FindLoadedType;
        _includeContract = includeContract ?? (_ => true);
        _mainThreadId = Environment.CurrentManagedThreadId;
        Bind();
    }

    internal bool BindingsAvailable => _bindings is not null;
    internal string BindingFailure => _bindingFailure;

    /// <summary>
    /// What a caller can do about a build whose chrome exposes no panel controls: nothing, and the
    /// sentence says so rather than naming the reflection that would not bind.
    /// </summary>
    internal const string NoActivator =
        "This build does not expose the controls that put a panel on the screen, so no panel can " +
        "be opened.";

    /// <summary>
    /// The panels the suite presses, by the title each one wears.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Closing a Unity modal never deactivates it — <c>UIModal.SetElementVisibility(false)</c> drops
    /// alpha, interactivity and raycasts and leaves the GameObject active — so every activator
    /// nested inside a panel the player closed an hour ago is still active, still prepared and still
    /// titled. The enumeration was scene-wide while the answer claimed to be about this screen, and
    /// it offered Game Complete!, Reset World, Dev Console, Loadouts, Jukebox, Key Binds and
    /// Construct Aspect on every screen in the game.
    /// </para>
    /// <para>
    /// <see cref="GameMcpTooltipNativeAccess.OnScreen"/> drops the ones inside closed panels. What
    /// no filter can answer is what an open <em>does</em>: <c>UIModal.PerformOpen</c> invokes two
    /// scene-wired <c>UnityEvent</c>s that no read of the build can follow. Reset World is the front
    /// door of a one-way door, Game Complete! an ending surface, and Dev Console fronts
    /// <c>DevConsoleEngine.RunCommand</c>. So the set the suite is willing to press is named here
    /// rather than inferred, and a panel that is not on it is never listed and never opened.
    /// </para>
    /// </remarks>
    internal static readonly string[] Offerable = { "Player", "Settings" };

    /// <summary>
    /// Every chrome control the player can currently press, with the panel each one opens.
    /// </summary>
    internal bool TryReadActivators(out IReadOnlyList<ModalActivator> activators, out string reason)
    {
        activators = Array.Empty<ModalActivator>();
        reason = string.Empty;
        if (Environment.CurrentManagedThreadId != _mainThreadId)
        {
            reason = GameActionAnswer.SuiteStopped();
            return false;
        }
        if (_bindings is not { } native)
        {
            reason = NoActivator;
            return false;
        }
        try
        {
            var live = new List<ModalActivator>();
            foreach (var value in Resources.FindObjectsOfTypeAll(native.ActivatorType))
            {
                if (value is not Component control ||
                    control.GetType() != native.ActivatorType ||
                    !control.gameObject.activeInHierarchy ||
                    !native.Prepared(control))
                {
                    continue;
                }
                var title = native.Title(control);
                if (string.IsNullOrWhiteSpace(title)) continue;
                if (!GameMcpTooltipNativeAccess.OnScreen(control)) continue;
                if (!IsOfferable(title)) continue;
                live.Add(new ModalActivator(control, title));
            }
            activators = live;
            return true;
        }
        catch (Exception exception) when (NativeFailureClassifier.IsExpected(exception))
        {
            reason = "The chrome panel controls could not be read." +
                GameActionFaultLog.Record(exception, "the chrome panel controls");
            return false;
        }
    }

    /// <summary>
    /// Press the one control that opens the named panel. The press is the player's own, and the
    /// control's own panel reading open afterwards is the whole of the answer.
    /// </summary>
    internal ModalOpenSubmission Submit(string title, IReadOnlyList<ModalActivator> activators)
    {
        if (activators is null) throw new ArgumentNullException(nameof(activators));
        if (Environment.CurrentManagedThreadId != _mainThreadId)
            return new ModalOpenSubmission(false, "wrong_thread", GameActionAnswer.SuiteStopped());
        if (_bindings is not { } native)
            return new ModalOpenSubmission(false, "contract_unavailable", NoActivator);

        if (!IsOfferable(title))
        {
            return new ModalOpenSubmission(false, "modal_not_offered",
                "The suite does not open the '" + title + "' panel; it opens only the player's " +
                "own chrome: " + string.Join(", ", Offerable) + ".");
        }

        var matches = new List<ModalActivator>();
        foreach (var candidate in activators)
            if (string.Equals(candidate.Title, title, StringComparison.Ordinal))
                matches.Add(candidate);
        if (matches.Count == 0)
        {
            return new ModalOpenSubmission(false, "no_modal_named",
                "No control on this screen opens a panel called '" + title + "'" +
                Offered(activators) + ".");
        }
        if (matches.Count > 1)
        {
            return new ModalOpenSubmission(false, "ambiguous_modal",
                matches.Count + " controls on this screen open a panel called '" + title +
                "', so which one was meant is unclear.");
        }
        var control = matches[0].Control;
        var panel = "the " + title + " panel";
        object? modal;
        try
        {
            modal = native.CreatedModal(control);
        }
        catch (Exception exception) when (NativeFailureClassifier.IsExpected(exception))
        {
            return new ModalOpenSubmission(false, "contract_unavailable",
                GameActionAnswer.CouldNotRead(panel, exception));
        }
        if (modal is null)
            return new ModalOpenSubmission(false, "contract_unavailable", GameActionAnswer.CouldNotRead(panel));

        var evidence = NativeMutationVerifier.ExecuteAfterCapture(
            "Panel open", panel, "the panel is open", false,
            () => native.IsOpen(modal), () => native.Open(control), (_, open) => open);
        if (evidence.IsVerified)
            return new ModalOpenSubmission(true, "committed", string.Empty, title);
        return evidence.Exception is { } failure
            ? new ModalOpenSubmission(false, "post_commit_fault",
                GameActionAnswer.GameErrored(panel, failure), faulted: true)
            : new ModalOpenSubmission(false, "verification_failed",
                GameActionAnswer.ChangeNotSeen(panel), faulted: true);
    }

    private static bool IsOfferable(string title) =>
        Array.IndexOf(Offerable, title) >= 0;

    /// <summary>The panels this screen does offer, because a refused name is a name to correct.</summary>
    private static string Offered(IReadOnlyList<ModalActivator> activators)
    {
        if (activators.Count == 0) return "; this screen offers none";
        var names = new string[activators.Count];
        for (var index = 0; index < activators.Count; index++) names[index] = activators[index].Title;
        return "; this screen offers " + string.Join(", ", names);
    }

    public void Dispose()
    {
        _bindings = null;
        _bindingFailure = string.Empty;
    }

    internal void InvalidateLifecycle()
    {
        _bindings = null;
        _bindingFailure = string.Empty;
        Bind();
    }

    private void Bind()
    {
        NativeBindingSet.TryBind<Bindings>("panel open", ContractIds, _resolveType, _includeContract, set =>
        {
            var activator = set.Type("return-to-menu.modal-activator.type-action", "UIModalActivator");
            var modal = set.Type("return-to-menu.modal.type-action", "UIModal");
            var title = set.Field<Func<object, string?>>(
                "modal-open.activator-title-action", activator, "modalTitle", typeof(string));
            var createdModal = set.Field<Func<object, object?>>(
                "return-to-menu.activator-created-modal-action", activator, "createdModal", modal);
            return new Bindings(
                activator,
                set.Field<Func<object, bool>>(
                    "return-to-menu.activator-modal-created-action", activator, "modalCreated", typeof(bool)),
                instance => title(instance) ?? string.Empty,
                set.Call<Action<object>>(
                    "return-to-menu.activator-open-action", activator, "OpenModal", typeof(void)),
                instance => createdModal(instance) is var created && NativeObjectLiveness.IsAlive(created)
                    ? created
                    : null,
                set.Call<Func<object, bool>>("return-to-menu.modal-open-action", modal, "IsOpen", typeof(bool)));
        }, out _bindings, out _bindingFailure);
    }

    private sealed class Bindings
    {
        internal Bindings(
            Type activatorType,
            Func<object, bool> prepared,
            Func<object, string> title,
            Action<object> open,
            Func<object, object?> createdModal,
            Func<object, bool> isOpen)
        {
            ActivatorType = activatorType;
            Prepared = prepared;
            Title = title;
            Open = open;
            CreatedModal = createdModal;
            IsOpen = isOpen;
        }

        internal Type ActivatorType { get; }
        internal Func<object, bool> Prepared { get; }
        internal Func<object, string> Title { get; }
        internal Action<object> Open { get; }
        internal Func<object, object?> CreatedModal { get; }
        internal Func<object, bool> IsOpen { get; }
    }
}
#endif
