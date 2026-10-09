using System;
using System.Collections.Generic;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ThinkBookToolkit.PluginApi;

namespace TbtHttpBridge;

/// <summary>
/// 恢复 TBT 1.0.4 的「与其它软件联动」本机 HTTP 数据/控制接口。
/// 工作进程内运行：每秒被宿主轮询一次，设置变更即切换监听器；
/// 控制请求先在本地校验，再作为 HostCommand 交回宿主执行（宿主上限 8 条/次）。
/// </summary>
public sealed class HttpBridgePlugin : IToolkitPlugin
{
    private const int DispatchPerCycle = 4;
    private const string StatusOverviewId = "tbts.http-bridge.status";
    private static readonly TimeSpan DispatchTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(10);

    private readonly object _gate = new();
    private readonly ControlQueue _queue = new();
    private readonly DateTimeOffset _startedAt = DateTimeOffset.Now;

    private LocalDataSharingService? _service;
    private BridgeSettings _settings = new();
    private PluginRequest? _lastRequest;
    private string? _lastError;
    private DateTimeOffset _nextRetry = DateTimeOffset.MinValue;
    private long _dispatched;
    private string? _lastRoute;
    private string? _lastValue;
    private DateTimeOffset? _lastDispatchAt;
    private bool _disposed;

    public ValueTask<PluginResult> EvaluateAsync(PluginRequest request, CancellationToken cancellationToken)
    {
        var commands = Array.Empty<HostCommand>();
        try
        {
            if (request is not null)
            {
                _lastRequest = request;
                lock (_gate) SyncListener(BridgeSettings.From(request.PluginSettings));
                commands = Dispatch(request);
                BridgeLog.WriteStatus(BuildStatus());
            }
        }
        catch (Exception ex)
        {
            // 绝不把异常抛出 EvaluateAsync：宿主会因此挂起插件，HTTP 服务随之消失。
            BridgeLog.Error("EvaluateAsync failed.", ex);
        }

        var result = new PluginResult(new Dictionary<string, double?>(), null, commands)
        {
            OverviewValues = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [StatusOverviewId] = StatusText()
            }
        };
        return ValueTask.FromResult(result);
    }

    /// <summary>调用方须持有 _gate。</summary>
    private void SyncListener(BridgeSettings settings)
    {
        _service ??= new LocalDataSharingService(
            BuildSnapshot, () => _settings.Mode, HandleControlAsync, BuildStatus);

        var changed = !_settings.SameAs(settings);
        _settings = settings;

        if (settings.Mode == BridgeMode.Disabled)
        {
            if (_service.IsRunning)
            {
                _service.Stop();
                BridgeLog.Info("Listener stopped: integration mode is disabled.");
            }
            _lastError = null;
            return;
        }

        if (_service.IsRunning && _service.Port == settings.Port) return;
        if (!changed && DateTimeOffset.Now < _nextRetry) return;

        try
        {
            _service.Start(settings.Port);
            _lastError = null;
            _nextRetry = DateTimeOffset.MinValue;
            BridgeLog.Info("Listener started on 127.0.0.1:" + settings.Port + " (mode " + settings.ModeKey + ").");
        }
        catch (Exception ex)
        {
            _lastError = ex.Message;
            _nextRetry = DateTimeOffset.Now + RetryInterval;
            BridgeLog.Error("Listener failed to start on port " + settings.Port + ".", ex);
        }
    }

    private SharedHardwareSnapshot BuildSnapshot()
    {
        var request = _lastRequest;
        if (request is null) return BridgeState.Empty;
        try
        {
            return BridgeState.Build(request);
        }
        catch (Exception ex)
        {
            BridgeLog.Error("Snapshot build failed.", ex);
            return BridgeState.Empty;
        }
    }

    private BridgeStatus BuildStatus()
    {
        lock (_gate)
        {
            return new BridgeStatus(
                _settings.ModeKey,
                _settings.Port,
                _service?.IsRunning == true,
                _lastError,
                _queue.Count,
                _dispatched,
                _lastRoute,
                _lastValue,
                _lastDispatchAt,
                _startedAt);
        }
    }

    private string StatusText()
    {
        lock (_gate)
        {
            if (_settings.Mode == BridgeMode.Disabled) return "已关闭";
            if (_service?.IsRunning == true) return _settings.Port + " 监听中";
            return _lastError is null ? "未监听" : _settings.Port + " 端口不可用";
        }
    }

    private HostCommand[] Dispatch(PluginRequest request)
    {
        var pending = _queue.Take(DispatchPerCycle);
        if (pending.Length == 0) return Array.Empty<HostCommand>();

        var commands = new List<HostCommand>(pending.Length);
        foreach (var item in pending)
        {
            var error = Prepare(request, item, out var command);
            if (command is null)
            {
                item.Complete(error ?? "Control request was rejected.");
                continue;
            }

            commands.Add(command);
            _dispatched++;
            _lastRoute = item.Route;
            _lastValue = item.Value;
            _lastDispatchAt = DateTimeOffset.Now;
            BridgeLog.Info("Dispatching " + command.Id + " " + JsonSerializer.Serialize(command.Arguments));
            item.Complete(null);
        }

        return commands.ToArray();
    }

    private static string? Prepare(PluginRequest request, PendingControl item, out HostCommand? command)
    {
        command = null;

        var validation = ControlRules.TryCanonicalize(item.Route, item.Value, out var canonical);
        if (validation is not null) return validation;

        if (ControlRules.CapabilityFor(request.Context.Data, ControlRules.FeatureId(item.Route)) == Capability.Unavailable)
            return "The toolkit reports this control as unavailable on this device.";

        var (id, parameterType) = ControlRules.CommandFor(item.Route);
        var binding = ControlRules.FindOperation(request, id, parameterType);
        if (binding is null) return "The toolkit host does not expose " + id + "(" + parameterType + ").";

        command = ControlRules.Build(binding, item.Route, canonical);
        return null;
    }

    private async Task<ControlOutcome> HandleControlAsync(string route, string value)
    {
        BridgeSettings settings;
        lock (_gate) settings = _settings;

        if (settings.Mode != BridgeMode.ShareDataAndControl)
            return new ControlOutcome(false, "Control requests are disabled.", HttpStatusCode.Forbidden);
        if (!ControlRules.IsRoute(route))
            return new ControlOutcome(false, "Unknown control route.", HttpStatusCode.NotFound);

        var validation = ControlRules.TryCanonicalize(route, value, out var canonical);
        if (validation is not null)
            return new ControlOutcome(false, validation, HttpStatusCode.BadRequest);

        var pending = new PendingControl(route, canonical);
        if (!_queue.TryEnqueue(pending))
            return new ControlOutcome(false, "The control queue is full.", HttpStatusCode.ServiceUnavailable);

        string? error;
        try
        {
            error = await pending.Completion.WaitAsync(DispatchTimeout).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return new ControlOutcome(
                false,
                "The toolkit host did not dispatch the request in time.",
                HttpStatusCode.ServiceUnavailable);
        }

        return error is null
            ? new ControlOutcome(true, null, HttpStatusCode.OK, true)
            : new ControlOutcome(false, error, HttpStatusCode.BadRequest);
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed) return ValueTask.CompletedTask;
        _disposed = true;

        try
        {
            lock (_gate)
            {
                _service?.Dispose();
                _service = null;
            }
        }
        catch (Exception ex)
        {
            BridgeLog.Error("Listener dispose failed.", ex);
        }

        _queue.FailAll("The toolkit is stopping the local bridge.");
        BridgeLog.Info("Http bridge plugin disposed.");
        return ValueTask.CompletedTask;
    }
}
