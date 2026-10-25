using System;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.World;

internal enum WorldLevelPriceKind { Immediate = 0, Drain = 1, Allocation = 2 }

/// <summary>A captured or exactly projected price in the resource's nominal spend coordinate.</summary>
internal readonly struct WorldLevelPrice
{
    internal WorldLevelPrice(int slotIndex, Guid resourceId, BigDouble amount, WorldLevelPriceKind kind)
    { SlotIndex = slotIndex; ResourceId = resourceId; Amount = amount; Kind = kind; }
    internal int SlotIndex { get; }
    internal Guid ResourceId { get; }
    internal BigDouble Amount { get; }
    internal WorldLevelPriceKind Kind { get; }
}

internal sealed class WorldLevelDomainQuote
{
    internal WorldLevelDomainQuote(bool complete, string reason, bool hasActiveWork,
        int currentLevel, int maximumLevel, PublicationTable<WorldLevelPrice> current,
        PublicationTable<WorldLevelPrice> next, bool nextComplete = true, string nextReason = "")
    {
        Complete = complete;
        Reason = reason ?? throw new ArgumentNullException(nameof(reason));
        HasActiveWork = hasActiveWork;
        CurrentLevel = currentLevel;
        MaximumLevel = maximumLevel;
        Current = current ?? throw new ArgumentNullException(nameof(current));
        Next = next ?? throw new ArgumentNullException(nameof(next));
        NextComplete = nextComplete;
        NextReason = nextReason ?? throw new ArgumentNullException(nameof(nextReason));
    }
    internal bool Complete { get; }
    internal string Reason { get; }
    internal bool NextComplete { get; }
    internal string NextReason { get; }
    internal bool HasActiveWork { get; }
    internal int CurrentLevel { get; }
    internal int MaximumLevel { get; }
    internal PublicationTable<WorldLevelPrice> Current { get; }
    internal PublicationTable<WorldLevelPrice> Next { get; }
    internal static WorldLevelDomainQuote Unavailable(string reason) => new(false, reason, false, 0, 0,
        PublicationTable<WorldLevelPrice>.Empty, PublicationTable<WorldLevelPrice>.Empty);
}

internal sealed class WorldLevelEconomy
{
    internal WorldLevelEconomy(WorldLevelDomainQuote output, WorldLevelDomainQuote druidry, WorldLevelDomainQuote alchemy)
    {
        Output = output ?? throw new ArgumentNullException(nameof(output));
        Druidry = druidry ?? throw new ArgumentNullException(nameof(druidry));
        Alchemy = alchemy ?? throw new ArgumentNullException(nameof(alchemy));
    }
    internal WorldLevelDomainQuote Output { get; }
    internal WorldLevelDomainQuote Druidry { get; }
    internal WorldLevelDomainQuote Alchemy { get; }
}

/// <summary>Native-free captures owned by one world frame; derivation runs on its worker.</summary>
internal sealed class WorldLevelEconomyBuffer
{
    private WorldOutputLevelInputs? _output;
    private WorldAlchemyLevelInputs? _alchemy;
    private WorldDruidryLevelInputs? _druidry;
    internal void ResetForCapture()
    {
        _output = null; _druidry = null; _alchemy = null;
    }
    internal void SetOutputInputs(WorldOutputLevelInputs inputs) => _output = inputs;
    internal void SetAlchemyInputs(WorldAlchemyLevelInputs inputs) => _alchemy = inputs;
    internal void SetDruidryInputs(WorldDruidryLevelInputs inputs) => _druidry = inputs;
    internal WorldLevelEconomy Build(PublicationTable<WorldResource> resources, WorldSpellWorkbench workbench,
        PublicationTable<WorldSpellTypeResonance> resonances,
        PublicationTable<WorldSpellCost>? nativeSpellCosts = null,
        GameWorldState? requirementWorld = null) => new(
        _output?.Build(workbench.OutputLevel, workbench.MaximumOutputLevel, resonances, nativeSpellCosts) ?? WorldLevelEconomyDefaults.Empty.Output,
        _druidry?.Build(resources, workbench.DruidryLevel, workbench.MaximumDruidryLevel) ?? WorldLevelEconomyDefaults.Empty.Druidry,
        _alchemy?.Build(resources, workbench.AlchemyLevel, workbench.MaximumAlchemyLevel, requirementWorld) ?? WorldLevelEconomyDefaults.Empty.Alchemy);
}

internal static class WorldLevelEconomyDefaults
{
    internal static readonly WorldLevelEconomy Empty = new(
        WorldLevelDomainQuote.Unavailable("Output price evidence has not been collected."),
        WorldLevelDomainQuote.Unavailable("Agromancy drain evidence has not been collected."),
        WorldLevelDomainQuote.Unavailable("Ordinary alchemy drain evidence has not been collected."));
}
