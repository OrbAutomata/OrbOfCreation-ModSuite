using System;
using System.Collections.Generic;
using OrbModding.Common.Runtime.World;

namespace OrbModding.Common;

internal enum EntityIdentityNameSource
{
    None = 0,
    LiveDisplayName = 1,
    LiveAssetName = 2,
    KnownEntityBootstrap = 3,
}

internal readonly struct EntityIdentityDescription
{
    internal EntityIdentityDescription(
        Guid uuid,
        string name,
        string assetName,
        string runtimeType,
        EntityIdentityNameSource source)
    {
        Uuid = uuid;
        Name = name ?? string.Empty;
        AssetName = assetName ?? string.Empty;
        RuntimeType = runtimeType ?? string.Empty;
        Source = source;
    }

    internal Guid Uuid { get; }
    internal string Name { get; }
    internal string AssetName { get; }
    internal string RuntimeType { get; }
    internal EntityIdentityNameSource Source { get; }
    internal bool HasName => Source != EntityIdentityNameSource.None && Name.Length > 0;
}

/// <summary>
/// The suite's only UUID-to-name rendering facade.
/// </summary>
/// <remarks>
/// Formatting is pure managed lookup over an immutable snapshot. It never reflects, touches Unity,
/// or consults the offline TSVs. The UUID is retained in every rendered result because names are
/// diagnostics, never identity.
/// </remarks>
internal static class EntityIdentityFormatter
{
    private static readonly object Sync = new();
    private static readonly HashSet<Guid> WarnedMisses = new();
    private static Action<string> _warning = static _ => { };
    private static Action<string> _error = static _ => { };
    private static long _generation;

    internal static void ConfigureDiagnostics(Action<string> warning, Action<string> error)
    {
        lock (Sync)
        {
            _warning = warning ?? throw new ArgumentNullException(nameof(warning));
            _error = error ?? throw new ArgumentNullException(nameof(error));
        }
    }

    internal static void Reset(long generation)
    {
        lock (Sync)
        {
            _generation = generation;
            WarnedMisses.Clear();
        }
    }

    internal static EntityIdentityDescription Describe(
        Guid uuid,
        EntityIdentityCatalogSnapshot? snapshot = null)
    {
        try
        {
            snapshot ??= EntityIdentityCatalogPublication.Current;
            if (snapshot.IsBound && snapshot.TryGet(uuid, out var live))
            {
                if (live.DisplayName.Length > 0)
                {
                    return new EntityIdentityDescription(
                        uuid,
                        live.DisplayName,
                        live.AssetName,
                        live.RuntimeType,
                        EntityIdentityNameSource.LiveDisplayName);
                }
                if (live.AssetName.Length > 0)
                {
                    return new EntityIdentityDescription(
                        uuid,
                        live.AssetName,
                        live.AssetName,
                        live.RuntimeType,
                        EntityIdentityNameSource.LiveAssetName);
                }
                WarnPostBindMiss(snapshot.Generation, uuid);
                return new EntityIdentityDescription(
                    uuid, string.Empty, string.Empty, live.RuntimeType,
                    EntityIdentityNameSource.None);
            }

            if (snapshot.State == EntityIdentityCatalogState.Unbound &&
                KnownEntityBootstrap.TryGet(uuid, out var known))
            {
                return new EntityIdentityDescription(
                    uuid, known, string.Empty, string.Empty,
                    EntityIdentityNameSource.KnownEntityBootstrap);
            }

            if (snapshot.IsBound) WarnPostBindMiss(snapshot.Generation, uuid);
        }
        catch (Exception)
        {
            // Diagnostics must remain total even if handed a malformed or stale snapshot.
        }
        return new EntityIdentityDescription(
            uuid, string.Empty, string.Empty, string.Empty,
            EntityIdentityNameSource.None);
    }

    internal static string Format(
        Guid uuid,
        EntityIdentityCatalogSnapshot? snapshot = null)
    {
        var canonical = uuid.ToString("D");
        try
        {
            var description = Describe(uuid, snapshot);
            if (!description.HasName) return canonical;
            if (description.Source == EntityIdentityNameSource.LiveDisplayName &&
                description.AssetName.Length > 0 &&
                !string.Equals(
                    description.Name, description.AssetName, StringComparison.Ordinal))
            {
                return description.Name + " [" + description.AssetName + "] (" + canonical + ")";
            }
            return description.Name + " (" + canonical + ")";
        }
        catch (Exception)
        {
            return canonical;
        }
    }

    /// <summary>
    /// The name a player-facing sentence uses. It is the display name alone: the asset name and the
    /// UUID are identity, and every response that carries a sentence already carries them as its
    /// own fields, where a caller can read them without parsing prose.
    /// </summary>
    /// <remarks>
    /// An entity with no known name falls back to its UUID, because a sentence that names nothing
    /// at all is worse than one that names the only handle there is. <see cref="Format"/> keeps the
    /// full diagnostic form for logs and traces, which are read by whoever is debugging the suite.
    /// </remarks>
    internal static string PlayerName(
        Guid uuid,
        EntityIdentityCatalogSnapshot? snapshot = null)
    {
        try
        {
            var description = Describe(uuid, snapshot);
            return description.HasName ? description.Name : uuid.ToString("D");
        }
        catch (Exception)
        {
            return uuid.ToString("D");
        }
    }

