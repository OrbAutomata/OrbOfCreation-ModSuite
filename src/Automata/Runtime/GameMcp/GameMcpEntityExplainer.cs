#if SERVICE_CYCLE_PROFILE
using System;
using System.Collections.Generic;
using OrbModding.Common;
using OrbModding.Common.Runtime.World;
using JObject = OrbAutomata.GameMcp.GameMcpObjectBuilder;
using JArray = OrbAutomata.GameMcp.GameMcpArrayBuilder;

namespace OrbAutomata.GameMcp;

/// <summary>Evaluated, one-publication explanation for one stable entity identity.</summary>
internal static class GameMcpEntityExplainer
{
    private const int MaximumRequirementExpansionDepth = 32;

    /// <summary>
    /// Bound on first use and kept, because binding is the expensive half and every request that
    /// reaches it runs on the same Unity thread inside a frame operation.
    /// </summary>
    private static WorldRequirementNativeVerdictProbe? _nativeVerdicts;

    private static WorldRequirementNativeVerdictProbe NativeVerdicts =>
        _nativeVerdicts ??= new WorldRequirementNativeVerdictProbe();

    internal static JObject Explain(GameMcpFrameContext state, string uuidText)
    {
        if (!Guid.TryParseExact(uuidText ?? string.Empty, "D", out var uuid) || uuid == Guid.Empty)
        {
            return GameMcpWorldQuery.NotAvailableWithoutWorld(
                state,
                "invalid_uuid",
                "uuid must be a non-empty canonical D-format GUID");
        }
        var publication = state.World;
        if (publication is null || publication.Generation.Value <= 1 ||
            publication.Snapshot.CollectedAtUtcTicks <= 0)
        {
            return GameMcpWorldQuery.NotAvailableWithoutWorld(
                state,
                "world_not_published",
                state.RuntimeNotAvailableReason.Length == 0
                    ? "the world collector has not published a captured world yet"
                    : state.RuntimeNotAvailableReason);
        }

        var world = publication.Snapshot;
        if (!TryResolve(world, uuid, out var kind, out var row, out var nativeType))
        {
            var known = world.EntityIdentities.TryGet(uuid, out var identity);

            // A runtime instance is not a loaded asset, so the asset catalog does not know it —
            // but the world published it minutes earlier inside a composite row. Answering
            // "nothing in this process knows this UUID" was wrong about the process and pointed at
            // a registry that could never resolve it.
            if (!known && TryOwningList(world, uuid, out var owningCategory))
            {
                return GameMcpWorldQuery.WithEnvelope(state, new JObject
                {
                    ["status"] = "not_available",
                    ["code"] = "not_world_projected",
                    ["reason"] =
                        "this is a runtime member of a published row rather than an entity of " +
                        "its own; read the row that owns it",
                    ["uuid"] = uuid.ToString("D"),
                    ["readWith"] = new JObject
                    {
                        ["tool"] = "world_list",
                        ["category"] = owningCategory,
                    },
                });
            }
            var code = known ? "not_world_projected" : "uuid_unknown";
            var reason = known
                ? "this entity exists but has no detailed explanation; read its published category with world_get"
                : "nothing in this process knows this UUID; search entity_catalog by name";
            var remedy = new JObject { ["tool"] = "entity_catalog" };
            if (known && GameMcpEntityCapabilityMap.TryCategoryForNativeType(
                    identity.RuntimeType,
                    out var knownCategory))
            {
                remedy["tool"] = "world_get";
                remedy["category"] = knownCategory;
            }
            return GameMcpWorldQuery.WithEnvelope(state, new JObject
            {
                ["status"] = "not_available",
                ["code"] = code,
                ["reason"] = reason,
                ["uuid"] = uuid.ToString("D"),
                ["readWith"] = remedy,
            });
        }

        var predicates = Predicates(world, uuid, kind);
        var requirements = Requirements(
            world, uuid, kind, out var parityFailure, out var parityFailureCode);
        var result = new JObject
        {
            ["status"] = parityFailure is null ? "available" : "not_available",
        };
        var description = TryReadNativeDescription(uuid, nativeType);
        if (description.Length > 0) result["description"] = description;
        result.CopyFrom(GameMcpEntityCatalog.Lookup(world.EntityIdentities, uuid));
        result["kind"] = kind.ToString();
        result["state"] = GameMcpEntityCapabilityMap.TryCategoryForNativeType(
            nativeType,
            out var category)
                ? GameMcpWorldQuery.ProjectEntityState(world, category, row)
                : new GameMcpDomainValue(row);
        // Both blocks are always present. An entity with no applicable predicate and one whose
        // predicates were never evaluated are different answers, and an omitted key said both.
        result["predicates"] = predicates;
        if (requirements is not null) result["requirements"] = requirements;
        var researchThresholds = ResearchThresholds(world, uuid, kind);
        if (researchThresholds is not null) result["researchThresholds"] = researchThresholds;
        var purchase = Purchase(world, uuid, kind);
        if (purchase is not null) result["purchase"] = purchase;
        result["blockers"] = Blockers(world, uuid, kind);
        if (parityFailure is not null)
        {
            result["code"] = parityFailureCode;
            result["reason"] = parityFailure;
        }
        return GameMcpWorldQuery.WithEnvelope(state, result);
    }

    /// <summary>
    /// The published list whose rows carry this UUID as a member rather than as their own identity.
    /// Equipped spell instances are the one such member the world publishes; nothing here guesses
    /// at a category that does not actually name the UUID it was handed.
    /// </summary>
    private static bool TryOwningList(GameWorldState world, Guid uuid, out string category)
    {
        for (var index = 0; index < world.SpellSlots.Count; index++)
        {
            if (world.SpellSlots[index].SpellInstanceId != uuid) continue;
            category = "spell-slots";
            return true;
        }
        category = string.Empty;
        return false;
    }

