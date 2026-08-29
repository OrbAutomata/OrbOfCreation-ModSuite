using System;
using System.Collections;
using OrbAutomata;
using OrbModding.Common;
using OrbModding.Tests.Services.TestSupport;
using Xunit;

namespace OrbModding.Tests.Services.StructureLifecycle;

public sealed class StructureLifecycleGameActionTests
{
    private const long Epoch = 131;
    private readonly IDictionary _registry = new Hashtable();

    [Fact]
    public void Disable_and_enable_use_the_ui_toggle_and_disabled_flag_sentinel()
    {
        var structure = Structure();
        Register(structure);
        using var boundary = Boundary();

        var disabled = Submit(boundary, StructureLifecycleActionKind.Disable, structure);
        var enabled = Submit(boundary, StructureLifecycleActionKind.Enable, structure);

        Assert.True(disabled.Verified, disabled.Reason);
        Assert.True(enabled.Verified, enabled.Reason);
        Assert.False(structure.disabled);
        Assert.Equal(1, structure.RemoveEffectsCalls);
        Assert.Equal(1, structure.ApplyEffectsCalls);
    }

    [Fact]
    public void Unavailable_and_already_satisfied_states_refuse_before_mutation()
    {
        var unavailable = Structure();
        unavailable.available = false;
        var disabled = Structure();
        disabled.disabled = true;
        Register(unavailable);
        Register(disabled);
        using var boundary = Boundary();

        var unavailableResult = Submit(
            boundary, StructureLifecycleActionKind.Disable, unavailable);
        var alreadyResult = Submit(
            boundary, StructureLifecycleActionKind.Disable, disabled);

        Assert.Equal(StructureLifecyclePreflight.NotAvailable,
            unavailableResult.Preflight);
        Assert.Equal(StructureLifecyclePreflight.AlreadyInState,
            alreadyResult.Preflight);
        Assert.Equal(0, unavailable.RemoveEffectsCalls);
        Assert.Equal(0, disabled.RemoveEffectsCalls);
    }

    [Fact]
    public void Native_no_op_fails_the_one_disabled_flag_sentinel()
    {
        var structure = Structure();
        structure.ApplyToggleMutation = false;
        Register(structure);
        using var boundary = Boundary();

        var result = Submit(boundary, StructureLifecycleActionKind.Disable, structure);

        Assert.Equal(StructureLifecyclePreflight.VerificationFailed, result.Preflight);
        Assert.False(structure.disabled);
    }

    [Fact]
    public void Unity_thread_is_refused_before_registry_or_native_state()
    {
        var structure = Structure();
        Register(structure);
        using var boundary = Boundary();

        var result = ForeignThread.Run(() =>
            Submit(boundary, StructureLifecycleActionKind.Disable, structure));

        Assert.Equal(StructureLifecyclePreflight.WrongThread, result.Preflight);
        Assert.False(structure.disabled);
    }

    [Fact]
    public void Every_missing_member_disables_the_complete_binding_set()
    {
        foreach (var missing in StructureLifecycleNativeBindings.ContractIds)
        {
            using var boundary = Boundary(includeContract: id => id != missing);
            Assert.False(boundary.BindingsAvailable);
            Assert.Contains(missing, boundary.BindingFailure, StringComparison.Ordinal);
        }
    }

    private StructureLifecycleGameAction Boundary(
        Func<string, bool>? includeContract = null)
    {
        var resolver = new TypedRegistryResolver(() => Epoch,
            () => TypedRegistrySourceSnapshot.Ready(_registry),
            value => value is IdScriptableObject item ? item.GetGuid() : null);
        return new StructureLifecycleGameAction(
            () => Epoch,
            static () => true,
            static () => "StructureLifecycle ownership was revoked.",
            includeContract: includeContract,
            registry: resolver);
    }

    private static StructureLifecycleSubmission Submit(
        StructureLifecycleGameAction boundary,
        StructureLifecycleActionKind kind,
        StructureSO structure)
    {
        var action = new StructureLifecycleAction(kind, structure.GetGuid(), Epoch);
        return boundary.Submit(in action);
    }

    private void Register(StructureSO structure) =>
        _registry.Add(structure.GetGuid(), structure);

    private static StructureSO Structure()
    {
        var structure = new StructureSO();
        structure.SetGuid(Guid.NewGuid());
        return structure;
    }
}
