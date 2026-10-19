#if SERVICE_CYCLE_PROFILE
using System;

namespace OrbAutomata.GameMcp;

internal sealed class GameMcpOpenModals
{
    private readonly Func<ModalDismissGameAction?> _modalDismissGameAction;

    internal GameMcpOpenModals(Func<ModalDismissGameAction?> modalDismissGameAction)
    {
        _modalDismissGameAction = modalDismissGameAction ??
            throw new ArgumentNullException(nameof(modalDismissGameAction));
    }

    internal bool TryReadTitles(out string[] titles, out string reason)
    {
        titles = Array.Empty<string>();
        reason = string.Empty;
        var modalDismissGameAction = _modalDismissGameAction();
        if (modalDismissGameAction is null)
        {
            reason = "The open panels cannot be read on this build, so no panel can be opened " +
                "without guessing at what it did.";
            return false;
        }
        return modalDismissGameAction.TryReadOpenModals(out titles, out reason);
    }

    /// <summary>
    /// Names whatever native modal is covering the board. A screen read that stayed silent about
    /// an open Settings panel is what made a covered board look like a working one.
    /// </summary>
    internal void AppendTo(GameMcpObjectBuilder details)
    {
        var modalDismissGameAction = _modalDismissGameAction();
        if (modalDismissGameAction is null)
        {
            details["openModalsUnavailable"] =
                "The native modal contracts were not composed.";
            return;
        }
        if (!modalDismissGameAction.TryReadOpenModals(out var titles, out var reason))
        {
            details["openModalsUnavailable"] =
                reason.Length == 0 ? "The open modal titles are unavailable." : reason;
            return;
        }
        if (titles.Length == 0) return;
        var open = new GameMcpArrayBuilder();
        foreach (var title in titles) open.Add(title);
        details["openModals"] = open;
    }
}
#endif
