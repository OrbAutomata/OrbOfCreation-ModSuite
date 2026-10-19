using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using OrbAutomata;
#if SERVICE_CYCLE_PROFILE
using OrbAutomata.GameMcp;
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
    private readonly GameMcpGadgetResults _gadgetResults;
    private readonly GameMcpOpenModals _openModals;
    private readonly GameMcpNavigator _navigator;
    private readonly GameMcpScreenReader _screenReader;
    private string _gameMcpTooltipContractFailure =
        "tooltip native layout has not been bound";

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

#if SERVICE_CYCLE_PROFILE
    public Plugin()
    {
        _gadgetResults = new GameMcpGadgetResults(
            () => _lifecycleGeneration,
            () => _configurationStore?.CurrentGeneration.Value ?? 0);
        _openModals = new GameMcpOpenModals(() => _modalDismissGameAction);
        _navigator = new GameMcpNavigator(
            this,
            () => _uiShell,
            CompleteGameMcpCommand,
            _gadgetResults,
            _openModals);
        _screenReader = new GameMcpScreenReader(
            () => _uiShell,
            () => _gameMcpTooltipNativeAccess,
            () => _gameMcpTooltipContractFailure,
            () => _modalOpenGameAction,
            message => Logger.LogWarning(message),
            _gadgetResults,
            _navigator);
    }

