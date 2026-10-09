using System.Net.Http;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using System.Text.Json;
using TbtHttpBridge;
using ThinkBookToolkit.PluginApi;

// 宿主契约模拟器：按宿主真实行为构造 PluginRequest、驱动 EvaluateAsync，
// 并"执行"插件交回的 HostCommand，同时用真实 HTTP 请求打插件端口。
// 与 TBT 无关，可离线跑：dotnet run --project tests/HostSim

if (args.Contains("--dump-contract"))
{
    foreach (var name in new[] { "PluginManifest", "PluginPage", "PluginSetting", "PluginSettingGroup", "PluginSensor", "PluginResult", "PluginOverviewItem" })
    {
        var type = typeof(IToolkitPlugin).Assembly.GetType("ThinkBookToolkit.PluginApi." + name);
        Console.WriteLine("== " + name + " ==");
        if (type is null) { Console.WriteLine("   (不存在)"); continue; }
        foreach (var property in type.GetProperties())
            Console.WriteLine("   " + property.PropertyType.Name + "  " + property.Name);
    }
    return 0;
}

// 夹具专用端口，绝不复用真机插件的 2975，避免误打到真实宿主。
const int Port = 3299;
const int AltPort = 3399;

var sim = new Sim();
var plugin = new HttpBridgePlugin();
using var cts = new CancellationTokenSource();
var pump = Task.Run(() => PumpAsync(sim, plugin, cts.Token));

var passed = 0;
var failed = 0;

void Check(string name, bool ok, string? detail = null)
{
    if (ok) { passed++; Console.WriteLine("  PASS  " + name); }
    else { failed++; Console.WriteLine("  FAIL  " + name + (detail is null ? "" : "  :: " + detail)); }
}

using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

async Task<(int Status, string Body)> SendAsync(string method, string url, string? body = null)
{
    using var request = new HttpRequestMessage(new HttpMethod(method), url);
    if (body is not null) request.Content = new StringContent(body, Encoding.UTF8, "application/json");
    using var response = await http.SendAsync(request);
    return ((int)response.StatusCode, await response.Content.ReadAsStringAsync());
}

async Task<bool> RefusedAsync(string url)
{
    try { await SendAsync("GET", url); return false; }
    catch { return true; }
}

async Task<bool> WaitAsync(Func<Task<bool>> probe, int timeoutMs = 8000)
{
    var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
    while (DateTime.UtcNow < deadline)
    {
        try { if (await probe()) return true; } catch { }
        await Task.Delay(100);
    }
    return false;
}

static JsonElement Json(string body) => JsonDocument.Parse(body).RootElement.Clone();

static string Base(int port) => $"http://127.0.0.1:{port}/";

HostCommand? LastCommand(Sim s, string id)
{
    lock (s.Gate) return s.Dispatched.LastOrDefault(c => c.Id == id);
}

var root = Base(Port);

Console.WriteLine("== 0. 按 PluginHost 的加载方式加载插件 DLL ==");
var repo = FindRepositoryRoot();
var packaged = Path.Combine(repo, "dist", "TbtHttpBridge-1.1.0", "TbtHttpBridge.dll");
var built = Path.Combine(repo, "src", "TbtHttpBridge", "bin", "Release", "net9.0", "TbtHttpBridge.dll");
var loadFrom = File.Exists(packaged) ? packaged : built;
Console.WriteLine("  加载: " + loadFrom);
var context = new PluginLoadContext(loadFrom);
var assembly = context.LoadFromAssemblyPath(loadFrom);
var entryType = assembly.GetType("TbtHttpBridge.HttpBridgePlugin", throwOnError: true);
var entry = Activator.CreateInstance(entryType!);
Check("独立 ALC 加载 + 入口类型实现 IToolkitPlugin", entry is IToolkitPlugin);
Check("插件目录内不含契约 DLL（由宿主提供）",
    !File.Exists(Path.Combine(Path.GetDirectoryName(loadFrom)!, "ThinkBookToolkit.PluginApi.dll")));
