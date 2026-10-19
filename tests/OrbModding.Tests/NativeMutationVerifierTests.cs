using System;
using System.Collections.Generic;
using System.Reflection;
using OrbModding.Common;
using Xunit;

namespace OrbModding.Tests;

[Collection(GameActionFaultLogCollection.Name)]
public sealed class NativeMutationVerifierTests : IDisposable
{
    public void Dispose() => GameActionFaultLog.ConfigureLog(null);

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public void RejectsNoOpPartialAndUnexpectedLargerChanges(int appliedDelta)
    {
        var state = 10;

        var evidence = NativeMutationVerifier.Execute(
            "test queue",
            "candidate-1",
            "exact delta +2",
            () => state,
            () => state += appliedDelta,
            (before, after) => after == before + 2);

        Assert.Equal(NativeMutationOutcome.PostconditionFailed, evidence.Outcome);
        Assert.True(evidence.MutationWasAttempted);
        Assert.Equal(10, evidence.Before);
        Assert.Equal(10 + appliedDelta, evidence.After);
        Assert.Contains("candidate-1", evidence.Format());
    }

    [Fact]
    public void CommitsAnOutcomeTheGameAppliedBeforeItThrewAndLogsTheThrow()
    {
        var logged = new List<string>();
        GameActionFaultLog.ConfigureLog(logged.Add);
        var state = 10;

        var evidence = NativeMutationVerifier.Execute(
            "test queue",
            "candidate-2",
            "exact delta +2",
            () => state,
            () =>
            {
                state += 2;
                throw new InvalidOperationException("native failure");
            },
            (before, after) => after == before + 2);

        Assert.Equal(NativeMutationOutcome.Verified, evidence.Outcome);
        Assert.Equal(12, evidence.After);
        Assert.Contains("native failure", evidence.Detail);
        var line = Assert.Single(logged);
        Assert.Contains("candidate-2", line);
        Assert.Contains("native failure", line);
    }

    [Fact]
    public void FailsClosedWhenTheGameThrewWithoutApplyingTheOutcome()
    {
        var state = 10;

        var evidence = NativeMutationVerifier.Execute(
            "test queue",
            "candidate-7",
            "exact delta +2",
            () => state,
            () =>
            {
                state += 1;
                throw new InvalidOperationException("native failure");
            },
            (before, after) => after == before + 2);

        Assert.Equal(NativeMutationOutcome.ExecutionThrew, evidence.Outcome);
        Assert.True(evidence.HasAfter);
        Assert.Equal(11, evidence.After);
        Assert.Contains("native failure", evidence.Detail);
    }

    /// <summary>
    /// A throw outside the expected native failures escapes only after the after-state was read, so a
    /// change that landed before it can still commit.
    /// </summary>
    [Fact]
    public void AnUnexpectedThrowEscapesAfterTheAfterStateIsRead()
    {
        var thrower = typeof(NativeMutationVerifierTests).GetMethod(
            nameof(ThrowFromNativeCode),
            BindingFlags.Static | BindingFlags.NonPublic)!;
        var reads = 0;

        Assert.Throws<TargetInvocationException>(() => NativeMutationVerifier.Execute(
            "test queue",
            "candidate-reflected",
            "exact delta +1",
            () => reads++,
            () => thrower.Invoke(null, null),
            (before, after) => after == before + 2));

        Assert.Equal(2, reads);
    }

    /// <summary>
    /// Evidence names the game's own exception rather than the reflection wrapper around it.
    /// </summary>
    [Fact]
    public void ExceptionDetailNamesTheGamesOwnExceptionInsideAReflectionWrapper()
    {
        var thrower = typeof(NativeMutationVerifierTests).GetMethod(
            nameof(ThrowFromNativeCode),
            BindingFlags.Static | BindingFlags.NonPublic)!;
        var wrapped = Assert.Throws<TargetInvocationException>(() => thrower.Invoke(null, null));

        var detail = ExceptionDetail.Describe(wrapped);

        Assert.Contains(typeof(NullReferenceException).FullName!, detail);
        Assert.Contains(nameof(ThrowFromNativeCode), detail);
        Assert.DoesNotContain(nameof(TargetInvocationException), detail);
        Assert.DoesNotContain("\n", detail);
        Assert.True(detail.Length <= ExceptionDetail.MaxLength);
    }

    [Fact]
    public void ExceptionDetailStaysBoundedForAHugeMultiLineMessage()
    {
        var message = string.Join("\n", new string('x', 5000), new string('y', 5000));

        var detail = ExceptionDetail.Describe(new InvalidOperationException(message));

        Assert.True(detail.Length <= ExceptionDetail.MaxLength);
        Assert.DoesNotContain("\n", detail);
        Assert.StartsWith(typeof(InvalidOperationException).FullName!, detail);
    }

    private static void ThrowFromNativeCode() =>
        throw new NullReferenceException("Object reference not set to an instance of an object");

    [Fact]
    public void AcceptsOnlyTheExpectedChange()
    {
        var state = 10;

        var evidence = NativeMutationVerifier.Execute(
            "test queue",
            "candidate-3",
            "exact delta +2",
            () => state,
            () => state += 2,
            (before, after) => after == before + 2);

        Assert.Equal(NativeMutationOutcome.Verified, evidence.Outcome);
        Assert.True(evidence.IsVerified);
        Assert.Equal(10, evidence.Before);
        Assert.Equal(12, evidence.After);
    }

    [Fact]
    public void DoesNotExecuteWhenBeforeCaptureFails()
    {
        var executed = false;

        var evidence = NativeMutationVerifier.Execute<int>(
            "test queue",
            "candidate-4",
            "exact delta +1",
            () => throw new InvalidOperationException("capture failure"),
            () => executed = true,
            (before, after) => after == before + 1);

        Assert.Equal(NativeMutationOutcome.BeforeCaptureFailed, evidence.Outcome);
        Assert.False(evidence.MutationWasAttempted);
        Assert.False(executed);
    }

    [Fact]
    public void ReportsAfterCaptureFailureAsAmbiguousMutation()
    {
        var state = 10;
        var captures = 0;

        var evidence = NativeMutationVerifier.Execute<int>(
            "test queue",
            "candidate-5",
            "exact delta +1",
            () => ++captures == 1 ? state : throw new InvalidOperationException("after unavailable"),
            () => state++,
            (before, after) => after == before + 1);

        Assert.Equal(NativeMutationOutcome.AfterCaptureFailed, evidence.Outcome);
        Assert.True(evidence.MutationWasAttempted);
        Assert.False(evidence.HasAfter);
    }

    [Fact]
    public void ExecutesFromAnAlreadyCapturedAdmissionState()
    {
        var state = 10;
        var afterCaptures = 0;

        var evidence = NativeMutationVerifier.ExecuteAfterCapture(
            "test queue",
            "candidate-6",
            "exact delta +1",
            state,
            () =>
            {
                afterCaptures++;
                return state;
            },
            () => state++,
            (before, after) => after == before + 1);

        Assert.True(evidence.IsVerified);
        Assert.Equal(1, afterCaptures);
        Assert.Equal(10, evidence.Before);
        Assert.Equal(11, evidence.After);
    }
}
