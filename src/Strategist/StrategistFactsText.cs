using System;
using System.Globalization;
using System.Text;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.Strategy;

namespace OrbAutomata;

/// <summary>
/// The strategist's exact inputs as text: what <c>strategist section=facts</c> prints. Numbers are
/// written losslessly, so a fixture that parses the text and derives again reproduces the bulletin.
/// </summary>
/// <remarks>
/// One record per line, a keyword then <c>key=value</c> fields; a <c>name=</c> field is always last
/// and runs to the end of the line, so names keep their spaces.
/// </remarks>
internal static class StrategistFactsText
{
    internal static string Write(StrategistFacts facts, StrategistWindowReading window)
    {
        var text = new StringBuilder();
        var clock = facts.Clock;
        text.Append("facts world=").Append(facts.WorldGeneration.ToString(CultureInfo.InvariantCulture))
            .Append(" played=").Append(Number(clock.TotalSeconds));
        if (clock.HasThisReset) text.Append(" reset=").Append(Number(clock.ThisResetSeconds));
        text.Append(" challenges=").Append(clock.ActiveChallenges.ToString(CultureInfo.InvariantCulture))
            .Append(" root=").Append(facts.ChainRoot.ToString("D")).Append('\n');

        foreach (var open in facts.Open.AsSpan())
        {
            text.Append("open");
            if (open.HasLevel)
                text.Append(" level=").Append(open.Current.ToString(CultureInfo.InvariantCulture))
                    .Append('/').Append(open.Maximum.ToString(CultureInfo.InvariantCulture));
            text.Append(" name=").Append(open.Name).Append('\n');
        }

        foreach (var resource in facts.Resources.AsSpan())
        {
            text.Append("resource ").Append(resource.Id.ToString("D"))
                .Append(" held=").Append(Number(resource.Held));
            if (resource.Capped) text.Append(" cap=").Append(Number(resource.Cap));
            if (!resource.Visible) text.Append(" hidden");
            if (resource.Bandwidth) text.Append(" points");
            text.Append(" name=").Append(resource.Name).Append('\n');
        }

        foreach (var node in facts.Nodes.AsSpan())
        {
            text.Append("node ").Append(node.Id.ToString("D")).Append(' ').Append(Kind(node.Kind));
            if (node.Offered) text.Append(" offered");
            if (node.Owned) text.Append(" owned");
            if (node.Gate) text.Append(" gate");
            text.Append(" name=").Append(node.Name).Append('\n');
        }

        foreach (var price in facts.Prices.AsSpan())
        {
            text.Append("price ").Append(price.NodeId.ToString("D")).Append(' ')
                .Append(price.ResourceId.ToString("D")).Append(" amount=").Append(Number(price.Amount))
                .Append('\n');
        }

        foreach (var edge in facts.Edges.AsSpan())
        {
            text.Append("edge ").Append(edge.OwnerId.ToString("D")).Append(' ')
                .Append(edge.TargetId.ToString("D"));
            if (edge.Met) text.Append(" met");
            text.Append('\n');
        }

        text.Append("window required=").Append(Number(window.RequiredSeconds)).Append('\n');
        foreach (var row in window.Rows.AsSpan())
        {
            text.Append("gain ").Append(row.ResourceId.ToString("D"))
                .Append(" gained=").Append(Number(row.Gained))
                .Append(" span=").Append(Number(row.SpanSeconds));
            if (row.ClippedAtCap) text.Append(" clipped");
            text.Append('\n');
        }

        return text.ToString();
    }

    private static string Kind(StrategistNodeKind kind) => kind switch
    {
        StrategistNodeKind.Upgrade => "upgrade",
        StrategistNodeKind.Research => "research",
        StrategistNodeKind.Structure => "structure",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    /// <summary>Mantissa and exponent exactly, so a number survives the trip.</summary>
    private static string Number(BigDouble value) =>
        value.Mantissa.ToString("R", CultureInfo.InvariantCulture) + "e" +
        value.Exponent.ToString(CultureInfo.InvariantCulture);
}