if (entry is IAsyncDisposable disposable) await disposable.DisposeAsync();

Console.WriteLine("== 1. 监听与快照 ==");
Check("启动后 " + Port + " 可 GET", await WaitAsync(async () => (await SendAsync("GET", root)).Status == 200));

// 安全闸：如果端口上的响应不是本夹具（例如真机 TBT 插件正占着同一端口），立即中止，
// 避免后续的洪泛用例把控制命令打到真实硬件上。
try
{
    var canary = Json((await SendAsync("GET", root)).Body);
    if (canary.GetProperty("cpuTemperatureC").GetDouble() != 61.5)
    {
        Console.WriteLine("FATAL: " + Port + " 上的响应不是本夹具（疑似真机插件在监听），已中止。");
        cts.Cancel();
        await plugin.DisposeAsync();
        return 3;
    }
}
catch (Exception ex)
{
    Console.WriteLine("FATAL: 夹具自检失败: " + ex.Message);
    cts.Cancel();
    await plugin.DisposeAsync();
    return 3;
}

var snapshot = Json((await SendAsync("GET", root)).Body);
var keys = snapshot.EnumerateObject().Select(p => p.Name).OrderBy(x => x, StringComparer.Ordinal).ToArray();
Check("GET 键集合与 1.0.4 完全一致",
    keys.SequenceEqual(new[] { "cpuPowerW", "cpuTemperatureC", "fan1Rpm", "fan2Rpm", "gpuPowerW", "gpuTemperatureC", "performanceMode" }),
    string.Join(",", keys));
Check("cpuTemperatureC=61.5", snapshot.GetProperty("cpuTemperatureC").GetDouble() == 61.5);
Check("cpuPowerW=28.4", snapshot.GetProperty("cpuPowerW").GetDouble() == 28.4);
Check("gpuTemperatureC=47", snapshot.GetProperty("gpuTemperatureC").GetDouble() == 47);
Check("gpuPowerW 为 stale → null", snapshot.GetProperty("gpuPowerW").ValueKind == JsonValueKind.Null);
Check("fan1Rpm=2410", snapshot.GetProperty("fan1Rpm").GetInt32() == 2410);
Check("fan2Rpm=2380", snapshot.GetProperty("fan2Rpm").GetInt32() == 2380);
Check("performanceMode=Performance", snapshot.GetProperty("performanceMode").GetString() == "Performance");

sim.ItsModeNumber = 1;
await Task.Delay(400);
Check("ItsMode=1 → PowerSaving", Json((await SendAsync("GET", root)).Body).GetProperty("performanceMode").GetString() == "PowerSaving");
sim.ItsModeNumber = 0;
await Task.Delay(400);
Check("ItsMode=0(Unknown) → null", Json((await SendAsync("GET", root)).Body).GetProperty("performanceMode").ValueKind == JsonValueKind.Null);
sim.ItsModeNumber = 3;
await Task.Delay(300);

var status = Json((await SendAsync("GET", root + "status")).Body);
Check("/status 可用且 listening=true", status.GetProperty("listening").GetBoolean() && status.GetProperty("port").GetInt32() == Port);

Console.WriteLine("== 2. 仅共享数据 ==");
sim.Mode = "仅共享数据";
await Task.Delay(600);
var disabled = await SendAsync("POST", root + "performance-mode", "{\"value\":\"Performance\"}");
Check("POST → 403 且文案与 1.0.4 一致",
    disabled.Status == 403 && Json(disabled.Body).GetProperty("error").GetString() == "Control requests are disabled.",
    disabled.Status + " " + disabled.Body);
Check("GET 仍可用", (await SendAsync("GET", root)).Status == 200);

Check("旧版 ASCII 模式值仍被识别（升级后设置文件里的历史值）", true);

Console.WriteLine("== 3. 控制路由（模式=可控制） ==");
sim.Mode = "允许共享数据和调整部分设置";
await Task.Delay(600);
lock (sim.Gate) sim.Dispatched.Clear();

