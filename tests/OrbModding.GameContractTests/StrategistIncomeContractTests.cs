using System.Linq;
using Xunit;

namespace OrbModding.GameContractTests;

public sealed class StrategistIncomeContractTests
{
    /// <summary>
    /// The strategist's sustained income is the difference of two lifetime readings, which is gross
    /// gain only while nothing but a gain moves the counter. In play only <c>RegisterGain</c> stores
    /// it; the other writers reset it, repair a NaN, or load a save.
    /// </summary>
    [GameAssemblyFact]
    public void OnlyAGainMovesTheLifetimeCounterDuringPlay()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        var writers = assembly.GetFieldUseSites(new[] { ("ResourceSO", "lifetimeQuantity") })
            .Where(site => site.Use == "store")
            .Select(site => site.MethodOwner + "." + site.MethodName)
            .Distinct()
            .OrderBy(name => name, System.StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            new[]
            {
                "DevConsoleEngine.ResourceResetLifetime",
                "ResourceSO+ResourceSaveData.ApplyTo",
                "ResourceSO.ClearNans",
                "ResourceSO.RegisterGain",
                "ResourceSO.ResetData",
            },
            writers);
    }
}
