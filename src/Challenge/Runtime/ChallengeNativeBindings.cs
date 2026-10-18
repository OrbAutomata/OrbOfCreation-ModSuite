using System;
using OrbModding.Common;

namespace OrbAutomata;

/// <summary>Complete lifecycle binding set for native challenge decisions and offer refresh.</summary>
internal sealed class ChallengeNativeBindings
{
    internal static readonly string[] ContractIds =
    {
        "challenge.challenge.type-action", "challenge.manager.type-action",
        "challenge.reset-manager.type-action", "challenge.list.type-action",
        "challenge.int-variable.type-action", "challenge.bool-variable.type-action",
        "challenge.challenge-state.type-action",
        "challenge.manager-instance-action", "challenge.reset-manager-instance-action",
        "challenge.manager-preferred-action", "challenge.manager-active-action",
        "challenge.reset-active-action", "challenge.reset-rerolls-left-action",
        "challenge.reset-cycle-complete-action", "challenge.reset-fetched-action",
        "challenge.list-empty-spot-action",
        "challenge.list-contains-action", "challenge.list-toggle-action",
        "challenge.list-restricted-action", "challenge.challenge-state-action",
        "challenge.challenge-toggle-queue-action", "challenge.challenge-abandon-action",
        "challenge.int-as-int-action", "challenge.int-set-action",
        "challenge.bool-get-action", "challenge.bool-set-action",
        "challenge.manager-fetch-action",
    };

    private ChallengeNativeBindings(Type challengeType, Type challengeManagerType,
        Type resetManagerType, Func<object?> challengeManager, Func<object?> resetManager,
        Func<object, object?> preferred, Func<object, object?> timeOffers,
        Func<object, object?> prestigeOffers, Func<object, object?> rerollsLeft,
        Func<object, object?> cycleComplete, Func<object, object?> fetched,
        Func<object, bool> hasEmptySpot, Func<object, object, bool> contains,
        Action<object, object> toggle, Func<object, object, bool> restricted,
        Func<object, int> state, Action<object> toggleQueue, Action<object> abandon,
        Func<object, int> asInt, Action<object, int> setInt, Func<object, bool> getBool,
        Action<object, bool> setBool, Action<object> reroll)
    {
        ChallengeType = challengeType;
        ChallengeManagerType = challengeManagerType;
        ResetManagerType = resetManagerType;
        ChallengeManager = challengeManager;
        ResetManager = resetManager;
        Preferred = preferred;
        TimeOffers = timeOffers;
        PrestigeOffers = prestigeOffers;
        RerollsLeft = rerollsLeft;
        CycleComplete = cycleComplete;
        Fetched = fetched;
        HasEmptySpot = hasEmptySpot;
        Contains = contains;
        Toggle = toggle;
        Restricted = restricted;
        State = state;
        ToggleQueue = toggleQueue;
        Abandon = abandon;
        AsInt = asInt;
        SetInt = setInt;
        GetBool = getBool;
        SetBool = setBool;
        Reroll = reroll;
    }

    internal Type ChallengeType { get; }
    internal Type ChallengeManagerType { get; }
    internal Type ResetManagerType { get; }
    internal Func<object?> ChallengeManager { get; }
    internal Func<object?> ResetManager { get; }
    internal Func<object, object?> Preferred { get; }
    internal Func<object, object?> TimeOffers { get; }
    internal Func<object, object?> PrestigeOffers { get; }
    internal Func<object, object?> RerollsLeft { get; }
    internal Func<object, object?> CycleComplete { get; }
    internal Func<object, object?> Fetched { get; }
    internal Func<object, bool> HasEmptySpot { get; }
    internal Func<object, object, bool> Contains { get; }
    internal Action<object, object> Toggle { get; }
    internal Func<object, object, bool> Restricted { get; }
    internal Func<object, int> State { get; }
    internal Action<object> ToggleQueue { get; }
    internal Action<object> Abandon { get; }
    internal Func<object, int> AsInt { get; }
    internal Action<object, int> SetInt { get; }
    internal Func<object, bool> GetBool { get; }
    internal Action<object, bool> SetBool { get; }
    /// <summary>
    /// The offer screen's own new-challenges button. One button, one shared reroll budget: the
    /// Reset modal's copy of it spends the same <c>challengeRerollsLeft</c> and refreshes the same
    /// list, so a second bound path was a second name for one press.
    /// </summary>
    internal Action<object> Reroll { get; }

