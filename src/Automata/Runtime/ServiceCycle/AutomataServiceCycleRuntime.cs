using System;
using System.Collections.Generic;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime;
#if SERVICE_CYCLE_PROFILE
using OrbAutomata.GameMcp;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Profile;
using OrbModding.Common.Runtime.World;
#endif

namespace OrbAutomata;

/// <summary>
/// The one Automata-owned ServiceCycle runtime. It owns the shared host and the
/// ordered feature runtimes, and drives them per Unity frame, per saved-configuration
/// publication, and per lifecycle boundary. It owns no feature policy, native adapter,
/// or typed service generic.
/// </summary>
internal sealed class AutomataServiceCycleRuntime : IAutomataServiceCycleRuntime
{
    private readonly Func<long> _readLifecycleEpoch;
    private readonly AutomataServiceCycleHost _host;
    private readonly IAutomataServiceCycleFeatureRuntime[] _features;
    private readonly ServiceConfigurationPublisher _configurationPublication;
    private ConfigGeneration _configurationGeneration;
    private readonly DiscoveryTreeOfferGameAction? _discoveryTreeOffers;
    private readonly SpellWorkbenchGameAction? _spellWorkbench;
    private readonly SpellCompositionGameAction? _spellComposition;
    private readonly SpellLoadoutGameAction? _spellLoadout;
    private readonly TargetingGameAction? _targeting;
    private readonly GenericDiscoveryGameAction? _genericDiscovery;
    private readonly EquipmentLoadoutGameAction? _equipmentLoadout;
    private readonly AlchemyLoadoutGameAction? _alchemyLoadout;
    private readonly RitualLifecycleGameAction? _ritualLifecycle;
    private readonly GenericLevelGameAction? _genericLevel;
    private readonly CraftingStationGameAction? _craftingStations;
    private readonly CraftingInstanceLifecycleGameAction? _craftingInstances;
    private readonly LoadoutGameAction? _loadouts;
    private readonly HarvestLifecycleGameAction? _harvestLifecycle;
    private readonly PlotLifecycleGameAction? _plotLifecycle;
    private readonly StructureLifecycleGameAction? _structureLifecycle;
    private readonly ReturnToMenuGameAction? _returnToMenu;
    private readonly ChallengeGameAction? _challenges;
    private readonly PrestigeGameAction? _prestige;
    private readonly ResearchGameAction? _research;
    private bool _disposed;
#if SERVICE_CYCLE_PROFILE
    private ulong _nextGameMcpActionIdentity;
#endif

    internal AutomataServiceCycleRuntime(
        Func<long> readLifecycleEpoch,
        ServiceConfigurationPublisher configurationPublication,
        AutomataServiceCycleHost host,
        IAutomataServiceCycleFeatureRuntime[] features,
        ConfigGeneration configurationGeneration,
        DiscoveryTreeOfferGameAction? discoveryTreeOffers = null,
        SpellWorkbenchGameAction? spellWorkbench = null,
        SpellCompositionGameAction? spellComposition = null,
        SpellLoadoutGameAction? spellLoadout = null,
        TargetingGameAction? targeting = null,
        GenericDiscoveryGameAction? genericDiscovery = null,
        EquipmentLoadoutGameAction? equipmentLoadout = null,
        AlchemyLoadoutGameAction? alchemyLoadout = null,
        RitualLifecycleGameAction? ritualLifecycle = null,
        GenericLevelGameAction? genericLevel = null,
        CraftingStationGameAction? craftingStations = null,
        CraftingInstanceLifecycleGameAction? craftingInstances = null,
        LoadoutGameAction? loadouts = null,
        HarvestLifecycleGameAction? harvestLifecycle = null,
        PlotLifecycleGameAction? plotLifecycle = null,
        StructureLifecycleGameAction? structureLifecycle = null,
        ReturnToMenuGameAction? returnToMenu = null,
        ChallengeGameAction? challenges = null,
        PrestigeGameAction? prestige = null,
        ResearchGameAction? research = null)
    {
        _readLifecycleEpoch = readLifecycleEpoch ?? throw new ArgumentNullException(nameof(readLifecycleEpoch));
        _configurationPublication = configurationPublication ??
            throw new ArgumentNullException(nameof(configurationPublication));
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _features = features ?? throw new ArgumentNullException(nameof(features));
        _configurationGeneration = configurationGeneration;
        _discoveryTreeOffers = discoveryTreeOffers;
        _spellWorkbench = spellWorkbench;
        _spellComposition = spellComposition;
        _spellLoadout = spellLoadout;
        _targeting = targeting;
        _genericDiscovery = genericDiscovery;
        _equipmentLoadout = equipmentLoadout;
        _alchemyLoadout = alchemyLoadout;
        _ritualLifecycle = ritualLifecycle;
        _genericLevel = genericLevel;
        _craftingStations = craftingStations;
        _craftingInstances = craftingInstances;
        _loadouts = loadouts;
        _harvestLifecycle = harvestLifecycle;
        _plotLifecycle = plotLifecycle;
        _structureLifecycle = structureLifecycle;
        _returnToMenu = returnToMenu;
        _challenges = challenges;
        _prestige = prestige;
        _research = research;
    }

    internal SuiteRuntimeConfiguration CurrentConfiguration => _configurationPublication.ReadLatest().Snapshot;
    internal ConfigGeneration CurrentConfigurationGeneration =>
        _configurationPublication.ReadLatest().Generation;
    internal LifecycleGeneration CurrentLifecycle => _host.CurrentLifecycle;
    internal bool EmergencyStopEngaged => _host.EmergencyStopEngaged;

    public void Tick(float unscaledDeltaTime)
    {
        if (_disposed) return;
        var report = _host.Tick();
        var pump = _host.Pump;
        for (var index = 0; index < _features.Length; index++)
            _features[index].ObserveFrame(pump, in report);
    }

    public void PublishSavedConfiguration(
        SuiteRuntimeConfiguration configuration,
        ConfigGeneration configurationGeneration)
    {
        if (_disposed) return;
        if (configuration is null) throw new ArgumentNullException(nameof(configuration));
        if (configurationGeneration.Value <= _configurationGeneration.Value) return;
        if (configurationGeneration != _configurationGeneration.Next())
            throw new InvalidOperationException(
                "A saved configuration generation was skipped before ServiceCycle publication.");
        var publishedGeneration = _configurationPublication.Publish(configuration);
        if (publishedGeneration != configurationGeneration)
            throw new InvalidOperationException(
                "The configuration store and ServiceCycle publication generations diverged.");
        _configurationGeneration = configurationGeneration;
        for (var index = 0; index < _features.Length; index++)
            _features[index].ObserveConfiguration(configurationGeneration);
    }

