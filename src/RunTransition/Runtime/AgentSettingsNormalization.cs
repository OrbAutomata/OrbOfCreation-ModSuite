using System;
using System.Reflection;
using OrbModding.Common;

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
    private const BindingFlags Instance =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags Static =
        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

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

    /// <summary>The notation the whole numeric surface — read, refusal, and delta — is written in.</summary>
    internal const string NumberNotation = "Scientific";

    internal static bool TryNormalize(
        out string reason,
        Func<string, Type?>? resolveType = null,
        Func<string, bool>? includeContract = null)
    {
        resolveType ??= ReflectionUtil.FindLoadedType;
        includeContract ??= static _ => true;
        try
        {
            var settingsType = RequireType(0, "SettingsManager", resolveType, includeContract);
            var instance = Field(1, settingsType, "instance", settingsType, true, includeContract)
                .GetValue(null);
            if (instance is null || instance.GetType() != settingsType)
            {
                reason = "SettingsManager.instance was unavailable after the run started";
                return false;
            }

            var boolType = RequireType(2, "BoolVariable", resolveType, includeContract);
            var stringType = RequireType(3, "StringVariable", resolveType, includeContract);
            var researchQueue = Field(
                4, settingsType, "enableQueueResearch", boolType, false, includeContract);
            var cancellableSpells = Field(
                5, settingsType, "cancellableSpells", boolType, false, includeContract);
            var numberDisplay = Field(
                6, settingsType, "numDisplay", stringType, false, includeContract);
            var readBool = Method(7, boolType, "GetValue", typeof(bool), Type.EmptyTypes, includeContract);
            var writeBool = Method(8, boolType, "SetValue", typeof(void), new[] { typeof(bool) }, includeContract);
            var readString = Method(9, stringType, "GetValue", typeof(string), Type.EmptyTypes, includeContract);
            var writeString = Method(10, stringType, "SetValue", typeof(void), new[] { typeof(string) }, includeContract);

            return Enable(instance, researchQueue, readBool, writeBool, "Research Queue Mode", out reason) &&
                Enable(instance, cancellableSpells, readBool, writeBool, "Cancellable Spells", out reason) &&
                Select(instance, numberDisplay, readString, writeString, out reason);
        }
        catch (Exception exception) when (exception is ArgumentException or
            InvalidOperationException or AmbiguousMatchException or
            NotSupportedException or TargetInvocationException)
        {
            reason = "the game's settings contracts are unavailable: " +
                exception.GetBaseException().Message;
            return false;
        }
    }

    private static bool Enable(
        object settings, FieldInfo field, MethodInfo read, MethodInfo write, string name,
        out string reason)
    {
        var variable = field.GetValue(settings);
        if (variable is null)
        {
            reason = "SettingsManager." + field.Name + " held no variable asset";
            return false;
        }
        if (read.Invoke(variable, Array.Empty<object>()) is true)
        {
            reason = string.Empty;
            return true;
        }
        write.Invoke(variable, new object[] { true });
        if (read.Invoke(variable, Array.Empty<object>()) is true)
        {
            reason = string.Empty;
            return true;
        }
        reason = name + " did not stay on after the setting was written";
        return false;
    }

    private static bool Select(
        object settings, FieldInfo field, MethodInfo read, MethodInfo write, out string reason)
    {
        var variable = field.GetValue(settings);
        if (variable is null)
        {
            reason = "SettingsManager." + field.Name + " held no variable asset";
            return false;
        }
        if (string.Equals(
                read.Invoke(variable, Array.Empty<object>()) as string,
                NumberNotation,
                StringComparison.Ordinal))
        {
            reason = string.Empty;
            return true;
        }
        write.Invoke(variable, new object[] { NumberNotation });
        var settled = read.Invoke(variable, Array.Empty<object>()) as string;
        if (string.Equals(settled, NumberNotation, StringComparison.Ordinal))
        {
            reason = string.Empty;
            return true;
        }
        reason = "the number notation settled on " + (settled ?? "nothing") +
            " rather than " + NumberNotation;
        return false;
    }

    private static Type RequireType(
        int index, string name, Func<string, Type?> resolveType, Func<string, bool> include)
    {
        Require(index, include);
        return resolveType(name) ??
            throw new InvalidOperationException(name + " was unavailable");
    }

    private static FieldInfo Field(
        int index, Type owner, string name, Type type, bool isStatic, Func<string, bool> include)
    {
        Require(index, include);
        var field = owner.GetField(name, isStatic ? Static : Instance);
        if (field is null || field.IsStatic != isStatic || field.FieldType != type)
            throw new InvalidOperationException(owner.Name + "." + name + " did not match.");
        return field;
    }

    private static MethodInfo Method(
        int index, Type owner, string name, Type result, Type[] parameters,
        Func<string, bool> include)
    {
        Require(index, include);
        var method = owner.GetMethod(name, Instance, null, parameters, null);
        if (method is null || method.IsStatic || method.ReturnType != result)
            throw new InvalidOperationException(owner.Name + "." + name + " did not match.");
        return method;
    }

    private static void Require(int index, Func<string, bool> include)
    {
        if (!include(ContractIds[index]))
            throw new InvalidOperationException(ContractIds[index] + " was unavailable");
    }
}
