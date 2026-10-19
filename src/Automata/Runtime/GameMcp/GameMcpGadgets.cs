#if SERVICE_CYCLE_PROFILE
using System;
using OrbModConfig;
using UnityEngine;

namespace OrbAutomata.GameMcp;

internal sealed class GameMcpGadgets
{
    private readonly Func<long> _lifecycleGeneration;
    private ModalDismissGameAction? _modalDismissGameAction;
    private ModalOpenGameAction? _modalOpenGameAction;

    internal GameMcpGadgets(
        MonoBehaviour host,
        Func<ModConfigUiShell?> uiShell,
        Action<GameMcpCommand, GameMcpCommandResult> complete,
        Func<long> lifecycleGeneration,
        Func<ulong> configurationGeneration,
        Func<GameMcpTooltipNativeAccess?> tooltipNativeAccess,
        Func<string> tooltipContractFailure,
        Action<string> logWarning)
    {
        _lifecycleGeneration = lifecycleGeneration ??
            throw new ArgumentNullException(nameof(lifecycleGeneration));
        Results = new GameMcpGadgetResults(lifecycleGeneration, configurationGeneration);
        OpenModals = new GameMcpOpenModals(() => _modalDismissGameAction);
        Navigator = new GameMcpNavigator(
            host,
            uiShell,
            complete,
            Results,
            OpenModals);
        ScreenReader = new GameMcpScreenReader(
            uiShell,
            tooltipNativeAccess,
            tooltipContractFailure,
            () => _modalOpenGameAction,
            logWarning,
            Results,
            Navigator);
    }

    internal GameMcpGadgetResults Results { get; }
    internal GameMcpOpenModals OpenModals { get; }
    internal GameMcpNavigator Navigator { get; }
    internal GameMcpScreenReader ScreenReader { get; }
    internal ModalDismissGameAction? ModalDismissGameAction => _modalDismissGameAction;
    internal ModalOpenGameAction? ModalOpenGameAction => _modalOpenGameAction;

    internal bool ModalBindingsAvailable =>
        _modalDismissGameAction?.BindingsAvailable == true &&
            _modalOpenGameAction?.BindingsAvailable == true;

    internal void ComposeModalActions()
    {
        _modalDismissGameAction = new ModalDismissGameAction(_lifecycleGeneration);
        _modalOpenGameAction = new ModalOpenGameAction();
    }

    internal void InvalidateLifecycle()
    {
        _modalDismissGameAction?.InvalidateLifecycle();
        _modalOpenGameAction?.InvalidateLifecycle();
    }

    internal void ReleaseModalActions()
    {
        _modalDismissGameAction?.Dispose();
        _modalDismissGameAction = null;
        _modalOpenGameAction?.Dispose();
        _modalOpenGameAction = null;
    }

    /// <summary>
    /// Why the panel verb is out, naming whichever half of it failed to bind. Opening and
    /// dismissing are one verb to a caller, so one line answers for both.
    /// </summary>
    internal string ModalBindingFailure()
    {
        if (_modalDismissGameAction is null || _modalOpenGameAction is null)
            return "the modal action boundary was not composed";
        if (!_modalDismissGameAction.BindingsAvailable)
            return _modalDismissGameAction.BindingFailure;
        return _modalOpenGameAction.BindingsAvailable
            ? string.Empty
            : _modalOpenGameAction.BindingFailure;
    }
}
#endif
