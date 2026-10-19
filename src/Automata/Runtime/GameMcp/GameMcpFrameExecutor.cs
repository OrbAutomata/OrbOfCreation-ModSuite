#if SERVICE_CYCLE_PROFILE
using System;
using System.Collections;
using System.Linq;
using BepInEx.Logging;
using OrbModConfig;
using OrbModding.Common;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal.Status;
using OrbModding.Common.Runtime.World;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OrbAutomata.GameMcp;

internal sealed class GameMcpFrameExecutor : IDisposable
{
    private readonly MonoBehaviour _host;
    private readonly ManualLogSource _log;
    private readonly Func<long> _lifecycleGeneration;
    private readonly Func<AutomataServiceCycleActivation?> _serviceCycleActivation;
    private readonly Func<AutomataActionFamilyOwnership?> _automataActionFamilyOwnership;
    private readonly AutomataConfigurationStore _configurationStore;
    private readonly AutomataDifferentialVerificationControl _mathVerification;
    private readonly AutomataWorldDemand _worldDemand;
    private readonly Func<bool> _nativeContractsAvailable;
    private GameMcpFrameInbox? _gameMcpOperations;
    private GameMcpHttpServer? _gameMcpServer;
    private GameMcpWritableSettingDescriptor[] _gameMcpWritableConfiguration =
        Array.Empty<GameMcpWritableSettingDescriptor>();
    private GameMcpTooltipNativeAccess? _gameMcpTooltipNativeAccess;
    private readonly GameMcpGadgets _gadgets;
    private readonly GameMcpFrameCommandScope _commandScope;
    private string _gameMcpTooltipContractFailure =
        "tooltip native layout has not been bound";

    private string _gameMcpAgentSettingsFailure = string.Empty;

