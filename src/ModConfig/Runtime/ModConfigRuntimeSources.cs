using System;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Diagnostics;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal.Status;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal.Outcomes;
using OrbModding.Common.Runtime.Verification;
#if SERVICE_CYCLE_PROFILE
using OrbModding.Common.Runtime.ServiceCycle.Observation.Profile.Control;
#endif

namespace OrbModConfig;

internal sealed class ModConfigRuntimeSources
{
    public ModConfigRuntimeSources(
        IConfigurationSchemaStatusSource schemaStatuses,
        IFeatureStatusSource featureStatuses,
        IRuntimeDiagnosticsSource diagnostics,
        IServiceActionTimelineSource actionOutcomes,
        IServiceCyclePumpTimingSource pumpTiming,
        IDiagnosticsBundleControl diagnosticsBundle,
        IDifferentialVerificationControl differentialVerification,
        IDecisionJournalStatusSource decisionJournal
#if SERVICE_CYCLE_PROFILE
        , IPerformanceProfileControl performanceProfile
#endif
        )
    {
        SchemaStatuses = schemaStatuses ?? throw new ArgumentNullException(nameof(schemaStatuses));
        FeatureStatuses = featureStatuses ?? throw new ArgumentNullException(nameof(featureStatuses));
        Diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        ActionOutcomes = actionOutcomes ?? throw new ArgumentNullException(nameof(actionOutcomes));
        PumpTiming = pumpTiming ?? throw new ArgumentNullException(nameof(pumpTiming));
        DiagnosticsBundle = diagnosticsBundle ?? throw new ArgumentNullException(nameof(diagnosticsBundle));
        DifferentialVerification = differentialVerification ??
                                   throw new ArgumentNullException(nameof(differentialVerification));
        DecisionJournal = decisionJournal ?? throw new ArgumentNullException(nameof(decisionJournal));
#if SERVICE_CYCLE_PROFILE
        PerformanceProfile = performanceProfile ?? throw new ArgumentNullException(nameof(performanceProfile));
#endif
    }

    public IConfigurationSchemaStatusSource SchemaStatuses { get; }
    public IFeatureStatusSource FeatureStatuses { get; }
    public IRuntimeDiagnosticsSource Diagnostics { get; }
    public IServiceActionTimelineSource ActionOutcomes { get; }
    public IServiceCyclePumpTimingSource PumpTiming { get; }
    public IDiagnosticsBundleControl DiagnosticsBundle { get; }
    public IDifferentialVerificationControl DifferentialVerification { get; }
    public IDecisionJournalStatusSource DecisionJournal { get; }
#if SERVICE_CYCLE_PROFILE
    public IPerformanceProfileControl PerformanceProfile { get; }
#endif
}
