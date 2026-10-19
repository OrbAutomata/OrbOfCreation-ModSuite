#if SERVICE_CYCLE_PROFILE
using System;
using OrbModding.Common;
using OrbModding.Common.Runtime.World;

namespace OrbAutomata.GameMcp;

internal static class GameMcpCommandPreparation
{
    internal static bool TryPrepareGameMcpCommand(
        GameMcpFrameOperation operation,
        GameMcpFrameContext context,
        out GameMcpCommand command,
        out GameMcpCommandResult failure)
    {
        var request = operation.Request;
        if (request.Route.Kind is not { } kind)
            throw new InvalidOperationException(request.ToolName + " is not answered by a command");

        var mode = request.Mode;
        var targetId = request.Uuid;
        var secondaryId = request.SecondaryUuid;
        var nativeType = string.Empty;
        var amount = request.Amount;
        var payloadKey = string.Empty;
        var payloadValue = string.Empty;
        GameMcpCommandResult? preparationFailure = null;
        if (kind == GameMcpCommandKind.Purchase && context.World is not null)
        {
            var structure = WorldLookup.TryFind(
                context.World.Snapshot.Structures, request.Uuid, out _);
            var upgrade = WorldLookup.TryFind(
                context.World.Snapshot.Upgrades, request.Uuid, out _);
            if (structure != upgrade)
            {
                mode = structure ? "structure" : "upgrade";
                nativeType = structure ? "StructureSO" : "UpgradeSO";
            }
        }
        else if (kind == GameMcpCommandKind.Cast)
        {
            nativeType = "SpellRecipeSO";
            amount = request.SlotIndex;
            payloadValue = request.SerializedValue;
        }
        else if (kind == GameMcpCommandKind.Concept)
            nativeType = "AlchemyRecipeSO";
        else if (kind == GameMcpCommandKind.Harvest)
        {
            nativeType = "PlotNodeSO";
            mode = request.Mode;
        }
        else if (kind == GameMcpCommandKind.SpellLevel)
            nativeType = "SpellRecipeSO";
        else if (kind == GameMcpCommandKind.DiscoveryTreeOffer)
        {
            nativeType = "DiscoveryTreeSO";
            mode = request.Mode.Substring("offer_".Length);
        }
        else if (kind == GameMcpCommandKind.SpellWorkbench)
        {
            nativeType = "SpellRecipeSO";
            mode = "create";
        }
        else if (kind == GameMcpCommandKind.SpellComposition)
        {
            nativeType = "IntVariable";

            // Four dials share one action, so the committed response names the one it moved.
            payloadKey = request.Key;
        }
        else if (kind == GameMcpCommandKind.SpellLoadout)
        {
            nativeType = "Spell";
            amount = request.Mode == "move" ? request.SlotIndex : 1;
            if (context.World is null)
                preparationFailure = GameMcpCommandResult.Rejected(
                    "world_not_published", context.RuntimeNotAvailableReason);
            else if (!GameMcpWorldQuery.TryEquippedSpellSlot(
                         context.World.Snapshot, request.Amount,
                         out targetId, out var spellSlotReason))
                preparationFailure = GameMcpCommandResult.Rejected(
                    "slot_unavailable", spellSlotReason);
        }
        else if (kind == GameMcpCommandKind.Targeting)
            nativeType = request.Mode == "submit" ? "StructureSO" : "TargetingManager+TargetLink";
        else if (kind == GameMcpCommandKind.Consumable)
        {
            nativeType = "ConsumableSO";
            payloadKey = request.Key;
            payloadValue = request.SerializedValue;
            if (request.Mode == "move") amount = request.SlotIndex;
        }
        else if (kind == GameMcpCommandKind.Crafting)
        {
            nativeType = "CraftingRecipeSO";
            payloadValue = request.SerializedValue;
        }
        else if (kind == GameMcpCommandKind.EquipmentLoadout)
            nativeType = "EquipmentSO";
        else if (kind == GameMcpCommandKind.AlchemyLoadout)
        {
            nativeType = "AlchemyRecipeSO";
            if (request.Mode == "move") amount = request.SlotIndex;
        }
        else if (kind == GameMcpCommandKind.RitualLifecycle)
            nativeType = "RitualSO";
        else if (kind == GameMcpCommandKind.GenericLevel)
        {
            if (context.World is null)
                preparationFailure = GameMcpCommandResult.Rejected(
                    "world_not_published", context.RuntimeNotAvailableReason);
            else if (!GameMcpEntityCapabilityMap.TryResolveGenericLevelType(
                         context.World.Snapshot, request.Uuid,
                         out nativeType, out var levelReason))
                preparationFailure = GameMcpCommandResult.Rejected(
                    "level_target_unavailable", levelReason);
        }
        else if (kind == GameMcpCommandKind.Loadout)
        {
            var snapshotMode = request.Mode.StartsWith("snapshot_", StringComparison.Ordinal);
            if (context.World is null)
            {
                preparationFailure = GameMcpCommandResult.Rejected(
                    "world_not_published", context.RuntimeNotAvailableReason);
            }
            else if (snapshotMode)
            {
                if (GameMcpWorldQuery.TrySnapshotList(
                        context.World.Snapshot, request.Key, out targetId, out var listReason))
                    nativeType = request.Key == "alchemy"
                        ? "AlchemySnapshotListVariable"
                        : "EquipmentSnapshotListVariable";
                else
                    preparationFailure = GameMcpCommandResult.Rejected(
                        "loadout_unavailable", listReason);
            }
            else if (GameMcpWorldQuery.TryPlayerLoadout(
                         context.World.Snapshot, request.Amount,
                         out targetId, out var loadoutReason))
            {
                nativeType = "PlayerLoadout";
            }
            else
            {
                preparationFailure = GameMcpCommandResult.Rejected(
                    "loadout_unavailable", loadoutReason);
            }
            mode = request.Mode == "set_section"
                ? request.Key == "equipment" ? "set_equipment" : "set_alchemy"
                : request.Mode;
            payloadKey = request.Key;
            payloadValue = request.SerializedValue;
            // Only a snapshot mode addresses a slot. A loadout position is resolved here into the
            // identity the boundary acts on, so it must not also ride along as a slot number.
            amount = snapshotMode ? request.SlotIndex : 1;
        }
        else if (kind == GameMcpCommandKind.HarvestLifecycle)
            nativeType = "HarvestElementSO";
        else if (kind == GameMcpCommandKind.StructureLifecycle)
            nativeType = "StructureSO";
        else if (kind == GameMcpCommandKind.ReturnToMenu)
        {
            nativeType = "UIBackToMenuButton";
            mode = "return_to_menu";
        }
        else if (kind == GameMcpCommandKind.Challenge)
        {
            nativeType = "ChallengeSO";
            if (request.Mode == "select" && context.World is not null)
                secondaryId = GameMcpWorldQuery.ChallengeSelectionToReplace(
                    context.World.Snapshot, targetId);
        }
        else if (kind == GameMcpCommandKind.Prestige)
            nativeType = "PersistentResetManager";
        else if (kind == GameMcpCommandKind.Research)
            nativeType = "ResearchSO";
        else if (kind == GameMcpCommandKind.ConfigurationSet)
        {
            mode = request.Section;
            payloadKey = request.Key;
            payloadValue = request.SerializedValue;
        }
        else if (kind == GameMcpCommandKind.AutomationSet)
        {
            mode = request.Mode;
            payloadKey = request.Key;
            payloadValue = request.SerializedValue;
        }
        else if (kind == GameMcpCommandKind.EmergencyStop)
            mode = request.Mode;
        else if (kind == GameMcpCommandKind.Screenshot)
            mode = "capture";
        else if (kind == GameMcpCommandKind.Navigation)
            mode = "navigate";
        else if (kind == GameMcpCommandKind.Probe)
            mode = request.Probe;
        else if (kind == GameMcpCommandKind.TooltipCatalog)
        {
            mode = "catalog";
            amount = request.Limit;
            payloadValue = request.Offset.ToString(
                System.Globalization.CultureInfo.InvariantCulture);
        }
        else if (kind == GameMcpCommandKind.TooltipRead)
        {
            mode = "read";
            payloadValue = request.Path;
        }
        else if (kind == GameMcpCommandKind.ContinueRun)
            mode = "continue";
        else if (kind == GameMcpCommandKind.Modal)
        {
            mode = request.Mode;
            payloadValue = request.Title;
        }

        command = new GameMcpCommand(
            operation.Sequence,
            kind,
            request.Classification == GameMcpOperationClass.Gameplay
                ? context.LifecycleGeneration
                : 0,
            request.Classification is GameMcpOperationClass.Gameplay or
                GameMcpOperationClass.SuiteAdministration
                    ? context.ConfigurationGeneration.Value
                    : 0,
            mode.Length == 0 ? request.ToolName : mode,
            targetId,
            secondaryId,
            nativeType,
            amount <= 0 ? 1 : amount,
            payloadKey,
            payloadValue,
            request.SaveCapture,
            operation,
            context,
            request.UuidCounts);

        if (request.Classification != GameMcpOperationClass.Gameplay)
        {
            failure = null!;
            return true;
        }
        if (context.World is null)
        {
            failure = GameMcpCommandResult.Rejected(
                "world_not_available",
                context.RuntimeNotAvailableReason.Length == 0
                    ? "The game state has not been read yet."
                    : context.RuntimeNotAvailableReason);
            return false;
        }
        if (context.LifecycleGeneration <= 0)
        {
            failure = GameMcpCommandResult.Rejected(
                "lifecycle_not_available",
                "No game is loaded, so there is nothing to act on.");
            return false;
        }
        if (!context.ConfigurationGeneration.IsValid)
        {
            failure = GameMcpCommandResult.Rejected(
                "configuration_not_available",
                GameMcpDecisionReason.For("configuration_not_available"));
            return false;
        }
        if (preparationFailure is not null)
        {
            failure = preparationFailure;
            return false;
        }
        var reason = string.Empty;
        if (GameMcpCommandKinds.IsEntityGameplayAction(kind) &&
            (nativeType.Length == 0 || !GameMcpEntityCapabilityMap.Contains(
                context.World.Snapshot,
                targetId,
                kind,
                out reason)))
        {
            // Nothing pending is not an unsupported target: the verb exists, the screen just has
            // no selection open. That is the whole answer, so no entity-ownership hint refines it.
            var noPendingTarget = kind == GameMcpCommandKind.Targeting &&
                !GameMcpEntityCapabilityMap.HasPendingTargetSelection(context.World.Snapshot);
            var code = noPendingTarget ? "no_pending_target" : "unsupported_action_target";
            if (!noPendingTarget && GameMcpEntityCapabilityMap.TryOwningTool(
                    context.World.Snapshot,
                    targetId,
                    out var owningCategory,
                    out var owningNativeType,
                    out var owningTool))
            {
                // The name a player reads and the handle they can act on. Spelling the subject the
                // way a log does — display name, asset name in brackets, whole canonical UUID —
                // put an address nobody could type into prose beside the same entity's own
                // structured fields, and the game's internal type name beside a category that had
                // already said the same thing in the player's word for it.
                var identity = EntityIdentityFormatter.PlayerHandle(
                    targetId,
                    context.World.Snapshot.EntityIdentities);
                if (owningTool.Length > 0 && !string.Equals(
                        owningTool, request.ToolName, StringComparison.Ordinal))
                {
                    code = "wrong_action_tool";

                    // Several verbs is the ordinary case — a rune is discovered and then levelled —
                    // so the pointer lists what stands rather than picking one of them for the
                    // caller. Only a tool list carries a space; one tool name never does.
                    var several = owningTool.IndexOf(' ') >= 0;
                    reason = identity + " lives under " + owningCategory +
                        "; use " + owningTool + " for its player action" + (several ? "s" : "");
                }
                else if (owningTool.Length > 0)
                {
                    code = "action_target_unavailable";
                    if (reason.Length == 0)
                        reason = identity + " is not available for this action right now";
                }
                else
                {
                    code = "read_only_entity";
                    reason = identity + " is available under " + owningCategory +
                        " but has no gameplay verb; inspect it with world_get";
                }
            }
            failure = GameMcpCommandResult.Rejected(
                code,
                reason.Length == 0
                    ? "the UUID is not supported by " + request.ToolName
                    : reason);
            return false;
        }
        failure = null!;
        return true;
    }
}
#endif
