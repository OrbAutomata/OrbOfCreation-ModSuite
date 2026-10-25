using System.Text;
using OrbModding.Common;

namespace OrbModConfig;

internal static class RuntimeDiagnosticsCardText
{
    public static string Title(RuntimeDiagnosticsCard card)
    {
        var version = string.IsNullOrWhiteSpace(card.Version) ? string.Empty : " " + card.Version;
        return $"{card.DisplayName}{version}  |  {card.Severity}";
    }

    public static string Body(RuntimeDiagnosticsCard card)
    {
        var text = new StringBuilder();
        text.Append(card.SchemaText);
        text.AppendLine();
        if (card.FeatureStatuses.Count == 0)
        {
            text.Append("Feature health: Not reported.");
        }
        else
        {
            text.Append("Feature health: ");
            for (var index = 0; index < card.FeatureStatuses.Count; index++)
            {
                if (index > 0) text.Append(" | ");
                var feature = card.FeatureStatuses[index];
                text.Append(feature.DisplayName).Append(": ").Append(FeatureStatusPresenter.Label(feature.State));
                if (!feature.Reason.IsEmpty) text.Append(" - ").Append(feature.Reason.Summary);
            }
        }

        foreach (var feature in card.FeatureStatuses)
        {
            if (string.IsNullOrWhiteSpace(feature.Detail)) continue;
            text.AppendLine();
            text.Append(feature.DisplayName).Append(" details:").AppendLine().Append(feature.Detail);
        }

        text.AppendLine();
        if (card.RuntimeServices.Count == 0)
        {
            text.Append("Runtime services: Not reported.");
            return text.ToString();
        }

        for (var serviceIndex = 0; serviceIndex < card.RuntimeServices.Count; serviceIndex++)
        {
            if (serviceIndex > 0) text.AppendLine();
            var service = card.RuntimeServices[serviceIndex];
            text.Append(service.DisplayName).Append(" runtime: ").Append(service.Implementation);
            foreach (var capability in service.Capabilities)
            {
                text.AppendLine();
                text.Append("  ").Append(capability.DisplayName).Append(": ")
                    .Append(FeatureStatusPresenter.Label(capability.State));
                if (!capability.Reason.IsEmpty) text.Append(" - ").Append(capability.Reason.Summary);
            }
        }
        return text.ToString();
    }
}
