using System;
using System.Linq;
using System.Reflection;
using OrbModding.Common;
using TMPro;

namespace OrbModConfig;

internal static class SteamKeyboardBridge
{
    private static readonly Type? UtilsType = Type.GetType("Steamworks.SteamUtils, Facepunch.Steamworks.Win64", false);
    private static TMP_InputField? _activeInput;
    private static bool _subscribed;
    private static Action<string>? _failureLog;
    private static bool _failureReported;

    /// <summary>
    /// Names where the first failure of the Steam Deck keyboard is reported. A Steamworks build that
    /// moved these members fails the same way every time, so only the first is written.
    /// </summary>
    internal static void ConfigureFailureLog(Action<string>? log)
    {
        _failureLog = log;
        _failureReported = false;
    }

    public static bool TryShow(TMP_InputField input, string description, ConfigEditorKind kind)
    {
        try
        {
            if (UtilsType?.GetProperty("IsRunningOnSteamDeck", BindingFlags.Static | BindingFlags.Public)?.GetValue(null) is not true) return false;
            Subscribe();
            var method = UtilsType.GetMethods(BindingFlags.Static | BindingFlags.Public)
                .FirstOrDefault(candidate => candidate.Name == "ShowGamepadTextInput" && candidate.GetParameters().Length == 5);
            if (method is null) return false;
            var parameters = method.GetParameters();
            var inputMode = Enum.ToObject(parameters[0].ParameterType, 0);
            var lineMode = Enum.ToObject(parameters[1].ParameterType, 0);
            _activeInput = input;
            var maximum = kind == ConfigEditorKind.String ? 4096 : 128;
            if (method.Invoke(null, new object[] { inputMode, lineMode, description, maximum, input.text }) is true) return true;
            _activeInput = null;
        }
        catch (Exception ex)
        {
            _activeInput = null;
            ReportFailure("could not open, so the setting falls back to the ordinary text field", ex);
        }
        return false;
    }

    private static void Subscribe()
    {
        if (_subscribed || UtilsType is null) return;
        var dismissed = UtilsType.GetEvent("OnGamepadTextInputDismissed", BindingFlags.Static | BindingFlags.Public);
        dismissed?.AddEventHandler(null, new Action<bool>(OnDismissed));
        _subscribed = dismissed is not null;
    }

    private static void OnDismissed(bool submitted)
    {
        var input = _activeInput;
        _activeInput = null;
        if (!submitted || input == null || UtilsType is null) return;
        try
        {
            var value = UtilsType.GetMethod("GetEnteredGamepadText", BindingFlags.Static | BindingFlags.Public)?.Invoke(null, null) as string;
            if (value is not null) input.text = value;
        }
        catch (Exception ex)
        {
            ReportFailure("returned no text, so what was typed on it was not entered", ex);
        }
    }

    private static void ReportFailure(string what, Exception exception)
    {
        if (_failureReported || _failureLog is not { } log) return;
        _failureReported = true;
        log($"The Steam Deck keyboard {what}: {ExceptionDetail.Describe(exception)}");
    }
}
