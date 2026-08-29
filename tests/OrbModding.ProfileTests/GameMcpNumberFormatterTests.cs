#if SERVICE_CYCLE_PROFILE
using Newtonsoft.Json.Linq;
using OrbAutomata.GameMcp;
using Xunit;

namespace OrbModding.ProfileTests;

public sealed class GameMcpNumberFormatterTests
{
    [Fact]
    public void Canonical_scalar_shape_is_always_scientific_except_zero()
    {
        Assert.Equal("0", GameMcpNumberFormatter.Format(BigDouble.Zero));
        var canonical = new[]
        {
            GameMcpNumberFormatter.Format(new BigDouble(1.4d, 4)),
            GameMcpNumberFormatter.Format(new BigDouble(7.816502d, 4)),
            GameMcpNumberFormatter.Format(new BigDouble(4.4d, 3)),
            GameMcpNumberFormatter.Format(new BigDouble(1.1d, 24)),
            GameMcpNumberFormatter.Format(new BigDouble(-5.634d, 24)),
            GameMcpNumberFormatter.Format(new BigDouble(1.234d, -2)),
        };
        Assert.All(canonical, value => Assert.Matches(
            "^-?[1-9](?:\\.[0-9]{1,2})?e-?[0-9]+$",
            value));
        Assert.Equal("1.4e4", canonical[0]);
        Assert.Equal("7.82e4", canonical[1]);
        Assert.Equal("4.4e3", GameMcpNumberFormatter.Format(new BigDouble(4.4d, 3)));
        Assert.Equal("1.1e24", GameMcpNumberFormatter.Format(new BigDouble(1.1d, 24)));
        Assert.Equal("5.63e24", GameMcpNumberFormatter.Format(new BigDouble(5.634d, 24)));
        Assert.Equal("-5.63e24", GameMcpNumberFormatter.Format(new BigDouble(-5.634d, 24)));
        Assert.Equal("1e6", GameMcpNumberFormatter.Format(new BigDouble(9.9999999d, 5)));
        Assert.Equal("1e6", GameMcpNumberFormatter.Format(new BigDouble(9.99999999d, 5)));
        Assert.Equal("1.23e-2", GameMcpNumberFormatter.Format(new BigDouble(1.234d, -2)));
        Assert.Equal("1.23e-3", GameMcpNumberFormatter.Format(new BigDouble(1.234d, -3)));
    }

    [Fact]
    public void Near_equal_payment_evidence_has_one_honest_rounded_token()
    {
        var cost = GameMcpObjectProjector.Project(new BigDouble(1.1d, 24));
        var observed = GameMcpObjectProjector.Project(new BigDouble(1.10000000000001d, 24));

        Assert.Equal(JTokenType.String, cost.Type);
        Assert.Equal("1.1e24", (string?)cost);
        Assert.Equal(cost, observed);
    }
}
#endif
