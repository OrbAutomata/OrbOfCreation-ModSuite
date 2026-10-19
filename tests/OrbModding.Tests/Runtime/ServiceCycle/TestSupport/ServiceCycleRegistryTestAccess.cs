using System.Reflection;
using OrbModding.Common.Runtime.ServiceCycle.Lifecycle;
using OrbModding.Common.Runtime.ServiceCycle.Registration;

namespace OrbModding.Tests.Runtime.ServiceCycle.TestSupport;

internal static class ServiceCycleRegistryTestAccess
{
    private static readonly FieldInfo ResourceClaimsField = typeof(ServiceCycleRegistry).GetField(
        "_resourceClaims",
        BindingFlags.Instance | BindingFlags.NonPublic)!;

    internal static ServiceResourceClaimLedger ResourceClaims(ServiceCycleRegistry registry) =>
        (ServiceResourceClaimLedger)ResourceClaimsField.GetValue(registry)!;
}
