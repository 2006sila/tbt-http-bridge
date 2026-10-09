using System;
using System.Collections.Generic;
using System.Text.Json;
using ThinkBookToolkit.PluginApi;

namespace TbtHttpBridge;

/// <summary>GET / 的载荷。字段名与顺序沿用 1.0.4 的 SharedHardwareSnapshot。</summary>
internal sealed record SharedHardwareSnapshot(
    double? CpuTemperatureC,
    double? CpuPowerW,
    double? GpuTemperatureC,
    double? GpuPowerW,
    int? Fan1Rpm,
    int? Fan2Rpm,
    string? PerformanceMode);

internal static class BridgeState
{
    private static readonly string[] ItsModeNames =
        ["Unknown", "PowerSaving", "Intelligent", "Performance", "Geek"];

    internal static SharedHardwareSnapshot Empty { get; } = new(null, null, null, null, null, null, null);

    internal static SharedHardwareSnapshot Build(PluginRequest request)
    {
        var sensors = request.Context.Sensors;
        return new SharedHardwareSnapshot(
            Number(sensors, "toolkit.sensor.cpuTemperatureC"),
            Number(sensors, "toolkit.sensor.cpuPowerW"),
            Number(sensors, "toolkit.sensor.gpuTemperatureC"),
            Number(sensors, "toolkit.sensor.gpuPowerW"),
            Rpm(sensors, "toolkit.sensor.fan1Rpm"),
            Rpm(sensors, "toolkit.sensor.fan2Rpm"),
            PerformanceMode(request.Context.Data));
    }

    /// <summary>只有 valid 读数才对外；stale / unavailable 一律 null（唯一有意的行为差异）。</summary>
    private static double? Number(IReadOnlyDictionary<string, SensorReading> sensors, string id)
    {
        if (sensors is null || !sensors.TryGetValue(id, out var reading) || reading is null) return null;
        if (!string.Equals(reading.Quality, "valid", StringComparison.Ordinal)) return null;
        var value = reading.Value;
        return value.HasValue && double.IsFinite(value.Value) ? value : null;
    }

    private static int? Rpm(IReadOnlyDictionary<string, SensorReading> sensors, string id)
    {
        var value = Number(sensors, id);
        return value.HasValue ? (int)Math.Round(value.Value) : null;
    }

    /// <summary>ItsMode 在宿主 JSON 里是数字；Unknown(0) / 缺失 → null。</summary>
    private static string? PerformanceMode(JsonElement data)
    {
        if (data.ValueKind != JsonValueKind.Object) return null;
        if (!data.TryGetProperty("Snapshot", out var snapshot) || snapshot.ValueKind != JsonValueKind.Object) return null;
        if (!snapshot.TryGetProperty("ItsMode", out var mode)) return null;

        if (mode.ValueKind == JsonValueKind.Number && mode.TryGetInt32(out var index))
            return index > 0 && index < ItsModeNames.Length ? ItsModeNames[index] : null;

        if (mode.ValueKind == JsonValueKind.String)
        {
            var text = mode.GetString() ?? string.Empty;
            if (int.TryParse(text, out var numeric))
                return numeric > 0 && numeric < ItsModeNames.Length ? ItsModeNames[numeric] : null;
            foreach (var name in ItsModeNames)
                if (string.Equals(name, text, StringComparison.OrdinalIgnoreCase) && !string.Equals(name, "Unknown", StringComparison.Ordinal))
                    return name;
        }

        return null;
    }
}
