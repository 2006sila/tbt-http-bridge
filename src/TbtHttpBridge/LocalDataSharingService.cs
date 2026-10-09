// 移植自 ThinkBookToolkit v1.0.4 —— src/ThinkBookToolkit/LocalDataSharingService.cs（MPL-2.0）。
// 该功能在 TBT 1.1.0 被移除（"Replaced the built-in localhost HTTP data/control listener"），
// 这里以插件形式恢复：路由、状态码、错误文案、CORS、响应字段保持不变。
//
// 相对原文的改动：
//   1) 三个注入点改为插件侧实现（快照 / 模式 / 控制回调）；
//   2) 传输层由 HttpListener 改为 TcpListener —— HttpListener 依赖 http.sys 的 URL 保留
//      （非管理员会 Access denied，且本机 HttpListener.IsSupported 为 false），
//      TcpListener 无此依赖，无论宿主是否以管理员启动都能监听回环地址；
//   3) 日志改 BridgeLog；新增只读 GET /status；请求体上限 64 KiB 与读取超时 5s；
//   4) 去掉 DeviceModelDetector 依赖。

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace TbtHttpBridge;

/// <summary>POST 的响应体，字段与 1.0.4 一致，仅新增 queued。</summary>
internal sealed record IntegrationControlResult(bool Success, string? Error, bool Queued = false);

/// <summary>插件侧控制结果。Queued=true 表示已交给宿主执行，不等于硬件已确认。</summary>
internal sealed record ControlOutcome(bool Success, string? Error, HttpStatusCode Status, bool Queued = false);

/// <summary>GET /status 的内容。</summary>
internal sealed record BridgeStatus(
    string Mode,
    int Port,
    bool Listening,
    string? LastError,
    int Queued,
    long Dispatched,
    string? LastRoute,
    string? LastValue,
    DateTimeOffset? LastDispatchAt,
    DateTimeOffset StartedAt);

internal sealed class LocalDataSharingService : IDisposable
{
    private const int MaximumBodyBytes = 64 * 1024;
    private const int MaximumHeaderBytes = 32 * 1024;
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(5);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly Func<SharedHardwareSnapshot> _snapshotProvider;
    private readonly Func<BridgeMode> _modeProvider;
    private readonly Func<string, string, Task<ControlOutcome>> _controlHandler;
    private readonly Func<BridgeStatus> _statusProvider;
    private readonly object _gate = new();
    private TcpListener? _listener;
    private CancellationTokenSource? _cancellation;
    private Task? _acceptTask;

    public LocalDataSharingService(
        Func<SharedHardwareSnapshot> snapshotProvider,
        Func<BridgeMode> modeProvider,
        Func<string, string, Task<ControlOutcome>> controlHandler,
        Func<BridgeStatus> statusProvider)
    {
        _snapshotProvider = snapshotProvider;
        _modeProvider = modeProvider;
        _controlHandler = controlHandler;
        _statusProvider = statusProvider;
    }

    public bool IsRunning
    {
        get
        {
            lock (_gate) return _listener is not null;
        }
    }

    public int? Port { get; private set; }

    public void Start(int port)
    {
        ValidatePort(port);
        lock (_gate)
        {
            if (_listener is not null && Port == port)
                return;
            StopCore();

            var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();

            var cancellation = new CancellationTokenSource();
            _listener = listener;
            _cancellation = cancellation;
            Port = port;
            _acceptTask = Task.Run(() => AcceptAsync(listener, cancellation.Token));
            BridgeLog.Info($"Local data sharing started at http://127.0.0.1:{port}/.");
        }
    }

    public void Stop()
    {
        lock (_gate)
            StopCore();
    }

    public static void ValidatePort(int port)
    {
        if (port is < 1 or > 65535)
            throw new ArgumentOutOfRangeException(
                nameof(port),
                "The data-sharing port must be between 1 and 65535.");
    }

    private async Task AcceptAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (SocketException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                _ = Task.Run(() => HandleClientAsync(client, cancellationToken), CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            BridgeLog.Error("Local data-sharing listener stopped unexpectedly.", ex);
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using (client)
        {
            var stream = client.GetStream();
            try
            {
                var request = await ReadRequestAsync(stream, cancellationToken).ConfigureAwait(false);
                if (request is null) return;

                if (request.TooLarge)
                {
                    await WriteResponseAsync(
                        stream,
                        HttpStatusCode.RequestEntityTooLarge,
                        Serialize(new IntegrationControlResult(false, "Request body is too large.")),
                        cancellationToken).ConfigureAwait(false);
                    return;
                }

                var (status, body) = await RouteAsync(request, cancellationToken).ConfigureAwait(false);
                await WriteResponseAsync(stream, status, body, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                BridgeLog.Error("A local data-sharing request failed.", ex);
                try
                {
                    await WriteResponseAsync(
                        stream,
                        HttpStatusCode.InternalServerError,
                        Serialize(new IntegrationControlResult(false, ex.GetBaseException().Message)),
                        CancellationToken.None).ConfigureAwait(false);
                }
                catch
                {
                }
            }
        }
    }

    private async Task<(HttpStatusCode Status, byte[] Body)> RouteAsync(
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Method == "OPTIONS")
            return (HttpStatusCode.NoContent, Array.Empty<byte>());

        if (request.Method == "GET")
        {
            if (request.Path == "status")
                return (HttpStatusCode.OK, Serialize(_statusProvider()));
            return (HttpStatusCode.OK, Serialize(_snapshotProvider()));
        }

        if (request.Method != "POST")
            return (HttpStatusCode.MethodNotAllowed, Array.Empty<byte>());

        if (_modeProvider() != BridgeMode.ShareDataAndControl)
            return (HttpStatusCode.Forbidden,
                Serialize(new IntegrationControlResult(false, "Control requests are disabled.")));

        var route = request.Path;
        if (route is not ("performance-mode" or "fan-strategy" or "fan-full-speed"))
            return (HttpStatusCode.NotFound,
                Serialize(new IntegrationControlResult(false, "Unknown control route.")));

        string value;
        using (var document = JsonDocument.Parse(request.Body))
        {
            if (!document.RootElement.TryGetProperty("value", out var property))
                return (HttpStatusCode.BadRequest,
                    Serialize(new IntegrationControlResult(false, "JSON property 'value' is required.")));

            value = property.ValueKind == JsonValueKind.String
                ? property.GetString() ?? string.Empty
                : property.GetRawText();
        }

        var outcome = await _controlHandler(route, value).ConfigureAwait(false);
        return (outcome.Success ? HttpStatusCode.OK : outcome.Status,
            Serialize(new IntegrationControlResult(outcome.Success, outcome.Error, outcome.Queued)));
    }

    private static byte[] Serialize<T>(T value) =>
        JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);

