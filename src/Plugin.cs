using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using OrbAutomata;
#if SERVICE_CYCLE_PROFILE
using OrbAutomata.GameMcp;
using TMPro;
using UnityEngine.UI;
#endif
using OrbMentor;
using OrbModConfig;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Diagnostics;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal.Outcomes;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal.Status;
using OrbModding.Common.Runtime.World;
#if SERVICE_CYCLE_PROFILE
using OrbModding.Common.Runtime.ServiceCycle.Observation.Profile.Control;
#endif
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OrbModding;

/// <summary>
/// The suite's single BepInEx entry point. One DLL, one loader identity, one configuration file:
/// automation, mastery catch-up and the configuration browser share one lifecycle. On an unknown
/// complete game build, only the browser and verifier load until the exact pair is acknowledged.
/// </summary>
[BepInPlugin(PluginIds.SuiteGuid, PluginIds.SuiteName, PluginIds.Version)]
public sealed class Plugin : BaseUnityPlugin
{
    private const int StartStatusFailureLogFrameThreshold = 120;
#if SERVICE_CYCLE_PROFILE
    private const bool AutoStartServiceCycleDiagnostics = true;
    private GameMcpFrameInbox? _gameMcpOperations;
    private GameMcpHttpServer? _gameMcpServer;
    private GameMcpWritableSettingDescriptor[] _gameMcpWritableConfiguration =
        Array.Empty<GameMcpWritableSettingDescriptor>();
    private GameMcpTooltipNativeAccess? _gameMcpTooltipNativeAccess;
    private ModalDismissGameAction? _modalDismissGameAction;
    private ModalOpenGameAction? _modalOpenGameAction;
    private string _gameMcpTooltipContractFailure =
        "tooltip native layout has not been bound";

    /// <summary>
    /// What a caller is told when the suite cannot read the game's tooltips. The binding layer's
    /// own account of which accessor was missing goes to the log, where it is actionable; on the
    /// wire it named types no caller can look up and gave them nothing to do.
    /// </summary>
    private const string TooltipsUnreadable =
        "The suite could not attach to the game's tooltips in this run, so no tooltip can be " +
        "read until the run restarts.";
    private string _gameMcpAgentSettingsFailure = string.Empty;
#else
    private const bool AutoStartServiceCycleDiagnostics = false;
#endif
    private const float UiRetryIntervalSeconds = 5.0f;
    private const float UiIntegrityIntervalSeconds = 5.0f;

    /// <summary>
    /// Every patch class this plugin installs, named one by one. Scanning an assembly for
    /// <see cref="HarmonyPatch"/> classes would silently adopt whatever else ends up compiled
    /// alongside it — which is the whole suite now that it ships as one DLL. An explicit list can
    /// only widen in a diff.
    /// </summary>
    internal static readonly Type[] HarmonyPatchTypes =
    {
        typeof(SpellFirePatch),
        typeof(MentorSpellMasteryPatch),
        typeof(MentorAlchemyMasteryPatch),
        typeof(MentorArtifactTickPatch),
        typeof(MentorArtifactExperiencePatch),
    };

    /// <summary>
    /// The native transitions the whole suite watches: a save loading, the game initialising, a
    /// runtime reset, a New Game+. They are what moves <see cref="GameLifecycleMonitor"/>'s
    /// generation, and with it the collected epoch that the world collector's structural-fact skip
    /// and Auto Buy's boundary check compare against — an unobserved save-load leaves both comparing
    /// stale to stale. They are installed from <see cref="ComposeAutomata"/> rather than beside
    /// Mentor's optional hooks, where they used to sit, for the reason W56 already gives for the
    /// completion postfix: that composition returns early when Mentor's own mastery hook is
    /// unavailable, and a blocked Mentor must not take the suite's lifecycle observation down with
    /// it. Named here rather than written inline so losing one is a diff rather than an omission.
    /// </summary>
    internal static readonly (string Target, string Handler, bool Postfix)[] LifecycleObservationHooks =
    {
        ("SaveStateManager:ImplementLoadedJson", nameof(BeforeSaveLoad), false),
        ("SaveStateManager:ImplementLoadedJson", nameof(AfterSaveLoaded), true),
        ("GameManager:InitGame", nameof(AfterGameInitialized), true),
        ("GameManager:ResetGameState", nameof(BeforeRuntimeReset), false),
        ("PersistentResetManager:PersistentResetLogic", nameof(BeforePersistentReset), false),
    };

    private Harmony? _harmony;
    private bool _observeGameTickFaults;
    private StuckGameMonitor? _stuckGame;

    private BepInExAutomataConfiguration? _automataConfig;
    private AutomataConfigurationStore? _configurationStore;
    private string? _shortcutAuditSignature;
    private string _auditedBaselineId = string.Empty;
    private AutomataFeatureStatuses? _featureStatuses;
    private readonly SpellLevelCapabilityState _spellLevelCapability = new();

    // Held by the plugin rather than by the feature because the Harmony patch that feeds it outlives
    // any one registration: the hook is installed once and the service is registered per lifecycle.
    private readonly AutoCastManualPauseState _autoCastManualPause = new();
    private readonly MentorMasteryEventJournal _mentorMasteryJournal = new();
    private AutomataActionFamilyOwnership? _automataActionFamilyOwnership;
    private AutomataServiceCycleActivation? _serviceCycleActivation;
    private readonly AutomataWorldDemand _worldDemand = new();
    private AutomationFeatureControlRegistry? _automationFeatureControls;
    private QuickControlColumn? _quickControls;
    private EmergencyStopControl? _emergencyStopControl;
    private AutomataDifferentialVerificationControl? _mathVerification;
    private float _quickControlsUiRetrySeconds;
    private string _quickControlsUiFailureReason = string.Empty;
    private readonly UiInstallationRetryState _quickControlsRetry = new();
    private bool _knownOwnershipWarningLogged;
    private bool _nativeContractsAvailable = true;
    private bool _auditedBuild;
    private bool _buildCompatibilityRuntimeAllowed;
    private bool _runtimeActivationAllowed;
    private string _observedBuildFingerprint = string.Empty;
    private bool _runtimeComposed;
    private bool _runtimeCompositionAttempted;

    private MentorConfig? _mentorConfig;
    private MentorActionFamilyOwnership? _mentorActionFamilyOwnership;

    private ModConfigSettings? _modConfigSettings;
    private float _uiRetrySeconds;
    private float _uiIntegritySeconds;
    private readonly UiInstallationRetryState _modsUiRetry = new();
    private bool _uiMaintenanceDue;
    private bool _uiIntegrityDue;
    private bool _uiStartupReadinessScheduled;
    private readonly UiStartupReadinessGate _uiStartupReadiness = new();
    private int _uiSceneEpoch;
    private int _deferInstallUntilFrame;
    private ModConfigUiShell? _uiShell;
    private ConfigCatalogSnapshot? _catalog;
    private ConfigCatalogGeneration _catalogGeneration;
    private ModConfigNavigationBookmark _catalogNavigation = ModConfigNavigationBookmark.Runtime;
    private ModConfigFrameWork? _uiWork;
    private ModConfigRuntimeSources? _runtimeSources;
    private ModConfigFeatureCommands? _modConfigFeatureCommands;
    private SuiteUiSurfaceDiagnostics? _uiSurfaceDiagnostics;
    private DiagnosticsBundleController? _diagnosticsBundleController;
    private AutomaticSaveBackupHealth? _automaticSaveBackupHealth;
    private Action? _runUiMaintenance;

    private long _lifecycleGeneration;
    private GameLifecycleLease _lifecycleLease;
    private ModConfigStartStatusView? _startStatusView;
    private string _startStatusFailure = string.Empty;
    private int _startStatusFailureFrames;
#if SERVICE_CYCLE_PROFILE
    private int _processId;
#endif
    private string _controlPlaneFailure = string.Empty;
    private AutomaticSaveBackupStatus _automaticSaveBackup = AutomaticSaveBackupStatus.NotRun;

    internal static Plugin? Instance { get; private set; }

    internal static ManualLogSource Log { get; private set; } = null!;

    private void Awake()
    {
        Instance = this;
        Log = Logger;
        EntityIdentityFormatter.ConfigureDiagnostics(
            message => Logger.LogWarning(message),
            message => Logger.LogError(message));
        GameActionFaultLog.ConfigureLog(message => Logger.LogError(message));
        GameLifecycleMonitor.Shared.ConfigureFailureLog(message => Logger.LogError(message));
        NativeViewAdapter.ConfigureFailureLog(message => Logger.LogWarning(message));
        SteamKeyboardBridge.ConfigureFailureLog(message => Logger.LogWarning(message));
        NativeMultiBuyScope.ConfigureFailureLog(message => Logger.LogAutomataError(message));
        EntityIdentityCatalog.Shared.Reset(GameLifecycleMonitor.Shared.Current.Generation);

        RunAutomaticSaveBackup();

        // The read-only save backup above is the first startup gate. The assembly audit is next,
        // still before configuration, Harmony, lifecycle subscriptions, or feature composition. An
        // incomplete audit refuses everything; a complete unknown pair may load only the control
        // plane until the exact pair is explicitly accepted.
        var loadDecision = SuiteLoadGate.Evaluate(Paths.GameRootPath);
        if (!loadDecision.CanLoadControlPlane)
        {
            _controlPlaneFailure = loadDecision.Message;
            Logger.LogError(loadDecision.Message);
            return;
        }

        var legacyObservability = AutomataLegacyObservabilityCleanup.Run(Paths.ConfigPath);
        if (legacyObservability.ShouldLog)
        {
            if (legacyObservability.HasWarnings)
                Logger.LogWarning(legacyObservability.Describe());
            else
                Logger.LogInfo(legacyObservability.Describe());
        }

        var configuration = SuiteConfiguration.TryBind(Config);
        if (!configuration.Success)
        {
            _controlPlaneFailure = configuration.Status.Reason;
            Logger.LogError(configuration.FailureCause.Length == 0
                ? configuration.Status.Reason
                : configuration.Status.Reason + " Cause: " + configuration.FailureCause);
            return;
        }
        var suite = configuration.Config!;
        _automataConfig = suite.Automata;
        _mentorConfig = suite.Mentor;
        _modConfigSettings = suite.ModConfig;
        _observeGameTickFaults = suite.ObserveGameTickFaults.Value;
        _auditedBuild = loadDecision.ShouldLoad;
        _auditedBaselineId = loadDecision.BaselineId;
        _observedBuildFingerprint = loadDecision.ObservedBuildFingerprint;
        var compatibility = UnverifiedBuildCompatibilityPolicy.AtStartup(
            _auditedBuild,
            _observedBuildFingerprint,
            _automataConfig.AllowUnverifiedGameBuild.Value,
            _automataConfig.AcceptedUnverifiedBuildFingerprint.Value);
        if (compatibility.ResetOverride)
            _automataConfig.SetAllowUnverifiedGameBuild(false);
        if (compatibility.EngageEmergencyStop)
            _automataConfig.SetEmergencyStop(true);
        _buildCompatibilityRuntimeAllowed = compatibility.RuntimeAllowed;
        _runtimeActivationAllowed = SuiteStartupAdmission.AllowsRuntime(
            _buildCompatibilityRuntimeAllowed,
            _automaticSaveBackup);
        _nativeContractsAvailable = _runtimeActivationAllowed;
        foreach (var diagnostic in configuration.Diagnostics)
            Logger.LogInfo($"Configuration migration {diagnostic.Kind}: {diagnostic.Source}; {diagnostic.Detail}");

        _lifecycleGeneration = GameLifecycleMonitor.Shared.Current.Generation;
        _configurationStore = new AutomataConfigurationStore(
            _automataConfig,
            PublishConfiguration);
        _featureStatuses = new AutomataFeatureStatuses(
            _configurationStore.Current,
            _lifecycleGeneration,
            configurationGeneration: _configurationStore.CurrentGeneration);
        _mathVerification = new AutomataDifferentialVerificationControl(
            message => Log.LogAutomataInfo(message));
        _emergencyStopControl = new EmergencyStopControl(
            _configurationStore,
            OnEmergencyStopChanged);
        _automationFeatureControls = AutomationFeatureControlRegistry.Create(
            _configurationStore,
            _featureStatuses,
            _spellLevelCapability,
            _mentorConfig);
        ValidateSuiteShortcuts();

        if (_auditedBuild)
            Log.LogAutomataInfo(loadDecision.Message);
        else
        {
            Log.LogAutomataWarning(loadDecision.Message);
            if (_buildCompatibilityRuntimeAllowed)
            {
                Log.LogAutomataWarning(_automaticSaveBackup.AllowsAutomation
                    ? "A persisted acknowledgement matches this exact unverified assembly pair. Runtime composition is permitted at the player's own risk."
                    : "A persisted acknowledgement matches this exact unverified assembly pair, but automatic save-backup failure still blocks runtime composition.");
            }
        }
        GameLifecycleMonitor.Shared.Transitioned += OnLifecycleTransition;
        SceneManager.activeSceneChanged += OnActiveSceneChanged;
        SceneManager.sceneLoaded += OnSceneLoaded;
        ObserveLifecycle(GameLifecycleTransitionKind.SceneEntered, SceneManager.GetActiveScene().name);
        _lifecycleLease = GameLifecycleMonitor.Shared.CaptureLease();

        ComposeDiagnosticsBundle();
        ComposeModConfig();
        if (GameMcpActionRegistrationPolicy.ShouldCompose(_runtimeActivationAllowed))
        {
            // The shared runtime also owns the player's MCP GameActions. Automation policy still
            // honors General/Enabled and every feature mode, but those settings must not remove
            // the game's manual action boundary from the MCP surface.
            EnsureRuntimeComposition();
        }
        else if (!_runtimeActivationAllowed && _automaticSaveBackup.AllowsAutomation)
        {
            Log.LogAutomataWarning(
                "Compatibility emergency stop is active. Press Resume all in Mods > General or the top-left STOP control to accept and resume, or use Advanced to accept while keeping STOP engaged.");
        }
#if SERVICE_CYCLE_PROFILE
        if (!GameMcpTooltipNativeAccess.TryCreate(
                typeof(HoverTooltip),
                typeof(Spell),
                typeof(PassiveAbility),
                typeof(PlotNodeActionInstance),
                out _gameMcpTooltipNativeAccess,
                out _gameMcpTooltipContractFailure))
        {
            Logger.LogWarning(
                "Game MCP tooltip inspection is unavailable: " +
                _gameMcpTooltipContractFailure);
        }
        _gameMcpWritableConfiguration = _automataConfig.CreateGameMcpWritableSchema();
        _modalDismissGameAction = new ModalDismissGameAction(() => _lifecycleGeneration);
        _modalOpenGameAction = new ModalOpenGameAction();
        _gameMcpOperations = new GameMcpFrameInbox();
        _gameMcpServer = GameMcpHttpServer.TryStart(
            _gameMcpOperations,
            message => Logger.LogInfo(message),
            message => Logger.LogError(message));
#endif
    }

    private void RunAutomaticSaveBackup()
    {
        try
        {
            var stampPath = AutomaticSaveBackupPathPolicy.ResolveStampPath(
                Config.ConfigFilePath,
                Paths.ConfigPath);
            _automaticSaveBackup = AutomaticSaveBackup.Run(
                PluginIds.ReleaseVersion,
                Application.persistentDataPath,
                stampPath,
                DateTime.UtcNow);
        }
        catch (Exception exception) when (!ExceptionDetail.IsProcessFatal(exception))
        {
            _automaticSaveBackup = AutomaticSaveBackupStatus.Failed(
                AutomaticSaveBackupTrigger.FreshInstall,
                exception.GetBaseException().Message);
        }

        _automaticSaveBackupHealth = new AutomaticSaveBackupHealth(
            _automaticSaveBackup,
            FeatureStatusRegistry.Shared,
            RuntimeDiagnosticsRegistry.Shared,
            GameLifecycleMonitor.Shared.Current.Generation);
        if (!_automaticSaveBackup.AllowsAutomation)
        {
            Logger.LogError(AutomaticSaveBackupWording.BlockingReason(_automaticSaveBackup));
            return;
        }

        Logger.LogInfo(
            "Automatic save backup " +
            (_automaticSaveBackup.BackupCreated ? "created" : "ready") +
            ": " +
            _automaticSaveBackup.BackupPath +
            " (" +
            _automaticSaveBackup.FileCount +
            " files)." +
            (_automaticSaveBackup.BackupCreated
                ? " Trigger: " + _automaticSaveBackup.Trigger + "."
                : string.Empty));
        if (_automaticSaveBackup.PrunedBackupCount > 0)
        {
            Logger.LogInfo(
                "Automatic save-backup retention pruned " +
                _automaticSaveBackup.PrunedBackupCount +
                " owned backup directories.");
        }
        foreach (var failure in _automaticSaveBackup.RetentionFailures)
            Logger.LogWarning("Automatic save-backup retention warning: " + failure);
    }

    private void EnsureRuntimeComposition()
    {
        if (_runtimeComposed) return;
        _runtimeCompositionAttempted = true;
        _harmony = new Harmony(PluginIds.SuiteGuid);
        MentorMasteryPatchBridge.Install(
            _mentorMasteryJournal,
            () => GameLifecycleMonitor.Shared.Current.Generation,
            message => Logger.LogError(message));
        foreach (var patchType in HarmonyPatchTypes)
            _harmony.CreateClassProcessor(patchType).Patch();
        ComposeGameTickFaultObservation();
        ComposeAutomata();
        _runtimeComposed = true;
    }

    private void ComposeGameTickFaultObservation()
    {
        if (!_observeGameTickFaults)
        {
            Log.LogAutomataInfo("Game tick fault observation is off (Diagnostics.ObserveGameTickFaults).");
            return;
        }
        foreach (var failure in GameTickFaultHooks.Install(_harmony!))
            Log.LogAutomataWarning("Game tick fault observation is unavailable for " + failure);
        _stuckGame = new StuckGameMonitor(
            GameTickFaultSubject.Describe,
            message => Log.LogAutomataInfo(message),
            message => Log.LogAutomataError(message),
            (faults, written, failed) =>
            {
                if (_diagnosticsBundleController is null)
                    failed("bug reports are unavailable in this run");
                else
                    _diagnosticsBundleController.RequestAutomatic(faults, written, failed);
            });
    }

