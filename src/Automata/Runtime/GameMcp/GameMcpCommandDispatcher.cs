#if SERVICE_CYCLE_PROFILE
using System;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Profile;
using OrbModding.Common.Runtime.World;

namespace OrbAutomata.GameMcp;

internal sealed class GameMcpCommandDispatcher
{
    private readonly AutomataServiceCycleHost _host;
    private readonly IAutomataServiceCycleFeatureRuntime[] _features;
    private readonly ServiceConfigurationPublisher _configurationPublication;
    private readonly LifecycleGameActions _gameActions;
    private bool _disposed;
    private ulong _nextGameMcpActionIdentity;

    internal GameMcpCommandDispatcher(
        AutomataServiceCycleHost host,
        ServiceConfigurationPublisher configurationPublication,
        LifecycleGameActions gameActions,
        IAutomataServiceCycleFeatureRuntime[] features)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _configurationPublication = configurationPublication ??
            throw new ArgumentNullException(nameof(configurationPublication));
        _gameActions = gameActions ?? throw new ArgumentNullException(nameof(gameActions));
        _features = features ?? throw new ArgumentNullException(nameof(features));
    }

    internal void Dispose() => _disposed = true;

    public GameMcpCommandResult ExecuteGameMcp(GameMcpCommand command)
    {
        if (command is null) throw new ArgumentNullException(nameof(command));
        if (_disposed)
            return GameMcpCommandResult.Rejected(
                "runtime_disposed",
                "the ServiceCycle runtime has been disposed");

        var registry = _host.Pump.DiagnosticsRegistry;
        var world = registry.World.ReadLatest();
        var configuration = _configurationPublication.ReadLatest();
        var lifecycle = checked((long)_host.CurrentLifecycle.Value);
        if (GameMcpNativeActionAdmission.TryReject(
                command,
                lifecycle,
                configuration.Generation.Value,
                _host.EmergencyStopEngaged,
                out var rejection))
            return rejection;

        try
        {
            return GameMcpCommandCatalog.Shipping.Executor(command.Kind)(
                command,
                new GameMcpCommandScope(
                    this, _gameActions, registry, world, configuration, lifecycle));
        }
        catch (GameMcpActionUnavailableException exception)
        {
            return GameMcpCommandResult.Rejected(
                exception.Code,
                exception.Message,
                lifecycle,
                configuration.Generation.Value);
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException or
                OverflowException or ObjectDisposedException)
        {
            return GameMcpCommandResult.Faulted(
                "main_thread_execution_fault",
                "server defect: the " + command.Kind + " command did not finish on the main thread." +
                GameActionFaultLog.Record(exception, command.Kind + " command"),
                lifecycle,
                configuration.Generation.Value);
        }
    }

    public SpellWorkbenchLoadPreview PreviewSpellWorkbench(
        in SpellWorkbenchLoadPreviewRequest request)
    {
        if (_disposed)
            return SpellWorkbenchLoadPreview.Refused(
                SpellWorkbenchActionResultCodes.ContractUnavailable,
                "The ServiceCycle runtime has been disposed.");
        var spellWorkbench = _gameActions.Find<SpellWorkbenchGameAction>();
        if (spellWorkbench is null)
            return SpellWorkbenchLoadPreview.Refused(
                SpellWorkbenchActionResultCodes.ContractUnavailable,
                "The shared spell workbench boundary is unavailable.");
        return spellWorkbench.Preview(in request);
    }

    public SpellWorkbenchStagedLayout ReadStagedSpellWorkbench()
    {
        if (_disposed)
            return SpellWorkbenchStagedLayout.Unavailable(
                SpellWorkbenchActionResultCodes.ContractUnavailable,
                "The ServiceCycle runtime has been disposed.");
        var spellWorkbench = _gameActions.Find<SpellWorkbenchGameAction>();
        if (spellWorkbench is null)
            return SpellWorkbenchStagedLayout.Unavailable(
                SpellWorkbenchActionResultCodes.ContractUnavailable,
                "The shared Spellcraft boundary is unavailable.");
        return spellWorkbench.ReadStagedLayout();
    }

    internal static readonly GameMcpCommandExecutor FeatureExecutor =
        static (command, scope) => scope.Dispatcher.ExecuteFeatureCommand(command, scope);

    private GameMcpCommandResult ExecuteFeatureCommand(
        GameMcpCommand command,
        GameMcpCommandScope scope)
    {
        var registry = scope.Registry;
        var world = scope.World;
        var configuration = scope.Configuration;
        var lifecycle = scope.Lifecycle;
        var service = ServiceForGameMcp(command.Kind);
        var context = CreateGameMcpContext(
            registry,
            service,
            world.Generation,
            out var ordinal);
        var config = configuration.Snapshot;
        var result = ExecuteFeature(
            command,
            config,
            world.Snapshot,
            in context,
            ordinal);
        var purchaseRefusal = GameMcpPurchaseRefusal.ProjectPurchaseRefusal(
            command, world.Snapshot, in result, lifecycle,
            configuration.Generation.Value,
            FindFeature(command.Kind) is AutoBuyFeatureRuntime bought
                ? bought.LastGameMcpSubmission.Diagnosis
                : default);
        if (purchaseRefusal is not null) return purchaseRefusal;
        var exactReason = GameMcpCastProjection.ExactGameMcpReason(command, world.Snapshot, in result);
        GameMcpValue? details = null;
        if (command.Kind == GameMcpCommandKind.Concept &&
            FindFeature(command.Kind) is AutoConceptFeatureRuntime concepts)
        {
            var submission = concepts.LastGameMcpSubmission;
            if (!submission.Outcome.Verified && !string.IsNullOrEmpty(submission.Outcome.Reason))
                exactReason = submission.Outcome.Reason;
            if (submission.MaximumAmount >= 0 &&
                submission.Outcome.Code == AutoConceptActionResultCodes.AmountUnavailable)
            {
                details = new GameMcpObjectBuilder
                {
                    ["maximumAmount"] = submission.MaximumAmount,
                }.Freeze();
            }
        }

        // The purchase and cast boundaries answer with a class the caller can branch on and a
        // sentence carrying the fact the class cannot: which epoch the purchase topology is
        // stamped at, or which spell now occupies the slot the plan named. Neither is derivable
        // from the code, and a response that dropped them left a caller with a number.
        if (command.Kind == GameMcpCommandKind.Purchase &&
            FindFeature(command.Kind) is AutoBuyFeatureRuntime purchases)
        {
            var submission = purchases.LastGameMcpSubmission;
            GameMcpPurchaseRefusal.ProjectPurchaseDetails(
                command, world.Snapshot, in submission, ref exactReason, ref details);
        }
        // One refusal stood for two gates — no level ready, and a level ready you cannot pay
        // for — and a round could not tell which it had met. It spent a mastery listing, two
        // navigations, a refused tooltip, a screenshot and a screen read finding out, while
        // the research verb had answered the same shape of question in eighty-nine bytes two
        // calls earlier. The boundary sees one verdict; the published world holds both halves,
        // so the sentence separating them is composed here.
        if (command.Kind == GameMcpCommandKind.SpellLevel &&
            command.Mode != "all" &&
            result.Code == SpellLevelActionResultCodes.LevelNotAffordable)
        {
            exactReason = GameMcpWorldQuery.MasteryRefusalReason(
                world.Snapshot, command.TargetId);
        }
        if (command.Kind == GameMcpCommandKind.SpellLevel &&
            result.Code == SpellLevelActionResultCodes.LevelAllLocked)
        {
            exactReason = GameMcpWorldQuery.LevelAllLockedReason(world.Snapshot);
        }
        if (command.Kind == GameMcpCommandKind.SpellLevel &&
            FindFeature(command.Kind) is SpellLevelFeatureRuntime levels)
        {
            details = GameMcpSpellLevelProjection.Project(
                levels.LastGameMcpSubmission, world.Snapshot);
        }

        if (command.Kind == GameMcpCommandKind.Cast &&
            FindFeature(command.Kind) is AutoCastFeatureRuntime casts)
        {
            var submission = casts.LastGameMcpSubmission;
            exactReason = GameMcpCastProjection.CastRefusalReason(
                command, world.Snapshot, in result,
                submission.Outcome.Verified, submission.Outcome.Reason);

            // The spell holding the slot rides as an entity, not only inside the sentence. A
            // caller acting on it had nowhere to read its id but a regular expression over
            // prose, because the structured id beside it names the spell that was planned —
            // the one thing this refusal has already established is not there.
            if (submission.Occupant != Guid.Empty)
            {
                details = new GameMcpObjectBuilder
                {
                    ["occupant"] = submission.Occupant.ToString("D"),
                }.Freeze();
            }
        }
        return GameMcpCommandResult.FromAction(
            result.WithReason(exactReason),
            lifecycle,
            configuration.Generation.Value,
            details);
    }

    private sealed class GameMcpActionUnavailableException : Exception
    {
        internal GameMcpActionUnavailableException(string code, string message)
            : base(message)
        {
            Code = string.IsNullOrWhiteSpace(code)
                ? throw new ArgumentException("An MCP rejection code is required.", nameof(code))
                : code;
        }

        internal string Code { get; }
    }

    private ServiceActionResult ExecuteFeature(
        GameMcpCommand command,
        SuiteRuntimeConfiguration config,
        GameWorldState world,
        in ServiceActionContext context,
        int serviceOrdinal)
    {
        var feature = FindFeature(command.Kind);
        switch (command.Kind)
        {
            case GameMcpCommandKind.Purchase:
            {
                var kind = command.Mode == "structure"
                    ? AutoBuyCandidateKind.Structure
                    : AutoBuyCandidateKind.Upgrade;
                var expected = kind == AutoBuyCandidateKind.Structure
                    ? "StructureSO"
                    : "UpgradeSO";
                GameMcpNativeActionAdmission.AssertNativeType(command, expected);
                var action = new AutoBuyCycleAction(
                    kind,
                    command.TargetId,
                    world.CollectedAtEpoch,
                    command.Amount);
                return ((AutoBuyFeatureRuntime)feature).TryExecuteGameMcp(
                    in action, in config, in context);
            }
            case GameMcpCommandKind.Cast:
            {
                GameMcpNativeActionAdmission.AssertNativeType(command, "SpellRecipeSO");
                var kind = command.Mode switch
                {
                    "release" => AutoCastActionKind.ReleaseCharge,
                    "toggle_off" => AutoCastActionKind.ToggleOff,
                    _ => AutoCastActionKind.Fire,
                };
                var slotIndex = checked(command.Amount - 1);
                var chargeHold = string.Equals(
                    command.PayloadValue, "charge", StringComparison.Ordinal);
                // Whether the game charges this spell is asked at the action boundary, of the live
                // spell the position resolves to. Asked here, of a world up to a cadence old, a
                // slot rearranged or emptied in between answered "this spell has no charged cast" —
                // a durable false belief about a capability, for a fact about a position.
                var action = new AutoCastCycleAction(
                    kind,
                    slotIndex,
                    command.TargetId,
                    world.CollectedAtEpoch,
                    chargeHold: chargeHold);
                return ((AutoCastFeatureRuntime)feature).TryExecuteGameMcp(
                    in action, in config, in context);
            }
            case GameMcpCommandKind.Concept:
            {
                GameMcpNativeActionAdmission.AssertNativeType(command, "AlchemyRecipeSO");
                var kind = command.Mode switch
                {
                    "add" => AutoConceptActionKind.Add,
                    "remove" => AutoConceptActionKind.RemoveOwned,
                    "rotate_out" => AutoConceptActionKind.RotateOut,
                    _ => throw new ArgumentException("unsupported concept mode " + command.Mode),
                };
                if (!AutoConceptPlanBeliefProjection.TryCreate(
                        world,
                        command.TargetId,
                        out var belief,
                        out var reason))
                {
                    throw new GameMcpActionUnavailableException(
                        "concept_world_facts_unavailable",
                        reason);
                }
                if (!AutoConceptPlanBeliefProjection.TryResolveGameMcpTarget(
                        kind,
                        command.Amount,
                        in belief,
                        out var targetOrDelta,
                        out var code,
                        out reason))
                {
                    throw new GameMcpActionUnavailableException(
                        code,
                        reason);
                }
                var action = new AutoConceptCycleAction(
                    kind,
                    command.TargetId,
                    targetOrDelta,
                    command.SecondaryId,
                    world.CollectedAtEpoch,
                    in belief);
                return ((AutoConceptFeatureRuntime)feature).TryExecuteGameMcp(
                    in action, in config, in context);
            }
            case GameMcpCommandKind.SpellLevel:
            {
                GameMcpNativeActionAdmission.AssertNativeType(command, "SpellRecipeSO");
                var kind = command.Mode == "all"
                    ? SpellLevelActionKind.All
                    : SpellLevelActionKind.Single;
                var action = new SpellLevelCycleAction(
                    kind,
                    command.TargetId,
                    world.CollectedAtEpoch);
                return ((SpellLevelFeatureRuntime)feature).TryExecuteGameMcp(
                    in action, in config, in context);
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(command.Kind));
        }
    }

    private IAutomataServiceCycleFeatureRuntime FindFeature(GameMcpCommandKind kind)
    {
        for (var index = 0; index < _features.Length; index++)
        {
            var feature = _features[index];
            if (kind == GameMcpCommandKind.Purchase && feature is AutoBuyFeatureRuntime ||
                kind == GameMcpCommandKind.Cast && feature is AutoCastFeatureRuntime ||
                kind == GameMcpCommandKind.Concept && feature is AutoConceptFeatureRuntime ||
                kind == GameMcpCommandKind.Harvest && feature is AutoHarvestFeatureRuntime ||
                kind == GameMcpCommandKind.SpellLevel && feature is SpellLevelFeatureRuntime)
                return feature;
        }
        throw new InvalidOperationException(
            "the requested feature runtime is not registered in this ServiceCycle");
    }

    private ServiceActionContext CreateGameMcpContext(
        OrbModding.Common.Runtime.ServiceCycle.Registration.ServiceCycleRegistry registry,
        ServiceId service,
        WorldGeneration world,
        out int serviceOrdinal)
    {
        serviceOrdinal = -1;
        for (var ordinal = 0; ordinal < registry.OrdinalCount; ordinal++)
        {
            if (registry.GetServiceId(ordinal) != service) continue;
            serviceOrdinal = ordinal;
            break;
        }
        if (serviceOrdinal < 0)
            throw new InvalidOperationException(
                "service " + service.Value + " is not registered");

        var sequence = checked(++_nextGameMcpActionIdentity);
        var identity = new ServiceCycleIdentity(
            service,
            _host.CurrentLifecycle,
            _configurationPublication.ReadLatest().Generation,
            registry.Strategy.ReadLatest().Generation,
            world,
            new CycleId(sequence));
        var coordinates = new ServiceCycleProfileCoordinates(
            serviceOrdinal,
            Math.Max(0, _host.Pump.LastAcceptedFrameIdentity));
        return new ServiceActionContext(
            identity,
            new BatchId(sequence),
            new ActionId(sequence),
            0,
            registry.Clock.Now,
            in coordinates);
    }

    internal static ServiceId ServiceForGameMcp(GameMcpCommandKind kind) => kind switch
    {
        GameMcpCommandKind.Purchase => AutoBuyServicePolicies.ServiceId,
        GameMcpCommandKind.Cast => AutoCastServicePolicies.ServiceId,
        GameMcpCommandKind.Concept => AutoConceptServicePolicies.ServiceId,
        GameMcpCommandKind.Harvest => AutoHarvestServicePolicies.ServiceId,
        GameMcpCommandKind.SpellLevel => SpellLevelServicePolicies.ServiceId,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}
#endif
