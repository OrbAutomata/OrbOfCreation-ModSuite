using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using OrbAutomata;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Tests.Services.TestSupport;
using OrbModding.TestSupport;
using Xunit;

namespace OrbModding.Tests.Services.QualityOfLife;

public sealed class PassiveNotificationMuteGameActionTests
{
    private const long Epoch = 241;
    private readonly IDictionary _registry = new Hashtable();

    public static IEnumerable<object[]> SupportedPassives() => new[]
    {
        KnownEntities.PassiveReflectiveBloomingLearning,
        KnownEntities.PassiveReflectiveBuildingLearning,
        KnownEntities.PassiveReflectiveChargedLearning,
        KnownEntities.PassiveReflectiveEnergizedLearning,
        KnownEntities.PassiveReflectiveEssenceLearning,
        KnownEntities.PassiveReflectiveMagicLearning,
        KnownEntities.PassiveReflectiveMentalLearning,
        KnownEntities.PassiveReflectiveNaturalLearning,
        KnownEntities.PassiveReflectiveRestorativeLearning,
        KnownEntities.PassiveReflectiveSpacialLearning,
    }.Select(passive => new object[] { passive.Uuid });

    public static IEnumerable<object[]> RequiredContracts() =>
        QualityOfLifeNativeContracts.ContractIds.Select(id => new object[] { id });

    [Theory]
    [MemberData(nameof(SupportedPassives))]
    public void A_supported_touched_passive_changes_only_its_saved_notification_mute(Guid id)
    {
        var target = Passive(id);
        target.global = true;
        target.startOnCooldown = true;
        target.maxTokens = 17;
        target.reactionTokenCost = 3;
        target.minTokenForEffect = 2;
        target.applyWhileRecharging = true;
        target.expireAttachedStatusEffect = true;
        var effects = target.passiveEffectBlocks;
        var tokens = target.tokenRate;
        _registry.Add(id, target);
        using var action = Boundary();

        var result = action.Submit(id, Epoch);

        Assert.True(result.Verified, result.Reason);
        Assert.True(target.muted);
        Assert.True(target.touched);
        Assert.False(target.hidden);
        Assert.False(target.silent);
        Assert.True(target.global);
        Assert.True(target.startOnCooldown);
        Assert.Equal(17d, target.maxTokens);
        Assert.Equal(3d, target.reactionTokenCost);
        Assert.Equal(2d, target.minTokenForEffect);
        Assert.True(target.applyWhileRecharging);
        Assert.True(target.expireAttachedStatusEffect);
        Assert.Same(effects, target.passiveEffectBlocks);
        Assert.Same(tokens, target.tokenRate);
        Assert.Equal(1, result.CallOutcome.NativeCallsAttempted);
        Assert.Equal(1, result.CallOutcome.MutationsCommitted);
    }

    [Fact]
    public void An_already_muted_passive_is_refused_without_toggling_it_back()
    {
        var target = SupportedPassive();
        target.muted = true;
        using var action = Boundary();

        var result = action.Submit(target.GetGuid(), Epoch);

        Assert.Equal(QualityOfLifeActionResultCodes.AlreadyMuted, result.Code);
        Assert.True(target.muted);
        Assert.Equal(0, result.CallOutcome.NativeCallsAttempted);
    }

