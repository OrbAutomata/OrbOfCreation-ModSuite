#if SERVICE_CYCLE_PROFILE
using System;
using System.Collections.Generic;
using System.Threading;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata.GameMcp;

internal enum GameMcpCommandKind
{
    Purchase = 1,
    Cast = 2,
    Concept = 3,
    Harvest = 4,
    SpellLevel = 5,
    ConfigurationSet = 6,
    EmergencyStop = 7,
    Screenshot = 8,
    Navigation = 9,
    Probe = 10,
    ScreenCatalog = 11,
    TooltipCatalog = 12,
    TooltipRead = 13,
    ContinueRun = 14,
    DiscoveryTreeOffer = 15,
    SpellWorkbench = 16,
    SpellComposition = 17,
    SpellLoadout = 18,
    Targeting = 19,
    Consumable = 20,
    Crafting = 21,
    GenericDiscovery = 22,
    EquipmentLoadout = 23,
    Challenge = 24,
    Prestige = 25,
    Research = 26,
    AlchemyLoadout = 27,
    RitualLifecycle = 28,
    GenericLevel = 29,
    CraftingStation = 30,
    Loadout = 31,
    HarvestLifecycle = 32,
    StructureLifecycle = 33,
    ReturnToMenu = 34,
    Modal = 35,
    AutomationSet = 36,
}

internal static class GameMcpCommandKinds
{
    internal static bool IsGameplayAction(GameMcpCommandKind kind) =>
        kind is >= GameMcpCommandKind.Purchase and <= GameMcpCommandKind.SpellLevel or
            GameMcpCommandKind.DiscoveryTreeOffer or GameMcpCommandKind.SpellWorkbench or
            GameMcpCommandKind.SpellComposition or GameMcpCommandKind.SpellLoadout or
            GameMcpCommandKind.Targeting or GameMcpCommandKind.Consumable or
            GameMcpCommandKind.Crafting or GameMcpCommandKind.GenericDiscovery or
            GameMcpCommandKind.EquipmentLoadout or GameMcpCommandKind.Challenge or
            GameMcpCommandKind.Prestige or GameMcpCommandKind.Research or
            GameMcpCommandKind.AlchemyLoadout or GameMcpCommandKind.RitualLifecycle or
            GameMcpCommandKind.GenericLevel or
            GameMcpCommandKind.Loadout or GameMcpCommandKind.HarvestLifecycle or
            GameMcpCommandKind.StructureLifecycle or GameMcpCommandKind.ReturnToMenu;

    internal static bool IsEntityGameplayAction(GameMcpCommandKind kind) =>
        IsGameplayAction(kind) && kind != GameMcpCommandKind.ReturnToMenu;

    internal static bool RequiresPostStateSettlement(GameMcpCommandKind kind) =>
        IsGameplayAction(kind) && kind != GameMcpCommandKind.ReturnToMenu;

    internal static GameMcpCommandKind FromToolName(string toolName) => toolName switch
    {
        "game_purchase" => GameMcpCommandKind.Purchase,
        "game_cast" => GameMcpCommandKind.Cast,
        "game_concept" => GameMcpCommandKind.Concept,
        "game_agromancy" => GameMcpCommandKind.HarvestLifecycle,
        "game_spell_level" => GameMcpCommandKind.SpellLevel,
        "game_casting_dial" => GameMcpCommandKind.SpellComposition,
        "game_spell_loadout" => GameMcpCommandKind.SpellLoadout,
        "game_targeting" => GameMcpCommandKind.Targeting,
        "game_consumable" => GameMcpCommandKind.Consumable,
        "game_craft" => GameMcpCommandKind.Crafting,
        "game_discover" => GameMcpCommandKind.GenericDiscovery,
        "game_equipment" => GameMcpCommandKind.EquipmentLoadout,
        "time_challenge" => GameMcpCommandKind.Challenge,
        "time_prestige" => GameMcpCommandKind.Prestige,
        "game_research" => GameMcpCommandKind.Research,
        "game_alchemy" => GameMcpCommandKind.AlchemyLoadout,
        "game_ritual" => GameMcpCommandKind.RitualLifecycle,
        "game_level" => GameMcpCommandKind.GenericLevel,
        "game_loadout" => GameMcpCommandKind.Loadout,
        "game_structure" => GameMcpCommandKind.StructureLifecycle,
        "game_return_to_menu" => GameMcpCommandKind.ReturnToMenu,
        "game_modal" => GameMcpCommandKind.Modal,
        "suite_config_set" => GameMcpCommandKind.ConfigurationSet,
        "suite_automation" => GameMcpCommandKind.AutomationSet,
        "suite_emergency_stop" => GameMcpCommandKind.EmergencyStop,
        "game_screenshot" => GameMcpCommandKind.Screenshot,
        "game_navigate" => GameMcpCommandKind.Navigation,
        "game_probe" => GameMcpCommandKind.Probe,
        "game_screen_catalog" => GameMcpCommandKind.ScreenCatalog,
        "game_tooltips" => GameMcpCommandKind.TooltipCatalog,
        "game_tooltip" => GameMcpCommandKind.TooltipRead,
        "game_continue" => GameMcpCommandKind.ContinueRun,
        _ => throw new ArgumentException(
            "no MCP command capability is registered for " + toolName,
            nameof(toolName)),
    };

    internal static GameMcpCommandKind FromRequest(
        string toolName,
        string mode,
        string surface)
    {
        var kind = FromToolName(toolName);
        if (toolName == "game_discover" &&
            mode.StartsWith("offer_", StringComparison.Ordinal))
            return GameMcpCommandKind.DiscoveryTreeOffer;
        if (toolName == "game_discover" && mode == "confirm" && surface == "spellcraft")
            return GameMcpCommandKind.SpellWorkbench;
        if (toolName == "game_spell_loadout" && mode == "add")
            return GameMcpCommandKind.SpellWorkbench;
        if (toolName == "game_agromancy" &&
            mode is "add_plot_action" or "remove_plot_action")
            return GameMcpCommandKind.Harvest;
        return kind;
    }

    internal static string ToolName(GameMcpCommandKind kind) => kind switch
    {
        GameMcpCommandKind.Purchase => "game_purchase",
        GameMcpCommandKind.Cast => "game_cast",
        GameMcpCommandKind.Concept => "game_concept",
        GameMcpCommandKind.Harvest => "game_agromancy",
        GameMcpCommandKind.SpellLevel => "game_spell_level",
        GameMcpCommandKind.DiscoveryTreeOffer => "game_discover",
        GameMcpCommandKind.SpellWorkbench => "game_discover",
        GameMcpCommandKind.SpellComposition => "game_casting_dial",
        GameMcpCommandKind.SpellLoadout => "game_spell_loadout",
        GameMcpCommandKind.Targeting => "game_targeting",
        GameMcpCommandKind.Consumable => "game_consumable",
        GameMcpCommandKind.Crafting => "game_craft",
        GameMcpCommandKind.GenericDiscovery => "game_discover",
        GameMcpCommandKind.EquipmentLoadout => "game_equipment",
        GameMcpCommandKind.Challenge => "time_challenge",
        GameMcpCommandKind.Prestige => "time_prestige",
        GameMcpCommandKind.Research => "game_research",
        GameMcpCommandKind.AlchemyLoadout => "game_alchemy",
        GameMcpCommandKind.RitualLifecycle => "game_ritual",
        GameMcpCommandKind.GenericLevel => "game_level",
        GameMcpCommandKind.Loadout => "game_loadout",
        GameMcpCommandKind.HarvestLifecycle => "game_agromancy",
        GameMcpCommandKind.StructureLifecycle => "game_structure",
        GameMcpCommandKind.ReturnToMenu => "game_return_to_menu",
        GameMcpCommandKind.Modal => "game_modal",
        _ => string.Empty,
    };
}

