#if SERVICE_CYCLE_PROFILE
using System;
using System.Collections;
using System.Linq;
using HarmonyLib;
using OrbModding.Common;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OrbAutomata.GameMcp;

internal static class GameMcpGadgetCommands
{
    internal static GameMcpCommandDescriptor Screenshot { get; } = new(
        GameMcpCommandKind.Screenshot,
        CaptureScreenshot,
        prepare: static (_, _, draft) => draft.Mode = "capture");

    internal static GameMcpCommandDescriptor Navigation { get; } = new(
        GameMcpCommandKind.Navigation,
        static (command, _, scope) =>
        {
            scope.Gadgets.Navigator.Navigate(command);
            return null;
        },
        prepare: static (_, _, draft) => draft.Mode = "navigate");

    internal static GameMcpCommandDescriptor Probe { get; } = new(
        GameMcpCommandKind.Probe,
        static (command, _, scope) => scope.Gadgets.ScreenReader.ProbeGameMcp(command),
        prepare: static (request, _, draft) => draft.Mode = request.Probe);

    internal static GameMcpCommandDescriptor TooltipCatalog { get; } = new(
        GameMcpCommandKind.TooltipCatalog,
        static (command, _, scope) =>
            scope.Gadgets.ScreenReader.CaptureTooltipCatalogGameMcp(command),
        prepare: static (request, _, draft) =>
        {
            draft.Mode = "catalog";
            draft.Amount = request.Limit;
            draft.PayloadValue = request.Offset.ToString(
                System.Globalization.CultureInfo.InvariantCulture);
        });

    internal static GameMcpCommandDescriptor TooltipRead { get; } = new(
        GameMcpCommandKind.TooltipRead,
        static (command, _, scope) => scope.Gadgets.ScreenReader.ReadTooltipGameMcp(command),
        prepare: static (request, _, draft) =>
        {
            draft.Mode = "read";
            draft.PayloadValue = request.Path;
        });

    internal static GameMcpCommandDescriptor ContinueRun { get; } = new(
        GameMcpCommandKind.ContinueRun,
        Continue,
        prepare: static (_, _, draft) => draft.Mode = "continue");

    internal static GameMcpCommandDescriptor Modal { get; } = new(
        GameMcpCommandKind.Modal,
        PressModal,
        prepare: static (request, _, draft) => draft.PayloadValue = request.Title);

    private static GameMcpCommandResult? CaptureScreenshot(
        GameMcpCommand command,
        GameMcpFrameContext context,
        GameMcpFrameCommandScope scope)
    {
        if (command.SaveCapture)
        {
            var admission = GameMcpScreenshotBudget.BeforeCapture(
                AutomataTraceRunRoot.Child("mcp-screenshots"));
            if (!admission.IsAvailable)
                return SavedScreenshotBudgetResult(scope.Gadgets, in admission);
        }
        scope.Host.StartCoroutine(CaptureGameMcpAtEndOfFrame(
            command,
            scope.Gadgets.Results.Committed(
                "screenshot_captured",
                new GameMcpObjectBuilder()),
            scope));
        return null;
    }

    private static GameMcpCommandResult? Continue(
        GameMcpCommand command,
        GameMcpFrameContext context,
        GameMcpFrameCommandScope scope)
    {
        var result = ContinueRunGameMcp(scope.Gadgets);
        if (string.Equals(result.Status, "committed", StringComparison.Ordinal))
        {
            scope.Host.StartCoroutine(scope.CompleteContinueRun(command, result));
            return null;
        }
        return result;
    }

