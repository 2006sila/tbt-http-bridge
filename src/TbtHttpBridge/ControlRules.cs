using System;
using System.Collections.Generic;
using System.Text.Json;
using ThinkBookToolkit.PluginApi;

namespace TbtHttpBridge;

internal enum Capability
{
    Unknown,
    Available,
    Unavailable
}

internal sealed class OperationBinding
{
    internal string Id { get; init; } = string.Empty;

    internal string Parameter { get; init; } = string.Empty;

    internal string ParameterType { get; init; } = string.Empty;
}

/// <summary>
/// 旧接口的取值规则 + 能力门禁 + 宿主命令绑定。
/// 取值白名单与错误文案与 TBT 1.0.4 的 HandleIntegrationControlAsync 逐字一致。
/// </summary>
internal static class ControlRules
{
    internal const string ItsModeCommand = "SetItsModeAsync";
    internal const string FanModeCommand = "SetFanModeAsync";
    internal const string FullSpeedCommand = "SetFullSpeedAsync";

    internal const string PerformanceModeRoute = "performance-mode";
    internal const string FanStrategyRoute = "fan-strategy";
    internal const string FullSpeedRoute = "fan-full-speed";

    internal static readonly string[] Routes = [PerformanceModeRoute, FanStrategyRoute, FullSpeedRoute];

    private static readonly string[] PerformanceModes = ["PowerSaving", "Intelligent", "Performance", "Geek"];
    private static readonly string[] FanStrategies = ["FirmwareAutomatic", "FixedRpm", "FanCurve", "AdvancedCurve"];

    internal static bool IsRoute(string route) =>
        Array.IndexOf(Routes, route) >= 0;

    /// <summary>校验并规范化为枚举名；返回 null 表示通过，否则为旧版错误文案。</summary>
    internal static string? TryCanonicalize(string route, string value, out string canonical)
    {
        canonical = string.Empty;
        switch (route)
        {
            case PerformanceModeRoute:
                foreach (var mode in PerformanceModes)
                    if (string.Equals(mode, value, StringComparison.OrdinalIgnoreCase)) { canonical = mode; return null; }
                return "Invalid performance mode. Use PowerSaving, Intelligent, Performance, or Geek.";

            case FanStrategyRoute:
                foreach (var strategy in FanStrategies)
                    if (string.Equals(strategy, value, StringComparison.OrdinalIgnoreCase)) { canonical = strategy; return null; }
                return "Invalid fan strategy. Use FirmwareAutomatic, FixedRpm, FanCurve, or AdvancedCurve.";

            case FullSpeedRoute:
                if (bool.TryParse(value, out var enabled)) { canonical = enabled ? "true" : "false"; return null; }
                return "Invalid fan-full-speed value. Use true or false.";

            default:
                return "Unknown control route.";
        }
    }

    internal static string FeatureId(string route) => route switch
    {
        PerformanceModeRoute => "performance.its",
        FanStrategyRoute => "performance.fan",
        _ => "performance.fan.full-speed"
    };

    /// <summary>Data.Features 里的可用性；报告缺失 → Unknown（按旧行为放行）。</summary>
    internal static Capability CapabilityFor(JsonElement data, string featureId)
    {
        if (data.ValueKind != JsonValueKind.Object) return Capability.Unknown;
        if (!data.TryGetProperty("Features", out var features) || features.ValueKind != JsonValueKind.Array)
            return Capability.Unknown;

        foreach (var item in features.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            if (!item.TryGetProperty("Id", out var id) || id.ValueKind != JsonValueKind.String) continue;
            if (!string.Equals(id.GetString(), featureId, StringComparison.Ordinal)) continue;

            var available = item.TryGetProperty("Available", out var a) && a.ValueKind == JsonValueKind.True;
            var partial = item.TryGetProperty("PartiallyAvailable", out var p) && p.ValueKind == JsonValueKind.True;
            return available || partial ? Capability.Available : Capability.Unavailable;
        }

        return Capability.Unknown;
    }

    /// <summary>从 Context.Operations 里解析命令名与参数名，避免参数名漂移导致宿主抛异常挂起插件。</summary>
    internal static OperationBinding? FindOperation(PluginRequest request, string commandId, string parameterType)
    {
        var operations = request.Context.Operations;
        if (operations is null) return null;

        foreach (var operation in operations)
        {
            if (operation is null || !string.Equals(operation.Id, commandId, StringComparison.Ordinal)) continue;
            foreach (var pair in operation.Parameters)
            {
                if (!string.Equals(pair.Value, parameterType, StringComparison.Ordinal)) continue;
                return new OperationBinding { Id = operation.Id, Parameter = pair.Key, ParameterType = pair.Value };
            }
            return null;
        }

        return null;
    }

    internal static HostCommand Build(OperationBinding binding, string route, string canonical)
    {
        var arguments = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            [binding.Parameter] = route == FullSpeedRoute
                ? JsonSerializer.SerializeToElement(bool.Parse(canonical))
                : JsonSerializer.SerializeToElement(canonical)
        };
        return new HostCommand(binding.Id, arguments);
    }

    internal static (string Command, string ParameterType) CommandFor(string route) => route switch
    {
        PerformanceModeRoute => (ItsModeCommand, "ItsMode"),
        FanStrategyRoute => (FanModeCommand, "FanControlMode"),
        _ => (FullSpeedCommand, "Boolean")
    };
}
