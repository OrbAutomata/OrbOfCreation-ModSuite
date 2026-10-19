#if SERVICE_CYCLE_PROFILE
using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using OrbModding.Common.Runtime.World;

namespace OrbAutomata.GameMcp;

internal sealed class GameMcpToolExecution
{
    internal GameMcpToolExecution(
        GameMcpValue payload,
        byte[]? inlinePng,
        bool isError,
        EntityIdentityCatalogSnapshot? entityIdentities = null)
    {
        Payload = payload ?? throw new ArgumentNullException(nameof(payload));
        InlinePng = inlinePng;
        IsError = isError;
        EntityIdentities = entityIdentities ?? EntityIdentityCatalogPublication.Current;
    }

    private GameMcpToolExecution(string text)
    {
        TextContent = string.IsNullOrWhiteSpace(text)
            ? throw new ArgumentException("Text tool content must not be empty.", nameof(text))
            : text;
    }

    internal GameMcpValue? Payload { get; }
    internal byte[]? InlinePng { get; }
    internal bool IsError { get; }
    internal string? TextContent { get; }
    internal EntityIdentityCatalogSnapshot EntityIdentities { get; } =
        EntityIdentityCatalogSnapshot.Unbound(0);

    internal static GameMcpToolExecution Read(GameMcpValue payload) =>
        new(payload, null, false);
    internal static GameMcpToolExecution Read(GameMcpObjectBuilder payload) =>
        new(payload.Freeze(), null, false);
    internal static GameMcpToolExecution Error(GameMcpValue payload) =>
        new(payload, null, true);
    internal static GameMcpToolExecution Error(GameMcpObjectBuilder payload) =>
        new(payload.Freeze(), null, true);
    internal static GameMcpToolExecution Text(string text) => new(text);

    internal GameMcpToolExecution WithEntityIdentities(
        EntityIdentityCatalogSnapshot entityIdentities) =>
        TextContent is not null
            ? this
            : new GameMcpToolExecution(
                Payload!, InlinePng, IsError,
                entityIdentities ?? throw new ArgumentNullException(nameof(entityIdentities)));

    internal JObject ToProtocolResult()
    {
        if (TextContent is not null)
        {
            return new JObject
            {
                ["content"] = new JArray
                {
                    new JObject
                    {
                        ["type"] = "text",
                        ["text"] = TextContent,
                    },
                },
            };
        }
        // One response, one representation. A page of text is what a caller reads, so it is what the
        // server says; emitting the same answer twice — once as a document and once as prose — made
        // clients decode and truncate the same result twice, and no tool here declares an output
        // schema that would oblige a machine-shaped copy.
        var content = new JArray();
        if (InlinePng is not null)
        {
            content.Add(new JObject
            {
                ["type"] = "image",
                ["data"] = Convert.ToBase64String(InlinePng),
                ["mimeType"] = "image/png",
            });
        }
        content.Add(new JObject
        {
            ["type"] = "text",
            ["text"] = GameMcpTextPage.Render(
                GameMcpDocumentJsonEncoder.Encode(Payload!, EntityIdentities)),
        });
        var result = new JObject { ["content"] = content };
        if (IsError) result["isError"] = true;
        return result;
    }
}

internal readonly struct GameMcpProtocolResponse
{
    private GameMcpProtocolResponse(int statusCode, JObject? body)
    {
        StatusCode = statusCode;
        Body = body;
    }

    internal int StatusCode { get; }
    internal JObject? Body { get; }
    internal static GameMcpProtocolResponse Accepted() => new(202, null);
    internal static GameMcpProtocolResponse Json(JObject body) => new(200, body);
}

/// <summary>
/// A tool call the server will not run because of what it was asked, carrying the producer code
/// that says which kind of no it is.
/// </summary>
internal sealed class GameMcpInvalidParamsException : Exception
{
    internal GameMcpInvalidParamsException(
        string message,
        string code = "argument_validation_failed")
        : base(message)
    {
        Code = code;
    }

    internal string Code { get; }

    internal static GameMcpInvalidParamsException Validation(IReadOnlyList<string> errors) =>
        new("tool arguments failed schema validation: " + string.Join("; ", errors));
}
#endif
