using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using OrbAutomata.GameMcp;
using Xunit;

namespace OrbModding.ProfileTests;

/// <summary>
/// Each tool publishes its per-mode argument rules in tools/list, and a caller reads them as the
/// contract. A field a mode forbids must be refused, never accepted and silently dropped, and a field
/// a mode requires must be asked for, so the published rules and the enforced ones are one.
/// </summary>
public sealed class GameMcpModeRuleTests
{
    public static IEnumerable<object[]> ForbiddenFields() =>
        Rules().SelectMany(rule => rule.Forbidden.Select(field =>
            new object[] { rule.Tool, rule.Mode, field }));

    public static IEnumerable<object[]> RequiredFields() =>
        Rules().SelectMany(rule => rule.Required
            .Where(field => Schema(rule.Tool)["required"]?.Values<string>().Contains(field) != true)
            .Select(field => new object[] { rule.Tool, rule.Mode, field }));

    [Theory]
    [MemberData(nameof(ForbiddenFields))]
    public void A_field_the_mode_forbids_is_refused(string tool, string mode, string field)
    {
        var arguments = Satisfying(tool, mode);
        arguments[field] = Placeholder(tool, field);

        var page = Call(tool, arguments);

        Assert.Contains(
            "field '" + field + "' is not accepted for mode '" + mode + "'", page);
    }

    [Theory]
    [MemberData(nameof(RequiredFields))]
    public void A_field_the_mode_requires_is_asked_for(string tool, string mode, string field)
    {
        var arguments = Satisfying(tool, mode);
        arguments.Remove(field);

        var page = Call(tool, arguments);

        Assert.Contains(
            "required field '" + field + "' is missing for mode '" + mode + "'", page);
    }

    private static string Call(string tool, JObject arguments)
    {
        var router = new GameMcpProtocolRouter(new GameMcpFrameInbox());
        return GameMcpTestHarness.Page(router.Handle(GameMcpAcceptanceFixture.Request(1, "tools/call",
            new JObject { ["name"] = tool, ["arguments"] = arguments })));
    }

    private static JObject Satisfying(string tool, string mode)
    {
        var schema = Schema(tool);
        var arguments = new JObject { ["mode"] = mode };
        var rule = Rules().Single(candidate => candidate.Tool == tool && candidate.Mode == mode);
        foreach (var field in (schema["required"]?.Values<string>() ?? Enumerable.Empty<string>())
                     .Concat(rule.Required))
        {
            if (field is null || arguments.ContainsKey(field) || rule.Forbidden.Contains(field))
                continue;
            arguments[field] = Placeholder(tool, field);
        }
        return arguments;
    }

    private static JToken Placeholder(string tool, string field)
    {
        var property = Schema(tool)["properties"]![field]!;
        return (string?)property["type"] switch
        {
            "integer" => 1,
            "boolean" => true,
            "array" => new JArray(),
            _ => property["enum"]?.First?.DeepClone() ?? "00000000-0000-0000-0000-000000000001",
        };
    }

    private static JObject Schema(string tool) =>
        (JObject)GameMcpAcceptanceFixture.Tools()
            .Single(candidate => (string?)candidate["name"] == tool)["inputSchema"]!;

    private static IEnumerable<(string Tool, string Mode, string[] Required, string[] Forbidden)> Rules()
    {
        foreach (var tool in GameMcpAcceptanceFixture.Tools())
        {
            if (tool["inputSchema"]?["allOf"] is not JArray rules) continue;
            foreach (var rule in rules)
            {
                var then = rule["then"]!;
                yield return (
                    (string)tool["name"]!,
                    (string)rule["if"]!["properties"]!["mode"]!["const"]!,
                    then["required"]?.Values<string>().Select(field => field!).ToArray() ?? new string[0],
                    then["not"]?["anyOf"]?.Select(any => (string)any["required"]![0]!).ToArray()
                        ?? new string[0]);
            }
        }
    }
}