    private static GameMcpCommandResult? PressModal(
        GameMcpCommand command,
        GameMcpFrameContext context,
        GameMcpFrameCommandScope scope)
    {
        var gadgets = scope.Gadgets;
        if (string.Equals(command.Mode, "open", StringComparison.Ordinal))
            return OpenModalGameMcp(command, gadgets);
        var dismiss = gadgets.ModalDismissGameAction;
        if (dismiss is null)
            return gadgets.Results.Rejected("contract_unavailable",
                "The native modal close control is unavailable.");
        var submission = dismiss.Submit();
        if (submission.Faulted)
            return gadgets.Results.Faulted(submission.Code, submission.Reason);
        if (!submission.Committed)
            return gadgets.Results.Rejected(submission.Code, submission.Reason);
        scope.Host.StartCoroutine(CompleteModalDismissGameMcp(command, submission.Title, scope));
        return null;
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
    private static GameMcpCommandResult OpenModalGameMcp(
        GameMcpCommand command,
        GameMcpGadgets gadgets)
    {
        if (gadgets.ModalOpenGameAction is null)
            return gadgets.Results.Rejected(
                "contract_unavailable", ModalOpenGameAction.NoActivator);
        var requested = command.PayloadValue;
        if (!gadgets.OpenModals.TryReadTitles(out var openBefore, out var openFailure))
            return gadgets.Results.Rejected("contract_unavailable", openFailure);
        if (openBefore.Length > 0)
        {
            if (Array.IndexOf(openBefore, requested) >= 0)
                return gadgets.Results.Committed("modal_opened", OpenedModal(requested));
            return gadgets.Results.Rejected(
                "modal_already_open",
                "The " + (openBefore[0].Length == 0 ? "open" : openBefore[0]) +
                " panel is covering the board, and the chrome beneath it takes no presses; " +
                "dismiss it first.");
        }
        if (!gadgets.ModalOpenGameAction.TryReadActivators(out var activators, out var readFailure))
            return gadgets.Results.Rejected("contract_unavailable", readFailure);
        var submission = gadgets.ModalOpenGameAction.Submit(requested, activators);
        if (submission.Faulted)
            return gadgets.Results.Faulted(submission.Code, submission.Reason);
        if (!submission.Committed)
            return gadgets.Results.Rejected(submission.Code, submission.Reason);
        return gadgets.Results.Committed("modal_opened", OpenedModal(submission.Title));
    }

    /// <summary>The open's own post-state: the name of the panel now covering the board.</summary>
    private static GameMcpObjectBuilder OpenedModal(string title) =>
        new() { ["opened"] = title };

    private static IEnumerator CompleteModalDismissGameMcp(
        GameMcpCommand command,
        string title,
        GameMcpFrameCommandScope scope)
    {
        var deadline = Time.realtimeSinceStartup + GameMcpPostStateSettlement.MaximumWaitSeconds;
        while (Time.realtimeSinceStartup < deadline)
        {
            yield return null;
            var dismissed = false;
            var reason = string.Empty;
            var dismiss = scope.Gadgets.ModalDismissGameAction;
            if (dismiss is null ||
                !dismiss.TryObserveDismissed(out dismissed, out reason))
            {
                scope.Complete(command, GameMcpCommandResult.Faulted(
                    "modal_state_unavailable",
                    reason.Length == 0 ? "The modal settled state is unavailable." : reason));
                yield break;
            }
            if (!dismissed) continue;
            // Which modal went away. A zero-byte body left the caller with nothing to compare
            // against the screen, and a screenshot was the only way to learn the press had landed;
            // the title was in hand the whole time, read off the control before it closed.
            scope.Complete(command, scope.Gadgets.Results.Committed(
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
        scope.Complete(command, scope.Gadgets.Results.Committed("modal_dismissed", timedOut));
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

    private static GameMcpCommandResult ContinueRunGameMcp(GameMcpGadgets gadgets)
    {
        if (!string.Equals(
                SceneManager.GetActiveScene().name,
                "Start",
                StringComparison.Ordinal))
        {
            return gadgets.Results.Rejected(
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
            return gadgets.Results.Failed(
                "continue_contract_unavailable",
                "This build does not expose the Continue button, so a save cannot be started " +
                "from here.");
        }

        startGame.Invoke(manager, Array.Empty<object>());
        return gadgets.Results.Committed(
            "continue_invoked",
            new GameMcpObjectBuilder());
    }

    private static IEnumerator CaptureGameMcpAtEndOfFrame(
        GameMcpCommand command,
        GameMcpCommandResult baseResult,
        GameMcpFrameCommandScope scope)
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
            var uiShell = scope.UiShell();
            if (uiShell is not null && uiShell.IsAlive)
            {
                var tabs = uiShell.CaptureNativeTabsForGameMcp();
                var activeTab = tabs.FirstOrDefault(tab => tab.Active);
                if (!string.IsNullOrWhiteSpace(activeTab.Label))
                    details["activeScreen"] = activeTab.Label;
            }
            scope.Gadgets.OpenModals.AppendTo(details);
            if (command.SaveCapture)
            {
                var directory = AutomataTraceRunRoot.Child("mcp-screenshots");
                var admission = GameMcpScreenshotBudget.BeforeCommit(directory, png.LongLength);
                if (!admission.IsAvailable)
                {
                    scope.Complete(
                        command, SavedScreenshotBudgetResult(scope.Gadgets, in admission));
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
            scope.Complete(
                command,
                baseResult.WithInlinePng(details.Freeze(), png));
        }
        catch (Exception exception)
        {
            scope.Complete(
                command,
                scope.Gadgets.Results.Faulted(
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

    private static GameMcpCommandResult SavedScreenshotBudgetResult(
        GameMcpGadgets gadgets,
        in GameMcpScreenshotBudgetAdmission admission) =>
        admission.Status == GameMcpScreenshotBudgetStatus.StorageUnavailable
            ? gadgets.Results.Faulted(
                "screenshot_budget_unavailable",
                "server defect: " + admission.Reason)
            : gadgets.Results.Rejected(
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
