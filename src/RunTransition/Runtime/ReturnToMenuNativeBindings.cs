using System;
using OrbModding.Common;

namespace OrbAutomata;

/// <summary>Exact v1.0.5 binding set for the Back to Main Menu button and the panel holding it.</summary>
internal sealed class ReturnToMenuNativeBindings
{
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
        Func<string, bool>? includeContract = null) =>
        NativeBindingSet.TryBind("Back to Main Menu", ContractIds, resolveType, includeContract, set =>
        {
            var button = set.Type("return-to-menu.button.type-action", "UIBackToMenuButton");
            var flash = set.Type("return-to-menu.screen-flash.type-action", "UIScreenFlash");
            var modal = set.Type("return-to-menu.modal.type-action", "UIModal");
            var activator = set.Type("return-to-menu.modal-activator.type-action", "UIModalActivator");
            var gameObject = set.Call<Func<UnityEngine.Component, UnityEngine.GameObject>>(
                "return-to-menu.component-game-object-action",
                typeof(UnityEngine.Component), "get_gameObject", typeof(UnityEngine.GameObject));
            var enabled = set.Call<Func<UnityEngine.Behaviour, bool>>(
                "return-to-menu.behaviour-enabled-action",
                typeof(UnityEngine.Behaviour), "get_enabled", typeof(bool));
            var activeInHierarchy = set.Call<Func<UnityEngine.GameObject, bool>>(
                "return-to-menu.game-object-active-action",
                typeof(UnityEngine.GameObject), "get_activeInHierarchy", typeof(bool));
            var interactable = set.Call<Func<UnityEngine.UI.Selectable, bool>>(
                "return-to-menu.selectable-interactable-action",
                typeof(UnityEngine.UI.Selectable), "IsInteractable", typeof(bool));
            var componentTransform = set.Call<Func<UnityEngine.Component, UnityEngine.Transform>>(
                "return-to-menu.component-transform-action",
                typeof(UnityEngine.Component), "get_transform", typeof(UnityEngine.Transform));
            var childOf = set.Call<Func<UnityEngine.Transform, UnityEngine.Transform, bool>>(
                "return-to-menu.transform-child-of-action",
                typeof(UnityEngine.Transform), "IsChildOf", typeof(bool), typeof(UnityEngine.Transform));
            var createdModal = set.Field<Func<object, object?>>(
                "return-to-menu.activator-created-modal-action", activator, "createdModal", modal);
            return new ReturnToMenuNativeBindings(
                button,
                set.Call<Action<object>>("return-to-menu.button-action", button, "BackToMenu", typeof(void)),
                set.StaticField<Func<object?>>(
                    "return-to-menu.screen-flash-instance-action", flash, "instance", flash),
                set.Field<Func<object, bool>>(
                    "return-to-menu.screen-flash-active-action", flash, "isActive", typeof(bool)),
                LiveControl(
                    set.Field<Func<object, UnityEngine.UI.Button>>(
                        "return-to-menu.button-component-action",
                        button, "button", typeof(UnityEngine.UI.Button)),
                    gameObject, enabled, activeInHierarchy, interactable),
                set.Call<Func<object, string>>(
                    "return-to-menu.object-name-action", typeof(UnityEngine.Object), "get_name", typeof(string)),
                activator,
                set.Field<Func<object, bool>>(
                    "return-to-menu.activator-modal-created-action", activator, "modalCreated", typeof(bool)),
                target => createdModal(target) is var created && NativeObjectLiveness.IsAlive(created) ? created : null,
                set.Call<Func<object, bool>>("return-to-menu.modal-open-action", modal, "IsOpen", typeof(bool)),
                LiveControl(
                    set.Field<Func<object, UnityEngine.UI.Button>>(
                        "return-to-menu.activator-button-component-action",
                        activator, "button", typeof(UnityEngine.UI.Button)),
                    gameObject, enabled, activeInHierarchy, interactable),
                set.Call<Action<object>>(
                    "return-to-menu.activator-open-action", activator, "OpenModal", typeof(void)),
                (container, child) =>
                    childOf(
                        componentTransform((UnityEngine.Component)child),
                        componentTransform((UnityEngine.Component)container)));
        }, out bindings, out reason);

    private static Func<object, bool> LiveControl(
        Func<object, UnityEngine.UI.Button> readButton,
        Func<UnityEngine.Component, UnityEngine.GameObject> gameObject,
        Func<UnityEngine.Behaviour, bool> enabled,
        Func<UnityEngine.GameObject, bool> activeInHierarchy,
        Func<UnityEngine.UI.Selectable, bool> interactable) =>
        target =>
        {
            var button = readButton(target);
            var component = (UnityEngine.Behaviour)target;
            return button is not null &&
                enabled(component) &&
                activeInHierarchy(gameObject(component)) &&
                enabled(button) &&
                activeInHierarchy(gameObject(button)) &&
                interactable(button);
        };
}