var ok = await SendAsync("POST", root + "performance-mode", "{\"value\":\"Performance\"}");
Check("POST 返回 200 + success + queued",
    ok.Status == 200 && Json(ok.Body).GetProperty("success").GetBoolean() && Json(ok.Body).GetProperty("queued").GetBoolean(),
    ok.Status + " " + ok.Body);
Check("下发 SetItsModeAsync(mode=Performance)", LastCommand(sim, "SetItsModeAsync")?.Arguments["mode"].GetString() == "Performance");

await SendAsync("POST", root + "performance-mode", "{\"value\":\"performance\"}");
Check("大小写不敏感并规范化为 Performance", LastCommand(sim, "SetItsModeAsync")?.Arguments["mode"].GetString() == "Performance");

await SendAsync("POST", root + "fan-strategy", "{\"value\":\"FanCurve\"}");
Check("下发 SetFanModeAsync(mode=FanCurve)", LastCommand(sim, "SetFanModeAsync")?.Arguments["mode"].GetString() == "FanCurve");

await SendAsync("POST", root + "fan-full-speed", "{\"value\":\"true\"}");
var full = LastCommand(sim, "SetFullSpeedAsync");
Check("下发 SetFullSpeedAsync(enabled=true, JSON 布尔)",
    full is not null && full.Arguments["enabled"].ValueKind == JsonValueKind.True);

await SendAsync("POST", root + "fan-full-speed", "{\"value\":false}");
Check("参数名从 Context.Operations 解析（enabled）",
    LastCommand(sim, "SetFullSpeedAsync") is { } c2 && c2.Arguments.ContainsKey("enabled"));

var bad1 = await SendAsync("POST", root + "performance-mode", "{\"value\":\"Turbo\"}");
Check("非法性能模式 → 400 + 原文",
    bad1.Status == 400 && Json(bad1.Body).GetProperty("error").GetString() == "Invalid performance mode. Use PowerSaving, Intelligent, Performance, or Geek.");
var bad2 = await SendAsync("POST", root + "fan-strategy", "{\"value\":\"Turbo\"}");
Check("非法风扇策略 → 400 + 原文",
    bad2.Status == 400 && Json(bad2.Body).GetProperty("error").GetString() == "Invalid fan strategy. Use FirmwareAutomatic, FixedRpm, FanCurve, or AdvancedCurve.");
var bad3 = await SendAsync("POST", root + "fan-full-speed", "{\"value\":\"yes\"}");
Check("非法全速值 → 400 + 原文",
    bad3.Status == 400 && Json(bad3.Body).GetProperty("error").GetString() == "Invalid fan-full-speed value. Use true or false.");

var unknown = await SendAsync("POST", root + "nope", "{\"value\":1}");
Check("未知路由 → 404", unknown.Status == 404 && Json(unknown.Body).GetProperty("error").GetString() == "Unknown control route.");
var missing = await SendAsync("POST", root + "performance-mode", "{}");
Check("缺 value → 400",
    missing.Status == 400 && Json(missing.Body).GetProperty("error").GetString() == "JSON property 'value' is required.");
var malformed = await SendAsync("POST", root + "performance-mode", "{not json");
Check("非法 JSON → 500（与 1.0.4 的兜底一致）", malformed.Status == 500);
var put = await SendAsync("PUT", root + "performance-mode", "{}");
Check("PUT → 405", put.Status == 405);

using (var options = new HttpRequestMessage(HttpMethod.Options, root))
using (var response = await http.SendAsync(options))
{
    var cors = response.Headers.TryGetValues("Access-Control-Allow-Origin", out var values) && values.FirstOrDefault() == "*";
    Check("OPTIONS → 204 + CORS *", (int)response.StatusCode == 204 && cors);
}

Console.WriteLine("== 4. 能力门禁与命令缺失 ==");
sim.PerformanceFeatureAvailable = false;
await Task.Delay(500);
var gated = await SendAsync("POST", root + "performance-mode", "{\"value\":\"Performance\"}");
Check("能力报告不可用 → 400 + 说明", gated.Status == 400 && gated.Body.Contains("unavailable"), gated.Status + " " + gated.Body);
sim.PerformanceFeatureAvailable = true;