    [Fact]
    public void An_unknown_passive_with_a_supported_diagnostic_name_is_not_mutated()
    {
        var target = Passive(Guid.NewGuid());
        target.name = KnownEntities.PassiveReflectiveMentalLearning.DiagnosticName;
        _registry.Add(target.GetGuid(), target);
        using var action = Boundary();

        var result = action.Submit(target.GetGuid(), Epoch);

        Assert.Equal(QualityOfLifeActionResultCodes.UnsupportedPassive, result.Code);
        Assert.False(target.muted);
        Assert.Equal(0, result.CallOutcome.NativeCallsAttempted);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    public void Eligibility_is_revalidated_at_the_native_action_boundary(bool touched, bool hidden, bool silent)
    {
        var target = SupportedPassive();
        target.touched = touched;
        target.hidden = hidden;
        target.silent = silent;
        using var action = Boundary();

        var result = action.Submit(target.GetGuid(), Epoch);

        Assert.Equal(QualityOfLifeActionResultCodes.NotEligible, result.Code);
        Assert.False(target.muted);
        Assert.Equal(0, result.CallOutcome.NativeCallsAttempted);
    }

    [Fact]
    public void A_new_lifecycle_re_resolves_the_current_native_object_and_refuses_old_plans()
    {
        var epoch = Epoch;
        var old = SupportedPassive();
        using var action = Boundary(readEpoch: () => epoch);
        Assert.True(action.Submit(old.GetGuid(), epoch).Verified);
        var replacement = Passive(old.GetGuid());
        _registry[old.GetGuid()] = replacement;
        epoch++;
        action.InvalidateLifecycle();

        var stale = action.Submit(old.GetGuid(), Epoch);
        Assert.Equal(CommonActionResultCodes.LifecycleReplaced, stale.Code);
        Assert.False(replacement.muted);
        var current = action.Submit(old.GetGuid(), epoch);

        Assert.True(current.Verified, current.Reason);
        Assert.True(replacement.muted);
        Assert.True(old.muted);
    }

    [Theory]
    [MemberData(nameof(RequiredContracts))]
    public void Every_missing_contract_disables_the_complete_binding_set(string missing)
    {
        var target = SupportedPassive();
        using var action = Boundary(includeContract: id => id != missing);

        var result = action.Submit(target.GetGuid(), Epoch);

        Assert.False(action.BindingsAvailable);
        Assert.Contains(missing, action.BindingFailure, StringComparison.Ordinal);
        Assert.Equal(QualityOfLifeActionResultCodes.ContractUnavailable, result.Code);
        Assert.False(target.muted);
    }

    [Fact]
    public void A_submission_from_another_thread_does_not_touch_the_game()
    {
        var target = SupportedPassive();
        using var action = Boundary();

        var id = target.GetGuid();
        var result = ForeignThread.Run(() => action.Submit(id, Epoch));

        Assert.Equal(QualityOfLifeActionResultCodes.WrongThread, result.Code);
        Assert.False(target.muted);
        Assert.Equal(0, result.CallOutcome.NativeCallsAttempted);
    }

    [Fact]
    public void Revoked_mutation_ownership_prevents_the_saved_flag_write()
    {
        var target = SupportedPassive();
        using var action = Boundary(tryPermit: () => false);

        var result = action.Submit(target.GetGuid(), Epoch);

        Assert.Equal(QualityOfLifeActionResultCodes.MutationPermitUnavailable, result.Code);
        Assert.Contains("revoked", result.Reason, StringComparison.Ordinal);
        Assert.False(target.muted);
        Assert.Equal(0, result.CallOutcome.NativeCallsAttempted);
    }

    [Fact]
    public void A_supported_uuid_bound_to_the_wrong_native_type_is_refused()
    {
        var id = KnownEntities.PassiveReflectiveMentalLearning.Uuid;
        var wrongType = new EquipmentTypeSO();
        wrongType.SetGuid(id);
        _registry.Add(id, wrongType);
        using var action = Boundary();

        var result = action.Submit(id, Epoch);

        Assert.Equal(QualityOfLifeActionResultCodes.IdentityUnavailable, result.Code);
        Assert.Equal(0, result.CallOutcome.NativeCallsAttempted);
    }

    [Fact]
    public void A_registry_key_cannot_override_the_native_objects_actual_identity()
    {
        var id = KnownEntities.PassiveReflectiveMentalLearning.Uuid;
        var target = Passive(Guid.NewGuid());
        _registry.Add(id, target);
        using var action = Boundary();

        var result = action.Submit(id, Epoch);

        Assert.Equal(QualityOfLifeActionResultCodes.IdentityUnavailable, result.Code);
        Assert.False(target.muted);
    }

    [Fact]
    public void An_unresolved_supported_uuid_does_not_become_an_empty_or_successful_mutation()
    {
        using var action = Boundary();

        var result = action.Submit(KnownEntities.PassiveReflectiveMentalLearning.Uuid, Epoch);

        Assert.Equal(QualityOfLifeActionResultCodes.IdentityUnavailable, result.Code);
        Assert.False(result.Verified);
        Assert.NotEmpty(result.Reason);
        Assert.Equal(0, result.CallOutcome.NativeCallsAttempted);
    }

    private PassiveNotificationMuteGameAction Boundary(
        Func<long>? readEpoch = null,
        Func<bool>? tryPermit = null,
        Func<string, bool>? includeContract = null)
    {
        var epoch = readEpoch ?? (() => Epoch);
        return new PassiveNotificationMuteGameAction(
            TestRegistryResolver.For(epoch, _registry), epoch,
            tryPermit ?? (() => true), () => "Notification-mute ownership was revoked.",
            includeContract: includeContract);
    }

    private PassiveAbilitySO SupportedPassive()
    {
        var target = Passive(KnownEntities.PassiveReflectiveMentalLearning.Uuid);
        _registry.Add(target.GetGuid(), target);
        return target;
    }

    private static PassiveAbilitySO Passive(Guid id)
    {
        var target = new PassiveAbilitySO { touched = true };
        target.SetGuid(id);
        return target;
    }
}
