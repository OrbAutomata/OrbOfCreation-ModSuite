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
                return ExecuteCrafting(
                    command, world.Snapshot, lifecycle, configuration.Generation.Value);
            if (command.Kind == GameMcpCommandKind.EquipmentLoadout)
                return ExecuteEquipmentLoadout(command, lifecycle, configuration.Generation.Value);
            if (command.Kind == GameMcpCommandKind.AlchemyLoadout)
                return ExecuteAlchemyLoadout(command, lifecycle, configuration.Generation.Value);
            if (command.Kind == GameMcpCommandKind.RitualLifecycle)
                return ExecuteRitualLifecycle(command, lifecycle, configuration.Generation.Value);
            if (command.Kind == GameMcpCommandKind.GenericLevel)
                return ExecuteGenericLevel(
                    command, world.Snapshot, lifecycle, configuration.Generation.Value);
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

    private GameMcpCommandResult ExecuteSpellComposition(
        GameMcpCommand command,
        long lifecycle,
        ulong configurationGeneration)
    {
        GameMcpNativeActionAdmission.AssertNativeType(command, "IntVariable");
        var spellComposition = _gameActions.Find<SpellCompositionGameAction>();
        if (spellComposition is null)
            return GameMcpCommandResult.Rejected(
                "contract_unavailable",
                "the shared spell composition GameAction was not composed",
                lifecycle,
                configurationGeneration);
        var dial = command.Mode switch
        {
            "set_output_level" => LevelDial.Output,
            "set_reserve_level" => LevelDial.Reserve,
            "set_druidry_level" => LevelDial.Druidry,
            "set_alchemy_level" => LevelDial.Alchemy,
            _ => (LevelDial?)null,
        };
        if (dial is null)
            return GameMcpCommandResult.Failed(
                "unsupported_control",
                "The suite sent a control this screen has no button for; nothing was applied.",
                lifecycle,
                configurationGeneration);
        var action = new SpellCompositionAction(
            dial.Value,
            command.Amount,
            command.ExpectedLifecycleGeneration);
        var submission = spellComposition.Submit(in action);
        var result = submission.Outcome.ToResult();
        return GameMcpCommandResult.FromAction(
            in result,
            lifecycle,
            configurationGeneration,
            GameMcpSpellCompositionProjection.Project(in submission, command.PayloadKey));
    }

    private GameMcpCommandResult ExecuteSpellLoadout(
        GameMcpCommand command,
        long lifecycle,
        ulong configurationGeneration)
    {
        GameMcpNativeActionAdmission.AssertNativeType(command, "Spell");
        var spellLoadout = _gameActions.Find<SpellLoadoutGameAction>();
        if (spellLoadout is null)
            return GameMcpCommandResult.Rejected(
                "contract_unavailable",
                "the shared spell loadout GameAction was not composed",
                lifecycle,
                configurationGeneration);
        return SubmitSpellLoadout(spellLoadout, command, lifecycle, configurationGeneration);
    }

    internal static GameMcpCommandResult SubmitSpellLoadout(
        SpellLoadoutGameAction spellLoadout,
        GameMcpCommand command,
        long lifecycle,
        ulong configurationGeneration)
    {
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
        var submission = spellLoadout.Submit(in action);
        var result = submission.ToResult();
        return GameMcpCommandResult.FromAction(
            in result,
            lifecycle,
            configurationGeneration,
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
        var targeting = _gameActions.Find<TargetingGameAction>();
        if (targeting is null)
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
        var submission = targeting.Submit(in action);
        var result = submission.Outcome.ToResult();
        return GameMcpCommandResult.FromAction(
            in result, lifecycle, configurationGeneration, GameMcpTargetingProjection.Project(in submission));
    }

    private GameMcpCommandResult ExecuteConsumable(
        GameMcpCommand command,
        long lifecycle,
        ulong configurationGeneration)
    {
        GameMcpNativeActionAdmission.AssertNativeType(command, "ConsumableSO");
        var consumables = _gameActions.Find<AutoItemsConsumablePlayerGameAction>();
        if (consumables is null)
            return GameMcpCommandResult.Rejected(
                "contract_unavailable",
                "the shared consumable player GameAction was not composed",
                lifecycle,
                configurationGeneration);
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
        var submission = consumables.Submit(in action);
        var result = submission.ToResult();
        return GameMcpCommandResult.FromAction(
            in result,
            lifecycle,
            configurationGeneration,
            GameMcpConsumableProjection.Project(in submission));
    }

    private GameMcpCommandResult ExecuteCrafting(
        GameMcpCommand command,
        GameWorldState world,
        long lifecycle,
        ulong configurationGeneration)
    {
        GameMcpNativeActionAdmission.AssertNativeType(command, "CraftingRecipeSO");
        if (command.Mode != "craft")
        {
            var craftingInstances = _gameActions.Find<CraftingInstanceLifecycleGameAction>();
            if (craftingInstances is null)
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
            var instanceSubmission = craftingInstances.Submit(in instanceAction);
            var instanceResult = instanceSubmission.Outcome.ToResult();
            return GameMcpCommandResult.FromAction(
                in instanceResult,
                lifecycle,
                configurationGeneration,
                GameMcpCraftingProjection.Project(in instanceSubmission));
        }
        var crafting = _gameActions.Find<AutoScribeOneShotCraftPlayerGameAction>();
        if (crafting is null)
            return GameMcpCommandResult.Rejected(
                "contract_unavailable",
                "the shared player crafting GameAction was not composed",
                lifecycle,
                configurationGeneration);
        var max = command.PayloadValue == "max";
        return GameMcpCraftingProjection.Press(
            command,
            lifecycle,
            configurationGeneration,
            max,
            () => GameMcpWorldQuery.CraftShortfallReason(world, command.TargetId),
            remaining =>
            {
                var action = new CraftingPlayerAction(
                    command.TargetId,
                    command.ExpectedLifecycleGeneration,
                    remaining,
                    max);
                return crafting.Submit(in action);
            });
    }

    private GameMcpCommandResult ExecuteEquipmentLoadout(
        GameMcpCommand command,
        long lifecycle,
        ulong configurationGeneration)
    {
        var equipmentLoadout = _gameActions.Find<EquipmentLoadoutGameAction>();
        if (equipmentLoadout is null)
            return GameMcpCommandResult.Rejected("contract_unavailable",
                "the shared equipment loadout GameAction was not composed", lifecycle,
                configurationGeneration);
        GameMcpNativeActionAdmission.AssertNativeType(command, "EquipmentSO");
        var kind = command.Mode == "equip"
            ? EquipmentLoadoutActionKind.Equip
            : EquipmentLoadoutActionKind.Unequip;
        var action = new EquipmentLoadoutAction(kind, command.TargetId, command.Amount,
            command.ExpectedLifecycleGeneration);
        var submission = equipmentLoadout.Submit(in action);
        var result = submission.Outcome.ToResult();
        return GameMcpCommandResult.FromAction(in result, lifecycle,
            configurationGeneration,
            GameMcpEquipmentLoadoutProjection.Project(in submission));
    }

    private GameMcpCommandResult ExecuteChallenge(
        GameMcpCommand command,
        long lifecycle,
        ulong configurationGeneration)
    {
        var challenges = _gameActions.Find<ChallengeGameAction>();
        if (challenges is null)
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
        var submission = challenges.Submit(in action);
        var result = submission.Outcome.ToResult();
        return GameMcpCommandResult.FromAction(in result, lifecycle,
            configurationGeneration,
            GameMcpChallengeProjection.Project(in submission));
    }

    private GameMcpCommandResult ExecuteAlchemyLoadout(
        GameMcpCommand command,
        long lifecycle,
        ulong configurationGeneration)
    {
        var alchemyLoadout = _gameActions.Find<AlchemyLoadoutGameAction>();
        if (alchemyLoadout is null)
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
        var submission = alchemyLoadout.Submit(in action);
        var result = submission.Outcome.ToResult();
        return GameMcpCommandResult.FromAction(in result, lifecycle,
            configurationGeneration,
            GameMcpAlchemyLoadoutProjection.Project(in submission));
    }

    private GameMcpCommandResult ExecutePrestige(
        GameMcpCommand command,
        long lifecycle,
        ulong configurationGeneration)
    {
        var prestige = _gameActions.Find<PrestigeGameAction>();
        if (prestige is null)
            return GameMcpCommandResult.Rejected("contract_unavailable",
                "the shared prestige GameAction was not composed", lifecycle,
                configurationGeneration);
        var action = new PrestigeAction(command.ExpectedLifecycleGeneration);
        var submission = prestige.Submit(in action);
        var result = submission.ToResult();
        return GameMcpCommandResult.FromAction(in result, lifecycle,
            configurationGeneration,
            GameMcpPrestigeProjection.Project(in submission));
    }

    private GameMcpCommandResult ExecuteRitualLifecycle(
        GameMcpCommand command,
        long lifecycle,
        ulong configurationGeneration)
    {
        var ritualLifecycle = _gameActions.Find<RitualLifecycleGameAction>();
        if (ritualLifecycle is null)
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
        var submission = ritualLifecycle.Submit(in action);
        var result = submission.Outcome.ToResult();
        return GameMcpCommandResult.FromAction(in result, lifecycle,
            configurationGeneration,
            GameMcpRitualLifecycleProjection.Project(in submission));
    }

    private GameMcpCommandResult ExecuteHarvestLifecycle(
        GameMcpCommand command,
        long lifecycle,
        ulong configurationGeneration)
    {
        var harvestLifecycle = _gameActions.Find<HarvestLifecycleGameAction>();
        if (harvestLifecycle is null)
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
        var submission = harvestLifecycle.Submit(in action);
        var result = submission.Outcome.ToResult();
        return GameMcpCommandResult.FromAction(in result, lifecycle,
            configurationGeneration,
            GameMcpHarvestLifecycleProjection.Project(in submission));
    }

    private GameMcpCommandResult ExecutePlotLifecycle(
        GameMcpCommand command,
        long lifecycle,
        ulong configurationGeneration)
    {
        var plotLifecycle = _gameActions.Find<PlotLifecycleGameAction>();
        if (plotLifecycle is null)
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
        var submission = plotLifecycle.Submit(in action);
        var result = submission.Outcome.ToResult();
        GameMcpValue? details = submission.Outcome.Verified
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
        return GameMcpCommandResult.FromAction(in result, lifecycle,
            configurationGeneration, details);
    }

    private GameMcpCommandResult ExecuteStructureLifecycle(
        GameMcpCommand command,
        long lifecycle,
        ulong configurationGeneration)
    {
        var structureLifecycle = _gameActions.Find<StructureLifecycleGameAction>();
        if (structureLifecycle is null)
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
        var submission = structureLifecycle.Submit(in action);
        var result = submission.ToResult();
        return GameMcpCommandResult.FromAction(in result, lifecycle,
            configurationGeneration,
            GameMcpStructureLifecycleProjection.Project(in submission));
    }

    private GameMcpCommandResult ExecuteReturnToMenu(
        GameMcpCommand command,
        long lifecycle,
        ulong configurationGeneration)
    {
        var returnToMenu = _gameActions.Find<ReturnToMenuGameAction>();
        if (returnToMenu is null)
            return GameMcpCommandResult.Rejected("contract_unavailable",
                "the shared Back to Menu GameAction was not composed", lifecycle,
                configurationGeneration);
        GameMcpNativeActionAdmission.AssertNativeType(command, "UIBackToMenuButton");
        var action = new ReturnToMenuAction(command.ExpectedLifecycleGeneration);
        var submission = returnToMenu.Submit(in action);
        var result = submission.Outcome.ToResult();
        var details = submission.Outcome.Verified
            // Which control the verb pressed, and whether it had to open a panel to reach it, are
            // how the suite drove the UI. The caller asked to be back at the main menu; the scene
            // it landed in is the whole answer.
            ? new GameMcpObjectBuilder { ["scene"] = "Start" }.Freeze()
            : new GameMcpObjectBuilder().Freeze();
        return GameMcpCommandResult.FromAction(in result, lifecycle,
            configurationGeneration, details);
    }

    private GameMcpCommandResult ExecuteGenericLevel(
        GameMcpCommand command,
        GameWorldState world,
        long lifecycle,
        ulong configurationGeneration)
    {
        var genericLevel = _gameActions.Find<GenericLevelGameAction>();
        if (genericLevel is null)
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
        var submission = genericLevel.Submit(in action);
        var result = submission.Outcome.ToResult();
        return GameMcpCommandResult.FromAction(in result, lifecycle,
            configurationGeneration,
            GameMcpGenericLevelProjection.Project(in submission, world));
    }

    private GameMcpCommandResult ExecuteLoadout(
        GameMcpCommand command,
        long lifecycle,
        ulong configurationGeneration)
    {
        var loadouts = _gameActions.Find<LoadoutGameAction>();
        if (loadouts is null)
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
        var submission = loadouts.Submit(in action);
        var result = submission.Outcome.ToResult();
        return GameMcpCommandResult.FromAction(in result, lifecycle,
            configurationGeneration,
            GameMcpLoadoutProjection.Project(in submission));
    }

    private GameMcpCommandResult ExecuteResearch(
        GameMcpCommand command,
        GameWorldState world,
        long lifecycle,
        ulong configurationGeneration)
    {
        GameMcpNativeActionAdmission.AssertNativeType(command, "ResearchSO");
        var researchAction = _gameActions.Find<ResearchGameAction>();
        if (researchAction is null)
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
        var submission = researchAction.Submit(in action);
        var result = submission.Outcome.ToResult();

        // The read block and this refusal answer the same gate, so they say the same sentence.
        // The GameAction sees an affordability verdict and no cost rows; the published world holds
        // the rows, so the sentence naming them is composed here, where both surfaces reach it.
        var reason =
            submission.Outcome.Code == ResearchActionResultCodes.Unaffordable &&
            WorldLookup.TryFind(world.Research, command.TargetId, out var research)
                ? GameMcpWorldQuery.ShortfallReason(world, research.Decision.DevelopmentCosts)
                : submission.Outcome.Code == ResearchActionResultCodes.Locked
                    ? GameMcpEntityExplainer.LockedResearchReason(world, command.TargetId)
                    : submission.Outcome.Reason;
        return GameMcpCommandResult.FromAction(result.WithReason(reason), lifecycle,
            configurationGeneration,
            GameMcpResearchProjection.Project(in submission));
    }

    private GameMcpCommandResult ExecuteSpellWorkbench(
        GameMcpCommand command,
        long lifecycle,
        ulong configurationGeneration)
    {
        GameMcpNativeActionAdmission.AssertNativeType(command, "SpellRecipeSO");
        var spellWorkbench = _gameActions.Find<SpellWorkbenchGameAction>();
        if (spellWorkbench is null)
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
        var action = new SpellWorkbenchAction(
            command.TargetId,
            command.ExpectedLifecycleGeneration,
            layout);
        var submission = spellWorkbench.Submit(in action);
        var result = submission.ToResult();
        return GameMcpCommandResult.FromAction(
            in result,
            lifecycle,
            configurationGeneration,
            GameMcpSpellWorkbenchProjection.Project(in submission));
    }

    private GameMcpCommandResult ExecuteDiscoveryTreeOffer(
        GameMcpCommand command,
        long lifecycle,
        ulong configurationGeneration)
    {
        GameMcpNativeActionAdmission.AssertNativeType(command, "DiscoveryTreeSO");
        var discoveryTreeOffers = _gameActions.Find<DiscoveryTreeOfferGameAction>();
        if (discoveryTreeOffers is null)
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
        var submission = discoveryTreeOffers.Submit(in action);
        var result = submission.Outcome.ToResult();
        return GameMcpCommandResult.FromAction(
            in result,
            lifecycle,
            configurationGeneration,
            GameMcpDiscoveryTreeOfferProjection.Project(kind, in submission));
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
