using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using OrbAutomata;
using OrbModding;
using OrbModding.Common;
using Xunit;
using KeyCode = UnityEngine.KeyCode;

namespace OrbModding.Tests;

public sealed class AutomataDifferentialVerificationShortcutTests
{
    private const string AutoCastSection = "AutoCast";
    private const string AutoCastKey = "ToggleShortcut";
    private const string VerificationSection = "Diagnostics";
    private const string VerificationKey = "VerifyGameMathShortcut";

    [Fact]
    public void CurrentSchemaMigratesOnlyInheritedShortcutDefaults()
    {
        var file = VersionTwoFile(
            "X + LeftAlt",
            "Y + LeftControl + LeftAlt");

        var result = SuiteConfiguration.TryBind(file);

        Assert.True(result.Success, result.Status.Reason);
        Assert.Equal(ConfigurationSchemaState.Migrated, result.Status.State);
        Assert.Equal(2, result.Status.FromVersion);
        Assert.Equal(6, result.Status.ToVersion);
        Assert.Equal(KeyCode.F8, result.Config!.Automata.AutoCastToggleShortcut.Value.MainKey);
        Assert.DoesNotContain(
            file,
            pair => pair.Key.Section == VerificationSection && pair.Key.Key == VerificationKey);
        Assert.Equal(2, result.Diagnostics.Count);
    }

    [Fact]
    public void CurrentSchemaPreservesPlayerCustomizedAutoCastAndRetiresVerifierChord()
    {
        var file = VersionTwoFile(
            "F7",
            "J + LeftShift");

        var result = SuiteConfiguration.TryBind(file);

        Assert.True(result.Success, result.Status.Reason);
        Assert.Equal(KeyCode.F7, result.Config!.Automata.AutoCastToggleShortcut.Value.MainKey);
        Assert.DoesNotContain(
            file,
            pair => pair.Key.Section == VerificationSection && pair.Key.Key == VerificationKey);
        Assert.Single(result.Diagnostics);
    }

    [Fact]
    public void SchemaFiveRetiresOldLookingVerifierValueWithoutReinterpretingAutoCast()
    {
        var file = new ConfigFile();
        file.SeedSerialized(
            ConfigurationSchemaTransaction.MarkerSection,
            ConfigurationSchemaTransaction.MarkerKey,
            "4");
        file.SeedSerialized(AutoCastSection, AutoCastKey, "X + LeftAlt");
        file.SeedSerialized(
            VerificationSection,
            VerificationKey,
            "Y + LeftControl + LeftAlt");

        var result = SuiteConfiguration.TryBind(file);

        Assert.True(result.Success, result.Status.Reason);
        Assert.Equal(ConfigurationSchemaState.Migrated, result.Status.State);
        Assert.Equal(KeyCode.X, result.Config!.Automata.AutoCastToggleShortcut.Value.MainKey);
        Assert.DoesNotContain(
            file,
            pair => pair.Key.Section == VerificationSection && pair.Key.Key == VerificationKey);
    }

    [Fact]
    public void FreshConfigurationUsesF8AndNoVerifierHotkey()
    {
        var file = new ConfigFile();
        var result = SuiteConfiguration.TryBind(file);

        Assert.True(result.Success, result.Status.Reason);
        Assert.Equal(KeyCode.F8, result.Config!.Automata.AutoCastToggleShortcut.Value.MainKey);
        Assert.DoesNotContain(
            file,
            pair => pair.Key.Section == VerificationSection && pair.Key.Key == VerificationKey);
    }

    [Fact]
    public void RuntimeButtonCoalescesRequestsAndRunsOnceOnTick()
    {
        var runs = 0;
        var control = new AutomataDifferentialVerificationControl(_ => { }, _ => runs++);

        Assert.True(control.RequestRun());
        Assert.False(control.RequestRun());
        Assert.True(control.RunRequested);
        Assert.Equal(1, control.Revision);

        control.Tick();

        Assert.Equal(1, runs);
        Assert.False(control.RunRequested);
        Assert.Equal(2, control.Revision);
        control.Tick();
        Assert.Equal(1, runs);
    }

    /// <summary>
    /// A caller that asks for the check gets the verdict lines back, not a pointer at the log. The
    /// lines still reach the log too — the button's own reader is unchanged.
    /// </summary>
    [Fact]
    public void AskingForTheCheckAnswersWithTheVerdictLinesItReported()
    {
        var logged = new List<string>();
        var control = new AutomataDifferentialVerificationControl(
            logged.Add,
            report =>
            {
                report("Cost verification PASSED: 522 compared, 522 exact.");
                report("Rate verification PASSED: 640 compared, 640 exact.");
            });

        Assert.True(control.TryRunNow(out var lines, out var reason));

        Assert.Equal(string.Empty, reason);
        Assert.Equal(logged, lines);
        Assert.Equal(2, lines.Length);
        Assert.StartsWith("Cost verification PASSED", lines[0], StringComparison.Ordinal);
    }

