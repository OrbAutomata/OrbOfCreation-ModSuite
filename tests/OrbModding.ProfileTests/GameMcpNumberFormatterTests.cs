#if SERVICE_CYCLE_PROFILE
using Newtonsoft.Json.Linq;
using OrbAutomata.GameMcp;
using Xunit;

namespace OrbModding.ProfileTests;

/// <summary>
/// The wire's entry into the game's own number rule, <c>Utils.BeautifyNumber</c>: one notation for
/// every magnitude, and it is the one the screen draws rather than an exponent form applied to
/// everything.
/// </summary>
public sealed class GameMcpNumberFormatterTests
{
    [Fact]
    public void Canonical_scalar_shape_is_the_number_the_screen_draws()
    {
        Assert.Equal("0", GameMcpNumberFormatter.Format(BigDouble.Zero));

        // Outside a tenth-to-a-thousand window the screen writes a mantissa of exactly two
        // decimals against a bare exponent, so the wire does too.
        var exponential = new[]
        {
            GameMcpNumberFormatter.Format(new BigDouble(1.4d, 4)),
            GameMcpNumberFormatter.Format(new BigDouble(7.816502d, 4)),
            GameMcpNumberFormatter.Format(new BigDouble(4.4d, 3)),
            GameMcpNumberFormatter.Format(new BigDouble(1.1d, 24)),
            GameMcpNumberFormatter.Format(new BigDouble(-5.634d, 24)),
            GameMcpNumberFormatter.Format(new BigDouble(1.234d, -2)),
        };
        Assert.All(exponential, value => Assert.Matches(
            "^-?[1-9][0-9]?\\.[0-9]{2}e-?[0-9]+$",
            value));
        Assert.Equal("1.40e4", exponential[0]);
        Assert.Equal("7.82e4", exponential[1]);
        Assert.Equal("4.40e3", exponential[2]);
        Assert.Equal("1.10e24", exponential[3]);
        Assert.Equal("-5.63e24", exponential[4]);
        Assert.Equal("1.23e-2", exponential[5]);
        Assert.Equal("1.23e-3", GameMcpNumberFormatter.Format(new BigDouble(1.234d, -3)));

        // The game rounds the mantissa where it stands instead of renormalising it, so a hair
        // under a million is ten-point-something times a hundred thousand on the screen.
        Assert.Equal("10.00e5", GameMcpNumberFormatter.Format(new BigDouble(9.9999999d, 5)));
        Assert.Equal("10.00e5", GameMcpNumberFormatter.Format(new BigDouble(9.99999999d, 5)));

        // Inside the window the screen writes the number plainly and narrows its decimals as the
        // number grows: three below one, two below ten, one below a hundred, none above it, and
        // none at all for a whole number.
        Assert.Equal("0.700", GameMcpNumberFormatter.Format(new BigDouble(7d, -1)));
        Assert.Equal("1.15", GameMcpNumberFormatter.Format(new BigDouble(1.15d, 0)));
        Assert.Equal("12.3", GameMcpNumberFormatter.Format(new BigDouble(1.234d, 1)));
        Assert.Equal("123", GameMcpNumberFormatter.Format(new BigDouble(1.23456d, 2)));
        Assert.Equal("26", GameMcpNumberFormatter.Format(new BigDouble(2.6d, 1)));
    }

    [Fact]
    public void Near_equal_payment_evidence_has_one_honest_rounded_token()
    {
        var cost = GameMcpObjectProjector.Project(new BigDouble(1.1d, 24));
        var observed = GameMcpObjectProjector.Project(new BigDouble(1.10000000000001d, 24));

        Assert.Equal(JTokenType.String, cost.Type);
        Assert.Equal("1.10e24", (string?)cost);
        Assert.Equal(cost, observed);
    }
}
#endif
