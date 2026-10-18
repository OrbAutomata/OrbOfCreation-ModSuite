using System.Globalization;
using OrbModding.Common.Runtime.GameMath;

namespace OrbAutomata;

/// <summary>
/// The sentences for the two states <c>SpellManager.RemoveSpell</c> refuses, shared by the
/// removal action and the equipped-spell preview so the same moment reads the same on both.
/// </summary>
internal static class SpellRemovalRefusal
{
    internal static string MidCast(string spellName) =>
        spellName + " is still recharging: it is mid-cast, and the game answers a removal now " +
        "with \"Cannot remove a spell that is still recharging.\" Wait for the cast to finish.";

    internal static string Recharging(
        string spellName,
        int currentCharges,
        int maximumCharges,
        BigDouble cooldownRemaining)
    {
        var maximum = maximumCharges.ToString(CultureInfo.InvariantCulture);
        return spellName + " is still recharging: it holds " +
            currentCharges.ToString(CultureInfo.InvariantCulture) + " of " + maximum + " charges" +
            (cooldownRemaining > BigDouble.Zero
                ? ", next in " + GameDurationText.Accurate(cooldownRemaining)
                : string.Empty) +
            ". The game only removes a spell at full charges — \"Cannot remove a spell that is " +
            "still recharging.\" — so remove it once it reads " + maximum + " of " + maximum + ".";
    }
}
