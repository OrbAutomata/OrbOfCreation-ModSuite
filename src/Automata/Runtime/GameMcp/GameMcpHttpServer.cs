#if SERVICE_CYCLE_PROFILE
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace OrbAutomata.GameMcp;

/// <summary>Loopback-only MCP streamable HTTP transport.</summary>
internal sealed class GameMcpHttpServer : IDisposable
{
    internal const int DefaultPort = 19106;
    internal const string EndpointPath = "/mcp";
    private const int MaximumRequestBytes = 1024 * 1024;
    private readonly HttpListener _listener;
    private readonly GameMcpProtocolRouter _router;
    private readonly Thread _acceptThread;
    private readonly Action<string> _logInfo;
    private readonly Action<string> _logError;
    private readonly object _handlerSync = new();
    private readonly HashSet<HttpListenerContext> _activeHandlers = new();
    private int _disposed;

    private GameMcpHttpServer(
        GameMcpFrameInbox operations,
        int port,
        Action<string> logInfo,
        Action<string> logError)
    {
        if (port is <= 0 or > 65535) throw new ArgumentOutOfRangeException(nameof(port));
        _logInfo = logInfo ?? throw new ArgumentNullException(nameof(logInfo));
        _logError = logError ?? throw new ArgumentNullException(nameof(logError));
        _router = new GameMcpProtocolRouter(operations, _logError);
        Port = port;
        Endpoint = "http://127.0.0.1:" + port + EndpointPath;
        _listener = new HttpListener();
        _listener.Prefixes.Add("http://127.0.0.1:" + port + "/");
        _listener.Start();
        _acceptThread = new Thread(AcceptLoop)
        {
            IsBackground = true,
            Name = "Orb game MCP HTTP accept",
        };
        _acceptThread.Start();
    }

    internal int Port { get; }
    internal string Endpoint { get; }
    internal bool IsListening => Volatile.Read(ref _disposed) == 0 && _listener.IsListening;

    internal static GameMcpHttpServer? TryStart(
        GameMcpFrameInbox operations,
        Action<string> logInfo,
        Action<string> logError,
        int port = DefaultPort)
    {
        try
        {
            var server = new GameMcpHttpServer(operations, port, logInfo, logError);
            logInfo(
                "Game MCP streamable HTTP server listening on " + server.Endpoint +
                " (loopback only, protocol " + GameMcpProtocolRouter.LatestProtocolVersion + ").");
            return server;
        }
        catch (Exception exception)
        {
            logError(
                "Game MCP server is unavailable; gameplay remains active: " +
                exception.GetBaseException().Message);
            return null;
        }
    }

    private void AcceptLoop()
    {
        while (Volatile.Read(ref _disposed) == 0)
        {
            HttpListenerContext context;
            try { context = _listener.GetContext(); }
            catch (HttpListenerException) when (Volatile.Read(ref _disposed) != 0) { return; }
            catch (ObjectDisposedException) when (Volatile.Read(ref _disposed) != 0) { return; }
            catch (Exception exception)
            {
                if (Volatile.Read(ref _disposed) != 0) return;
                _logError("Game MCP accept failed: " + exception.GetBaseException().Message);
                continue;
            }
            if (!TryRegisterHandler(context))
            {
                Abort(context);
                return;
            }
            ThreadPool.QueueUserWorkItem(static value =>
            {
                var work = (GameMcpHttpWork)value!;
                try { work.Server.Handle(work.Context); }
                finally { work.Server.CompleteHandler(work.Context); }
            }, new GameMcpHttpWork(this, context));
        }
    }

    private bool TryRegisterHandler(HttpListenerContext context)
    {
        lock (_handlerSync)
        {
            if (Volatile.Read(ref _disposed) != 0) return false;
            return _activeHandlers.Add(context);
        }
    }

    private void CompleteHandler(HttpListenerContext context)
    {
        lock (_handlerSync)
        {
            if (!_activeHandlers.Remove(context))
                _logError("Game MCP handler accounting lost an active HTTP context.");
            Monitor.PulseAll(_handlerSync);
        }
    }

