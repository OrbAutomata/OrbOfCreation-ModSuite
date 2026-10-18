using System.Linq;
using Xunit;

namespace OrbModding.GameContractTests;

/// <summary>
/// The Unity calls a profiling build's full trace makes to sample the heap every pump. A player that
/// dropped one would fail to load the profiling build rather than record a wrong number, and this
/// says so before an install does.
/// </summary>
public sealed class ProfilingHeapCounterContractTests
{
    [GameAssemblyFact]
    public void TheUsedHeapReadingIsAStaticLong()
    {
        var paths = GameAssemblyPaths.Require();
        using var unity = new GameAssemblyMetadata(paths.UnityCore);

        AssertMethod(unity, "UnityEngine.Profiling.Profiler", "GetMonoUsedSizeLong", isStatic: true, "System.Int64");
    }

    [GameAssemblyFact]
    public void TheFrameAllocationRecorderStartsByCategoryAndName()
    {
        var paths = GameAssemblyPaths.Require();
        using var unity = new GameAssemblyMetadata(paths.UnityCore);

        AssertMethod(
            unity,
            "Unity.Profiling.ProfilerRecorder",
            "StartNew",
            isStatic: true,
            "Unity.Profiling.ProfilerRecorder",
            "Unity.Profiling.ProfilerCategory",
            "System.String",
            "System.Int32",
            "Unity.Profiling.ProfilerRecorderOptions");
        AssertMethod(unity, "Unity.Profiling.ProfilerCategory", "get_Memory", isStatic: true, "Unity.Profiling.ProfilerCategory");
        AssertMethod(unity, "Unity.Profiling.ProfilerRecorder", "get_Valid", isStatic: false, "System.Boolean");
        AssertMethod(unity, "Unity.Profiling.ProfilerRecorder", "get_LastValue", isStatic: false, "System.Int64");
        AssertMethod(unity, "Unity.Profiling.ProfilerRecorder", "Dispose", isStatic: false, "System.Void");
    }

    private static void AssertMethod(
        GameAssemblyMetadata assembly,
        string typeName,
        string methodName,
        bool isStatic,
        string returnType,
        params string[] parameters)
    {
        Assert.Contains(assembly.GetMethods(typeName, methodName), method =>
            method.IsStatic == isStatic &&
            method.ReturnType == returnType &&
            method.ParameterTypes.SequenceEqual(parameters));
    }
}
