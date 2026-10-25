using System;

namespace OrbModding.Common.Runtime.World;

/// <summary>A collection reason, its target, and whether another read can recover it.</summary>
internal readonly struct WorldCollectionFailure
{
    internal WorldCollectionFailure(GameActionReason reason, Guid targetId = default,
        string code = "authored_fact", bool hasReadFault = false)
    {
        Reason = reason;
        TargetId = targetId;
        Code = code;
        HasReadFault = hasReadFault;
    }

    internal GameActionReason Reason { get; }
    internal Guid TargetId { get; }
    internal string Code { get; }
    internal bool HasReadFault { get; }
    internal int Length => Reason.Sentence.Length;
    internal string Text => Reason.Text;

    internal WorldCollectionFailure Around(string before, string after = "") =>
        new(Reason.Around(before, after), TargetId, Code, HasReadFault);

    /// <summary>Keep the first read fault ahead of durable facts, and retain every retry indication.</summary>
    internal WorldCollectionFailure Include(in WorldCollectionFailure next, bool preferReadFault = true)
    {
        var first = Length == 0 || (preferReadFault && !HasReadFault && next.HasReadFault) ? next : this;
        return new(first.Reason, first.TargetId, first.Code, HasReadFault || next.HasReadFault);
    }

    internal bool SameIdentityAs(in WorldCollectionFailure other) =>
        TargetId == other.TargetId && string.Equals(Code, other.Code, StringComparison.Ordinal) &&
        string.Equals(Reason.Sentence, other.Reason.Sentence, StringComparison.Ordinal);

    public static implicit operator WorldCollectionFailure(string sentence) => new(sentence);
    public static WorldCollectionFailure operator +(string before, WorldCollectionFailure failure) => failure.Around(before);
    public static WorldCollectionFailure operator +(WorldCollectionFailure failure, string after) => failure.Around("", after);
}
