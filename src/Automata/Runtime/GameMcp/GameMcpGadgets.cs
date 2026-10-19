#if SERVICE_CYCLE_PROFILE
using System;
using System.Collections;
using System.Linq;
using HarmonyLib;
using OrbModConfig;
using OrbModding.Common;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OrbAutomata.GameMcp;

internal sealed class GameMcpGadgets
{
    private readonly MonoBehaviour _host;
    private readonly Func<ModConfigUiShell?> _uiShell;
    private readonly Action<GameMcpCommand, GameMcpCommandResult> _complete;
    private readonly Func<GameMcpCommand, GameMcpCommandResult, IEnumerator> _completeContinueRun;
    private readonly Func<long> _lifecycleGeneration;
    private readonly GameMcpGadgetResults _gadgetResults;
    private readonly GameMcpOpenModals _openModals;
    private readonly GameMcpNavigator _navigator;
    private ModalDismissGameAction? _modalDismissGameAction;
    private ModalOpenGameAction? _modalOpenGameAction;

    internal GameMcpGadgets(
        MonoBehaviour host,
        Func<ModConfigUiShell?> uiShell,
        Action<GameMcpCommand, GameMcpCommandResult> complete,
        Func<GameMcpCommand, GameMcpCommandResult, IEnumerator> completeContinueRun,
        Func<long> lifecycleGeneration,
        Func<ulong> configurationGeneration,
        Func<GameMcpTooltipNativeAccess?> tooltipNativeAccess,
        Func<string> tooltipContractFailure,
        Action<string> logWarning)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _uiShell = uiShell ?? throw new ArgumentNullException(nameof(uiShell));
        _complete = complete ?? throw new ArgumentNullException(nameof(complete));
        _completeContinueRun = completeContinueRun ??
            throw new ArgumentNullException(nameof(completeContinueRun));
        _lifecycleGeneration = lifecycleGeneration ??
            throw new ArgumentNullException(nameof(lifecycleGeneration));
        _gadgetResults = new GameMcpGadgetResults(lifecycleGeneration, configurationGeneration);
        _openModals = new GameMcpOpenModals(() => _modalDismissGameAction);
        _navigator = new GameMcpNavigator(
            host,
            uiShell,
            complete,
            _gadgetResults,
            _openModals);
        ScreenReader = new GameMcpScreenReader(
            uiShell,
            tooltipNativeAccess,
            tooltipContractFailure,
            () => _modalOpenGameAction,
            logWarning,
            _gadgetResults,
            _navigator);
    }

    internal GameMcpScreenReader ScreenReader { get; }

    internal bool ModalBindingsAvailable =>
        _modalDismissGameAction?.BindingsAvailable == true &&
            _modalOpenGameAction?.BindingsAvailable == true;

    internal void ComposeModalActions()
    {
        _modalDismissGameAction = new ModalDismissGameAction(_lifecycleGeneration);
        _modalOpenGameAction = new ModalOpenGameAction();
    }

    internal void InvalidateLifecycle()
    {
        _modalDismissGameAction?.InvalidateLifecycle();
        _modalOpenGameAction?.InvalidateLifecycle();
    }

    internal void ReleaseModalActions()
    {
        _modalDismissGameAction?.Dispose();
        _modalDismissGameAction = null;
        _modalOpenGameAction?.Dispose();
        _modalOpenGameAction = null;
    }

    /// <summary>
    /// Why the panel verb is out, naming whichever half of it failed to bind. Opening and
    /// dismissing are one verb to a caller, so one line answers for both.
    /// </summary>
    internal string ModalBindingFailure()
    {
        if (_modalDismissGameAction is null || _modalOpenGameAction is null)
            return "the modal action boundary was not composed";
        if (!_modalDismissGameAction.BindingsAvailable)
            return _modalDismissGameAction.BindingFailure;
        return _modalOpenGameAction.BindingsAvailable
            ? string.Empty
            : _modalOpenGameAction.BindingFailure;
    }

    internal bool TryExecuteGameMcpGadget(
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
            _host.StartCoroutine(CaptureGameMcpAtEndOfFrame(
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
                _host.StartCoroutine(_completeContinueRun(command, result));
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
            _host.StartCoroutine(CompleteModalDismissGameMcp(command, submission.Title));
            result = null!;
            return false;
        }

        result = access switch
        {
            GameMcpGadgetAccess.Probe => ScreenReader.ProbeGameMcp(command),
            GameMcpGadgetAccess.TooltipCatalog =>
                ScreenReader.CaptureTooltipCatalogGameMcp(command),
            GameMcpGadgetAccess.TooltipRead => ScreenReader.ReadTooltipGameMcp(command),
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
                _complete(command, GameMcpCommandResult.Faulted(
                    "modal_state_unavailable",
                    reason.Length == 0 ? "The modal settled state is unavailable." : reason));
                yield break;
            }
            if (!dismissed) continue;
            // Which modal went away. A zero-byte body left the caller with nothing to compare
            // against the screen, and a screenshot was the only way to learn the press had landed;
            // the title was in hand the whole time, read off the control before it closed.
            _complete(command, _gadgetResults.Committed(
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
        _complete(command, _gadgetResults.Committed("modal_dismissed", timedOut));
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
            var uiShell = _uiShell();
            if (uiShell is not null && uiShell.IsAlive)
            {
                var tabs = uiShell.CaptureNativeTabsForGameMcp();
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
                    _complete(command, SavedScreenshotBudgetResult(in admission));
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
            _complete(
                command,
                baseResult.WithInlinePng(details.Freeze(), png));
        }
        catch (Exception exception)
        {
            _complete(
                command,
                _gadgetResults.Faulted(
                    "inline_screenshot_failed",
                    "server defect: the end-of-frame screenshot did not finish." +
                    GameActionFaultLog.Record(exception, "the screen being captured")));
        }
        finally
        {
            if (encodedTexture is not null && !ReferenceEquals(encodedTexture, texture))
                UnityEngine.Object.Destroy(encodedTexture);
            if (texture is not null) UnityEngine.Object.Destroy(texture);
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
}
#endif