    internal static bool TryCreate(out ChallengeNativeBindings? bindings, out string reason,
        Func<string, Type?>? resolveType = null, Func<string, bool>? includeContract = null) =>
        NativeBindingSet.TryBind("challenge", ContractIds, resolveType, includeContract, set =>
        {
            var challenge = set.Type("challenge.challenge.type-action", "ChallengeSO");
            var manager = set.Type("challenge.manager.type-action", "ChallengeManager");
            var reset = set.Type("challenge.reset-manager.type-action", "PersistentResetManager");
            var list = set.Type("challenge.list.type-action", "ChallengeListVariable");
            var integer = set.Type("challenge.int-variable.type-action", "IntVariable");
            var boolean = set.Type("challenge.bool-variable.type-action", "BoolVariable");
            var challengeState = set.Type("challenge.challenge-state.type-action", "ChallengeSO+ChallengeState");

            return new ChallengeNativeBindings(challenge, manager, reset,
                set.StaticField<Func<object?>>("challenge.manager-instance-action", manager, "instance", manager),
                set.StaticField<Func<object?>>("challenge.reset-manager-instance-action", reset, "instance", reset),
                set.Field<Func<object, object?>>(
                    "challenge.manager-preferred-action", manager, "preferredChallenges", list),
                set.Field<Func<object, object?>>("challenge.manager-active-action", manager, "activeChallenges", list),
                set.Field<Func<object, object?>>("challenge.reset-active-action", reset, "activeChallenges", list),
                set.Field<Func<object, object?>>(
                    "challenge.reset-rerolls-left-action", reset, "challengeRerollsLeft", integer),
                set.Field<Func<object, object?>>(
                    "challenge.reset-cycle-complete-action", reset, "hasCompleteWorldCycle", boolean),
                set.Field<Func<object, object?>>(
                    "challenge.reset-fetched-action", reset, "hasFetchedChallenges", boolean),
                set.Call<Func<object, bool>>("challenge.list-empty-spot-action", list, "HasEmptySpot", typeof(bool)),
                set.Call<Func<object, object, bool>>(
                    "challenge.list-contains-action", list, "Contains", typeof(bool), challenge),
                set.Call<Action<object, object>>(
                    "challenge.list-toggle-action", list, "Toggle", typeof(void), challenge),
                set.Call<Func<object, object, bool>>(
                    "challenge.list-restricted-action", list, "IsChallengeRestricted", typeof(bool), challenge),
                set.Field<Func<object, int>>("challenge.challenge-state-action", challenge, "state", challengeState),
                set.Call<Action<object>>(
                    "challenge.challenge-toggle-queue-action", challenge, "ToggleQueueActivation", typeof(void)),
                set.Call<Action<object>>(
                    "challenge.challenge-abandon-action", challenge, "AbandonChallenge", typeof(void)),
                set.Call<Func<object, int>>("challenge.int-as-int-action", integer, "AsInt", typeof(int)),
                set.Call<Action<object, int>>(
                    "challenge.int-set-action", integer, "SetValue", typeof(void), typeof(int)),
                set.Call<Func<object, bool>>("challenge.bool-get-action", boolean, "GetValue", typeof(bool)),
                set.Call<Action<object, bool>>(
                    "challenge.bool-set-action", boolean, "SetValue", typeof(void), typeof(bool)),
                set.Call<Action<object>>(
                    "challenge.manager-fetch-action", manager, "LoadNewActiveChallenges", typeof(void)));
        }, out bindings, out reason);
}