    private static async Task<HttpRequest?> ReadRequestAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        using var readTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        readTimeout.CancelAfter(ReadTimeout);
        var token = readTimeout.Token;

        var buffer = new byte[8192];
        var accumulated = new List<byte>(8192);
        var headerEnd = -1;
        while (headerEnd < 0)
        {
            var read = await stream.ReadAsync(buffer, token).ConfigureAwait(false);
            if (read <= 0) return null;
            for (var i = 0; i < read; i++) accumulated.Add(buffer[i]);
            if (accumulated.Count > MaximumHeaderBytes) return null;
            headerEnd = IndexOfHeaderEnd(accumulated);
        }

        var headerText = Encoding.ASCII.GetString(accumulated.ToArray(), 0, headerEnd);
        var lines = headerText.Split("\r\n");
        var parts = lines[0].Split(' ');
        if (parts.Length < 2) return null;

        var method = parts[0].ToUpperInvariant();
        var target = parts[1];
        var query = target.IndexOf('?');
        if (query >= 0) target = target[..query];
        var path = target.Trim('/').ToLowerInvariant();

        var contentLength = 0;
        foreach (var line in lines.Skip(1))
        {
            if (!line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)) continue;
            _ = int.TryParse(line["Content-Length:".Length..].Trim(), out contentLength);
        }

        if (contentLength > MaximumBodyBytes) return new HttpRequest(method, path, Array.Empty<byte>(), true);
        if (contentLength <= 0) return new HttpRequest(method, path, Array.Empty<byte>(), false);

        var body = new byte[contentLength];
        var bodyStart = headerEnd + 4;
        var carried = Math.Min(contentLength, accumulated.Count - bodyStart);
        accumulated.CopyTo(bodyStart, body, 0, carried);
        while (carried < contentLength)
        {
            var read = await stream.ReadAsync(body.AsMemory(carried), token).ConfigureAwait(false);
            if (read <= 0) break;
            carried += read;
        }

        return new HttpRequest(method, path, body, false);
    }

    private static int IndexOfHeaderEnd(List<byte> bytes)
    {
        for (var i = 3; i < bytes.Count; i++)
            if (bytes[i] == 10 && bytes[i - 1] == 13 && bytes[i - 2] == 10 && bytes[i - 3] == 13)
                return i - 3;
        return -1;
    }

    private static async Task WriteResponseAsync(
        NetworkStream stream,
        HttpStatusCode status,
        byte[] body,
        CancellationToken cancellationToken)
    {
        var head = new StringBuilder()
            .Append("HTTP/1.1 ").Append((int)status).Append(' ').Append(ReasonPhrase(status)).Append("\r\n")
            .Append("Access-Control-Allow-Origin: *\r\n")
            .Append("Access-Control-Allow-Headers: Content-Type\r\n")
            .Append("Access-Control-Allow-Methods: GET, POST, OPTIONS\r\n")
            .Append("Cache-Control: no-store\r\n")
            .Append("Content-Type: application/json; charset=utf-8\r\n")
            .Append("Content-Length: ").Append(body.Length).Append("\r\n")
            .Append("Connection: close\r\n\r\n")
            .ToString();

        await stream.WriteAsync(Encoding.ASCII.GetBytes(head), cancellationToken).ConfigureAwait(false);
        if (body.Length > 0)
            await stream.WriteAsync(body, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string ReasonPhrase(HttpStatusCode status) => status switch
    {
        HttpStatusCode.OK => "OK",
        HttpStatusCode.NoContent => "No Content",
        HttpStatusCode.BadRequest => "Bad Request",
        HttpStatusCode.Forbidden => "Forbidden",
        HttpStatusCode.NotFound => "Not Found",
        HttpStatusCode.MethodNotAllowed => "Method Not Allowed",
        HttpStatusCode.RequestEntityTooLarge => "Payload Too Large",
        HttpStatusCode.InternalServerError => "Internal Server Error",
        HttpStatusCode.ServiceUnavailable => "Service Unavailable",
        _ => "Status"
    };

    private void StopCore()
    {
        var listener = _listener;
        var cancellation = _cancellation;
        if (listener is null && cancellation is null)
            return;
        _listener = null;
        _cancellation = null;
        _acceptTask = null;
        Port = null;
        try { cancellation?.Cancel(); } catch { }
        try { listener?.Stop(); } catch { }
        cancellation?.Dispose();
        BridgeLog.Info("Local data sharing stopped.");
    }

    public void Dispose() => Stop();

    private sealed record HttpRequest(string Method, string Path, byte[] Body, bool TooLarge);
}