/// <summary>
/// One immutable request copied off an HTTP worker and consumed on Unity's main thread.
/// No JSON token, game object, native reference, or mutable configuration crosses this seam.
/// </summary>
internal sealed class GameMcpCommand
{
    internal GameMcpCommand(
        long sequence,
        GameMcpCommandKind kind,
        long expectedLifecycleGeneration,
        ulong expectedConfigurationGeneration,
        string mode,
        Guid targetId,
        Guid secondaryId,
        string derivedNativeType,
        int amount,
        string payloadKey,
        string payloadValue,
        bool saveCapture,
        GameMcpFrameOperation? sourceOperation = null,
        GameMcpFrameContext? frameContext = null,
        GameMcpUuidCount[]? uuidCounts = null)
    {
        if (sequence <= 0) throw new ArgumentOutOfRangeException(nameof(sequence));
        var nativeAction = GameMcpCommandKinds.IsGameplayAction(kind);
        if (nativeAction && expectedLifecycleGeneration <= 0)
            throw new ArgumentOutOfRangeException(nameof(expectedLifecycleGeneration));
        if ((nativeAction || kind is GameMcpCommandKind.ConfigurationSet or
                GameMcpCommandKind.AutomationSet or GameMcpCommandKind.EmergencyStop) &&
            expectedConfigurationGeneration == 0)
            throw new ArgumentOutOfRangeException(nameof(expectedConfigurationGeneration));
        if (string.IsNullOrWhiteSpace(mode)) throw new ArgumentException("A mode is required.", nameof(mode));
        if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount));

        Sequence = sequence;
        Kind = kind;
        ExpectedLifecycleGeneration = expectedLifecycleGeneration;
        ExpectedConfigurationGeneration = expectedConfigurationGeneration;
        Mode = mode;
        TargetId = targetId;
        SecondaryId = secondaryId;
        DerivedNativeType = derivedNativeType ?? string.Empty;
        Amount = amount;
        PayloadKey = payloadKey ?? string.Empty;
        PayloadValue = payloadValue ?? string.Empty;
        SaveCapture = saveCapture;
        SourceOperation = sourceOperation;
        FrameContext = frameContext;
        UuidCounts = uuidCounts is null
            ? Array.Empty<GameMcpUuidCount>()
            : (GameMcpUuidCount[])uuidCounts.Clone();
    }

    internal long Sequence { get; }
    internal GameMcpCommandKind Kind { get; }
    internal long ExpectedLifecycleGeneration { get; }
    internal ulong ExpectedConfigurationGeneration { get; }
    internal string Mode { get; }
    internal Guid TargetId { get; }
    internal Guid SecondaryId { get; }
    internal string DerivedNativeType { get; }
    internal int Amount { get; }
    internal string PayloadKey { get; }
    internal string PayloadValue { get; }
    internal bool SaveCapture { get; }
    internal GameMcpFrameOperation? SourceOperation { get; }
    internal GameMcpFrameContext? FrameContext { get; }
    internal GameMcpUuidCount[] UuidCounts { get; }
}

internal sealed class GameMcpCommandResult
{
    private GameMcpCommandResult(
        string status,
        string code,
        string reason,
        long observedLifecycleGeneration,
        ulong observedConfigurationGeneration,
        GameMcpValue? details,
        bool hasActionResult,
        ServiceActionResult actionResult,
        byte[]? inlinePng)
    {
        Status = status;
        Code = code;
        Reason = reason;
        ObservedLifecycleGeneration = observedLifecycleGeneration;
        ObservedConfigurationGeneration = observedConfigurationGeneration;
        Details = details;
        HasActionResult = hasActionResult;
        ActionResult = actionResult;
        InlinePng = inlinePng;
    }

    internal string Status { get; }
    internal string Code { get; }
    internal string Reason { get; }
    internal long ObservedLifecycleGeneration { get; }
    internal ulong ObservedConfigurationGeneration { get; }
    internal GameMcpValue? Details { get; }
    internal bool HasActionResult { get; }
    /// <summary>
    /// True only when the MCP tool itself failed before it produced a canonical domain result.
    /// A faulted GameAction is still a successfully delivered tool result: its exact reason must
    /// remain available to clients through structuredContent.
    /// </summary>
    internal bool IsProtocolError =>
        string.Equals(Status, "faulted", StringComparison.Ordinal) && !HasActionResult;
    internal ServiceActionResult ActionResult { get; }
    internal byte[]? InlinePng { get; }

    internal static GameMcpCommandResult Rejected(
        string code,
        string reason,
        long observedLifecycleGeneration = 0,
        ulong observedConfigurationGeneration = 0,
        GameMcpValue? details = null) =>
        new(
            "refused",
            code,
            reason,
            observedLifecycleGeneration,
            observedConfigurationGeneration,
            details,
            false,
            default,
            null);

    internal static GameMcpCommandResult Faulted(
        string code,
        string reason,
        long observedLifecycleGeneration = 0,
        ulong observedConfigurationGeneration = 0) =>
        new(
            "faulted",
            code,
            reason,
            observedLifecycleGeneration,
            observedConfigurationGeneration,
            null,
            false,
            default,
            null);

    internal static GameMcpCommandResult FromAction(
        in ServiceActionResult result,
        GameMcpCommandKind commandKind,
        long observedLifecycleGeneration,
        ulong observedConfigurationGeneration,
        string? exactReason = null,
        GameMcpValue? details = null)
    {
        var status = result.Disposition switch
        {
            ServiceActionDisposition.Committed => "committed",
            ServiceActionDisposition.Rejected => "refused",
            ServiceActionDisposition.Faulted => "faulted",
            ServiceActionDisposition.Skipped => "refused",
            _ => "faulted",
        };
        var code = GameMcpActionResultCodeNames.Name(result.Code, commandKind);
        var reason = status == "committed"
            ? string.Empty
            : string.IsNullOrWhiteSpace(exactReason)
                ? GameMcpActionResultCodeNames.Reason(result.Code, commandKind)
                : exactReason!;
        return new GameMcpCommandResult(
            status,
            code,
            reason,
            observedLifecycleGeneration,
            observedConfigurationGeneration,
            details,
            true,
            result,
            null);
    }

    internal static GameMcpCommandResult Committed(
        string code,
        long observedLifecycleGeneration,
        ulong observedConfigurationGeneration,
        GameMcpValue? details = null,
        byte[]? inlinePng = null) =>
        new(
            "committed",
            code,
            string.Empty,
            observedLifecycleGeneration,
            observedConfigurationGeneration,
            details,
            false,
            default,
            inlinePng);

    internal GameMcpCommandResult WithInlinePng(GameMcpValue? details, byte[] inlinePng)
    {
        if (inlinePng is null || inlinePng.Length == 0)
            throw new ArgumentException("A captured PNG is required.", nameof(inlinePng));
        return new GameMcpCommandResult(
            Status,
            Code,
            Reason,
            ObservedLifecycleGeneration,
            ObservedConfigurationGeneration,
            details,
            HasActionResult,
            ActionResult,
            inlinePng);
    }