    private void ComposeAutomata()
    {
        var featureStatuses = _featureStatuses!;

        var ownership = _automataActionFamilyOwnership = new AutomataActionFamilyOwnership();
        var harvestFamily = ownership.Family(AutomataActionFamilyOwnership.Harvest);
        var itemsFamily = ownership.Family(AutomataActionFamilyOwnership.Items);
        var scribeFamily = ownership.Family(AutomataActionFamilyOwnership.Scribe);
        _mentorActionFamilyOwnership = new MentorActionFamilyOwnership();
        Log.LogAutomataWarning(
            "Action-family ownership is best-effort: exact known conflicts and cooperative suite owners are isolated, but unknown plugins that invoke native actions without registering cannot be proven absent and are not disabled.");
        _automataActionFamilyOwnership.RefreshLoadedPluginInventory(
            Chainloader.PluginInfos.Count,
            guid => Chainloader.PluginInfos.ContainsKey(guid));
        Func<long> readLifecycleEpoch =
            () => GameLifecycleMonitor.Shared.Current.Generation;
        var registryResolver = TypedRegistryResolver.Shared;
        _serviceCycleActivation = new AutomataServiceCycleActivation(
            IsLifecycleReady,
            (configuration, configurationGeneration) =>
            {
                // One frame counter shared by every feature below. Resolving it per feature
                // would let two services disagree about what frame it is — a wiring mistake
                // that compiles and looks like a quiet game. The world publication is not
                // here at all: the registry owns it, because there is one game.
                Func<long> readFrameIdentity = static () => Time.frameCount;
                var scribeCatalog = new AutoScribeIdentityCatalog();
                IAutomataServiceCycleFeature autoScribeFeature =
                    scribeCatalog.TryGetProfile(_auditedBaselineId, out var scribeProfile)
                        ? new AutoScribeServiceCycleFeature(
                            new AutoScribeFeatureDependencies(
                                registryResolver,
                                scribeProfile,
                                readLifecycleEpoch,
                                ownsActionFamily: () =>
                                    _automataActionFamilyOwnership!.OwnsScribe,
                                tryCaptureMutationPermit: scribeFamily.TryCapture,
                                readOwnershipFailure: scribeFamily.Failure,
                                featureStatus: featureStatuses.AutoScribe))
                        : new AutoScribeUnavailableServiceCycleFeature(
                            featureStatuses.AutoScribe);
                return AutomataServiceCycleComposition.TryCreate(
                    configuration,
                    configurationGeneration,
                    new AutomataServiceCycleHostDependencies(
                            readFrameIdentity,
                            readLifecycleEpoch,
                            ServiceActionTimelineRegistry.Shared,
                            pumpTiming: ServiceCyclePumpTimingRegistry.Shared,
                            observability: new AutomataServiceCycleObservabilityOptions(
#if SERVICE_CYCLE_PROFILE
                            AutomataFullTracePathPolicy.CreateOptions(),
#else
                            default,
#endif
                            AutomataDecisionJournalPathPolicy.Create(
                                DecisionJournalStatusRegistry.Shared),
                            AutoStartServiceCycleDiagnostics)),
                    new IAutomataServiceCycleFeature[]
                    {
                        // Registered first so the world is collected before the services that
                        // read it evaluate. Ordering between services is not enforced and this
                        // does not make it so — it only avoids every consumer's first cycle
                        // reading the empty snapshot for no reason.
                        new AutomataWorldCollectionFeature(
                            readFrameIdentity,
                            readLifecycleEpoch,
                            _worldDemand,
                            static report =>
                            {
                                var line = report.Describe() + " " + report.DescribeCost();
                                if (report.IsComplete) Log.LogInfo(line);
                                else Log.LogWarning(line);
                            },
                            createCollector: () =>
                                GameWorldCollector.ForSession(_mentorMasteryJournal)),
                        new AutoItemsServiceCycleFeature(
                            new AutoItemsFeatureDependencies(
                                registryResolver,
                                readLifecycleEpoch,
                                ownsActionFamily: () =>
                                    _automataActionFamilyOwnership!.OwnsItems,
                                tryCaptureMutationPermit: itemsFamily.TryCapture,
                                readOwnershipFailure: itemsFamily.Failure,
                                featureStatus: featureStatuses.AutoItems,
                                warn: message => Log.LogAutomataWarning(message))),
                        autoScribeFeature,
                        new AutoHarvestServiceCycleFeature(
                            new AutoHarvestFeatureDependencies(
                                registryResolver,
                                ownsActionFamily: () => _automataActionFamilyOwnership!.OwnsHarvest,
                                tryCaptureMutationPermit: harvestFamily.TryCapture,
                                runtimeDiagnostics: RuntimeDiagnosticsRegistry.Shared,
                                featureStatus: featureStatuses.AutoHarvest)),
                        new AutoBuyServiceCycleFeature(
                            new AutoBuyFeatureDependencies(
                                readLifecycleEpoch,
                                ownershipMask: () =>
                                    _automataActionFamilyOwnership!.EffectiveAutoBuyOwnership(
                                        _configurationStore!.Current.AutoBuy),
                                runtimeDiagnostics: RuntimeDiagnosticsRegistry.Shared,
                                featureStatus: featureStatuses.AutoBuy,
                                refusalBundles: new AutoBuyRefusalBundleWriter(
                                    () => AutomataTraceRunRoot.Stable("diagnostics")),
                                reportInvariant: message => Log.LogAutomataError(message)
#if SERVICE_CYCLE_PROFILE
                                , gameMcpOwnership: kind =>
                                    _automataActionFamilyOwnership!.OwnsAutoBuy(kind)
#endif
                                )),
                        new SpellLevelServiceCycleFeature(
                            new SpellLevelFeatureDependencies(
                                readLifecycleEpoch,
                                ownsActionFamily: () => _automataActionFamilyOwnership!.OwnsSpellLevel,
                                capability: _spellLevelCapability,
                                featureStatus: featureStatuses.SpellLevel)),
                        new AutoCastServiceCycleFeature(
                            new AutoCastFeatureDependencies(
                                readLifecycleEpoch,
                                ownsActionFamily: () => _automataActionFamilyOwnership!.OwnsCast,
                                _autoCastManualPause,
                                featureStatus: featureStatuses.AutoCast)),
                        new AutoConceptServiceCycleFeature(
                            new AutoConceptFeatureDependencies(
                                readLifecycleEpoch,
                                ownsActionFamily: () => _automataActionFamilyOwnership!.OwnsConcept,
                                featureStatus: featureStatuses.AutoConcept)),
                        new MentorServiceCycleFeature(
                            new MentorFeatureDependencies(
                                readLifecycleEpoch,
                                captureMutationPermit: domain =>
                                    _mentorActionFamilyOwnership!.TryCaptureMutationPermit(
                                        domain switch
                                        {
                                            MasteryExperienceDomain.Spell => MentorDomain.Spells,
                                            MasteryExperienceDomain.Artifact => MentorDomain.Artifacts,
                                            _ => MentorDomain.Alchemy,
                                        }) == true,
                                featureStatus: featureStatuses.Mentor)),
                        new StrategistServiceCycleFeature(),
                    },
                    Log
#if SERVICE_CYCLE_PROFILE
                    , composeGameActions: actions =>
                    {
                        var discoveryTreeOfferFamily = ownership.Family(GameMcpCommandKind.DiscoveryTreeOffer);
                        actions.Add("game_discover", new DiscoveryTreeOfferGameAction(
                            readLifecycleEpoch,
                            discoveryTreeOfferFamily.TryCapture,
                            discoveryTreeOfferFamily.Failure));
                        var spellWorkbenchFamily = ownership.Family(GameMcpCommandKind.SpellWorkbench);
                        actions.Add("game_spell_loadout", new SpellWorkbenchGameAction(
                            readLifecycleEpoch,
                            spellWorkbenchFamily.TryCapture,
                            spellWorkbenchFamily.Failure));
                        var spellCompositionFamily = ownership.Family(GameMcpCommandKind.SpellComposition);
                        actions.Add("game_casting_dial", new SpellCompositionGameAction(
                            readLifecycleEpoch,
                            spellCompositionFamily.TryCapture,
                            spellCompositionFamily.Failure));
                        var spellLoadoutFamily = ownership.Family(GameMcpCommandKind.SpellLoadout);
                        actions.Add("game_spell_loadout", new SpellLoadoutGameAction(
                            readLifecycleEpoch,
                            spellLoadoutFamily.TryCapture,
                            spellLoadoutFamily.Failure,
                            readAutoCastOn: () => AutoCastConfigurationPolicy.IsOperational(
                                _configurationStore!.Current)));
                        var targetingFamily = ownership.Family(GameMcpCommandKind.Targeting);
                        actions.Add("game_targeting", new TargetingGameAction(
                            readLifecycleEpoch,
                            targetingFamily.TryCapture,
                            targetingFamily.Failure));
                        var equipmentLoadoutFamily = ownership.Family(GameMcpCommandKind.EquipmentLoadout);
                        var equipment = actions.Add("game_equipment", new EquipmentLoadoutGameAction(
                            readLifecycleEpoch,
                            equipmentLoadoutFamily.TryCapture,
                            equipmentLoadoutFamily.Failure));
                        var alchemyLoadoutFamily = ownership.Family(GameMcpCommandKind.AlchemyLoadout);
                        var alchemy = actions.Add("game_alchemy", new AlchemyLoadoutGameAction(
                            readLifecycleEpoch,
                            alchemyLoadoutFamily.TryCapture,
                            alchemyLoadoutFamily.Failure));
                        var ritualLifecycleFamily = ownership.Family(GameMcpCommandKind.RitualLifecycle);
                        actions.Add("game_ritual", new RitualLifecycleGameAction(
                            readLifecycleEpoch,
                            ritualLifecycleFamily.TryCapture,
                            ritualLifecycleFamily.Failure));
                        var genericLevelFamily = ownership.Family(GameMcpCommandKind.GenericLevel);
                        actions.Add("game_level_up", new GenericLevelGameAction(
                            readLifecycleEpoch,
                            genericLevelFamily.TryCapture,
                            genericLevelFamily.Failure));
                        actions.Add("game_craft", new CraftingInstanceLifecycleGameAction(
                            readLifecycleEpoch,
                            scribeFamily.TryCapture,
                            scribeFamily.Failure));
                        var loadoutFamily = ownership.Family(GameMcpCommandKind.Loadout);
                        actions.Add("game_loadout", new LoadoutGameAction(
                            readLifecycleEpoch,
                            loadoutFamily.TryCapture,
                            loadoutFamily.Failure,
                            equipment,
                            alchemy));
                        var harvestLifecycleFamily = ownership.Family(GameMcpCommandKind.HarvestLifecycle);
                        actions.Add("game_agromancy", new HarvestLifecycleGameAction(
                            readLifecycleEpoch,
                            harvestLifecycleFamily.TryCapture,
                            harvestLifecycleFamily.Failure));
                        actions.Add("game_agromancy", new PlotLifecycleGameAction(
                            readLifecycleEpoch,
                            harvestFamily.TryCapture,
                            harvestFamily.Failure));
                        var structureLifecycleFamily = ownership.Family(GameMcpCommandKind.StructureLifecycle);
                        actions.Add("game_structure", new StructureLifecycleGameAction(
                            readLifecycleEpoch,
                            structureLifecycleFamily.TryCapture,
                            structureLifecycleFamily.Failure));
                        var returnToMenuFamily = ownership.Family(GameMcpCommandKind.ReturnToMenu);
                        actions.Add("game_return_to_menu", new ReturnToMenuGameAction(
                            readLifecycleEpoch,
                            returnToMenuFamily.TryCapture,
                            returnToMenuFamily.Failure,
                            readScene: () => SceneManager.GetActiveScene().name,
                            findLoadedObjects: type => Resources.FindObjectsOfTypeAll(type)
                                .Cast<object>()
                                .ToArray()));
                        var challengeFamily = ownership.Family(GameMcpCommandKind.Challenge);
                        actions.Add("time_challenge", new ChallengeGameAction(
                            readLifecycleEpoch,
                            challengeFamily.TryCapture,
                            challengeFamily.Failure));
                        var prestigeFamily = ownership.Family(GameMcpCommandKind.Prestige);
                        actions.Add("time_prestige", new PrestigeGameAction(
                            readLifecycleEpoch,
                            prestigeFamily.TryCapture,
                            prestigeFamily.Failure));
                        var researchFamily = ownership.Family(GameMcpCommandKind.Research);
                        actions.Add("game_research", new ResearchGameAction(
                            readLifecycleEpoch,
                            researchFamily.TryCapture,
                            researchFamily.Failure));
                    }
#endif
                    );
            },
            _configurationStore!.Current,
            _configurationStore!.CurrentGeneration,
            featureStatuses.ObserveServiceCycleUnavailable);
        foreach (var hook in LifecycleObservationHooks)
            PatchOptional(hook.Target, hook.Handler, hook.Postfix);
        var runtimeConfig = _configurationStore.Current;
        Log.LogAutomataInfo(
            $"Automata loaded. AutoBuyMode={runtimeConfig.AutoBuy.Mode}, " +
            $"StructureAffordability={runtimeConfig.AutoBuy.StructureAffordability}, " +
            $"UpgradeAffordability={runtimeConfig.AutoBuy.UpgradeAffordability}, " +
            $"AutoCastMode={runtimeConfig.AutoCast.Mode}, " +
            $"AutoCastFullCharge={runtimeConfig.AutoCast.FullCharge}, " +
            $"AutoCastRitualAware={runtimeConfig.AutoCast.RitualAware}, " +
            $"AutoCastStartResourcePercent={runtimeConfig.AutoCast.StartResourcePercent}, " +
            $"AutoConceptMode={runtimeConfig.AutoConcept.Mode}, " +
            $"AutoConceptSlotManagement={runtimeConfig.AutoConcept.SlotManagement}, " +
            $"AutoHarvestMode={runtimeConfig.AutoHarvest.Mode}, " +
            $"AutoHarvestFruitTrees={runtimeConfig.AutoHarvest.CollectFruitTrees}, " +
            $"AutoHarvestTreasureTrees={runtimeConfig.AutoHarvest.CollectTreasureTrees}, " +
            $"AutoItemsMode={runtimeConfig.AutoItems.Mode}, " +
            $"AutoItemsUseScrolls={runtimeConfig.AutoItems.UseScrolls}, " +
            $"AutoItemsUseRelics={runtimeConfig.AutoItems.UseRelics}, " +
            $"AutoItemsTemporaryItemAllowlistConfigured=" +
            $"{AutoItemsTemporaryItemAllowlist.HasAnyValidEntry(runtimeConfig.AutoItems.TemporaryItemAllowlist)}, " +
            $"AutoScribeMode={runtimeConfig.AutoScribe.Mode}, " +
            $"AutoScribeRoles={runtimeConfig.AutoScribe.Roles}, " +
            $"AutoLevelSpells={runtimeConfig.AutoBuy.AutoLevelSpells}, " +
            "Auto Buy fills the available queue and groups structures by live Bulk Development.");
    }

    private void ComposeDiagnosticsBundle()
    {
        var configRoot = string.IsNullOrWhiteSpace(Paths.ConfigPath)
            ? string.Empty
            : Path.GetFullPath(Paths.ConfigPath);
        var bepInExRoot = configRoot.Length == 0
            ? string.Empty
            : Path.GetDirectoryName(configRoot) ?? string.Empty;
        var output = configRoot.Length == 0
            ? string.Empty
            : Path.Combine(configRoot, "OrbOfCreation-ModSuite", "diagnostics");
        var logPath = bepInExRoot.Length == 0
            ? string.Empty
            : Path.Combine(bepInExRoot, "LogOutput.log");
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var redactor = new DiagnosticsTextRedactor(
            new[]
            {
                userProfile,
                Application.persistentDataPath,
                configRoot,
                bepInExRoot,
            },
            new[] { Environment.UserName });
        var buildIdentity = _auditedBuild
            ? "audited baseline " + _auditedBaselineId
            : "unverified assembly pair " + _observedBuildFingerprint;
        _diagnosticsBundleController = DiagnosticsBundleController.TryCreate(
            DiagnosticsBundleRegistry.Shared,
            new DiagnosticsBundleControllerOptions(
                output,
                Config.ConfigFilePath,
                Application.persistentDataPath,
                logPath,
                PluginIds.ReleaseVersion,
                buildIdentity,
                () => FeatureStatusRegistry.Shared.GetSnapshot(),
                () => RuntimeDiagnosticsRegistry.Shared.GetSnapshot(),
                CaptureDiagnosticsRuntimeEvidence,
                () => DecisionJournalStatusSources.Shared.Status,
                () => AutomataLoggingExtensions.Flush(Log),
                redactor),
            Log);
    }

    private AutomataDiagnosticsRuntimeEvidence CaptureDiagnosticsRuntimeEvidence()
    {
        if (_serviceCycleActivation is not null &&
            _serviceCycleActivation.TryCaptureDiagnostics(out var evidence))
            return evidence;
        return AutomataDiagnosticsRuntimeEvidence.Unavailable(
            "The automation runtime is not active, so recent event and journal evidence is unavailable.");
    }

    /// <summary>
    /// Composed last so the catalog the browser later discovers already sees the automation and
    /// mastery feature statuses published above it.
    /// </summary>
    private void ComposeModConfig()
    {
        _uiSurfaceDiagnostics = new SuiteUiSurfaceDiagnostics(
            RuntimeDiagnosticsRegistry.Shared,
            message => Log.LogAutomataInfo(message),
            message => Log.LogAutomataError(message));
        _runtimeSources = new ModConfigRuntimeSources(
            ConfigurationSchemaStatusRegistry.Shared,
            FeatureStatusRegistry.Shared,
            RuntimeDiagnosticsRegistry.Shared,
            ServiceActionTimelineSources.Shared,
            ServiceCyclePumpTimingRegistry.Shared,
            DiagnosticsBundleRegistry.Shared,
            _mathVerification ??
            throw new InvalidOperationException("Differential verification control was not composed."),
            DecisionJournalStatusSources.Shared
#if SERVICE_CYCLE_PROFILE
            , PerformanceProfileControlRegistry.Shared
#endif
            );
        _modConfigFeatureCommands = new ModConfigFeatureCommands(
            _automationFeatureControls ??
            throw new InvalidOperationException(
                "Automation feature control registry was not composed."),
            _emergencyStopControl ??
            throw new InvalidOperationException(
                "Emergency stop control was not composed."));
        _runUiMaintenance = RunUiMaintenance;
        _uiWork = new ModConfigFrameWork(() => Time.frameCount);
        var activeScene = SceneManager.GetActiveScene();
        ResetSceneState(activeScene);
        if (activeScene.name == "Main") ScheduleUiStartupReadiness(activeScene);
    }

