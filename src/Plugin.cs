using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
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
    private static readonly string GameMcpDllSha256 = ComputeExecutingDllSha256();
    private GameMcpFrameInbox? _gameMcpOperations;
    private GameMcpHttpServer? _gameMcpServer;
    private GameMcpWritableSettingDescriptor[] _gameMcpWritableConfiguration =
        Array.Empty<GameMcpWritableSettingDescriptor>();
    private GameMcpTooltipNativeAccess? _gameMcpTooltipNativeAccess;
    private ModalDismissGameAction? _modalDismissGameAction;
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

    // One lifecycle generation, one lease and one invalidation bus for the whole suite: the three
    // plugins each tracked their own copies of the same shared monitor and the same shared bus.
    private long _lifecycleGeneration;
    private GameLifecycleLease _lifecycleLease;
    private GameplayInvalidationBus? _invalidationBus;
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
            Logger.LogError(configuration.Status.Reason);
            return;
        }
        var suite = configuration.Config!;
        _automataConfig = suite.Automata;
        _mentorConfig = suite.Mentor;
        _modConfigSettings = suite.ModConfig;
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
                out _gameMcpTooltipNativeAccess,
                out _gameMcpTooltipContractFailure))
        {
            Logger.LogWarning(
                "Game MCP tooltip inspection is unavailable: " +
                _gameMcpTooltipContractFailure);
        }
        _gameMcpWritableConfiguration = _automataConfig.CreateGameMcpWritableSchema();
        _modalDismissGameAction = new ModalDismissGameAction(() => _lifecycleGeneration);
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
        catch (Exception exception) when (!AutomaticSaveBackup.IsProcessFatal(exception))
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
            () => GameLifecycleMonitor.Shared.Current.Generation);
        foreach (var patchType in HarmonyPatchTypes)
            _harmony.CreateClassProcessor(patchType).Patch();
        ComposeAutomata();
        _runtimeComposed = true;
    }

    private void ComposeAutomata()
    {
        var featureStatuses = _featureStatuses!;

        _automataActionFamilyOwnership = new AutomataActionFamilyOwnership();
        _mentorActionFamilyOwnership = new MentorActionFamilyOwnership();
        Log.LogAutomataWarning(
            "Action-family ownership is best-effort: exact known conflicts and cooperative suite owners are isolated, but unknown plugins that invoke native actions without registering cannot be proven absent and are not disabled.");
        _automataActionFamilyOwnership.RefreshLoadedPluginInventory(
            Chainloader.PluginInfos.Count,
            guid => Chainloader.PluginInfos.ContainsKey(guid));
        Func<long> readAutoHarvestLifecycleEpoch =
            () => GameLifecycleMonitor.Shared.Current.Generation;
        var autoHarvestRegistryResolver = TypedRegistryResolver.Shared;
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
                                autoHarvestRegistryResolver,
                                scribeProfile,
                                readAutoHarvestLifecycleEpoch,
                                ownsActionFamily: () =>
                                    _automataActionFamilyOwnership!.OwnsScribe,
                                tryCaptureMutationPermit: () =>
                                    _automataActionFamilyOwnership!
                                        .TryCaptureScribeMutationPermit(),
                                readOwnershipFailure: () =>
                                    _automataActionFamilyOwnership!
                                        .ScribeOwnershipFailure,
                                featureStatus: featureStatuses.AutoScribe))
                        : new AutoScribeUnavailableServiceCycleFeature(
                            featureStatuses.AutoScribe);
                return AutomataServiceCycleComposition.TryCreate(
                    configuration,
                    configurationGeneration,
                    new AutomataServiceCycleHostDependencies(
                            readFrameIdentity,
                            readAutoHarvestLifecycleEpoch,
                            ServiceActionOutcomeWindowRegistry.Shared,
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
                            readAutoHarvestLifecycleEpoch,
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
                                autoHarvestRegistryResolver,
                                readAutoHarvestLifecycleEpoch,
                                ownsActionFamily: () =>
                                    _automataActionFamilyOwnership!.OwnsItems,
                                tryCaptureMutationPermit: () =>
                                    _automataActionFamilyOwnership!
                                        .TryCaptureItemMutationPermit(),
                                readOwnershipFailure: () =>
                                    _automataActionFamilyOwnership!
                                        .ItemsOwnershipFailure,
                                featureStatus: featureStatuses.AutoItems)),
                        autoScribeFeature,
                        new AutoHarvestServiceCycleFeature(
                            new AutoHarvestFeatureDependencies(
                                autoHarvestRegistryResolver,
                                ownsActionFamily: () => _automataActionFamilyOwnership!.OwnsHarvest,
                                tryCaptureMutationPermit: () =>
                                    _automataActionFamilyOwnership!.TryCaptureHarvestMutationPermit(),
                                runtimeDiagnostics: RuntimeDiagnosticsRegistry.Shared,
                                featureStatus: featureStatuses.AutoHarvest)),
                        new AutoBuyServiceCycleFeature(
                            new AutoBuyFeatureDependencies(
                                readAutoHarvestLifecycleEpoch,
                                ownershipMask: () =>
                                    _automataActionFamilyOwnership!.EffectiveAutoBuyOwnership(
                                        _configurationStore!.Current.AutoBuy),
                                runtimeDiagnostics: RuntimeDiagnosticsRegistry.Shared,
                                featureStatus: featureStatuses.AutoBuy,
                                // Affordability drift skips and re-plans without a synchronous
                                // bundle; structural contradictions capture once and stand down.
                                refusalResponse: new AutoBuyRefusalResponder(
                                    () => _configurationStore!.Current.AutoBuy.Mode ==
                                        AutoBuyOperationMode.Active,
                                    StandDownAutoBuy,
                                    new AutoBuyRefusalBundleWriter(
                                        () => AutomataTraceRunRoot.Stable("diagnostics")),
                                    message => Log.LogAutomataError(message))
#if SERVICE_CYCLE_PROFILE
                                , gameMcpOwnership: kind =>
                                    _automataActionFamilyOwnership!.OwnsAutoBuy(kind)
#endif
                                )),
                        new SpellLevelServiceCycleFeature(
                            new SpellLevelFeatureDependencies(
                                readAutoHarvestLifecycleEpoch,
                                ownsActionFamily: () => _automataActionFamilyOwnership!.OwnsSpellLevel,
                                capability: _spellLevelCapability,
                                featureStatus: featureStatuses.SpellLevel)),
                        new AutoCastServiceCycleFeature(
                            new AutoCastFeatureDependencies(
                                readAutoHarvestLifecycleEpoch,
                                ownsActionFamily: () => _automataActionFamilyOwnership!.OwnsCast,
                                _autoCastManualPause,
                                featureStatus: featureStatuses.AutoCast)),
                        new AutoConceptServiceCycleFeature(
                            new AutoConceptFeatureDependencies(
                                readAutoHarvestLifecycleEpoch,
                                ownsActionFamily: () => _automataActionFamilyOwnership!.OwnsConcept,
                                featureStatus: featureStatuses.AutoConcept)),
                        new MentorServiceCycleFeature(
                            new MentorFeatureDependencies(
                                readAutoHarvestLifecycleEpoch,
                                captureMutationPermit: domain =>
                                    _mentorActionFamilyOwnership!.TryCaptureMutationPermit(
                                        domain switch
                                        {
                                            MasteryExperienceDomain.Spell => MentorDomain.Spells,
                                            MasteryExperienceDomain.Artifact => MentorDomain.Artifacts,
                                            _ => MentorDomain.Alchemy,
                                        }) == true,
                                featureStatus: featureStatuses.Mentor)),
                    },
                    Log
