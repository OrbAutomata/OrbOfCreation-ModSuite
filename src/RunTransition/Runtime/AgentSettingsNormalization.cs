using System;
using OrbModding.Common;
using OrbModding.Common.Runtime.GameMath;

namespace OrbAutomata;

/// <summary>
/// The three native settings the tool surface is documented against, applied on the load that
/// asked for the game.
/// </summary>
/// <remarks>
/// Research Queue Mode off turns a queued develop into a refusal; cancellable spells off turns a
/// toggle-off into a refusal; and any notation but Scientific makes the numbers the game draws
/// disagree with the numbers the wire carries. None of the three is a decision a caller makes —
/// they are the shape of the game every documented verb assumes — so the load sets them, verifies
/// them, and never mentions them again. Each is the exact write the settings dropdown performs,
/// and each is skipped when the game already holds it.
/// </remarks>
internal static class AgentSettingsNormalization
{
    internal static readonly string[] ContractIds =
    {
        "run-transition.settings.type-action",
        "run-transition.settings-instance-action",
        "run-transition.bool-variable.type-action",
        "run-transition.string-variable.type-action",
        "run-transition.settings-research-queue-action",
        "run-transition.settings-cancellable-spells-action",
        "run-transition.settings-number-display-action",
        "run-transition.bool-get-action",
        "run-transition.bool-set-action",
        "run-transition.string-get-action",
        "run-transition.string-set-action",
    };

    /// <summary>
    /// The notation the whole numeric surface — read, refusal, and delta — is written in, taken
    /// from the renderer that mirrors it so the arm the suite writes and the arm the suite spells
    /// are one constant.
    /// </summary>
    internal const string NumberNotation = GameScientificNumber.Notation;

    internal static bool TryNormalize(
        out string reason,
        Func<string, Type?>? resolveType = null,
        Func<string, bool>? includeContract = null)
    {
        if (!NativeBindingSet.TryBind<Bindings>(
                "game settings", ContractIds, resolveType, includeContract, Bind, out var native, out reason))
            return false;

        var instance = native!.Instance();
        if (instance is null || instance.GetType() != native.SettingsType)
        {
            reason = "SettingsManager.instance was unavailable after the run started";
            return false;
        }
        try
        {
            return Enable(native, native.ResearchQueue(instance), "enableQueueResearch",
                    "Research Queue Mode", out reason) &&
                Enable(native, native.CancellableSpells(instance), "cancellableSpells",
                    "Cancellable Spells", out reason) &&
                Select(native, native.NumberDisplay(instance), out reason);
        }
        catch (Exception exception) when (NativeFailureClassifier.IsExpected(exception))
        {
            reason = GameActionFaultLog.Reason("The game settings could not be applied.", exception, "run settings").Text;
            return false;
        }
    }

    private static Bindings Bind(NativeBindingSet set)
    {
        var settings = set.Type("run-transition.settings.type-action", "SettingsManager");
        var boolean = set.Type("run-transition.bool-variable.type-action", "BoolVariable");
        var text = set.Type("run-transition.string-variable.type-action", "StringVariable");
        return new Bindings(
            settings,
            set.StaticField<Func<object?>>(
                "run-transition.settings-instance-action", settings, "instance", settings),
            set.Field<Func<object, object?>>(
                "run-transition.settings-research-queue-action", settings, "enableQueueResearch", boolean),
            set.Field<Func<object, object?>>(
                "run-transition.settings-cancellable-spells-action", settings, "cancellableSpells", boolean),
            set.Field<Func<object, object?>>(
                "run-transition.settings-number-display-action", settings, "numDisplay", text),
            set.Call<Func<object, bool>>("run-transition.bool-get-action", boolean, "GetValue", typeof(bool)),
            set.Call<Action<object, bool>>(
                "run-transition.bool-set-action", boolean, "SetValue", typeof(void), typeof(bool)),
            set.Call<Func<object, string?>>(
                "run-transition.string-get-action", text, "GetValue", typeof(string)),
            set.Call<Action<object, string>>(
                "run-transition.string-set-action", text, "SetValue", typeof(void), typeof(string)));
    }

    private static bool Enable(
        Bindings native, object? variable, string field, string name, out string reason)
    {
        if (variable is null)
        {
            reason = "SettingsManager." + field + " held no variable asset";
            return false;
        }
        if (native.ReadBool(variable))
        {
            reason = string.Empty;
            return true;
        }
        native.WriteBool(variable, true);
        if (native.ReadBool(variable))
        {
            reason = string.Empty;
            return true;
        }
        reason = name + " did not stay on after the setting was written";
        return false;
    }

    private static bool Select(Bindings native, object? variable, out string reason)
    {
        if (variable is null)
        {
            reason = "SettingsManager.numDisplay held no variable asset";
            return false;
        }
        if (string.Equals(native.ReadString(variable), NumberNotation, StringComparison.Ordinal))
        {
            reason = string.Empty;
            return true;
        }
        native.WriteString(variable, NumberNotation);
        var settled = native.ReadString(variable);
        if (string.Equals(settled, NumberNotation, StringComparison.Ordinal))
        {
            reason = string.Empty;
            return true;
        }
        reason = "the number notation settled on " + (settled ?? "nothing") +
            " rather than " + NumberNotation;
        return false;
    }

    private sealed class Bindings
    {
        internal Bindings(
            Type settingsType,
            Func<object?> instance,
            Func<object, object?> researchQueue,
            Func<object, object?> cancellableSpells,
            Func<object, object?> numberDisplay,
            Func<object, bool> readBool,
            Action<object, bool> writeBool,
            Func<object, string?> readString,
            Action<object, string> writeString)
        {
            SettingsType = settingsType;
            Instance = instance;
            ResearchQueue = researchQueue;
            CancellableSpells = cancellableSpells;
            NumberDisplay = numberDisplay;
            ReadBool = readBool;
            WriteBool = writeBool;
            ReadString = readString;
            WriteString = writeString;
        }

        internal Type SettingsType { get; }
        internal Func<object?> Instance { get; }
        internal Func<object, object?> ResearchQueue { get; }
        internal Func<object, object?> CancellableSpells { get; }
        internal Func<object, object?> NumberDisplay { get; }
        internal Func<object, bool> ReadBool { get; }
        internal Action<object, bool> WriteBool { get; }
        internal Func<object, string?> ReadString { get; }
        internal Action<object, string> WriteString { get; }
    }
}
