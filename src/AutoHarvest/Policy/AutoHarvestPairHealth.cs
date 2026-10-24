namespace OrbAutomata;

/// <summary>
/// Why one selected harvest pair is not being acted on, in the terms the player can do something
/// about.
/// </summary>
/// <remarks>
/// <para>
/// They are separate members now, so the status line can say the thing that is actually so.
/// </para>
/// <para>
/// The numbers are the wire: the pair health rides the cycle projection as an integer. Existing
/// members keep their values and the new ones are appended, so a journal recorded before this
/// change still reads as what it meant.
/// </para>
/// </remarks>
internal enum AutoHarvestPairHealthKind
{
    NotSelected = 0,
    NotObserved = 1,
    Eligible = 2,

    /// <summary>The action's own prerequisites have not been satisfied.</summary>
    /// <remarks>
    /// Nothing produces this any more. It was the reading of an unset native prerequisite latch, and
    /// an unset latch says that the game has not answered rather than that it answered no — see
    /// <see cref="PrerequisitesNotConfirmed"/>, which replaced it. The member stays because the value
    /// is the wire: a journal recorded before that change meant this, and has to keep reading as this.
    /// </remarks>
    ProgressionLocked = 3,
    NativeBusy = 4,
    QueueBlocked = 5,
    RegistryNotReady = 6,
    ContractUnavailable = 7,

    /// <summary>A mutation was attempted and the game did not do what it was asked.</summary>
    /// <remarks>
    /// Nothing produces this any more: a pair whose add did not engage backs off and is retried
    /// instead of staying faulted. The member stays because the value is the wire.
    /// </remarks>
    Faulted = 8,

    /// <summary>The plot the harvest belongs to is not visible in the world.</summary>
    PlotNotVisible = 9,

    /// <summary>
    /// The plot does not currently name this action exactly once among the ones it offers. Not a
    /// lock: an unlocked plot stops offering its harvest action when there is nothing on it to
    /// harvest, and starts again on its own.
    /// </summary>
    ActionNotOffered = 10,

    /// <summary>
    /// The game has not confirmed this action's prerequisites. Not a lock: the native latch is set
    /// when the game runs a check and passes it, and it says nothing about whether a check has been
    /// run at all, so its unset state is the absence of an answer rather than a refusal. The pair is
    /// not acted on either way — evidence nobody has is not grounds for acting — but the player is not
    /// told to go and finish progression that may already be finished.
    /// </summary>
    PrerequisitesNotConfirmed = 11,
}

internal readonly struct AutoHarvestPairHealth
{
    public AutoHarvestPairHealth(
        AutoHarvestPair pair,
        bool selected,
        AutoHarvestPairHealthKind kind,
        bool featureScoped = false)
    {
        Pair = pair;
        Selected = selected;
        Kind = kind;
        FeatureScoped = featureScoped;
    }

    public AutoHarvestPair Pair { get; }
    public bool Selected { get; }
    public AutoHarvestPairHealthKind Kind { get; }
    public bool FeatureScoped { get; }

    public static AutoHarvestPairHealth NotSelected(AutoHarvestPair pair) =>
        new(pair, selected: false, AutoHarvestPairHealthKind.NotSelected);

    public static AutoHarvestPairHealth NotObserved(AutoHarvestPair pair) =>
        new(pair, selected: true, AutoHarvestPairHealthKind.NotObserved);

    public static AutoHarvestPairHealth Eligible(AutoHarvestPair pair) =>
        new(pair, selected: true, AutoHarvestPairHealthKind.Eligible);
}