#if SERVICE_CYCLE_PROFILE
                    , createDiscoveryTreeOffers: () => new DiscoveryTreeOfferGameAction(
                        readAutoHarvestLifecycleEpoch,
                        tryCaptureMutationPermit: () =>
                            _automataActionFamilyOwnership!
                                .TryCaptureDiscoveryTreeOfferMutationPermit(),
                        readOwnershipFailure: () =>
                            _automataActionFamilyOwnership!
                                .DiscoveryTreeOfferOwnershipFailure)
                    , createSpellWorkbench: () => new SpellWorkbenchGameAction(
                        readAutoHarvestLifecycleEpoch,
                        tryCaptureMutationPermit: () =>
                            _automataActionFamilyOwnership!
                                .TryCaptureSpellWorkbenchMutationPermit(),
                        readOwnershipFailure: () =>
                            _automataActionFamilyOwnership!
                                .SpellWorkbenchOwnershipFailure)
                    , createSpellComposition: () => new SpellCompositionGameAction(
                        readAutoHarvestLifecycleEpoch,
                        tryCaptureMutationPermit: () =>
                            _automataActionFamilyOwnership!
                                .TryCaptureSpellCompositionMutationPermit(),
                        readOwnershipFailure: () =>
                            _automataActionFamilyOwnership!
                                .SpellCompositionOwnershipFailure)
                    , createSpellLoadout: () => new SpellLoadoutGameAction(
                        readAutoHarvestLifecycleEpoch,
                        tryCaptureMutationPermit: () =>
                            _automataActionFamilyOwnership!
                                .TryCaptureSpellLoadoutMutationPermit(),
                        readOwnershipFailure: () =>
                            _automataActionFamilyOwnership!
                                .SpellLoadoutOwnershipFailure)
                    , createTargeting: () => new TargetingGameAction(
                        readAutoHarvestLifecycleEpoch,
                        tryCaptureMutationPermit: () =>
                            _automataActionFamilyOwnership!
                                .TryCaptureTargetingMutationPermit(),
                        readOwnershipFailure: () =>
                            _automataActionFamilyOwnership!
                                .TargetingOwnershipFailure)
                    , createGenericDiscovery: () => new GenericDiscoveryGameAction(
                        readAutoHarvestLifecycleEpoch,
                        tryCaptureMutationPermit: () =>
                            _automataActionFamilyOwnership!
                                .TryCaptureGenericDiscoveryMutationPermit(),
                        readOwnershipFailure: () =>
                            _automataActionFamilyOwnership!
                                .GenericDiscoveryOwnershipFailure)
                    , createEquipmentLoadout: () => new EquipmentLoadoutGameAction(
                        readAutoHarvestLifecycleEpoch,
                        tryCaptureMutationPermit: () =>
                            _automataActionFamilyOwnership!
                                .TryCaptureEquipmentLoadoutMutationPermit(),
                        readOwnershipFailure: () =>
                            _automataActionFamilyOwnership!
                                .EquipmentLoadoutOwnershipFailure)
                    , createAlchemyLoadout: () => new AlchemyLoadoutGameAction(
                        readAutoHarvestLifecycleEpoch,
                        tryCaptureMutationPermit: () =>
                            _automataActionFamilyOwnership!
                                .TryCaptureAlchemyLoadoutMutationPermit(),
                        readOwnershipFailure: () =>
                            _automataActionFamilyOwnership!
                                .AlchemyLoadoutOwnershipFailure)
                    , createRitualLifecycle: () => new RitualLifecycleGameAction(
                        readAutoHarvestLifecycleEpoch,
                        tryCaptureMutationPermit: () =>
                            _automataActionFamilyOwnership!
                                .TryCaptureRitualLifecycleMutationPermit(),
                        readOwnershipFailure: () =>
                            _automataActionFamilyOwnership!
                                .RitualLifecycleOwnershipFailure)
                    , createGenericLevel: () => new GenericLevelGameAction(
                        readAutoHarvestLifecycleEpoch,
                        tryCaptureMutationPermit: () =>
                            _automataActionFamilyOwnership!
                                .TryCaptureGenericLevelMutationPermit(),
                        readOwnershipFailure: () =>
                            _automataActionFamilyOwnership!
                                .GenericLevelOwnershipFailure)
                    , createCraftingStations: () => new CraftingStationGameAction(
                        readAutoHarvestLifecycleEpoch,
                        tryCaptureMutationPermit: () =>
                            _automataActionFamilyOwnership!
                                .TryCaptureScribeMutationPermit(),
                        readOwnershipFailure: () =>
                            _automataActionFamilyOwnership!
                                .ScribeOwnershipFailure)
                    , createCraftingInstances: () => new CraftingInstanceLifecycleGameAction(
                        readAutoHarvestLifecycleEpoch,
                        tryCaptureMutationPermit: () =>
                            _automataActionFamilyOwnership!
                                .TryCaptureScribeMutationPermit(),
                        readOwnershipFailure: () =>
                            _automataActionFamilyOwnership!
                                .ScribeOwnershipFailure)
                    , createLoadouts: (equipment, alchemy) => new LoadoutGameAction(
                        readAutoHarvestLifecycleEpoch,
                        tryCaptureMutationPermit: () =>
                            _automataActionFamilyOwnership!
                                .TryCapturePlayerLoadoutMutationPermit(),
                        readOwnershipFailure: () =>
                            _automataActionFamilyOwnership!
                                .PlayerLoadoutOwnershipFailure,
                        equipment,
                        alchemy)
                    , createHarvestLifecycle: () => new HarvestLifecycleGameAction(
                        readAutoHarvestLifecycleEpoch,
                        tryCaptureMutationPermit: () =>
                            _automataActionFamilyOwnership!
                                .TryCaptureHarvestLifecycleMutationPermit(),
                        readOwnershipFailure: () =>
                            _automataActionFamilyOwnership!
                                .HarvestLifecycleOwnershipFailure)
                    , createPlotLifecycle: () => new PlotLifecycleGameAction(
                        readAutoHarvestLifecycleEpoch,
                        tryCaptureMutationPermit: () =>
                            _automataActionFamilyOwnership!
                                .TryCaptureHarvestMutationPermit(),
                        readOwnershipFailure: () =>
                            _automataActionFamilyOwnership!
                                .HarvestOwnershipFailure)
                    , createStructureLifecycle: () => new StructureLifecycleGameAction(
                        readAutoHarvestLifecycleEpoch,
                        tryCaptureMutationPermit: () =>
                            _automataActionFamilyOwnership!
                                .TryCaptureStructureLifecycleMutationPermit(),
                        readOwnershipFailure: () =>
                            _automataActionFamilyOwnership!
                                .StructureLifecycleOwnershipFailure)
                    , createReturnToMenu: () => new ReturnToMenuGameAction(
                        readAutoHarvestLifecycleEpoch,
                        tryCaptureMutationPermit: () =>
                            _automataActionFamilyOwnership!
                                .TryCaptureRunTransitionMutationPermit(),
                        readOwnershipFailure: () =>
                            _automataActionFamilyOwnership!
                                .RunTransitionOwnershipFailure,
                        readScene: () => SceneManager.GetActiveScene().name,
                        findLoadedObjects: type => Resources.FindObjectsOfTypeAll(type)
                            .Cast<object>()
                            .ToArray())
                    , createChallenges: () => new ChallengeGameAction(
                        readAutoHarvestLifecycleEpoch,
                        tryCaptureMutationPermit: () =>
                            _automataActionFamilyOwnership!
                                .TryCaptureChallengeMutationPermit(),
                        readOwnershipFailure: () =>
                            _automataActionFamilyOwnership!
                                .ChallengeOwnershipFailure)
                    , createPrestige: () => new PrestigeGameAction(
                        readAutoHarvestLifecycleEpoch,
                        tryCaptureMutationPermit: () =>
                            _automataActionFamilyOwnership!
                                .TryCapturePrestigeMutationPermit(),
                        readOwnershipFailure: () =>
                            _automataActionFamilyOwnership!
                                .PrestigeOwnershipFailure)
                    , createResearch: () => new ResearchGameAction(
                        readAutoHarvestLifecycleEpoch,
                        tryCaptureMutationPermit: () =>
                            _automataActionFamilyOwnership!
                                .TryCaptureResearchMutationPermit(),
                        readOwnershipFailure: () =>
                            _automataActionFamilyOwnership!
                                .ResearchOwnershipFailure)
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
        _invalidationBus ??= GameplayInvalidationBus.Shared;
        _uiSurfaceDiagnostics = new SuiteUiSurfaceDiagnostics(
            RuntimeDiagnosticsRegistry.Shared,
            message => Log.LogAutomataInfo(message),
            message => Log.LogAutomataError(message));
        _runtimeSources = new ModConfigRuntimeSources(
            ConfigurationSchemaStatusRegistry.Shared,
            FeatureStatusRegistry.Shared,
            RuntimeDiagnosticsRegistry.Shared,
            ServiceActionOutcomeWindowSources.Shared,
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

        // The shared bus owns one process-wide operation cap and sequence cutoff per Unity frame.
        if (_modConfigSettings is not null)
        {
            _invalidationBus?.Pump(
                Time.frameCount,
                GameplayInvalidationBus.DefaultMaxOperationsPerFrame);
        }

        UpdateAutomata();
        _diagnosticsBundleController?.Tick();
#if SERVICE_CYCLE_PROFILE
        DrainGameMcpOperations();
#endif
        UpdateMentor();
        UpdateUiStartupReadiness(Time.unscaledDeltaTime);
        UpdateQuickControls(Time.unscaledDeltaTime);
        UpdateModConfig();
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

        _invalidationBus = null;
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
        EntityIdentityCatalog.Shared.Reset(_lifecycleGeneration);
        _serviceCycleActivation?.InvalidateLifecycle();
#if SERVICE_CYCLE_PROFILE
        _modalDismissGameAction?.InvalidateLifecycle();
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
    /// Every source counts. This used to hang off the invalidation the suite's own settings panel
    /// raises, so a setting changed through BepInEx's configuration manager or by editing the file
    /// updated what the panel showed and never advanced a generation: the services kept deciding
    /// against the previous reading until something unrelated republished.
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
        if (!TryExecuteGameMcpFrameOperation(operation, context, out var result))
            return null;
        return result.WithEntityIdentities(EntityIdentities(context));
    }

    private static GameMcpToolExecution ProjectGameMcpFrameOperationFault(
        GameMcpFrameOperation operation,
        GameMcpFrameContext? context,
        Exception exception)
    {
        var result = new GameMcpObjectBuilder
        {
            ["status"] = "faulted",
            ["code"] = "operation_dispatch_fault",
            ["reason"] = exception.GetBaseException().Message,
        };
        GameMcpValue payload = result.Freeze();
        if (context is not null && operation.Request.Classification == GameMcpOperationClass.ReadOnly &&
            (operation.Request.RequiredData & GameMcpFrameData.World) != 0)
        {
            payload = GameMcpWorldQuery.WithEnvelope(context, payload);
        }
        return GameMcpToolExecution.Error(payload).WithEntityIdentities(
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
            _modalDismissGameAction?.BindingsAvailable == true,
            _modalDismissGameAction?.BindingFailure ??
                "the modal action boundary was not composed",
            GameLifecycleMonitor.Shared.Current.State,
            _gameMcpAgentSettingsFailure);
    }

    private bool TryExecuteGameMcpFrameOperation(
        GameMcpFrameOperation operation,
        GameMcpFrameContext context,
        out GameMcpToolExecution execution)
    {
        var request = operation.Request;
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
                    request.LimitFromCaller).Freeze());
                return true;
            case "world_get":
                execution = GameMcpToolExecution.Read(
                    GameMcpWorldQuery.GetRows(
                        context,
                        request.Category,
                        request.Uuids).Freeze());
                return true;
            case "entity_catalog":
                execution = GameMcpToolExecution.Read(
                    GameMcpEntityCatalog.Search(
                        EntityIdentities(context), request.Query, request.Offset,
                        request.Limit).Freeze());
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
                        request.LimitFromCaller).Freeze());
                return true;
            case "suite_health":
                execution = GameMcpToolExecution.Text(ProjectGameMcpHealthText(context));
                return true;
            case "suite_check_game_math":
                execution = RunGameMcpGameMathCheck();
                return true;
            case "game_screen_catalog":
                execution = GameMcpToolExecution.Read(CaptureScreenCatalogGameMcp());
                return true;
            case "suite_configuration":
                execution = GameMcpToolExecution.Read(
                    ProjectGameMcpConfiguration(context, request.Mode == "describe"));
                return true;
            case "suite_automation" when request.Mode == "list":
                execution = GameMcpToolExecution.Read(
                    ProjectGameMcpAutomationFeatures(context));
                return true;
            case "time_challenge" when request.Mode == "state":
                execution = GameMcpToolExecution.Read(
                    GameMcpWorldQuery.ChallengeStateRead(context).Freeze());
                return true;
            case "trace_health":
                execution = GameMcpToolExecution.Text(ProjectGameMcpTraceHealthText(context));
                return true;
            case "game_discover" when request.Mode == "preview":
                execution = GameMcpToolExecution.Read(
                    GameMcpWorldQuery.ProjectDiscoveryPreview(
                        context,
                        request.Key,
                        request.UuidCounts));
                return true;
            case "game_spell_loadout" when request.Mode == "preview":
                var glyphs = new SpellWorkbenchGlyphStack[request.UuidCounts.Length];
                for (var index = 0; index < glyphs.Length; index++)
                    glyphs[index] = new SpellWorkbenchGlyphStack(
                        request.UuidCounts[index].Uuid,
                        request.UuidCounts[index].Count);
                var previewRequest = new SpellWorkbenchPricePreviewRequest(
                    request.Uuid,
                    _lifecycleGeneration,
                    glyphs);
                SpellWorkbenchPricePreview preview;
                if (_serviceCycleActivation is null ||
                    !_serviceCycleActivation.TryPreviewSpellWorkbench(
                        in previewRequest,
                        out preview))
                {
                    preview = SpellWorkbenchPricePreview.Refused(
                        SpellWorkbenchPreflight.ContractUnavailable,
                        "The ServiceCycle runtime is not active in this scene.");
                }
                execution = GameMcpToolExecution.Read(
                    GameMcpSpellWorkbenchProjection.ProjectPricePreview(
                        in preview));
                return true;
            case "game_spell_loadout" when request.Mode == "staged":
                SpellWorkbenchStagedLayout staged;
                if (_serviceCycleActivation is null ||
                    !_serviceCycleActivation.TryReadStagedSpellWorkbench(out staged))
                {
                    staged = SpellWorkbenchStagedLayout.Unavailable(
                        SpellWorkbenchPreflight.ContractUnavailable,
                        "The ServiceCycle runtime is not active in this scene.");
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
                result = GameMcpCommandResult.Rejected(
                    "runtime_not_available",
                    "the ServiceCycle runtime is not active in this scene");
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
        CompleteGameMcpCommand(command, committed.WithDetails(state));
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
            return GameMcpToolExecution.Text(ProjectGameMcpHealthText(context));
        if (uri == "orb://suite/configuration")
            return GameMcpToolExecution.Read(ProjectGameMcpConfiguration(context, describe: false));
        if (uri == "orb://trace/health")
            return GameMcpToolExecution.Text(ProjectGameMcpTraceHealthText(context));
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

    internal static string ProjectGameMcpHealthText(GameMcpFrameContext context)
    {
        var stopped = context.Runtime?.EmergencyStopEngaged ??
            context.Configuration.Snapshot.Safety.EmergencyDisable;
        var result = new StringBuilder()
            .AppendLine("available")
            .Append("build: ").Append(PluginIds.Version).Append(" dll sha256 ")
            .AppendLine(GameMcpDllSha256)
            .Append("scene: ").AppendLine(GameMcpTextFormatter.Plain(context.SceneName))
            // The same lifecycle fact game_probe reports and the world reads refuse on, so the three
            // cannot hold three beliefs about whether a game exists.
            .Append("lifecycle: ").Append(context.LifecycleState.ToString())
            .Append(", generation ")
            .AppendLine(context.LifecycleGeneration.ToString(CultureInfo.InvariantCulture))
            // The scene name alone cannot tell a caller which run a verdict describes: the runtime
            // outlives every scene change, so the same scene answered both ways across one session.
            // The world generation is what actually moves, so it is published — and a lifecycle
            // boundary flushes the publication, so this reads "not published" again once the run
            // it described is gone.
            .Append("world: ").AppendLine(GameMcpWorldQuery.IsWorldPublished(context)
                ? "generation " +
                    context.World!.Generation.Value.ToString(CultureInfo.InvariantCulture)
                : "not published")
            .Append("emergency stop: ").AppendLine(stopped ? "engaged" : "clear");
        // Health is exception-shaped, the way it already is for features and services: a capability
        // that works says nothing, so every line on the page is a thing the caller has to act on.
        // The page leads with "available", which is the standing answer for everything unlisted.
        if (!context.RuntimeAvailable) result.AppendLine("runtime: unavailable");
        if (!context.NativeContractsAvailable) result.AppendLine("native contracts: unavailable");
        if (context.Runtime is not
            { PlayerCraftingAvailable: true, CraftingInstancesAvailable: true })
            result.AppendLine("game_craft: unavailable");
        if (!context.ModalDismissAvailable) result.AppendLine("game_modal: unavailable");
        if (!context.RuntimeAvailable && context.RuntimeNotAvailableReason.Length > 0)
            result.Append("runtime reason: ").AppendLine(
                GameMcpTextFormatter.Plain(context.RuntimeNotAvailableReason));
        else if (context.Runtime is { } runtime)
        {
            var craftingReason = !runtime.PlayerCraftingAvailable
                ? runtime.PlayerCraftingUnavailableReason
                : !runtime.CraftingInstancesAvailable
                    ? runtime.CraftingInstancesUnavailableReason
                    : string.Empty;
            if (craftingReason.Length > 0)
                result.Append("game_craft reason: ").AppendLine(
                    GameMcpTextFormatter.Plain(craftingReason));
        }
        if (!context.ModalDismissAvailable && context.ModalDismissUnavailableReason.Length > 0)
            result.Append("game_modal reason: ").AppendLine(
                GameMcpTextFormatter.Plain(context.ModalDismissUnavailableReason));
        // The load normalizes three settings the documented verbs assume, and a caller never asked
        // for them — so when one did not land, the develop queue and the spell cancel it silently
        // costs are refusals nothing else on the wire can explain.
        if (context.AgentSettingsFailure.Length > 0)
            result.Append("agent settings: ").AppendLine(
                GameMcpTextFormatter.Plain(context.AgentSettingsFailure));

        var featureGroups = context.FeatureStatuses
            .GroupBy(feature => new { feature.State, feature.Reason.Code })
            .OrderBy(group => group.Key.State.ToString(), StringComparer.Ordinal)
            .ThenBy(group => group.Key.Code.ToString(), StringComparer.Ordinal);
        foreach (var group in featureGroups)
        {
            var state = GameMcpEntityWireNormalizer.Snake(group.Key.State.ToString());
            var reasonCode = GameMcpEntityWireNormalizer.Snake(group.Key.Code.ToString());
            result.Append("features ").Append(state);
            if (reasonCode.Length > 0 && reasonCode != "none" &&
                !string.Equals(reasonCode, state, StringComparison.Ordinal))
                result.Append(" (").Append(reasonCode).Append(')');
            result.Append(": ").AppendLine(string.Join(", ", group.Select(
                feature => GameMcpTextFormatter.Plain(
                    CanonicalGameMcpFeatureName(feature.DisplayName)))));
        }

        var runtimeServices = context.Runtime?.Services ?? Array.Empty<AutomataServiceFrameFacts>();
        var serviceGroups = runtimeServices.GroupBy(service =>
            service.HasRunner
                ? service.Runner.Fault.IsValid ? "faulted" : service.Runner.Phase.ToString()
                : "unavailable");
        foreach (var group in serviceGroups.OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            result.Append("services ")
                .Append(GameMcpEntityWireNormalizer.Snake(group.Key))
                .Append(": ")
                .AppendLine(string.Join(", ", group.Select(
                    service => GameMcpTextFormatter.Plain(
                        CanonicalGameMcpFeatureName(service.DisplayName)))));
        }
        return result.ToString().TrimEnd();
    }

    private static string CanonicalGameMcpFeatureName(string name) =>
        string.Equals(name, "Orb Mentor", StringComparison.Ordinal) ? "Mentor" : name;

    /// <summary>
    /// The committed value of every writable setting, one setting per line.
    /// </summary>
    /// <remarks>
    /// What a setting means and which values it takes do not change between calls, so they are
    /// documentation, not an answer: the ordinary read is the values a caller came for, and
    /// <c>mode=describe</c> is where the type, the domain — a range or a list of names, said the
    /// same way for both — and the sentence live for whoever is deciding what to write.
    /// </remarks>
    internal static GameMcpValue ProjectGameMcpConfiguration(
        GameMcpFrameContext context,
        bool describe)
    {
        if (!context.ConfigurationGeneration.IsValid)
        {
            return new GameMcpObjectBuilder
            {
                ["status"] = "not_available",
                ["code"] = "configuration_unpublished",
                ["reason"] = "no committed configuration has been published yet",
            }.Freeze();
        }
        var described = new GameMcpArrayBuilder();
        var values = new GameMcpObjectBuilder();
        for (var index = 0; index < context.WritableConfiguration.Length; index++)
        {
            var item = context.WritableConfiguration[index];
            var value = CanonicalConfigurationValue(
                GameMcpConfigurationSchema.SerializePublishedValue(
                    context.Configuration.Snapshot,
                    item.Section,
                    item.Key),
                item.SettingType);
            if (!describe)
            {
                values[item.Section + "/" + item.Key] = value;
                continue;
            }
            var setting = new GameMcpObjectBuilder
            {
                ["setting"] = item.Section + "/" + item.Key,
                ["value"] = value,
                ["type"] = item.SettingType,
            };
            var domain = item.Constraint.Domain.Length > 0
                ? item.Constraint.Domain
                : PlainConfigurationDomain(item.Constraint.AcceptableValues);
            if (domain.Length > 0) setting["domain"] = domain;

            // The same two numbers a refused write hands back. A caller that has to parse a range
            // out of prose before it may write is a caller that will get the parse wrong once.
            GameMcpConfigurationValuePolicy.AddBound(setting, item.Constraint.Bound);
            setting["description"] = item.Description;
            described.Add(setting);
        }
        var result = new GameMcpObjectBuilder();
        if (describe)
        {
            if (described.Count > 0) result["settings"] = described;
            return result.Freeze();
        }
        result.CopyFrom(values);
        return result.Freeze();
    }

    internal static GameMcpValue ProjectGameMcpAutomationFeatures(GameMcpFrameContext context)
    {
        var config = context.Configuration.Snapshot;
        var features = new GameMcpArrayBuilder();
        foreach (var feature in GameMcpAutomationFeatures.All)
        {
            var row = new GameMcpObjectBuilder
            {
                ["feature"] = feature.Name,
            };

            // On six of seven rows the display name is the id in title case, so a whole column
            // repeated the column beside it. The one feature whose screen name is not its id says
            // so, and every other row is read straight off the id the verb already takes.
            if (!string.Equals(
                    feature.DisplayName,
                    GameMcpAutomationFeatures.TitleCased(feature.Name),
                    StringComparison.Ordinal))
            {
                row["name"] = feature.DisplayName;
            }
            row["on"] = feature.IsOn(config);
            features.Add(row);
        }
        var result = new GameMcpObjectBuilder
        {
            ["features"] = features,
        };
        // Both of these silence every feature that reads as on, so a list that omitted them would
        // be answering a different question than the caller asked.
        GameMcpAutomationFeatures.AddSuiteOverrides(result, config);
        return result.Freeze();
    }

    internal static GameMcpValue ProjectGameMcpAutomationCommit(
        GameMcpAutomationFeature feature,
        bool wasOn,
        SuiteRuntimeConfiguration settled)
    {
        var result = new GameMcpObjectBuilder
        {
            ["feature"] = feature.Name,
            ["name"] = feature.DisplayName,
            ["on"] = new GameMcpObjectBuilder
            {
                ["before"] = wasOn,
                ["after"] = feature.IsOn(settled),
            },
        };
        // A caller who turns a feature on under an engaged stop has to read that here, in the
        // answer to the write, not on a later list call.
        GameMcpAutomationFeatures.AddSuiteOverrides(result, settled);
        return result.Freeze();
    }

    private static object CanonicalConfigurationValue(string value, string settingType)
    {
        if (string.Equals(settingType, "Boolean", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(settingType, "bool", StringComparison.OrdinalIgnoreCase))
        {
            return value.ToLowerInvariant();
        }

        // An allowlist stored as joined UUIDs printed 288 characters that resolved to nothing a
        // caller could use, on a surface that says every other id as a named handle. Written as the
        // list it is, each entry crosses the wire the way every other entity reference does.
        var entries = TryConfigurationIdentityList(value);
        if (entries is null) return value;
        var list = new GameMcpArrayBuilder();
        for (var index = 0; index < entries.Length; index++) list.Add(entries[index]);
        return list;
    }

    /// <summary>The whole UUIDs a joined setting holds, or nothing when it holds something else.</summary>
    private static string[]? TryConfigurationIdentityList(string value)
    {
        if (value.Length == 0) return null;
        var entries = value.Split(',');
        for (var index = 0; index < entries.Length; index++)
        {
            entries[index] = entries[index].Trim();
            if (!Guid.TryParseExact(entries[index], "D", out _)) return null;
        }
        return entries;
    }

    private static string PlainConfigurationDomain(string value)
    {
        var result = (value ?? string.Empty).Trim();
        const string marker = "# Acceptable value range:";
        if (result.StartsWith(marker, StringComparison.OrdinalIgnoreCase))
            result = result.Substring(marker.Length).Trim();
        return result;
    }

    internal static string ProjectGameMcpTraceHealthText(GameMcpFrameContext context)
    {
        var status = context.TraceWriterStatus;
        if (status.State == DecisionJournalStatusState.Unavailable)
            return "unavailable\nreason: the decision journal writer is not active in this runtime";
        var result = new StringBuilder()
            .AppendLine("available")
            .Append("trace writer: ").AppendLine(
                GameMcpEntityWireNormalizer.Snake(status.State.ToString()));
        var outcome = GameMcpEntityWireNormalizer.Snake(status.Result.ToString());
        if (outcome.Length > 0 && outcome != "none")
            result.Append("result: ").AppendLine(outcome);
        result.Append("records: accepted ").Append(
                status.AcceptedRecords.ToString(CultureInfo.InvariantCulture))
            .Append(", written ").Append(
                status.WrittenRecords.ToString(CultureInfo.InvariantCulture))
            .Append(", discarded ").AppendLine(
                status.DiscardedRecords.ToString(CultureInfo.InvariantCulture))
            .Append("bytes written: ").AppendLine(
                status.BytesWritten.ToString(CultureInfo.InvariantCulture))
            .Append("segments: written ").Append(
                status.WrittenSegments.ToString(CultureInfo.InvariantCulture))
            .Append(", retained ").AppendLine(
                status.RetainedSegments.ToString(CultureInfo.InvariantCulture))
            .Append("pending blocks: ").Append(
                status.PendingBlocks.ToString(CultureInfo.InvariantCulture))
            .Append(" (peak ").Append(
                status.PeakPendingBlocks.ToString(CultureInfo.InvariantCulture))
            .AppendLine(")");
        if (status.ArtifactName.Length > 0)
            result.Append("artifact: ").AppendLine(
                GameMcpTextFormatter.Plain(status.ArtifactName));
        if (status.FaultSite.Length > 0)
            result.Append("fault site: ").AppendLine(
                GameMcpTextFormatter.Plain(status.FaultSite));
        if (status.FaultMessage.Length > 0)
            result.Append("fault: ").AppendLine(
                GameMcpTextFormatter.Plain(status.FaultMessage));
        result.Append("revision: ").Append(
            context.TraceWriterRevision.ToString(CultureInfo.InvariantCulture));
        return result.ToString();
    }

    private static string ComputeExecutingDllSha256()
    {
        try
        {
            var path = Assembly.GetExecutingAssembly().Location;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return "unavailable";
            using var stream = File.OpenRead(path);
            using var sha = SHA256.Create();
            // A build fingerprint answers one question: same DLL or not. Six bytes settle it; the
            // remaining twenty-six were a constant sixty-four-character tax on every health call.
            return string.Concat(sha.ComputeHash(stream).Take(6).Select(
                value => value.ToString("x2", CultureInfo.InvariantCulture)));
        }
        catch (Exception)
        {
            return "unavailable";
        }
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
            if (request.ToolName == "game_discover")
            {
                mode = "discover";
                payloadKey = request.Key;
                if (context.World is null)
                    preparationFailure = GameMcpCommandResult.Rejected(
                        "world_not_published",
                        context.RuntimeNotAvailableReason);
                else if (!GameMcpWorldQuery.TryResolveSpellDiscovery(
                             context.World.Snapshot, request.Key, request.UuidCounts,
                             out targetId, out var resolutionReason))
                    preparationFailure = GameMcpCommandResult.Rejected(
                        "discovery_recipe_unresolved", resolutionReason);
            }
            else if (request.ToolName == "game_spell_loadout")
                mode = "create";
        }
        else if (kind == GameMcpCommandKind.SpellComposition)
        {
            nativeType = "IntVariable";

            // Two dials share one screen, so the committed response names the one it moved.
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
            nativeType = "CraftingRecipeSO";
        else if (kind == GameMcpCommandKind.GenericDiscovery)
        {
            payloadKey = request.Key;
            if (context.World is null)
                preparationFailure = GameMcpCommandResult.Rejected(
                    "world_not_published",
                    context.RuntimeNotAvailableReason);
            else if (!GameMcpWorldQuery.TryResolveGenericDiscovery(
                         context.World.Snapshot,
                         request.Key,
                         request.UuidCounts,
                         out targetId,
                         out nativeType,
                         out _,
                         out var resolutionCode,
                         out var resolutionReason))
                preparationFailure = GameMcpCommandResult.Rejected(
                    resolutionCode,
                    resolutionReason);
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
        else if (kind == GameMcpCommandKind.CraftingStation)
        {
            nativeType = "CraftingStructure";
            if (request.Mode == "set_ingredient") amount = request.SlotIndex;
            else if (request.Mode == "set_level") amount = request.Amount;
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
                "the frame has no published configuration");
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
                    reason = identity + " lives under " + owningCategory +
                        "; use " + owningTool + " for its player action";
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
                "the committed suite configuration store is not composed");
        var expected = command.ExpectedConfigurationGeneration;
        var before = _configurationStore.CurrentGeneration;
        if (expected != before.Value)
        {
            return GameMcpCommandResult.Rejected(
                "stale_configuration_generation",
                "command expected configuration generation " + expected +
                " but the main thread now has generation " + before.Value,
                observedConfigurationGeneration: before.Value);
        }

        if (command.Kind == GameMcpCommandKind.ConfigurationSet)
        {
            var priorValue = GameMcpConfigurationSchema.SerializePublishedValue(
                _configurationStore.Current,
                command.Mode,
                command.PayloadKey);
            if (!TryAdmitConfigurationWriteAgainstWorld(command, context, out var worldFailure))
                return worldFailure;
            if (!_configurationStore.TrySetGameMcp(
                    command.Mode,
                    command.PayloadKey,
                    command.PayloadValue,
                    before,
                    out var reason,
                    out var bound))
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
                            ["before"] = priorValue,
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
            if (!_configurationStore.TrySetGameMcp(
                    feature.Section,
                    feature.Key,
                    command.PayloadValue,
                    before,
                    out var automationReason,
                    out _))
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
                details: ProjectGameMcpAutomationCommit(
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
            if (_modalDismissGameAction is null)
            {
                result = GadgetRejected("contract_unavailable",
                    "The native modal close control is unavailable.");
                return true;
            }
            var submission = _modalDismissGameAction.Submit();
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
                "the audited Continue action exists only on the Start scene",
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
            return GameMcpCommandResult.Rejected(
                "continue_contract_unavailable",
                "the audited SaveStateManager.StartGame contract could not be resolved",
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

        var details = new GameMcpObjectBuilder
        {
            ["scene"] = state.SceneName,
            ["runtimeAvailable"] = state.RuntimeAvailable,
        };
        if (!state.RuntimeAvailable && state.RuntimeNotAvailableReason.Length > 0)
            details["runtimeReason"] = state.RuntimeNotAvailableReason;
        CompleteGameMcpCommand(command, committed.WithDetails(details.Freeze()));
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
                    "server defect: the end-of-frame screenshot did not finish: " +
                    exception.GetBaseException().Message,
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
            return ProjectGameMcpScreenCatalog(
                scene,
                navigationAvailable: false,
                Array.Empty<(string Label, bool Active)>(),
                Array.Empty<(string Strip, string Label, bool Active)>());
        var tabs = _uiShell.CaptureNativeTabsForGameMcp();
        var subtabs = CaptureSubtabs();
        return ProjectGameMcpScreenCatalog(
            scene,
            navigationAvailable: true,
            tabs.Select(tab => (tab.Label, tab.Active)).ToArray(),
            subtabs.Select(subtab => (subtab.StripKey, subtab.Label, subtab.Active)).ToArray());
    }

    internal static GameMcpValue ProjectGameMcpScreenCatalog(
        string scene,
        bool navigationAvailable,
        IReadOnlyList<(string Label, bool Active)> tabs,
        IReadOnlyList<(string Strip, string Label, bool Active)> subtabs)
    {
        var result = new GameMcpObjectBuilder
        {
            ["status"] = navigationAvailable ? "available" : "unavailable",
            ["scene"] = scene,
            ["navigationAvailable"] = navigationAvailable,
        };
        if (!navigationAvailable)
        {
            result["reasonCode"] = "navigation_unavailable";
            result["reason"] = "the Main scene navigation shell is not alive";
            result["screens"] = new GameMcpArrayBuilder();
            return result.Freeze();
        }
        var projectedTabs = new GameMcpArrayBuilder();
        for (var index = 0; index < tabs.Count; index++)
        {
            var tab = tabs[index];
            var projectedTab = new GameMcpObjectBuilder
            {
                ["label"] = tab.Label,
                ["active"] = tab.Active,
            };
            if (!tab.Active || subtabs.Count == 0)
            {
                projectedTabs.Add(projectedTab);
                continue;
            }
            projectedTab["subtabStrips"] = ProjectGameMcpSubtabStrips(subtabs);
            projectedTabs.Add(projectedTab);
        }
        result["screens"] = projectedTabs;
        return result.Freeze();
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

    private static GameMcpValue ProjectGameMcpSubtabStrips(
        IEnumerable<(string Strip, string Label, bool Active)> subtabs)
    {
        var projectedStrips = new GameMcpArrayBuilder();
        var strips = subtabs.GroupBy(subtab => subtab.Strip, StringComparer.Ordinal);
        foreach (var strip in strips)
        {
            var labels = new GameMcpArrayBuilder();
            var projectedStrip = new GameMcpObjectBuilder();
            foreach (var subtab in strip)
            {
                labels.Add(subtab.Label);
                if (subtab.Active) projectedStrip["active"] = subtab.Label;
            }
            projectedStrip["labels"] = labels;
            projectedStrips.Add(projectedStrip);
        }
        return projectedStrips.Freeze();
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
                "the live native navigation catalog is available only while the Main scene shell is alive");
            return false;
        }

        var request = command.SourceOperation?.Request;
        if (request?.Tab is null)
        {
            failure = GadgetRejected(
                "navigation_request_invalid",
                "the immutable navigation request has no tab selector");
            return false;
        }
        if (command.TargetId != Guid.Empty &&
            !GameMcpGadgetPolicy.IsPlotDestination(
                request.Tab.Label,
                request.Subtab?.Label))
        {
            var requestedDestination = request.Tab.Label + " > " +
                (request.Subtab?.Label ?? "no subtab");
            failure = GadgetRejected(
                "plot_destination_mismatch",
                "Agromancy plots can be selected only on World > Agromancy, not " +
                requestedDestination);
            return false;
        }
        subtabSelector = request.Subtab;
        var tabs = _uiShell.CaptureNativeTabsForGameMcp();
        if (!TryResolveTabSelector(request.Tab, tabs, out var tab, out var tabReason))
        {
            failure = NavigationRefusal(
                "screen_match_failed",
                tabReason,
                null,
                "screenCandidates",
                tabs.Select(candidate => candidate.Label));
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
            var subtabs = CaptureSubtabs();
            if (!TryResolveSubtabSelector(
                    subtabSelector,
                    subtabs,
                    out var subtab,
                    out var subtabReason))
            {
                yield return CompleteNavigateGameMcpAfterSettlement(
                    command,
                    NavigationRefusal(
                        "subtab_match_failed",
                        SubtabRefusalReason(subtabReason, settledScreen[0]),
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
        if (command.TargetId != Guid.Empty)
        {
            var plotResult = NavigateExactPlot(
                command.TargetId,
                SceneManager.GetActiveScene().name);
            if (!string.Equals(plotResult.Status, "committed", StringComparison.Ordinal))
            {
                yield return CompleteNavigateGameMcpAfterSettlement(
                    command,
                    plotResult.WithDetails(details.Freeze()));
                yield break;
            }
            details["plotNodeUuid"] = command.TargetId.ToString("D");
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
                ["subtabStrips"] = ProjectGameMcpSubtabStrips(
                    subtabs.Select(value =>
                        (value.StripKey, value.Label, value.Active))),
            };
            if (!string.IsNullOrWhiteSpace(activeTab.Label))
                details["activeScreen"] = activeTab.Label;
            if (command.TargetId != Guid.Empty)
                details["selectedPlot"] = command.TargetId.ToString("D");
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

    private GameMcpCommandResult NavigateExactPlot(
        Guid stableUuid,
        string scene)
    {
        if (scene != "Main")
        {
            return GadgetRejected(
                "wrong_scene",
                "plot selection is available only in the Main scene, not " + scene);
        }

        const string plotNativeType = "PlotNodeSO";
        var plotType = AccessTools.TypeByName(plotNativeType);
        var listType = AccessTools.TypeByName("UIPlotNodeList");
        if (plotType is null || listType is null)
        {
            return GadgetRejected(
                "native_plot_navigation_unavailable",
                "required native types are unavailable: expected " +
                plotNativeType + " and UIPlotNodeList");
        }

        var plot = TypedRegistryResolver.Shared.Resolve(stableUuid, plotType);
        if (!plot.IsResolved)
        {
            return GadgetRejected(
                "native_plot_not_resolved",
                "stable plot " + EntityIdentityFormatter.Format(stableUuid) + " as " +
                plotNativeType + " was not resolved: " + plot.Reason);
        }

        var activeLists = Resources.FindObjectsOfTypeAll(listType)
            .OfType<MonoBehaviour>()
            .Where(list => list.enabled && list.gameObject.activeInHierarchy)
            .ToArray();
        if (activeLists.Length != 1)
        {
            return GadgetRejected(
                "native_plot_list_unavailable",
                "expected exactly one active UIPlotNodeList but found " +
                activeLists.Length);
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
                "UIPlotNodeList.OnNodeClick(" + plotNativeType +
                ") -> System.Void could not be resolved");
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
                NativeViewAdapter.IsAlive(NativeViewAdapter.ReadView(candidate.Component)) &&
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
        GameMcpNavigationSelector selector,
        IReadOnlyList<GameMcpSubtab> entries,
        out GameMcpSubtab selected,
        out string reason)
    {
        var matches = new List<GameMcpSubtab>();
        if (selector.Label.Length > 0)
        {
            var requested = selector.Label;
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
            return GadgetRejected(
                "tooltip_contract_unavailable",
                _gameMcpTooltipContractFailure);
        }
        var entries = CaptureActiveHoverTooltips()
            .Where(static entry => entry.Hover.tooltipItem is not null)
            .ToArray();
        if (!int.TryParse(
                command.PayloadValue,
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out var offset))
        {
            return GadgetRejected(
                "tooltip_offset_invalid",
                "the immutable tooltip catalog offset could not be decoded");
        }
        var end = (int)Math.Min(entries.Length, (long)offset + command.Amount);

        // The prefix is the ancestry the returned rows share, not the ancestry of the whole screen.
        // Taken over the screen it collapsed to about ten characters precisely when the page was
        // long, so every row of a deep panel repeated some 240 identical characters of ancestor
        // path — the densest tokens on the wire, and about 95% of what the call spent.
        var prefix = TooltipPathPrefix(entries, offset, end);
        var projected = new GameMcpArrayBuilder();
        for (var index = offset; index < end; index++)
        {
            var entry = entries[index];
            var hover = entry.Hover;
            var item = hover.tooltipItem!;
            if (!nativeAccess.TryReadSubTooltips(hover, out var children, out var readFailure))
            {
                return GadgetRejected(
                    "tooltip_contract_unavailable",
                    readFailure);
            }
            var tooltip = new GameMcpObjectBuilder
            {
                ["path"] = NativeObjectPath.Relative(entry.Path, prefix),
                ["name"] = item.GetName(),
            };
            AddTooltipIdentity(tooltip, item);
            projected.Add(tooltip);
        }
        var details = new GameMcpObjectBuilder
        {
            ["scene"] = SceneManager.GetActiveScene().name,
            ["total"] = entries.Length,
        };
        if (projected.Count == 0)
        {
            details["columns"] = GameMcpEntityWireNormalizer.WireColumns(
                new[] { "path", "name", "uuid" });
        }
        details["rows"] = projected;
        if (prefix.Length > 0) details["pathPrefix"] = prefix;
        if (end < entries.Length) details["nextOffset"] = end;
        return GadgetCommitted(
            "tooltip_catalog_read",
            details);
    }

    private GameMcpCommandResult ReadTooltipGameMcp(GameMcpCommand command)
    {
        var requestedPath = command.PayloadValue;
        var active = CaptureActiveHoverTooltips();

        // The catalog hands out the part of the path its page's shared prefix does not already say,
        // and which prefix that was depends on which page the row came from. So a row resolves by
        // the tail it was given: the whole path, or any path ending in it at an element boundary.
        var matches = active
            .Where(entry => NativeObjectPath.Addresses(entry.Path, requestedPath))
            .ToArray();
        if (matches.Length != 1)
        {
            // Naming the next step is the point. A tail is only as unique as the page it came from,
            // and two scroll lists on one screen hand out colliding tails routinely; the catalog
            // already published the prefix that separates them, so the refusal says to put it back
            // on rather than leaving a caller to guess that a longer path exists.
            return GadgetRejected(
                "tooltip_match_failed",
                "tooltip path '" + requestedPath + "' matched " +
                matches.Length + " active current-screen elements" +
                (matches.Length > 1
                    ? "; prepend the pathPrefix game_tooltips returned with this row to name one"
                    : "; re-read game_tooltips for this screen's current paths"));
        }
        var hover = matches[0].Hover;
        if (hover.tooltipItem is null)
        {
            return GadgetRejected(
                "tooltip_content_unavailable",
                "the exact HoverTooltip has no assigned ITooltipable");
        }
        var nativeAccess = _gameMcpTooltipNativeAccess;
        if (nativeAccess is null)
        {
            return GadgetRejected(
                "tooltip_contract_unavailable",
                _gameMcpTooltipContractFailure);
        }
        if (!nativeAccess.TryReadSubTooltips(hover, out var children, out var readFailure))
        {
            return GadgetRejected(
                "tooltip_contract_unavailable",
                readFailure);
        }
        var inspected = UITooltipContainer.globalTooltips?
            .Where(panel => panel is not null && panel.item is not null)
            .Select(panel => panel.item!)
            .ToArray() ?? Array.Empty<ITooltipable>();
        GameMcpObjectBuilder details;
        try
        {
            details = GameMcpTooltipProjector.Project(
                hover.tooltipItem,
                children,
                inspected);
            AddTooltipIdentity(details, hover.tooltipItem);
        }
        catch (Exception exception)
        {
            return GadgetRejected(
                "tooltip_content_unavailable",
                "projecting the exact tooltip document threw: " +
                exception.GetBaseException().Message);
        }
        var result = GadgetCommitted(
            "tooltip_read",
            details);
        return result;
    }

    /// <summary>One live hover element with the hierarchy keys already read off it.</summary>
    private readonly struct TooltipElement
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

    private static string TooltipPathPrefix(IReadOnlyList<TooltipElement> entries, int start, int end)
    {
        var paths = new List<string>();
        for (var index = Math.Max(start, 0); index < Math.Min(end, entries.Count); index++)
            paths.Add(entries[index].Path);
        return NativeObjectPath.CommonPrefix(paths.ToArray());
    }

    private static void AddTooltipIdentity(
        GameMcpObjectBuilder result,
        ITooltipable item)
    {
        if (item is not IdScriptableObject entity) return;
        var uuid = entity.GetGuid();
        if (uuid != Guid.Empty) result["uuid"] = uuid.ToString("D");
    }

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
                if (!queue.TryReadRemainingRoom(out var remaining))
                    return GadgetRejected(
                        "native_probe_unavailable",
                        "ActionManager.GetRemainingRoom could not be resolved or returned an invalid value");
                details = new GameMcpObjectBuilder
                {
                    ["remainingRoom"] = remaining,
                };
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
                return GadgetRejected(
                    "unsupported_probe",
                    "probe '" + command.Mode +
                    "' is not allowlisted; supported probes are runtime, " +
                    "action_queue_room, and navigation");
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

    private GameMcpCommandResult GadgetRejected(string code, string reason) =>
        GameMcpCommandResult.Rejected(
            code,
            reason,
            observedLifecycleGeneration: _lifecycleGeneration,
            observedConfigurationGeneration:
                _configurationStore?.CurrentGeneration.Value ?? 0);

    /// <summary>
    /// A navigate that reached the requested screen and then failed on its subtab moved the board.
    /// The sentence says so, because a bare "refused" reads as "nothing happened" and the caller's
    /// next read finds a screen it did not ask to be on.
    /// </summary>
    private static string SubtabRefusalReason(
        string subtabReason,
        bool settled)
    {
        var arrived = settled
            ? "The requested screen is now active"
            : "The requested screen was selected but did not settle within one second";
        return arrived + " and was not left; " + subtabReason + ".";
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

    private void StandDownAutoBuy(string summary)
    {
        if (!_configurationStore!.DisableAutoBuy()) return;
        _featureStatuses!.ObserveAutoBuyInvariantStandDown(
            summary,
            _configurationStore.CurrentGeneration);
    }

    private bool IsLifecycleReady() =>
        SceneManager.GetActiveScene().name == "Main" &&
        GameLifecycleMonitor.Shared.Current.IsGameplayReady &&
        GameLifecycleMonitor.Shared.IsCurrent(_lifecycleLease);

    internal static bool AssemblyAuditAllowsMutation(AssemblyAuditResult audit) => audit.MatchesExpected;

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

    internal static void ShowNotice(string message, RectTransform? anchor)
    {
        try
        {
            var nodeType = Type.GetType("TooltipNode, Assembly-CSharp", false);
            var popupType = Type.GetType("UIPopupText, Assembly-CSharp", false);
            if (nodeType is null || popupType is null || anchor is null) return;
            var node = Activator.CreateInstance(nodeType, message, Color.white);
            var listType = typeof(System.Collections.Generic.List<>).MakeGenericType(nodeType);
            var list = (IList)Activator.CreateInstance(listType)!; list.Add(node);
            var method = popupType.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).FirstOrDefault(m => m.Name == "CreateOn" && m.GetParameters().Length == 3);
            method?.Invoke(null, new object[] { list, anchor, new Vector2(0, 32) });
        }
        catch { }
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
        var invalidationBus = _invalidationBus ??
                              throw new InvalidOperationException("Mod Config invalidation bus was not composed.");
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
                invalidationBus,
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
