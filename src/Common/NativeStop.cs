using System;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common;

/// <summary>
/// Why a GameAction's call stopped after it had already pressed the game: the press counts as an
/// attempt either way, and the action refuses only when the suite pressed it back and the game
/// reads undone.
/// </summary>
internal readonly struct NativeStop
{
    private NativeStop(ServiceActionResultCode code, string reason, Func<bool>? undone)
    {
        Code = code;
        Reason = reason ?? throw new ArgumentNullException(nameof(reason));
        Undone = undone;
    }

    internal ServiceActionResultCode Code { get; }
    internal string Reason { get; }
    internal Func<bool>? Undone { get; }

    /// <summary>
    /// The suite pressed its change back: a refusal with one attempt when <paramref name="undone"/>
    /// reads true after the call, otherwise an attempted fault.
    /// </summary>
    internal static NativeStop Refused(ServiceActionResultCode code, string reason, Func<bool> undone) =>
        new(code, reason, undone ?? throw new ArgumentNullException(nameof(undone)));

    /// <summary>The game was pressed and nothing pressed it back: an attempted fault.</summary>
    internal static NativeStop Fault(ServiceActionResultCode code, string reason) =>
        new(code, reason, null);
}
