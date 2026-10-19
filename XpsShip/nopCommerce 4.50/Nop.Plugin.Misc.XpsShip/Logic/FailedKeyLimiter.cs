namespace Nop.Plugin.Misc.XpsShip.Logic;

/// <summary>
/// Counts failed-key requests per IP and blocks an IP that passes the limit. Held in memory (registered as a
/// singleton), so it covers one web server; in a web farm each node counts separately.
/// </summary>
public class FailedKeyLimiter
{
    private readonly object _lock = new();
    private readonly Dictionary<string, List<DateTime>> _failures = new();
    private readonly Dictionary<string, DateTime> _blockedUntil = new();
    private readonly int _limit;
    private readonly TimeSpan _window;
    private readonly TimeSpan _block;

    public FailedKeyLimiter() : this(XpsShipDefaults.FailedKeyLimit,
        TimeSpan.FromMinutes(XpsShipDefaults.FailedKeyWindowMinutes), TimeSpan.FromMinutes(XpsShipDefaults.BlockMinutes))
    {
    }

    public FailedKeyLimiter(int limit, TimeSpan window, TimeSpan block)
    {
        _limit = limit;
        _window = window;
        _block = block;
    }

    public bool IsBlocked(string ip, DateTime nowUtc)
    {
        lock (_lock)
        {
            if (!_blockedUntil.TryGetValue(ip, out var until))
                return false;
            if (until > nowUtc)
                return true;

            _blockedUntil.Remove(ip);
            return false;
        }
    }

    /// <summary>Records a failure. True when this failure is the one that blocks the IP.</summary>
    public bool RecordFailure(string ip, DateTime nowUtc)
    {
        lock (_lock)
        {
            if (!_failures.TryGetValue(ip, out var times))
                _failures[ip] = times = new List<DateTime>();

            times.Add(nowUtc);
            times.RemoveAll(t => t <= nowUtc - _window);

            // Drop other IPs' stale entries so the dictionary can't grow without bound
            foreach (var stale in _failures.Where(f => f.Value.All(t => t <= nowUtc - _window)).Select(f => f.Key).ToList())
                _failures.Remove(stale);

            if (times.Count <= _limit)
                return false;

            _failures.Remove(ip);
            _blockedUntil[ip] = nowUtc + _block;
            return true;
        }
    }
}