    private void Handle(HttpListenerContext context)
    {
        GameMcpFrameThreadBoundary.AssertTransportWorkAllowed("HTTP request handling");
        try
        {
            if (!IsEndpoint(context.Request.Url))
            {
                WriteEmpty(context.Response, 404);
                return;
            }
            if (!OriginAllowed(context.Request.Headers["Origin"]))
            {
                WriteJson(
                    context.Response,
                    403,
                    GameMcpProtocolRouter.Error(
                        null,
                        -32000,
                        "Origin is not an allowed loopback origin"));
                return;
            }
            if (context.Request.HttpMethod == "GET")
            {
                context.Response.Headers["Allow"] = "POST";
                WriteEmpty(context.Response, 405);
                return;
            }
            if (context.Request.HttpMethod != "POST")
            {
                context.Response.Headers["Allow"] = "GET, POST";
                WriteEmpty(context.Response, 405);
                return;
            }
            if (!AcceptsStreamableHttp(context.Request.Headers["Accept"]))
            {
                WriteJson(
                    context.Response,
                    406,
                    GameMcpProtocolRouter.Error(
                        null,
                        -32600,
                        "Accept must list application/json and text/event-stream"));
                return;
            }
            if (!IsJsonContentType(context.Request.ContentType))
            {
                WriteJson(
                    context.Response,
                    415,
                    GameMcpProtocolRouter.Error(
                        null,
                        -32600,
                        "Content-Type must be application/json"));
                return;
            }

            JObject request;
            try
            {
                var body = ReadBody(context.Request);
                var token = JToken.Parse(body);
                request = token as JObject ??
                    throw new JsonException("The MCP body must be one JSON-RPC object.");
            }
            // The one place a parser's own text may reach a caller: it describes the caller's own
            // bytes, which is the single exception text a caller can act on, and nothing of the
            // game's has failed — so there is no fault to log a reference for. Named by
            // ProductionSourceAuditTests.NoRawExceptionTextReachesTheWire, which exempts no other.
            catch (Exception exception) when (
                exception is JsonException or InvalidDataException or DecoderFallbackException)
            {
                WriteJson(
                    context.Response,
                    400,
                    GameMcpProtocolRouter.Error(
                        null,
                        -32700,
                        "The request body is not JSON-RPC the suite can read: " +
                        exception.GetBaseException().Message));
                return;
            }

            var method = (string?)request["method"];
            if (!string.Equals(method, "initialize", StringComparison.Ordinal))
            {
                var header = context.Request.Headers["MCP-Protocol-Version"];
                var version = string.IsNullOrWhiteSpace(header) ? "2025-03-26" : header.Trim();
                if (!GameMcpProtocolRouter.IsSupportedProtocolVersion(version))
                {
                    WriteJson(
                        context.Response,
                        400,
                        GameMcpProtocolRouter.Error(
                            request["id"],
                            -32602,
                            "unsupported MCP-Protocol-Version: " + version));
                    return;
                }
            }

            var response = _router.Handle(request);
            if (response.Body is null)
            {
                WriteEmpty(context.Response, response.StatusCode);
                return;
            }
            context.Response.Headers["MCP-Protocol-Version"] =
                GameMcpProtocolRouter.LatestProtocolVersion;
            WriteJson(context.Response, response.StatusCode, response.Body);
        }
        catch (Exception exception)
        {
            _logError("Game MCP request failed: " + exception.GetBaseException().Message);
            try
            {
                if (context.Response.OutputStream.CanWrite)
                {
                    WriteJson(
                        context.Response,
                        500,
                        GameMcpProtocolRouter.Error(
                            null,
                            -32603,
                            "internal MCP transport failure"));
                }
            }
            catch { }
        }
    }