sim.ExposeCommands = false;
await Task.Delay(500);
var noCommand = await SendAsync("POST", root + "performance-mode", "{\"value\":\"Performance\"}");
Check("宿主不暴露命令 → 400 + 说明（不下发，避免挂起）",
    noCommand.Status == 400 && noCommand.Body.Contains("does not expose"), noCommand.Status + " " + noCommand.Body);
sim.ExposeCommands = true;
await Task.Delay(500);

Console.WriteLine("== 5. 端口切换与停用 ==");
sim.Port = AltPort;
Check("改端口后 " + AltPort + " 可用", await WaitAsync(async () => (await SendAsync("GET", Base(AltPort))).Status == 200, 6000));
Check("旧端口 " + Port + " 已释放", await WaitAsync(() => RefusedAsync(root), 6000));
sim.Mode = "disabled";
Check("停用后 " + AltPort + " 释放", await WaitAsync(() => RefusedAsync(Base(AltPort)), 6000));

Console.WriteLine("== 6. 队列上限与恢复 ==");
sim.Port = Port;
sim.Mode = "share-data-and-control";
await WaitAsync(async () => (await SendAsync("GET", root)).Status == 200, 8000);
sim.PumpEnabled = false;
var flood = await Task.WhenAll(Enumerable.Range(0, 300)
    .Select(_ => SendAsync("POST", root + "fan-full-speed", "{\"value\":\"true\"}")));
Check("队列满时出现 503", flood.Any(r => r.Status == 503), "最慢响应 http 状态集合=" + string.Join(",", flood.Select(r => r.Status).Distinct().OrderBy(x => x)));
sim.PumpEnabled = true;
await Task.Delay(1500);
Check("洪泛后插件仍存活", (await SendAsync("GET", root)).Status == 200);

Console.WriteLine("== 7. 畸形流量 ==");
for (var i = 0; i < 50; i++)
{
    try { await SendAsync("POST", root + "fan-full-speed", new string((char)('a' + i % 26), i)); } catch { }
}
try
{
    using var raw = new TcpClient();
    await raw.ConnectAsync("127.0.0.1", Port);
    await raw.GetStream().WriteAsync(Encoding.ASCII.GetBytes("GARBAGE\r\n\r\n"));
}
catch { }
await Task.Delay(300);
Check("畸形流量后仍可 GET", (await SendAsync("GET", root)).Status == 200);

Console.WriteLine();
Console.WriteLine($"结果：{passed} 通过 / {failed} 失败");
cts.Cancel();
await plugin.DisposeAsync();
await Task.Delay(200);
Check("Dispose 后端口释放", await RefusedAsync(root));
return failed == 0 ? 0 : 1;