#endif
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
                                reportInvariant: message => Log.LogAutomataError(message),
                                warn: message => Log.LogAutomataWarning(message)
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
        var answered = result.WithEntityIdentities(context.EntityIdentities);
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

    private bool TryExecuteGameMcpFrameOperation(
        GameMcpFrameOperation operation,
        GameMcpFrameContext context,
        out GameMcpToolExecution execution)
    {
        var request = operation.Request;
        if (GameMcpFrameReads.TryExecuteFrameRead(request, context, out execution)) return true;
        switch (request.ToolName)
        {
            case "suite_check_game_math":
                execution = RunGameMcpGameMathCheck();
                return true;
            case "game_screen_catalog":
                execution = GameMcpToolExecution.Read(_screenReader.CaptureScreenCatalogGameMcp());
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
                execution = GameMcpFrameReads.ExecuteGameMcpResource(request.ResourceUri, context);
                return true;
        }

        if (!GameMcpCommandPreparation.TryPrepareGameMcpCommand(
                operation, context, out var command, out var failure))
        {
            execution = GameMcpFrameReads.ProjectGameMcpCommand(command, failure);
            return true;
        }

        if (command.Kind is GameMcpCommandKind.ConfigurationSet or
            GameMcpCommandKind.AutomationSet or GameMcpCommandKind.EmergencyStop)
        {
            execution = GameMcpFrameReads.ProjectGameMcpCommand(
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
            execution = GameMcpFrameReads.ProjectGameMcpCommand(command, gadgetResult);
            return true;
        }
        if (_automataActionFamilyOwnership is null)
        {
            execution = GameMcpFrameReads.ProjectGameMcpCommand(
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
                execution = GameMcpFrameReads.ProjectGameMcpCommand(command, result);
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
            if (!GameMcpCommandPreparation.TryAdmitConfigurationWriteAgainstWorld(
                    command, context, out var worldFailure))
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
                _gadgetResults.Committed(
                    "screenshot_captured",
                    new GameMcpObjectBuilder())));
            result = null!;
            return false;
        }
        if (access == GameMcpGadgetAccess.Navigation)
        {
            _navigator.Navigate(command);
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
                result = _gadgetResults.Rejected("contract_unavailable",
                    "The native modal close control is unavailable.");
                return true;
            }
            var submission = _modalDismissGameAction.Submit();
            if (submission.Faulted)
            {
                result = _gadgetResults.Faulted(submission.Code, submission.Reason);
                return true;
            }
            if (!submission.Committed)
            {
                result = _gadgetResults.Rejected(submission.Code, submission.Reason);
                return true;
            }
            StartCoroutine(CompleteModalDismissGameMcp(command, submission.Title));
            result = null!;
            return false;
        }

        result = access switch
        {
            GameMcpGadgetAccess.Probe => _screenReader.ProbeGameMcp(command),
            GameMcpGadgetAccess.ScreenCatalog => throw new InvalidOperationException(
                "the screen catalog is executed as a text read before gadget dispatch"),
            GameMcpGadgetAccess.TooltipCatalog =>
                _screenReader.CaptureTooltipCatalogGameMcp(command),
            GameMcpGadgetAccess.TooltipRead => _screenReader.ReadTooltipGameMcp(command),
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
            return _gadgetResults.Rejected("contract_unavailable", ModalOpenGameAction.NoActivator);
        var requested = command.PayloadValue;
        if (!_openModals.TryReadTitles(out var openBefore, out var openFailure))
            return _gadgetResults.Rejected("contract_unavailable", openFailure);
        if (openBefore.Length > 0)
        {
            if (Array.IndexOf(openBefore, requested) >= 0)
                return _gadgetResults.Committed("modal_opened", OpenedModal(requested));
            return _gadgetResults.Rejected(
                "modal_already_open",
                "The " + (openBefore[0].Length == 0 ? "open" : openBefore[0]) +
                " panel is covering the board, and the chrome beneath it takes no presses; " +
                "dismiss it first.");
        }
        if (!_modalOpenGameAction.TryReadActivators(out var activators, out var readFailure))
            return _gadgetResults.Rejected("contract_unavailable", readFailure);
        var submission = _modalOpenGameAction.Submit(requested, activators);
        if (submission.Faulted)
            return _gadgetResults.Faulted(submission.Code, submission.Reason);
        if (!submission.Committed)
            return _gadgetResults.Rejected(submission.Code, submission.Reason);
        return _gadgetResults.Committed("modal_opened", OpenedModal(submission.Title));
    }

    /// <summary>The open's own post-state: the name of the panel now covering the board.</summary>
    private static GameMcpObjectBuilder OpenedModal(string title) =>
        new() { ["opened"] = title };

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
            CompleteGameMcpCommand(command, _gadgetResults.Committed(
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
        CompleteGameMcpCommand(command, _gadgetResults.Committed("modal_dismissed", timedOut));
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
            return _gadgetResults.Rejected(
                "continue_wrong_scene",
                "Continue is only on the game's title screen; you are already in a run.");
        }

        var managerType = AccessTools.TypeByName("SaveStateManager");
        var manager = managerType is null
            ? null
            : Resources.FindObjectsOfTypeAll(managerType).FirstOrDefault();
        var startGame = AccessTools.Method("SaveStateManager:StartGame");
        if (manager is null || startGame is null)
        {
            return _gadgetResults.Failed(
                "continue_contract_unavailable",
                "This build does not expose the Continue button, so a save cannot be started " +
                "from here.");
        }

        startGame.Invoke(manager, Array.Empty<object>());
        return _gadgetResults.Committed(
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
            _openModals.AppendTo(details);
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
                _gadgetResults.Faulted(
                    "inline_screenshot_failed",
                    "server defect: the end-of-frame screenshot did not finish." +
                    GameActionFaultLog.Record(exception, "the screen being captured")));
        }
        finally
        {
            if (encodedTexture is not null && !ReferenceEquals(encodedTexture, texture))
                Destroy(encodedTexture);
            if (texture is not null) Destroy(texture);
        }
    }

    private GameMcpCommandResult SavedScreenshotBudgetResult(
        in GameMcpScreenshotBudgetAdmission admission) =>
        admission.Status == GameMcpScreenshotBudgetStatus.StorageUnavailable
            ? _gadgetResults.Faulted(
                "screenshot_budget_unavailable",
                "server defect: " + admission.Reason)
            : _gadgetResults.Rejected(
                "screenshot_budget_reached",
                admission.Reason);

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
