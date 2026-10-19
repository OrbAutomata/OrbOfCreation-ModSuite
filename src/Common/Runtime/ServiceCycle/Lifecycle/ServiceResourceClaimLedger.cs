using System;
using System.Threading;

namespace OrbModding.Common.Runtime.ServiceCycle.Lifecycle;

internal sealed partial class ServiceResourceClaimLedger
{
    /// <summary>
    /// How many claims one service can hold at once.
    /// </summary>
    /// <remarks>
    /// A live runner holds two — its worker definition and its worker state — and a replacement
    /// overlaps two runners while the retiring one finishes, so four is the bound. The extra pair is
    /// headroom: exhausting this array fails a construction, and the cost of never doing so is one
    /// null reference per service.
    /// </remarks>
    internal const int ClaimsPerService = 6;

    private readonly ServiceResourceClaim?[] _claims;
    private ServiceResourceClaim? _activeFactoryToken;
    private long _nextToken;

    internal ServiceResourceClaimLedger(int serviceCapacity)
    {
        if (serviceCapacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(serviceCapacity));
        _claims = new ServiceResourceClaim[
            checked(serviceCapacity * ClaimsPerService)];
    }

    internal int Capacity => _claims.Length;

    internal ServiceResourceClaimResult TryBeginFactory(
        ServiceResourceRole role,
        out ServiceResourceClaim claim)
    {
        var candidate = new ServiceResourceClaim(
            Interlocked.Increment(ref _nextToken),
            role);
        for (var index = 0; index < _claims.Length; index++)
        {
            if (Volatile.Read(ref _claims[index]) is not null) continue;
            candidate.AssignSlot(index);
            if (Interlocked.CompareExchange(
                    ref _claims[index],
                    candidate,
                    null) is not null)
            {
                claim = null!;
                return ServiceResourceClaimResult.Contended;
            }
            if (Interlocked.CompareExchange(
                    ref _activeFactoryToken,
                    candidate,
                    null) is null)
            {
                claim = candidate;
                return ServiceResourceClaimResult.Claimed;
            }
            ReleaseReservation(candidate);
            claim = null!;
            return ServiceResourceClaimResult.Contended;
        }
        claim = null!;
        return ServiceResourceClaimResult.CapacityExhausted;
    }

    internal ServiceResourceClaimResult FinalizeFactory(
        ServiceResourceClaim claim,
        object identity)
    {
        if (claim is null) throw new ArgumentNullException(nameof(claim));
        if (identity is null) throw new ArgumentNullException(nameof(identity));
        if ((uint)claim.SlotIndex >= (uint)_claims.Length ||
            !ReferenceEquals(
                Volatile.Read(ref _claims[claim.SlotIndex]),
                claim) ||
            !ReferenceEquals(
                Volatile.Read(ref _activeFactoryToken),
                claim) ||
            !claim.IsFactoryOpen)
        {
            throw new InvalidOperationException(
                "The resource factory token is not live.");
        }

        claim.PublishOwned(identity);
        for (var index = 0; index < _claims.Length; index++)
        {
            var other = Volatile.Read(ref _claims[index]);
            if (other is null ||
                ReferenceEquals(other, claim) ||
                !ReferenceEquals(other.Identity, identity))
                continue;
            ReleaseReservation(claim);
            return ServiceResourceClaimResult.Aliased;
        }
        return ServiceResourceClaimResult.Claimed;
    }

    internal int LiveClaimCount
    {
        get
        {
            var count = 0;
            for (var index = 0; index < _claims.Length; index++)
            {
                if (Volatile.Read(ref _claims[index]) is not null) count++;
            }
            return count;
        }
    }
}
