using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using OrbModding.Common;

namespace OrbAutomata;

/// <summary>Exact v1.0.5 binding set for the Back to Main Menu button and the panel holding it.</summary>
internal sealed class ReturnToMenuNativeBindings
{
    private const BindingFlags Instance =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags Static =
        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    internal static readonly string[] ContractIds =
    {
        "return-to-menu.button.type-action",
        "return-to-menu.button-action",
        "return-to-menu.screen-flash.type-action",
        "return-to-menu.screen-flash-instance-action",
        "return-to-menu.screen-flash-active-action",
        "return-to-menu.button-component-action",
        "return-to-menu.component-game-object-action",
        "return-to-menu.behaviour-enabled-action",
        "return-to-menu.game-object-active-action",
        "return-to-menu.selectable-interactable-action",
        "return-to-menu.object-name-action",
        "return-to-menu.modal.type-action",
        "return-to-menu.modal-open-action",
        "return-to-menu.modal-activator.type-action",
        "return-to-menu.activator-created-modal-action",
        "return-to-menu.activator-modal-created-action",
        "return-to-menu.activator-button-component-action",
        "return-to-menu.activator-open-action",
        "return-to-menu.component-transform-action",
        "return-to-menu.transform-child-of-action",
    };

    private ReturnToMenuNativeBindings(
        Type buttonType,
        Action<object> backToMenu,
        Func<object?> screenFlash,
        Func<object, bool> flashActive,
        Func<object, bool> controlLive,
        Func<object, string> controlName,
        Type activatorType,
        Func<object, bool> panelPrepared,
        Func<object, object?> panelModal,
        Func<object, bool> panelOpen,
        Func<object, bool> panelControlLive,
        Action<object> openPanel,
        Func<object, object, bool> panelContains)
    {
        ButtonType = buttonType;
        BackToMenu = backToMenu;
        ScreenFlash = screenFlash;
        FlashActive = flashActive;
        ControlLive = controlLive;
        ControlName = controlName;
        ActivatorType = activatorType;
        PanelPrepared = panelPrepared;
        PanelModal = panelModal;
        PanelOpen = panelOpen;
        PanelControlLive = panelControlLive;
        OpenPanel = openPanel;
        PanelContains = panelContains;
    }

    internal Type ButtonType { get; }
    internal Action<object> BackToMenu { get; }
    internal Func<object?> ScreenFlash { get; }
    internal Func<object, bool> FlashActive { get; }
    internal Func<object, bool> ControlLive { get; }
    internal Func<object, string> ControlName { get; }

    /// <summary>The button component the player clicks to raise a panel, and that panel's state.</summary>
    internal Type ActivatorType { get; }
    internal Func<object, bool> PanelPrepared { get; }
    internal Func<object, object?> PanelModal { get; }
    internal Func<object, bool> PanelOpen { get; }
    internal Func<object, bool> PanelControlLive { get; }
    internal Action<object> OpenPanel { get; }
    internal Func<object, object, bool> PanelContains { get; }

    internal static bool TryCreate(
        out ReturnToMenuNativeBindings? bindings,
        out string reason,
        Func<string, Type?>? resolveType = null,
        Func<string, bool>? includeContract = null)
    {
        bindings = null;
        resolveType ??= ReflectionUtil.FindLoadedType;
        includeContract ??= static _ => true;
        try
        {
            Require(0, includeContract);
            var button = resolveType("UIBackToMenuButton") ??
                throw new InvalidOperationException("UIBackToMenuButton was unavailable");
            var backToMenu = Method(1, button, "BackToMenu", typeof(void), includeContract);
            Require(2, includeContract);
            var flash = resolveType("UIScreenFlash") ??
                throw new InvalidOperationException("UIScreenFlash was unavailable");
            var instance = Field(3, flash, "instance", flash, true, includeContract);
            var active = Field(4, flash, "isActive", typeof(bool), false, includeContract);
            var controlButton = Field(5, button, "button", typeof(UnityEngine.UI.Button),
                false, includeContract);
            var gameObject = Method(6, typeof(UnityEngine.Component), "get_gameObject",
                typeof(UnityEngine.GameObject), includeContract);
            var enabled = Method(7, typeof(UnityEngine.Behaviour), "get_enabled",
                typeof(bool), includeContract);
            var activeInHierarchy = Method(8, typeof(UnityEngine.GameObject),
                "get_activeInHierarchy", typeof(bool), includeContract);
            var interactable = Method(9, typeof(UnityEngine.UI.Selectable), "IsInteractable",
                typeof(bool), includeContract);
            var objectName = Method(10, typeof(UnityEngine.Object), "get_name",
                typeof(string), includeContract);
            Require(11, includeContract);
            var modal = resolveType("UIModal") ??
                throw new InvalidOperationException("UIModal was unavailable");
            var modalOpen = Method(12, modal, "IsOpen", typeof(bool), includeContract);
            Require(13, includeContract);
            var activator = resolveType("UIModalActivator") ??
                throw new InvalidOperationException("UIModalActivator was unavailable");
            var createdModal = Field(14, activator, "createdModal", modal, false, includeContract);
            var modalPrepared = Field(15, activator, "modalCreated", typeof(bool), false,
                includeContract);
            var activatorButton = Field(16, activator, "button",
                typeof(UnityEngine.UI.Button), false, includeContract);
            var openModal = Method(17, activator, "OpenModal", typeof(void), includeContract);
            var componentTransform = Method(18, typeof(UnityEngine.Component), "get_transform",
                typeof(UnityEngine.Transform), includeContract);
            var childOf = Method(19, typeof(UnityEngine.Transform), "IsChildOf", typeof(bool),
                new[] { typeof(UnityEngine.Transform) }, includeContract);
            bindings = new ReturnToMenuNativeBindings(
                button,
                Action(backToMenu),
                StaticObject(instance),
                FieldFunc<bool>(active),
                LiveControl(controlButton, gameObject, enabled, activeInHierarchy, interactable),
                StringMethod(objectName),
                activator,
                FieldFunc<bool>(modalPrepared),
                UnityObjectField(createdModal),
                BoolMethod(modalOpen),
                LiveControl(activatorButton, gameObject, enabled, activeInHierarchy, interactable),
                Action(openModal),
                Contains(componentTransform, childOf));
            reason = string.Empty;
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or
            InvalidOperationException or AmbiguousMatchException or NotSupportedException)
        {
            reason = "Back to Main Menu contracts are unavailable: " +
                exception.GetBaseException().Message;
            return false;
        }
    }

