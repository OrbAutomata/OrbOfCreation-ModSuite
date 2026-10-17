using System;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal static class AutoConceptServiceProjection
{
    internal const int CapturedRecipesKey = 10;
    internal const int EligibleRecipesKey = 11;
    internal const int ActiveRecipesKey = 12;
    internal const int OwnedRecipesKey = 13;
    internal const int PlannedActionsKey = 14;
    internal const int DecisionKindKey = 15;
    internal const int IdleReasonKey = 16;
    internal const int IdleMoveKey = 17;
    internal const int IdleRecipeKey = 18;
    internal const int IdleDisplacedKey = 20;
    internal const int IdleResourceKey = 22;

    public static void Write(in AutoConceptCycleState state, ServiceStateProjectionBuilder output)
    {
        var decision = state.Decision;
        output.Add(new ServiceProjectionKey(CapturedRecipesKey), ServiceProjectionValue.FromInteger(decision.CapturedRecipes));
        output.Add(new ServiceProjectionKey(EligibleRecipesKey), ServiceProjectionValue.FromInteger(decision.EligibleRecipes));
        output.Add(new ServiceProjectionKey(ActiveRecipesKey), ServiceProjectionValue.FromInteger(decision.ActiveRecipes));
        output.Add(new ServiceProjectionKey(OwnedRecipesKey), ServiceProjectionValue.FromInteger(decision.OwnedRecipes));
        output.Add(new ServiceProjectionKey(PlannedActionsKey), ServiceProjectionValue.FromInteger(decision.PlannedActions));
        output.Add(new ServiceProjectionKey(DecisionKindKey), ServiceProjectionValue.FromInteger((int)decision.Kind));
        output.Add(new ServiceProjectionKey(IdleReasonKey), ServiceProjectionValue.FromInteger((int)decision.IdleReason));
        output.Add(new ServiceProjectionKey(IdleMoveKey), ServiceProjectionValue.FromInteger((int)decision.Idle.Move));
        WriteGuid(output, IdleRecipeKey, decision.Idle.Recipe);
        WriteGuid(output, IdleDisplacedKey, decision.Idle.Displaced);
        WriteGuid(output, IdleResourceKey, decision.Idle.Resource);
    }

    internal static Guid ReadGuid(long high, long low)
    {
        Span<byte> bytes = stackalloc byte[16];
        BitConverter.TryWriteBytes(bytes, high);
        BitConverter.TryWriteBytes(bytes.Slice(8), low);
        return new Guid(bytes);
    }

    private static void WriteGuid(ServiceStateProjectionBuilder output, int key, Guid value)
    {
        Span<byte> bytes = stackalloc byte[16];
        value.TryWriteBytes(bytes);
        output.Add(new ServiceProjectionKey(key), ServiceProjectionValue.FromInteger(BitConverter.ToInt64(bytes)));
        output.Add(new ServiceProjectionKey(key + 1), ServiceProjectionValue.FromInteger(BitConverter.ToInt64(bytes.Slice(8))));
    }
}