    private void Update()
    {
        if (_automataConfig is null) return;
        UpdateBuildCompatibilityOverride();
        PublishChangedConfiguration();
        ValidateSuiteShortcuts();
        if (!GameMcpActionRegistrationPolicy.ShouldCompose(_runtimeActivationAllowed))
        {
            _runtimeCompositionAttempted = false;
        }
        else if (!_runtimeComposed && !_runtimeCompositionAttempted)
        {
            try
            {
                EnsureRuntimeComposition();
            }
            catch (Exception ex)
            {
                Logger.LogError("Could not compose the shared gameplay runtime: " +
                                ex.GetBaseException().Message);
                _featureStatuses?.ObserveServiceCycleUnavailable(
                    _configurationStore!.Current,
                    _configurationStore.CurrentGeneration);
            }
        }

        UpdateAutomata();
        _stuckGame?.Update();
        _diagnosticsBundleController?.Tick();
#if SERVICE_CYCLE_PROFILE
        DrainGameMcpOperations();
#endif
        UpdateMentor();
        UpdateUiStartupReadiness(Time.unscaledDeltaTime);
        UpdateQuickControls(Time.unscaledDeltaTime);
        UpdateModConfig();
        _uiShell?.ShowStuckGameNotice(_stuckGame?.Notice);
    }

    private void UpdateBuildCompatibilityOverride()
    {
        if (_auditedBuild || _automataConfig is null) return;

        var emergencyClearRequested = _automataConfig.TryTakeEmergencyClearRequest();
        if (emergencyClearRequested && !_buildCompatibilityRuntimeAllowed)
            _automataConfig.SetAllowUnverifiedGameBuild(true);

        var decision = UnverifiedBuildCompatibilityPolicy.AfterExplicitChange(
            audited: false,
            _observedBuildFingerprint,
            _automataConfig.AllowUnverifiedGameBuild.Value,
            _automataConfig.AcceptedUnverifiedBuildFingerprint.Value);
        if (decision.AcceptObserved)
            _automataConfig.AcceptUnverifiedBuild(_observedBuildFingerprint);

        if (!decision.RuntimeAllowed &&
            decision.EngageEmergencyStop &&
            !_automataConfig.EmergencyDisable.Value)
        {
            _automataConfig.SetEmergencyStop(true);
        }

        if (decision.RuntimeAllowed == _buildCompatibilityRuntimeAllowed) return;
        if (decision.RuntimeAllowed && !emergencyClearRequested)
            _automataConfig.SetEmergencyStop(true);
        _buildCompatibilityRuntimeAllowed = decision.RuntimeAllowed;
        _runtimeActivationAllowed = SuiteStartupAdmission.AllowsRuntime(
            _buildCompatibilityRuntimeAllowed,
            _automaticSaveBackup);
        _nativeContractsAvailable = _runtimeActivationAllowed;

        if (decision.RuntimeAllowed)
        {
            Logger.LogWarning(!_automaticSaveBackup.AllowsAutomation
                ? "The player accepted this exact unverified game assembly pair, but automatic save-backup failure still blocks runtime composition until the next launch succeeds."
                : emergencyClearRequested
                    ? "The player cleared the emergency stop and accepted this exact unverified game assembly pair. Runtime composition is now permitted at the player's own risk."
                    : "The player accepted this exact unverified game assembly pair. Runtime composition is now permitted at the player's own risk; the emergency stop remains engaged until explicitly resumed.");
            return;
        }

        Logger.LogError(
            "The unverified-build acknowledgement was removed. The compatibility emergency stop is engaged; restart the game to unload already-installed patches.");
    }

    private void UpdateStartStatusView()
    {
        if (!string.Equals(
                SceneManager.GetActiveScene().name,
                "Start",
                StringComparison.Ordinal))
        {
            _startStatusView?.Dispose();
            _startStatusView = null;
            _startStatusFailure = string.Empty;
            _startStatusFailureFrames = 0;
            return;
        }
        var controlPlaneReady = _automataConfig is not null &&
            _controlPlaneFailure.Length == 0;
#if SERVICE_CYCLE_PROFILE
        if (_processId == 0)
            _processId = ModConfigProcessIdentity.CaptureCurrentProcessId();
        var presentation = ModConfigStartStatusPresenter.Build(
            PluginIds.ReleaseVersion,
            controlPlaneReady,
            _auditedBuild,
            _runtimeActivationAllowed,
            _automaticSaveBackup,
            _gameMcpServer is not null,
            _processId);
#else
        var presentation = ModConfigStartStatusPresenter.Build(
            PluginIds.ReleaseVersion,
            controlPlaneReady,
            _auditedBuild,
            _runtimeActivationAllowed,
            _automaticSaveBackup);
#endif
        _startStatusView ??= new ModConfigStartStatusView();
        if (_startStatusView.TryRender(
                presentation,
                out var reason))
        {
            _startStatusFailure = string.Empty;
            _startStatusFailureFrames = 0;
            return;
        }
        if (string.Equals(_startStatusFailure, reason, StringComparison.Ordinal))
            _startStatusFailureFrames++;
        else
        {
            _startStatusFailure = reason;
            _startStatusFailureFrames = 1;
        }
        if (_startStatusFailureFrames != StartStatusFailureLogFrameThreshold) return;
        Logger.LogError("Start status panel unavailable: " + reason);
    }

    private void ValidateSuiteShortcuts()
    {
        if (_automataConfig is null || _mentorConfig is null) return;
        var autoCast = _automataConfig.AutoCastToggleShortcut.Value;
        var mentor = _mentorConfig.ToggleShortcut.Value;
        var signature = autoCast + "\u001f" + mentor;
        if (string.Equals(signature, _shortcutAuditSignature, StringComparison.Ordinal)) return;
        _shortcutAuditSignature = signature;
        var listeners = SuiteShortcutCollisionValidator.Inventory(autoCast, mentor);
        var collisions = SuiteShortcutCollisionValidator.Validate(listeners);
        foreach (var listener in listeners)
        {
            if (listener.Kind == SuiteShortcutListenerKind.RuntimePageButton)
            {
                Logger.LogInfo(listener.DisplayName + " uses a Mods Runtime button and has no key listener.");
                continue;
            }
            if (!collisions.Any(collision =>
                    string.Equals(collision.ListenerId, listener.Id, StringComparison.Ordinal)))
            {
                Logger.LogInfo(
                    $"Shortcut audit: {listener.DisplayName} ({listener.Shortcut}) has no audited native default collision.");
            }
        }
        foreach (var collision in collisions)
        {
            if (collision.IsSuiteListener)
            {
                Logger.LogWarning(
                    $"Shortcut audit: {collision.ListenerDisplayName} and " +
                    $"{collision.ConflictingBinding} are both bound to the exact chord " +
                    $"{collision.Key}; one press will run both listeners.");
                continue;
            }
            Logger.LogWarning(
                $"Shortcut audit: {collision.ListenerDisplayName} uses {collision.Key} as " +
                (collision.IsMainKey ? "its main key" : "a held modifier") +
                $", which also drives the native {collision.ConflictingBinding} binding.");
        }
    }

    private void UpdateAutomata()
    {
        var configuration = _configurationStore!.Current;
        _mathVerification?.Tick();
        if (!_nativeContractsAvailable)
        {
            _featureStatuses?.ObserveContractUnavailable(
                configuration,
                _lifecycleGeneration,
                !_automaticSaveBackup.AllowsAutomation
                    ? AutomaticSaveBackupWording.BlockingReason(_automaticSaveBackup)
                    : "Installed game assemblies are quarantined pending an exact-build acknowledgement.",
                _configurationStore.CurrentGeneration);
            return;
        }
        var deltaTime = Time.unscaledDeltaTime;
        if (SceneManager.GetActiveScene().name == "Main" &&
            _automataConfig!.IsAutoCastTogglePressed() &&
            _automationFeatureControls!.TryGet("Auto Cast", out var autoCast))
            autoCast.Toggle();
        if (!configuration.General.Enabled)
        {
            CancelPreparedAutomationForOwnershipRelease();
            _automataActionFamilyOwnership?.Refresh(
                configuration,
                lifecycleReady: false,
                Time.frameCount);
            return;
        }
        var lifecycleReady = IsLifecycleReady();
        _automataActionFamilyOwnership?.RefreshLoadedPluginInventory(
            Chainloader.PluginInfos.Count,
            guid => Chainloader.PluginInfos.ContainsKey(guid));
        if (!_knownOwnershipWarningLogged &&
            _automataActionFamilyOwnership?.KnownAutoBuyLoaded == true)
        {
            _knownOwnershipWarningLogged = true;
            Log.LogAutomataWarning(
                "AutobuyOrb is loaded. Automata will block Structure and Upgrade purchases because those native action families overlap; Auto Cast, Auto Concept, Spell Leveling, and Mentor remain independent.");
        }
        _automataActionFamilyOwnership?.Refresh(configuration, lifecycleReady, Time.frameCount);
        if (lifecycleReady)
        {
            _serviceCycleActivation?.Tick(deltaTime);
        }
        else
        {
            _featureStatuses?.ObserveLifecycleNotReady(
                configuration,
                _lifecycleGeneration,
                _configurationStore.CurrentGeneration);
        }
    }

    private void UpdateMentor()
    {
        if (_mentorConfig is null) return;
        _mentorActionFamilyOwnership?.Refresh(_mentorConfig, IsGameplayScene(), Time.frameCount);
        if (SceneManager.GetActiveScene().name == "Main" && _mentorConfig.ToggleShortcut.Value.IsDown())
        {
            if (_automationFeatureControls!.TryGet("Mentor", out var mentor))
                mentor.Toggle();
            Logger.LogInfo($"Orb Mentor is now {_mentorConfig.Mode.Value}.");
        }
    }

    private void UpdateModConfig()
    {
        if (_modConfigSettings is null)
        {
            DeactivateUiWork(disposeShell: true);
            return;
        }

        if (SceneManager.GetActiveScene().name != "Main")
        {
            DeactivateUiWork(disposeShell: false);
            return;
        }

        if (!_uiStartupReadiness.Admission.ModsRail)
        {
            _uiWork?.SetState(true, false);
            return;
        }

        if (_modConfigSettings.EnableUiShell.Value != true)
        {
            DeactivateUiWork(disposeShell: true);
            return;
        }

        if (_uiShell is not null && !_uiShell.IsAlive)
            _uiMaintenanceDue = true;
        else if (_uiShell is null)
        {
            if (Time.frameCount >= _deferInstallUntilFrame)
            {
                _uiRetrySeconds -= Math.Max(0.0f, Time.unscaledDeltaTime);
                if (_uiRetrySeconds <= 0.0f) _uiMaintenanceDue = true;
            }
        }
        else
        {
            if (_uiShell.ScheduleRefresh(Time.unscaledDeltaTime)) _uiMaintenanceDue = true;
            if (AdvanceCadence(ref _uiIntegritySeconds, Time.unscaledDeltaTime, UiIntegrityIntervalSeconds))
            {
                _uiIntegrityDue = true;
                _uiMaintenanceDue = true;
            }
        }

        var runUiMaintenance = _runUiMaintenance;
        if (runUiMaintenance is not null)
            _uiWork?.TryRun(true, _uiMaintenanceDue, runUiMaintenance);
        _uiWork?.SetState(true, _uiMaintenanceDue);
    }

    private void LateUpdate()
    {
        UpdateStartStatusView();
    }

    private void OnDisable()
    {
        CancelPreparedAutomationForOwnershipRelease();
        _automataActionFamilyOwnership?.ReleaseLifecycleClaims();
        _mentorActionFamilyOwnership?.ReleaseLifecycleClaims();
    }

    private void OnDestroy()
    {
#if SERVICE_CYCLE_PROFILE
        _gameMcpOperations?.Close(
            "suite_shutdown",
            "the suite is shutting down; pending MCP commands cannot mutate native state");
        _gameMcpServer?.Dispose();
        _gameMcpServer = null;
        _gameMcpOperations = null;
        _gameMcpWritableConfiguration = Array.Empty<GameMcpWritableSettingDescriptor>();
        _gameMcpTooltipNativeAccess = null;
        _modalDismissGameAction?.Dispose();
        _modalDismissGameAction = null;
        _modalOpenGameAction?.Dispose();
        _modalOpenGameAction = null;
        _gameMcpTooltipContractFailure = "tooltip native layout has been released";
#endif
        _startStatusView?.Dispose();
        _startStatusView = null;
        _quickControls?.Dispose();
        _quickControls = null;
        _emergencyStopControl = null;
        _automationFeatureControls = null;
        _uiShell?.Dispose();
        _uiShell = null;
        _uiWork?.Dispose();
        _uiWork = null;
        _runUiMaintenance = null;
        _diagnosticsBundleController?.Dispose();
        _diagnosticsBundleController = null;
        _runtimeSources = null;
        _modConfigFeatureCommands = null;
        _uiSurfaceDiagnostics?.Dispose();
        _uiSurfaceDiagnostics = null;
        _automaticSaveBackupHealth?.Dispose();
        _automaticSaveBackupHealth = null;
        _configurationStore = null;

        _mentorActionFamilyOwnership?.Dispose();
        _mentorActionFamilyOwnership = null;

        GameLifecycleMonitor.Shared.Transitioned -= OnLifecycleTransition;
        SceneManager.activeSceneChanged -= OnActiveSceneChanged;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (_serviceCycleActivation is not null)
        {
            // The runtime engages the emergency stop as it tears down, deliberately as a
            // non-clearable shutdown episode so a resume cannot revive a disposed runtime. That
            // leaves EmergencyEntered as the last thing a recording ever hears from the suite, with
            // no EmergencyCleared behind it, which reads exactly like a suite that died mid-run.
            Logger.LogAutomataInfo(
                "Suite shutdown stops automation and leaves it stopped: the emergency stop entered " +
                "here is the teardown interlock and is never cleared, because the runtime it " +
                "protects is going away.");
        }
        _serviceCycleActivation?.Dispose();
        _serviceCycleActivation = null;
        _automataActionFamilyOwnership?.Dispose();
        _automataActionFamilyOwnership = null;
        _mathVerification = null;
        _featureStatuses?.Dispose();
        _featureStatuses = null;

        _harmony?.UnpatchSelf();
        _harmony = null;
        Instance = null;
    }

    private void CancelPreparedAutomationForOwnershipRelease()
    {
        _serviceCycleActivation?.CancelPreparedWork();
    }

    private void UpdateQuickControls(float unscaledDeltaTime)
    {
        if (_automationFeatureControls is null || _emergencyStopControl is null) return;
        if (SceneManager.GetActiveScene().name != "Main")
        {
            _quickControls?.Dispose();
            _quickControls = null;
            _quickControlsUiRetrySeconds = 0f;
            ResetQuickControlsFailure();
            return;
        }
        if (_quickControls is not null && !_quickControls.IsAlive)
        {
            _quickControls.Dispose();
            _quickControls = null;
        }
        if (_quickControls is not null &&
            _quickControls.AllowsFeatureControls != _nativeContractsAvailable)
        {
            _quickControls.Dispose();
            _quickControls = null;
            ResetQuickControlsFailure();
        }
        _quickControls?.Render();
        if (_quickControls is not null && _quickControls.Failures.Count == 0)
        {
            ResetQuickControlsFailure();
            _uiSurfaceDiagnostics?.ReportSuccess(SuiteUiSurface.QuickControls);
            return;
        }

        if (!_uiStartupReadiness.Admission.QuickControls) return;

        _quickControlsUiRetrySeconds -= Math.Max(0f, unscaledDeltaTime);
        if (_quickControlsUiRetrySeconds > 0f) return;
        _quickControlsUiRetrySeconds = UiRetryIntervalSeconds;
        if (!QuickControlNativeAdapter.TryCapture(out var native, out var captureReason))
        {
            ReportQuickControlsRetry(captureReason);
            return;
        }
        if (!QuickControlColumn.TryCreate(
                _automationFeatureControls,
                _emergencyStopControl,
                native,
                allowFeatureControls: _nativeContractsAvailable,
                out var candidate,
                out var constructionReason))
        {
            ReportQuickControlsRetry(constructionReason);
            return;
        }

        if (_quickControls is null ||
            candidate!.Failures.Count < _quickControls.Failures.Count)
        {
            _quickControls?.Dispose();
            _quickControls = candidate;
        }
        else
        {
            candidate!.Dispose();
        }
        if (_quickControls is not null && _quickControls.Failures.Count == 0)
        {
            ResetQuickControlsFailure();
            _uiSurfaceDiagnostics?.ReportSuccess(SuiteUiSurface.QuickControls);
            return;
        }
        ReportQuickControlsRetry(constructionReason);
    }

    private void ReportQuickControlsRetry(string reason)
    {
        _quickControlsUiFailureReason = string.IsNullOrWhiteSpace(reason)
            ? "audited native objects are not ready"
            : reason;
        var observation = _quickControlsRetry.ObserveFailure();
        if (observation.ShouldLogRetry)
        {
            Logger.LogInfo(
                "Quick controls are not ready; installation will retry: " +
                _quickControlsUiFailureReason);
        }
        if (observation.IsTerminal)
            _uiSurfaceDiagnostics?.ReportFailure(
                SuiteUiSurface.QuickControls,
                _quickControlsUiFailureReason);
        else
            _uiSurfaceDiagnostics?.ReportWaiting(
                SuiteUiSurface.QuickControls,
                _quickControlsUiFailureReason);
    }

    private void ResetQuickControlsFailure()
    {
        _quickControlsUiFailureReason = string.Empty;
        _quickControlsRetry.Reset();
    }

    private void UpdateUiStartupReadiness(float unscaledDeltaTime)
    {
        if (SceneManager.GetActiveScene().name != "Main" ||
            !_uiStartupReadiness.ShouldInspect(unscaledDeltaTime))
            return;
        var readiness = NativeViewAdapter.ObserveTopBarStartupReadiness();
        var before = _uiStartupReadiness.Admission;
        var after = _uiStartupReadiness.Observe(readiness.Kind);
        if (before == after || !after.QuickControls || !after.ModsRail) return;
        // One gate releases both suite surfaces in the same Update. Ready means the six shared
        // icon candidates exist; slow-failure admission means the startup grace ended or a real
        // structural mismatch must enter the ordinary five-second/terminal discipline now.
        _quickControlsUiRetrySeconds = 0f;
        _uiRetrySeconds = 0f;
        _uiMaintenanceDue = true;
    }

