using System;
using System.Collections;
using OrbAutomata;
using OrbModding.Common;
using OrbModding.Tests.Services.TestSupport;
using Xunit;

namespace OrbModding.Tests.Services.GenericLevel;

public sealed class GenericLevelGameActionTests
{
    private const long Epoch = 127;
    private readonly IDictionary _registry = new Hashtable();

    [Theory]
    [InlineData("EquipmentTypeSO")]
    [InlineData("GlyphSO")]
    [InlineData("ResourceTypeSO")]
    [InlineData("TimeRuneSO")]
    public void Paid_level_checks_capacity_then_lets_the_native_callback_apply_usage(string nativeType)
    {
        var target = Target(nativeType);
        var resource = Resource(10);
        PaidCost(target).costs.Add(new ResourceTuple(resource, new BigDouble(3)));
        Register(target);
        using var boundary = Boundary();

        var result = Submit(boundary, target, nativeType, GenericLevelActionKind.Purchase);

        Assert.True(result.Verified, result.Reason);
        Assert.Equal(1, TotalLevel(target));
        Assert.Equal(new BigDouble(10), resource.quantity);
        Assert.Equal(0, PaidCost(target).PerformCalls);
    }

    [Theory]
    [InlineData("EquipmentTypeSO")]
    [InlineData("GlyphSO")]
    [InlineData("ResourceTypeSO")]
    public void Bonus_level_uses_the_native_free_level_callback_and_bonus_sentinel(string nativeType)
    {
        var target = Target(nativeType);
        var cost = BonusCost(target);
        cost.costs.Add(new ResourceTuple(Resource(10), new BigDouble(3)));
        Register(target);
        using var boundary = Boundary();

        var result = Submit(boundary, target, nativeType, GenericLevelActionKind.Bonus);

        Assert.True(result.Verified, result.Reason);
        Assert.Equal(1, BonusLevels(target));
        Assert.Equal(0, cost.PerformCalls);
    }

    [Fact]
    public void Time_rune_refuses_a_bonus_control_it_does_not_have()
    {
        var target = Target("TimeRuneSO");
        Register(target);
        using var boundary = Boundary();

        var result = Submit(boundary, target, "TimeRuneSO", GenericLevelActionKind.Bonus);

        Assert.Equal(GenericLevelPreflight.BonusUnavailable, result.Preflight);
    }

