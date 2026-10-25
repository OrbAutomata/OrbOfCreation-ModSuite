using System;
using OrbModding.Common;

namespace OrbAutomata;

/// <summary>
/// The saved preference behind PassiveAbility.ToggleMuted. That native method only inverts its
/// reference's muted field; setting that same field true avoids toggling an already-muted asset.
/// Reflective passives are non-global, so no artificial live ability instance is constructed.
/// </summary>
internal sealed class PassiveNotificationMuteGameAction : NativeGameAction<PassiveNotificationMuteBindings>
{
    private readonly TypedRegistryResolver _registry;
    private static readonly GameActionCodes Codes = new(
        QualityOfLifeActionResultCodes.WrongThread, QualityOfLifeActionResultCodes.ContractUnavailable,
        QualityOfLifeActionResultCodes.MutationPermitUnavailable, QualityOfLifeActionResultCodes.PostCommitFault,
        QualityOfLifeActionResultCodes.VerificationFailed);

    internal PassiveNotificationMuteGameAction(TypedRegistryResolver registry, Func<long> readLifecycleEpoch,
        Func<bool> tryCaptureMutationPermit, Func<string> readOwnershipFailure,
        Func<string, Type?>? resolveType = null, Func<string, bool>? includeContract = null)
        : base("Passive notification mute", "Passive abilities", Codes, readLifecycleEpoch,
            tryCaptureMutationPermit, readOwnershipFailure,
            (out PassiveNotificationMuteBindings? bindings, out string reason) =>
                PassiveNotificationMuteBindings.TryCreate(out bindings, out reason, resolveType, includeContract)) =>
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));

    internal NativeActionOutcome Submit(Guid passiveId, long lifecycleEpoch) =>
        Admit(lifecycleEpoch, native =>
        {
            if (!QualityOfLifePolicy.IsSupported(passiveId))
                return NativeActionOutcome.Reject(QualityOfLifeActionResultCodes.UnsupportedPassive,
                    "Only audited reflective Learning notifications may be muted by Quality of Life.");
            if (!_registry.TryResolveCurrent(passiveId, native.PassiveType, out var passive, out var reason) ||
                native.Guid(passive) != passiveId)
                return NativeActionOutcome.Reject(QualityOfLifeActionResultCodes.IdentityUnavailable,
                    reason.Sentence.Length == 0 ? "The passive's native UUID changed." : reason);
            if (native.Muted(passive))
                return NativeActionOutcome.Reject(QualityOfLifeActionResultCodes.AlreadyMuted,
                    "The passive's notifications are already muted.");
            if (!native.Touched(passive) || native.Hidden(passive) || native.Silent(passive))
                return NativeActionOutcome.Reject(QualityOfLifeActionResultCodes.NotEligible,
                    "The reflective passive is not an encountered, visible, non-silent notification source.");
            if (!TryPermit(out var refusal)) return refusal;
            return Execute(EntityIdentityFormatter.PlayerName(passiveId),
                "The passive's saved notification preference is muted.", false,
                () => native.Muted(passive), () => native.SetMuted(passive, true),
                static (_, muted) => muted);
        });
}
