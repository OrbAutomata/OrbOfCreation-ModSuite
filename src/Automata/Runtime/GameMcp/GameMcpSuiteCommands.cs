#if SERVICE_CYCLE_PROFILE
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata.GameMcp;

internal static class GameMcpSuiteCommands
{
    internal static GameMcpCommandDescriptor ConfigurationSet { get; } = new(
        GameMcpCommandKind.ConfigurationSet,
        AtCurrentGeneration(SetConfiguration),
        prepare: static (request, _, draft) =>
        {
            draft.Mode = request.Section;
            draft.PayloadKey = request.Key;
            draft.PayloadValue = request.SerializedValue;
        });

    internal static GameMcpCommandDescriptor AutomationSet { get; } = new(
        GameMcpCommandKind.AutomationSet,
        AtCurrentGeneration(SetAutomation),
        prepare: static (request, _, draft) =>
        {
            draft.PayloadKey = request.Key;
            draft.PayloadValue = request.SerializedValue;
        });

    internal static GameMcpCommandDescriptor EmergencyStop { get; } = new(
        GameMcpCommandKind.EmergencyStop,
        AtCurrentGeneration(SetEmergencyStop));

    private delegate GameMcpCommandResult SuiteWrite(
        GameMcpCommand command,
        GameMcpFrameContext context,
        GameMcpFrameCommandScope scope,
        ConfigGeneration before);

    private static GameMcpFrameCommandExecutor AtCurrentGeneration(SuiteWrite write) =>
        (command, context, scope) =>
        {
            var expected = command.ExpectedConfigurationGeneration;
            var before = scope.ConfigurationStore.CurrentGeneration;
            if (expected != before.Value)
            {
                return GameMcpCommandResult.Rejected(
                    "stale_configuration_generation",
                    GameMcpDecisionReason.For("stale_configuration_generation"),
                    observedConfigurationGeneration: before.Value);
            }
            return write(command, context, scope, before);
        };

    private static GameMcpCommandResult SetConfiguration(
        GameMcpCommand command,
        GameMcpFrameContext context,
        GameMcpFrameCommandScope scope,
        ConfigGeneration before)
    {
        if (GameMcpAutomationFeatures.IsBreakerSetting(command.Mode, command.PayloadKey))
            return GameMcpCommandResult.Rejected(
                "wrong_configuration_surface",
                GameMcpDecisionReason.For("wrong_configuration_surface"),
                observedLifecycleGeneration: scope.LifecycleGeneration(),
                observedConfigurationGeneration: before.Value);
        // The publication the write is about to replace, held as the snapshot it is rather than
        // as text. Only an address the store admitted has a published value to spell, so the
        // pair is rendered on the committed path and a name that resolves to no setting is
        // refused by the store in its own words.
        var priorConfiguration = scope.ConfigurationStore.Current;
        if (!TryAdmitConfigurationWriteAgainstWorld(
                command, context, out var worldFailure))
            return worldFailure;
        var written = scope.ConfigurationStore.SetGameMcp(
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
                    scope.ConfigurationStore.CurrentGeneration.Value);
        if (written == AutomataConfigurationWrite.Refused)
        {
            return GameMcpCommandResult.Rejected(
                "configuration_write_rejected",
                reason,
                observedConfigurationGeneration:
                    scope.ConfigurationStore.CurrentGeneration.Value,
                details: GameMcpConfigurationValuePolicy.RefusalFacts(command, in bound));
        }
        return GameMcpCommandResult.Committed(
            "configuration_committed",
            observedLifecycleGeneration: scope.LifecycleGeneration(),
            observedConfigurationGeneration:
                scope.ConfigurationStore.CurrentGeneration.Value,
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
                            scope.ConfigurationStore.Current,
                            command.Mode,
                            command.PayloadKey),
                    },
                },
            }.Freeze());
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

    private static GameMcpCommandResult SetAutomation(
        GameMcpCommand command,
        GameMcpFrameContext context,
        GameMcpFrameCommandScope scope,
        ConfigGeneration before)
    {
        if (!GameMcpAutomationFeatures.TryGet(command.PayloadKey, out var feature))
            return GameMcpCommandResult.Rejected(
                "automation_feature_unknown",
                "no automation feature is registered as " + command.PayloadKey);
        var requested = command.PayloadValue == "Active";
        var wasOn = feature.IsOn(scope.ConfigurationStore.Current);
        if (wasOn == requested)
            return GameMcpCommandResult.Rejected(
                "already_in_requested_state",
                feature.DisplayName + " is already " + (requested ? "on" : "off"),
                observedLifecycleGeneration: scope.LifecycleGeneration(),
                observedConfigurationGeneration: before.Value);
        var automationWrite = scope.ConfigurationStore.SetGameMcp(
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
                    scope.ConfigurationStore.CurrentGeneration.Value);
        if (automationWrite == AutomataConfigurationWrite.Refused)
        {
            return GameMcpCommandResult.Rejected(
                "configuration_write_rejected",
                automationReason,
                observedConfigurationGeneration:
                    scope.ConfigurationStore.CurrentGeneration.Value);
        }
        return GameMcpCommandResult.Committed(
            "automation_committed",
            observedLifecycleGeneration: scope.LifecycleGeneration(),
            observedConfigurationGeneration:
                scope.ConfigurationStore.CurrentGeneration.Value,
            details: GameMcpAutomationFeatures.ProjectGameMcpAutomationCommit(
                feature,
                wasOn,
                scope.ConfigurationStore.Current));
    }

    private static GameMcpCommandResult SetEmergencyStop(
        GameMcpCommand command,
        GameMcpFrameContext context,
        GameMcpFrameCommandScope scope,
        ConfigGeneration before)
    {
        var engage = command.Mode == "engage";
        if (!engage && !scope.RuntimeActivationAllowed())
        {
            return GameMcpCommandResult.Rejected(
                "runtime_activation_blocked",
                !scope.AutomaticSaveBackup.AllowsAutomation
                    ? "the emergency stop cannot resume while automatic save-backup failure blocks runtime activation"
                    : "the emergency stop cannot resume while exact-build compatibility blocks runtime activation",
                observedLifecycleGeneration: scope.LifecycleGeneration(),
                observedConfigurationGeneration: before.Value);
        }
        if (scope.ConfigurationStore.Current.Safety.EmergencyDisable == engage)
        {
            return GameMcpCommandResult.Rejected(
                "already_in_requested_state",
                engage
                    ? "the suite emergency stop is already engaged"
                    : "the suite emergency stop is already clear",
                observedLifecycleGeneration: scope.LifecycleGeneration(),
                observedConfigurationGeneration: before.Value);
        }

        // Match the in-game control's order. Engaging synchronously cancels the host before another
        // queued command can run in this frame; resume is published and the host clears only through
        // its ordinary configured-stop pump and fresh-world gate.
        scope.EmergencyStopChanged(engage);
        scope.ConfigurationStore.SetEmergencyStop(engage);
        return GameMcpCommandResult.Committed(
            engage ? "emergency_stop_engaged" : "emergency_stop_resume_committed",
            observedLifecycleGeneration: scope.LifecycleGeneration(),
            observedConfigurationGeneration:
                scope.ConfigurationStore.CurrentGeneration.Value,
            details: new GameMcpObjectBuilder
            {
                ["emergencyStopEngaged"] = scope.ConfigurationStore.Current.Safety.EmergencyDisable,
            }.Freeze());
    }
}
#endif