    internal GameMcpCommandResult WithDetails(GameMcpValue details) =>
        new(
            Status,
            Code,
            Reason,
            ObservedLifecycleGeneration,
            ObservedConfigurationGeneration,
            details,
            HasActionResult,
            ActionResult,
            InlinePng);

    internal GameMcpValue Project(GameMcpCommand command)
    {
        if (command is null) throw new ArgumentNullException(nameof(command));
        var stableCode = Code;
        if (HasActionResult)
            stableCode = GameMcpActionResultCodeNames.Name(ActionResult.Code, command.Kind);
        var status = Status;
        if (command.SourceOperation?.Request.Classification == GameMcpOperationClass.ReadOnly)
        {
            status = status switch
            {
                "committed" => "available",
                "refused" => "unavailable",
                _ => status,
            };
        }

        // One vocabulary everywhere: `reason` is the prose, `reasonCode` is the machine name, and a
        // success carries neither — it would only restate its own status word.
        var projected = new GameMcpObjectBuilder
        {
            ["status"] = status,
        };
        var succeeded = status is "committed" or "available";
        var pair = command.SecondaryId != Guid.Empty && command.TargetId != Guid.Empty;
        var role = SecondaryRole(command.Kind);
        if (!succeeded)
        {
            projected["reasonCode"] = stableCode;
            projected["reason"] = Reason;
            if (command.TargetId != Guid.Empty)
                projected["uuid"] = command.TargetId.ToString("D");

            // The pair's second half is a field on the refusal too. It used to appear only inside
            // the sentence, which left it unreadable to anything but a human.
            if (pair && role.Length > 0)
                projected[role + "Uuid"] = command.SecondaryId.ToString("D");
        }
        if (Details is GameMcpObject details) projected.CopyFrom(details);
        else if (Details is not null) projected["result"] = Details;

        // A two-entity request keeps its own identity at the top on the commit as well. The delta
        // names both entities in their own blocks, and one of those was being promoted over the
        // one the caller actually asked about — so the same request shape echoed the plot when it
        // refused and the action when it committed, mis-filing half of a caller's correlation.
        if (succeeded && pair)
            projected["uuid"] = command.TargetId.ToString("D");
        return projected.Freeze();
    }

    /// <summary>
    /// What the second entity of a two-entity request is to the first. The pair's second half is
    /// named on the refusal too, where it used to appear only inside the sentence.
    /// </summary>
    private static string SecondaryRole(GameMcpCommandKind kind) => kind switch
    {
        GameMcpCommandKind.Harvest or GameMcpCommandKind.HarvestLifecycle => "action",
        GameMcpCommandKind.DiscoveryTreeOffer => "offer",
        _ => string.Empty,
    };
}


internal static class GameMcpActionResultCodeNames
{
    internal static string Reason(
        ServiceActionResultCode code,
        GameMcpCommandKind commandKind)
    {
        if (code == CommonActionResultCodes.Committed)
            return "the audited native mutation committed and its postcondition was verified";
        if (code == CommonActionResultCodes.EmergencyStop)
            return "the suite emergency stop rejected the action before native mutation";
        if (code == CommonActionResultCodes.LifecycleReplaced)
            return "the live game lifecycle no longer matches the collected world epoch";
        if (code == CommonActionResultCodes.ServiceDisabled)
            return "the owning suite service is disabled";
        if (code == CommonActionResultCodes.NativeRejected)
            return "live native admission rejected the UUID-resolved target after revalidation";
        if (code == CommonActionResultCodes.PolicyRejected)
            return "the owning service policy rejected the action";
        if (code == CommonActionResultCodes.AdapterFault)
            return "the native adapter could not prove a safe, verified mutation";
        if (code == CommonActionResultCodes.Skipped)
            return "live revalidation or the native call produced no mutation, so the action was skipped";
        if (code == AutoCastActionResultCodes.ManualPause)
            return "the spell slot is under the player's manual-pause authority";
        if (code == AutoCastActionResultCodes.TargetingInProgress)
            return "the spell slot is already in a native targeting interaction";
        if (code == AutoCastActionResultCodes.SpellNotToggleable)
            return "This equipped spell is not a toggle spell.";
        if (code == AutoCastActionResultCodes.SpellAlreadyInactive)
            return "This toggle spell is already off.";
        if (code == AutoCastActionResultCodes.SpellAlreadyCasting)
            return "This spell is already running, so a fire press starts no cast; " +
                "toggle_off ends a running toggle spell.";
        if (code == AutoCastActionResultCodes.CancellationDisabled)
            return "Enable Cancellable Spells in the game settings before turning this spell off.";
        if (code == SpellLevelActionResultCodes.ProgressionLocked)
            return "native spell-level progression is not unlocked";
        if (code == SpellLevelActionResultCodes.LevelNotAffordable)
            return "This spell has no ready mastery level whose cost you can afford.";
        if (code == AutoHarvestActionResultCodes.PairContractUnavailable)
            return "The current plot and harvest action cannot be matched safely.";
        if (code == AutoHarvestActionResultCodes.FeatureContractUnavailable)
            return "the native harvest feature contract is unavailable";
        if (code == AutoHarvestActionResultCodes.PairFaulted)
            return "the native harvest pair faulted before a verified mutation";
        if (code == AutoHarvestActionResultCodes.NativePrerequisitesCurrentlyUnmet)
            return "native prerequisites are currently unmet according to one fresh action-boundary check";
        if (code == AutoHarvestActionResultCodes.NativePrerequisiteValidationUnavailable)
            return "the exact native harvest prerequisite validation was unreadable, so no quantity mutation was attempted";
        if (code == AutoHarvestActionResultCodes.ActionFamilyUnavailable ||
            code == AutoBuyActionResultCodes.ActionFamilyUnavailable ||
            code == AutoCastActionResultCodes.ActionFamilyUnavailable)
        {
            return "the suite does not own the requested native action family";
        }
        if (commandKind == GameMcpCommandKind.Purchase)
        {
            if (code == AutoBuyActionResultCodes.OwningViewUnavailable)
                return "The screen this is bought from is not available yet.";
            if (code == AutoBuyActionResultCodes.OwningViewRelationMissing)
                return "No purchase screen for this target is in this run's captured topology.";
            if (code == AutoBuyActionResultCodes.OwningViewRelationUnreadable)
                return "The game's purchase-screen chain for this target could not be read when " +
                    "this run's topology was captured.";
            if (code == AutoBuyActionResultCodes.OwningViewRelationContradictory)
                return "This target's captured purchase-screen relation contradicts itself.";
            if (code == AutoBuyActionResultCodes.OwningViewTopologyUnbound)
                return "The suite could not bind the purchase-screen topology this build needs, " +
                    "so no purchase can be admitted.";
            if (code == AutoBuyActionResultCodes.OwningViewTopologyUncaptured)
                return "The purchase-screen topology holds no admission evidence for this run.";
            if (code == AutoBuyActionResultCodes.OwningViewRelationStatusUnmodeled)
                return "This target's captured purchase-screen relation carries a status this " +
                    "build does not model.";
            if (code == AutoBuyActionResultCodes.OwningViewAvailabilityUnreadable)
                return "The game did not answer whether this target's purchase screen is " +
                    "available right now.";
            if (code == AutoBuyActionResultCodes.StructureUnavailable)
                return "The game has not unlocked this structure yet.";
            if (code == AutoBuyActionResultCodes.DestinationCapacityFull)
                return "Every slot this upgrade would fill is already occupied.";
            if (code == AutoBuyActionResultCodes.DestinationCapacityContractUnavailable)
                return "The upgrade's destination-capacity contract was unreadable, so no " +
                    "purchase was attempted.";
            if (code == AutoBuyActionResultCodes.DestinationCapacityIdentityMismatch)
                return "The upgrade's destination list is not the one the suite has audited.";
            if (code == AutoBuyActionResultCodes.BatchSpendDrift)
                return "Earlier purchases in this batch spent the margin this one was planned " +
                    "against.";
        }
        if (commandKind == GameMcpCommandKind.Cast)
        {
            if (code == AutoCastActionResultCodes.NativeCasterBusy)
                return "The game's spell system is busy right now.";
            if (code == AutoCastActionResultCodes.SlotIdentityChanged)
                return "The spell slot no longer holds the spell this call named.";
            if (code == AutoCastActionResultCodes.SpellNotReady)
                return "The game refused the cast on its own readiness terms.";
            if (code == AutoCastActionResultCodes.NoValidTarget)
                return "This spell has nothing valid to aim at right now.";
            if (code == AutoCastActionResultCodes.ChargeHoldRefused)
                return "A charged-cast hold could not be established, so no cast was submitted.";
        }

        // Last resort, and deliberately number-free. Every boundary above answers with its own
        // sentence; reaching here means one refused without supplying it, which is a defect in that
        // producer rather than a kind of no. The integer stays in the log where a maintainer can
        // trace it — on the wire it named no axis a caller could act on, and round 8 shipped it to
        // a caller nine different ways.
        return Surface(commandKind) + " refused and gave no reason of its own";
    }

