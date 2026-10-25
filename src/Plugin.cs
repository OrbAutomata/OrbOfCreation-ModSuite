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
using OrbModding.Common.Runtime.ServiceCycle.Configuration;
using OrbModding.Common.Runtime.Configuration;
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
    private GameMcpFrameExecutor? _gameMcpFrameExecutor;
#else
    private const bool AutoStartServiceCycleDiagnostics = false;
#endif
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
    /// and Auto Buy's boundary check compare against — an unobserved save-load leaves both
    /// comparing stale to stale. Named here rather than written inline so losing one is a diff
    /// rather than an omission.
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
    private QuickControlsHost? _quickControlsHost;
    private EmergencyStopControl? _emergencyStopControl;
    private AutomataDifferentialVerificationControl? _mathVerification;
    private bool _knownOwnershipWarningLogged;
    private bool _nativeContractsAvailable = true;
    private bool _auditedBuild;
    private bool _buildCompatibilityRuntimeAllowed;
    private bool _runtimeActivationAllowed;
    private string _observedBuildFingerprint = string.Empty;
    private bool _runtimeComposed;
    private bool _runtimeCompositionAttempted;

    private MentorConfig? _mentorConfig;

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
        _quickControlsHost = new QuickControlsHost(
            _automationFeatureControls,
            _emergencyStopControl,
            message => Logger.LogInfo(message),
            () => _uiSurfaceDiagnostics?.ReportSuccess(SuiteUiSurface.QuickControls),
            reason => _uiSurfaceDiagnostics?.ReportWaiting(SuiteUiSurface.QuickControls, reason),
            reason => _uiSurfaceDiagnostics?.ReportFailure(SuiteUiSurface.QuickControls, reason));
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
        _gameMcpFrameExecutor = new GameMcpFrameExecutor(
            this,
            Logger,
            () => _uiShell,
            () => _lifecycleGeneration,
            () => _serviceCycleActivation,
            () => _automataActionFamilyOwnership,
            _configurationStore,
            _mathVerification,
            _worldDemand,
            () => _nativeContractsAvailable,
            () => _runtimeActivationAllowed,
            _automaticSaveBackup,
            OnEmergencyStopChanged,
            _automataConfig.CreateGameMcpWritableSchema());
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
#if SERVICE_CYCLE_PROFILE
        var harvestFamily = ownership.Family(AutoHarvestFeatureContribution.ActionFamily);
        var scribeFamily = ownership.Family(AutoScribeFeatureContribution.ActionFamily);
#endif
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
                var features = new AutomataFeatureComposition
                {
                    ReadLifecycleEpoch = readLifecycleEpoch,
                    RegistryResolver = registryResolver,
                    Ownership = ownership,
                    Configuration = _configurationStore!,
                    Statuses = featureStatuses,
                    SpellLevelCapability = _spellLevelCapability,
                    ManualCastPause = _autoCastManualPause,
                    AuditedBaselineId = _auditedBaselineId,
                    RuntimeDiagnostics = RuntimeDiagnosticsRegistry.Shared,
                    RefusalBundles = new AutoBuyRefusalBundleWriter(
                        () => AutomataTraceRunRoot.Stable("diagnostics")),
                    ReportInvariant = message => Log.LogAutomataError(message),
                    Warn = message => Log.LogAutomataWarning(message),
                    Info = message => Log.LogAutomataInfo(message),
                };
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
                    AutomataFeatureCatalog.CreateServices(features,
                        // The collector registers first so consumers start with its publication.
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
                        new StrategistServiceCycleFeature()),
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
            DecisionJournalStatusSources.Shared,
            new DeferredStrategistBulletinSource(
                () => _serviceCycleActivation,
                () => !_automaticSaveBackup.AllowsAutomation
                    ? AutomaticSaveBackupWording.BlockingReason(_automaticSaveBackup)
                    : !_runtimeActivationAllowed
                        ? "Installed game assemblies are quarantined pending an exact-build acknowledgement."
                        : string.Empty)
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
        _gameMcpFrameExecutor?.Drain();
#endif
        UpdateMentor();
        UpdateUiStartupReadiness(Time.unscaledDeltaTime);
        _quickControlsHost?.Update(
            Time.unscaledDeltaTime,
            SceneManager.GetActiveScene().name == "Main",
            _uiStartupReadiness.Admission.QuickControls,
            _nativeContractsAvailable);
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
            _gameMcpFrameExecutor?.ServerRunning == true,
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
    }

    private void OnDestroy()
    {
#if SERVICE_CYCLE_PROFILE
        _gameMcpFrameExecutor?.Dispose();
        _gameMcpFrameExecutor = null;
#endif
        _startStatusView?.Dispose();
        _startStatusView = null;
        _quickControlsHost?.Dispose();
        _quickControlsHost = null;
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
        _quickControlsHost?.RetryNow();
        _uiRetrySeconds = 0f;
        _uiMaintenanceDue = true;
    }

    private void OnEmergencyStopChanged(bool stopped)
    {
        CancelPreparedAutomationForOwnershipRelease();
        if (stopped)
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
        _gameMcpFrameExecutor?.InvalidateLifecycle();
#endif
        _automataActionFamilyOwnership?.ReleaseLifecycleClaims();
        if (_configurationStore is not null)
            _featureStatuses?.ObserveLifecycleNotReady(
                _configurationStore.Current,
                _lifecycleGeneration,
                _configurationStore.CurrentGeneration);
        MentorMasteryPatchBridge.ResetLifecycle(_lifecycleGeneration);
        _lifecycleLease = GameLifecycleMonitor.Shared.CaptureLease();

        // The quick-controls anchor and every borrowed sprite are scene-object references. Save
        // load, reset, and NG+ can replace them without changing the active scene name, so no
        // lifecycle generation may retain the previous column.
        _quickControlsHost?.ResetLifecycle();

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

        _uiRetrySeconds = UiInstallationRetryState.RetryIntervalSeconds;
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
        _quickControlsHost?.ResetLifecycle();
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
