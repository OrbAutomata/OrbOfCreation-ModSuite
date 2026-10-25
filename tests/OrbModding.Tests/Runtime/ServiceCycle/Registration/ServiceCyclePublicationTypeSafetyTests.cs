using System;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.Common.Runtime.World;
using OrbModding.Common.Runtime;
using OrbModding.Tests.Runtime.ServiceCycle.TestSupport;
using Xunit;
using static OrbModding.Tests.Runtime.ServiceCycle.TestSupport.ServiceCycleTypeSafetyFixtures;

namespace OrbModding.Tests.Runtime.ServiceCycle.Registration;

public sealed class ServiceCyclePublicationTypeSafetyTests
{
    [Fact]
    public void RegistrationAcceptsSealedStorageAndDeepReadonlyPublishedValues()
    {
        using var registry = new ServiceCycleRegistry(1);
        using var registration = registry.Register(
            new TypeSafetyDefinition<SafeState, ImmutableAction>(
                new SafeState()),
            new LifecycleGeneration(1));

        Assert.Same(TestSuiteConfiguration.Default, registry.Configuration.ReadLatest().Snapshot);
    }

    /// <summary>
    /// The snapshot every service actually reads passes the audit its role defines.
    /// </summary>
    /// <remarks>
    /// There is one record now, and this is where it is proved — a setting added as a mutable
    /// property, an array, or anything holding a Unity object fails here rather than wherever the
    /// first service happens to be registered.
    /// </remarks>
    [Fact]
    public void TheSuiteConfigurationPassesTheDeepImmutabilityAudit()
    {
        var violation = ServiceCycleTypeSafetyValidator.ValidateSuiteConfiguration();

        Assert.False(violation.HasValue, violation.HasValue ? violation.Value.Message : string.Empty);
    }

    /// <summary>
    /// The bulletin every service is handed passes the audit its role defines.
    /// </summary>
    /// <remarks>
    /// Proved here rather than at construction for the same reason the configuration is: the suite
    /// names one bulletin type, so the audit belongs where the type is named.
    /// </remarks>
    [Fact]
    public void TheSuiteStrategyPassesTheDeepImmutabilityAudit()
    {
        var violation = ServiceCycleTypeSafetyValidator.ValidateSuiteStrategy();

        Assert.False(violation.HasValue, violation.HasValue ? violation.Value.Message : string.Empty);
    }

    [Fact]
    public void CyclicSealedReadonlyPublicationGraphTerminatesAndIsAccepted() =>
        AssertConfigurationAccepted(new CyclicConfig(new CyclicConfig(null)));

    [Fact]
    public void MutableConfigurationAndActionShapesAreRejectedBeforeConstruction()
    {
        AssertConfigurationRejected(new MutableConfig());

        using var registry = new ServiceCycleRegistry(1);
        var mutableAction = new TypeSafetyDefinition<SafeState, MutableAction>(
            new SafeState());
        Assert.Throws<InvalidOperationException>(() => registry.Register(
            mutableAction, new LifecycleGeneration(1)));
    }

    [Fact]
    public void ArraysDelegatesAndUnityTypesAreRejectedFromPublishedGraphs()
    {
        AssertConfigurationRejected(new ArrayConfig(new[] { 1 }));
        AssertConfigurationRejected(new DelegateConfig(() => { }));
        AssertConfigurationRejected(new UnityConfig(null));
    }

    [Fact]
    public void StaticStorageIsRejectedButLiteralConstantsRemainSafe()
    {
        AssertConfigurationRejected(new StaticUnityCacheConfig(1));
        AssertConfigurationAccepted(new ConstantBearingConfig(ConstantBearingConfig.SchemaVersion));
    }

    [Fact]
    public void ActionsCannotBeOrRecursivelyRetainTheCaptureBuffer()
    {
        using var registry = new ServiceCycleRegistry(1);
        Assert.Throws<InvalidOperationException>(() => registry.Register(
            new TypeSafetyDefinition<SafeState, GameWorldCycleFrame>(
                new SafeState()),
            new LifecycleGeneration(1)));
        Assert.Throws<InvalidOperationException>(() => registry.Register(
            new TypeSafetyDefinition<SafeState, ActionWithCaptureBuffer>(
                new SafeState()),
            new LifecycleGeneration(1)));
    }

    private sealed class CyclicConfig
    {
        private readonly CyclicConfig? _next;
        internal CyclicConfig(CyclicConfig? next) => _next = next;
        public CyclicConfig? Next => _next;
    }
    private sealed class MutableConfig { internal int Value { get; set; } }
    private sealed class MutableAction { internal int Value { get; set; } }
    private readonly struct ArrayConfig
    {
        private readonly int[] _values;
        internal ArrayConfig(int[] values) => _values = values;
    }
    private readonly struct DelegateConfig
    {
        private readonly Action _action;
        internal DelegateConfig(Action action) => _action = action;
    }
    private sealed class UnityConfig
    {
        private readonly UnityEngine.Object? _value;
        internal UnityConfig(UnityEngine.Object? value) => _value = value;
    }
    private sealed class StaticUnityCacheConfig
    {
        private static UnityEngine.Object? _cache = null;
        private readonly int _value;
        internal StaticUnityCacheConfig(int value) => _value = value;
        internal int Value => _value;
        private static UnityEngine.Object? Cache => _cache;
    }
    private sealed class ConstantBearingConfig
    {
        public const int SchemaVersion = 1;
        private readonly int _value;
        internal ConstantBearingConfig(int value) => _value = value;
        internal int Value => _value;
    }
    private readonly struct ActionWithCaptureBuffer
    {
        private readonly GameWorldCycleFrame _frame;
        internal ActionWithCaptureBuffer(GameWorldCycleFrame frame) => _frame = frame;
    }
}