    internal static void ReportCatalogFailure(string message)
    {
        try
        {
            Action<string> error;
            lock (Sync) error = _error;
            error(message);
        }
        catch (Exception)
        {
            // A diagnostics sink cannot turn optional naming into a gameplay failure.
        }
    }

    private static void WarnPostBindMiss(long generation, Guid uuid)
    {
        Action<string>? warning = null;
        lock (Sync)
        {
            if (_generation != generation)
            {
                _generation = generation;
                WarnedMisses.Clear();
            }
            if (WarnedMisses.Add(uuid)) warning = _warning;
        }
        if (warning is null) return;
        try
        {
            // UUID-only by design: formatting the warning would recurse into this same miss.
            warning("Live entity-name catalog has no label for UUID " + uuid.ToString("D"));
        }
        catch (Exception)
        {
        }
    }
}

/// <summary>The 62 generated suite contracts, used only before the live catalog binds.</summary>
internal static class KnownEntityBootstrap
{
    private readonly struct EntryValue
    {
        internal EntryValue(Guid uuid, string name)
        {
            Uuid = uuid;
            Name = name;
        }

        internal Guid Uuid { get; }
        internal string Name { get; }
    }

    private static readonly EntryValue[] Entries =
    {
        Entry(KnownEntities.ActiveActionables),
        Entry(KnownEntities.ActiveConcepts),
        Entry(KnownEntities.ActivePlotNodeActions),
        Entry(KnownEntities.ActiveScribeInstances),
        Entry(KnownEntities.ActiveSpells),
        Entry(KnownEntities.Alchemy),
        Entry(KnownEntities.AlchemyScreen),
        Entry(KnownEntities.AutoScribeInstances),
        Entry(KnownEntities.Brewing),
        Entry(KnownEntities.BulkDevelopment),
        Entry(KnownEntities.CompletionScalingWeight),
        Entry(KnownEntities.ConceptRecipes),
        Entry(KnownEntities.Conceptualization),
        Entry(KnownEntities.ConsumableFruitType),
        Entry(KnownEntities.ConsumablePotionType),
        Entry(KnownEntities.ConsumableRelicType),
        Entry(KnownEntities.ConsumableScrollType),
        Entry(KnownEntities.ConsumableThreadType),
        Entry(KnownEntities.CraftScrollAdvancement),
        Entry(KnownEntities.CraftScrollDevelopment),
        Entry(KnownEntities.CraftScrollEcho),
        Entry(KnownEntities.CraftScrollExcellence),
        Entry(KnownEntities.CraftScrollLearning),
        Entry(KnownEntities.CraftScrollPower),
        Entry(KnownEntities.CreatedWorldAspects),
        Entry(KnownEntities.Dismantle),
        Entry(KnownEntities.EnchantAdvancement),
        Entry(KnownEntities.EnchantDevelopment),
        Entry(KnownEntities.EnchantEcho),
        Entry(KnownEntities.EnchantExcellence),
        Entry(KnownEntities.EnchantInvestment),
        Entry(KnownEntities.EnchantLearning),
        Entry(KnownEntities.EnchantPower),
        Entry(KnownEntities.EnchantSpeed),
        Entry(KnownEntities.Enchantment),
        Entry(KnownEntities.FruitTreeCollect),
        Entry(KnownEntities.FruitTreePlot),
        Entry(KnownEntities.FruitTreeRewardPool),
        Entry(KnownEntities.MagicSpellbook),
        Entry(KnownEntities.MasteriesEnabled),
        Entry(KnownEntities.MultiBuy),
        Entry(KnownEntities.PotionToxicity),
        Entry(KnownEntities.Reductive),
        Entry(KnownEntities.Refinement),
        Entry(KnownEntities.Reflective),
        Entry(KnownEntities.ScribeCrafting),
        Entry(KnownEntities.ScribeCraftingRecipes),
        Entry(KnownEntities.ScrollAdvancement),
        Entry(KnownEntities.ScrollDevelopment),
        Entry(KnownEntities.ScrollEcho),
        Entry(KnownEntities.ScrollExcellence),
        Entry(KnownEntities.ScrollInvestment),
        Entry(KnownEntities.ScrollLearning),
        Entry(KnownEntities.ScrollPower),
        Entry(KnownEntities.ScrollSpeed),
        Entry(KnownEntities.Transmutation),
        Entry(KnownEntities.TreasureTreeCollect),
        Entry(KnownEntities.TreasureTreePlot),
        Entry(KnownEntities.TreasureTreeRewardPool),
        Entry(KnownEntities.UnlockLevelAllSpells),
        Entry(KnownEntities.WorkshopArtifact),
        Entry(KnownEntities.WorldAspectSlots),
    };

    private static readonly Dictionary<Guid, string> ByUuid = Build();

    internal static int Count => Entries.Length;

    internal static bool TryGet(Guid uuid, out string name) =>
        ByUuid.TryGetValue(uuid, out name!);

    private static EntryValue Entry<TContract>(KnownEntity<TContract> entity) =>
        new(entity.Uuid, entity.DiagnosticName);

    private static Dictionary<Guid, string> Build()
    {
        var result = new Dictionary<Guid, string>(Entries.Length);
        for (var index = 0; index < Entries.Length; index++)
            result.Add(Entries[index].Uuid, Entries[index].Name);
        return result;
    }
}