    private static string Surface(GameMcpCommandKind commandKind) => commandKind switch
    {
        GameMcpCommandKind.DiscoveryTreeOffer => "the Discovery Tree offer boundary",
        GameMcpCommandKind.SpellWorkbench => "the spell workbench boundary",
        GameMcpCommandKind.SpellComposition => "the spell composition boundary",
        GameMcpCommandKind.SpellLoadout => "the spell loadout boundary",
        GameMcpCommandKind.Targeting => "the targeting boundary",
        GameMcpCommandKind.Consumable => "the consumable boundary",
        GameMcpCommandKind.Crafting => "the one-shot crafting boundary",
        GameMcpCommandKind.GenericDiscovery => "the generic discovery boundary",
        _ => "the native action boundary",
    };

    internal static string Name(
        ServiceActionResultCode code,
        GameMcpCommandKind commandKind)
    {
        if (code == CommonActionResultCodes.Committed) return "committed";
        if (code == CommonActionResultCodes.EmergencyStop) return "emergency_stop";
        if (code == CommonActionResultCodes.LifecycleReplaced) return "lifecycle_replaced";
        if (code == CommonActionResultCodes.ServiceDisabled) return "service_disabled";
        if (code == CommonActionResultCodes.NativeRejected) return "native_rejected";
        if (code == CommonActionResultCodes.PolicyRejected) return "policy_rejected";
        if (code == CommonActionResultCodes.AdapterFault) return "adapter_fault";
        if (code == CommonActionResultCodes.Skipped) return "skipped";
        if (code == AutoHarvestActionResultCodes.ActionFamilyUnavailable)
            return "action_family_unavailable";
        if (code == AutoHarvestActionResultCodes.PairContractUnavailable)
            return "pair_contract_unavailable";
        if (code == AutoHarvestActionResultCodes.FeatureContractUnavailable)
            return "feature_contract_unavailable";
        if (code == AutoHarvestActionResultCodes.PairFaulted)
            return "pair_faulted";
        if (code == AutoHarvestActionResultCodes.NativePrerequisitesCurrentlyUnmet)
            return "native_prerequisites_currently_unmet";
        if (code == AutoHarvestActionResultCodes.NativePrerequisiteValidationUnavailable)
            return "native_prerequisite_validation_unavailable";
        if (code == AutoBuyActionResultCodes.ActionFamilyUnavailable &&
            (commandKind == GameMcpCommandKind.Purchase ||
             commandKind == GameMcpCommandKind.SpellLevel))
            return "action_family_unavailable";
        // Auto Buy's numbers overlap the loadout and spell-level vocabularies, so they are only
        // correct beside the command kind that owns them.
        if (commandKind == GameMcpCommandKind.Purchase)
        {
            if (code == AutoBuyActionResultCodes.OwningViewUnavailable) return "not_visible";
            if (code == AutoBuyActionResultCodes.OwningViewRelationMissing)
                return "owning_screen_unknown";
            if (code == AutoBuyActionResultCodes.OwningViewRelationUnreadable)
                return "owning_screen_unreadable";
            if (code == AutoBuyActionResultCodes.OwningViewRelationContradictory)
                return "owning_screen_contradictory";
            if (code == AutoBuyActionResultCodes.OwningViewTopologyUnbound)
                return "contract_unavailable";
            if (code == AutoBuyActionResultCodes.OwningViewTopologyUncaptured)
                return "topology_not_captured";
            if (code == AutoBuyActionResultCodes.OwningViewRelationStatusUnmodeled)
                return "owning_screen_status_unmodelled";
            if (code == AutoBuyActionResultCodes.OwningViewAvailabilityUnreadable)
                return "owning_screen_availability_unreadable";
            if (code == AutoBuyActionResultCodes.StructureUnavailable) return "not_available";
            if (code == AutoBuyActionResultCodes.DestinationCapacityFull) return "destination_full";
            if (code == AutoBuyActionResultCodes.DestinationCapacityContractUnavailable)
                return "contract_unavailable";
            if (code == AutoBuyActionResultCodes.DestinationCapacityIdentityMismatch)
                return "identity_unavailable";
            if (code == AutoBuyActionResultCodes.BatchSpendDrift) return "batch_spend_drift";
        }
        // Feature result-code numbers are namespaced per feature and deliberately reused across
        // them, so a name is only correct beside the command kind that owns the vocabulary.
        // Spell leveling shares 2048-2050 with the loadout codes, and an unscoped match named an
        // empty snapshot slot "level_not_affordable" on a surface with no levels and no costs.
        if (commandKind == GameMcpCommandKind.SpellLevel)
        {
            if (code == SpellLevelActionResultCodes.ProgressionLocked)
                return "progression_locked";
            if (code == SpellLevelActionResultCodes.LevelNotAffordable)
                return "level_not_affordable";
        }
        if (code == AutoCastActionResultCodes.ActionFamilyUnavailable)
            return "action_family_unavailable";
        if (code == AutoCastActionResultCodes.ManualPause) return "manual_pause";
        if (code == AutoCastActionResultCodes.TargetingInProgress)
            return "targeting_in_progress";
        if (code == AutoCastActionResultCodes.NativeCasterBusy)
            return "native_caster_busy";
        if (code == AutoCastActionResultCodes.SlotIdentityChanged)
            return "slot_identity_changed";
        if (code == AutoCastActionResultCodes.SpellNotReady) return "spell_not_ready";
        if (code == AutoCastActionResultCodes.NoValidTarget) return "no_valid_target";
        if (code == AutoCastActionResultCodes.SpellNotToggleable)
            return "spell_not_toggleable";
        if (code == AutoCastActionResultCodes.SpellAlreadyInactive)
            return "spell_already_inactive";
        if (code == AutoCastActionResultCodes.SpellAlreadyCasting)
            return "spell_already_casting";
        if (code == AutoCastActionResultCodes.CancellationDisabled)
            return "cancellable_spells_disabled";
        if (commandKind == GameMcpCommandKind.DiscoveryTreeOffer)
        {
            if (code == DiscoveryTreeOfferActionResultCodes.ContractUnavailable) return "contract_unavailable";
            if (code == DiscoveryTreeOfferActionResultCodes.WrongThread) return "wrong_thread";
            if (code == DiscoveryTreeOfferActionResultCodes.IdentityUnavailable) return "identity_unavailable";
            if (code == DiscoveryTreeOfferActionResultCodes.TreeUnavailable) return "tree_unavailable";
            if (code == DiscoveryTreeOfferActionResultCodes.WrongMode) return "wrong_mode";
            if (code == DiscoveryTreeOfferActionResultCodes.NoDiscoveries) return "no_discoveries";
            if (code == DiscoveryTreeOfferActionResultCodes.OfferUnavailable) return "offer_unavailable";
            if (code == DiscoveryTreeOfferActionResultCodes.AlreadyDiscovered) return "already_discovered";
            if (code == DiscoveryTreeOfferActionResultCodes.RerollUnavailable) return "reroll_unavailable";
            if (code == DiscoveryTreeOfferActionResultCodes.Unaffordable) return "unaffordable";
            if (code == DiscoveryTreeOfferActionResultCodes.MutationPermitUnavailable) return "action_family_unavailable";
            if (code == DiscoveryTreeOfferActionResultCodes.PostCommitFault) return "post_commit_fault";
            if (code == DiscoveryTreeOfferActionResultCodes.VerificationFailed) return "verification_failed";
        }
        if (commandKind == GameMcpCommandKind.SpellWorkbench)
        {
            if (code == SpellWorkbenchActionResultCodes.ContractUnavailable) return "contract_unavailable";
            if (code == SpellWorkbenchActionResultCodes.WrongThread) return "wrong_thread";
            if (code == SpellWorkbenchActionResultCodes.IdentityUnavailable) return "identity_unavailable";
            if (code == SpellWorkbenchActionResultCodes.SelectionUnavailable) return "selection_unavailable";
            if (code == SpellWorkbenchActionResultCodes.WrongSelection) return "wrong_selection";
            if (code == SpellWorkbenchActionResultCodes.AlreadyDiscovered) return "already_discovered";
            if (code == SpellWorkbenchActionResultCodes.DiscoveryUnavailable) return "discovery_unavailable";
            if (code == SpellWorkbenchActionResultCodes.RecipeUnavailable) return "recipe_unavailable";
            if (code == SpellWorkbenchActionResultCodes.Unaffordable) return "unaffordable";
            if (code == SpellWorkbenchActionResultCodes.LoadoutFull) return "loadout_full";
            if (code == SpellWorkbenchActionResultCodes.CompositionUnsupported) return "composition_unsupported";
            if (code == SpellWorkbenchActionResultCodes.MutationPermitUnavailable)
                return "action_family_unavailable";
            if (code == SpellWorkbenchActionResultCodes.PostCommitFault) return "post_commit_fault";
            if (code == SpellWorkbenchActionResultCodes.VerificationFailed) return "verification_failed";
            if (code == SpellWorkbenchActionResultCodes.UsageRequirementsUnavailable)
                return "usage_requirements_unavailable";
            if (code == SpellWorkbenchActionResultCodes.UsageUnaffordable)
                return "usage_budget_unavailable";
            if (code == SpellWorkbenchActionResultCodes.UniqueSpellConflict)
                return "unique_spell_conflict";
            if (code == SpellWorkbenchActionResultCodes.GlyphRequirementsUnavailable)
                return "glyph_requirements_unavailable";
        }
        if (commandKind == GameMcpCommandKind.SpellComposition)
        {
            if (code == SpellCompositionActionResultCodes.ContractUnavailable) return "contract_unavailable";
            if (code == SpellCompositionActionResultCodes.WrongThread) return "wrong_thread";
            if (code == SpellCompositionActionResultCodes.LevelOutOfRange) return "level_out_of_range";
            if (code == SpellCompositionActionResultCodes.AlreadyInRequestedState) return "already_in_requested_state";
            if (code == SpellCompositionActionResultCodes.MutationPermitUnavailable) return "action_family_unavailable";
            if (code == SpellCompositionActionResultCodes.PostCommitFault) return "post_commit_fault";
            if (code == SpellCompositionActionResultCodes.VerificationFailed) return "verification_failed";
        }
        if (commandKind == GameMcpCommandKind.SpellLoadout)
        {
            if (code == SpellLoadoutActionResultCodes.ContractUnavailable) return "contract_unavailable";
            if (code == SpellLoadoutActionResultCodes.WrongThread) return "wrong_thread";
            if (code == SpellLoadoutActionResultCodes.IdentityUnavailable) return "identity_unavailable";
            if (code == SpellLoadoutActionResultCodes.NativeRemoveRefused) return "native_remove_refused";
            if (code == SpellLoadoutActionResultCodes.DestinationOutOfRange) return "destination_out_of_range";
            if (code == SpellLoadoutActionResultCodes.AlreadyInRequestedState) return "already_in_requested_state";
            if (code == SpellLoadoutActionResultCodes.MutationPermitUnavailable) return "action_family_unavailable";
            if (code == SpellLoadoutActionResultCodes.PostCommitFault) return "post_commit_fault";
            if (code == SpellLoadoutActionResultCodes.VerificationFailed) return "verification_failed";
        }
        if (commandKind == GameMcpCommandKind.Targeting)
        {
            if (code == TargetingActionResultCodes.ContractUnavailable) return "contract_unavailable";
            if (code == TargetingActionResultCodes.WrongThread) return "wrong_thread";
            if (code == TargetingActionResultCodes.NoPendingRequest) return "no_pending_request";
            if (code == TargetingActionResultCodes.TargetUnavailable) return "target_unavailable";
            if (code == TargetingActionResultCodes.NativeTargetRefused) return "native_target_refused";
            if (code == TargetingActionResultCodes.CancelUnavailable) return "cancel_unavailable";
            if (code == TargetingActionResultCodes.MutationPermitUnavailable) return "action_family_unavailable";
            if (code == TargetingActionResultCodes.PostCommitFault) return "post_commit_fault";
            if (code == TargetingActionResultCodes.VerificationFailed) return "verification_failed";
        }
        if (commandKind == GameMcpCommandKind.Consumable)
        {
            if (code == ConsumablePlayerActionResultCodes.ContractUnavailable) return "contract_unavailable";
            if (code == ConsumablePlayerActionResultCodes.WrongThread) return "wrong_thread";
            if (code == ConsumablePlayerActionResultCodes.ItemUnavailable) return "item_unavailable";
            if (code == ConsumablePlayerActionResultCodes.NotVisible) return "not_visible";
            if (code == ConsumablePlayerActionResultCodes.TargetingInProgress) return "targeting_in_progress";
            if (code == ConsumablePlayerActionResultCodes.InventoryBusy) return "inventory_busy";
            if (code == ConsumablePlayerActionResultCodes.CanFireRefused) return "can_fire_refused";
            if (code == ConsumablePlayerActionResultCodes.NoCancellableUsage) return "no_cancellable_usage";
            if (code == ConsumablePlayerActionResultCodes.NothingToDiscard) return "nothing_to_discard";
            if (code == ConsumablePlayerActionResultCodes.RandomizationUnavailable) return "randomization_unavailable";
            if (code == ConsumablePlayerActionResultCodes.AlreadyInRequestedState) return "already_in_requested_state";
            if (code == ConsumablePlayerActionResultCodes.ListUnavailable) return "list_unavailable";
            if (code == ConsumablePlayerActionResultCodes.SourceUnavailable) return "source_unavailable";
            if (code == ConsumablePlayerActionResultCodes.DestinationOutOfRange) return "destination_out_of_range";
            if (code == ConsumablePlayerActionResultCodes.MutationPermitUnavailable) return "action_family_unavailable";
            if (code == ConsumablePlayerActionResultCodes.MultiBuyUnavailable) return "multi_buy_unavailable";
            if (code == ConsumablePlayerActionResultCodes.PostCommitFault) return "post_commit_fault";
            if (code == ConsumablePlayerActionResultCodes.VerificationFailed) return "verification_failed";
        }
        if (commandKind == GameMcpCommandKind.Crafting)
        {
            if (code == CraftingInstanceLifecycleActionResultCodes.ContractUnavailable) return "contract_unavailable";
            if (code == CraftingInstanceLifecycleActionResultCodes.WrongThread) return "wrong_thread";
            if (code == CraftingInstanceLifecycleActionResultCodes.IdentityUnavailable) return "identity_unavailable";
            if (code == CraftingInstanceLifecycleActionResultCodes.NotVisible) return "not_visible";
            if (code == CraftingInstanceLifecycleActionResultCodes.PageRelationAmbiguous) return "page_relation_ambiguous";
            if (code == CraftingInstanceLifecycleActionResultCodes.InstanceUnavailable) return "instance_unavailable";
            if (code == CraftingInstanceLifecycleActionResultCodes.AutomationFull) return "automation_full";
            if (code == CraftingInstanceLifecycleActionResultCodes.MultiBuyUnavailable) return "multi_buy_unavailable";
            if (code == CraftingInstanceLifecycleActionResultCodes.MutationPermitUnavailable) return "action_family_unavailable";
            if (code == CraftingInstanceLifecycleActionResultCodes.PostCommitFault) return "post_commit_fault";
            if (code == CraftingInstanceLifecycleActionResultCodes.VerificationFailed) return "verification_failed";
            if (code == CraftingPlayerActionResultCodes.ContractUnavailable) return "contract_unavailable";
            if (code == CraftingPlayerActionResultCodes.WrongThread) return "wrong_thread";
            if (code == CraftingPlayerActionResultCodes.RecipeUnavailable) return "recipe_unavailable";
            if (code == CraftingPlayerActionResultCodes.NotVisible) return "not_visible";
            if (code == CraftingPlayerActionResultCodes.PageRelationAmbiguous) return "page_relation_ambiguous";
            if (code == CraftingPlayerActionResultCodes.InvalidPurchaseAmount) return "invalid_purchase_amount";
            if (code == CraftingPlayerActionResultCodes.QueueFull) return "queue_full";
            if (code == CraftingPlayerActionResultCodes.Unaffordable) return "unaffordable";
            if (code == CraftingPlayerActionResultCodes.MutationPermitUnavailable) return "action_family_unavailable";
            if (code == CraftingPlayerActionResultCodes.PostCommitFault) return "post_commit_fault";
            if (code == CraftingPlayerActionResultCodes.VerificationFailed) return "verification_failed";
        }
        if (commandKind == GameMcpCommandKind.GenericDiscovery)
        {
            if (code == GenericDiscoveryActionResultCodes.ContractUnavailable) return "contract_unavailable";
            if (code == GenericDiscoveryActionResultCodes.WrongThread) return "wrong_thread";
            if (code == GenericDiscoveryActionResultCodes.IdentityUnavailable) return "identity_unavailable";
            if (code == GenericDiscoveryActionResultCodes.UnsupportedType) return "unsupported_type";
            if (code == GenericDiscoveryActionResultCodes.NotVisible) return "not_visible";
            if (code == GenericDiscoveryActionResultCodes.AlreadyDiscovered) return "already_discovered";
            if (code == GenericDiscoveryActionResultCodes.DiscoveryUnavailable) return "discovery_unavailable";
            if (code == GenericDiscoveryActionResultCodes.Unaffordable) return "unaffordable";
            if (code == GenericDiscoveryActionResultCodes.MutationPermitUnavailable) return "action_family_unavailable";
            if (code == GenericDiscoveryActionResultCodes.PostCommitFault) return "post_commit_fault";
            if (code == GenericDiscoveryActionResultCodes.VerificationFailed) return "verification_failed";
            if (code == GenericDiscoveryActionResultCodes.CompositionChanged) return "composition_changed";
        }
        if (commandKind == GameMcpCommandKind.EquipmentLoadout)
        {
            if (code == EquipmentLoadoutActionResultCodes.ContractUnavailable) return "contract_unavailable";
            if (code == EquipmentLoadoutActionResultCodes.WrongThread) return "wrong_thread";
            if (code == EquipmentLoadoutActionResultCodes.IdentityUnavailable) return "identity_unavailable";
            if (code == EquipmentLoadoutActionResultCodes.NotCreated) return "not_created";
            if (code == EquipmentLoadoutActionResultCodes.AlreadyInRequestedState) return "already_in_requested_state";
            if (code == EquipmentLoadoutActionResultCodes.LoadoutFull) return "loadout_full";
            if (code == EquipmentLoadoutActionResultCodes.EquipmentTypeFull) return "equipment_type_full";
            if (code == EquipmentLoadoutActionResultCodes.UsageUnaffordable) return "usage_unaffordable";
            if (code == EquipmentLoadoutActionResultCodes.MultiBuyUnavailable) return "multi_buy_unavailable";
            if (code == EquipmentLoadoutActionResultCodes.MutationPermitUnavailable) return "action_family_unavailable";
            if (code == EquipmentLoadoutActionResultCodes.PostCommitFault) return "post_commit_fault";
            if (code == EquipmentLoadoutActionResultCodes.VerificationFailed) return "verification_failed";
            if (code == EquipmentLoadoutActionResultCodes.AmountUnavailable) return "amount_unavailable";
        }
        if (commandKind == GameMcpCommandKind.Challenge)
        {
            if (code == ChallengeActionResultCodes.ContractUnavailable) return "contract_unavailable";
            if (code == ChallengeActionResultCodes.WrongThread) return "wrong_thread";
            if (code == ChallengeActionResultCodes.IdentityUnavailable) return "identity_unavailable";
            if (code == ChallengeActionResultCodes.OfferUnavailable) return "offer_unavailable";
            if (code == ChallengeActionResultCodes.SelectionFull) return "selection_full";
            if (code == ChallengeActionResultCodes.SelectionRestricted) return "selection_restricted";
            if (code == ChallengeActionResultCodes.InvalidState) return "invalid_state";
            if (code == ChallengeActionResultCodes.FetchUnavailable) return "fetch_unavailable";
            if (code == ChallengeActionResultCodes.NoRerolls) return "no_rerolls";
            if (code == ChallengeActionResultCodes.MutationPermitUnavailable) return "action_family_unavailable";
            if (code == ChallengeActionResultCodes.PostCommitFault) return "post_commit_fault";
            if (code == ChallengeActionResultCodes.VerificationFailed) return "verification_failed";
        }
        if (commandKind == GameMcpCommandKind.Prestige)
        {
            if (code == PrestigeActionResultCodes.ContractUnavailable) return "contract_unavailable";
            if (code == PrestigeActionResultCodes.WrongThread) return "wrong_thread";
            if (code == PrestigeActionResultCodes.WorldCycleIncomplete) return "world_cycle_incomplete";
            if (code == PrestigeActionResultCodes.ChallengesNotFetched) return "challenges_not_fetched";
            if (code == PrestigeActionResultCodes.MutationPermitUnavailable) return "action_family_unavailable";
            if (code == PrestigeActionResultCodes.PostCommitFault) return "post_commit_fault";
            if (code == PrestigeActionResultCodes.VerificationFailed) return "verification_failed";
        }
        if (commandKind == GameMcpCommandKind.Research)
        {
            if (code == ResearchActionResultCodes.ContractUnavailable) return "contract_unavailable";
            if (code == ResearchActionResultCodes.WrongThread) return "wrong_thread";
            if (code == ResearchActionResultCodes.IdentityUnavailable) return "identity_unavailable";
            if (code == ResearchActionResultCodes.DevelopUnavailable) return "develop_unavailable";
            if (code == ResearchActionResultCodes.MultiBuyUnavailable) return "multi_buy_unavailable";
            if (code == ResearchActionResultCodes.InvalidMode) return "invalid_mode";
            if (code == ResearchActionResultCodes.InvalidState) return "invalid_state";
            if (code == ResearchActionResultCodes.BonusUnavailable) return "bonus_unavailable";
            if (code == ResearchActionResultCodes.MutationPermitUnavailable) return "action_family_unavailable";
            if (code == ResearchActionResultCodes.PostCommitFault) return "post_commit_fault";
            if (code == ResearchActionResultCodes.VerificationFailed) return "verification_failed";
            if (code == ResearchActionResultCodes.AmountUnavailable) return "amount_unavailable";

            // The same words the research row uses for the same native gate.
            if (code == ResearchActionResultCodes.AlreadyMaxed) return "already_maxed";
            if (code == ResearchActionResultCodes.Unaffordable) return "unaffordable";
            if (code == ResearchActionResultCodes.RequirementsUnmet) return "requirements_unmet";
            if (code == ResearchActionResultCodes.LeewayExhausted) return "research_leeway_exhausted";
            if (code == ResearchActionResultCodes.AlreadyDeveloping) return "already_developing";
        }
        if (commandKind == GameMcpCommandKind.AlchemyLoadout)
        {
            if (code == AlchemyLoadoutActionResultCodes.ContractUnavailable) return "contract_unavailable";
            if (code == AlchemyLoadoutActionResultCodes.WrongThread) return "wrong_thread";
            if (code == AlchemyLoadoutActionResultCodes.IdentityUnavailable) return "identity_unavailable";
            if (code == AlchemyLoadoutActionResultCodes.WrongDomain) return "wrong_alchemy_surface";
            if (code == AlchemyLoadoutActionResultCodes.NotDiscovered) return "not_discovered";
            if (code == AlchemyLoadoutActionResultCodes.AlreadyInRequestedState) return "already_in_requested_state";
            if (code == AlchemyLoadoutActionResultCodes.LoadoutFull) return "loadout_full";
            if (code == AlchemyLoadoutActionResultCodes.UsageUnavailable) return "amount_unavailable";
            if (code == AlchemyLoadoutActionResultCodes.MutationPermitUnavailable) return "action_family_unavailable";
            if (code == AlchemyLoadoutActionResultCodes.PostCommitFault) return "post_commit_fault";
            if (code == AlchemyLoadoutActionResultCodes.VerificationFailed) return "verification_failed";
        }
        if (commandKind == GameMcpCommandKind.GenericLevel)
        {
            if (code == GenericLevelActionResultCodes.ContractUnavailable) return "contract_unavailable";
            if (code == GenericLevelActionResultCodes.WrongThread) return "wrong_thread";
            if (code == GenericLevelActionResultCodes.IdentityUnavailable) return "identity_unavailable";
            if (code == GenericLevelActionResultCodes.WrongDomain) return "wrong_level_surface";
            if (code == GenericLevelActionResultCodes.Undiscovered) return "undiscovered";
            if (code == GenericLevelActionResultCodes.Hidden) return "hidden";
            if (code == GenericLevelActionResultCodes.Unavailable) return "not_available";
            if (code == GenericLevelActionResultCodes.CannotLevel) return "cannot_level";
            if (code == GenericLevelActionResultCodes.BonusUnavailable) return "bonus_unavailable";
            if (code == GenericLevelActionResultCodes.ResourcesHidden) return "resources_hidden";
            if (code == GenericLevelActionResultCodes.Unaffordable) return "unaffordable";
            if (code == GenericLevelActionResultCodes.MutationPermitUnavailable) return "action_family_unavailable";
            if (code == GenericLevelActionResultCodes.PostCommitFault) return "post_commit_fault";
            if (code == GenericLevelActionResultCodes.VerificationFailed) return "verification_failed";
        }
        if (commandKind == GameMcpCommandKind.StructureLifecycle)
        {
            if (code == StructureLifecycleActionResultCodes.ContractUnavailable) return "contract_unavailable";
            if (code == StructureLifecycleActionResultCodes.WrongThread) return "wrong_thread";
            if (code == StructureLifecycleActionResultCodes.IdentityUnavailable) return "identity_unavailable";
            if (code == StructureLifecycleActionResultCodes.NotAvailable) return "not_available";
            if (code == StructureLifecycleActionResultCodes.AlreadyInState) return "already_in_requested_state";
            if (code == StructureLifecycleActionResultCodes.MutationPermitUnavailable) return "action_family_unavailable";
            if (code == StructureLifecycleActionResultCodes.PostCommitFault) return "post_commit_fault";
            if (code == StructureLifecycleActionResultCodes.VerificationFailed) return "verification_failed";
        }
        if (commandKind == GameMcpCommandKind.ReturnToMenu)
        {
            if (code == ReturnToMenuActionResultCodes.ContractUnavailable) return "contract_unavailable";
            if (code == ReturnToMenuActionResultCodes.WrongThread) return "wrong_thread";
            if (code == ReturnToMenuActionResultCodes.WrongScene) return "wrong_scene";
            if (code == ReturnToMenuActionResultCodes.TransitionInProgress) return "transition_in_progress";
            if (code == ReturnToMenuActionResultCodes.ControlUnavailable) return "control_unavailable";
            if (code == ReturnToMenuActionResultCodes.MutationPermitUnavailable) return "action_family_unavailable";
            if (code == ReturnToMenuActionResultCodes.PostCommitFault) return "post_commit_fault";
            if (code == ReturnToMenuActionResultCodes.VerificationFailed) return "verification_failed";
        }
        if (commandKind == GameMcpCommandKind.CraftingStation)
        {
            if (code == CraftingStationActionResultCodes.ContractUnavailable) return "contract_unavailable";
            if (code == CraftingStationActionResultCodes.WrongThread) return "wrong_thread";
            if (code == CraftingStationActionResultCodes.IdentityUnavailable) return "identity_unavailable";
            if (code == CraftingStationActionResultCodes.SelectionUnavailable) return "selection_unavailable";
            if (code == CraftingStationActionResultCodes.SelectionHidden) return "selection_hidden";
            if (code == CraftingStationActionResultCodes.LevelOutOfRange) return "level_out_of_range";
            if (code == CraftingStationActionResultCodes.NotLoaded) return "recipe_incomplete";
            if (code == CraftingStationActionResultCodes.AlreadyInRequestedState) return "already_in_requested_state";
            if (code == CraftingStationActionResultCodes.MutationPermitUnavailable) return "action_family_unavailable";
            if (code == CraftingStationActionResultCodes.PostCommitFault) return "post_commit_fault";
            if (code == CraftingStationActionResultCodes.VerificationFailed) return "verification_failed";
        }
        if (commandKind == GameMcpCommandKind.Loadout)
        {
            if (code == LoadoutActionResultCodes.ContractUnavailable) return "contract_unavailable";
            if (code == LoadoutActionResultCodes.WrongThread) return "wrong_thread";
            if (code == LoadoutActionResultCodes.IdentityUnavailable) return "identity_unavailable";
            if (code == LoadoutActionResultCodes.WrongTargetType) return "wrong_loadout_surface";
            if (code == LoadoutActionResultCodes.AlreadyInRequestedState) return "already_in_requested_state";
            if (code == LoadoutActionResultCodes.SwitchBlocked) return "switch_blocked";
            if (code == LoadoutActionResultCodes.EntryUnavailable) return "saved_entry_unavailable";
            if (code == LoadoutActionResultCodes.SlotOutOfRange) return "slot_out_of_range";
            if (code == LoadoutActionResultCodes.SlotEmpty) return "slot_empty";
            if (code == LoadoutActionResultCodes.SlotOccupied) return "slot_occupied";
            if (code == LoadoutActionResultCodes.ActiveSectionEmpty) return "active_section_empty";
            if (code == LoadoutActionResultCodes.NameOutOfRange) return "name_out_of_range";
            if (code == LoadoutActionResultCodes.MutationPermitUnavailable) return "action_family_unavailable";
            if (code == LoadoutActionResultCodes.PostCommitFault) return "post_commit_fault";
            if (code == LoadoutActionResultCodes.VerificationFailed) return "verification_failed";
        }
        if (commandKind == GameMcpCommandKind.RitualLifecycle)
        {
            if (code == RitualLifecycleActionResultCodes.LevelOutOfRange) return "level_out_of_range";
            if (code == RitualLifecycleActionResultCodes.BattleAlreadyActive) return "ritual_battle_active";
            if (code == RitualLifecycleActionResultCodes.NoBattleActive) return "no_ritual_battle_active";
            if (code == RitualLifecycleActionResultCodes.WrongActiveRitual) return "wrong_active_ritual";
            if (code == RitualLifecycleActionResultCodes.ContractUnavailable) return "contract_unavailable";
            if (code == RitualLifecycleActionResultCodes.WrongThread) return "wrong_thread";
            if (code == RitualLifecycleActionResultCodes.IdentityUnavailable) return "identity_unavailable";
            if (code == RitualLifecycleActionResultCodes.NotDiscovered) return "not_discovered";
            if (code == RitualLifecycleActionResultCodes.AlreadyInRequestedState) return "already_in_requested_state";
            if (code == RitualLifecycleActionResultCodes.LevelLocked) return "level_locked";
            if (code == RitualLifecycleActionResultCodes.Unaffordable) return "unaffordable";
            if (code == RitualLifecycleActionResultCodes.NoDurationEffect) return "no_active_duration_reward";
            if (code == RitualLifecycleActionResultCodes.MutationPermitUnavailable) return "action_family_unavailable";
            if (code == RitualLifecycleActionResultCodes.PostCommitFault) return "post_commit_fault";
            if (code == RitualLifecycleActionResultCodes.VerificationFailed) return "verification_failed";
        }
        if (commandKind == GameMcpCommandKind.HarvestLifecycle)
        {
            if (code == HarvestLifecycleActionResultCodes.ElementUsageUnavailable) return "element_capacity_unavailable";
            if (code == HarvestLifecycleActionResultCodes.ActionUnavailable) return "action_not_available";
            if (code == HarvestLifecycleActionResultCodes.AmountUnavailable) return "amount_unavailable";
            if (code == HarvestLifecycleActionResultCodes.NotActive) return "not_active";
        }
        if (commandKind == GameMcpCommandKind.Harvest)
        {
            if (code == PlotLifecycleActionResultCodes.ActionUnavailable) return "action_not_available";
            if (code == PlotLifecycleActionResultCodes.QuantityUnavailable) return "amount_unavailable";
            if (code == PlotLifecycleActionResultCodes.NotActive) return "not_active";
        }
        if (code == AutoCastActionResultCodes.ChargeHoldRefused)
            return "charge_hold_refused";
        if (code == AutoConceptActionResultCodes.ActionFamilyUnavailable)
            return "action_family_unavailable";
        if (code == AutoConceptActionResultCodes.RecipeIdentityChanged)
            return "recipe_identity_changed";
        if (code == AutoConceptActionResultCodes.AssignmentUnsettled)
            return "assignment_unsettled";
        if (code == AutoConceptActionResultCodes.OwnershipChanged)
            return "ownership_changed";
        if (code == AutoConceptActionResultCodes.SlotUnavailable)
            return "slot_unavailable";
        if (code == AutoConceptActionResultCodes.ProjectionRefused)
            return "projection_refused";
        if (code == AutoConceptActionResultCodes.MasteryLimitChanged)
            return "mastery_limit_changed";
        if (code == AutoConceptActionResultCodes.AmountUnavailable)
            return "amount_unavailable";

        // A feature result number is not a wire vocabulary. `feature_1990` named no axis a caller
        // could act on and taught nothing; the class it maps to says the game refused, which is
        // exactly what an unmapped result code means, and the producer's sentence carries the rest.
        return "native_rejected";
    }
}

