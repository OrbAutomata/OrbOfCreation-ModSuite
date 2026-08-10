using System;
using OrbAutomata;
using OrbModding.Common.Runtime.GameMath;
using Xunit;

namespace OrbModding.Tests.Runtime.Verification;

/// <summary>
/// The verifier's job is to catch a wrong port, so the way it behaves when it cannot read the game
/// matters as much as the comparison itself: a verifier that silently skips what it cannot resolve
/// would report a clean pass for a port nobody actually checked.
/// </summary>
/// <remarks>
/// The agreeing path is deliberately not tested here. It requires the real assemblies, and a stub
/// built to match the port would only prove the port agrees with itself — the precise circularity
/// this whole verification step exists to break. That check happens in game.
/// </remarks>
public sealed class AutomataCostVerifierTests
{
    [Fact]
    public void AnUnresolvableContractMakesTheVerifierUnavailableRatherThanPassing()
    {
        // A type with none of the required members stands in for a game build whose shape changed.
        var verifier = new AutomataCostVerifier(typeof(object));

        Assert.False(verifier.IsAvailable);
    }

    [Fact]
    public void AnUnavailableVerifierRefusesToVerifyAndSaysWhy()
    {
        var verifier = new AutomataCostVerifier(typeof(object));
        var run = new DifferentialRun();

        var verified = verifier.TryVerify(new object(), run, out var failure);

        Assert.False(verified);
        Assert.NotEmpty(failure);

        // Nothing was compared, so nothing may be reported as agreement.
        Assert.Equal(0, run.Compared);
        Assert.Equal(VerificationVerdict.Inconclusive, run.Finding().Verdict);
    }

    [Fact]
    public void APartiallyShapedTypeStillFailsClosed()
    {
        // Having some of the members is the dangerous case: it is the one where a naive resolver
        // would proceed and read garbage for the rest.
        var verifier = new AutomataCostVerifier(typeof(PartialStructure));

        Assert.False(verifier.IsAvailable);
    }

    [Fact]
    public void NullArgumentsAreRejectedRatherThanTreatedAsNothingToDo()
    {
        var verifier = new AutomataCostVerifier(typeof(object));

        Assert.Throws<ArgumentNullException>(() => verifier.TryVerify(null!, new DifferentialRun(), out _));
        Assert.Throws<ArgumentNullException>(() => verifier.TryVerify(new object(), null!, out _));
    }

    /// <summary>Carries a couple of the required members and none of the rest.</summary>
    private sealed class PartialStructure
    {
        public int quantity;
        public int queuedQuantity;

        public Guid GetGuid() => Guid.Empty;
    }
}