    public void CancelPreparedWork()
    {
        if (_disposed) return;
        if (!_host.EmergencyStopEngaged)
            // This cancellation is recoverable: the caller may be releasing ownership, disabling
            // automation, or synchronously engaging the configured emergency stop. Marking it as a
            // shutdown made the configuration pump correctly refuse to clear it, so RESUME could
            // never restore this runtime. The next published false reading may clear a user episode;
            // Dispose still creates the non-clearable shutdown episode below.
            _host.SetEmergencyStop(true, EmergencyStopReason.UserRequested);
    }

    public void InvalidateLifecycle()
    {
        if (_disposed) return;
        var nativeLifecycle = _readLifecycleEpoch();
        if (!_host.TryReplaceLifecycle(nativeLifecycle)) return;
        for (var index = 0; index < _features.Length; index++)
            _features[index].ObserveLifecycle(
                nativeLifecycle,
                _configurationGeneration);
        _discoveryTreeOffers?.InvalidateLifecycle();
        _spellWorkbench?.InvalidateLifecycle();
        _spellComposition?.InvalidateLifecycle();
        _spellLoadout?.InvalidateLifecycle();
        _targeting?.InvalidateLifecycle();
        _genericDiscovery?.InvalidateLifecycle();
        _equipmentLoadout?.InvalidateLifecycle();
        _alchemyLoadout?.InvalidateLifecycle();
        _ritualLifecycle?.InvalidateLifecycle();
        _genericLevel?.InvalidateLifecycle();
        _craftingStations?.InvalidateLifecycle();
        _craftingInstances?.InvalidateLifecycle();
        _loadouts?.InvalidateLifecycle();
        _harvestLifecycle?.InvalidateLifecycle();
        _plotLifecycle?.InvalidateLifecycle();
        _structureLifecycle?.InvalidateLifecycle();
        _returnToMenu?.InvalidateLifecycle();
        _challenges?.InvalidateLifecycle();
        _prestige?.InvalidateLifecycle();
        _research?.InvalidateLifecycle();
    }

