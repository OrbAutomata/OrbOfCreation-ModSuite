using System;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace OrbModding.Tests.Services.TestSupport;

/// <summary>
/// Runs a call on a thread that is provably not the caller's, and hands back what it returned.
/// </summary>
/// <remarks>
/// <para>
/// A GameAction records the managed id of the thread that constructed it and refuses any submission
/// arriving on a different one. A test that wants to observe that refusal must therefore call from a
/// thread whose id differs from the constructing thread's — which, in every one of these tests, is
/// the test thread itself.
/// </para>
/// <para>
/// <c>await Task.Run(...)</c> does not guarantee that. xUnit runs test methods on thread-pool
/// threads, and awaiting releases the test's thread back to the pool, which is then free to hand
/// that very thread to the queued work item. The submission then runs with the constructing thread's
/// own id, the action sees no violation, and the test fails on a scheduling accident rather than on
/// the behaviour it names.
/// </para>
/// <para>
/// A dedicated thread plus a blocking <see cref="Thread.Join()"/> removes the gamble. Managed thread
/// ids are unique among threads that are alive at the same time; they are only ever reused after a
/// thread has died. The caller blocks inside <c>Join</c> for the whole of <paramref name="work"/>,
/// so the constructing thread is alive for every instant the worker runs, and the worker's id
/// therefore cannot be the id the action captured. The refusal becomes a certainty instead of a
/// probability.
/// </para>
/// <para>
/// The worker's outcome crosses back on that same <c>Join</c>: a returned value is read after it,
/// and a thrown exception is rethrown on the caller with its original stack, so a failure inside
/// <paramref name="work"/> surfaces as that failure rather than as a silent default.
/// </para>
/// </remarks>
internal static class ForeignThread
{
    internal static TResult Run<TResult>(Func<TResult> work)
    {
        if (work is null) throw new ArgumentNullException(nameof(work));

        var result = default(TResult)!;
        ExceptionDispatchInfo? fault = null;
        var thread = new Thread(() =>
        {
            try { result = work(); }
            catch (Exception ex) { fault = ExceptionDispatchInfo.Capture(ex); }
        });

        thread.Start();
        thread.Join();

        fault?.Throw();
        return result;
    }
}
