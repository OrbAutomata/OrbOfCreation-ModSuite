using System;
using OrbAutomata;
using OrbModding.Tests.Services.TestSupport;
using Xunit;

namespace OrbModding.Tests.Services.CraftingStation;

public sealed class CraftingStationGameActionTests : IDisposable
{
    private const long Epoch = 149;

    public CraftingStationGameActionTests() => CraftingStructureSO.All.Clear();

    public void Dispose() => CraftingStructureSO.All.Clear();

    [Fact]
    public void Visible_selectors_level_and_activation_use_the_native_callbacks()
    {
        var surface = Surface();
        using var boundary = Boundary();

        var first = Submit(boundary, surface.Station,
            CraftingStationActionKind.SetIngredient, surface.First.GetGuid(), 0);
        var second = Submit(boundary, surface.Station,
            CraftingStationActionKind.SetIngredient, surface.Second.GetGuid(), 1);
        var level = Submit(boundary, surface.Station,
            CraftingStationActionKind.SetLevel, Guid.Empty, 4);
        var start = Submit(boundary, surface.Station, CraftingStationActionKind.Start);
        var stop = Submit(boundary, surface.Station, CraftingStationActionKind.Stop);

        Assert.True(first.Verified, first.Reason);
        Assert.True(second.Verified, second.Reason);
        Assert.True(level.Verified, level.Reason);
        Assert.True(start.Verified, start.Reason);
        Assert.True(stop.Verified, stop.Reason);
        Assert.Same(surface.First, surface.Station.GetIngredient(0)!.GetTooltipable());
        Assert.Same(surface.Second, surface.Station.GetIngredient(1)!.GetTooltipable());
        Assert.Equal(surface.Recipe.GetGuid(), surface.Station.recipeId.guid);
        Assert.Equal(4, surface.Station.GetLevel());
        Assert.False(surface.Station.IsActive());
    }

    [Fact]
    public void Output_selection_uses_the_visible_output_and_rebuilds_the_recipe()
    {
        var surface = Surface();
        using var boundary = Boundary();

        var result = Submit(boundary, surface.Station,
            CraftingStationActionKind.SetOutput, surface.Output.GetGuid());

        Assert.True(result.Verified, result.Reason);
        Assert.True(surface.Station.IsLoaded());
        Assert.Equal(surface.Recipe.GetGuid(), surface.Station.recipeId.guid);
        Assert.Same(surface.First, surface.Station.GetIngredient(0)!.GetTooltipable());
        Assert.Same(surface.Second, surface.Station.GetIngredient(1)!.GetTooltipable());
    }

    [Fact]
    public void Hidden_selection_and_unloaded_start_are_admission_refusals()
    {
        var surface = Surface(secondAvailable: false);
        using var boundary = Boundary();

        var hidden = Submit(boundary, surface.Station,
            CraftingStationActionKind.SetIngredient, surface.Second.GetGuid(), 1);
        var unloaded = Submit(boundary, surface.Station, CraftingStationActionKind.Start);

        Assert.Equal(CraftingStationPreflight.SelectionHidden, hidden.Preflight);
        Assert.Equal(CraftingStationPreflight.NotLoaded, unloaded.Preflight);
        Assert.False(surface.Station.IsActive());
    }

    [Fact]
    public void Native_no_op_fails_the_one_observable_outcome_sentinel()
    {
        var surface = Surface();
        surface.Station.SuppressMutation = true;
        using var boundary = Boundary();

        var result = Submit(boundary, surface.Station,
            CraftingStationActionKind.SetIngredient, surface.First.GetGuid(), 0);

        Assert.Equal(CraftingStationPreflight.VerificationFailed, result.Preflight);
        Assert.Null(surface.Station.GetIngredient(0));
    }

    [Fact]
    public void Unity_thread_is_refused_before_native_state()
    {
        var surface = Surface();
        using var boundary = Boundary();

        var result = ForeignThread.Run(() => Submit(boundary, surface.Station,
            CraftingStationActionKind.SetIngredient, surface.First.GetGuid(), 0));

        Assert.Equal(CraftingStationPreflight.WrongThread, result.Preflight);
        Assert.Null(surface.Station.GetIngredient(0));
    }

    [Fact]
    public void Every_missing_member_disables_the_complete_station_family()
    {
        foreach (var missing in CraftingStationNativeBindings.ContractIds)
        {
            using var boundary = Boundary(id => id != missing);
            Assert.False(boundary.BindingsAvailable);
            Assert.Contains(missing, boundary.BindingFailure, StringComparison.Ordinal);
        }
    }

    private static CraftingStationGameAction Boundary(Func<string, bool>? include = null) =>
        new(() => Epoch, static () => true, static () => "Crafting ownership was revoked.",
            Resolve, include);

    private static Type? Resolve(string name) => name switch
    {
        "CraftingStructureSO" => typeof(CraftingStructureSO),
        "CraftingStructure" => typeof(CraftingStructure),
        "CraftingStructureListVariable" => typeof(CraftingStructureListVariable),
        "CraftingStructureSO+TypeListElement" => typeof(CraftingStructureSO.TypeListElement),
        "CraftingStructureSO+TypeElement" => typeof(CraftingStructureSO.TypeElement),
        "ITooltipable" => typeof(ITooltipable),
        "TooltipableObject" => typeof(TooltipableObject),
        _ => null,
    };

    private static CraftingStationSubmission Submit(
        CraftingStationGameAction boundary,
        CraftingStructure station,
        CraftingStationActionKind kind,
        Guid selection = default,
        int value = 0)
    {
        var action = new CraftingStationAction(kind, station.GetGuid(), selection, value, Epoch);
        return boundary.Submit(in action);
    }

    private static SurfaceState Surface(bool secondAvailable = true)
    {
        var first = Resource("Water", new BigDouble(10));
        var second = Resource("Leaf", new BigDouble(10));
        var output = Resource("Tonic", BigDouble.Zero);
        var firstElement = new CraftingStructureSO.TypeElement(first);
        var secondElement = new CraftingStructureSO.TypeElement(second) { Available = secondAvailable };
        var outputElement = new CraftingStructureSO.TypeElement(output);
        var structure = new CraftingStructureSO();
        structure.SetGuid(Guid.NewGuid());
        structure.ingredientLists.Add(new CraftingStructureSO.TypeListElement { elements = { firstElement } });
        structure.ingredientLists.Add(new CraftingStructureSO.TypeListElement { elements = { secondElement } });
        var recipe = new CraftingStructureSO.Recipe
        {
            ingredients = { firstElement, secondElement },
            output = outputElement,
        };
        structure.recipes.Add(recipe);
        CraftingStructureSO.All.Add(structure);
        var station = new CraftingStructure(structure);
        return new SurfaceState(station, recipe, first, second, output);
    }

    private static ResourceSO Resource(string name, BigDouble quantity)
    {
        var resource = new ResourceSO { name = name, quantity = quantity };
        resource.SetGuid(Guid.NewGuid());
        return resource;
    }

    private readonly record struct SurfaceState(
        CraftingStructure Station,
        CraftingStructureSO.Recipe Recipe,
        ResourceSO First,
        ResourceSO Second,
        ResourceSO Output);
}
