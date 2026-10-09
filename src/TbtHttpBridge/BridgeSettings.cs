using System;
using System.Collections.Generic;
using System.Text.Json;

namespace TbtHttpBridge;

/// <summary>与 1.0.4 的 SoftwareIntegrationMode 同名同序。</summary>
internal enum BridgeMode
{
    Disabled,
    ShareDataOnly,
    ShareDataAndControl
}

internal sealed class BridgeSettings
{
    internal const int DefaultPort = 2975;

    internal BridgeMode Mode { get; init; } = BridgeMode.Disabled;

    internal int Port { get; init; } = DefaultPort;

    /// <summary>设置里的原始字符串，用于 /status 回显。</summary>
    internal string ModeKey { get; init; } = "disabled";

    internal bool SameAs(BridgeSettings other) => Mode == other.Mode && Port == other.Port;

    internal static BridgeSettings From(IReadOnlyDictionary<string, JsonElement>? settings)
    {
        var mode = BridgeMode.Disabled;
        var modeKey = "disabled";
        if (settings is not null && settings.TryGetValue("mode", out var modeElement) &&
            modeElement.ValueKind == JsonValueKind.String)
        {
            modeKey = modeElement.GetString() ?? "disabled";
            // 同时接受中文显示值（清单 Choices，与旧版界面用词一致）和历史 ASCII 值（升级前写进设置文件的）。
            mode = modeKey switch
            {
                "仅共享数据" or "share-data-only" => BridgeMode.ShareDataOnly,
                "允许共享数据和调整部分设置" or "share-data-and-control" => BridgeMode.ShareDataAndControl,
                _ => BridgeMode.Disabled
            };
        }

        var port = DefaultPort;
        if (settings is not null && settings.TryGetValue("port", out var portElement) &&
            portElement.ValueKind == JsonValueKind.Number && portElement.TryGetInt32(out var parsed) &&
            parsed is >= 1 and <= 65535)
        {
            port = parsed;
        }

        return new BridgeSettings { Mode = mode, Port = port, ModeKey = modeKey };
    }
}