    private static string TryReadNativeDescription(Guid uuid, string nativeType)
    {
        try
        {
            var type = Type.GetType(nativeType + ", Assembly-CSharp", throwOnError: false);
            if (type is null) return string.Empty;
            var resolved = TypedRegistryResolver.Shared.Resolve(uuid, type);
            if (!resolved.IsResolved || resolved.Value is not ITooltipable tooltip)
                return string.Empty;
            return tooltip.GetDescription()?.Trim() ?? string.Empty;
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    private static JObject Predicates(GameWorldState world, Guid id, EntityKind kind)
    {
        var result = new JObject();
        switch (kind)
        {
            case EntityKind.Structure:
            {
                WorldLookup.TryFind(world.Structures, id, out var structure);
                result["available"] = Verdict(
                    structure.Reading.Unlocked,
                    "native_unavailable");
                result["canPurchase"] = PurchaseVerdict(
                    world, id, structure.Reading.Unlocked, "native_unavailable");
                break;
            }
            case EntityKind.Upgrade:
            {
                WorldLookup.TryFind(world.Upgrades, id, out var upgrade);
                result["available"] = Verdict(
                    upgrade.Reading.Available,
                    "native_unavailable");
                result["canPurchase"] = PurchaseVerdict(
                    world, id, upgrade.Reading.Available && !upgrade.IsExhausted,
                    upgrade.IsExhausted ? "already_maxed" : "native_unavailable");
                break;
            }
            case EntityKind.Research:
            {
                WorldLookup.TryFind(world.Research, id, out var research);
                result["visible"] = Verdict(
                    research.Visible,
                    "native_hidden");
                result["available"] = Verdict(
                    research.Available,
                    research.Complete ? "already_maxed" : "native_unavailable");
                // ResearchSO.IsWithinDevelopRange's own gates, in its own order, under the same
                // words the research row uses for them. Leeway and the two caps are one gate:
                // native develops on leeway OR on being below both caps, so an exhausted leeway
                // beside an open cap is not what refused. CanDevelop() adds !IsDeveloping() on top
                // of the whole expression, so a running development is asked last.
                var reason = research.Complete
                    ? "already_maxed"
                    : !research.MeetsLevelRequirements
                        ? "requirements_unmet"
                        : !research.StillHasLeeway &&
                          !(research.BelowArtificialMaxLevel && research.BelowMaxInvestmentLevel)
                            ? "research_leeway_exhausted"
                            : !research.WithinDevelopRange
                                ? "native_development_range_refused"
                                : research.IsDeveloping
                                    ? "already_developing"
                                    : research.CanDevelop
                                        ? "can_develop"
                                        : "native_can_develop_refused";
                result["canDevelop"] = Verdict(
                    research.CanDevelop,
                    reason);
                break;
            }
            case EntityKind.SpellRecipe:
            {
                WorldLookup.TryFind(world.SpellRecipes, id, out var spell);
                var offered = IsCurrentDiscoveryOffer(world, id);
                var visible = Verdict(
                    spell.Discovered || !spell.HiddenDiscovery || offered,
                    "hidden_discovery");
                result["visible"] = visible;
                result["available"] = visible;
                result["canDiscover"] = Verdict(
                    !spell.Discovered && (!spell.HiddenDiscovery || offered),
                    spell.Discovered ? "already_discovered" : "hidden_discovery");
                result["canUse"] = SpellCanUse(world, id);
                break;
            }
            case EntityKind.AlchemyRecipe:
            {
                WorldLookup.TryFind(world.AlchemyRecipes, id, out var alchemy);
                AddDiscoveryPredicates(result, world, id, alchemy.Discovered, nativeDiscoverable: true);
                break;
            }
            case EntityKind.CraftingRecipe:
            {
                WorldLookup.TryFind(world.CraftingRecipes, id, out var recipe);
                var visible = Verdict(
                    recipe.Reading.Visible,
                    recipe.Reading.VisibilityReasonCode);
                result["visible"] = visible;
                result["available"] = visible;
                result["canPurchase"] = Verdict(
                    recipe.Reading.CanBuyAtStartingQuantity,
                    recipe.Reading.NativePurchaseReasonCode);
                break;
            }
            case EntityKind.Consumable:
            {
                WorldLookup.TryFind(world.Consumables, id, out var consumable);
                var visible = Verdict(
                    consumable.Visible,
                    "not_visible");
                result["visible"] = visible;
                result["available"] = visible;
                result["canUse"] = Verdict(
                    consumable.CanFire,
                    consumable.Quantity <= 0 ? "none_owned" : "native_can_fire_refused");
                break;
            }
            case EntityKind.Resource:
            {
                WorldLookup.TryFind(world.Resources, id, out var resource);
                var visible = Verdict(
                    resource.Reading.Visible,
                    "not_visible");
                result["visible"] = visible;
                result["available"] = visible;
                break;
            }
            case EntityKind.Ritual:
            {
                WorldLookup.TryFind(world.Rituals, id, out var ritual);
                AddDiscoveryPredicates(result, world, id, ritual.Discovered, nativeDiscoverable: true);
                break;
            }
            case EntityKind.Glyph:
            {
                WorldLookup.TryFind(world.Glyphs, id, out var glyph);
                AddDiscoveryPredicates(result, world, id, glyph.Discovered, glyph.Discoverable);
                result["available"] = Verdict(
                    glyph.Learned,
                    glyph.Discoverable ? "not_discovered" : "prerequisites_unmet");
                break;
            }
            case EntityKind.Equipment:
            {
                WorldLookup.TryFind(world.Equipment, id, out var equipment);
                AddDiscoveryPredicates(result, world, id, equipment.IsCreated, nativeDiscoverable: true);
                break;
            }
            case EntityKind.TimeRune:
            {
                WorldLookup.TryFind(world.TimeRunes, id, out var timeRune);
                AddDiscoveryPredicates(result, world, id, timeRune.Discovered, nativeDiscoverable: true);
                break;
            }
        }
        return result;
    }

    private static void AddDiscoveryPredicates(
        JObject result,
        GameWorldState world,
        Guid id,
        bool discovered,
        bool nativeDiscoverable)
    {
        var offered = IsCurrentDiscoveryOffer(world, id);
        var visible = discovered || offered;
        result["visible"] = Verdict(
            visible,
            "not_discovered_or_offered");
        result["available"] = result["visible"]!;
        result["canDiscover"] = Verdict(
            !discovered && nativeDiscoverable,
            discovered
                ? "already_discovered"
                : !nativeDiscoverable
                    ? "native_not_discoverable"
                    : "discovery_unavailable");
    }

    private static bool IsCurrentDiscoveryOffer(GameWorldState world, Guid id)
    {
        for (var treeIndex = 0; treeIndex < world.DiscoveryTrees.Count; treeIndex++)
        {
            var offers = world.DiscoveryTrees[treeIndex].CurrentOfferIds;
            for (var offerIndex = 0; offerIndex < offers.Count; offerIndex++)
            {
                if (offers[offerIndex] == id) return true;
            }
        }
        return false;
    }

    private static JObject SpellCanUse(GameWorldState world, Guid recipeId)
    {
        var found = false;
        var ready = false;
        var reason = "not_equipped";
        var slots = new JArray();
        for (var index = 0; index < world.SpellSlots.Count; index++)
        {
            var slot = world.SpellSlots[index];
            if (!slot.Occupied || slot.SpellRecipeId != recipeId) continue;
            found = true;
            ready |= slot.CastReady;
            if (!slot.CastReady)
            {
                reason = !slot.ResourcesCovered
                    ? "resources_uncovered"
                    : !slot.ChargeAvailable
                        ? "charge_unavailable"
                        : slot.Attuning
                            ? "attuning"
                            : slot.Casting || slot.ReadyingCast
                                ? "cast_in_progress"
                                : "native_can_cast_refused";
            }
            slots.Add(new GameMcpDomainValue(slot));
        }
        var result = Verdict(
            found && ready,
            found ? reason : "not_equipped");
        if (slots.Count > 0) result["slots"] = slots;
        return result;
    }

    private static JObject? Requirements(
        GameWorldState world,
        Guid id,
        EntityKind kind,
        out string? parityFailure,
        out string parityFailureCode)
    {
        parityFailure = null;
        parityFailureCode = string.Empty;
        if (kind is not EntityKind.Structure and not EntityKind.Upgrade and not EntityKind.Research)
            return null;

        var ownerKind = kind switch
        {
            EntityKind.Upgrade => WorldRequirementOwnerKind.Upgrade,
            EntityKind.Research => WorldRequirementOwnerKind.Research,
            _ => WorldRequirementOwnerKind.Structure,
        };
        var checkLevel = kind switch
        {
            EntityKind.Upgrade => UpgradeCheckLevel(world, id),
            EntityKind.Research when WorldLookup.TryFind(world.Research, id, out var research) =>
                research.EffectiveRequirementLevel,
            _ => StructureCheckLevel(world, id),
        };
        var suite = WorldRequirementEvaluator.Evaluate(world, id, checkLevel);
        var root = ProjectRequirementContainer(
            world,
            id,
            containerIndex: 0,
            checkLevel,
            new HashSet<RequirementKey>(),
            depth: 0);
        var parity = new JObject
        {
            ["suiteVerdict"] = suite.ToString(),
        };
        // Asked here, on the Unity thread, for this one entity. It used to be a table the world
        // capture filled for every upgrade, structure, and research four times a second, of which
        // one row was ever read.
        if (!NativeVerdicts.TryRead(id, out var native, out var probeFailure))
        {
            parityFailureCode = "native_verdict_unavailable";
            parityFailure =
                "The game's own prerequisite verdict could not be read for this entity: " +
                probeFailure + ".";
            parity["status"] = "not_available";
            parity["reasonCode"] = parityFailureCode;
            parity["reason"] = parityFailure;
        }
        else if (native.OwnerKind != ownerKind || native.CheckLevel != checkLevel)
        {
            parityFailureCode = "native_verdict_input_mismatch";
            parityFailure =
                "The game's prerequisite verdict answers about a different owner or level, " +
                "so it does not answer the same question.";
            parity["status"] = "not_available";
            parity["reasonCode"] = parityFailureCode;
            parity["reason"] = parityFailure;
            parity["nativeOwnerKind"] = native.OwnerKind.ToString();
            parity["nativeCheckLevel"] = native.CheckLevel;
        }
        else
        {
            parity["nativeVerdict"] = native.Met ? "Met" : "Unmet";
            if (suite == WorldRequirementVerdict.Unevaluable)
            {
                parityFailureCode = "suite_verdict_unevaluable";
                parityFailure =
                    "The suite could not evaluate a requirement the game answered, " +
                    "so the two verdicts cannot be compared.";
                parity["status"] = "not_available";
                parity["reasonCode"] = parityFailureCode;
                parity["reason"] = parityFailure;
            }
            else if ((suite == WorldRequirementVerdict.Met) != native.Met)
            {
                parityFailureCode = "native_verdict_mismatch";
                parityFailure = "The suite reads this requirement as " + suite +
                    " where the game reads it as " + (native.Met ? "Met" : "Unmet") + ".";
                parity["status"] = "mismatch";
                parity["reasonCode"] = parityFailureCode;
                parity["reason"] = parityFailure;
            }
            else
            {
                parity["status"] = "matched";
                parity["reasonCode"] = "native_verdict_matched";
            }
        }

        var requirements = new JObject
        {
            ["checkLevel"] = checkLevel,
            ["suiteVerdict"] = suite.ToString(),
            ["root"] = root,
        };
        if (parityFailure is not null) requirements["nativeParity"] = parity;
        return requirements;
    }

    private static JObject ProjectRequirementContainer(
        GameWorldState world,
        Guid ownerId,
        int containerIndex,
        long checkLevel,
        HashSet<RequirementKey> trail,
        int depth)
    {
        var key = new RequirementKey(ownerId, containerIndex);
        if (depth > MaximumRequirementExpansionDepth)
            return Refusal("requirement_depth_exceeded", ownerId, containerIndex);
        if (!trail.Add(key)) return Refusal("requirement_cycle", ownerId, containerIndex);
        try
        {
            var children = new JArray();
            if (WorldEntityRequirementLookup.TryFindContainerRange(
                    world.EntityRequirements, ownerId, containerIndex, out var start, out var count))
            {
                var rows = world.EntityRequirements.AsSpan();
                if (count > 0 && rows[start].OwnerKind != WorldRequirementOwnerKind.PrerequisiteLink)
                {
                    ProjectFlatRequirementGroups(
                        world, rows, start, count, checkLevel, trail, depth, children);
                }
                else
                {
                    for (var offset = 0; offset < count; offset++)
                    {
                        ref readonly var row = ref rows[start + offset];
                        if (row.ParentOrdinal >= 0) continue;
                        children.Add(ProjectRequirementNode(
                            world, rows, start, count, in row, checkLevel, trail, depth + 1));
                    }
                }
            }
            var result = new JObject
            {
                ["status"] = "available",
                ["ownerUuid"] = ownerId.ToString("D"),
                ["tierIndex"] = containerIndex,
                ["operator"] = "AND",

                // An operator node always carries its list. Dropping an empty one left a node
                // asserting a structure it did not have, and read the same as one nobody expanded.
                ["children"] = children,
            };
            return result;
        }
        finally
        {
            trail.Remove(key);
        }
    }

    private static void ProjectFlatRequirementGroups(
        GameWorldState world,
        ReadOnlySpan<WorldEntityRequirement> rows,
        int start,
        int count,
        long checkLevel,
        HashSet<RequirementKey> trail,
        int depth,
        JArray destination)
    {
        var priorGroup = -1;
        for (var offset = 0; offset < count; offset++)
        {
            ref readonly var first = ref rows[start + offset];
            if (first.Program != WorldRequirementProgramKind.NextLevel ||
                first.GroupOrdinal == priorGroup)
            {
                continue;
            }
            priorGroup = first.GroupOrdinal;

            var groupChildren = new JArray();
            JObject? singleton = null;
            for (var candidateOffset = 0; candidateOffset < count; candidateOffset++)
            {
                ref readonly var candidate = ref rows[start + candidateOffset];
                if (candidate.Program != first.Program ||
                    candidate.GroupOrdinal != first.GroupOrdinal)
                {
                    continue;
                }
                var projected = ProjectRequirementNode(
                    world, rows, start, count, in candidate, checkLevel, trail, depth + 1);
                singleton = projected;
                groupChildren.Add(projected);
            }

            if (groupChildren.Count == 1)
            {
                destination.Add(singleton!);
                continue;
            }
            destination.Add(new JObject
            {
                ["nodeKind"] = "group",
                ["ordinal"] = first.GroupOrdinal,
                ["parentOrdinal"] = -1,
                ["depth"] = 0,
                ["operator"] = first.GroupKind == WorldRequirementGroupKind.Any ? "OR" : "AND",
                ["children"] = groupChildren,
            });
        }
    }

    private static JObject ProjectRequirementNode(
        GameWorldState world,
        ReadOnlySpan<WorldEntityRequirement> rows,
        int start,
        int count,
        in WorldEntityRequirement row,
        long checkLevel,
        HashSet<RequirementKey> trail,
        int depth)
    {
        if (row.NodeKind == WorldRequirementNodeKind.Group)
        {
            var children = new JArray();
            for (var offset = 0; offset < count; offset++)
            {
                ref readonly var child = ref rows[start + offset];
                if (child.ParentOrdinal != row.Ordinal) continue;
                children.Add(ProjectRequirementNode(
                    world, rows, start, count, in child, checkLevel, trail, depth + 1));
            }
            var group = new JObject
            {
                ["nodeKind"] = "group",
                ["ordinal"] = row.Ordinal,
                ["parentOrdinal"] = row.ParentOrdinal,
                ["depth"] = row.Depth,
                ["operator"] = row.Operator.ToString().ToUpperInvariant(),
                ["children"] = children,
            };
            return group;
        }

        var evaluated = WorldRequirementEvaluator.ExplainLeaf(world, in row, checkLevel);
        var leaf = new JObject
        {
            ["nodeKind"] = "leaf",
            ["ordinal"] = row.Ordinal,
            ["parentOrdinal"] = row.ParentOrdinal,
            ["depth"] = row.Depth,
            ["conditionType"] = row.ConditionTypeName,
            ["conditionKind"] = row.Kind.ToString(),
            ["requirementNativeType"] = RequirementNativeType(row.Kind),
            ["reqType"] = row.ReqType,
            ["selectedValueKind"] = evaluated.SelectedValueKind,
            ["current"] = ProjectNumber(evaluated.Current),
            ["required"] = ProjectNumber(evaluated.Required),
            ["met"] = evaluated.Met,
            ["verdict"] = evaluated.Verdict.ToString(),
            ["reasonCode"] = evaluated.ReasonCode,
            ["baseThreshold"] = ProjectNumber(evaluated.BaseThreshold),
            ["scaledThreshold"] = ProjectNumber(evaluated.ScaledThreshold),
            ["effectiveThreshold"] = ProjectNumber(evaluated.EffectiveThreshold),
        };
        if (row.TargetId != Guid.Empty)
            leaf["requirementUuid"] = row.TargetId.ToString("D");
        if (row.Kind == WorldRequirementConditionKind.PrerequisiteLink)
        {
            var tiers = ProjectLinkTiers(
                world, in row, checkLevel, evaluated, trail, depth + 1);
            if (tiers.Count > 0) leaf["prerequisiteLinkTiers"] = tiers;
        }
        return leaf;
    }

    private static JArray ProjectLinkTiers(
        GameWorldState world,
        in WorldEntityRequirement row,
        long checkLevel,
        in WorldRequirementLeafEvaluation evaluation,
        HashSet<RequirementKey> trail,
        int depth)
    {
        var selectedTier = row.ReqType == 0
            ? 0L
            : BigDouble.Round(evaluation.ScaledThreshold).ToLong();
        var tiers = new JArray();
        for (var index = 0; index < world.PrerequisiteLinkTiers.Count; index++)
        {
            var tier = world.PrerequisiteLinkTiers[index];
            if (tier.LinkId != row.TargetId) continue;
            tiers.Add(new JObject
            {
                ["tierIndex"] = tier.TierIndex,
                ["selected"] = selectedTier == tier.TierIndex,
                ["activeEnabled"] = tier.ActiveEnabled,
                ["passiveEnabled"] = tier.PassiveEnabled,
                ["evaluatedFrame"] = tier.EvaluatedFrame,
                ["collectedFrame"] = tier.CollectedFrame,
                ["evaluatedThisFrame"] = tier.EvaluatedThisFrame,
                ["requirements"] = ProjectRequirementContainer(
                    world,
                    row.TargetId,
                    tier.TierIndex,
                    checkLevel,
                    trail,
                    depth),
            });
        }
        return tiers;
    }

    private static JObject? ResearchThresholds(GameWorldState world, Guid id, EntityKind kind)
    {
        if (kind != EntityKind.Research || !WorldLookup.TryFind(world.Research, id, out var research))
            return null;
        var result = new JObject
        {
            ["selectedValueKind"] = "total_level",
            ["current"] = research.TotalLevel,
            ["baseThreshold"] = research.BaseRequirementLevel,
            ["scaledThreshold"] = research.BaseRequirementLevel,
            ["effectiveThreshold"] = research.EffectiveRequirementLevel,
            ["leeway"] = research.Modifiers.LeewayPoints.ToInt(),
            ["metWithLeeway"] = research.StillHasLeeway,
            ["nativeMeetsLevelRequirements"] = research.MeetsLevelRequirements,
            ["nativeStillHasLeeway"] = research.StillHasLeeway,
            ["adjustment"] = research.RequirementLevelAdjustment,
        };
        if (research.RequirementAdjustments.Count > 0)
            result["activeAdjustments"] = new GameMcpDomainValue(
                research.RequirementAdjustments);
        return result;
    }

    private static JObject? Purchase(GameWorldState world, Guid id, EntityKind kind)
    {
        if (kind is not EntityKind.Structure and not EntityKind.Upgrade)
            return null;
        if (kind == EntityKind.Structure &&
            (!WorldLookup.TryFind(world.Structures, id, out var structure) ||
             !structure.Reading.Unlocked))
            return null;
        if (kind == EntityKind.Upgrade &&
            (!WorldLookup.TryFind(world.Upgrades, id, out var upgrade) ||
             !upgrade.Reading.Available || upgrade.IsExhausted))
            return null;
        if (!WorldPurchaseCostLookup.TryFindRange(
                world.PurchaseCosts, id, out var start, out var count))
        {
            return new JObject
            {
                ["evaluated"] = false,
                ["reasonCode"] = "exact_cost_unavailable",
            };
        }
        var rows = new JArray();
        for (var index = 0; index < count; index++)
        {
            var row = world.PurchaseCosts[start + index];
            rows.Add(GameMcpWorldQuery.ProjectPurchaseCost(world, in row));
        }

        // No outer status around the array: each row already carries its own verdict, and a row
        // without one was not evaluated.
        return new JObject
        {
            ["rows"] = rows,
        };
    }

    private static JObject Blockers(GameWorldState world, Guid id, EntityKind kind)
    {
        var result = new JObject();

        if (kind is EntityKind.Structure or EntityKind.Upgrade)
        {
            JObject queue;
            if (WorldLookup.TryFind(
                    world.ActionQueues, KnownEntities.ActiveActionables.Uuid, out var actionQueue))
            {
                queue = Blocker(
                    !actionQueue.Consistent || !actionQueue.HasEmptySlot,
                    !actionQueue.Consistent
                        ? "queue_reading_inconsistent"
                        : actionQueue.HasEmptySlot ? "queue_room_available" : "queue_full");
            }
            else
            {
                queue = Blocker(true, "queue_not_published");
            }
            result["queue"] = queue;
        }
        if (kind == EntityKind.Upgrade && WorldLookup.TryFind(world.Upgrades, id, out var upgrade))
        {
            result["cap"] = Blocker(upgrade.IsExhausted, upgrade.IsExhausted
                ? "level_cap_reached"
                : "below_level_cap");
        }
        else if (kind == EntityKind.Research &&
                 WorldLookup.TryFind(world.Research, id, out var research))
        {
            var slack = research.Modifiers.LeewayPoints.ToInt();
            // Leeway blocks only together with the caps: native develops on leeway OR on being
            // below both caps, so an exhausted leeway beside an open cap is not a blocker.
            var leeway = Blocker(
                !research.StillHasLeeway &&
                !(research.BelowArtificialMaxLevel && research.BelowMaxInvestmentLevel),
                research.StillHasLeeway
                    ? "native_leeway_available"
                    : "native_leeway_exhausted");
            leeway["currentTotalLevel"] = research.TotalLevel;
            leeway["leeway"] = slack;
            leeway["effectiveRequirement"] = research.EffectiveRequirementLevel;
            leeway["nativeMeetsLevelRequirements"] = research.MeetsLevelRequirements;
            result["leeway"] = leeway;
            var cap = Blocker(
                research.Complete || !research.BelowArtificialMaxLevel ||
                    !research.BelowMaxInvestmentLevel,
                research.Complete
                    ? "already_maxed"
                    : !research.BelowArtificialMaxLevel
                        ? "artificial_research_cap_reached"
                        : !research.BelowMaxInvestmentLevel
                            ? "research_investment_cap_reached"
                            : "below_research_cap");
            cap["queuedLevels"] = GameMcpWorldQuery.ResearchQueuedLevels(in research);
            cap["purchasedLevel"] = research.PurchasedLevels;
            cap["baseLevelExcludingBonus"] = research.BaseLevel;
            cap["bonusLevel"] = research.BonusLevel;
            cap["totalLevel"] = research.TotalLevel;
            if (research.MaxLevel >= 0) cap["effectiveCap"] = research.MaxLevel;
            if (research.ArtificialMaxLevel >= 0)
                cap["artificialCap"] = research.ArtificialMaxLevel;
            cap["nativeComplete"] = research.Complete;
            result["cap"] = cap;
        }
        if (kind == EntityKind.SpellRecipe &&
            WorldLookup.TryFind(world.SpellRecipes, id, out var spell))
        {
            result["recipeDiscovery"] = Blocker(
                !spell.Discovered,
                spell.Discovered ? "recipe_discovered" : "recipe_not_discovered");
            var (bandwidth, drain) = SpellResourceBlockers(world, id);
            if (bandwidth is not null) result["bandwidth"] = bandwidth;
            if (drain is not null) result["drain"] = drain;
        }
        else if (kind == EntityKind.AlchemyRecipe &&
                 WorldLookup.TryFind(world.AlchemyRecipes, id, out var alchemy))
        {
            result["recipeDiscovery"] = Blocker(
                !alchemy.Discovered,
                alchemy.Discovered ? "recipe_discovered" : "recipe_not_discovered");
            var (bandwidth, drain) = AlchemyResourceBlockers(world, id);
            if (bandwidth is not null) result["bandwidth"] = bandwidth;
            if (drain is not null) result["drain"] = drain;
        }
        else if (kind == EntityKind.CraftingRecipe &&
                 WorldLookup.TryFind(world.CraftingRecipes, id, out var crafting))
        {
            result["recipeDiscovery"] = Blocker(
                !crafting.Reading.Visible,
                crafting.Reading.VisibilityReasonCode);
            var bandwidth = CraftingBandwidthBlocker(world, in crafting);
            var drain = CraftingDrainBlocker(in crafting);
            if (bandwidth is not null) result["bandwidth"] = bandwidth;
            if (drain is not null) result["drain"] = drain;
        }

        return result;
    }

    private static (JObject? Bandwidth, JObject? Drain) SpellResourceBlockers(
        GameWorldState world,
        Guid recipeId)
    {
        var bandwidthRows = new JArray();
        var drainRows = new JArray();
        var bandwidthBlocked = false;
        var drainBlocked = false;
        for (var slotIndex = 0; slotIndex < world.SpellSlots.Count; slotIndex++)
        {
            var slot = world.SpellSlots[slotIndex];
            if (!slot.Occupied || slot.SpellRecipeId != recipeId) continue;
            CollectSpellCosts(
                world, slot.SlotIndex, WorldSpellCostKind.Immediate,
                bandwidthRows, ref bandwidthBlocked);
            CollectSpellCosts(
                world, slot.SlotIndex, WorldSpellCostKind.Drain,
                drainRows, ref drainBlocked);
        }
        return (
            ResourceBlocker(bandwidthRows, bandwidthBlocked, "bandwidth"),
            ResourceBlocker(drainRows, drainBlocked, "drain"));
    }

    private static void CollectSpellCosts(
        GameWorldState world,
        int slotIndex,
        WorldSpellCostKind kind,
        JArray rows,
        ref bool blocked)
    {
        if (!WorldSpellCostLookup.TryFindRange(
                world.SpellCosts, slotIndex, kind, out var start, out var count)) return;
        for (var index = 0; index < count; index++)
        {
            var cost = world.SpellCosts[start + index];
            var row = ResourceCostEvidence(world, cost.ResourceId, cost.Amount, out var oneBlocked);
            row["slotIndex"] = slotIndex;
            row["costKind"] = kind.ToString();
            rows.Add(row);
            blocked |= oneBlocked;
        }
    }

    private static (JObject? Bandwidth, JObject? Drain) AlchemyResourceBlockers(
        GameWorldState world,
        Guid recipeId)
    {
        var bandwidthRows = ResourceCosts(
            world, recipeId, WorldAlchemyCostKind.RecipeDrain, out var bandwidthBlocked);
        var drainRows = ResourceCosts(
            world, recipeId, WorldAlchemyCostKind.CurrentDrain, out var drainBlocked);
        return (
            ResourceBlocker(bandwidthRows, bandwidthBlocked, "bandwidth"),
            ResourceBlocker(drainRows, drainBlocked, "drain"));
    }

    private static JArray ResourceCosts(
        GameWorldState world,
        Guid recipeId,
        WorldAlchemyCostKind kind,
        out bool blocked)
    {
        blocked = false;
        var rows = new JArray();
        if (!WorldAlchemyCostLookup.TryFindRange(
                world.AlchemyCosts, recipeId, kind, out var start, out var count)) return rows;
        for (var index = 0; index < count; index++)
        {
            var cost = world.AlchemyCosts[start + index];
            var row = ResourceCostEvidence(world, cost.ResourceId, cost.Amount, out var oneBlocked);
            row["costKind"] = kind.ToString();
            rows.Add(row);
            blocked |= oneBlocked;
        }
        return rows;
    }

    private static JObject ResourceCostEvidence(
        GameWorldState world,
        Guid resourceId,
        BigDouble amount,
        out bool blocked)
    {
        var available = BigDouble.Zero;
        var playerCost = amount;
        var bandwidth = false;
        if (WorldLookup.TryFind(world.Resources, resourceId, out var resource))
        {
            bandwidth = resource.Reading.Traits.BandwidthResource;
            available = WorldResourceCoordinate.SpendableAmount(in resource);
            playerCost = WorldResourceCoordinate.PlayerFacingCost(in resource, amount);
            blocked = !WorldResourceCoordinate.HasAmount(in resource, amount);
        }
        else
        {
            blocked = true;
        }
        var result = new JObject
        {
            ["resourceUuid"] = resourceId.ToString("D"),
            ["cost"] = ProjectNumber(playerCost),
            ["amount"] = ProjectNumber(available),
            ["affordable"] = !blocked,
            ["blocked"] = blocked,
        };
        if (bandwidth) result["bandwidth"] = true;
        if (blocked) result["reasonCode"] = "resource_or_headroom_insufficient";
        return result;
    }

    private static JObject? CraftingBandwidthBlocker(
        GameWorldState world,
        in WorldCraftingRecipe recipe)
    {
        var rows = new JArray();
        var blocked = false;
        for (var index = 0; index < recipe.Resources.Count; index++)
        {
            var resource = recipe.Resources[index];
            if (resource.Kind != WorldCraftingRecipeResourceKind.AuthoredInput ||
                !resource.BandwidthResource) continue;
            var available = WorldLookup.TryFind(
                world.Resources, resource.ResourceId, out var resourceState);
            var oneBlocked = !available ||
                !WorldResourceCoordinate.HasAmount(in resourceState, resource.Amount);
            var spendable = available
                ? WorldResourceCoordinate.SpendableAmount(in resourceState)
                : BigDouble.Zero;
            blocked |= oneBlocked;
            rows.Add(new JObject
            {
                ["resourceUuid"] = resource.ResourceId.ToString("D"),
                ["cost"] = ProjectNumber(resource.Amount),
                ["amount"] = ProjectNumber(spendable),
                ["affordable"] = !oneBlocked,
                ["bandwidth"] = true,
                ["blocked"] = oneBlocked,
            });
        }
        return ResourceBlocker(rows, blocked, "bandwidth");
    }

    private static JObject? CraftingDrainBlocker(in WorldCraftingRecipe recipe)
    {
        var rows = new JArray();
        var blocked = false;
        for (var index = 0; index < recipe.DrainBlocks.Count; index++)
        {
            var row = recipe.DrainBlocks[index];
            rows.Add(new GameMcpDomainValue(row));
            blocked |= row.Blocked;
        }
        return ResourceBlocker(rows, blocked, "drain");
    }

    private static JObject? ResourceBlocker(JArray rows, bool blocked, string axis)
    {
        if (rows.Count == 0) return null;
        var result = new JObject
        {
            ["blocked"] = blocked,
            ["rows"] = rows,
        };
        if (blocked) result["reasonCode"] = axis + "_blocked";
        return result;
    }

    internal static bool TryDescribePublishedEntity(
        GameWorldState world,
        Guid id,
        out string category,
        out string nativeType,
        out object row)
    {
        if (!TryResolve(world, id, out var kind, out row, out nativeType))
        {
            category = string.Empty;
            return false;
        }
        category = kind switch
        {
            EntityKind.Structure => "structures",
            EntityKind.Upgrade => "upgrades",
            EntityKind.Research => "research",
            EntityKind.SpellRecipe => "spell-recipes",
            EntityKind.AlchemyRecipe => "alchemy-recipes",
            EntityKind.CraftingRecipe => "crafting-recipes",
            EntityKind.Consumable => "consumables",
            EntityKind.Resource => "resources",
            EntityKind.Ritual => "rituals",
            EntityKind.Glyph => "glyphs",
            EntityKind.Equipment => "equipment",
            EntityKind.TimeRune => "time-runes",
            EntityKind.DiscoveryTree => "discovery-trees",
            _ => string.Empty,
        };
        return category.Length != 0;
    }

    private static bool TryResolve(
        GameWorldState world,
        Guid id,
        out EntityKind kind,
        out object row,
        out string nativeType)
    {
        var matches = 0;
        var resolvedKind = EntityKind.Unknown;
        object? resolvedRow = null;
        var resolvedNativeType = string.Empty;
        void Found(EntityKind candidateKind, object candidate, string candidateType)
        {
            matches++;
            resolvedKind = candidateKind;
            resolvedRow = candidate;
            resolvedNativeType = candidateType;
        }

        if (WorldLookup.TryFind(world.Structures, id, out var structure))
            Found(EntityKind.Structure, structure, "StructureSO");
        if (WorldLookup.TryFind(world.Upgrades, id, out var upgrade))
            Found(EntityKind.Upgrade, upgrade, "UpgradeSO");
        if (WorldLookup.TryFind(world.Research, id, out var research))
            Found(EntityKind.Research, research, "ResearchSO");
        if (WorldLookup.TryFind(world.SpellRecipes, id, out var spell))
            Found(EntityKind.SpellRecipe, spell, "SpellRecipeSO");
        if (WorldLookup.TryFind(world.AlchemyRecipes, id, out var alchemy))
            Found(EntityKind.AlchemyRecipe, alchemy, "AlchemyRecipeSO");
        if (WorldLookup.TryFind(world.CraftingRecipes, id, out var crafting))
            Found(EntityKind.CraftingRecipe, crafting, "CraftingRecipeSO");
        if (WorldLookup.TryFind(world.Consumables, id, out var consumable))
            Found(EntityKind.Consumable, consumable, "ConsumableSO");
        if (WorldLookup.TryFind(world.Resources, id, out var resource))
            Found(EntityKind.Resource, resource, "ResourceSO");
        if (WorldLookup.TryFind(world.Rituals, id, out var ritual))
            Found(EntityKind.Ritual, ritual, "RitualSO");
        if (WorldLookup.TryFind(world.Glyphs, id, out var glyph))
            Found(EntityKind.Glyph, glyph, "GlyphSO");
        if (WorldLookup.TryFind(world.Equipment, id, out var equipment))
            Found(EntityKind.Equipment, equipment, "EquipmentSO");
        if (WorldLookup.TryFind(world.TimeRunes, id, out var timeRune))
            Found(EntityKind.TimeRune, timeRune, "TimeRuneSO");
        if (WorldLookup.TryFind(world.DiscoveryTrees, id, out var discoveryTree))
            Found(EntityKind.DiscoveryTree, discoveryTree, "DiscoveryTreeSO");
        kind = resolvedKind;
        row = resolvedRow!;
        nativeType = resolvedNativeType;
        return matches == 1;
    }

    private static long UpgradeCheckLevel(GameWorldState world, Guid id)
    {
        WorldLookup.TryFind(world.Upgrades, id, out var row);
        return WorldRequirementEvaluator.UpgradeCheckLevel(in row);
    }

    private static long StructureCheckLevel(GameWorldState world, Guid id)
    {
        WorldLookup.TryFind(world.Structures, id, out var row);
        return WorldRequirementEvaluator.StructureCheckLevel(in row);
    }

    private static JObject Verdict(
        bool value,
        string falseReason) => new()
    {
        ["value"] = value,
        ["reasonCode"] = value ? "passed" : falseReason,
    };

    private static JObject PurchaseVerdict(
        GameWorldState world,
        Guid id,
        bool available,
        string unavailableReason)
    {
        if (!available) return Verdict(false, unavailableReason);
        if (!WorldPurchaseCostLookup.TryFindRange(
                world.PurchaseCosts, id, out var start, out var count) || count == 0)
            return Verdict(false, "price_unavailable");
        var cost = world.PurchaseCosts[start];
        if (!cost.AffordabilityEvaluated)
            return Verdict(false, "affordability_unavailable");
        return Verdict(cost.Affordable,
            cost.Affordable ? "passed" : "unaffordable");
    }

    private static JObject Blocker(bool blocked, string reasonCode) => new()
    {
        ["blocked"] = blocked,
        ["reasonCode"] = reasonCode,
    };

    private static JObject Refusal(string code, Guid ownerId, int tierIndex) => new()
    {
        ["status"] = "not_available",
        ["code"] = code,
        ["ownerUuid"] = ownerId.ToString("D"),
        ["tierIndex"] = tierIndex,
    };

    private static GameMcpValue ProjectNumber(BigDouble value) =>
        new GameMcpDomainValue(value);

    private static string RequirementNativeType(WorldRequirementConditionKind kind) => kind switch
    {
        WorldRequirementConditionKind.Upgrade => "UpgradeSO",
        WorldRequirementConditionKind.Research => "ResearchSO",
        WorldRequirementConditionKind.Structure => "StructureSO",
        WorldRequirementConditionKind.Spell => "SpellRecipeSO",
        WorldRequirementConditionKind.AlchemyRecipe => "AlchemyRecipeSO",
        WorldRequirementConditionKind.Ritual => "RitualSO",
        WorldRequirementConditionKind.Number => "NumberVariable",
        WorldRequirementConditionKind.Generic => "UpgradeableObject",
        WorldRequirementConditionKind.PrerequisiteLink => "PrerequisiteLinkSO",
        _ => "unknown",
    };

    private readonly struct RequirementKey : IEquatable<RequirementKey>
    {
        internal RequirementKey(Guid ownerId, int tierIndex)
        {
            OwnerId = ownerId;
            TierIndex = tierIndex;
        }

        private Guid OwnerId { get; }
        private int TierIndex { get; }
        public bool Equals(RequirementKey other) =>
            OwnerId == other.OwnerId && TierIndex == other.TierIndex;
        public override bool Equals(object? obj) => obj is RequirementKey other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(OwnerId, TierIndex);
    }

    private enum EntityKind
    {
        Unknown = 0,
        Structure = 1,
        Upgrade = 2,
        Research = 3,
        SpellRecipe = 4,
        AlchemyRecipe = 5,
        CraftingRecipe = 6,
        Consumable = 7,
        Resource = 8,
        Ritual = 9,
        Glyph = 10,
        Equipment = 11,
        TimeRune = 12,
        DiscoveryTree = 13,
    }
}
#endif