    /// <summary>
    /// One run per frame. A press already queued from the Runtime page runs later in the same frame,
    /// so a second run would measure caches the first one warmed rather than the game.
    /// </summary>
    [Fact]
    public void ACheckAlreadyQueuedFromTheRuntimePageRefusesASecondRun()
    {
        var runs = 0;
        var control = new AutomataDifferentialVerificationControl(_ => { }, _ => runs++);
        control.RequestRun();

        var admitted = control.TryRunNow(out var lines, out var reason);

        Assert.False(admitted);
        Assert.Empty(lines);
        Assert.Equal(
            "A game math check is already queued from the Runtime page and runs this frame.",
            reason);
        Assert.Equal(0, runs);
    }

    [Fact]
    public void InputInventoryProvesF8IsClearAndVerifierHasNoListener()
    {
        var listeners = SuiteShortcutCollisionValidator.Inventory(
            new KeyboardShortcut(KeyCode.F8),
            new KeyboardShortcut(KeyCode.M, KeyCode.LeftAlt));
        var collisions = SuiteShortcutCollisionValidator.Validate(listeners);

        Assert.Equal(3, listeners.Count);
        Assert.Equal(
            SuiteShortcutListenerKind.PerFrameKeyboardPolling,
            listeners.Single(listener => listener.Id == "auto-cast-toggle").Kind);
        Assert.Equal(
            SuiteShortcutListenerKind.PerFrameKeyboardPolling,
            listeners.Single(listener => listener.Id == "mentor-toggle").Kind);
        var verifier = listeners.Single(listener => listener.Id == "differential-verifier");
        Assert.Equal(SuiteShortcutListenerKind.RuntimePageButton, verifier.Kind);
        Assert.Equal(KeyCode.None, verifier.Shortcut.MainKey);
        Assert.DoesNotContain(
            collisions,
            collision => collision.ListenerId == "auto-cast-toggle");
        var mentorCollision = Assert.Single(
            collisions,
            collision => collision.ListenerId == "mentor-toggle");
        Assert.Equal(KeyCode.LeftAlt, mentorCollision.Key);
        Assert.Equal("More Info", mentorCollision.ConflictingBinding);
        Assert.False(mentorCollision.IsMainKey);
        Assert.False(mentorCollision.IsSuiteListener);
    }

    [Fact]
    public void CollisionValidationFindsExactSuiteDoubleBinds()
    {
        var chord = new KeyboardShortcut(KeyCode.M, KeyCode.LeftAlt);
        var collisions = SuiteShortcutCollisionValidator.Validate(
            SuiteShortcutCollisionValidator.Inventory(chord, chord));

        Assert.Contains(
            collisions,
            collision => collision.IsSuiteListener &&
                         collision.ListenerId == "auto-cast-toggle" &&
                         collision.ConflictingBinding == "Mentor toggle");
    }

    [Fact]
    public void CollisionValidationFindsNativeMainKeysAndHeldModifiers()
    {
        var collisions = SuiteShortcutCollisionValidator.Validate(
            SuiteShortcutCollisionValidator.Inventory(
                new KeyboardShortcut(KeyCode.X, KeyCode.LeftAlt),
                new KeyboardShortcut(KeyCode.M, KeyCode.LeftAlt)));

        Assert.Contains(
            collisions,
            collision => collision.ListenerId == "auto-cast-toggle" &&
                         collision.Key == KeyCode.X &&
                         collision.ConflictingBinding == "Open Inventory" &&
                         collision.IsMainKey);
        Assert.Contains(
            collisions,
            collision => collision.ListenerId == "auto-cast-toggle" &&
                         collision.Key == KeyCode.LeftAlt &&
                         collision.ConflictingBinding == "More Info" &&
                         !collision.IsMainKey);
    }

    private static ConfigFile VersionTwoFile(string autoCast, string verifier)
    {
        var file = new ConfigFile();
        file.SeedSerialized(
            ConfigurationSchemaTransaction.MarkerSection,
            ConfigurationSchemaTransaction.MarkerKey,
            "2");
        file.SeedSerialized(AutoCastSection, AutoCastKey, autoCast);
        file.SeedSerialized(VerificationSection, VerificationKey, verifier);
        return file;
    }
}