    private static void Require(int index, Func<string, bool> include)
    {
        if (!include(ContractIds[index]))
            throw new InvalidOperationException(ContractIds[index] + " was unavailable");
    }

    private static MethodInfo Method(
        int index,
        Type owner,
        string name,
        Type result,
        Func<string, bool> include) =>
        Method(index, owner, name, result, Type.EmptyTypes, include);

    private static MethodInfo Method(
        int index,
        Type owner,
        string name,
        Type result,
        Type[] parameters,
        Func<string, bool> include)
    {
        Require(index, include);
        var method = owner.GetMethod(name, Instance, null, parameters, null);
        if (method is null || method.IsStatic || method.ReturnType != result)
            throw new InvalidOperationException(owner.Name + "." + name + " did not match.");
        return method;
    }

    private static FieldInfo Field(
        int index,
        Type owner,
        string name,
        Type type,
        bool isStatic,
        Func<string, bool> include)
    {
        Require(index, include);
        var field = owner.GetField(name, isStatic ? Static : Instance);
        if (field is null || field.IsStatic != isStatic || field.FieldType != type)
            throw new InvalidOperationException(owner.Name + "." + name + " did not match.");
        return field;
    }

    private static Action<object> Action(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object), "target");
        return Expression.Lambda<Action<object>>(
            Expression.Call(Expression.Convert(target, method.DeclaringType!), method),
            target).Compile();
    }

    private static Func<object?> StaticObject(FieldInfo field) =>
        Expression.Lambda<Func<object?>>(
            Expression.Convert(Expression.Field(null, field), typeof(object))).Compile();

    private static Func<object, T> FieldFunc<T>(FieldInfo field)
    {
        var target = Expression.Parameter(typeof(object), "target");
        return Expression.Lambda<Func<object, T>>(
            Expression.Field(Expression.Convert(target, field.DeclaringType!), field),
            target).Compile();
    }

    private static Func<object, bool> LiveControl(
        FieldInfo buttonField,
        MethodInfo gameObject,
        MethodInfo enabled,
        MethodInfo activeInHierarchy,
        MethodInfo interactable)
    {
        var target = Expression.Parameter(typeof(object), "target");
        var component = Expression.Convert(target, buttonField.DeclaringType!);
        var nativeButton = Expression.Variable(buttonField.FieldType, "button");
        var componentGameObject = Expression.Call(component, gameObject);
        var buttonGameObject = Expression.Call(nativeButton, gameObject);
        var body = Expression.Block(
            new[] { nativeButton },
            Expression.Assign(nativeButton, Expression.Field(component, buttonField)),
            Expression.AndAlso(
                Expression.NotEqual(nativeButton, Expression.Constant(null, buttonField.FieldType)),
                Expression.AndAlso(
                    Expression.Call(component, enabled),
                    Expression.AndAlso(
                        Expression.Call(componentGameObject, activeInHierarchy),
                        Expression.AndAlso(
                            Expression.Call(nativeButton, enabled),
                            Expression.AndAlso(
                                Expression.Call(buttonGameObject, activeInHierarchy),
                                Expression.Call(nativeButton, interactable)))))));
        return Expression.Lambda<Func<object, bool>>(body, target).Compile();
    }

    private static Func<object, bool> BoolMethod(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object), "target");
        return Expression.Lambda<Func<object, bool>>(
            Expression.Call(Expression.Convert(target, method.DeclaringType!), method),
            target).Compile();
    }

    /// <summary>
    /// A destroyed Unity reference is not a live panel, and reading through one throws. The field
    /// answers with the game's own object-lifetime rule instead of a plausible reference.
    /// </summary>
    private static Func<object, object?> UnityObjectField(FieldInfo field) =>
        target => field.GetValue(target) as UnityEngine.Object is { } value && value != null
            ? value
            : null;

    private static Func<object, object, bool> Contains(
        MethodInfo componentTransform,
        MethodInfo childOf)
    {
        var container = Expression.Parameter(typeof(object), "container");
        var child = Expression.Parameter(typeof(object), "child");
        var owner = componentTransform.DeclaringType!;
        return Expression.Lambda<Func<object, object, bool>>(
            Expression.Call(
                Expression.Call(Expression.Convert(child, owner), componentTransform),
                childOf,
                Expression.Call(Expression.Convert(container, owner), componentTransform)),
            container,
            child).Compile();
    }

    private static Func<object, string> StringMethod(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object), "target");
        return Expression.Lambda<Func<object, string>>(
            Expression.Call(Expression.Convert(target, method.DeclaringType!), method),
            target).Compile();
    }
}