    public AutomataDiagnosticsRuntimeEvidence CaptureDiagnostics()
    {
        if (_disposed)
            return AutomataDiagnosticsRuntimeEvidence.Unavailable(
                "The automation runtime has shut down.");
        return _host.CaptureDiagnostics();
    }

#if SERVICE_CYCLE_PROFILE
    public AutomataRuntimeFrameFacts CaptureFrameFacts(bool includeServices)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(AutomataServiceCycleRuntime));
        var playerCraftingAvailable = false;
        var playerCraftingUnavailableReason =
            "the player crafting action boundary was not composed";
        for (var index = 0; index < _features.Length; index++)
        {
            if (_features[index] is not AutoScribeServiceCycleFeature.Runtime scribe) continue;
            playerCraftingAvailable = scribe.PlayerCraftingBindingsAvailable;
            playerCraftingUnavailableReason = scribe.PlayerCraftingBindingFailure;
            break;
        }
        return AutomataRuntimeFrameFacts.Capture(
            _host,
            _configurationPublication,
            includeServices,
            playerCraftingAvailable,
            playerCraftingUnavailableReason,
            _craftingInstances?.BindingsAvailable == true,
            _craftingInstances?.BindingFailure ??
                "the crafting-instance action boundary was not composed");
    }

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
            if (command.Kind == GameMcpCommandKind.DiscoveryTreeOffer)
                return ExecuteDiscoveryTreeOffer(
                    command,
                    lifecycle,
                    configuration.Generation.Value);
            if (command.Kind == GameMcpCommandKind.SpellWorkbench)
                return ExecuteSpellWorkbench(command, lifecycle, configuration.Generation.Value);
            if (command.Kind == GameMcpCommandKind.SpellComposition)
                return ExecuteSpellComposition(command, lifecycle, configuration.Generation.Value);
            if (command.Kind == GameMcpCommandKind.SpellLoadout)
                return ExecuteSpellLoadout(command, lifecycle, configuration.Generation.Value);
            if (command.Kind == GameMcpCommandKind.Targeting)
                return ExecuteTargeting(command, lifecycle, configuration.Generation.Value);
            if (command.Kind == GameMcpCommandKind.Consumable)
                return ExecuteConsumable(command, lifecycle, configuration.Generation.Value);
            if (command.Kind == GameMcpCommandKind.Crafting)
                return ExecuteCrafting(command, lifecycle, configuration.Generation.Value);
            if (command.Kind == GameMcpCommandKind.GenericDiscovery)
                return ExecuteGenericDiscovery(
                    command,
                    lifecycle,
                    configuration.Generation.Value);
            if (command.Kind == GameMcpCommandKind.EquipmentLoadout)
                return ExecuteEquipmentLoadout(command, lifecycle, configuration.Generation.Value);
            if (command.Kind == GameMcpCommandKind.AlchemyLoadout)
                return ExecuteAlchemyLoadout(command, lifecycle, configuration.Generation.Value);
            if (command.Kind == GameMcpCommandKind.RitualLifecycle)
                return ExecuteRitualLifecycle(command, lifecycle, configuration.Generation.Value);
            if (command.Kind == GameMcpCommandKind.GenericLevel)
                return ExecuteGenericLevel(command, lifecycle, configuration.Generation.Value);
            if (command.Kind == GameMcpCommandKind.CraftingStation)
                return ExecuteCraftingStation(command, lifecycle, configuration.Generation.Value);
            if (command.Kind == GameMcpCommandKind.Loadout)
                return ExecuteLoadout(command, lifecycle, configuration.Generation.Value);
            if (command.Kind == GameMcpCommandKind.HarvestLifecycle)
                return ExecuteHarvestLifecycle(command, lifecycle, configuration.Generation.Value);
            if (command.Kind == GameMcpCommandKind.Harvest)
                return ExecutePlotLifecycle(command, lifecycle, configuration.Generation.Value);
            if (command.Kind == GameMcpCommandKind.StructureLifecycle)
                return ExecuteStructureLifecycle(command, lifecycle, configuration.Generation.Value);
            if (command.Kind == GameMcpCommandKind.ReturnToMenu)
                return ExecuteReturnToMenu(command, lifecycle, configuration.Generation.Value);
            if (command.Kind == GameMcpCommandKind.Challenge)
                return ExecuteChallenge(command, lifecycle, configuration.Generation.Value);
            if (command.Kind == GameMcpCommandKind.Prestige)
                return ExecutePrestige(command, lifecycle, configuration.Generation.Value);
            if (command.Kind == GameMcpCommandKind.Research)
                return ExecuteResearch(
                    command, world.Snapshot, lifecycle, configuration.Generation.Value);
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
            var purchaseRefusal = ProjectPurchaseRefusal(
                command, world.Snapshot, in result, lifecycle,
                configuration.Generation.Value);
            if (purchaseRefusal is not null) return purchaseRefusal;
            var exactReason = ExactGameMcpReason(command, world.Snapshot, in result);
            GameMcpValue? details = null;
            if (command.Kind == GameMcpCommandKind.Concept &&
                FindFeature(command.Kind) is AutoConceptFeatureRuntime concepts)
            {
                var submission = concepts.LastGameMcpSubmission;
                if (!submission.Verified && !string.IsNullOrEmpty(submission.Reason))
                    exactReason = submission.Reason;
                if (submission.MaximumAmount >= 0 &&
                    submission.Preflight == AutoConceptPreflight.ResourceBackpressure)
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
                if (!submission.Verified && !string.IsNullOrEmpty(submission.Reason))
                    exactReason = submission.Reason;

                // A refusal whose sentence names a ceiling carries that ceiling as a number too,
                // read from the same live reading the sentence was written from.
                if (submission.MaximumAmount >= 0)
                {
                    details = new GameMcpObjectBuilder
                    {
                        ["maximumAmount"] = submission.MaximumAmount,
                    }.Freeze();
                }
                else if (submission.Verified)
                {
                    // A purchase is queued, not applied, so the answer is what the press queued and
                    // it is complete here: the count is the mutation's own verified queued-level
                    // delta, and no later world can add to it without contradicting it.
                    details = GameMcpWorldQuery.QueuedMutation(
                        command.TargetId, command.Amount, submission.CommittedLevels);
                }
            }
            if (command.Kind == GameMcpCommandKind.Cast &&
                FindFeature(command.Kind) is AutoCastFeatureRuntime casts)
            {
                var submission = casts.LastGameMcpSubmission;
                if (!submission.Verified && !string.IsNullOrEmpty(submission.Reason))
                    exactReason = submission.Reason;

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
                in result,
                command.Kind,
                lifecycle,
                configuration.Generation.Value,
                exactReason,
                details);
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
                exception.GetBaseException().Message,
                lifecycle,
                configuration.Generation.Value);
        }
    }

    public SpellWorkbenchLoadPreview PreviewSpellWorkbench(
        in SpellWorkbenchLoadPreviewRequest request)
    {
        if (_disposed)
            return SpellWorkbenchLoadPreview.Refused(
                SpellWorkbenchPreflight.ContractUnavailable,
                "The ServiceCycle runtime has been disposed.");
        if (_spellWorkbench is null)
            return SpellWorkbenchLoadPreview.Refused(
                SpellWorkbenchPreflight.ContractUnavailable,
                "The shared spell workbench boundary is unavailable.");
        return _spellWorkbench.Preview(in request);
    }

    public SpellWorkbenchStagedLayout ReadStagedSpellWorkbench()
    {
        if (_disposed)
            return SpellWorkbenchStagedLayout.Unavailable(
                SpellWorkbenchPreflight.ContractUnavailable,
                "The ServiceCycle runtime has been disposed.");
        if (_spellWorkbench is null)
            return SpellWorkbenchStagedLayout.Unavailable(
                SpellWorkbenchPreflight.ContractUnavailable,
                "The shared Spellcraft boundary is unavailable.");
        return _spellWorkbench.ReadStagedLayout();
    }

    private GameMcpCommandResult ExecuteSpellComposition(
        GameMcpCommand command,
        long lifecycle,
        ulong configurationGeneration)
    {
        GameMcpNativeActionAdmission.AssertNativeType(command, "IntVariable");
        if (_spellComposition is null)
            return GameMcpCommandResult.Rejected(
                "contract_unavailable",
                "the shared spell composition GameAction was not composed",
                lifecycle,
                configurationGeneration);
        var dial = command.Mode switch
        {
            "set_output_level" => CastingDial.Output,
            "set_reserve_level" => CastingDial.Reserve,
            _ => (CastingDial?)null,
        };
        if (dial is null)
            return GameMcpCommandResult.Rejected(
                "unsupported_mode",
                "the Casting action boundary accepts only the global Output and Reserve dials",
                lifecycle,
                configurationGeneration);
        var action = new SpellCompositionAction(
            dial.Value,
            command.Amount,
            command.ExpectedLifecycleGeneration);
        var submission = _spellComposition.Submit(in action);
        var result = SpellCompositionActionResultMapper.Map(in submission);
        return GameMcpCommandResult.FromAction(
            in result,
            command.Kind,
            lifecycle,
            configurationGeneration,
            submission.Reason,
            GameMcpSpellCompositionProjection.Project(in submission, command.PayloadKey));
    }

    private GameMcpCommandResult ExecuteSpellLoadout(
        GameMcpCommand command,
        long lifecycle,
        ulong configurationGeneration)
    {
        GameMcpNativeActionAdmission.AssertNativeType(command, "Spell");
        if (_spellLoadout is null)
            return GameMcpCommandResult.Rejected(
                "contract_unavailable",
                "the shared spell loadout GameAction was not composed",
                lifecycle,
                configurationGeneration);
        var kind = command.Mode switch
        {
            "remove" => SpellLoadoutActionKind.Remove,
            "move" => SpellLoadoutActionKind.Move,
            _ => throw new ArgumentException("unsupported spell loadout mode " + command.Mode),
        };
        var action = new SpellLoadoutAction(
            kind,
            command.TargetId,
            command.Amount - 1,
            command.ExpectedLifecycleGeneration);
        var submission = _spellLoadout.Submit(in action);
        var result = SpellLoadoutActionResultMapper.Map(in submission);
        return GameMcpCommandResult.FromAction(
            in result,
            command.Kind,
            lifecycle,
            configurationGeneration,
            submission.Reason,
            GameMcpSpellLoadoutProjection.Project(in submission));
    }

    private GameMcpCommandResult ExecuteTargeting(
        GameMcpCommand command,
        long lifecycle,
        ulong configurationGeneration)
    {
        GameMcpNativeActionAdmission.AssertNativeType(
            command,
            command.Mode == "submit" ? "StructureSO" : "TargetingManager+TargetLink");
        if (_targeting is null)
            return GameMcpCommandResult.Rejected(
                "contract_unavailable", "the shared targeting GameAction was not composed",
                lifecycle, configurationGeneration);
        var kind = command.Mode switch
        {
            "submit" => TargetingActionKind.Submit,
            "randomize" => TargetingActionKind.Randomize,
            "cancel" => TargetingActionKind.Cancel,
            _ => throw new ArgumentException("unsupported targeting mode " + command.Mode),
        };
        var action = new TargetingAction(kind, command.TargetId, command.ExpectedLifecycleGeneration);
        var submission = _targeting.Submit(in action);
        var result = TargetingActionResultMapper.Map(in submission);
        return GameMcpCommandResult.FromAction(
            in result, command.Kind, lifecycle, configurationGeneration,
            submission.Reason, GameMcpTargetingProjection.Project(in submission));
    }

    private GameMcpCommandResult ExecuteConsumable(
        GameMcpCommand command,
        long lifecycle,
        ulong configurationGeneration)
    {
        GameMcpNativeActionAdmission.AssertNativeType(command, "ConsumableSO");
        var feature = FindFeature(command.Kind);
        var kind = command.Mode switch
        {
            "use" => ConsumablePlayerActionKind.Use,
            "cancel" => ConsumablePlayerActionKind.Cancel,
            "discard" => ConsumablePlayerActionKind.Discard,
            "set_randomization" => ConsumablePlayerActionKind.SetRandomization,
            "move" => ConsumablePlayerActionKind.Move,
            _ => throw new ArgumentException("unsupported consumable mode " + command.Mode),
        };
        var list = command.PayloadKey switch
        {
            "inventory" => ConsumablePlayerListKind.Inventory,
            "hotbar" => ConsumablePlayerListKind.Hotbar,
            _ => ConsumablePlayerListKind.None,
        };
        var randomized = string.Equals(
            command.PayloadValue,
            "true",
            StringComparison.Ordinal);
        var action = new ConsumablePlayerAction(
            kind,
            command.TargetId,
            command.ExpectedLifecycleGeneration,
            command.Amount,
            randomized,
            list,
            kind == ConsumablePlayerActionKind.Move ? command.Amount - 1 : -1);
        var submission = ((AutoItemsFeatureRuntime)feature).TryExecuteGameMcp(in action);
        var result = ConsumablePlayerActionResultMapper.Map(in submission);
        return GameMcpCommandResult.FromAction(
            in result,
            command.Kind,
            lifecycle,
            configurationGeneration,
            submission.Reason,
            GameMcpConsumableProjection.Project(in submission));
    }

    private GameMcpCommandResult ExecuteCrafting(
        GameMcpCommand command,
        long lifecycle,
        ulong configurationGeneration)
    {
        GameMcpNativeActionAdmission.AssertNativeType(command, "CraftingRecipeSO");
        if (command.Mode != "craft")
        {
            if (_craftingInstances is null)
                return GameMcpCommandResult.Rejected(
                    "contract_unavailable",
                    "the shared crafting-instance GameAction was not composed",
                    lifecycle,
                    configurationGeneration);
            var kind = command.Mode switch
            {
                "automate" => CraftingInstanceLifecycleActionKind.Automate,
                "cancel_manual" => CraftingInstanceLifecycleActionKind.CancelManual,
                "cancel_automation" => CraftingInstanceLifecycleActionKind.CancelAutomation,
                _ => throw new ArgumentException("unsupported crafting mode " + command.Mode),
            };
            var instanceAction = new CraftingInstanceLifecycleAction(
                kind, command.TargetId, command.ExpectedLifecycleGeneration);
            var instanceSubmission = _craftingInstances.Submit(in instanceAction);
            var instanceResult = CraftingInstanceLifecycleActionResultMapper.Map(
                in instanceSubmission);
            return GameMcpCommandResult.FromAction(
                in instanceResult,
                command.Kind,
                lifecycle,
                configurationGeneration,
                instanceSubmission.Reason,
                GameMcpCraftingProjection.Project(in instanceSubmission));
        }
        var feature = FindFeature(command.Kind);
        var action = new CraftingPlayerAction(
            command.TargetId,
            command.ExpectedLifecycleGeneration);
        var submission = ((AutoScribeServiceCycleFeature.Runtime)feature)
            .TryExecuteGameMcp(in action);
        var result = CraftingPlayerActionResultMapper.Map(in submission);
        return GameMcpCommandResult.FromAction(
            in result,
            command.Kind,
            lifecycle,
            configurationGeneration,
            submission.Reason,
            GameMcpCraftingProjection.Project(in submission));
    }

    private GameMcpCommandResult ExecuteGenericDiscovery(
        GameMcpCommand command,
        long lifecycle,
        ulong configurationGeneration)
    {
        if (_genericDiscovery is null)
            return GameMcpCommandResult.Rejected(
                "contract_unavailable",
                "the shared generic discovery GameAction was not composed",
                lifecycle,
                configurationGeneration);
        GameMcpNativeActionAdmission.AssertNativeType(command, command.DerivedNativeType);
        var components = new GenericDiscoveryComponent[command.UuidCounts.Length];
        for (var index = 0; index < components.Length; index++)
            components[index] = new GenericDiscoveryComponent(
                command.UuidCounts[index].Uuid,
                command.UuidCounts[index].Count);
        var action = new GenericDiscoveryAction(
            command.TargetId,
            command.DerivedNativeType,
            command.PayloadKey,
            components,
            command.ExpectedLifecycleGeneration);
        var submission = _genericDiscovery.Submit(in action);
        var result = GenericDiscoveryActionResultMapper.Map(in submission);
        return GameMcpCommandResult.FromAction(
            in result,
            command.Kind,
            lifecycle,
            configurationGeneration,
            submission.Reason,
            GameMcpGenericDiscoveryProjection.Project(in submission));
    }

    private GameMcpCommandResult ExecuteEquipmentLoadout(
        GameMcpCommand command,
        long lifecycle,
        ulong configurationGeneration)
    {
        if (_equipmentLoadout is null)
            return GameMcpCommandResult.Rejected("contract_unavailable",
                "the shared equipment loadout GameAction was not composed", lifecycle,
                configurationGeneration);
        GameMcpNativeActionAdmission.AssertNativeType(command, "EquipmentSO");
        var kind = command.Mode == "equip"
            ? EquipmentLoadoutActionKind.Equip
            : EquipmentLoadoutActionKind.Unequip;
        var action = new EquipmentLoadoutAction(kind, command.TargetId, command.Amount,
            command.ExpectedLifecycleGeneration);
        var submission = _equipmentLoadout.Submit(in action);
        var result = EquipmentLoadoutActionResultMapper.Map(in submission);
        return GameMcpCommandResult.FromAction(in result, command.Kind, lifecycle,
            configurationGeneration, submission.Reason,
            GameMcpEquipmentLoadoutProjection.Project(in submission));
    }

    private GameMcpCommandResult ExecuteChallenge(
        GameMcpCommand command,
        long lifecycle,
        ulong configurationGeneration)
    {
        if (_challenges is null)
            return GameMcpCommandResult.Rejected("contract_unavailable",
                "the shared challenge GameAction was not composed", lifecycle,
                configurationGeneration);
        if (command.TargetId != Guid.Empty)
            GameMcpNativeActionAdmission.AssertNativeType(command, "ChallengeSO");
        var kind = command.Mode switch
        {
            "select" => ChallengeActionKind.Select,
            "queue" => ChallengeActionKind.Queue,
            "abandon" => ChallengeActionKind.Abandon,
            "reroll" => ChallengeActionKind.Reroll,
            _ => throw new ArgumentException("unsupported challenge mode " + command.Mode),
        };
        var action = new ChallengeAction(kind, command.TargetId, command.SecondaryId,
            command.ExpectedLifecycleGeneration);
        var submission = _challenges.Submit(in action);
        var result = ChallengeActionResultMapper.Map(in submission);
        return GameMcpCommandResult.FromAction(in result, command.Kind, lifecycle,
            configurationGeneration, submission.Reason,
            GameMcpChallengeProjection.Project(in submission));
    }

    private GameMcpCommandResult ExecuteAlchemyLoadout(
        GameMcpCommand command,
        long lifecycle,
        ulong configurationGeneration)
    {
        if (_alchemyLoadout is null)
            return GameMcpCommandResult.Rejected("contract_unavailable",
                "the shared ordinary Alchemy GameAction was not composed", lifecycle,
                configurationGeneration);
        GameMcpNativeActionAdmission.AssertNativeType(command, "AlchemyRecipeSO");
        var kind = command.Mode switch
        {
            "add" => AlchemyLoadoutActionKind.Add,
            "remove" => AlchemyLoadoutActionKind.Remove,
            _ => throw new ArgumentException("unsupported Alchemy mode " + command.Mode),
        };
        var action = new AlchemyLoadoutAction(kind, command.TargetId, command.Amount,
            command.ExpectedLifecycleGeneration);
        var submission = _alchemyLoadout.Submit(in action);
        var result = AlchemyLoadoutActionResultMapper.Map(in submission);
        return GameMcpCommandResult.FromAction(in result, command.Kind, lifecycle,
            configurationGeneration, submission.Reason,
            GameMcpAlchemyLoadoutProjection.Project(in submission));
    }

    private GameMcpCommandResult ExecutePrestige(
        GameMcpCommand command,
        long lifecycle,
        ulong configurationGeneration)
    {
        if (_prestige is null)
            return GameMcpCommandResult.Rejected("contract_unavailable",
                "the shared prestige GameAction was not composed", lifecycle,
                configurationGeneration);
        var action = new PrestigeAction(command.ExpectedLifecycleGeneration);
        var submission = _prestige.Submit(in action);
        var result = PrestigeActionResultMapper.Map(in submission);
        return GameMcpCommandResult.FromAction(in result, command.Kind, lifecycle,
            configurationGeneration, submission.Reason,
            GameMcpPrestigeProjection.Project(in submission));
    }

    private GameMcpCommandResult ExecuteRitualLifecycle(
        GameMcpCommand command,
        long lifecycle,
        ulong configurationGeneration)
    {
        if (_ritualLifecycle is null)
            return GameMcpCommandResult.Rejected("contract_unavailable",
                "the shared Ritual GameAction was not composed", lifecycle,
                configurationGeneration);
        GameMcpNativeActionAdmission.AssertNativeType(command, "RitualSO");
        var kind = command.Mode switch
        {
            "select" => RitualLifecycleActionKind.Select,
            "deselect" => RitualLifecycleActionKind.Deselect,
            "set_level" => RitualLifecycleActionKind.SetLevel,
            "activate" => RitualLifecycleActionKind.Activate,
            "cancel_duration" => RitualLifecycleActionKind.CancelDuration,
            "end" => RitualLifecycleActionKind.EndBattle,
            _ => throw new ArgumentException("unsupported Ritual mode " + command.Mode),
        };
        var action = new RitualLifecycleAction(kind, command.TargetId,
            kind == RitualLifecycleActionKind.SetLevel ? command.Amount - 1 : 0,
            command.ExpectedLifecycleGeneration);
        var submission = _ritualLifecycle.Submit(in action);
        var result = RitualLifecycleActionResultMapper.Map(in submission);
        return GameMcpCommandResult.FromAction(in result, command.Kind, lifecycle,
            configurationGeneration, submission.Reason,
            GameMcpRitualLifecycleProjection.Project(in submission));
    }

    private GameMcpCommandResult ExecuteHarvestLifecycle(
        GameMcpCommand command,
        long lifecycle,
        ulong configurationGeneration)
    {
        if (_harvestLifecycle is null)
            return GameMcpCommandResult.Rejected("contract_unavailable",
                "the shared harvest-list GameAction was not composed", lifecycle,
                configurationGeneration);
        GameMcpNativeActionAdmission.AssertNativeType(command, "HarvestElementSO");
        var kind = command.Mode switch
        {
            "add_element" => HarvestLifecycleActionKind.AddElement,
            "remove_element" => HarvestLifecycleActionKind.RemoveElement,
            "add_element_action" => HarvestLifecycleActionKind.AddAction,
            "remove_element_action" => HarvestLifecycleActionKind.RemoveAction,
            _ => throw new ArgumentException("unsupported Agromancy element mode " + command.Mode),
        };
        var action = new HarvestLifecycleAction(kind, command.TargetId,
            command.SecondaryId, command.Amount, command.ExpectedLifecycleGeneration);
        var submission = _harvestLifecycle.Submit(in action);
        var result = HarvestLifecycleActionResultMapper.Map(in submission);
        return GameMcpCommandResult.FromAction(in result, command.Kind, lifecycle,
            configurationGeneration, submission.Reason,
            GameMcpHarvestLifecycleProjection.Project(in submission));
    }

    private GameMcpCommandResult ExecutePlotLifecycle(
        GameMcpCommand command,
        long lifecycle,
        ulong configurationGeneration)
    {
        if (_plotLifecycle is null)
            return GameMcpCommandResult.Rejected("contract_unavailable",
                "the shared plot-action GameAction was not composed", lifecycle,
                configurationGeneration);
        GameMcpNativeActionAdmission.AssertNativeType(command, "PlotNodeSO");
        var kind = command.Mode switch
        {
            "add_plot_action" => PlotLifecycleActionKind.Add,
            "remove_plot_action" => PlotLifecycleActionKind.Remove,
            _ => throw new ArgumentException("unsupported Agromancy plot mode " + command.Mode),
        };
        var action = new PlotLifecycleAction(kind, command.TargetId,
            command.SecondaryId, command.Amount, command.ExpectedLifecycleGeneration);
        var submission = _plotLifecycle.Submit(in action);
        var result = PlotLifecycleActionResultMapper.Map(in submission);
        GameMcpValue? details = submission.Verified
            ? new GameMcpObjectBuilder
            {
                ["active"] = new GameMcpObjectBuilder
                {
                    ["before"] = submission.BeforeQuantity,
                    ["after"] = submission.AfterQuantity,
                },
            }.Freeze()
            : submission.MaximumAmount >= 0
                ? new GameMcpObjectBuilder
                {
                    ["maximumAmount"] = submission.MaximumAmount,
                }.Freeze()
                : null;
        return GameMcpCommandResult.FromAction(in result, command.Kind, lifecycle,
            configurationGeneration, submission.Reason, details);
    }

    private GameMcpCommandResult ExecuteStructureLifecycle(
        GameMcpCommand command,
        long lifecycle,
        ulong configurationGeneration)
    {
        if (_structureLifecycle is null)
            return GameMcpCommandResult.Rejected("contract_unavailable",
                "the shared structure GameAction was not composed", lifecycle,
                configurationGeneration);
        GameMcpNativeActionAdmission.AssertNativeType(command, "StructureSO");
        var kind = command.Mode switch
        {
            "enable" => StructureLifecycleActionKind.Enable,
            "disable" => StructureLifecycleActionKind.Disable,
            _ => throw new ArgumentException("unsupported structure mode " + command.Mode),
        };
        var action = new StructureLifecycleAction(
            kind, command.TargetId, command.ExpectedLifecycleGeneration);
        var submission = _structureLifecycle.Submit(in action);
        var result = StructureLifecycleActionResultMapper.Map(in submission);
        return GameMcpCommandResult.FromAction(in result, command.Kind, lifecycle,
            configurationGeneration, submission.Reason,
            GameMcpStructureLifecycleProjection.Project(in submission));
    }

    private GameMcpCommandResult ExecuteReturnToMenu(
        GameMcpCommand command,
        long lifecycle,
        ulong configurationGeneration)
    {
        if (_returnToMenu is null)
            return GameMcpCommandResult.Rejected("contract_unavailable",
                "the shared Back to Menu GameAction was not composed", lifecycle,
                configurationGeneration);
        GameMcpNativeActionAdmission.AssertNativeType(command, "UIBackToMenuButton");
        var action = new ReturnToMenuAction(command.ExpectedLifecycleGeneration);
        var submission = _returnToMenu.Submit(in action);
        var result = ReturnToMenuActionResultMapper.Map(in submission);
        var details = submission.Verified
            // Which control the verb pressed, and whether it had to open a panel to reach it, are
            // how the suite drove the UI. The caller asked to be back at the main menu; the scene
            // it landed in is the whole answer.
            ? new GameMcpObjectBuilder { ["scene"] = "Start" }.Freeze()
            : new GameMcpObjectBuilder().Freeze();
        return GameMcpCommandResult.FromAction(in result, command.Kind, lifecycle,
            configurationGeneration, submission.Reason, details);
    }

    private GameMcpCommandResult ExecuteGenericLevel(
        GameMcpCommand command,
        long lifecycle,
        ulong configurationGeneration)
    {
        if (_genericLevel is null)
            return GameMcpCommandResult.Rejected("contract_unavailable",
                "the shared level GameAction was not composed", lifecycle,
                configurationGeneration);
        GameMcpNativeActionAdmission.AssertNativeType(command, command.DerivedNativeType);
        var kind = command.Mode switch
        {
            "purchase" => GenericLevelActionKind.Purchase,
            "bonus" => GenericLevelActionKind.Bonus,
            _ => throw new ArgumentException("unsupported level mode " + command.Mode),
        };
        var action = new GenericLevelAction(kind, command.TargetId,
            command.DerivedNativeType, command.Amount, command.ExpectedLifecycleGeneration);
        var submission = _genericLevel.Submit(in action);
        var result = GenericLevelActionResultMapper.Map(in submission);
        return GameMcpCommandResult.FromAction(in result, command.Kind, lifecycle,
            configurationGeneration, submission.Reason,
            GameMcpGenericLevelProjection.Project(in submission));
    }

    private GameMcpCommandResult ExecuteCraftingStation(
        GameMcpCommand command,
        long lifecycle,
        ulong configurationGeneration)
    {
        if (_craftingStations is null)
            return GameMcpCommandResult.Rejected("contract_unavailable",
                "the shared Brewing Station GameAction was not composed", lifecycle,
                configurationGeneration);
        GameMcpNativeActionAdmission.AssertNativeType(command, "CraftingStructure");
        var kind = command.Mode switch
        {
            "set_ingredient" => CraftingStationActionKind.SetIngredient,
            "set_output" => CraftingStationActionKind.SetOutput,
            "set_level" => CraftingStationActionKind.SetLevel,
            "start" => CraftingStationActionKind.Start,
            "stop" => CraftingStationActionKind.Stop,
            _ => throw new ArgumentException("unsupported Brewing Station mode " + command.Mode),
        };
        var value = kind == CraftingStationActionKind.SetIngredient
            ? command.Amount - 1
            : kind == CraftingStationActionKind.SetLevel ? command.Amount : 0;
        var action = new CraftingStationAction(kind, command.TargetId,
            command.SecondaryId, value, command.ExpectedLifecycleGeneration);
        var submission = _craftingStations.Submit(in action);
        var result = CraftingStationActionResultMapper.Map(in submission);
        return GameMcpCommandResult.FromAction(in result, command.Kind, lifecycle,
            configurationGeneration, submission.Reason,
            GameMcpCraftingStationProjection.Project(in submission));
    }

    private GameMcpCommandResult ExecuteLoadout(
        GameMcpCommand command,
        long lifecycle,
        ulong configurationGeneration)
    {
        if (_loadouts is null)
            return GameMcpCommandResult.Rejected("contract_unavailable",
                "the shared player-loadout GameAction was not composed", lifecycle,
                configurationGeneration);
        GameMcpNativeActionAdmission.AssertNativeType(command, command.DerivedNativeType);
        var kind = command.Mode switch
        {
            "select" => LoadoutActionKind.Select,
            "set_equipment" => LoadoutActionKind.SetEquipmentSection,
            "set_alchemy" => LoadoutActionKind.SetAlchemySection,
            "rename" => LoadoutActionKind.Rename,
            "next_icon" => LoadoutActionKind.NextIcon,
            "next_color" => LoadoutActionKind.NextColor,
            "snapshot_save" => LoadoutActionKind.SnapshotSave,
            "snapshot_load" => LoadoutActionKind.SnapshotLoad,
            "snapshot_clear" => LoadoutActionKind.SnapshotClear,
            _ => throw new ArgumentException("unsupported loadout mode " + command.Mode),
        };
        var enabled = string.Equals(command.PayloadValue, "true", StringComparison.Ordinal);
        var action = new LoadoutAction(kind, command.TargetId, command.Amount - 1,
            enabled, command.PayloadValue, command.ExpectedLifecycleGeneration);
        var submission = _loadouts.Submit(in action);
        var result = LoadoutActionResultMapper.Map(in submission);
        return GameMcpCommandResult.FromAction(in result, command.Kind, lifecycle,
            configurationGeneration, submission.Reason,
            GameMcpLoadoutProjection.Project(in submission));
    }

    private GameMcpCommandResult ExecuteResearch(
        GameMcpCommand command,
        GameWorldState world,
        long lifecycle,
        ulong configurationGeneration)
    {
        GameMcpNativeActionAdmission.AssertNativeType(command, "ResearchSO");
        if (_research is null)
            return GameMcpCommandResult.Rejected("contract_unavailable",
                "the shared research GameAction was not composed", lifecycle,
                configurationGeneration);
        var kind = command.Mode switch
        {
            "develop" => ResearchActionKind.Develop,
            "pause" => ResearchActionKind.Pause,
            "resume" => ResearchActionKind.Resume,
            "cancel" => ResearchActionKind.Cancel,
            "bonus" => ResearchActionKind.Bonus,
            _ => throw new ArgumentException("unsupported research mode " + command.Mode),
        };
        var action = new ResearchAction(kind, command.TargetId, command.Amount,
            command.ExpectedLifecycleGeneration);
        var submission = _research.Submit(in action);
        var result = ResearchActionResultMapper.Map(in submission);

        // The read block and this refusal answer the same gate, so they say the same sentence.
        // The GameAction sees an affordability verdict and no cost rows; the published world holds
        // the rows, so the sentence naming them is composed here, where both surfaces reach it.
        var reason = submission.Preflight == ResearchPreflight.Unaffordable &&
            WorldLookup.TryFind(world.Research, command.TargetId, out var research)
                ? GameMcpWorldQuery.ShortfallReason(world, research.Decision.DevelopmentCosts)
                : submission.Reason;
        return GameMcpCommandResult.FromAction(in result, command.Kind, lifecycle,
            configurationGeneration, reason,
            GameMcpResearchProjection.Project(in submission));
    }

    private GameMcpCommandResult ExecuteSpellWorkbench(
        GameMcpCommand command,
        long lifecycle,
        ulong configurationGeneration)
    {
        GameMcpNativeActionAdmission.AssertNativeType(command, "SpellRecipeSO");
        if (_spellWorkbench is null)
            return GameMcpCommandResult.Rejected(
                "contract_unavailable",
                "the shared spell workbench GameAction was not composed",
                lifecycle,
                configurationGeneration);
        var layout = new SpellWorkbenchGlyphStack[command.UuidCounts.Length];
        for (var index = 0; index < layout.Length; index++)
            layout[index] = new SpellWorkbenchGlyphStack(
                command.UuidCounts[index].Uuid,
                command.UuidCounts[index].Count);
        var fromDiscovery = string.Equals(
            command.SourceOperation?.Request.ToolName,
            "game_discover",
            StringComparison.Ordinal);
        var fromLoadout = string.Equals(
            command.SourceOperation?.Request.ToolName,
            "game_spell_loadout",
            StringComparison.Ordinal);
        var kind = fromDiscovery
            ? SpellWorkbenchActionKind.Discover
            : fromLoadout
                ? SpellWorkbenchActionKind.CreateWithLayout
                : throw new ArgumentException(
                    "spell workbench actions require a visible discovery or loadout surface");
        var action = new SpellWorkbenchAction(
            kind,
            command.TargetId,
            command.ExpectedLifecycleGeneration,
            fromDiscovery ? layout : Array.Empty<SpellWorkbenchGlyphStack>(),
            fromLoadout ? layout : Array.Empty<SpellWorkbenchGlyphStack>());
        var submission = _spellWorkbench.Submit(in action);
        var result = SpellWorkbenchActionResultMapper.Map(in submission);
        return GameMcpCommandResult.FromAction(
            in result,
            command.Kind,
            lifecycle,
            configurationGeneration,
            submission.Reason,
            GameMcpSpellWorkbenchProjection.Project(in submission));
    }

    private GameMcpCommandResult ExecuteDiscoveryTreeOffer(
        GameMcpCommand command,
        long lifecycle,
        ulong configurationGeneration)
    {
        GameMcpNativeActionAdmission.AssertNativeType(command, "DiscoveryTreeSO");
        if (_discoveryTreeOffers is null)
            return GameMcpCommandResult.Rejected(
                "contract_unavailable",
                "the shared Discovery Tree offer GameAction was not composed",
                lifecycle,
                configurationGeneration);
        var kind = command.Mode switch
        {
            "initiate" => DiscoveryTreeOfferActionKind.Initiate,
            "select" => DiscoveryTreeOfferActionKind.Select,
            "confirm" => DiscoveryTreeOfferActionKind.Confirm,
            "reroll" => DiscoveryTreeOfferActionKind.Reroll,
            _ => throw new ArgumentException("unsupported Discovery Tree offer mode " + command.Mode),
        };
        var action = new DiscoveryTreeOfferAction(
            kind,
            command.TargetId,
            command.SecondaryId,
            command.ExpectedLifecycleGeneration);
        var submission = _discoveryTreeOffers.Submit(in action);
        var result = DiscoveryTreeOfferActionResultMapper.Map(in submission);
        return GameMcpCommandResult.FromAction(
            in result,
            command.Kind,
            lifecycle,
            configurationGeneration,
            submission.Reason,
            GameMcpDiscoveryTreeOfferProjection.Project(kind, in submission));
    }

    private static string? ExactGameMcpReason(
        GameMcpCommand command,
        GameWorldState world,
        in ServiceActionResult result)
    {
        if (command.Kind != GameMcpCommandKind.Purchase ||
            !string.Equals(command.Mode, "upgrade", StringComparison.Ordinal) ||
            result.Disposition != ServiceActionDisposition.Skipped ||
            !WorldLookup.TryFind(world.Upgrades, command.TargetId, out var upgrade) ||
            upgrade.Reading.CachedCostLevel == upgrade.Reading.Level)
        {
            return null;
        }

        return "This upgrade's live price is still refreshing; open its game screen and retry.";
    }

    internal static GameMcpCommandResult? ProjectPurchaseRefusal(
        GameMcpCommand command,
        GameWorldState world,
        in ServiceActionResult result,
        long lifecycle,
        ulong configurationGeneration)
    {
        // native_rejected is reserved for a native refusal the published world cannot explain. A
        // rejection whose cause the read side already names answers with that same name.
        if (command.Kind != GameMcpCommandKind.Purchase ||
            result.Disposition is not (ServiceActionDisposition.Skipped or
                ServiceActionDisposition.Rejected))
        {
            return null;
        }
        if (result.Disposition == ServiceActionDisposition.Rejected &&
            result.Code != CommonActionResultCodes.NativeRejected)
        {
            return null;
        }
        if (string.Equals(command.Mode, "upgrade", StringComparison.Ordinal) &&
            WorldLookup.TryFind(world.Upgrades, command.TargetId, out var upgrade) &&
            upgrade.IsExhausted)
        {
            // The player's word for the thing, and only that. This sentence used to spell the
            // target the way a log line does — display name, asset name in brackets, and the whole
            // canonical UUID — inside prose a player reads. The id the caller acts on rides as the
            // response's own `uuid` field, where it can be read without parsing a sentence.
            return GameMcpCommandResult.Rejected(
                "already_maxed",
                EntityIdentityFormatter.PlayerName(command.TargetId, world.EntityIdentities) +
                " is already at its maximum level.",
                lifecycle,
                configurationGeneration);
        }
        if (!WorldPurchaseCostLookup.TryFindRange(
                world.PurchaseCosts, command.TargetId, out var start, out var count))
            return null;
        var shortfalls = new List<(string Resource, BigDouble Needed, BigDouble Held)>();
        for (var index = start; index < start + count; index++)
        {
            var cost = world.PurchaseCosts[index];
            if (!cost.AffordabilityEvaluated || cost.ResourceAffordable) continue;
            var resource = EntityIdentityFormatter.Describe(
                cost.ResourceId, world.EntityIdentities);
            shortfalls.Add((
                resource.HasName ? resource.Name : cost.ResourceId.ToString("D"),
                GameMcpWorldQuery.AdmittedCost(world, in cost),
                GameMcpWorldQuery.SpendableAmount(
                    world, cost.ResourceId, cost.AvailableAmount)));
        }
        if (shortfalls.Count == 0) return null;
        return GameMcpCommandResult.Rejected(
            "unaffordable",
            GameMcpDecisionReason.Shortfall(shortfalls),
            lifecycle,
            configurationGeneration);
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
                    default,
                    chargeHold);
                return ((AutoCastFeatureRuntime)feature).TryExecuteGameMcp(
                    in action, in config, in context);
            }
            case GameMcpCommandKind.Concept:
            {
                GameMcpNativeActionAdmission.AssertNativeType(command, "AlchemyRecipeSO");
                var kind = command.Mode switch
                {
                    "add" => AutoConceptActionKind.Add,
                    "remove_owned" => AutoConceptActionKind.RemoveOwned,
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
            if ((kind == GameMcpCommandKind.Purchase && feature is AutoBuyFeatureRuntime ||
                kind == GameMcpCommandKind.Cast && feature is AutoCastFeatureRuntime ||
                kind == GameMcpCommandKind.Concept && feature is AutoConceptFeatureRuntime ||
                kind == GameMcpCommandKind.Harvest && feature is AutoHarvestFeatureRuntime ||
                kind == GameMcpCommandKind.SpellLevel && feature is SpellLevelFeatureRuntime) ||
                kind == GameMcpCommandKind.Consumable && feature is AutoItemsFeatureRuntime ||
                kind == GameMcpCommandKind.Crafting &&
                    feature is AutoScribeServiceCycleFeature.Runtime)
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

#endif

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            _discoveryTreeOffers?.Dispose();
            _spellWorkbench?.Dispose();
            _spellComposition?.Dispose();
            _spellLoadout?.Dispose();
            _targeting?.Dispose();
            _genericDiscovery?.Dispose();
            _equipmentLoadout?.Dispose();
            _alchemyLoadout?.Dispose();
            _ritualLifecycle?.Dispose();
            _genericLevel?.Dispose();
            _craftingStations?.Dispose();
            _craftingInstances?.Dispose();
            _loadouts?.Dispose();
            _harvestLifecycle?.Dispose();
            _plotLifecycle?.Dispose();
            _structureLifecycle?.Dispose();
            _returnToMenu?.Dispose();
            _challenges?.Dispose();
            _prestige?.Dispose();
            _research?.Dispose();
            if (!_host.EmergencyStopEngaged)
                _host.SetEmergencyStop(true, EmergencyStopReason.SuiteShutdown);
        }
        finally
        {
            try
            {
                for (var index = 0; index < _features.Length; index++)
                    _features[index].DisposeDiagnostics();
            }
            finally
            {
                try
                {
                    _host.Shutdown();
                }
                finally
                {
                    for (var index = 0; index < _features.Length; index++)
                        _features[index].DisposeRegistration();
                }
            }
        }
    }
}