    private void OnEmergencyStopChanged(bool stopped)
    {
        CancelPreparedAutomationForOwnershipRelease();
        if (stopped)
            _mentorActionFamilyOwnership?.ReleaseLifecycleClaims();
        _uiMaintenanceDue = true;
        Logger.LogWarning(stopped
            ? "Suite emergency stop engaged; prepared automation work was discarded."
            : "Suite emergency stop cleared by immediate toggle.");
    }

    private void OnActiveSceneChanged(Scene previous, Scene next)
    {
        ObserveLifecycle(GameLifecycleTransitionKind.SceneExited, previous.name);
        ObserveLifecycle(GameLifecycleTransitionKind.SceneEntered, next.name);
        if (next.name == "Main") ScheduleUiStartupReadiness(next);
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == "Main") ScheduleUiStartupReadiness(scene);
    }

    private void ScheduleUiStartupReadiness(Scene scene)
    {
        if (_uiStartupReadinessScheduled) return;
        _uiStartupReadinessScheduled = true;
        var sceneEpoch = _uiSceneEpoch;
        StartCoroutine(ObserveUiStartupReadinessBoundary(scene, sceneEpoch));
    }

    private IEnumerator ObserveUiStartupReadinessBoundary(Scene scene, int sceneEpoch)
    {
        // Scene load precedes the native UI lifecycle. Start the bounded shared-readiness window
        // after the first frame; the game renders UIViewRadio's icon entries later on a
        // machine/load-dependent schedule that is not coordinated with the suite scene clock.
        yield return new WaitForEndOfFrame();
        if (sceneEpoch != _uiSceneEpoch ||
            scene.name != "Main" ||
            SceneManager.GetActiveScene().name != "Main")
            yield break;
        _uiStartupReadiness.Begin();
    }

    private void OnLifecycleTransition(GameLifecycleTransition transition)
    {
        if (transition.Current.Generation == _lifecycleGeneration) return;
        Logger.LogAutomataInfo(transition.Describe());
        _lifecycleGeneration = transition.Current.Generation;
        _stuckGame?.ResetLifecycle();
        EntityIdentityCatalog.Shared.Reset(_lifecycleGeneration);
        _serviceCycleActivation?.InvalidateLifecycle();
#if SERVICE_CYCLE_PROFILE
        _modalDismissGameAction?.InvalidateLifecycle();
        _modalOpenGameAction?.InvalidateLifecycle();
        _gameMcpAgentSettingsFailure = string.Empty;
#endif
        _automataActionFamilyOwnership?.ReleaseLifecycleClaims();
        if (_configurationStore is not null)
            _featureStatuses?.ObserveLifecycleNotReady(
                _configurationStore.Current,
                _lifecycleGeneration,
                _configurationStore.CurrentGeneration);
        MentorMasteryPatchBridge.ResetLifecycle(_lifecycleGeneration);
        _mentorActionFamilyOwnership?.ReleaseLifecycleClaims();
        _lifecycleLease = GameLifecycleMonitor.Shared.CaptureLease();

        // The quick-controls anchor and every borrowed sprite are scene-object references. Save
        // load, reset, and NG+ can replace them without changing the active scene name, so no
        // lifecycle generation may retain the previous column.
        _quickControls?.Dispose();
        _quickControls = null;
        _quickControlsUiRetrySeconds = 0f;
        ResetQuickControlsFailure();

        // Mod Config rebuilds its shell only when the game entered a scene; every other transition
        // leaves the installed UI alone.
        if (transition.Current.LastTransition != GameLifecycleTransitionKind.SceneEntered) return;
        _uiShell?.Dispose();
        _uiShell = null;
        ResetSceneState(SceneManager.GetActiveScene());
    }

    private static void ObserveLifecycle(
        GameLifecycleTransitionKind kind,
        string sceneName,
        object? nativeIdentity = null)
    {
        GameLifecycleMonitor.Shared.TryObserve(
            new GameLifecycleObservation(
                kind,
                Time.frameCount,
                sceneName,
                PluginIds.SuiteGuid,
                nativeIdentity),
            out _,
            out _);
    }

    /// <summary>
    /// Commits a pending external or staged settings change at the start of the application frame.
    /// </summary>
    /// <remarks>
    /// Every source counts: a setting changed through BepInEx's configuration manager or by editing
    /// the file advances the generation exactly as an apply in the suite's own settings panel does.
    /// </remarks>
    private void PublishChangedConfiguration()
    {
        _configurationStore?.PublishPending();
    }

    private void PublishConfiguration(
        SuiteRuntimeConfiguration configuration,
        ConfigGeneration configurationGeneration)
    {
        _featureStatuses?.ObserveConfiguration(configuration, configurationGeneration);
        _serviceCycleActivation?.PublishSavedConfiguration(
            configuration,
            configurationGeneration);
    }