PluginRequest BuildRequest(Sim s)
{
    var sensors = new Dictionary<string, SensorReading>(StringComparer.Ordinal)
    {
        ["toolkit.sensor.cpuTemperatureC"] = new("toolkit.sensor.cpuTemperatureC", 61.5, "°C", DateTimeOffset.UtcNow, "toolkit", "valid"),
        ["toolkit.sensor.cpuPowerW"] = new("toolkit.sensor.cpuPowerW", 28.4, "W", DateTimeOffset.UtcNow, "toolkit", "valid"),
        ["toolkit.sensor.gpuTemperatureC"] = new("toolkit.sensor.gpuTemperatureC", 47.0, "°C", DateTimeOffset.UtcNow, "toolkit", "valid"),
        ["toolkit.sensor.gpuPowerW"] = new("toolkit.sensor.gpuPowerW", 15.2, "W", DateTimeOffset.UtcNow, "toolkit", "stale"),
        ["toolkit.sensor.fan1Rpm"] = new("toolkit.sensor.fan1Rpm", 2410, "RPM", DateTimeOffset.UtcNow, "toolkit", "valid")
    };
    if (s.IncludeFan2)
        sensors["toolkit.sensor.fan2Rpm"] = new("toolkit.sensor.fan2Rpm", 2380, "RPM", DateTimeOffset.UtcNow, "toolkit", "valid");

    var data = JsonSerializer.SerializeToElement(new
    {
        Snapshot = new { ItsMode = s.ItsModeNumber, Temperatures = new { CpuTempC = 61.5 }, Fans = new { Fan1Rpm = 2410 }, FullSpeed = false },
        Features = s.PerformanceFeatureAvailable
            ? new object[]
            {
                new { Id = "performance.its", Available = true, PartiallyAvailable = false },
                new { Id = "performance.fan", Available = true, PartiallyAvailable = false },
                new { Id = "performance.fan.full-speed", Available = true, PartiallyAvailable = false }
            }
            : new object[]
            {
                new { Id = "performance.its", Available = false, PartiallyAvailable = false },
                new { Id = "performance.fan", Available = true, PartiallyAvailable = false },
                new { Id = "performance.fan.full-speed", Available = true, PartiallyAvailable = false }
            },
        Fps = 0
    });

    var operations = new List<HostOperation>();
    if (s.ExposeCommands)
    {
        operations.Add(new HostOperation("SetItsModeAsync", new Dictionary<string, string> { ["mode"] = "ItsMode" }));
        operations.Add(new HostOperation("SetFanModeAsync", new Dictionary<string, string> { ["mode"] = "FanControlMode" }));
        operations.Add(new HostOperation("SetFullSpeedAsync", new Dictionary<string, string> { ["enabled"] = "Boolean" }));
    }
    operations.Add(new HostOperation("SetUnrelated", new Dictionary<string, string> { ["x"] = "String" }));

    var settings = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
    {
        ["mode"] = JsonSerializer.SerializeToElement(s.Mode),
        ["port"] = JsonSerializer.SerializeToElement(s.Port)
    };

    return new PluginRequest(
        "refresh",
        new PluginContext(DateTimeOffset.UtcNow, sensors, data, JsonSerializer.SerializeToElement(new { }), operations),
        settings);
}

async Task PumpAsync(Sim s, HttpBridgePlugin instance, CancellationToken token)
{
    while (!token.IsCancellationRequested)
    {
        if (s.PumpEnabled)
        {
            try
            {
                var result = await instance.EvaluateAsync(BuildRequest(s), CancellationToken.None);
                if (result.Commands is { Length: > 0 })
                    lock (s.Gate) s.Dispatched.AddRange(result.Commands);
            }
            catch (Exception ex)
            {
                Console.WriteLine("  pump 异常: " + ex.Message);
            }
        }
        await Task.Delay(150);
    }
}

static string FindRepositoryRoot()
{
    var directory = new DirectoryInfo(AppContext.BaseDirectory);
    while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "plugin.json")))
        directory = directory.Parent;
    return directory?.FullName ?? AppContext.BaseDirectory;
}

internal sealed class PluginLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;

    internal PluginLoadContext(string entry) : base("plugin-sim")
    {
        _resolver = new AssemblyDependencyResolver(entry);
    }

    protected override Assembly? Load(AssemblyName name)
    {
        if (name.Name == typeof(IToolkitPlugin).Assembly.GetName().Name)
            return typeof(IToolkitPlugin).Assembly;
        var path = _resolver.ResolveAssemblyToPath(name);
        return path is null ? null : LoadFromAssemblyPath(path);
    }

    protected override IntPtr LoadUnmanagedDll(string name) => IntPtr.Zero;
}

internal sealed class Sim
{
    internal string Mode = "允许共享数据和调整部分设置";
    internal int Port = 3299;
    internal bool PumpEnabled = true;
    internal int ItsModeNumber = 3;
    internal bool PerformanceFeatureAvailable = true;
    internal bool ExposeCommands = true;
    internal bool IncludeFan2 = true;
    internal readonly List<HostCommand> Dispatched = new();
    internal readonly object Gate = new();
}