/// <summary>Pure, ordered admission checks applied before a main-thread native adapter is selected.</summary>
internal static class GameMcpNativeActionAdmission
{
    internal static bool TryReject(
        GameMcpCommand command,
        long currentLifecycleGeneration,
        ulong currentConfigurationGeneration,
        bool emergencyStopEngaged,
        out GameMcpCommandResult rejection)
    {
        if (command.ExpectedLifecycleGeneration != currentLifecycleGeneration)
        {
            rejection = GameMcpCommandResult.Rejected(
                "lifecycle_replaced",
                "command expected lifecycle " + command.ExpectedLifecycleGeneration +
                " but the main thread now has lifecycle " + currentLifecycleGeneration,
                currentLifecycleGeneration,
                currentConfigurationGeneration);
            return true;
        }
        if (command.ExpectedConfigurationGeneration != currentConfigurationGeneration)
        {
            rejection = GameMcpCommandResult.Rejected(
                "stale_configuration_generation",
                "command expected configuration generation " +
                command.ExpectedConfigurationGeneration +
                " but the main thread now has generation " +
                currentConfigurationGeneration,
                currentLifecycleGeneration,
                currentConfigurationGeneration);
            return true;
        }
        if (emergencyStopEngaged)
        {
            rejection = GameMcpCommandResult.Rejected(
                "emergency_stop",
                "the suite emergency stop is engaged; no MCP native action was attempted",
                currentLifecycleGeneration,
                currentConfigurationGeneration);
            return true;
        }
        rejection = null!;
        return false;
    }

    internal static void AssertNativeType(GameMcpCommand command, string derived)
    {
        if (!string.Equals(command.DerivedNativeType, derived, StringComparison.Ordinal))
            throw new ArgumentException(
                "the server-derived native type must be exactly " + derived +
                " for " + command.Kind + ", not " + command.DerivedNativeType);
    }
}
#endif
