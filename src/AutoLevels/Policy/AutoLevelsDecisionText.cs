using System;
using OrbModding.Common;
using OrbModding.Common.Runtime.GameMath;
using OrbModding.Common.Runtime.World;

namespace OrbAutomata;

/// <summary>The one player-facing explanation of a typed Auto Levels policy result.</summary>
internal static class AutoLevelsDecisionText
{
    internal static string Describe(in AutoLevelsDecision decision, EntityIdentityCatalogSnapshot names)
    {
        var dial = decision.Dial switch
        {
            LevelDial.Output => "Output",
            LevelDial.Reserve => "Reserve",
            LevelDial.Druidry => "Druidry",
            LevelDial.Alchemy => "Alchemy",
            _ => throw new ArgumentOutOfRangeException(nameof(decision.Dial)),
        };
        var prefix = dial + " " + decision.CurrentLevel +
            (decision.Kind == AutoLevelsDecisionKind.Hold ? ": holding. " :
                " → " + decision.TargetLevel + ": ");
        var resource = decision.ResourceId == Guid.Empty ? string.Empty :
            EntityIdentityFormatter.PlayerName(decision.ResourceId, names);
        var comparison = resource + " needs " + GameScientificNumber.Beautify(decision.Required) +
            "; budget " + GameScientificNumber.Beautify(decision.Budget) + ".";
        return prefix + (decision.Reason switch
        {
            AutoLevelsDecisionReason.Disabled => "Auto Levels is disabled.",
            AutoLevelsDecisionReason.EvidenceUnavailable => decision.Detail.Length == 0
                ? "The game's complete cost quote is unavailable." : decision.Detail,
            AutoLevelsDecisionReason.ControlLocked => decision.Detail.Length == 0
                ? "The game has not unlocked this level control." : decision.Detail,
            AutoLevelsDecisionReason.InvalidEvidence => "The current level or resource evidence is inconsistent.",
            AutoLevelsDecisionReason.NoActiveWork => "No loaded work uses this dial.",
            AutoLevelsDecisionReason.AtMaximum => "The native maximum has been reached.",
            AutoLevelsDecisionReason.AtMinimum => "The native minimum prevents a reduction. " + comparison,
            AutoLevelsDecisionReason.ReserveMaximum => "Approaching the native Reserve maximum.",
            AutoLevelsDecisionReason.ImmediateCost => "A loaded spell exceeds its regeneration budget. " + comparison,
            AutoLevelsDecisionReason.NextImmediateCost => "The next level exceeds a loaded spell's regeneration budget. " + comparison,
            AutoLevelsDecisionReason.DominantDrain => "This domain owns most of a resource's drain and empties its buffer too soon. " + comparison,
            AutoLevelsDecisionReason.OtherDrainPressure => "Other spending is exhausting the buffer; this domain does not own most of the drain. " + comparison,
            AutoLevelsDecisionReason.NextDrain => "The next level would empty its dominant resource buffer too soon. " + comparison,
            AutoLevelsDecisionReason.Allocation => "The next level needs more free allocation. " + comparison,
            AutoLevelsDecisionReason.Sustainable => "The next level fits the captured resource budgets.",
            _ => throw new ArgumentOutOfRangeException(nameof(decision.Reason)),
        });
    }
}