    private static string ReadBody(HttpListenerRequest request)
    {
        GameMcpFrameThreadBoundary.AssertTransportWorkAllowed("HTTP body reading");
        if (request.ContentLength64 > MaximumRequestBytes)
            throw new InvalidDataException("The MCP request exceeds the 1 MiB limit.");
        using var memory = request.ContentLength64 > 0
            ? new MemoryStream((int)request.ContentLength64)
            : new MemoryStream();
        var buffer = new byte[8192];
        var total = 0;
        while (true)
        {
            var read = request.InputStream.Read(buffer, 0, buffer.Length);
            if (read == 0) break;
            total = checked(total + read);
            if (total > MaximumRequestBytes)
                throw new InvalidDataException("The MCP request exceeds the 1 MiB limit.");
            memory.Write(buffer, 0, read);
        }
        return new UTF8Encoding(false, true).GetString(memory.GetBuffer(), 0, checked((int)memory.Length));
    }

    private static void WriteJson(HttpListenerResponse response, int statusCode, JObject body)
    {
        GameMcpFrameThreadBoundary.AssertTransportWorkAllowed("HTTP response writing");
        var bytes = new UTF8Encoding(false).GetBytes(body.ToString(Formatting.None));
        response.StatusCode = statusCode;
        response.ContentType = "application/json; charset=utf-8";
        response.ContentEncoding = Encoding.UTF8;
        response.ContentLength64 = bytes.Length;
        response.OutputStream.Write(bytes, 0, bytes.Length);
        response.OutputStream.Close();
    }

    private static void WriteEmpty(HttpListenerResponse response, int statusCode)
    {
        GameMcpFrameThreadBoundary.AssertTransportWorkAllowed("HTTP response writing");
        response.StatusCode = statusCode;
        response.ContentLength64 = 0;
        response.OutputStream.Close();
    }

    private static bool IsEndpoint(Uri? uri) =>
        uri is not null &&
        (string.Equals(uri.AbsolutePath, EndpointPath, StringComparison.Ordinal) ||
         string.Equals(uri.AbsolutePath, EndpointPath + "/", StringComparison.Ordinal));

    internal static bool OriginAllowed(string? origin)
    {
        if (string.IsNullOrWhiteSpace(origin)) return true;
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return false;
        return string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase) ||
               IPAddress.TryParse(uri.Host, out var address) && IPAddress.IsLoopback(address);
    }

    internal static bool AcceptsStreamableHttp(string? accept)
    {
        if (string.IsNullOrWhiteSpace(accept)) return false;
        return accept.IndexOf("application/json", StringComparison.OrdinalIgnoreCase) >= 0 &&
               accept.IndexOf("text/event-stream", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool IsJsonContentType(string? contentType) =>
        !string.IsNullOrWhiteSpace(contentType) &&
        contentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        try { _listener.Stop(); }
        catch { }
        try { _listener.Close(); }
        catch { }
        if (Thread.CurrentThread != _acceptThread)
            _acceptThread.Join(TimeSpan.FromSeconds(2));
        lock (_handlerSync)
        {
            foreach (var context in _activeHandlers) Abort(context);
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
            while (_activeHandlers.Count > 0)
            {
                var remaining = deadline - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero) break;
                Monitor.Wait(_handlerSync, remaining);
            }
            if (_activeHandlers.Count > 0)
            {
                _logError(
                    "Game MCP shutdown timed out with " + _activeHandlers.Count +
                    " HTTP handlers still active; the closed command bus will reject late submissions.");
            }
        }
        _logInfo("Game MCP streamable HTTP server stopped.");
    }

    private static void Abort(HttpListenerContext context)
    {
        try { context.Response.Abort(); }
        catch { }
    }

    private readonly struct GameMcpHttpWork
    {
        internal GameMcpHttpWork(GameMcpHttpServer server, HttpListenerContext context)
        {
            Server = server;
            Context = context;
        }

        internal GameMcpHttpServer Server { get; }
        internal HttpListenerContext Context { get; }
    }
}
#endif