    internal GameMcpFrameExecutor(
        MonoBehaviour host,
        ManualLogSource log,
        Func<ModConfigUiShell?> uiShell,
        Func<long> lifecycleGeneration,
        Func<AutomataServiceCycleActivation?> serviceCycleActivation,
        Func<AutomataActionFamilyOwnership?> automataActionFamilyOwnership,
        AutomataConfigurationStore configurationStore,
        AutomataDifferentialVerificationControl mathVerification,
        AutomataWorldDemand worldDemand,
        Func<bool> nativeContractsAvailable,
        Func<bool> runtimeActivationAllowed,
        AutomaticSaveBackupStatus automaticSaveBackup,
        Action<bool> emergencyStopChanged,
        GameMcpWritableSettingDescriptor[] writableConfiguration)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _lifecycleGeneration = lifecycleGeneration ??
            throw new ArgumentNullException(nameof(lifecycleGeneration));
        _serviceCycleActivation = serviceCycleActivation ??
            throw new ArgumentNullException(nameof(serviceCycleActivation));
        _automataActionFamilyOwnership = automataActionFamilyOwnership ??
            throw new ArgumentNullException(nameof(automataActionFamilyOwnership));
        _configurationStore = configurationStore ??
            throw new ArgumentNullException(nameof(configurationStore));
        _mathVerification = mathVerification ??
            throw new ArgumentNullException(nameof(mathVerification));
        _worldDemand = worldDemand ?? throw new ArgumentNullException(nameof(worldDemand));
        _nativeContractsAvailable = nativeContractsAvailable ??
            throw new ArgumentNullException(nameof(nativeContractsAvailable));
        _gadgets = new GameMcpGadgets(
            host,
            uiShell,
            CompleteGameMcpCommand,
            lifecycleGeneration,
            () => _configurationStore.CurrentGeneration.Value,
            () => _gameMcpTooltipNativeAccess,
            () => _gameMcpTooltipContractFailure,
            message => _log.LogWarning(message));
        _commandScope = new GameMcpFrameCommandScope(
            host,
            uiShell,
            CompleteGameMcpCommand,
            CompleteContinueRunGameMcp,
            _gadgets,
            _configurationStore,
            lifecycleGeneration,
            runtimeActivationAllowed ??
                throw new ArgumentNullException(nameof(runtimeActivationAllowed)),
            automaticSaveBackup ??
                throw new ArgumentNullException(nameof(automaticSaveBackup)),
            emergencyStopChanged ??
                throw new ArgumentNullException(nameof(emergencyStopChanged)));
        if (!GameMcpTooltipNativeAccess.TryCreate(
                typeof(HoverTooltip),
                typeof(Spell),
                typeof(PassiveAbility),
                typeof(PlotNodeActionInstance),
                out _gameMcpTooltipNativeAccess,
                out _gameMcpTooltipContractFailure))
        {
            _log.LogWarning(
                "Game MCP tooltip inspection is unavailable: " +
                _gameMcpTooltipContractFailure);
        }
        _gameMcpWritableConfiguration = writableConfiguration;
        _gadgets.ComposeModalActions();
        _gameMcpOperations = new GameMcpFrameInbox();
        _gameMcpServer = GameMcpHttpServer.TryStart(
            _gameMcpOperations,
            GameMcpToolCatalog.Shipping,
            GameMcpCommandCatalog.Shipping,
            message => _log.LogInfo(message),
            message => _log.LogError(message));
    }

    internal bool ServerRunning => _gameMcpServer is not null;

    internal void InvalidateLifecycle()
    {
        _gadgets.InvalidateLifecycle();
        _gameMcpAgentSettingsFailure = string.Empty;
    }

    public void Dispose()
    {
        _gameMcpOperations?.Close(
            "suite_shutdown",
            "the suite is shutting down; pending MCP commands cannot mutate native state");
        _gameMcpServer?.Dispose();
        _gameMcpServer = null;
        _gameMcpOperations = null;
        _gameMcpWritableConfiguration = Array.Empty<GameMcpWritableSettingDescriptor>();
        _gameMcpTooltipNativeAccess = null;
        _gadgets.ReleaseModalActions();
        _gameMcpTooltipContractFailure = "tooltip native layout has been released";
    }

    internal void Drain()
    {
        if (_gameMcpOperations is null) return;
        GameMcpFrameBatchExecutor.Drain(
            _gameMcpOperations,
            CaptureGameMcpFrameContext,
            ExecuteGameMcpFrameOperation,
            ProjectGameMcpFrameOperationFault);
    }

    private GameMcpToolExecution? ExecuteGameMcpFrameOperation(
        GameMcpFrameOperation operation,
        GameMcpFrameContext context)
    {
        // Every operation the frame answers itself writes its own ledger line here, so the sequence
        // has no holes in it: a read used to draw an operation number and never write a completion,
        // and a command refused inside its claiming frame did the same. A command that leaves the
        // frame — a gadget, or a mutation waiting for post-state settlement — returns null and is
        // written when it completes instead, so nothing is written twice.
        var startedAtRawTicks = System.Diagnostics.Stopwatch.GetTimestamp();
        if (!TryExecuteGameMcpFrameOperation(operation, context, out var result))
            return null;
        var answered = result.WithEntityIdentities(context.EntityIdentities);
        _log.LogAutomataInfo(GameMcpOperationLedger.DescribeAnswered(
            operation,
            answered,
            Time.frameCount,
            (System.Diagnostics.Stopwatch.GetTimestamp() - startedAtRawTicks) * 1000.0 /
                System.Diagnostics.Stopwatch.Frequency));
        return answered;
    }

    /// <summary>
    /// The answer for an operation that threw inside the frame.
    /// </summary>
    /// <remarks>
    /// It shipped the exception's own message, which names types and members from inside the suite
    /// and the game: a caller can act on none of it, and it is the one thing whoever fixes the
    /// defect needs. It goes to the suite log under a reference the caller is handed and can quote.
    /// </remarks>
    private GameMcpToolExecution ProjectGameMcpFrameOperationFault(
        GameMcpFrameOperation operation,
        GameMcpFrameContext? context,
        Exception exception)
    {
        var reference = "MCP-" + Guid.NewGuid().ToString("N").Substring(0, 8).ToUpperInvariant();
        _log.LogError(
            "Game MCP internal error " + reference + " on " +
            operation.Request.ToolName + ": " + exception);
        var result = new GameMcpObjectBuilder
        {
            ["status"] = "faulted",
            ["code"] = "operation_dispatch_fault",
            ["reason"] = GameMcpProtocolRouter.InternalErrorReason(
                operation.Request.ToolName, reference),
        };
        return GameMcpToolExecution.Error(result.Freeze()).WithEntityIdentities(
            context is null
                ? EntityIdentityCatalogPublication.Current
                : context.EntityIdentities);
    }

    private void CompleteGameMcpCommand(
        GameMcpCommand command,
        GameMcpCommandResult result)
    {
        if (command.SourceOperation is not null && command.FrameContext is not null)
        {
            var payload = result.Project(command);
            _gameMcpOperations?.Complete(
                command.SourceOperation,
                new GameMcpToolExecution(
                    payload,
                    result.InlinePng,
                    result.IsProtocolError,
                    command.FrameContext.EntityIdentities));
        }
        _log.LogAutomataInfo(
            GameMcpOperationLedger.Describe(command, result, Time.frameCount));
    }

    private GameMcpFrameContext CaptureGameMcpFrameContext(GameMcpFrameData required)
    {
        var includeServices = (required & GameMcpFrameData.ServiceHealth) != 0;
        AutomataRuntimeFrameFacts? runtime = null;
        if ((required & (GameMcpFrameData.World | GameMcpFrameData.ServiceHealth)) != 0 &&
            _serviceCycleActivation() is { } activation &&
            activation.TryCaptureFrameFacts(includeServices, out var captured))
        {
            runtime = captured;
        }

        var configuration = runtime?.Configuration ?? new ConfigurationPublication(
            _configurationStore.CurrentGeneration,
            _configurationStore.Current);
        var features = (required & GameMcpFrameData.FeatureHealth) != 0
            ? FeatureStatusRegistry.Shared.GetSnapshot().ToArray()
            : Array.Empty<FeatureStatusSnapshot>();
        var trace = (required & GameMcpFrameData.TraceWriterHealth) != 0
            ? DecisionJournalStatusRegistry.Shared.Status
            : DecisionJournalStatus.Unavailable;
        var traceRevision = (required & GameMcpFrameData.TraceWriterHealth) != 0
            ? DecisionJournalStatusRegistry.Shared.Revision
            : 0;
        var writable = (required & GameMcpFrameData.WritableConfiguration) != 0
            ? _gameMcpWritableConfiguration
            : Array.Empty<GameMcpWritableSettingDescriptor>();
        return new GameMcpFrameContext(
            runtime?.World,
            runtime,
            configuration,
            _lifecycleGeneration(),
            (required & GameMcpFrameData.Scene) != 0
                ? SceneManager.GetActiveScene().name
                : string.Empty,
            (required & GameMcpFrameData.NativeContractHealth) != 0 &&
                _nativeContractsAvailable(),
            features,
            trace,
            traceRevision,
            writable,
            _gadgets.ModalBindingsAvailable,
            _gadgets.ModalBindingFailure(),
            GameLifecycleMonitor.Shared.Current.State,
            _gameMcpAgentSettingsFailure);
    }

    private bool TryExecuteGameMcpFrameOperation(
        GameMcpFrameOperation operation,
        GameMcpFrameContext context,
        out GameMcpToolExecution execution)
    {
        var activation = _serviceCycleActivation();
        var request = operation.Request;
        if (GameMcpFrameReads.TryExecuteFrameRead(request, context, out execution)) return true;
        if (request.Route.HostRead is { } hostRead)
        {
            execution = hostRead(this, request);
            return true;
        }

        if (!GameMcpCommandPreparation.TryPrepareGameMcpCommand(
                operation, context, out var command, out var failure))
        {
            execution = GameMcpFrameReads.ProjectGameMcpCommand(command, failure);
            return true;
        }

        if (!GameMcpCommandKinds.IsGameplayAction(command.Kind))
        {
            var answered = GameMcpCommandCatalog.Shipping.FrameExecutor(command.Kind)(
                command, context, _commandScope);
            if (answered is null)
            {
                execution = null!;
                return false;
            }
            execution = GameMcpFrameReads.ProjectGameMcpCommand(command, answered);
            return true;
        }
        var ownership = _automataActionFamilyOwnership();
        if (ownership is null)
        {
            execution = GameMcpFrameReads.ProjectGameMcpCommand(
                command,
                GameMcpCommandResult.Rejected(
                    "action_family_unavailable",
                    "the action-family ownership registry is unavailable"));
            return true;
        }
        if (!ownership.TryBeginGameMcpOperation(
                command.Kind,
                command.Mode,
                out var ownershipScope,
                out var ownershipReason))
        {
            execution = GameMcpFrameReads.ProjectGameMcpCommand(
                command,
                GameMcpCommandResult.Rejected(
                    "action_family_unavailable",
                    ownershipReason.Length == 0
                        ? "the exact gameplay action family could not be claimed"
                        : ownershipReason));
            return true;
        }
        GameMcpCommandResult result;
        using (ownershipScope)
        {
            if (activation is null ||
                !activation.TryExecuteGameMcp(command, out result))
            {
                result = GameMcpCommandResult.Failed(
                    "runtime_not_available",
                    "The suite is not running on this screen, so no action can be sent to the " +
                    "game.");
            }
        }
        if (string.Equals(result.Status, "committed", StringComparison.Ordinal))
        {
            if (!GameMcpPostStateSettlement.Settles(command))
            {
                execution = GameMcpFrameReads.ProjectGameMcpCommand(command, result);
                return true;
            }
            var actionCompletedAtUtcTicks = DateTime.UtcNow.Ticks;
            _host.StartCoroutine(CompleteGameMcpGameplayPostState(
                command,
                result,
                context.World?.Generation.Value ?? 0,
                actionCompletedAtUtcTicks));
            execution = null!;
            return false;
        }
        execution = GameMcpFrameReads.ProjectGameMcpCommand(command, result);
        return true;
    }

    private IEnumerator CompleteGameMcpGameplayPostState(
        GameMcpCommand command,
        GameMcpCommandResult committed,
        ulong actionWorldGeneration,
        long actionCompletedAtUtcTicks)
    {
        var deadline = Time.realtimeSinceStartup + GameMcpPostStateSettlement.WaitSeconds(command);
        GameMcpFrameContext? latest = null;
        _worldDemand.Begin();
        try
        {
            while (Time.realtimeSinceStartup < deadline)
            {
                yield return null;
                latest = CaptureGameMcpFrameContext(GameMcpPostStateSettlement.Data(command));
                if (GameMcpPostStateSettlement.IsReady(
                        latest, actionWorldGeneration, actionCompletedAtUtcTicks, command))
                    break;
            }
        }
        finally
        {
            _worldDemand.End();
        }

        GameMcpValue state;
        if (GameMcpPostStateSettlement.IsReady(
                latest, actionWorldGeneration, actionCompletedAtUtcTicks, command))
        {
            state = GameMcpPostStateSettlement.Project(
                latest!, command, committed);
        }
        else
        {
            state = GameMcpPostStateSettlement.TimedOut(command, latest);
        }
        CompleteGameMcpCommand(command, committed.WithSettledPostState(state));
    }

    internal GameMcpToolExecution CaptureScreenCatalog() =>
        GameMcpToolExecution.Read(_gadgets.ScreenReader.CaptureScreenCatalogGameMcp());

    internal GameMcpToolExecution PreviewSpellWorkbench(GameMcpOperationRequest request)
    {
        var glyphs = new SpellWorkbenchGlyphStack[request.UuidCounts.Length];
        for (var index = 0; index < glyphs.Length; index++)
            glyphs[index] = new SpellWorkbenchGlyphStack(
                request.UuidCounts[index].Uuid,
                request.UuidCounts[index].Count);
        var previewRequest = new SpellWorkbenchLoadPreviewRequest(
            request.Uuid,
            _lifecycleGeneration(),
            glyphs);
        SpellWorkbenchLoadPreview preview;
        if (_serviceCycleActivation() is not { } activation ||
            !activation.TryPreviewSpellWorkbench(
                in previewRequest,
                out preview))
        {
            preview = SpellWorkbenchLoadPreview.Refused(
                SpellWorkbenchActionResultCodes.ContractUnavailable,
                "The suite is not running on this screen, so nothing can be previewed.");
        }
        return GameMcpToolExecution.Read(
            GameMcpSpellWorkbenchProjection.ProjectLoadPreview(
                in preview));
    }

    internal GameMcpToolExecution ReadStagedSpellWorkbench()
    {
        SpellWorkbenchStagedLayout staged;
        if (_serviceCycleActivation() is not { } activation ||
            !activation.TryReadStagedSpellWorkbench(out staged))
        {
            staged = SpellWorkbenchStagedLayout.Unavailable(
                SpellWorkbenchActionResultCodes.ContractUnavailable,
                "The suite is not running on this screen, so nothing can be read here.");
        }
        return GameMcpToolExecution.Read(
            GameMcpSpellWorkbenchProjection.ProjectStagedLayout(in staged));
    }

    /// <summary>
    /// Runs the differential check the Runtime page's action runs, and answers with its verdict
    /// lines instead of leaving them in the log for a human to find.
    /// </summary>
    /// <remarks>
    /// This is the whole run, in this frame, on the Unity thread — the same seconds-long stall the
    /// button costs. It is deliberately not spread over frames: every pass would otherwise read a
    /// different frame's game state and the verdicts would not be comparable to each other.
    /// </remarks>
    internal GameMcpToolExecution RunGameMcpGameMathCheck()
    {
        // A no is a result. `isError` is reserved for a tool that failed before it produced a
        // domain answer, so it arrives the way every other refusal in the suite does — a caller
        // branches on the class, not on which of two transports carried it.
        if (!_mathVerification.TryRunNow(out var lines, out var code, out var reason))
        {
            return GameMcpToolExecution.Read(new GameMcpObjectBuilder
            {
                ["status"] = "refused",
                ["reasonCode"] = code,
                ["reason"] = reason,
            });
        }
        return GameMcpToolExecution.Text(string.Join("\n", lines));
    }

    private IEnumerator CompleteContinueRunGameMcp(
        GameMcpCommand command,
        GameMcpCommandResult committed)
    {
        const float timeoutSeconds = 10f;
        var deadline = Time.realtimeSinceStartup + timeoutSeconds;
        GameMcpFrameContext state;
        do
        {
            yield return null;
            state = CaptureGameMcpFrameContext(GameMcpFrameData.World | GameMcpFrameData.Scene);
        }
        while (Time.realtimeSinceStartup < deadline &&
               (string.Equals(state.SceneName, "Start", StringComparison.Ordinal) ||
                !state.RuntimeAvailable));

        // The load leaves the game in the shape every documented verb assumes, and says nothing
        // about it: an unattended caller should never have to know a settings screen exists. A
        // normalization that did not land is the one case worth a word, and health is where the
        // suite's own broken capabilities are already named — the log alone reaches nobody the
        // refusals will land on.
        _gameMcpAgentSettingsFailure = string.Empty;
        if (!string.Equals(state.SceneName, "Start", StringComparison.Ordinal) &&
            !AgentSettingsNormalization.TryNormalize(out var settingsFailure))
        {
            _gameMcpAgentSettingsFailure = settingsFailure;
            _log.LogWarning(
                "Game MCP could not normalize the agent-required game settings: " + settingsFailure);
        }

        CompleteGameMcpCommand(command, committed.WithDetails(
            GameMcpContinueProjection.Project(state.SceneName, state.RuntimeAvailable)));
    }
}
#endif
