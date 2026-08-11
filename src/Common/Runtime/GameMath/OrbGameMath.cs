using System;

namespace OrbModding.Common.Runtime.GameMath;

/// <summary>
/// Scalar arithmetic ported from the game's own implementation so the suite can evaluate it on a
/// worker thread instead of asking the game to recompute it on the Unity thread.
/// </summary>
/// <remarks>
/// <para>
/// <b>Provenance.</b> Ported from <c>Assembly-CSharp.dll</c> /
/// <c>Assembly-CSharp-firstpass.dll</c>, SHA-256 <c>5652EBE3…</c>, which matches the audited
/// <c>steam-macos-2026-07-13</c> baseline in <c>data/native-contracts.json</c>. Every member below
/// names the original it reproduces. This is a transcription, not an interpretation: where the
/// original is surprising, the port keeps the surprise and documents it rather than "fixing" it,
/// because a port that silently disagrees with the game is worse than no port at all.
/// </para>
/// <para>
/// <b>Why this exists.</b> Base state — quantities, levels, flags, modifier sets — is a cheap read.
/// What is expensive is asking the game to recompute <em>derived</em> values: <c>GetNextCost()</c>
/// rebuilds a cost list through four chained <c>new ResourceCostList(costs.Select(…).ToList())</c>
/// transforms, per candidate, per collect. Owning the math lets the suite grab the cached inputs
/// and derive off-thread, allocation-free.
/// </para>
/// <para>
/// <b>Version binding.</b> This code is only valid for the assembly baseline above. The load gate
/// quarantines gameplay runtime on any other build unless the player explicitly accepts that exact
/// assembly pair.
/// </para>
/// </remarks>
internal static class OrbGameMath
{
    /// <summary>
    /// Ported from <c>BigDouble.AsPercent()</c>:
    /// <c>Normalize(mantissa, exponent - 2)</c>.
    /// </summary>
    /// <remarks>
    /// This is a division by 100 performed on the exponent, not a floating-point divide. Writing
    /// it as <c>value / 100</c> would be mathematically equivalent and numerically different, which
    /// is exactly the kind of drift the differential test exists to catch — so it is reproduced in
    /// the original's form.
    /// </remarks>
    internal static BigDouble AsPercent(BigDouble value) =>
        BigDouble.Normalize(value.Mantissa, value.Exponent - 2);

    /// <summary>Ported from <c>BigDouble.Invert()</c>: <c>Pow(this, -1)</c>.</summary>
    internal static BigDouble Invert(BigDouble value) => BigDouble.Pow(value, -1L);

    /// <summary>Ported from <c>Utils.GetNumDigits(BigDouble)</c>: <c>value.Exponent + 1</c>.</summary>
    internal static long GetNumDigits(BigDouble value) => value.Exponent + 1;

    /// <summary>
    /// Ported from <c>Utils.RoundToTwoSigs(BigDouble)</c>: round to two significant digits by
    /// scaling down to that magnitude, rounding, and scaling back.
    /// </summary>
    /// <remarks>
    /// <c>BigDouble.Round</c> delegates to <c>Math.Round(double)</c>, which is round-half-to-even,
    /// so 1250 rounds down to 1200 while 1350 rounds up to 1400. Substituting away-from-zero
    /// rounding here would look like a bug fix and would silently disagree with every cost the game
    /// computes.
    /// </remarks>
    internal static BigDouble RoundToTwoSigs(BigDouble value)
    {
        var numDigits = GetNumDigits(value);
        if (numDigits < 2) return value;

        var scale = BigDouble.Pow(10, numDigits - 2);
        return BigDouble.Round(value / scale) * scale;
    }

    /// <summary>
    /// Ported from <c>Utils.SnapFloorToInt(BigDouble)</c>: narrow through <c>ToFloat()</c>, add the
    /// game's 0.001 tolerance, then floor. Bandwidth affordability uses this on both operands.
    /// </summary>
    internal static int SnapFloorToInt(BigDouble value) =>
        (int)Math.Floor((float)value.ToDouble() + 0.001f);

