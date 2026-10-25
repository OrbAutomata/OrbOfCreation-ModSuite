using OrbModding.Common.Runtime.World;

namespace OrbAutomata;

internal readonly struct AutoLevelsCycleAction
{
    internal AutoLevelsCycleAction(LevelDial dial, int currentLevel, int targetLevel, long lifecycleEpoch)
    { Dial = dial; CurrentLevel = currentLevel; TargetLevel = targetLevel; LifecycleEpoch = lifecycleEpoch; }
    internal LevelDial Dial { get; }
    internal int CurrentLevel { get; }
    internal int TargetLevel { get; }
    internal long LifecycleEpoch { get; }
}
