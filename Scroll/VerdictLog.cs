namespace Recall_Seed.Scroll;

/// <summary>
/// Thread-safe bounded in-memory log of the most recent contract-6 verdicts. Written by the receiver's
/// accept-loop thread, read by the MCP tool thread, so every access is under a lock. Bounded (ring):
/// oldest verdicts are evicted past capacity, this is a live status window, not durable storage.
/// </summary>
internal sealed class VerdictLog
{
    public static VerdictLog Shared { get; } = new();

    readonly object _gate = new();
    readonly LinkedList<ScrollVerdict> _items = new();
    readonly int _capacity;

    public VerdictLog(int capacity = 100) => _capacity = Math.Max(1, capacity);

    public int Count
    {
        get { lock (_gate) return _items.Count; }
    }

    public void Add(ScrollVerdict v)
    {
        lock (_gate)
        {
            _items.AddLast(v);
            while (_items.Count > _capacity) _items.RemoveFirst();
        }
    }

    public IReadOnlyList<ScrollVerdict> Recent(int limit)
    {
        var take = Math.Max(1, limit);
        var outp = new List<ScrollVerdict>(Math.Min(take, _capacity));
        lock (_gate)
        {
            for (var n = _items.Last; n != null && outp.Count < take; n = n.Previous)
                outp.Add(n.Value);
        }
        return outp;
    }

    public ScrollVerdict? Latest(string? endpointId = null)
        => NewestWhere(v => endpointId == null || v.EndpointId == endpointId);

    public ScrollVerdict? LatestByCorrelation(string correlationId)
        => NewestWhere(v => v.Correlation == correlationId);

    ScrollVerdict? NewestWhere(Func<ScrollVerdict, bool> match)
    {
        lock (_gate)
        {
            for (var n = _items.Last; n != null; n = n.Previous)
                if (match(n.Value)) return n.Value;
        }
        return null;
    }
}
