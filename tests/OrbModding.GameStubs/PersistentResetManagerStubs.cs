using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

public class PersistentResetManager
{
    public static PersistentResetManager instance = new PersistentResetManager();
    public ResourceSO persistentResource = new ResourceSO();
    public IntVariable persistValue = new IntVariable { Value = 0 };
    public IntVariable persistValueNew = new IntVariable { Value = 0 };
    public IntVariable persistValueLast = new IntVariable { Value = 0 };
    public IntVariable persistentResetCount = new IntVariable { Value = 0 };
    public IntVariable challengeRerollsLeft = new IntVariable();
    public IntVariable challengeRerollsMax = new IntVariable();
    public BoolVariable hasCompleteWorldCycle = new BoolVariable();
    public BoolVariable hasFetchedChallenges = new BoolVariable();
    public ChallengeListVariable activeChallenges = new ChallengeListVariable();
    public ChallengeListVariable allChallenges = new ChallengeListVariable();
    public List<ChallengeSO> NextChallenges { get; } = new List<ChallengeSO>();
    public bool SuppressFetch { get; set; }
    public bool ThrowAfterFetch { get; set; }
    public int FetchCalls { get; private set; }
    public int ResetCalls { get; private set; }
    public bool SuppressReset { get; set; }
    public Exception? ThrowAfterReset { get; set; }
    public static Action? PersistentResetSignal { get; set; }

    public void FetchNewChallenges()
    {
        FetchCalls++;
        if (!SuppressFetch)
        {
            activeChallenges.CycleOut();
            activeChallenges.value = new List<ChallengeSO>(NextChallenges);
            activeChallenges.Instantiate();
        }
        if (ThrowAfterFetch) throw new InvalidOperationException("injected failure after prestige challenge fetch");
    }

    private void PersistentResetLogic()
    {
        ResetCalls++;
        PersistentResetSignal?.Invoke();
        if (!SuppressReset)
        {
            persistValueLast.Value = persistValue.Value;
            persistentResetCount.Value++;
            hasCompleteWorldCycle.value = false;
            hasFetchedChallenges.value = false;
            foreach (var challenge in allChallenges.value) challenge.rewardQueued = false;
            foreach (var challenge in activeChallenges.value)
                if (challenge.state == ChallengeSO.ChallengeState.QueuedStart)
                    challenge.state = ChallengeSO.ChallengeState.CurrentlyActive;
            GameManager.PersistentResetGameState();
            GameManager.CleanGame();
        }
        if (ThrowAfterReset is { } failure) throw failure;
    }
}
