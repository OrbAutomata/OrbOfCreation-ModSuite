#if SERVICE_CYCLE_PROFILE
using System;
using OrbModding.Common;

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

        var descriptor = GameMcpCommandCatalog.Shipping.Descriptor(kind);
        var draft = new GameMcpCommandDraft(request, descriptor.NativeType);
        descriptor.Prepare?.Invoke(request, context, draft);

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
            draft.Mode.Length == 0 ? request.ToolName : draft.Mode,
            draft.TargetId,
            draft.SecondaryId,
            draft.NativeType,
            draft.Amount <= 0 ? 1 : draft.Amount,
            draft.PayloadKey,
            draft.PayloadValue,
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
        if (draft.Failure is not null)
        {
            failure = draft.Failure;
            return false;
        }
        var reason = string.Empty;
        if (GameMcpCommandKinds.IsEntityGameplayAction(kind) &&
            (draft.NativeType.Length == 0 || !GameMcpEntityCapabilityMap.Contains(
                context.World.Snapshot,
                draft.TargetId,
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
                    draft.TargetId,
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
                    draft.TargetId,
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

internal sealed class GameMcpCommandDraft
{
    internal string Mode;
    internal Guid TargetId;
    internal Guid SecondaryId;
    internal string NativeType;
    internal int Amount;
    internal string PayloadKey = string.Empty;
    internal string PayloadValue = string.Empty;
    internal GameMcpCommandResult? Failure;

    internal GameMcpCommandDraft(GameMcpOperationRequest request, string nativeType)
    {
        Mode = request.Mode;
        TargetId = request.Uuid;
        SecondaryId = request.SecondaryUuid;
        NativeType = nativeType;
        Amount = request.Amount;
    }
}
#endif
