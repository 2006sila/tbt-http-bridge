using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace TbtHttpBridge;

/// <summary>一次等待宿主轮询取走的控制请求。</summary>
internal sealed class PendingControl
{
    private readonly TaskCompletionSource<string?> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal PendingControl(string route, string value)
    {
        Route = route;
        Value = value;
    }

    internal string Route { get; }

    internal string Value { get; }

    internal Task<string?> Completion => _completion.Task;

    internal void Complete(string? error) => _completion.TrySetResult(error);
}

internal sealed class ControlQueue
{
    internal const int Capacity = 256;

    private readonly object _gate = new();
    private readonly Queue<PendingControl> _queue = new();

    internal int Count
    {
        get
        {
            lock (_gate) return _queue.Count;
        }
    }

    internal bool TryEnqueue(PendingControl item)
    {
        lock (_gate)
        {
            if (_queue.Count >= Capacity) return false;
            _queue.Enqueue(item);
            return true;
        }
    }

    internal PendingControl[] Take(int maximum)
    {
        lock (_gate)
        {
            var taken = new List<PendingControl>(Math.Min(maximum, _queue.Count));
            while (taken.Count < maximum && _queue.Count > 0) taken.Add(_queue.Dequeue());
            return taken.ToArray();
        }
    }

    internal void FailAll(string error)
    {
        foreach (var item in Take(int.MaxValue)) item.Complete(error);
    }
}
