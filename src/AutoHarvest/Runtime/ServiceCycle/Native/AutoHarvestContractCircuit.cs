using System;

namespace OrbAutomata;

/// <summary>
/// The registry mismatches the game proved this lifecycle, for one pair or for both. A new lifecycle
/// probes the contract again.
/// </summary>
internal sealed class AutoHarvestContractCircuit : IAutoHarvestContractCircuit
{
    private bool _featureBlocked;
    private bool _fruitBlocked;
    private bool _treasureBlocked;

    public AutoHarvestNativeFailure FailureFor(AutoHarvestPair pair)
    {
        if (_featureBlocked)
        {
            return AutoHarvestNativeFailure.Create(
                AutoHarvestRuntimeFailureKind.Contract,
                AutoHarvestRuntimeFailureScope.Feature);
        }

        var pairBlocked = pair switch
        {
            AutoHarvestPair.FruitTree => _fruitBlocked,
            AutoHarvestPair.TreasureTree => _treasureBlocked,
            _ => throw new ArgumentOutOfRangeException(nameof(pair)),
        };
        return pairBlocked
            ? AutoHarvestNativeFailure.Create(
                AutoHarvestRuntimeFailureKind.Contract,
                AutoHarvestRuntimeFailureScope.Pair)
            : default;
    }

    public void Block(AutoHarvestPair pair, AutoHarvestRuntimeFailureScope scope)
    {
        if (scope == AutoHarvestRuntimeFailureScope.Feature)
        {
            _featureBlocked = true;
            return;
        }
        if (scope != AutoHarvestRuntimeFailureScope.Pair)
            throw new ArgumentOutOfRangeException(nameof(scope));
        if (pair == AutoHarvestPair.FruitTree) _fruitBlocked = true;
        else if (pair == AutoHarvestPair.TreasureTree) _treasureBlocked = true;
        else throw new ArgumentOutOfRangeException(nameof(pair));
    }

    public void InvalidateLifecycle()
    {
        _featureBlocked = false;
        _fruitBlocked = false;
        _treasureBlocked = false;
    }
}