    /// <summary>
    /// Ported from <c>Utils.RoundToTwoSigsEarly(BigDouble)</c>:
    /// <c>if (!(value >= 100)) return RoundToTwoSigs(value); return value;</c>
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two guards make this far narrower than the name suggests. At 100 and above the value passes
    /// through untouched; below 10, <see cref="GetNumDigits"/> is under two and
    /// <see cref="RoundToTwoSigs"/> returns early. <b>The only values this function actually alters
    /// are those in [10, 100)</b>, where it snaps to a whole number. Worth knowing before assuming
    /// a cost was rounded.
    /// </para>
    /// <para>
    /// The negated comparison is deliberate and reproduced literally, because it decides the NaN
    /// case — <c>NaN >= 100</c> is false, so a NaN operand takes the rounding branch rather than
    /// passing through. Rewriting this as <c>value &lt; 100</c> would read more naturally and
    /// behave differently.
    /// </para>
    /// </remarks>
    internal static BigDouble RoundToTwoSigsEarly(BigDouble value)
    {
        if (!(value >= 100)) return RoundToTwoSigs(value);
        return value;
    }

    /// <summary>
    /// Ported from <c>Utils.IsWithinError(BigDouble, BigDouble, BigDouble)</c>: a relative comparison
    /// that divides by whichever operand is non-zero.
    /// </summary>
    /// <remarks>
    /// The asymmetry is the original's. The denominator is the <em>second</em> operand unless it is
    /// zero, so the relation is not commutative, and only the both-zero case is special-cased. Both
    /// operands zero would otherwise divide zero by zero.
    /// </remarks>
    internal static bool IsWithinError(BigDouble left, BigDouble right, BigDouble error)
    {
        if (left == 0 && right == 0) return true;
        return BigDouble.Abs((left - right) / (right == 0 ? left : right)) < error;
    }

    /// <summary>
    /// Ported from <c>Utils.ApproxError(BigDouble, BigDouble)</c>:
    /// <see cref="IsWithinError"/> at one part in a thousand.
    /// </summary>
    internal static bool ApproxError(BigDouble left, BigDouble right) =>
        IsWithinError(left, right, 0.001);

    /// <summary>
    /// Ported from <c>Utils.Approx(BigDouble, BigDouble)</c>: an <em>absolute</em> difference below
    /// one part in a hundred thousand.
    /// </summary>
    /// <remarks>
    /// Not <see cref="ApproxError"/>, which is relative, and the two are not interchangeable here.
    /// This is the test <c>Spell.GetResonantPercent</c> applies to a resonance whose untouched value
    /// is exactly one, and it selects between two products that differ.
    /// </remarks>
    internal static bool Approx(BigDouble left, BigDouble right) =>
        BigDouble.Abs(left - right) < (BigDouble)1E-05;

    /// <summary>
    /// Ported from <c>Utils.SumGeometricSequence(BigDouble a, BigDouble r, BigDouble n)</c>: the sum
    /// of <c>n + 1</c> terms of a geometric series with first term <paramref name="a"/> and ratio
    /// <paramref name="ratio"/>.
    /// </summary>
    internal static BigDouble SumGeometricSequence(BigDouble a, BigDouble ratio, BigDouble n)
    {
        if (ratio == 1) return a * (n + 1);
        return a * (1 - BigDouble.Pow(ratio, n + 1)) / (1 - ratio);
    }

    /// <summary>
    /// Ported from <c>Utils.SumArithmeticSequence(BigDouble start, BigDouble end, BigDouble n)</c>:
    /// <c>n / 2 * (start + end)</c>.
    /// </summary>
    internal static BigDouble SumArithmeticSequence(BigDouble start, BigDouble end, BigDouble n) =>
        n / 2 * (start + end);

    /// <summary>
    /// Ported from <c>Utils.SumArithmeticSequenceDiff(BigDouble start, BigDouble diff, BigDouble n)</c>:
    /// the sum of <c>n + 1</c> terms starting at <paramref name="start"/> and rising by
    /// <paramref name="diff"/>.
    /// </summary>
    internal static BigDouble SumArithmeticSequenceDiff(BigDouble start, BigDouble diff, BigDouble n) =>
        SumArithmeticSequence(start, start + (diff * n), n + 1);

    /// <summary>
    /// Ported from <c>Utils.FindGeometricSequenceN(BigDouble sum, BigDouble a, BigDouble r)</c>: the
    /// inverse of <see cref="SumGeometricSequence"/>, solving for the term count.
    /// </summary>
    /// <remarks>
    /// Throws on a ratio of exactly one, as the original does — the closed form divides by
    /// <c>1 - r</c>. Callers reach this only through the overflow path, which has already excluded
    /// that case.
    /// </remarks>
    internal static BigDouble FindGeometricSequenceN(BigDouble sum, BigDouble a, BigDouble ratio)
    {
        if (ratio == 1) throw new ArgumentOutOfRangeException(nameof(ratio), "cannot be 1.");
        return BigDouble.Log(1 - sum / a * (1 - ratio), ratio) - 1.0;
    }
}
