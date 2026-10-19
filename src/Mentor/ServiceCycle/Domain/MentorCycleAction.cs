using System;
using OrbModding.Common.Runtime.World;

namespace OrbMentor;

internal readonly struct MentorCycleAction
{
    internal MentorCycleAction(
        MasteryExperienceDomain domain,
        Guid recipientId,
        MentorAmount amount,
        int masteryCeilingExclusive,
        long collectedAtEpoch)
    {
        Domain = domain;
        RecipientId = recipientId;
        Amount = amount;
        MasteryCeilingExclusive = masteryCeilingExclusive;
        CollectedAtEpoch = collectedAtEpoch;
    }

    public MasteryExperienceDomain Domain { get; }
    public Guid RecipientId { get; }
    public MentorAmount Amount { get; }
    public int MasteryCeilingExclusive { get; }
    public long CollectedAtEpoch { get; }
}
