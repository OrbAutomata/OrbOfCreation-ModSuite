using BepInEx.Configuration;
using OrbAutomata;
using OrbMentor;
using OrbModConfig;
using OrbModding.Common;

namespace OrbModding;

/// <summary>
/// Everything the suite binds out of the one BepInEx configuration file, in one transaction.
/// <para>
/// The order matters where two features want the same <c>(section, key)</c>:
/// <see cref="ConfigFile.Bind{T}(ConfigDefinition,T,ConfigDescription)"/> returns the entry that is
/// already bound, so the first binder's description, ordering metadata and default win and later
/// binders simply receive the same entry. <c>General/Enabled</c> and
/// <c>Safety/EmergencyDisable</c> are shared that way on purpose: one master switch, one
/// emergency stop.
/// </para>
/// </summary>
internal sealed class SuiteConfiguration
{
    private SuiteConfiguration(
        BepInExAutomataConfiguration automata,
        MentorConfig mentor,
        ModConfigSettings modConfig,
        ConfigEntry<bool> observeGameTickFaults)
    {
        Automata = automata;
        Mentor = mentor;
        ModConfig = modConfig;
        ObserveGameTickFaults = observeGameTickFaults;
    }

    internal BepInExAutomataConfiguration Automata { get; }

    internal MentorConfig Mentor { get; }

    internal ModConfigSettings ModConfig { get; }

    /// <summary>
    /// Kill switch for the diagnostic hook that names the game element throwing from the game's own
    /// tick. Read once when the runtime composes, so a change takes effect on the next game start.
    /// </summary>
    internal ConfigEntry<bool> ObserveGameTickFaults { get; }

    internal static ConfigurationSchemaBindResult<SuiteConfiguration> TryBind(
        ConfigFile file,
        IConfigurationFileOperations? fileOperations = null,
        ConfigurationSchemaStatusRegistry? statuses = null) =>
        ConfigurationSchemaTransaction.Bind(
            PluginIds.SuiteGuid,
            file,
            SuiteConfigurationSchema.Plan,
            BindCurrent,
            fileOperations,
            statuses);

    private static SuiteConfiguration BindCurrent(ConfigFile file)
    {
        var automata = BepInExAutomataConfiguration.BindCurrent(file);
        var mentor = MentorConfig.BindCurrent(file);
        automata.AttachMentor(mentor);
        var configuration = new SuiteConfiguration(
            automata,
            mentor,
            ModConfigSettings.BindCurrent(file),
            file.Bind(
                "Diagnostics",
                "ObserveGameTickFaults",
                true,
                new ConfigDescription(
                    "Name the game element that throws from the game's own tick. Observes only; takes effect on the next game start.",
                    null,
                    new ModConfigMetadata(50, 20, hidden: true))));
        return configuration;
    }
}
