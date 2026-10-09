using System;
using System.IO;
using System.Text;
using System.Text.Json;

namespace TbtHttpBridge;

/// <summary>日志与状态文件。绝不写插件目录（目录指纹会被改坏）。</summary>
internal static class BridgeLog
{
    private static readonly object Gate = new();
    private static readonly string Root = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TbtHttpBridge");
    private static readonly string LogPath = Path.Combine(Root, "bridge.log");
    private const long MaximumBytes = 256 * 1024;

    internal static string Directory => Root;

    internal static void Info(string message) => Write("INFO", message);

    internal static void Error(string message, Exception? exception = null) =>
        Write("ERROR", exception is null
            ? message
            : message + " :: " + exception.GetType().Name + ": " + exception.Message);

    private static void Write(string level, string message)
    {
        try
        {
            lock (Gate)
            {
                System.IO.Directory.CreateDirectory(Root);
                if (File.Exists(LogPath) && new FileInfo(LogPath).Length > MaximumBytes) File.Delete(LogPath);
                File.AppendAllText(
                    LogPath,
                    DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " [" + level + "] " + message + Environment.NewLine,
                    new UTF8Encoding(false));
            }
        }
        catch
        {
        }
    }

    internal static void WriteStatus(object status)
    {
        try
        {
            lock (Gate)
            {
                System.IO.Directory.CreateDirectory(Root);
                var json = JsonSerializer.SerializeToUtf8Bytes(status, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllBytes(Path.Combine(Root, "status.json"), json);
            }
        }
        catch
        {
        }
    }
}
