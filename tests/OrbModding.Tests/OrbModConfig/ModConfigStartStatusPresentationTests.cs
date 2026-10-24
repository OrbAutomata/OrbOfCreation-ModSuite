using System;
using OrbModConfig;
using OrbModding.Common;
using Xunit;

namespace OrbModding.Tests.OrbModConfig;

public sealed class ModConfigStartStatusPresentationTests
{
    [Fact]
    public void StartStatusUsesTheCurrentBuildsTransactionWording()
    {
        foreach (var backupCreated in new[] { true, false })
        {
            var backup = AutomaticSaveBackupStatus.Ready(
                backupCreated,
                trigger: backupCreated
                    ? AutomaticSaveBackupTrigger.FreshInstall
                    : AutomaticSaveBackupTrigger.None,
                backupPath: "/save/backups/auto-modsuite-backup-20260731T101112Z",
                fileCount: 2,
                prunedBackupCount: 0,
                retentionFailures: Array.Empty<string>());
            var presentation = ModConfigStartStatusPresenter.Build(
                "0.5.0",
                controlPlaneReady: true,
                auditedBuild: true,
                runtimeActivationAllowed: true,
                saveBackup: backup
#if SERVICE_CYCLE_PROFILE
                , gameMcpServerReady: true, processId: 42
#endif
                );

#if SERVICE_CYCLE_PROFILE
            Assert.Equal("Orb ModSuite  ·  v0.5.0", presentation.Rows[0]);
            Assert.Contains("Performance-debug build", presentation.Rows);
            Assert.Contains("MCP ready  ·  Audited game verified", presentation.Rows);
            Assert.Contains("Agent: 127.0.0.1:19106/mcp  ·  PID 42  ·  Localhost only", presentation.Rows);
            Assert.Contains(
                backupCreated
                    ? "Save backup created · 2 files"
                    : "Save backup ready · 2 files",
                presentation.Rows);
            Assert.Equal(5, presentation.Rows.Count);
#else
            Assert.Equal(
                new[]
                {
                    "Orb ModSuite  ·  v0.5.0",
                    "Release build",
                    "Audited game verified",
                    backupCreated ? "Save backup created." : "Save backup ready.",
                },
                presentation.Rows);
#endif
            Assert.Equal(ModConfigStartStatusTone.Ready, presentation.Tone);

#if !SERVICE_CYCLE_PROFILE
            var visibleText = string.Join("\n", presentation.Rows);
            Assert.DoesNotContain(backup.BackupPath, visibleText, StringComparison.Ordinal);
            Assert.DoesNotContain("2 files", visibleText, StringComparison.Ordinal);
            Assert.DoesNotContain(
                "Saves are backed up automatically on startup.",
                visibleText,
                StringComparison.Ordinal);
            foreach (var forbidden in new[]
                     {
                         "MCP",
                         "agent",
                         "perf-debug",
                         "performance-debug",
                         "PID",
                         "localhost",
                         "trace",
                         "probe",
                     })
            {
                Assert.DoesNotContain(forbidden, visibleText, StringComparison.OrdinalIgnoreCase);
            }
            Assert.Equal(4, presentation.Rows.Count);
#endif
            Assert.All(presentation.Rows, row => Assert.False(string.IsNullOrWhiteSpace(row)));
        }

        var retention = AutomaticSaveBackupStatus.Ready(
            backupCreated: true,
            trigger: AutomaticSaveBackupTrigger.FreshInstall,
            backupPath: "/save/backups/auto-modsuite-backup-20260731T101112Z",
            fileCount: 2,
            prunedBackupCount: 0,
            retentionFailures: new[] { "Could not prune one owned automatic backup." });
        var retentionPresentation = ModConfigStartStatusPresenter.Build(
            "0.5.0",
            controlPlaneReady: true,
            auditedBuild: true,
            runtimeActivationAllowed: true,
            saveBackup: retention
#if SERVICE_CYCLE_PROFILE
                , gameMcpServerReady: true, processId: 42
#endif
                );
        Assert.Contains(
            "Save backup created · 2 files · /save/backups/auto-modsuite-backup-20260731T101112Z · retention warning",
            retentionPresentation.Rows);
        Assert.Equal(ModConfigStartStatusTone.Attention, retentionPresentation.Tone);
    }

    [Fact]
    public void BackupFailureIsNamedAsTheBlockingStartHealth()
    {
        var backup = AutomaticSaveBackupStatus.Failed(
            AutomaticSaveBackupTrigger.VersionChanged,
            "Could not read active save file 'ooc_save_1.sav' cleanly.");

        var presentation = ModConfigStartStatusPresenter.Build(
            "0.5.0",
            controlPlaneReady: true,
            auditedBuild: true,
            runtimeActivationAllowed: false,
            saveBackup: backup
#if SERVICE_CYCLE_PROFILE
                , gameMcpServerReady: true, processId: 42
#endif
                );

        Assert.Equal(ModConfigStartStatusTone.Failure, presentation.Tone);
#if SERVICE_CYCLE_PROFILE
        Assert.Contains("MCP ready  ·  Audited game verified", presentation.Rows);
#else
        Assert.Contains("Audited game verified", presentation.Rows);
#endif
        Assert.Contains(
            "Save backup failed · automation blocked · Could not read active save file 'ooc_save_1.sav' cleanly.",
            presentation.Rows);
    }
}
