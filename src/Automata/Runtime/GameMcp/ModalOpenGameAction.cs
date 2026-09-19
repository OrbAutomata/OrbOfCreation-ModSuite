#if SERVICE_CYCLE_PROFILE
using System;
using System.Collections.Generic;
using System.Reflection;
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
    internal ModalOpenSubmission(bool committed, string code, string reason, string title = "")
    {
        Committed = committed;
        Code = code ?? string.Empty;
        Reason = reason ?? string.Empty;
        Title = title ?? string.Empty;
    }

    internal bool Committed { get; }
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
    /// The activator type, its prepared flag and its open control are the same members the Back to
    /// Main Menu action already binds, so they are the same contracts; only the authored title is
    /// new here.
    /// </summary>
    internal static readonly string[] ContractIds =
    {
        "return-to-menu.modal-activator.type-action",
        "return-to-menu.activator-modal-created-action",
        "return-to-menu.activator-open-action",
        "modal-open.activator-title-action",
    };

    private const BindingFlags Instance =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

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
                live.Add(new ModalActivator(control, title));
            }
            activators = live;
            return true;
        }
        catch (Exception exception) when (exception is InvalidOperationException or
            ArgumentException or TargetInvocationException)
        {
            reason = "The chrome panel controls could not be read." +
                GameActionFaultLog.Record(exception, "the chrome panel controls");
            return false;
        }
    }

    /// <summary>
    /// Press the one control that opens the named panel. The press is the player's own, and the
    /// panel being up afterwards is the whole of the answer.
    /// </summary>
    internal ModalOpenSubmission Submit(string title, IReadOnlyList<ModalActivator> activators)
    {
        if (activators is null) throw new ArgumentNullException(nameof(activators));
        if (Environment.CurrentManagedThreadId != _mainThreadId)
            return new ModalOpenSubmission(false, "wrong_thread", GameActionAnswer.SuiteStopped());
        if (_bindings is not { } native)
            return new ModalOpenSubmission(false, "contract_unavailable", NoActivator);

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
        try
        {
            native.Open(matches[0].Control);
            return new ModalOpenSubmission(true, "committed", string.Empty, matches[0].Title);
        }
        catch (Exception exception) when (exception is InvalidOperationException or
            ArgumentException or TargetInvocationException)
        {
            return new ModalOpenSubmission(false, "contract_unavailable",
                "The chrome panel control failed." +
                GameActionFaultLog.Record(exception, "the chrome panel controls"));
        }
    }

    /// <summary>The panels this screen does offer, because a refused name is a name to correct.</summary>
    private static string Offered(IReadOnlyList<ModalActivator> activators)
    {
        if (activators.Count == 0) return "; this screen offers none";
        var names = new string[activators.Count];
        for (var index = 0; index < activators.Count; index++) names[index] = activators[index].Title;
        return "; this screen offers " + string.Join(", ", names);
    }

    public void Dispose() => _bindings = null;

    internal void InvalidateLifecycle()
    {
        _bindings = null;
        _bindingFailure = string.Empty;
        Bind();
    }

    private void Bind()
    {
        try
        {
            Require(0);
            var activator = _resolveType("UIModalActivator") ??
                throw new InvalidOperationException("UIModalActivator was unavailable");
            var prepared = Field(1, activator, "modalCreated", typeof(bool));
            var open = Method(2, activator, "OpenModal");
            var title = Field(3, activator, "modalTitle", typeof(string));
            _bindings = new Bindings(
                activator,
                instance => prepared.GetValue(instance) as bool? == true,
                instance => title.GetValue(instance) as string ?? string.Empty,
                instance => open.Invoke(instance, null));
            _bindingFailure = string.Empty;
        }
        catch (Exception exception) when (exception is InvalidOperationException or
            ArgumentException or AmbiguousMatchException)
        {
            _bindings = null;
            _bindingFailure = "Panel open contracts are unavailable: " +
                exception.GetBaseException().Message;
        }
    }

    private void Require(int index)
    {
        if (!_includeContract(ContractIds[index]))
            throw new InvalidOperationException(ContractIds[index] + " was unavailable");
    }

    private FieldInfo Field(int index, Type owner, string name, Type type)
    {
        Require(index);
        var field = owner.GetField(name, Instance);
        if (field is null || field.FieldType != type)
            throw new InvalidOperationException(
                owner.Name + "." + name + " did not match the audited signature");
        return field;
    }

    private MethodInfo Method(int index, Type owner, string name)
    {
        Require(index);
        var method = owner.GetMethod(name, Instance, null, Type.EmptyTypes, null);
        if (method is null || method.IsStatic || method.ReturnType != typeof(void))
            throw new InvalidOperationException(
                owner.Name + "." + name + " did not match the audited signature");
        return method;
    }

    private sealed class Bindings
    {
        internal Bindings(
            Type activatorType,
            Func<object, bool> prepared,
            Func<object, string> title,
            Action<object> open)
        {
            ActivatorType = activatorType;
            Prepared = prepared;
            Title = title;
            Open = open;
        }

        internal Type ActivatorType { get; }
        internal Func<object, bool> Prepared { get; }
        internal Func<object, string> Title { get; }
        internal Action<object> Open { get; }
    }
}
#endif
