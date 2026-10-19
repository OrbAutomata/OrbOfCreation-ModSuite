using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata.Ecma335;
using OrbModding.Common;
using Xunit;

namespace OrbModding.GameContractTests;

public sealed class ProductionContractIdsTests
{
    /// <remarks>
    /// The owners are found in the suite's metadata and then loaded one at a time, because the game
    /// and Unity assemblies are not beside this test, so loading every suite type at once throws.
    /// </remarks>
    [Fact]
    public void EveryProductionContractIdIsExactlyOneManifestContract()
    {
        var manifest = NativeContractManifest.Load();
        var suite = typeof(PluginIds).Assembly;
        using var metadata = new GameAssemblyMetadata(suite.Location);
        var reader = metadata.Reader;
        var owners = reader.TypeDefinitions
            .Where(handle => reader.GetTypeDefinition(handle).GetFields().Any(fieldHandle =>
            {
                var field = reader.GetFieldDefinition(fieldHandle);
                return reader.StringComparer.Equals(field.Name, "ContractIds")
                    && (field.Attributes & FieldAttributes.Static) != 0;
            }))
            .Select(handle => suite.ManifestModule.ResolveType(MetadataTokens.GetToken(handle)))
            .ToList();
        Assert.NotEmpty(owners);

        var failures = new List<string>();
        foreach (var owner in owners)
        {
            var field = owner.GetField(
                "ContractIds",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)!;
            foreach (var id in (string[])field.GetValue(null)!)
            {
                var matches = manifest.Contracts.Count(contract => contract.Id == id);
                if (matches != 1)
                {
                    failures.Add($"{owner.FullName}.ContractIds: '{id}' is {matches} manifest contracts, not exactly one");
                }
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }
}
