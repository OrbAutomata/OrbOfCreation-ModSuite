using System.Reflection;
using System.Runtime.CompilerServices;
using OrbModding.GameStubs;
using Xunit.Sdk;

namespace OrbModding.TestSupport;

/// <summary>One initial reset and a reset after every case preserve constructor-authored fixtures.</summary>
public sealed class ResetGameStubsAttribute : BeforeAfterTestAttribute
{
    [ModuleInitializer]
    internal static void InitializeNativeModel() => GameStubState.Reset();

    // A receipt from the actual runner hook lets its installation test detect missing registration.
    internal static MethodInfo? CurrentCase { get; private set; }

    public override void Before(MethodInfo methodUnderTest) => CurrentCase = methodUnderTest;

    // xUnit invokes Before after the test constructor. Resetting there would erase the fixture the
    // constructor just authored. The initial reset above and each previous case's After own cleanup.
    public override void After(MethodInfo methodUnderTest)
    {
        try { GameStubState.Reset(); }
        finally { CurrentCase = null; }
    }
}
