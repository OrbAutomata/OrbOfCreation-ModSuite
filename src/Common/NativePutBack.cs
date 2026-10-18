using System;

namespace OrbModding.Common;

/// <summary>
/// What a GameAction's call undoes when it throws partway, and how that undo reads back: one value,
/// so an undo never travels without the check that proves it.
/// </summary>
internal sealed class NativePutBack
{
    internal NativePutBack(Action undo, Func<bool> landed)
    {
        Undo = undo ?? throw new ArgumentNullException(nameof(undo));
        Landed = landed ?? throw new ArgumentNullException(nameof(landed));
    }

    internal Action Undo { get; }
    internal Func<bool> Landed { get; }
}
