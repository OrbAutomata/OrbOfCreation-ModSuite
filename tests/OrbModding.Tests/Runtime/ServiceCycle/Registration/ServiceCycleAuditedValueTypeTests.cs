using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.Tests.Runtime.ServiceCycle.TestSupport;
using Xunit;
using static OrbModding.Tests.Runtime.ServiceCycle.TestSupport.ServiceCycleTypeSafetyFixtures;

namespace OrbModding.Tests.Runtime.ServiceCycle.Registration;

/// <summary>
/// Locks in the deliberate, curated exception that lets service-cycle data shapes carry the game's
/// immutable <c>BigDouble</c> value math by value. The exception is narrow by design: only that exact
/// audited value type crosses the boundary — every other type from the same game assembly stays
/// rejected, so the assembly-name wall is not weakened for anything else.
/// </summary>
public sealed class ServiceCycleAuditedValueTypeTests
{
    [Fact]
    public void AuditedBigDoubleValueMathCrossesTheBoundary()
    {
        using var registry = new ServiceCycleRegistry(1);
        AssertFrameAccepted(new BigDoubleFrame(new BigDouble(1.446, 23)));
    }

    [Fact]
    public void OtherGameAssemblyTypesStayRejected()
    {
        using var registry = new ServiceCycleRegistry(1);
        AssertFrameRejected(new ResourceTupleFrame(default));
    }

    [Fact]
    public void AnArrayIsJudgedByItsElement()
    {
        AssertFrameAccepted(new BigDoubleArrayFrame(new BigDouble[1]));
        AssertFrameRejected(new ResourceTupleArrayFrame(new global::ResourceTuple[1]));
    }

    private readonly struct BigDoubleFrame
    {
        private readonly BigDouble _magnitude;
        internal BigDoubleFrame(BigDouble magnitude) => _magnitude = magnitude;
    }

    private readonly struct ResourceTupleFrame
    {
        private readonly global::ResourceTuple _tuple;
        internal ResourceTupleFrame(global::ResourceTuple tuple) => _tuple = tuple;
    }

    private readonly struct BigDoubleArrayFrame
    {
        private readonly BigDouble[] _magnitudes;
        internal BigDoubleArrayFrame(BigDouble[] magnitudes) => _magnitudes = magnitudes;
    }

    private readonly struct ResourceTupleArrayFrame
    {
        private readonly global::ResourceTuple[] _tuples;
        internal ResourceTupleArrayFrame(global::ResourceTuple[] tuples) => _tuples = tuples;
    }
}
