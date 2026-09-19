using System;
using System.Linq;
using System.Reflection;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.Tests.Runtime.ServiceCycle.Registration;

/// <summary>
/// The census of every type wearing a service-cycle trust badge. The badge buys its bearer a way past
/// a structural validator — an audited publication value is admitted without its members being walked
/// — so the question of who may wear it is a review decision, and this file is where that review is
/// recorded.
/// <para>
/// Until now the answer was "whoever Common declares", enforced by an assembly comparison inside the
/// validators. That predicate dies with the one-DLL merge, where it becomes true of every type in the
/// suite and the badge turns into a self-service escape hatch. The assembly boundary no longer says
/// who may wear it; this list does. Equality is exact, never <c>Contains</c>: a new bearer fails a
/// test that names it, and that failure is the review trigger.
/// </para>
/// </summary>
public sealed class ServiceCycleAuditedTypeAllowlistTests
{
    private static readonly Assembly RuntimeAssembly = typeof(ServiceCycleRegistry).Assembly;

    /// <summary>
    /// The badge is honored regardless of declaring assembly, so the feature plugins are censused
    /// too. After the merge these are the same assembly as the runtime and the scan collapses to one.
    /// </summary>
    private static readonly Assembly[] SuiteAssemblies = { RuntimeAssembly };

    [Fact]
    public void AuditedPublicationValuesAreAnExactReviewedSet()
    {
        var bearers = BearersOf(typeof(ServiceCyclePublicationValueAttribute));

        Assert.Equal(
            new[]
            {
                typeof(PublicationTable<>).FullName,
                typeof(EntityIdentityCatalogSnapshot).FullName,
            },
            bearers);
    }

    private static string?[] BearersOf(Type attribute) =>
        SuiteAssemblies
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type.IsDefined(attribute, inherit: false))
            .Select(type => type.FullName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
}
