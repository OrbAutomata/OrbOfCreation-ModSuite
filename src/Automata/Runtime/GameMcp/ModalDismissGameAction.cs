#if SERVICE_CYCLE_PROFILE
using System;
using System.Linq.Expressions;
using System.Reflection;
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

    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
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
        catch (Exception exception) when (exception is InvalidOperationException or
            ArgumentException or TargetInvocationException)
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
        catch (Exception exception) when (exception is InvalidOperationException or
            ArgumentException or TargetInvocationException)
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
        catch (Exception exception) when (exception is InvalidOperationException or
            ArgumentException or TargetInvocationException)
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

    private void BindLifecycle()
    {
        try
        {
            Type T(int index, string name)
            {
                Require(index);
                return _resolveType(name) ?? throw new InvalidOperationException(name + " was unavailable");
            }

            Require(0);
            Require(1);
            var resources = typeof(Resources);
            var unityObject = typeof(UnityEngine.Object);
            var modal = T(2, "UIModal");
            var findAll = Method(3, resources, "FindObjectsOfTypeAll", unityObject.MakeArrayType(),
                new[] { typeof(Type) }, isStatic: true);
            var isOpen = Method(4, modal, "IsOpen", typeof(bool), Type.EmptyTypes, isStatic: false);
            var isClosing = Field(5, modal, "isClosing", typeof(bool));
            var graceTime = Field(6, modal, "graceTime", typeof(float));
            var close = Method(7, modal, "CloseModal", typeof(void), Type.EmptyTypes, isStatic: false);
            var title = Field(8, modal, "modalTitle", typeof(TextMeshProUGUI));
            _bindings = new Bindings(modal, FindAll(findAll), BoolMethod(isOpen),
                BoolField(isClosing), FloatField(graceTime), Action(close), Title(title));
            _bindingFailure = string.Empty;
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            _bindings = null;
            _bindingFailure = "Modal close contracts are unavailable: " +
                exception.GetBaseException().Message;
        }
    }

    private void Require(int index)
    {
        if (!_includeContract(ContractIds[index]))
            throw new InvalidOperationException(ContractIds[index] + " was unavailable");
    }

    private MethodInfo Method(
        int index,
        Type owner,
        string name,
        Type result,
        Type[] parameters,
        bool isStatic)
    {
        Require(index);
        var method = owner.GetMethod(name, isStatic ? Static : Instance, null, parameters, null);
        if (method is null || method.IsStatic != isStatic || method.ReturnType != result)
            throw new InvalidOperationException(owner.Name + "." + name + " did not match the audited signature");
        return method;
    }

    private FieldInfo Field(int index, Type owner, string name, Type type)
    {
        Require(index);
        var field = owner.GetField(name, Instance);
        if (field is null || field.IsStatic || field.FieldType != type)
            throw new InvalidOperationException(owner.Name + "." + name + " did not match the audited field");
        return field;
    }

    private static Func<Type, Array> FindAll(MethodInfo method)
    {
        var type = Expression.Parameter(typeof(Type), "type");
        return Expression.Lambda<Func<Type, Array>>(
            Expression.Convert(Expression.Call(method, type), typeof(Array)), type).Compile();
    }

    private static Func<object, bool> BoolMethod(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object), "target");
        return Expression.Lambda<Func<object, bool>>(
            Expression.Call(Expression.Convert(target, method.DeclaringType!), method), target).Compile();
    }

    private static Func<object, bool> BoolField(FieldInfo field)
    {
        var target = Expression.Parameter(typeof(object), "target");
        return Expression.Lambda<Func<object, bool>>(
            Expression.Field(Expression.Convert(target, field.DeclaringType!), field), target).Compile();
    }

    private static Func<object, float> FloatField(FieldInfo field)
    {
        var target = Expression.Parameter(typeof(object), "target");
        return Expression.Lambda<Func<object, float>>(
            Expression.Field(Expression.Convert(target, field.DeclaringType!), field), target).Compile();
    }

    private static Action<object> Action(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object), "target");
        return Expression.Lambda<Action<object>>(
            Expression.Call(Expression.Convert(target, method.DeclaringType!), method), target).Compile();
    }

    private static Func<object, string> Title(FieldInfo field) =>
        modal => field.GetValue(modal) is TextMeshProUGUI label ? label.text ?? string.Empty : string.Empty;

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
