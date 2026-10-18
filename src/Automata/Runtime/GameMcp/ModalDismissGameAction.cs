#if SERVICE_CYCLE_PROFILE
using System;
using OrbModding.Common;
using TMPro;
using UnityEngine;

namespace OrbAutomata.GameMcp;

internal readonly struct ModalDismissSubmission
{
    internal ModalDismissSubmission(bool committed, string code, string reason, string title = "")
    {
        Committed = committed;
        Code = code ?? string.Empty;
        Reason = reason ?? string.Empty;
        Title = title ?? string.Empty;
    }

    internal bool Committed { get; }
    internal string Code { get; }
    internal string Reason { get; }

    /// <summary>
    /// The title the closing modal was showing, read before the close so the answer can name what
    /// it shut. A dismissed modal has no title left to read.
    /// </summary>
    internal string Title { get; }
}

/// <summary>Lifecycle-scoped Unity-main-thread boundary for the visible native modal close control.</summary>
internal sealed class ModalDismissGameAction : IDisposable
{
    internal static readonly string[] ContractIds =
    {
        "modal-dismiss.resources.type-action",
        "modal-dismiss.unity-object.type-action",
        "modal-dismiss.modal.type-action",
        "modal-dismiss.find-all-action",
        "modal-dismiss.modal-open-action",
        "modal-dismiss.modal-closing-action",
        "modal-dismiss.modal-grace-action",
        "modal-dismiss.modal-close-action",
        "modal-dismiss.modal-title-action",
    };

    private readonly Func<long> _readLifecycleEpoch;
    private readonly Func<string, Type?> _resolveType;
    private readonly Func<string, bool> _includeContract;
    private readonly int _mainThreadId;
    private Bindings? _bindings;
    private object? _pendingModal;
    private long _pendingEpoch;
    private string _bindingFailure = string.Empty;

    internal ModalDismissGameAction(
        Func<long> readLifecycleEpoch,
        Func<string, Type?>? resolveType = null,
        Func<string, bool>? includeContract = null)
    {
        _readLifecycleEpoch = readLifecycleEpoch ?? throw new ArgumentNullException(nameof(readLifecycleEpoch));
        _resolveType = resolveType ?? ReflectionUtil.FindLoadedType;
        _includeContract = includeContract ?? (_ => true);
        _mainThreadId = Environment.CurrentManagedThreadId;
        BindLifecycle();
    }

    internal bool BindingsAvailable => _bindings is not null;
    internal string BindingFailure => _bindingFailure;

    /// <summary>
    /// What a caller can do about a build whose modal has no close control: nothing, and the
    /// sentence says so rather than naming the reflection that would not bind.
    /// </summary>
    private const string NoCloseButton =
        "This build does not expose a modal close button, so no modal can be dismissed.";

    /// <summary>
    /// Dismissal is a screen action with no entity to name, so the caller has no lifecycle to
    /// submit. The boundary reads the live lifecycle itself and pins it for the settled read.
    /// </summary>
    internal ModalDismissSubmission Submit()
    {
        if (Environment.CurrentManagedThreadId != _mainThreadId)
            return Refused("wrong_thread", GameActionAnswer.SuiteStopped());
        if (_bindings is not { } native)
            return Refused("contract_unavailable", NoCloseButton);

        try
        {
            object? candidate = null;
            var openCount = 0;
            foreach (var value in native.FindAll(native.ModalType))
            {
                if (value is null || value.GetType() != native.ModalType || !native.IsOpen(value))
                    continue;
                candidate = value;
                openCount++;
            }
            if (openCount == 0)
                return Refused("no_open_modal", "There is no open modal to dismiss.");
            if (openCount != 1)
                return Refused("multiple_modals_open",
                    "More than one modal is open, so no single close control is unambiguous.");
            if (native.IsClosing(candidate!))
                return Refused("modal_already_closing", "The open modal is already closing.");
            if (native.GraceTime(candidate!) > 0f)
                return Refused("modal_close_not_ready", "The modal close control is not ready yet.");

            var title = native.Title(candidate!);
            native.Close(candidate!);
            if (!native.IsClosing(candidate!))
                return Refused("requested_state_not_reached", "The modal did not begin closing.");
            _pendingModal = candidate;
            _pendingEpoch = _readLifecycleEpoch();
            return new ModalDismissSubmission(true, "committed", string.Empty, title);
        }
        catch (Exception exception) when (NativeFailureClassifier.IsExpected(exception))
        {
            return Refused("contract_unavailable",
                "The modal close control failed." +
                GameActionFaultLog.Record(exception, "the open modal"));
        }
    }