#if SERVICE_CYCLE_PROFILE
    private void DrainGameMcpOperations()
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
        var answered = result.WithEntityIdentities(EntityIdentities(context));
        Logger.LogAutomataInfo(GameMcpOperationLedger.DescribeAnswered(
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
        Logger.LogError(
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
                : EntityIdentities(context));
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
                    EntityIdentities(command.FrameContext)));
        }
        Logger.LogAutomataInfo(
            GameMcpOperationLedger.Describe(command, result, Time.frameCount));
    }

    private GameMcpFrameContext CaptureGameMcpFrameContext(GameMcpFrameData required)
    {
        var includeServices = (required & GameMcpFrameData.ServiceHealth) != 0;
        AutomataRuntimeFrameFacts? runtime = null;
        if ((required & (GameMcpFrameData.World | GameMcpFrameData.ServiceHealth)) != 0 &&
            _serviceCycleActivation is not null &&
            _serviceCycleActivation.TryCaptureFrameFacts(includeServices, out var captured))
        {
            runtime = captured;
        }

        var configuration = runtime?.Configuration ?? new ConfigurationPublication(
            _configurationStore?.CurrentGeneration ?? default,
            _configurationStore?.Current ?? new SuiteRuntimeConfiguration());
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
            _lifecycleGeneration,
            (required & GameMcpFrameData.Scene) != 0
                ? SceneManager.GetActiveScene().name
                : string.Empty,
            (required & GameMcpFrameData.NativeContractHealth) != 0 &&
                _nativeContractsAvailable,
            features,
            trace,
            traceRevision,
            writable,
            _modalDismissGameAction?.BindingsAvailable == true &&
                _modalOpenGameAction?.BindingsAvailable == true,
            ModalBindingFailure(),
            GameLifecycleMonitor.Shared.Current.State,
            _gameMcpAgentSettingsFailure);
    }

    /// <summary>
    /// Why the panel verb is out, naming whichever half of it failed to bind. Opening and
    /// dismissing are one verb to a caller, so one line answers for both.
    /// </summary>
    private string ModalBindingFailure()
    {
        if (_modalDismissGameAction is null || _modalOpenGameAction is null)
            return "the modal action boundary was not composed";
        if (!_modalDismissGameAction.BindingsAvailable)
            return _modalDismissGameAction.BindingFailure;
        return _modalOpenGameAction.BindingsAvailable
            ? string.Empty
            : _modalOpenGameAction.BindingFailure;
    }

    internal static bool TryExecuteFrameRead(
        GameMcpOperationRequest request,
        GameMcpFrameContext context,
        out GameMcpToolExecution execution)
    {
        switch (request.ToolName)
        {
            case "world_overview":
                execution = GameMcpToolExecution.Read(
                    GameMcpWorldQuery.Overview(context).Freeze());
                return true;
            case "world_categories":
                execution = GameMcpToolExecution.Read(
                    GameMcpWorldQuery.ListCategories(context).Freeze());
                return true;
            case "world_list":
                execution = GameMcpToolExecution.Read(GameMcpWorldQuery.ListRows(
                    context,
                    request.Category,
                    request.Offset,
                    request.Limit,
                    request.AffordableOnly,
                    request.LimitFromCaller,
                    request.DiscoveredFilter).Freeze());
                return true;
            case "world_get":
                execution = GameMcpToolExecution.Read(
                    GameMcpWorldQuery.GetRows(
                        context,
                        request.Category,
                        request.Uuids).Freeze());
                return true;
            case "world_search":
                execution = GameMcpToolExecution.Read(
                    GameMcpWorldQuery.Search(
                        context,
                        request.Query,
                        request.Offset,
                        request.Limit,
                        request.Category,
                        request.StateFilter,
                        request.RunFilter,
                        request.KeywordFilter,
                        request.LimitFromCaller,
                        request.DiscoveredFilter,
                        request.ModifiesFilter).Freeze());
                return true;
            case "suite_health":
                execution = GameMcpToolExecution.Text(
                    GameMcpHealthProjection.ProjectGameMcpHealthText(context));
                return true;
            case "strategist":
                execution = GameMcpStrategistQuery.Read(context, request.Section);
                return true;
            case "suite_configuration":
                execution = GameMcpToolExecution.Read(
                    GameMcpConfigurationProjection.ProjectGameMcpConfiguration(
                        context, request.Mode == "describe", request.Section));
                return true;
            case "suite_breakers" when request.Mode == "list":
                execution = GameMcpToolExecution.Read(
                    GameMcpAutomationFeatures.ProjectGameMcpAutomationFeatures(context));
                return true;
            case "time_challenge" when request.Mode == "state":
                execution = GameMcpToolExecution.Read(
                    GameMcpWorldQuery.ChallengeStateRead(context).Freeze());
                return true;
            case "trace_health":
                execution = GameMcpToolExecution.Text(
                    GameMcpHealthProjection.ProjectGameMcpTraceHealthText(context));
                return true;
        }
        execution = null!;
        return false;
    }

    private bool TryExecuteGameMcpFrameOperation(
        GameMcpFrameOperation operation,
        GameMcpFrameContext context,
        out GameMcpToolExecution execution)
    {
        var request = operation.Request;
        if (TryExecuteFrameRead(request, context, out execution)) return true;
        switch (request.ToolName)
        {
            case "suite_check_game_math":
                execution = RunGameMcpGameMathCheck();
                return true;
            case "game_screen_catalog":
                execution = GameMcpToolExecution.Read(CaptureScreenCatalogGameMcp());
                return true;
            case "game_spell_loadout" when request.Mode == "preview":
                var glyphs = new SpellWorkbenchGlyphStack[request.UuidCounts.Length];
                for (var index = 0; index < glyphs.Length; index++)
                    glyphs[index] = new SpellWorkbenchGlyphStack(
                        request.UuidCounts[index].Uuid,
                        request.UuidCounts[index].Count);
                var previewRequest = new SpellWorkbenchLoadPreviewRequest(
                    request.Uuid,
                    _lifecycleGeneration,
                    glyphs);
                SpellWorkbenchLoadPreview preview;
                if (_serviceCycleActivation is null ||
                    !_serviceCycleActivation.TryPreviewSpellWorkbench(
                        in previewRequest,
                        out preview))
                {
                    preview = SpellWorkbenchLoadPreview.Refused(
                        SpellWorkbenchActionResultCodes.ContractUnavailable,
                        "The suite is not running on this screen, so nothing can be previewed.");
                }
                execution = GameMcpToolExecution.Read(
                    GameMcpSpellWorkbenchProjection.ProjectLoadPreview(
                        in preview));
                return true;
            case "game_spell_loadout" when request.Mode == "staged":
                SpellWorkbenchStagedLayout staged;
                if (_serviceCycleActivation is null ||
                    !_serviceCycleActivation.TryReadStagedSpellWorkbench(out staged))
                {
                    staged = SpellWorkbenchStagedLayout.Unavailable(
                        SpellWorkbenchActionResultCodes.ContractUnavailable,
                        "The suite is not running on this screen, so nothing can be read here.");
                }
                execution = GameMcpToolExecution.Read(
                    GameMcpSpellWorkbenchProjection.ProjectStagedLayout(in staged));
                return true;
            case "resource_read":
                execution = ExecuteGameMcpResource(request.ResourceUri, context);
                return true;
        }

        if (!TryPrepareGameMcpCommand(operation, context, out var command, out var failure))
        {
            execution = ProjectGameMcpCommand(command, failure);
            return true;
        }

        if (command.Kind is GameMcpCommandKind.ConfigurationSet or
            GameMcpCommandKind.AutomationSet or GameMcpCommandKind.EmergencyStop)
        {
            execution = ProjectGameMcpCommand(
                command,
                ExecuteAdministrativeGameMcp(command, context));
            return true;
        }
        if (command.Kind is >= GameMcpCommandKind.Screenshot and
            <= GameMcpCommandKind.ContinueRun or GameMcpCommandKind.Modal)
        {
            if (!TryExecuteGameMcpGadget(command, out var gadgetResult))
            {
                execution = null!;
                return false;
            }
            execution = ProjectGameMcpCommand(command, gadgetResult);
            return true;
        }
        if (_automataActionFamilyOwnership is null)
        {
            execution = ProjectGameMcpCommand(
                command,
                GameMcpCommandResult.Rejected(
                    "action_family_unavailable",
                    "the action-family ownership registry is unavailable"));
            return true;
        }
        if (!_automataActionFamilyOwnership.TryBeginGameMcpOperation(
                command.Kind,
                command.Mode,
                out var ownershipScope,
                out var ownershipReason))
        {
            execution = ProjectGameMcpCommand(
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
            if (_serviceCycleActivation is null ||
                !_serviceCycleActivation.TryExecuteGameMcp(command, out result))
            {
                result = GameMcpCommandResult.Failed(
                    "runtime_not_available",
                    "The suite is not running on this screen, so no action can be sent to the " +
                    "game.");
            }
        }
        if (string.Equals(result.Status, "committed", StringComparison.Ordinal))
        {
            if (!GameMcpCommandKinds.RequiresPostStateSettlement(command.Kind))
            {
                execution = ProjectGameMcpCommand(command, result);
                return true;
            }
            var actionCompletedAtUtcTicks = DateTime.UtcNow.Ticks;
            StartCoroutine(CompleteGameMcpGameplayPostState(
                command,
                result,
                context.World?.Generation.Value ?? 0,
                actionCompletedAtUtcTicks));
            execution = null!;
            return false;
        }
        execution = ProjectGameMcpCommand(command, result);
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
                latest = CaptureGameMcpFrameContext(
                    command.Kind == GameMcpCommandKind.Prestige
                        ? GameMcpFrameData.World | GameMcpFrameData.Scene
                        : GameMcpFrameData.World);
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
            state = GameMcpWorldQuery.ProjectGameplayPostState(
                latest!, command, committed);
        }
        else
        {
            state = GameMcpPostStateSettlement.TimedOut(command, latest);
        }
        CompleteGameMcpCommand(command, committed.WithSettledPostState(state));
    }

    private static EntityIdentityCatalogSnapshot EntityIdentities(
        GameMcpFrameContext context) =>
        context.World?.Snapshot.EntityIdentities ??
        EntityIdentityCatalogPublication.Current;

    private static GameMcpToolExecution ProjectGameMcpCommand(
        GameMcpCommand command,
        GameMcpCommandResult result)
    {
        if (command.Kind == GameMcpCommandKind.TooltipRead &&
            result.InlinePng is null &&
            string.Equals(result.Status, "committed", StringComparison.Ordinal) &&
            result.Details is GameMcpObject tooltip &&
            TryReadText(tooltip, out var text))
            return GameMcpToolExecution.Text(text);
        return new GameMcpToolExecution(
            result.Project(command),
            result.InlinePng,
            result.IsProtocolError);
    }

    private static bool TryReadText(GameMcpObject document, out string text)
    {
        for (var index = 0; index < document.Properties.Count; index++)
        {
            var property = document.Properties[index];
            if (property.Name == "text" && property.Value is GameMcpScalar scalar &&
                scalar.Value is string value && value.Length > 0)
            {
                text = value;
                return true;
            }
        }
        text = string.Empty;
        return false;
    }

    private GameMcpToolExecution ExecuteGameMcpResource(
        string uri,
        GameMcpFrameContext context)
    {
        if (uri == "orb://world/overview")
            return GameMcpToolExecution.Read(GameMcpWorldQuery.Overview(context).Freeze());
        if (uri == "orb://world/categories")
            return GameMcpToolExecution.Read(GameMcpWorldQuery.ListCategories(context).Freeze());
        if (uri == "orb://suite/health")
            return GameMcpToolExecution.Text(
                GameMcpHealthProjection.ProjectGameMcpHealthText(context));
        if (uri == "orb://suite/configuration")
            return GameMcpToolExecution.Read(
                GameMcpConfigurationProjection.ProjectGameMcpConfiguration(context, describe: false));
        if (uri == "orb://trace/health")
            return GameMcpToolExecution.Text(
                GameMcpHealthProjection.ProjectGameMcpTraceHealthText(context));
        var category = Uri.UnescapeDataString(
            uri.Substring("orb://world/category/".Length));
        return GameMcpToolExecution.Read(GameMcpWorldQuery.ListRows(
            context,
            category,
            0,
            GameMcpWorldQuery.DefaultLimit,
            limitFromCaller: false).Freeze());
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
    private GameMcpToolExecution RunGameMcpGameMathCheck()
    {
        // A no is a result. `isError` is reserved for a tool that failed before it produced a
        // domain answer, so both of these arrive the way every other refusal in the suite does —
        // a caller branches on the class, not on which of two transports carried it.
        if (_mathVerification is null)
        {
            return GameMcpToolExecution.Read(new GameMcpObjectBuilder
            {
                ["status"] = "unavailable",
                ["reasonCode"] = "contract_unavailable",
                ["reason"] = "The suite's game math check is not composed in this scene.",
            });
        }
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

    internal static bool TryPrepareGameMcpCommand(
        GameMcpFrameOperation operation,
        GameMcpFrameContext context,
        out GameMcpCommand command,
        out GameMcpCommandResult failure)
    {
        var request = operation.Request;
        var kind = GameMcpCommandKinds.FromRequest(
            request.ToolName,
            request.Mode,
            request.Key);

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
        else if (kind == GameMcpCommandKind.ScreenCatalog)
            mode = "catalog";
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

    /// <summary>
    /// The half of a configuration write that only the live game can settle. A declared range is
    /// checked against the setting; a range that is a game fact is checked against the world this
    /// frame published, so a write that would silence a feature is refused instead of stored.
    /// </summary>
    private static bool TryAdmitConfigurationWriteAgainstWorld(
        GameMcpCommand command,
        GameMcpFrameContext context,
        out GameMcpCommandResult failure)
    {
        if (GameMcpConfigurationValuePolicy.TryValidateAgainstWorld(
                command.Mode,
                command.PayloadKey,
                command.PayloadValue,
                context.World?.Snapshot,
                out var reason,
                out var bound))
        {
            failure = null!;
            return true;
        }
        failure = GameMcpCommandResult.Rejected(
            "configuration_write_rejected",
            reason,
            details: GameMcpConfigurationValuePolicy.RefusalFacts(command, in bound));
        return false;
    }

    private GameMcpCommandResult ExecuteAdministrativeGameMcp(
        GameMcpCommand command,
        GameMcpFrameContext context)
    {
        if (_configurationStore is null)
            return GameMcpCommandResult.Rejected(
                "configuration_not_available",
                GameMcpDecisionReason.For("configuration_not_available"));
        var expected = command.ExpectedConfigurationGeneration;
        var before = _configurationStore.CurrentGeneration;
        if (expected != before.Value)
        {
            return GameMcpCommandResult.Rejected(
                "stale_configuration_generation",
                GameMcpDecisionReason.For("stale_configuration_generation"),
                observedConfigurationGeneration: before.Value);
        }

        if (command.Kind == GameMcpCommandKind.ConfigurationSet)
        {
            if (GameMcpAutomationFeatures.IsBreakerSetting(command.Mode, command.PayloadKey))
                return GameMcpCommandResult.Rejected(
                    "wrong_configuration_surface",
                    GameMcpDecisionReason.For("wrong_configuration_surface"),
                    observedLifecycleGeneration: _lifecycleGeneration,
                    observedConfigurationGeneration: before.Value);
            // The publication the write is about to replace, held as the snapshot it is rather than
            // as text. Only an address the store admitted has a published value to spell, so the
            // pair is rendered on the committed path and a name that resolves to no setting is
            // refused by the store in its own words.
            var priorConfiguration = _configurationStore.Current;
            if (!TryAdmitConfigurationWriteAgainstWorld(command, context, out var worldFailure))
                return worldFailure;
            var written = _configurationStore.SetGameMcp(
                command.Mode,
                command.PayloadKey,
                command.PayloadValue,
                before,
                out var reason,
                out var bound);
            if (written == AutomataConfigurationWrite.Unconfirmed)
                return GameMcpCommandResult.Failed(
                    "configuration_write_unconfirmed",
                    GameMcpDecisionReason.For("configuration_write_unconfirmed"),
                    observedConfigurationGeneration:
                        _configurationStore.CurrentGeneration.Value);
            if (written == AutomataConfigurationWrite.Refused)
            {
                return GameMcpCommandResult.Rejected(
                    "configuration_write_rejected",
                    reason,
                    observedConfigurationGeneration:
                        _configurationStore.CurrentGeneration.Value,
                    details: GameMcpConfigurationValuePolicy.RefusalFacts(command, in bound));
            }
            return GameMcpCommandResult.Committed(
                "configuration_committed",
                observedLifecycleGeneration: _lifecycleGeneration,
                observedConfigurationGeneration:
                    _configurationStore.CurrentGeneration.Value,
                details: new GameMcpObjectBuilder
                {
                    ["setting"] = new GameMcpObjectBuilder
                    {
                        ["section"] = command.Mode,
                        ["key"] = command.PayloadKey,

                        // What a write changed is the pair, not the endpoint. A caller that reads
                        // only `value` cannot tell a committed change from a no-op it repeated.
                        ["value"] = new GameMcpObjectBuilder
                        {
                            ["before"] = GameMcpConfigurationSchema.SerializePublishedValue(
                                priorConfiguration,
                                command.Mode,
                                command.PayloadKey),
                            ["after"] = GameMcpConfigurationSchema.SerializePublishedValue(
                                _configurationStore.Current,
                                command.Mode,
                                command.PayloadKey),
                        },
                    },
                }.Freeze());
        }

        if (command.Kind == GameMcpCommandKind.AutomationSet)
        {
            if (!GameMcpAutomationFeatures.TryGet(command.PayloadKey, out var feature))
                return GameMcpCommandResult.Rejected(
                    "automation_feature_unknown",
                    "no automation feature is registered as " + command.PayloadKey);
            var requested = command.PayloadValue == "Active";
            var wasOn = feature.IsOn(_configurationStore.Current);
            if (wasOn == requested)
                return GameMcpCommandResult.Rejected(
                    "already_in_requested_state",
                    feature.DisplayName + " is already " + (requested ? "on" : "off"),
                    observedLifecycleGeneration: _lifecycleGeneration,
                    observedConfigurationGeneration: before.Value);
            var automationWrite = _configurationStore.SetGameMcp(
                feature.Section,
                feature.Key,
                command.PayloadValue,
                before,
                out var automationReason,
                out _);
            if (automationWrite == AutomataConfigurationWrite.Unconfirmed)
                return GameMcpCommandResult.Failed(
                    "configuration_write_unconfirmed",
                    GameMcpDecisionReason.For("configuration_write_unconfirmed"),
                    observedConfigurationGeneration:
                        _configurationStore.CurrentGeneration.Value);
            if (automationWrite == AutomataConfigurationWrite.Refused)
            {
                return GameMcpCommandResult.Rejected(
                    "configuration_write_rejected",
                    automationReason,
                    observedConfigurationGeneration:
                        _configurationStore.CurrentGeneration.Value);
            }
            return GameMcpCommandResult.Committed(
                "automation_committed",
                observedLifecycleGeneration: _lifecycleGeneration,
                observedConfigurationGeneration:
                    _configurationStore.CurrentGeneration.Value,
                details: GameMcpAutomationFeatures.ProjectGameMcpAutomationCommit(
                    feature,
                    wasOn,
                    _configurationStore.Current));
        }

        var engage = command.Mode == "engage";
        if (!engage && !_runtimeActivationAllowed)
        {
            return GameMcpCommandResult.Rejected(
                "runtime_activation_blocked",
                !_automaticSaveBackup.AllowsAutomation
                    ? "the emergency stop cannot resume while automatic save-backup failure blocks runtime activation"
                    : "the emergency stop cannot resume while exact-build compatibility blocks runtime activation",
                observedLifecycleGeneration: _lifecycleGeneration,
                observedConfigurationGeneration: before.Value);
        }
        if (_configurationStore.Current.Safety.EmergencyDisable == engage)
        {
            return GameMcpCommandResult.Rejected(
                "already_in_requested_state",
                engage
                    ? "the suite emergency stop is already engaged"
                    : "the suite emergency stop is already clear",
                observedLifecycleGeneration: _lifecycleGeneration,
                observedConfigurationGeneration: before.Value);
        }

        // Match the in-game control's order. Engaging synchronously cancels the host before another
        // queued command can run in this frame; resume is published and the host clears only through
        // its ordinary configured-stop pump and fresh-world gate.
        OnEmergencyStopChanged(engage);
        _configurationStore.SetEmergencyStop(engage);
        return GameMcpCommandResult.Committed(
            engage ? "emergency_stop_engaged" : "emergency_stop_resume_committed",
            observedLifecycleGeneration: _lifecycleGeneration,
            observedConfigurationGeneration:
                _configurationStore.CurrentGeneration.Value,
            details: new GameMcpObjectBuilder
            {
                ["emergencyStopEngaged"] = _configurationStore.Current.Safety.EmergencyDisable,
            }.Freeze());
    }

    private bool TryExecuteGameMcpGadget(
        GameMcpCommand command,
        out GameMcpCommandResult result)
    {
        var access = GameMcpGadgetPolicy.AccessFor(command.Kind);
        if (command.SaveCapture)
        {
            var admission = GameMcpScreenshotBudget.BeforeCapture(
                AutomataTraceRunRoot.Child("mcp-screenshots"));
            if (!admission.IsAvailable)
            {
                result = SavedScreenshotBudgetResult(in admission);
                return true;
            }
        }
        if (access == GameMcpGadgetAccess.Framebuffer)
        {
            StartCoroutine(CaptureGameMcpAtEndOfFrame(
                command,
                GadgetCommitted(
                    "screenshot_captured",
                    new GameMcpObjectBuilder())));
            result = null!;
            return false;
        }
        if (access == GameMcpGadgetAccess.Navigation)
        {
            StartCoroutine(NavigateGameMcpAcrossFrames(command));
            result = null!;
            return false;
        }
        if (access == GameMcpGadgetAccess.ContinueRun)
        {
            result = ContinueRunGameMcp();
            if (string.Equals(result.Status, "committed", StringComparison.Ordinal))
            {
                StartCoroutine(CompleteContinueRunGameMcp(command, result));
                return false;
            }
            return true;
        }
        if (access == GameMcpGadgetAccess.Modal)
        {
            if (string.Equals(command.Mode, "open", StringComparison.Ordinal))
            {
                result = OpenModalGameMcp(command);
                return true;
            }
            if (_modalDismissGameAction is null)
            {
                result = GadgetRejected("contract_unavailable",
                    "The native modal close control is unavailable.");
                return true;
            }
            var submission = _modalDismissGameAction.Submit();
            if (submission.Faulted)
            {
                result = GadgetFaulted(submission.Code, submission.Reason);
                return true;
            }
            if (!submission.Committed)
            {
                result = GadgetRejected(submission.Code, submission.Reason);
                return true;
            }
            StartCoroutine(CompleteModalDismissGameMcp(command, submission.Title));
            result = null!;
            return false;
        }

        result = access switch
        {
            GameMcpGadgetAccess.Probe => ProbeGameMcp(command),
            GameMcpGadgetAccess.ScreenCatalog => throw new InvalidOperationException(
                "the screen catalog is executed as a text read before gadget dispatch"),
            GameMcpGadgetAccess.TooltipCatalog => CaptureTooltipCatalogGameMcp(command),
            GameMcpGadgetAccess.TooltipRead => ReadTooltipGameMcp(command),
            GameMcpGadgetAccess.ContinueRun => throw new InvalidOperationException(
                "Continue is completed after its scene transition"),
            _ => throw new InvalidOperationException(
                "the request-time MCP gadget mapping is incomplete"),
        };
        return true;
    }

    /// <summary>
    /// Put one of the chrome's panels on the screen, named the way the panel names itself.
    /// </summary>
    /// <remarks>
    /// The press lands inside the frame: the game's own open sets the panel's open flag before it
    /// starts the animation that makes it visible, so the sentinel is read straight away and the
    /// caller is not told a press landed that a later frame could still refuse. A different panel
    /// already covering the board is the one refusal that has to come first — the chrome beneath a
    /// modal takes no clicks, so pressing it would answer "opened" over a screen that never moved.
    /// </remarks>
    private GameMcpCommandResult OpenModalGameMcp(GameMcpCommand command)
    {
        if (_modalOpenGameAction is null)
            return GadgetRejected("contract_unavailable", ModalOpenGameAction.NoActivator);
        var requested = command.PayloadValue;
        if (!TryReadOpenModalTitles(out var openBefore, out var openFailure))
            return GadgetRejected("contract_unavailable", openFailure);
        if (openBefore.Length > 0)
        {
            if (Array.IndexOf(openBefore, requested) >= 0)
                return GadgetCommitted("modal_opened", OpenedModal(requested));
            return GadgetRejected(
                "modal_already_open",
                "The " + (openBefore[0].Length == 0 ? "open" : openBefore[0]) +
                " panel is covering the board, and the chrome beneath it takes no presses; " +
                "dismiss it first.");
        }
        if (!_modalOpenGameAction.TryReadActivators(out var activators, out var readFailure))
            return GadgetRejected("contract_unavailable", readFailure);
        var submission = _modalOpenGameAction.Submit(requested, activators);
        if (submission.Faulted)
            return GadgetFaulted(submission.Code, submission.Reason);
        if (!submission.Committed)
            return GadgetRejected(submission.Code, submission.Reason);
        return GadgetCommitted("modal_opened", OpenedModal(submission.Title));
    }

    /// <summary>The open's own post-state: the name of the panel now covering the board.</summary>
    private static GameMcpObjectBuilder OpenedModal(string title) =>
        new() { ["opened"] = title };

    private bool TryReadOpenModalTitles(out string[] titles, out string reason)
    {
        titles = Array.Empty<string>();
        reason = string.Empty;
        if (_modalDismissGameAction is null)
        {
            reason = "The open panels cannot be read on this build, so no panel can be opened " +
                "without guessing at what it did.";
            return false;
        }
        return _modalDismissGameAction.TryReadOpenModals(out titles, out reason);
    }

    private IEnumerator CompleteModalDismissGameMcp(GameMcpCommand command, string title)
    {
        var deadline = Time.realtimeSinceStartup + GameMcpPostStateSettlement.MaximumWaitSeconds;
        while (Time.realtimeSinceStartup < deadline)
        {
            yield return null;
            var dismissed = false;
            var reason = string.Empty;
            if (_modalDismissGameAction is null ||
                !_modalDismissGameAction.TryObserveDismissed(out dismissed, out reason))
            {
                CompleteGameMcpCommand(command, GameMcpCommandResult.Faulted(
                    "modal_state_unavailable",
                    reason.Length == 0 ? "The modal settled state is unavailable." : reason));
                yield break;
            }
            if (!dismissed) continue;
            // Which modal went away. A zero-byte body left the caller with nothing to compare
            // against the screen, and a screenshot was the only way to learn the press had landed;
            // the title was in hand the whole time, read off the control before it closed.
            CompleteGameMcpCommand(command, GadgetCommitted(
                "modal_dismissed",
                DismissedModal(title)));
            yield break;
        }
        var timedOut = DismissedModal(title);
        timedOut["postStateUnavailable"] = new GameMcpObjectBuilder
        {
            ["reasonCode"] = "post_state_timeout",
            ["reason"] = "the modal began closing but remained open after one second",
        }.Freeze();
        CompleteGameMcpCommand(command, GadgetCommitted("modal_dismissed", timedOut));
    }

    /// <summary>
    /// The dismissal's own post-state: the name of the modal that closed. An untitled modal has no
    /// name to publish, and absence says so rather than an empty string pretending to be one.
    /// </summary>
    private static GameMcpObjectBuilder DismissedModal(string title)
    {
        var details = new GameMcpObjectBuilder();
        if (!string.IsNullOrWhiteSpace(title)) details["dismissed"] = title;
        return details;
    }

    private GameMcpCommandResult ContinueRunGameMcp()
    {
        if (!string.Equals(
                SceneManager.GetActiveScene().name,
                "Start",
                StringComparison.Ordinal))
        {
            return GameMcpCommandResult.Rejected(
                "continue_wrong_scene",
                "Continue is only on the game's title screen; you are already in a run.",
                observedLifecycleGeneration: _lifecycleGeneration,
                observedConfigurationGeneration:
                    _configurationStore?.CurrentGeneration.Value ?? 0);
        }

        var managerType = AccessTools.TypeByName("SaveStateManager");
        var manager = managerType is null
            ? null
            : Resources.FindObjectsOfTypeAll(managerType).FirstOrDefault();
        var startGame = AccessTools.Method("SaveStateManager:StartGame");
        if (manager is null || startGame is null)
        {
            return GameMcpCommandResult.Failed(
                "continue_contract_unavailable",
                "This build does not expose the Continue button, so a save cannot be started " +
                "from here.",
                observedLifecycleGeneration: _lifecycleGeneration,
                observedConfigurationGeneration:
                    _configurationStore?.CurrentGeneration.Value ?? 0);
        }

        startGame.Invoke(manager, Array.Empty<object>());
        return GadgetCommitted(
            "continue_invoked",
            new GameMcpObjectBuilder());
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
            Logger.LogWarning(
                "Game MCP could not normalize the agent-required game settings: " + settingsFailure);
        }

        CompleteGameMcpCommand(command, committed.WithDetails(
            GameMcpContinueProjection.Project(state.SceneName, state.RuntimeAvailable)));
    }

    private IEnumerator CaptureGameMcpAtEndOfFrame(
        GameMcpCommand command,
        GameMcpCommandResult baseResult)
    {
        yield return new WaitForEndOfFrame();
        Texture2D? texture = null;
        Texture2D? encodedTexture = null;
        try
        {
            texture = ScreenCapture.CaptureScreenshotAsTexture();
            if (texture is null)
                throw new InvalidOperationException(
                    "ScreenCapture.CaptureScreenshotAsTexture returned null");
            encodedTexture = DownscaleScreenshot(
                texture, GameMcpGadgetPolicy.CaptureWidth);
            var png = encodedTexture.EncodeToPNG();
            if (png is null || png.Length == 0)
                throw new InvalidOperationException("Texture2D.EncodeToPNG returned no bytes");
            var details = new GameMcpObjectBuilder();
            if (baseResult.Details is GameMcpObject existingDetails)
                details.CopyFrom(existingDetails);
            details["width"] = encodedTexture.width;
            details["height"] = encodedTexture.height;
            details["scene"] = SceneManager.GetActiveScene().name;
            if (_uiShell is not null && _uiShell.IsAlive)
            {
                var tabs = _uiShell.CaptureNativeTabsForGameMcp();
                var activeTab = tabs.FirstOrDefault(tab => tab.Active);
                if (!string.IsNullOrWhiteSpace(activeTab.Label))
                    details["activeScreen"] = activeTab.Label;
            }
            AppendOpenModals(details);
            if (command.SaveCapture)
            {
                var directory = AutomataTraceRunRoot.Child("mcp-screenshots");
                var admission = GameMcpScreenshotBudget.BeforeCommit(directory, png.LongLength);
                if (!admission.IsAvailable)
                {
                    CompleteGameMcpCommand(command, SavedScreenshotBudgetResult(in admission));
                    yield break;
                }
                System.IO.Directory.CreateDirectory(directory);
                var name = "mcp-" +
                    DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffffZ") +
                    "-" + command.Sequence + ".png";
                var path = System.IO.Path.Combine(directory, name);
                using (var stream = new System.IO.FileStream(
                           path,
                           System.IO.FileMode.CreateNew,
                           System.IO.FileAccess.Write,
                           System.IO.FileShare.Read))
                {
                    stream.Write(png, 0, png.Length);
                }
                details["savedRelativePath"] =
                    AutomataTraceRunRoot.FormatRelativePath("mcp-screenshots/" + name);
            }
            CompleteGameMcpCommand(
                command,
                baseResult.WithInlinePng(details.Freeze(), png));
        }
        catch (Exception exception)
        {
            CompleteGameMcpCommand(
                command,
                GameMcpCommandResult.Faulted(
                    "inline_screenshot_failed",
                    "server defect: the end-of-frame screenshot did not finish." +
                    GameActionFaultLog.Record(exception, "the screen being captured"),
                    observedLifecycleGeneration: _lifecycleGeneration,
                    observedConfigurationGeneration:
                        _configurationStore?.CurrentGeneration.Value ?? 0));
        }
        finally
        {
            if (encodedTexture is not null && !ReferenceEquals(encodedTexture, texture))
                Destroy(encodedTexture);
            if (texture is not null) Destroy(texture);
        }
    }

    private GameMcpCommandResult SavedScreenshotBudgetResult(
        in GameMcpScreenshotBudgetAdmission admission)
    {
        var lifecycle = _lifecycleGeneration;
        var configuration = _configurationStore?.CurrentGeneration.Value ?? 0;
        return admission.Status == GameMcpScreenshotBudgetStatus.StorageUnavailable
            ? GameMcpCommandResult.Faulted(
                "screenshot_budget_unavailable",
                "server defect: " + admission.Reason,
                observedLifecycleGeneration: lifecycle,
                observedConfigurationGeneration: configuration)
            : GameMcpCommandResult.Rejected(
                "screenshot_budget_reached",
                admission.Reason,
                observedLifecycleGeneration: lifecycle,
                observedConfigurationGeneration: configuration);
    }

    private static Texture2D DownscaleScreenshot(Texture2D source, int maxWidth)
    {
        if (source.width <= maxWidth) return source;
        var width = maxWidth;
        var height = Math.Max(1, (int)Math.Round(
            source.height * (double)width / source.width,
            MidpointRounding.AwayFromZero));
        var result = new Texture2D(width, height);
        for (var y = 0; y < height; y++)
        {
            var v = height == 1 ? 0f : y / (float)(height - 1);
            for (var x = 0; x < width; x++)
            {
                var u = width == 1 ? 0f : x / (float)(width - 1);
                result.SetPixel(x, y, source.GetPixelBilinear(u, v));
            }
        }
        result.Apply(updateMipmaps: false, makeNoLongerReadable: false);
        return result;
    }

    private GameMcpValue CaptureScreenCatalogGameMcp()
    {
        var scene = SceneManager.GetActiveScene().name;
        if (scene != "Main" || _uiShell is null || !_uiShell.IsAlive)
            return GameMcpScreenCatalogProjection.ProjectGameMcpScreenCatalog(
                scene,
                navigationAvailable: false,
                Array.Empty<(string Label, bool Active, bool Locked)>(),
                Array.Empty<(string Strip, string Label, bool Active)>());
        var tabs = _uiShell.CaptureNativeTabsForGameMcp();
        var subtabs = CaptureSubtabs();
        return GameMcpScreenCatalogProjection.ProjectGameMcpScreenCatalog(
            scene,
            navigationAvailable: true,
            tabs.Select(tab => (tab.Label, tab.Active, tab.Locked)).ToArray(),
            subtabs.Select(subtab => (subtab.StripKey, subtab.Label, subtab.Active)).ToArray());
    }

    /// <summary>
    /// Names whatever native modal is covering the board. A screen read that stayed silent about
    /// an open Settings panel is what made a covered board look like a working one.
    /// </summary>
    private void AppendOpenModals(GameMcpObjectBuilder details)
    {
        if (_modalDismissGameAction is null)
        {
            details["openModalsUnavailable"] =
                "The native modal contracts were not composed.";
            return;
        }
        if (!_modalDismissGameAction.TryReadOpenModals(out var titles, out var reason))
        {
            details["openModalsUnavailable"] =
                reason.Length == 0 ? "The open modal titles are unavailable." : reason;
            return;
        }
        if (titles.Length == 0) return;
        var open = new GameMcpArrayBuilder();
        foreach (var title in titles) open.Add(title);
        details["openModals"] = open;
    }

    private bool TryBeginNavigateGameMcp(
        GameMcpCommand command,
        out GameMcpNavigationSelector? subtabSelector,
        out GameMcpObjectBuilder details,
        out GameMcpCommandResult failure)
    {
        subtabSelector = null;
        details = new GameMcpObjectBuilder();
        failure = null!;
        var scene = SceneManager.GetActiveScene().name;
        if (scene != "Main" || _uiShell is null || !_uiShell.IsAlive)
        {
            failure = GadgetRejected(
                "native_navigation_unavailable",
                "The game is not showing its screen tabs right now — this answers only while a " +
                "save is open.");
            return false;
        }

        var request = command.SourceOperation?.Request;
        if (request?.Tab is null)
        {
            failure = GadgetFailed(
                "navigation_request_invalid",
                "The suite sent a navigation that named no screen, so nothing was applied.");
            return false;
        }
        if (command.TargetId != Guid.Empty && TileOf(command) == GameMcpTile.None)
        {
            // One spelling for one destination: the catalog path this tool takes, the same way
            // every screen cell on the surface prints it.
            var requestedDestination = request.Subtab is null
                ? request.Tab.Label
                : request.Tab.Label + "/" + request.Subtab.Label;
            failure = GadgetRejected(
                "tile_destination_mismatch",
                "A tile can be picked on the page that draws it — " +
                GameMcpGadgetPolicy.TileDestinations + " — not on " + requestedDestination);
            return false;
        }
        subtabSelector = request.Subtab;
        var tabs = _uiShell.CaptureNativeTabsForGameMcp();
        if (!TryResolveTabSelector(request.Tab, tabs, out var tab, out var tabReason))
        {
            // A locked screen is not a candidate. Offering the five a young run does not draw is
            // how a caller spends a call arriving somewhere the game will not show it.
            failure = NavigationRefusal(
                "screen_match_failed",
                tabReason,
                null,
                "screenCandidates",
                tabs.Where(candidate => !candidate.Locked).Select(candidate => candidate.Label));
            return false;
        }
        if (tab.Locked)
        {
            failure = GadgetRejected(
                "screen_locked",
                tab.Label + " is not unlocked yet, so the game draws no such screen.");
            return false;
        }
        if (!_uiShell.TrySelectNativeTabForGameMcp(tab.Index, out var selectReason))
        {
            failure = GadgetRejected("native_tab_rejected", selectReason);
            return false;
        }

        details = new GameMcpObjectBuilder { ["activeScreen"] = tab.Label };
        return true;
    }

    private IEnumerator NavigateGameMcpAcrossFrames(GameMcpCommand command)
    {
        if (!TryBeginNavigateGameMcp(
                command,
                out var subtabSelector,
                out var details,
                out var failure))
        {
            CompleteGameMcpCommand(command, failure);
            yield break;
        }
        // Native tab selection changes the active content hierarchy over the following frames.
        // Resolving a subtab against a half-built hierarchy is what made one screen's strip match a
        // name that belongs to another: the candidates the matcher searched were not the ones the
        // catalog advertises for the screen the caller asked for.
        var settledScreen = new bool[1];
        yield return SettleNavigation(settledScreen);

        if (subtabSelector is not null)
        {
            var segments = subtabSelector.Labels;
            for (var step = 0; step < segments.Count; step++)
            {
                // A strip appears only once its parent is active, and the parent's click rebuilds
                // the content area over the following frames. Settling between the clicks is what
                // makes the child's strip present to match against — the same reason the screen
                // settles above, one level down.
                if (step > 0) yield return SettleNavigation(settledScreen);
                var subtabs = CaptureSubtabs();
                if (!TryResolveSubtabSelector(
                        segments[step],
                        subtabs,
                        out var subtab,
                        out var subtabReason))
                {
                    yield return CompleteNavigateGameMcpAfterSettlement(
                        command,
                        NavigationRefusal(
                            "subtab_match_failed",
                            SubtabRefusalReason(
                                subtabReason,
                                settledScreen[0],
                                segments.Count > 1 ? subtabSelector.Label : null),
                            details,
                            "subtabCandidates",
                            subtabs.Select(candidate => candidate.Label)));
                    yield break;
                }
                if (!subtab.TrySelect(out var selectionReason))
                {
                    yield return CompleteNavigateGameMcpAfterSettlement(
                        command,
                        GadgetRejected("subtab_selection_failed", selectionReason)
                            .WithDetails(details.Freeze()));
                    yield break;
                }
                yield return null;
            }
        }
        if (command.TargetId != Guid.Empty)
        {
            // The tile pick used to run one frame after the last subtab click, with the destination
            // still assembling — the same unsettled read that let a departed screen's strip ride
            // along, one surface over. The tiles are drawn by that same rebuild, and
            // `subtab.TrySelect` presses the tab whether or not it was already active, so being on
            // the page already did not help: every pick read a page mid-rebuild.
            yield return SettleNavigation(settledScreen);
            var scene = SceneManager.GetActiveScene().name;
            var tileResult = TileOf(command) == GameMcpTile.Spell
                ? NavigateExactSpell(command.TargetId, scene)
                : NavigateExactPlot(command.TargetId, scene);
            if (!string.Equals(tileResult.Status, "committed", StringComparison.Ordinal))
            {
                yield return CompleteNavigateGameMcpAfterSettlement(
                    command,
                    tileResult.WithDetails(details.Freeze()));
                yield break;
            }
        }
        // The strips are read once, after arrival settles, in one place. Reading them here — a
        // single frame after the subtab click, with the destination still assembling — is what let
        // the departed screen's strip ride along and made identical navigations disagree.
        var result = GadgetCommitted(
            "navigation_arrived",
            details);
        yield return CompleteNavigateGameMcpAfterSettlement(command, result);
    }

    /// <summary>
    /// Runs until the navigation shell reports the same screen and strips two frames running, or
    /// until one second has passed. <paramref name="settled"/>'s single slot records which it was.
    /// </summary>
    private IEnumerator SettleNavigation(bool[] settled)
    {
        var deadline = Time.realtimeSinceStartup + 1f;
        var stableFrames = 0;
        string? previous = null;
        while (Time.realtimeSinceStartup < deadline && stableFrames < 2)
        {
            yield return null;
            var current = NavigationSettlementSignature();
            stableFrames = string.Equals(previous, current, StringComparison.Ordinal)
                ? stableFrames + 1
                : 0;
            previous = current;
        }
        settled[0] = stableFrames >= 2;
    }

    private IEnumerator CompleteNavigateGameMcpAfterSettlement(
        GameMcpCommand command,
        GameMcpCommandResult result)
    {
        var settled = new bool[1];
        yield return SettleNavigation(settled);
        if (string.Equals(result.Status, "committed", StringComparison.Ordinal) &&
            !settled[0])
        {
            result = GadgetCommitted(
                "navigation_arrived",
                new GameMcpObjectBuilder
                {
                    ["postStateUnavailable"] = new GameMcpObjectBuilder
                    {
                        ["reasonCode"] = "post_state_timeout",
                        ["reason"] = "the destination did not settle within one second",
                    }.Freeze(),
                });
        }
        else if (string.Equals(result.Status, "committed", StringComparison.Ordinal) &&
                 (_uiShell is null || !_uiShell.IsAlive))
        {
            result = GadgetCommitted(
                "navigation_arrived",
                new GameMcpObjectBuilder
                {
                    ["scene"] = SceneManager.GetActiveScene().name,
                    ["subtabStripsUnavailable"] =
                        "The navigation shell was no longer alive after the destination settled.",
                });
        }
        else if (string.Equals(result.Status, "committed", StringComparison.Ordinal))
        {
            var tabs = _uiShell!.CaptureNativeTabsForGameMcp();
            var activeTab = tabs.FirstOrDefault(tab => tab.Active);
            var subtabs = CaptureSubtabs();
            var details = new GameMcpObjectBuilder
            {
                ["scene"] = SceneManager.GetActiveScene().name,
                ["subtabStrips"] = GameMcpScreenCatalogProjection.ProjectGameMcpSubtabStrips(
                    subtabs.Select(value =>
                        (value.StripKey, value.Label, value.Active))),
            };
            if (!string.IsNullOrWhiteSpace(activeTab.Label))
                details["activeScreen"] = activeTab.Label;
            if (command.TargetId != Guid.Empty)
            {
                details[TileOf(command) == GameMcpTile.Spell ? "selectedSpell" : "selectedPlot"] =
                    command.TargetId.ToString("D");
            }
            AppendOpenModals(details);
            result = GadgetCommitted("navigation_arrived", details);
        }
        CompleteGameMcpCommand(command, result);
    }

    private string NavigationSettlementSignature()
    {
        var values = CaptureSubtabs();
        var activeTab = _uiShell is not null && _uiShell.IsAlive
            ? _uiShell.CaptureNativeTabsForGameMcp().FirstOrDefault(tab => tab.Active).Label
            : string.Empty;
        return SceneManager.GetActiveScene().name + "|" + activeTab + "|" +
            string.Join("|", values.Select(value =>
                value.StripKey + ":" + value.Label + ":" + (value.Active ? "1" : "0")));
    }

    private static GameMcpTile TileOf(GameMcpCommand command)
    {
        var request = command.SourceOperation?.Request;
        return request?.Tab is null
            ? GameMcpTile.None
            : GameMcpGadgetPolicy.TileAt(request.Tab.Label, request.Subtab?.Leaf);
    }

    /// <summary>
    /// Opens one spell's card on Magic > Spellbook > Spells, the way clicking its icon does.
    /// </summary>
    /// <remarks>
    /// <c>UISpellRecipeList.ClickRecipe</c> toggles the page's selected-recipe variable, so a click
    /// on the spell whose card is already open closes it. The card is the variable, so the click is
    /// made only when the variable does not already hold this spell, and the variable holding it
    /// afterwards is the postcondition.
    /// </remarks>
    private GameMcpCommandResult NavigateExactSpell(Guid stableUuid, string scene)
    {
        if (scene != "Main")
        {
            return GadgetRejected(
                "wrong_scene",
                "Spells can only be picked while a save is open.");
        }

        var recipeType = AccessTools.TypeByName("SpellRecipeSO");
        var listType = AccessTools.TypeByName("UISpellRecipeList");
        var selectedField = listType?.GetField(
            "selectedSpellRecipe", BindingFlags.Instance | BindingFlags.Public);
        var clickRecipe = listType?.GetMethod(
            "ClickRecipe",
            BindingFlags.Instance | BindingFlags.Public,
            binder: null,
            types: recipeType is null ? Type.EmptyTypes : new[] { recipeType },
            modifiers: null);
        var isItem = recipeType is null
            ? null
            : selectedField?.FieldType.GetMethod(
                "IsItem",
                BindingFlags.Instance | BindingFlags.Public,
                binder: null,
                types: new[] { recipeType },
                modifiers: null);
        if (recipeType is null || listType is null || selectedField is null || clickRecipe is null ||
            clickRecipe.ReturnType != typeof(void) || isItem is null ||
            isItem.ReturnType != typeof(bool))
        {
            return GadgetRejected(
                "native_spell_navigation_unavailable",
                "This build does not expose the spell list, so a spell cannot be selected.");
        }

        var recipe = TypedRegistryResolver.Shared.Resolve(stableUuid, recipeType);
        if (!recipe.IsResolved)
        {
            return GadgetRejected(
                "native_spell_not_resolved",
                "No spell in this run carries that id; world_list category=spell-recipes names " +
                "the ones it does.");
        }

        var lists = Resources.FindObjectsOfTypeAll(listType)
            .OfType<MonoBehaviour>()
            .Where(list =>
                list.enabled && list.gameObject.activeInHierarchy &&
                GameMcpTooltipNativeAccess.OnScreen(list) &&
                selectedField.GetValue(list) is not null)
            .ToArray();
        if (lists.Length != 1)
        {
            return GadgetRejected(
                "native_spell_list_unavailable",
                "The game is showing " + (lists.Length == 0 ? "no spell list" : "more than " +
                "one spell list") + " right now, so which spell card was meant is unclear.");
        }

        var selection = selectedField.GetValue(lists[0]);
        var arguments = new[] { recipe.Value };
        if (isItem.Invoke(selection, arguments) is not true)
            clickRecipe.Invoke(lists[0], arguments);
        if (isItem.Invoke(selection, arguments) is not true)
        {
            return GadgetFailed(
                "verification_failed",
                "The spell was clicked and its card did not open, so the suite cannot say what " +
                "the Spells page shows.");
        }
        return GadgetCommitted(
            "navigation_invoked",
            new GameMcpObjectBuilder
            {
                ["spellUuid"] = stableUuid.ToString("D"),
                ["nativeMethod"] = "UISpellRecipeList.ClickRecipe",
                ["sceneBefore"] = scene,
            });
    }

    private GameMcpCommandResult NavigateExactPlot(
        Guid stableUuid,
        string scene)
    {
        if (scene != "Main")
        {
            return GadgetRejected(
                "wrong_scene",
                "Plots can only be picked while a save is open.");
        }

        const string plotNativeType = "PlotNodeSO";
        var plotType = AccessTools.TypeByName(plotNativeType);
        var listType = AccessTools.TypeByName("UIPlotNodeList");
        if (plotType is null || listType is null)
        {
            return GadgetRejected(
                "native_plot_navigation_unavailable",
                "This build does not expose the plot list, so a plot cannot be selected.");
        }

        var plot = TypedRegistryResolver.Shared.Resolve(stableUuid, plotType);
        if (!plot.IsResolved)
        {
            return GadgetRejected(
                "native_plot_not_resolved",
                "No plot in this run carries that id; page World > Agromancy for the plots it " +
                "does draw.");
        }

        var activeLists = Resources.FindObjectsOfTypeAll(listType)
            .OfType<MonoBehaviour>()
            .Where(list => list.enabled && list.gameObject.activeInHierarchy)
            .ToArray();
        if (activeLists.Length != 1)
        {
            return GadgetRejected(
                "native_plot_list_unavailable",
                "The game is showing " + (activeLists.Length == 0 ? "no plot list" : "more than " +
                "one plot list") + " right now, so which plot was meant is unclear.");
        }

        var onNodeClick = listType.GetMethod(
            "OnNodeClick",
            BindingFlags.Instance | BindingFlags.Public,
            binder: null,
            types: new[] { plotType },
            modifiers: null);
        if (onNodeClick is null || onNodeClick.ReturnType != typeof(void))
        {
            return GadgetRejected(
                "native_plot_navigation_unavailable",
                "This build does not expose the plot list, so a plot cannot be selected.");
        }

        onNodeClick.Invoke(activeLists[0], new[] { plot.Value });
        return GadgetCommitted(
            "navigation_invoked",
            new GameMcpObjectBuilder
            {
                ["plotUuid"] = stableUuid.ToString("D"),
                ["nativeMethod"] = "UIPlotNodeList.OnNodeClick",
                ["sceneBefore"] = scene,
            });
    }

    private IReadOnlyList<GameMcpSubtab> CaptureSubtabs()
    {
        if (_uiShell is null || !_uiShell.IsAlive)
            return Array.Empty<GameMcpSubtab>();
        if (_uiShell.IsOpenForGameMcp)
        {
            var pages = _uiShell.CapturePagesForGameMcp();
            var suiteResult = new GameMcpSubtab[pages.Count];
            for (var index = 0; index < pages.Count; index++)
            {
                var pageIndex = index;
                suiteResult[index] = new GameMcpSubtab(
                    index,
                    pages[index],
                    "Mods/Page[" + index + "]",
                    "Mods",
                    index == _uiShell.SelectedPageIndexForGameMcp,
                    () => _uiShell.TrySelectPageForGameMcp(pageIndex, out var reason)
                        ? string.Empty
                        : reason);
            }
            return suiteResult;
        }
        var viewRadioType = AccessTools.TypeByName("UIViewRadioButton");
        if (viewRadioType is null) return Array.Empty<GameMcpSubtab>();
        var candidates = Resources.FindObjectsOfTypeAll(viewRadioType)
            .OfType<Component>()
            .Where(component =>
                component.gameObject.activeInHierarchy &&
                // Liveness is not membership. This game leaves a departed screen's strip alive in
                // the hierarchy, so the same predicate the tooltip catalog uses asks the game
                // whether the group this button hangs on is the one being drawn — otherwise Time's
                // strip publishes itself on Magic, and settlement, which is built from this very
                // capture, finds a stale strip perfectly stable and declares arrival on it.
                GameMcpTooltipNativeAccess.OnScreen(component) &&
                !_uiShell.IsNativeTabForGameMcp(component))
            .Select(component => new
            {
                Component = component,
                Button = component.GetComponent<Button>(),
                Label = component.GetComponentInChildren<TextMeshProUGUI>(includeInactive: true),
                Path = NativeObjectPath.BuildIndexed(component),
            })
            .Where(candidate =>
                candidate.Button is not null &&
                candidate.Button.enabled &&
                candidate.Button.interactable &&
                candidate.Label is not null &&
                GameMcpGadgetPolicy.IsCurrentContentSubtabPath(candidate.Path))
            .OrderBy(candidate => candidate.Path, StringComparer.Ordinal)
            .ToArray();
        var result = new GameMcpSubtab[candidates.Length];
        for (var index = 0; index < candidates.Length; index++)
        {
            var candidate = candidates[index];
            result[index] = new GameMcpSubtab(
                index,
                candidate.Label!.text?.Trim() ?? string.Empty,
                candidate.Path,
                ParentPath(candidate.Path),
                NativeObjectLiveness.IsAlive(NativeViewAdapter.ReadView(candidate.Component)) &&
                    NativeViewAdapter.IsActive(NativeViewAdapter.ReadView(candidate.Component)!),
                () =>
                {
                    candidate.Button!.onClick.Invoke();
                    return string.Empty;
                });
        }
        return result;
    }

    private static GameMcpArrayBuilder ProjectSubtabStrips(
        IReadOnlyList<GameMcpSubtab> subtabs)
    {
        var strips = new GameMcpArrayBuilder();
        foreach (var group in subtabs.GroupBy(subtab => subtab.StripKey, StringComparer.Ordinal))
        {
            var labels = new GameMcpArrayBuilder();
            var strip = new GameMcpObjectBuilder();
            var firstLabel = string.Empty;
            foreach (var subtab in group)
            {
                if (firstLabel.Length == 0) firstLabel = subtab.Label;
                labels.Add(subtab.Label);
                if (subtab.Active) strip["active"] = subtab.Label;
            }
            strip["labels"] = labels;
            strips.Add(strip);
        }
        return strips;
    }

    private static string ParentPath(string path)
    {
        var separator = path.LastIndexOf('/');
        return separator <= 0 ? path : path.Substring(0, separator);
    }

    private static bool TryResolveTabSelector(
        GameMcpNavigationSelector? selector,
        IReadOnlyList<GameMcpNativeTab> entries,
        out GameMcpNativeTab selected,
        out string reason)
    {
        if (selector is null)
        {
            selected = default;
            reason = "tab selector is absent";
            return false;
        }
        if (selector.Label.Length > 0)
        {
            var requested = selector.Label;
            var matches = new List<GameMcpNativeTab>();
            for (var index = 0; index < entries.Count; index++)
                if (string.Equals(entries[index].Label, requested, StringComparison.Ordinal))
                    matches.Add(entries[index]);
            if (matches.Count == 1)
            {
                selected = matches[0];
                reason = string.Empty;
                return true;
            }
            selected = default;
            reason = "exact tab name '" + requested + "' matched " +
                matches.Count + " live catalog entries";
            return false;
        }
        selected = default;
        reason = "tab name is empty";
        return false;
    }

    private static bool TryResolveSubtabSelector(
        string requested,
        IReadOnlyList<GameMcpSubtab> entries,
        out GameMcpSubtab selected,
        out string reason)
    {
        var matches = new List<GameMcpSubtab>();
        if (requested.Length > 0)
        {
            for (var index = 0; index < entries.Count; index++)
                if (string.Equals(entries[index].Label, requested, StringComparison.Ordinal))
                    matches.Add(entries[index]);
            reason = "exact subtab name '" + requested + "'";
        }
        else
        {
            selected = null!;
            reason = "subtab name is empty";
            return false;
        }
        if (matches.Count == 1)
        {
            selected = matches[0];
            reason = string.Empty;
            return true;
        }
        selected = null!;
        reason += " matched " + matches.Count + " live catalog entries";
        return false;
    }

    private GameMcpCommandResult CaptureTooltipCatalogGameMcp(GameMcpCommand command)
    {
        var nativeAccess = _gameMcpTooltipNativeAccess;
        if (nativeAccess is null)
        {
            return TooltipsUnreadableBecause(_gameMcpTooltipContractFailure);
        }
        if (!TryCaptureScreenElements(out var entries, out var chromeFailure))
        {
            return TooltipsUnreadableBecause(chromeFailure);
        }
        if (!int.TryParse(
                command.PayloadValue,
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out var offset))
        {
            return GadgetRejected(
                "tooltip_offset_invalid",
                "That page marker is not one game_screen_elements printed; page it again — the " +
                "markers change when the screen does.");
        }
        // A page is a stretch of panels, not a stretch of elements: offset, limit, total and
        // nextOffset all count panels, so a page never cuts one in half.
        var paths = new string[entries.Length];
        for (var index = 0; index < entries.Length; index++) paths[index] = entries[index].Path;
        var panels = NativeObjectPath.Runs(paths);
        var first = Math.Max(offset, 0);
        var end = (int)Math.Min(panels.Count, (long)offset + command.Amount);

        // Every element is addressed by the shortest tail of its path that no other live element
        // answers to. The Canvas-rooted chain existed so a row plus its page's prefix would
        // resolve; a tail that resolves on its own needs neither, and a round measured 23% of this
        // verb as ancestry no caller ever quoted back. The address lengthens one segment at a time
        // and only where uniqueness requires it, so it is always a valid `path` argument.
        var projected = new GameMcpArrayBuilder();
        for (var index = first; index < end; index++)
        {
            var panel = panels[index];
            var members = new List<GameMcpObjectBuilder>(panel.Count);
            var segments = new List<string>(panel.Count);
            for (var member = panel.Start; member < panel.Start + panel.Count; member++)
            {
                var entry = entries[member];
                var segment = GameMcpTooltipPanelRow.ShortestUnique(entry.Path, paths);
                GameMcpObjectBuilder tooltip;
                if (entry.Hover is { } hover)
                {
                    var item = hover.tooltipItem!;
                    if (!nativeAccess.TryReadSubTooltips(hover, out _, out var readFailure))
                    {
                        return TooltipsUnreadableBecause(readFailure);
                    }
                    if (!nativeAccess.TryReadEntityId(item, out var entityId, out var identityFailure))
                    {
                        return TooltipsUnreadableBecause(identityFailure);
                    }
                    tooltip = GameMcpTooltipPanelRow.Project(
                        segment,
                        item.GetName(),
                        entityId,
                        command.FrameContext?.World?.Snapshot);
                }
                else
                {
                    tooltip = GameMcpTooltipPanelRow.Opens(segment, entry.ModalTitle);
                }
                members.Add(tooltip);
                segments.Add(segment);
            }
            projected.Add(GameMcpTooltipPanelRow.Panel(members, segments));
        }
        var details = new GameMcpObjectBuilder
        {
            ["scene"] = SceneManager.GetActiveScene().name,
        };
        details["total"] = panels.Count;
        details["rows"] = projected;
        if (end < panels.Count) details["nextOffset"] = end;
        return GadgetCommitted(
            "tooltip_catalog_read",
            details);
    }

    /// <summary>
    /// The panel one chrome control opens, when the path names exactly one chrome control.
    /// </summary>
    private bool TryNameChromePanel(string path, out string title, out string failure)
    {
        title = string.Empty;
        failure = string.Empty;
        if (_modalOpenGameAction is null)
        {
            failure = ModalOpenGameAction.NoActivator;
            return false;
        }
        if (!_modalOpenGameAction.TryReadActivators(out var activators, out failure)) return false;
        foreach (var activator in activators)
        {
            var located = NativeObjectPath.Locate(activator.Control).Path;
            if (!NativeObjectPath.Addresses(located, path)) continue;
            if (title.Length > 0)
            {
                title = string.Empty;
                return false;
            }
            title = activator.Title;
        }
        return title.Length > 0;
    }

    private GameMcpCommandResult ReadTooltipGameMcp(GameMcpCommand command)
    {
        var requestedPath = command.PayloadValue;
        var active = CaptureActiveHoverTooltips();
        var nativeAccess = _gameMcpTooltipNativeAccess;
        if (nativeAccess is null)
        {
            return TooltipsUnreadableBecause(_gameMcpTooltipContractFailure);
        }

        HoverTooltip hover;
        if (command.TargetId != Guid.Empty)
        {
            if (!TryAddressTooltipByEntity(
                    nativeAccess, active, command.TargetId, out var drawn, out var entityRefusal))
            {
                return entityRefusal;
            }
            if (drawn is null) return ReadEntityTooltipGameMcp(command, nativeAccess);
            hover = drawn;
        }
        else
        {
            // The catalog hands out the part of the path its page's shared prefix does not already
            // say, and which prefix that was depends on which page the row came from. So a row
            // resolves by the tail it was given: the whole path, or any path ending in it at an
            // element boundary.
            var matches = active
                .Where(entry => NativeObjectPath.Addresses(entry.Path, requestedPath))
                .ToArray();
            if (matches.Length == 0)
            {
                // The chrome controls are on the screen and in the element list, and none of them
                // draws a tooltip. "Matched 0 elements" would have read as a stale path for a path
                // the catalog printed one call ago, so the answer names the panel instead.
                if (!TryNameChromePanel(requestedPath, out var panel, out var chromeFailure))
                {
                    if (chromeFailure.Length > 0) return TooltipsUnreadableBecause(chromeFailure);
                }
                else
                {
                    return GadgetRejected(
                        "tooltip_content_unavailable",
                        "This control opens the " + panel + " panel and draws no tooltip of its " +
                        "own; put the panel up with game_modal mode open, title " + panel + ".");
                }
            }
            if (matches.Length != 1)
            {
                // Naming the next step is the point. A tail is only as unique as the page it came
                // from, and two scroll lists on one screen hand out colliding tails routinely; the
                // catalog already published the prefix that separates them, so the refusal says to
                // put it back on rather than leaving a caller to guess that a longer path exists.
                return GadgetRejected(
                    "tooltip_match_failed",
                    "tooltip path '" + requestedPath + "' matched " +
                    matches.Length + " active current-screen elements" +
                    "; re-read game_screen_elements for this screen's current paths, which name " +
                    "one element each");
            }
            hover = matches[0].Hover;
        }
        if (hover.tooltipItem is null)
        {
            return GadgetRejected(
                "tooltip_content_unavailable",
                "The game draws no tooltip for this element.");
        }
        if (!nativeAccess.TryReadSubTooltips(hover, out var children, out var readFailure))
        {
            return TooltipsUnreadableBecause(readFailure);
        }

        // A panel draws one node list, and which one is the live more-info key the player is or is
        // not holding. The container the screen already has up for this item answers with the
        // game's own IsUsingAltTooltip(); with no container up the screen is drawing nothing for
        // this element and the panel's resting state is its main list.
        var usingAlt = UITooltipContainer.globalTooltips?
            .Any(panel => panel is not null &&
                ReferenceEquals(panel.item, hover.tooltipItem) &&
                panel.IsUsingAltTooltip()) == true;
        if (!nativeAccess.TryReadEntityId(hover.tooltipItem, out var entityId, out var identityFailure))
        {
            return TooltipsUnreadableBecause(identityFailure);
        }
        return ProjectTooltipGameMcp(nativeAccess, hover.tooltipItem, children, usingAlt, entityId);
    }

    /// <summary>
    /// An entity's own tooltip, for an id nothing on the screen is drawing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A tooltip's link lines name what the player sees by hovering a row inside it — the resource
    /// a cost is paid in, the statistic an effect raises, the glossary word a number is measured
    /// in — and none of those is drawn on the screen itself. Answering only for what the screen
    /// draws made every such link a call that refused, so the panel a link opens is read where it
    /// lives: on the entity, exactly as hovering the link would put it up. Every tooltip builder in
    /// the pinned build is held to doing nothing but answer, by the installed-game side-effect
    /// contract over the whole call graph below them.
    /// </para>
    /// <para>
    /// No container is up for a panel the screen is not drawing, so the more-info key has nothing
    /// to toggle and the panel is read in its resting state, its main node list. The panel is the
    /// entity's alone: the sub-panels an element adds beside it are the element's, and a caller who
    /// wants those reads the element.
    /// </para>
    /// </remarks>
    private GameMcpCommandResult ReadEntityTooltipGameMcp(
        GameMcpCommand command,
        GameMcpTooltipNativeAccess nativeAccess)
    {
        var world = command.FrameContext!.World?.Snapshot;
        var catalog = EntityIdentities(command.FrameContext!);
        if (world is null || !catalog.IsBound)
        {
            return GadgetRejected(
                "entity_catalog_unavailable",
                "Nothing this screen draws is about this id, and no save is loaded to read its " +
                "tooltip from; continue a save first.");
        }
        if (!catalog.TryGet(command.TargetId, out var identity))
        {
            return GadgetRejected("unknown_uuid", GameMcpTooltipPanelRow.UnknownEntity);
        }
        if (GameMcpEntityExplainer.IsHidden(world, command.TargetId))
        {
            return GadgetRejected("not_on_screen", GameMcpTooltipPanelRow.HiddenEntity);
        }
        if (!GameMcpEntityExplainer.TryResolveTooltip(
                command.TargetId, identity.RuntimeType, out var tooltip))
        {
            return GadgetRejected(
                "tooltip_content_unavailable",
                identity.DisplayName + " is a " + identity.RuntimeType + " the game draws no " +
                "tooltip for, and nothing this screen draws is about it.");
        }
        return ProjectTooltipGameMcp(
            nativeAccess, tooltip, Array.Empty<ITooltipable>(), false, command.TargetId);
    }

    private GameMcpCommandResult ProjectTooltipGameMcp(
        GameMcpTooltipNativeAccess nativeAccess,
        ITooltipable item,
        ITooltipable[] children,
        bool usingAlt,
        Guid entityId)
    {
        // A link pointer names the entity it points at, so it needs that entity's id; a tooltipable
        // about nothing answers empty and its pointer carries the name alone. A bound accessor that
        // throws is a native fault rather than an absent id, and it refuses the read with the same
        // account the hovered element's own identity would have given.
        var linkFailure = string.Empty;
        Guid Identify(ITooltipable link)
        {
            if (nativeAccess.TryReadEntityId(link, out var linkId, out var linkDetail)) return linkId;
            linkFailure = linkDetail;
            throw new InvalidOperationException(nameof(Identify));
        }

        GameMcpObjectBuilder details;
        try
        {
            if (!GameMcpTooltipProjector.TryProject(
                    item,
                    children,
                    usingAlt,
                    GameMcpTooltipLayout.Read(),
                    Identify,
                    out details))
            {
                return GadgetRejected(
                    "tooltip_depth_exceeded",
                    "This tooltip leads on through more of the game than the suite will read in " +
                    "one answer, so none of its text is returned; the screen still shows it.");
            }
            if (entityId != Guid.Empty) details["uuid"] = entityId.ToString("D");
        }
        catch (Exception exception)
        {
            if (linkFailure.Length > 0) return TooltipsUnreadableBecause(linkFailure);
            return GadgetRejected(
                "tooltip_read_faulted",
                "Reading this tooltip failed, so its text cannot be read." +
                GameActionFaultLog.Record(exception, "tooltip"));
        }
        var result = GadgetCommitted(
            "tooltip_read",
            details);
        return result;
    }

    /// <summary>
    /// The one live element a published entity id addresses, null when the screen draws nothing
    /// about it, or the refusal that answers instead.
    /// </summary>
    /// <remarks>
    /// The element-to-entity binding is the catalog's own, taken per element, so a uuid addresses
    /// exactly the elements the catalog would have printed that id on. A binding that cannot be
    /// taken at all refuses the whole call rather than quietly narrowing the set the answer was
    /// chosen from.
    /// </remarks>
    private bool TryAddressTooltipByEntity(
        GameMcpTooltipNativeAccess nativeAccess,
        IReadOnlyList<TooltipElement> active,
        Guid requested,
        out HoverTooltip? hover,
        out GameMcpCommandResult refusal)
    {
        hover = null;
        refusal = null!;
        var entities = new Guid[active.Count];
        var paths = new string[active.Count];
        for (var index = 0; index < active.Count; index++)
        {
            var entry = active[index];
            paths[index] = entry.Path;
            if (entry.Hover.tooltipItem is null) continue;
            if (!nativeAccess.TryReadEntityId(
                    entry.Hover.tooltipItem, out var entityId, out var identityFailure))
            {
                refusal = TooltipsUnreadableBecause(identityFailure);
                return false;
            }
            entities[index] = entityId;
        }

        var address = GameMcpTooltipPanelRow.AddressEntity(entities, paths, requested);
        if (address.Resolved)
        {
            hover = active[address.Element].Hover;
            return true;
        }
        if (address.Undrawn) return true;

        var listed = new GameMcpArrayBuilder();
        for (var index = 0; index < address.Paths.Count; index++) listed.Add(address.Paths[index]);
        refusal = GadgetRejected(
            address.Code, address.Reason, new GameMcpObjectBuilder { ["paths"] = listed });
        return false;
    }

    /// <summary>
    /// One thing the current screen draws that a caller can address: a hover element, or a chrome
    /// control that puts a panel up.
    /// </summary>
    /// <remarks>
    /// The chrome controls carry no <c>HoverTooltip</c>, which is the whole reason the Player,
    /// settings and achievement icons were absent from a verb whose job is to say what is on the
    /// screen. They are not entities and never carry an id; what they carry is the name of the
    /// panel they open, which is the argument that opens it.
    /// </remarks>
    private readonly struct ScreenElement
    {
        internal ScreenElement(
            HoverTooltip? hover,
            string modalTitle,
            NativeObjectPath.Placement placement)
        {
            Hover = hover;
            ModalTitle = modalTitle ?? string.Empty;
            Placement = placement;
        }

        internal HoverTooltip? Hover { get; }

        /// <summary>The panel this control opens, empty on every hover element.</summary>
        internal string ModalTitle { get; }

        internal NativeObjectPath.Placement Placement { get; }
        internal string Path => Placement.Path;
    }

    /// <summary>
    /// Everything the player can currently point at, in screen order: the hover elements and the
    /// chrome controls together, because a caller reading what is on the screen is reading one
    /// screen and not two lists.
    /// </summary>
    private bool TryCaptureScreenElements(out ScreenElement[] elements, out string reason)
    {
        elements = Array.Empty<ScreenElement>();
        reason = string.Empty;
        var merged = new List<ScreenElement>();
        foreach (var entry in CaptureActiveHoverTooltips())
        {
            if (entry.Hover.tooltipItem is null) continue;
            merged.Add(new ScreenElement(entry.Hover, string.Empty, entry.Placement));
        }
        if (_modalOpenGameAction is null)
        {
            reason = ModalOpenGameAction.NoActivator;
            return false;
        }
        if (!_modalOpenGameAction.TryReadActivators(out var activators, out reason)) return false;
        foreach (var activator in activators)
        {
            merged.Add(new ScreenElement(
                null, activator.Title, NativeObjectPath.Locate(activator.Control)));
        }
        elements = merged
            .OrderBy(static entry => entry.Placement.OrderKey, StringComparer.Ordinal)
            .ToArray();
        return true;
    }

    /// <summary>One live hover element with the hierarchy keys already read off it.</summary>
    internal readonly struct TooltipElement
    {
        internal TooltipElement(HoverTooltip hover, NativeObjectPath.Placement placement)
        {
            Hover = hover;
            Placement = placement;
        }

        internal HoverTooltip Hover { get; }
        internal NativeObjectPath.Placement Placement { get; }
        internal string Path => Placement.Path;
    }

    /// <summary>
    /// Every hover element the player can currently see, in screen order, each carrying its own
    /// hierarchy path.
    /// </summary>
    /// <remarks>
    /// The ancestry is walked once per element. Sorting, the shared prefix, and each row's printed
    /// path all read that one result, where they previously walked the same chain three times over.
    /// </remarks>
    private static IReadOnlyList<TooltipElement> CaptureActiveHoverTooltips() =>
        Resources.FindObjectsOfTypeAll(typeof(HoverTooltip))
            .OfType<HoverTooltip>()
            .Where(hover =>
                hover.enabled &&
                hover.gameObject.activeInHierarchy &&
                GameMcpTooltipNativeAccess.OnScreen(hover))
            .Select(static hover => new TooltipElement(hover, NativeObjectPath.Locate(hover)))
            .OrderBy(static entry => entry.Placement.OrderKey, StringComparer.Ordinal)
            .ToArray();

    private GameMcpCommandResult ProbeGameMcp(GameMcpCommand command)
    {
        GameMcpObjectBuilder details;
        switch (command.Mode)
        {
            case "runtime":
                var lifecycle = GameLifecycleMonitor.Shared.Current;
                details = new GameMcpObjectBuilder
                {
                    ["scene"] = SceneManager.GetActiveScene().name,
                    ["frame"] = Time.frameCount,
                    ["timeScale"] = Time.timeScale,
                    ["lifecycleGeneration"] = lifecycle.Generation,
                    ["lifecycleState"] = lifecycle.State.ToString(),
                    ["gameplayReady"] = lifecycle.IsGameplayReady,
                    ["modsShellAlive"] = _uiShell?.IsAlive == true,
                };
                break;
            case "action_queue_room":
                var queue = new AutoBuyNativeQueueRoomAdapter();
                if (!queue.TryReadRemainingRoom(out var remaining, out var queueFailure))
                    return GadgetRejected(
                        "native_probe_unavailable",
                        "The action queue's free room could not be read: " + queueFailure);
                // The game's own upgrade button queues past the maximum, so the reading goes
                // negative. Nought free slots is the answer either way; the overshoot rides beside
                // it as the fact it is, and is absent when there is none.
                details = new GameMcpObjectBuilder
                {
                    ["remainingRoom"] = Math.Max(0, remaining),
                };
                if (remaining < 0)
                    details["entriesBeyondCapacity"] = -remaining;
                break;
            case "navigation":
                var tabs = _uiShell is not null && _uiShell.IsAlive
                    ? _uiShell.CaptureNativeTabsForGameMcp()
                    : Array.Empty<GameMcpNativeTab>();
                details = new GameMcpObjectBuilder
                {
                    ["scene"] = SceneManager.GetActiveScene().name,
                    ["nativeTabCount"] = tabs.Count,
                    ["activeNativeSubtabCount"] = CaptureSubtabs().Count,
                };
                break;
            default:
                return GadgetFailed(
                    "unsupported_probe",
                    "The suite asked for a reading this build does not take, so nothing was read.");
        }
        return GadgetCommitted(
            "probe_read",
            details);
    }

    private GameMcpCommandResult GadgetCommitted(
        string code,
        GameMcpObjectBuilder details) =>
        GameMcpCommandResult.Committed(
            code,
            observedLifecycleGeneration: _lifecycleGeneration,
            observedConfigurationGeneration:
                _configurationStore?.CurrentGeneration.Value ?? 0,
            details.Freeze());

    /// <summary>
    /// A gadget answer for a call the suite stopped on its own, with the game never asked.
    /// </summary>
    /// <remarks>
    /// The router validates every screen and every probe name before a gadget is reached, so the
    /// guards below it are reachable only by the suite contradicting its own router. They stay —
    /// a wrong assumption must fail closed rather than fall through — but the word for them is not
    /// <c>refused</c>, which reads as the game having said no and sends a caller looking for a game
    /// state to change that does not exist.
    /// </remarks>
    /// <summary>
    /// The one tooltip-unreadable answer, with the binding layer's own account of what was missing
    /// written to the suite log rather than dropped.
    /// </summary>
    private GameMcpCommandResult TooltipsUnreadableBecause(string nativeDetail)
    {
        if (nativeDetail.Length > 0)
            Logger.LogWarning("Game MCP could not read a tooltip: " + nativeDetail);
        return GadgetFailed("tooltip_contract_unavailable", TooltipsUnreadable);
    }

    private GameMcpCommandResult GadgetFailed(string code, string reason) =>
        GameMcpCommandResult.Failed(
            code,
            reason,
            observedLifecycleGeneration: _lifecycleGeneration,
            observedConfigurationGeneration:
                _configurationStore?.CurrentGeneration.Value ?? 0);

    private GameMcpCommandResult GadgetFaulted(string code, string reason) =>
        GameMcpCommandResult.Faulted(
            code,
            reason,
            observedLifecycleGeneration: _lifecycleGeneration,
            observedConfigurationGeneration:
                _configurationStore?.CurrentGeneration.Value ?? 0);

    private GameMcpCommandResult GadgetRejected(
        string code,
        string reason,
        GameMcpObjectBuilder? details = null) =>
        GameMcpCommandResult.Rejected(
            code,
            reason,
            observedLifecycleGeneration: _lifecycleGeneration,
            observedConfigurationGeneration:
                _configurationStore?.CurrentGeneration.Value ?? 0,
            details?.Freeze());

    /// <summary>
    /// A navigate that reached the requested screen and then failed on its subtab moved the board.
    /// The sentence says so, because a bare "refused" reads as "nothing happened" and the caller's
    /// next read finds a screen it did not ask to be on.
    /// </summary>
    private static string SubtabRefusalReason(
        string subtabReason,
        bool settled,
        string? path = null)
    {
        var arrived = settled
            ? "The requested screen is now active"
            : "The requested screen was selected but did not settle within one second";
        var onTheWay = path is null ? string.Empty : " on the way to " + path;
        return arrived + " and was not left; " + subtabReason + onTheWay + ".";
    }

    private GameMcpCommandResult NavigationRefusal(
        string code,
        string reason,
        GameMcpObjectBuilder? state,
        string candidateField,
        IEnumerable<string> candidates)
    {
        var values = new GameMcpArrayBuilder();
        foreach (var candidate in candidates) values.Add(candidate);
        var details = new GameMcpObjectBuilder();
        if (state is not null) details.CopyFrom(state);
        if (values.Count > 0) details[candidateField] = values;
        return GadgetRejected(code, reason).WithDetails(details.Freeze());
    }

    private sealed class GameMcpSubtab
    {
        private readonly Func<string> _select;

        internal GameMcpSubtab(
            int index,
            string label,
            string path,
            string stripKey,
            bool active,
            Func<string> select)
        {
            Index = index;
            Label = label ?? string.Empty;
            Path = path ?? string.Empty;
            StripKey = stripKey ?? string.Empty;
            Active = active;
            _select = select ?? throw new ArgumentNullException(nameof(select));
        }

        internal int Index { get; }
        internal string Label { get; }
        internal string Path { get; }
        internal string StripKey { get; }
        internal bool Active { get; }
        internal bool TrySelect(out string reason)
        {
            reason = _select();
            return reason.Length == 0;
        }
    }

#endif

    private bool IsLifecycleReady() =>
        SceneManager.GetActiveScene().name == "Main" &&
        GameLifecycleMonitor.Shared.Current.IsGameplayReady &&
        GameLifecycleMonitor.Shared.IsCurrent(_lifecycleLease);

    private static void BeforeSaveLoad(object __instance) =>
        ObserveLifecycle(GameLifecycleTransitionKind.SaveLoadStarted, SceneManager.GetActiveScene().name, __instance);
    private static void AfterSaveLoaded(object __instance) =>
        ObserveLifecycle(GameLifecycleTransitionKind.SaveLoaded, SceneManager.GetActiveScene().name, __instance);
    private static void AfterGameInitialized(object __instance)
    {
        ObserveLifecycle(GameLifecycleTransitionKind.RegistryRebuilt, SceneManager.GetActiveScene().name, __instance);
        ObserveLifecycle(GameLifecycleTransitionKind.RuntimeReady, SceneManager.GetActiveScene().name, __instance);
    }
    private static void BeforePersistentReset(object __instance) =>
        ObserveLifecycle(GameLifecycleTransitionKind.NewGamePlusStarted, SceneManager.GetActiveScene().name, __instance);
    private static void BeforeRuntimeReset() =>
        ObserveLifecycle(GameLifecycleTransitionKind.ResetStarted, SceneManager.GetActiveScene().name);

    private static bool IsGameplayScene() => Instance is { } plugin && plugin.IsLifecycleReady();

    private void PatchOptional(string targetName, string patchName, bool postfix)
    {
        var target = AccessTools.Method(targetName);
        if (target is null) { Logger.LogWarning($"Optional native hook unavailable: {targetName}."); return; }
        var patch = new HarmonyMethod(typeof(Plugin), patchName);
        try
        {
            if (postfix) _harmony!.Patch(target, postfix: patch); else _harmony!.Patch(target, prefix: patch);
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"Optional native hook failed: {targetName}: {ex.GetBaseException().Message}");
        }
    }

    private void RunUiMaintenance()
    {
        _uiMaintenanceDue = false;
        if (_uiShell is not null && !_uiShell.IsAlive)
        {
            _uiShell.Dispose();
            _uiShell = null;
            _uiRetrySeconds = 0f;
            _deferInstallUntilFrame = Time.frameCount + 1;
            return;
        }
        if (_uiShell is not null)
        {
            _uiShell.RunPendingRefresh();
            if (_uiIntegrityDue)
            {
                var loadedSources = ConfigCatalog.CaptureLoadedSources();
                var generation = ConfigCatalogGeneration.Capture(loadedSources);
                if (!ModConfigCatalogSession.IsCurrent(
                        _catalog,
                        _catalogGeneration,
                        generation))
                {
                    _catalogNavigation = _uiShell.CaptureNavigation();
                    _uiShell.Dispose();
                    _uiShell = null;
                    _catalog = ModConfigCatalogSession.GetOrDiscover(
                        ref _catalog,
                        ref _catalogGeneration,
                        generation,
                        () => ConfigCatalog.Build(loadedSources, _runtimeSources!.SchemaStatuses),
                        LogCatalog);
                    _uiIntegrityDue = false;
                    _uiRetrySeconds = 0f;
                    _uiMaintenanceDue = true;
                    return;
                }
                _uiShell.RefreshNavigation();
                _uiIntegrityDue = false;
                _uiIntegritySeconds = UiIntegrityIntervalSeconds;
            }
            _uiMaintenanceDue = _uiIntegrityDue || _uiShell.HasPendingRefresh;
            return;
        }

        _uiRetrySeconds = UiRetryIntervalSeconds;
        var runtimeSources = _runtimeSources ??
                             throw new InvalidOperationException("Mod Config runtime sources were not composed.");
        var featureCommands = _modConfigFeatureCommands ??
                              throw new InvalidOperationException("Mod Config feature commands were not composed.");
        var loadedCatalogSources = ConfigCatalog.CaptureLoadedSources();
        var currentCatalogGeneration = ConfigCatalogGeneration.Capture(loadedCatalogSources);
        var catalog = ModConfigCatalogSession.GetOrDiscover(
            ref _catalog,
            ref _catalogGeneration,
            currentCatalogGeneration,
            () => ConfigCatalog.Build(loadedCatalogSources, runtimeSources.SchemaStatuses),
            LogCatalog);
        if (!ModConfigUiShell.TryCreate(
                Logger,
                catalog,
                runtimeSources,
                featureCommands,
                _catalogNavigation,
                MarkUiMaintenanceDue,
                MarkNavigationMaintenanceDue,
                out _uiShell,
                out var reason))
        {
            var observation = _modsUiRetry.ObserveFailure();
            if (observation.ShouldLogRetry)
            {
                Logger.LogInfo("Mod Config UI is not ready; installation will retry: " + reason);
            }
            if (observation.IsTerminal)
                _uiSurfaceDiagnostics?.ReportFailure(SuiteUiSurface.ModsRail, reason);
            else
                _uiSurfaceDiagnostics?.ReportWaiting(SuiteUiSurface.ModsRail, reason);
            return;
        }
        _modsUiRetry.Reset();
        _uiSurfaceDiagnostics?.ReportSuccess(SuiteUiSurface.ModsRail);
        _uiIntegritySeconds = UiIntegrityIntervalSeconds;
    }

    private void LogCatalog(ConfigCatalogSnapshot catalog)
    {
        Logger.LogInfo(
            $"Orb Mod Config loaded. UiShell={_modConfigSettings?.EnableUiShell.Value == true}; " +
            $"DiscoveredMods={catalog.Mods.Count}, DiscoveredSettings={catalog.SettingCount}.");
        foreach (var mod in catalog.Mods)
        {
            Logger.LogInfo(
                $"Mod Config catalog: {mod.Name} {mod.Version} ({mod.Guid}); " +
                $"Sections={mod.Sections.Count}, Settings={mod.Sections.Sum(section => section.Settings.Count)}.");
        }
    }

    private void MarkUiMaintenanceDue() => _uiMaintenanceDue = true;

    private void MarkNavigationMaintenanceDue()
    {
        _uiIntegrityDue = true;
        _uiMaintenanceDue = true;
    }

    private void DeactivateUiWork(bool disposeShell)
    {
        _uiWork?.SetState(false, false);
        _uiMaintenanceDue = false;
        _uiIntegrityDue = false;
        if (!disposeShell || _uiShell is null) return;
        _uiShell.Dispose();
        _uiShell = null;
        _uiRetrySeconds = 0f;
        _modsUiRetry.Reset();
    }

    private void ResetSceneState(Scene scene)
    {
        _uiSceneEpoch++;
        _uiStartupReadinessScheduled = false;
        _uiStartupReadiness.Reset();
        _uiRetrySeconds = 0f;
        _uiIntegritySeconds = 0f;
        _modsUiRetry.Reset();
        _quickControls?.Dispose();
        _quickControls = null;
        _quickControlsUiRetrySeconds = 0f;
        ResetQuickControlsFailure();
        _uiMaintenanceDue = false;
        _uiIntegrityDue = false;
        _deferInstallUntilFrame = 0;
        _uiSurfaceDiagnostics?.ResetForScene();
        _uiWork?.SetState(scene.name == "Main", false);
    }

    internal static bool AdvanceCadence(ref float remainingSeconds, float elapsedSeconds, float intervalSeconds)
    {
        remainingSeconds -= Math.Max(0.0f, elapsedSeconds);
        if (remainingSeconds > 0.0f) return false;
        remainingSeconds = Math.Max(0.1f, intervalSeconds);
        return true;
    }
}
