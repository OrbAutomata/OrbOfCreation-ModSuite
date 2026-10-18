#if SERVICE_CYCLE_PROFILE
using System;
using System.Collections.Generic;
using System.Diagnostics;
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
    EquipmentLoadout = 23,
    Challenge = 24,
    Prestige = 25,
    Research = 26,
    AlchemyLoadout = 27,
    RitualLifecycle = 28,
    GenericLevel = 29,
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
        AutomataActionFamilyOwnership.NamesGameMcpKind(kind);

    internal static bool IsEntityGameplayAction(GameMcpCommandKind kind) =>
        IsGameplayAction(kind) && kind != GameMcpCommandKind.ReturnToMenu;

    /// <summary>
    /// Whether a committed mutation's answer needs a world captured after it, or already holds every
    /// fact it is going to state.
    /// </summary>
    /// <remarks>
    /// A purchase answers from the queued-level delta its own native verifier observed, so there is
    /// nothing left for a settled world to add: it queues levels the game drains over the following
    /// seconds, and every world after the press describes a queue already draining rather than the
    /// press. Waiting for one could only turn a purchase that verifiably committed into a
    /// <c>post_state_timeout</c>, which is the same lie in a slower costume.
    /// </remarks>
    internal static bool RequiresPostStateSettlement(GameMcpCommandKind kind) =>
        IsGameplayAction(kind) &&
        kind != GameMcpCommandKind.ReturnToMenu &&
        kind != GameMcpCommandKind.Purchase;

    internal static GameMcpCommandKind FromToolName(string toolName) => toolName switch
    {
        "game_purchase" => GameMcpCommandKind.Purchase,
        "game_cast" => GameMcpCommandKind.Cast,
        "game_concept" => GameMcpCommandKind.Concept,
        "game_agromancy" => GameMcpCommandKind.HarvestLifecycle,
        "game_spell_mastery" => GameMcpCommandKind.SpellLevel,
        "game_casting_dial" => GameMcpCommandKind.SpellComposition,
        "game_spell_loadout" => GameMcpCommandKind.SpellLoadout,
        "game_targeting" => GameMcpCommandKind.Targeting,
        "game_consumable" => GameMcpCommandKind.Consumable,
        "game_craft" => GameMcpCommandKind.Crafting,
        "game_discover" => GameMcpCommandKind.DiscoveryTreeOffer,
        "game_equipment" => GameMcpCommandKind.EquipmentLoadout,
        "time_challenge" => GameMcpCommandKind.Challenge,
        "time_prestige" => GameMcpCommandKind.Prestige,
        "game_research" => GameMcpCommandKind.Research,
        "game_alchemy" => GameMcpCommandKind.AlchemyLoadout,
        "game_ritual" => GameMcpCommandKind.RitualLifecycle,
        "game_level_up" => GameMcpCommandKind.GenericLevel,
        "game_loadout" => GameMcpCommandKind.Loadout,
        "game_structure" => GameMcpCommandKind.StructureLifecycle,
        "game_return_to_menu" => GameMcpCommandKind.ReturnToMenu,
        "game_modal" => GameMcpCommandKind.Modal,
        "suite_config_set" => GameMcpCommandKind.ConfigurationSet,
        "suite_breakers" => GameMcpCommandKind.AutomationSet,
        "suite_emergency_stop" => GameMcpCommandKind.EmergencyStop,
        "game_screenshot" => GameMcpCommandKind.Screenshot,
        "game_navigate" => GameMcpCommandKind.Navigation,
        "game_probe" => GameMcpCommandKind.Probe,
        "game_screen_catalog" => GameMcpCommandKind.ScreenCatalog,
        "game_screen_elements" => GameMcpCommandKind.TooltipCatalog,
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
        if (toolName == "game_spell_loadout" && mode == "add")
            return GameMcpCommandKind.SpellWorkbench;
        if (toolName == "game_agromancy" &&
            mode is "add_plot_action" or "remove_plot_action")
            return GameMcpCommandKind.Harvest;
        if (mode is "set_druidry_level" or "set_alchemy_level")
            return GameMcpCommandKind.SpellComposition;
        return kind;
    }

    internal static string ToolName(GameMcpCommandKind kind) => kind switch
    {
        GameMcpCommandKind.Purchase => "game_purchase",
        GameMcpCommandKind.Cast => "game_cast",
        GameMcpCommandKind.Concept => "game_concept",
        GameMcpCommandKind.Harvest => "game_agromancy",
        GameMcpCommandKind.SpellLevel => "game_spell_mastery",
        GameMcpCommandKind.DiscoveryTreeOffer => "game_discover",
        GameMcpCommandKind.SpellWorkbench => "game_spell_loadout",
        GameMcpCommandKind.SpellComposition => "game_casting_dial",
        GameMcpCommandKind.SpellLoadout => "game_spell_loadout",
        GameMcpCommandKind.Targeting => "game_targeting",
        GameMcpCommandKind.Consumable => "game_consumable",
        GameMcpCommandKind.Crafting => "game_craft",
        GameMcpCommandKind.EquipmentLoadout => "game_equipment",
        GameMcpCommandKind.Challenge => "time_challenge",
        GameMcpCommandKind.Prestige => "time_prestige",
        GameMcpCommandKind.Research => "game_research",
        GameMcpCommandKind.AlchemyLoadout => "game_alchemy",
        GameMcpCommandKind.RitualLifecycle => "game_ritual",
        GameMcpCommandKind.GenericLevel => "game_level_up",
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
    private readonly long _startedAtRawTicks = Stopwatch.GetTimestamp();

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

    /// <summary>The verb this command was asked for, empty when it carries no operation.</summary>
    internal string ToolName => SourceOperation?.Request.ToolName ?? string.Empty;

    /// <summary>
    /// How long this command has been alive. A command that waits for post-state settlement spans
    /// several frames, and the ledger could not tell one of those from an instant read.
    /// </summary>
    internal double ElapsedMilliseconds =>
        (Stopwatch.GetTimestamp() - _startedAtRawTicks) * 1000.0 / Stopwatch.Frequency;
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

    /// <summary>
    /// A stop whose reason the suite owns. It is the same word
    /// <see cref="GameMcpDecisionReason.IsSuiteDefect"/> derives from a result code, spelled out
    /// for the sites that hold their own sentence and never go through a code.
    /// </summary>
    internal static GameMcpCommandResult Failed(
        string code,
        string reason,
        long observedLifecycleGeneration = 0,
        ulong observedConfigurationGeneration = 0) =>
        new(
            "failed",
            code,
            reason,
            observedLifecycleGeneration,
            observedConfigurationGeneration,
            null,
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
        long observedLifecycleGeneration,
        ulong observedConfigurationGeneration,
        GameMcpValue? details = null)
    {
        var code = result.Code.Name;
        // Three words, three owners. The disposition answers whether the mutation ran, which is
        // the service-cycle contract's question and is right as it stands; who stopped the call is
        // a different question, and it is answered here, where the wire's vocabulary lives. A
        // fifth disposition would have made every service adapter answer a question none of them
        // asks.
        var status = result.Disposition switch
        {
            ServiceActionDisposition.Committed => "committed",
            ServiceActionDisposition.Faulted => "faulted",
            // A stop nobody accounted for is the suite failing to say anything, not the game
            // saying no — the word has to follow the account, not the disposition alone.
            ServiceActionDisposition.Rejected or ServiceActionDisposition.Skipped =>
                GameMcpDecisionReason.IsSuiteDefect(code) ||
                (string.IsNullOrWhiteSpace(result.Reason) && !GameMcpDecisionReason.Knows(code))
                    ? "failed"
                    : "refused",
            _ => "faulted",
        };
        var reason = status == "committed"
            ? string.Empty
            : string.IsNullOrWhiteSpace(result.Reason)
                ? GameMcpDecisionReason.Knows(code)
                    ? GameMcpDecisionReason.For(code)
                    : GameMcpDecisionReason.NoAccount
                : result.Reason;
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

    /// <summary>
    /// The settled world joins what the press already said instead of replacing it.
    /// </summary>
    /// <remarks>
    /// A <c>×2</c> level buy off a ladder whose first rung is free printed its two levels and
    /// nothing at all about its cost. The press projection is the only producer that saw every
    /// rung's price, and settlement handed the wire a document built from the world alone, so that
    /// answer was constructed and then thrown away — on every settlement-bearing kind, not only
    /// this one. The settled world is the newer truth for every key it names; everything only the
    /// press could know stays.
    /// </remarks>
    internal GameMcpCommandResult WithSettledPostState(GameMcpValue state)
    {
        if (state is not GameMcpObject settled || Details is not GameMcpObject pressed)
            return WithDetails(state);
        var merged = new GameMcpObjectBuilder();
        for (var index = 0; index < settled.Properties.Count; index++)
            merged.Add(settled.Properties[index].Name, settled.Properties[index].Value);
        for (var index = 0; index < pressed.Properties.Count; index++)
        {
            var property = pressed.Properties[index];
            if (!merged.Contains(property.Name)) merged.Add(property.Name, property.Value);
        }
        return WithDetails(merged.Freeze());
    }

    internal GameMcpValue Project(GameMcpCommand command)
    {
        if (command is null) throw new ArgumentNullException(nameof(command));
        var stableCode = Code;
        if (HasActionResult)
            stableCode = ActionResult.Code.Name;
        var status = Status;
        if (command.SourceOperation?.Request.Classification == GameMcpOperationClass.ReadOnly)
        {
            // A read answers `available` or `unavailable` and has no third word: `failed` is a
            // mutation's answer, and a read that cannot serve a fact is unavailable however it got
            // that way. The sentence still says who stopped it.
            status = status switch
            {
                "committed" => "available",
                "refused" or "failed" => "unavailable",
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

            // A loaded spell is a runtime instance no catalog names, so its id echoed as
            // "(unnamed 96a6b7)". The caller addressed it by slot, and the sentence names the spell.
            if (command.Kind == GameMcpCommandKind.SpellLoadout && command.SourceOperation is { } press)
                projected["slot"] = press.Request.Amount;
            else if (command.TargetId != Guid.Empty)
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
                GameMcpDecisionReason.For("stale_configuration_generation"),
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