    internal bool TryObserveDismissed(out bool dismissed, out string reason)
    {
        dismissed = false;
        reason = string.Empty;
        if (Environment.CurrentManagedThreadId != _mainThreadId || _bindings is not { } native ||
            _readLifecycleEpoch() != _pendingEpoch)
        {
            reason = "The modal lifecycle changed before dismissal settled.";
            return false;
        }
        try
        {
            var target = _pendingModal;
            if (target is null || target.GetType() != native.ModalType)
            {
                reason = "The modal selected for dismissal is no longer available.";
                return false;
            }
            dismissed = !native.IsOpen(target);
            if (dismissed) _pendingModal = null;
            return true;
        }
        catch (Exception exception) when (NativeFailureClassifier.IsExpected(exception))
        {
            reason = "The modal settled state could not be read." +
                GameActionFaultLog.Record(exception, "the open modal");
            return false;
        }
    }

    /// <summary>
    /// The titles the game is showing on its open modals, so a screen read tells the caller what
    /// is covering the board instead of leaving it to be discovered by a refused action.
    /// </summary>
    internal bool TryReadOpenModals(out string[] titles, out string reason)
    {
        titles = Array.Empty<string>();
        reason = string.Empty;
        if (Environment.CurrentManagedThreadId != _mainThreadId)
        {
            reason = GameActionAnswer.SuiteStopped();
            return false;
        }
        if (_bindings is not { } native)
        {
            reason = NoCloseButton;
            return false;
        }
        try
        {
            var open = new System.Collections.Generic.List<string>();
            foreach (var value in native.FindAll(native.ModalType))
            {
                if (value is null || value.GetType() != native.ModalType || !native.IsOpen(value))
                    continue;
                open.Add(native.Title(value));
            }
            titles = open.ToArray();
            return true;
        }
        catch (Exception exception) when (NativeFailureClassifier.IsExpected(exception))
        {
            reason = "The open modal titles could not be read." +
                GameActionFaultLog.Record(exception, "the open modal");
            return false;
        }
    }

    internal void InvalidateLifecycle()
    {
        _bindings = null;
        _pendingModal = null;
        _bindingFailure = string.Empty;
        BindLifecycle();
    }

    public void Dispose()
    {
        _bindings = null;
        _pendingModal = null;
        _bindingFailure = string.Empty;
    }

    private void BindLifecycle() =>
        NativeBindingSet.TryBind<Bindings>("modal close", ContractIds, _resolveType, _includeContract, set =>
        {
            var resources = set.Type("modal-dismiss.resources.type-action", typeof(Resources));
            var unityObject = set.Type("modal-dismiss.unity-object.type-action", typeof(UnityEngine.Object));
            var modal = set.Type("modal-dismiss.modal.type-action", "UIModal");
            var title = set.Field<Func<object, TextMeshProUGUI?>>(
                "modal-dismiss.modal-title-action", modal, "modalTitle", typeof(TextMeshProUGUI));
            return new Bindings(
                modal,
                set.StaticCall<Func<Type, Array>>(
                    "modal-dismiss.find-all-action",
                    resources, "FindObjectsOfTypeAll", unityObject.MakeArrayType(), typeof(Type)),
                set.Call<Func<object, bool>>("modal-dismiss.modal-open-action", modal, "IsOpen", typeof(bool)),
                set.Field<Func<object, bool>>(
                    "modal-dismiss.modal-closing-action", modal, "isClosing", typeof(bool)),
                set.Field<Func<object, float>>(
                    "modal-dismiss.modal-grace-action", modal, "graceTime", typeof(float)),
                set.Call<Action<object>>("modal-dismiss.modal-close-action", modal, "CloseModal", typeof(void)),
                instance => title(instance) is { } label ? label.text ?? string.Empty : string.Empty);
        }, out _bindings, out _bindingFailure);

    private static ModalDismissSubmission Refused(string code, string reason) => new(false, code, reason);

    private sealed class Bindings
    {
        internal Bindings(
            Type modalType,
            Func<Type, Array> findAll,
            Func<object, bool> isOpen,
            Func<object, bool> isClosing,
            Func<object, float> graceTime,
            Action<object> close,
            Func<object, string> title)
        {
            ModalType = modalType;
            FindAll = findAll;
            IsOpen = isOpen;
            IsClosing = isClosing;
            GraceTime = graceTime;
            Close = close;
            Title = title;
        }

        internal Type ModalType { get; }
        internal Func<Type, Array> FindAll { get; }
        internal Func<object, bool> IsOpen { get; }
        internal Func<object, bool> IsClosing { get; }
        internal Func<object, float> GraceTime { get; }
        internal Action<object> Close { get; }
        internal Func<object, string> Title { get; }
    }
}
#endif