    [Fact]
    public void Unaffordable_level_names_the_short_resource_without_payment()
    {
        var target = (EquipmentTypeSO)Target("EquipmentTypeSO");
        var resource = Resource(2);
        target.LevelCost.costs.Add(new ResourceTuple(resource, new BigDouble(3)));
        Register(target);
        using var boundary = Boundary();

        var result = Submit(boundary, target, "EquipmentTypeSO", GenericLevelActionKind.Purchase);

        Assert.Equal(GenericLevelPreflight.Unaffordable, result.Preflight);
        Assert.Equal(0, target.LevelCost.PerformCalls);
        Assert.Contains(resource.GetGuid().ToString("D"), result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Native_no_op_fails_the_directional_level_sentinel()
    {
        var target = new EquipmentTypeSO { SuppressLevelPurchase = true };
        target.SetGuid(Guid.NewGuid());
        Register(target);
        using var boundary = Boundary();

        var result = Submit(boundary, target, "EquipmentTypeSO", GenericLevelActionKind.Purchase);

        Assert.Equal(GenericLevelPreflight.VerificationFailed, result.Preflight);
        Assert.Equal(0, target.GetLevel());
    }

    [Fact]
    public void Prerequisite_learned_pool_glyph_uses_picker_availability_not_discovery_state()
    {
        var target = (GlyphSO)Target("GlyphSO");
        target.discovered = false;
        target.NativeAvailable = true;
        Register(target);
        using var boundary = Boundary();

        var result = Submit(boundary, target, "GlyphSO", GenericLevelActionKind.Purchase);

        Assert.True(result.Verified, result.Reason);
        Assert.Equal(1, target.GetLevel());
    }

    [Fact]
    public void Glyph_hidden_from_the_native_picker_is_refused_before_purchase()
    {
        var target = (GlyphSO)Target("GlyphSO");
        target.discovered = true;
        target.NativeAvailable = false;
        Register(target);
        using var boundary = Boundary();

        var result = Submit(boundary, target, "GlyphSO", GenericLevelActionKind.Purchase);

        Assert.Equal(GenericLevelPreflight.Unavailable, result.Preflight);
        Assert.Equal(0, target.GetLevel());
    }

    [Fact]
    public void Hidden_resource_type_is_refused_before_purchase()
    {
        var target = (ResourceTypeSO)Target("ResourceTypeSO");
        target.specialHidden = true;
        Register(target);
        using var boundary = Boundary();

        var result = Submit(boundary, target, "ResourceTypeSO", GenericLevelActionKind.Purchase);

        Assert.Equal(GenericLevelPreflight.Hidden, result.Preflight);
        Assert.Equal(0, target.GetLevel());
    }

    [Fact]
    public void Unity_thread_is_refused_before_identity_or_native_state()
    {
        var target = Target("GlyphSO");
        Register(target);
        using var boundary = Boundary();

        var result = ForeignThread.Run(() =>
            Submit(boundary, target, "GlyphSO", GenericLevelActionKind.Purchase));

        Assert.Equal(GenericLevelPreflight.WrongThread, result.Preflight);
    }

    [Fact]
    public void Every_missing_member_disables_the_complete_level_matrix()
    {
        foreach (var missing in GenericLevelNativeBindings.ContractIds)
        {
            using var boundary = Boundary(includeContract: id => id != missing);
            Assert.False(boundary.BindingsAvailable);
            Assert.Contains(missing, boundary.BindingFailure, StringComparison.Ordinal);
        }
    }

    private GenericLevelGameAction Boundary(Func<string, bool>? includeContract = null)
    {
        var resolver = new TypedRegistryResolver(() => Epoch,
            () => TypedRegistrySourceSnapshot.Ready(_registry),
            value => value is IdScriptableObject item ? item.GetGuid() : null);
        return new GenericLevelGameAction(() => Epoch, static () => true,
            static () => "GenericLevel ownership was revoked.",
            includeContract: includeContract, registry: resolver);
    }

    private static GenericLevelSubmission Submit(
        GenericLevelGameAction boundary,
        IdScriptableObject target,
        string nativeType,
        GenericLevelActionKind kind)
    {
        var action = new GenericLevelAction(kind, target.GetGuid(), nativeType, 1, Epoch);
        return boundary.Submit(in action);
    }

    private void Register(IdScriptableObject target) => _registry.Add(target.GetGuid(), target);

    private static IdScriptableObject Target(string nativeType)
    {
        IdScriptableObject result = nativeType switch
        {
            "EquipmentTypeSO" => new EquipmentTypeSO(),
            "GlyphSO" => new GlyphSO(),
            "ResourceTypeSO" => new ResourceTypeSO(),
            "TimeRuneSO" => new TimeRuneSO(),
            _ => throw new ArgumentOutOfRangeException(nameof(nativeType)),
        };
        result.SetGuid(Guid.NewGuid());
        if (result is GlyphSO glyph) glyph.discovered = true;
        if (result is TimeRuneSO rune) rune.discovered = true;
        return result;
    }

    private static ResourceSO Resource(int amount)
    {
        var resource = new ResourceSO { quantity = new BigDouble(amount) };
        resource.SetGuid(Guid.NewGuid());
        return resource;
    }

    private static ResourceCostList PaidCost(IdScriptableObject target) => target switch
    {
        EquipmentTypeSO value => value.LevelCost,
        GlyphSO value => value.LevelCost,
        ResourceTypeSO value => value.LevelCost,
        TimeRuneSO value => value.LevelCost,
        _ => throw new ArgumentOutOfRangeException(nameof(target)),
    };

    private static ResourceCostList BonusCost(IdScriptableObject target) => target switch
    {
        EquipmentTypeSO value => value.BonusLevelCost,
        GlyphSO value => value.BonusLevelCost,
        ResourceTypeSO value => value.BonusLevelCost,
        _ => throw new ArgumentOutOfRangeException(nameof(target)),
    };

    private static int TotalLevel(IdScriptableObject target) => target switch
    {
        EquipmentTypeSO value => value.GetLevel(),
        GlyphSO value => value.GetLevel(),
        ResourceTypeSO value => value.GetLevel(),
        TimeRuneSO value => value.GetLevel(),
        _ => throw new ArgumentOutOfRangeException(nameof(target)),
    };

    private static int BonusLevels(IdScriptableObject target) => target switch
    {
        EquipmentTypeSO value => value.GetFreeLevels(),
        GlyphSO value => value.GetFreeLevels(),
        ResourceTypeSO value => value.GetFreeLevels(),
        _ => throw new ArgumentOutOfRangeException(nameof(target)),
    };
}
