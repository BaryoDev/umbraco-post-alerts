using System.Security.Cryptography;
using System.Text;

namespace BaryoDev.Umbraco.PostAlerts.Services;

/// <summary>In-memory sliding window per client. The address is hashed with a per-process key and never kept.</summary>
public sealed class RateLimiter(TimeProvider time)
{
    private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);
    private readonly Dictionary<string, Queue<DateTimeOffset>> _hits = new();
    private readonly Lock _gate = new();
    private DateTimeOffset _lastSweep;

    public bool TryAcquire(string client, int max, TimeSpan window)
    {
        string id = Convert.ToHexString(HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(client)));
        DateTimeOffset now = time.GetUtcNow();
        lock (_gate)
        {
            if (now - _lastSweep > window)
            {
                Sweep(now, window);
                _lastSweep = now;
            }
            if (!_hits.TryGetValue(id, out var queue))
            {
                queue = new Queue<DateTimeOffset>();
                _hits[id] = queue;
            }
            while (queue.Count > 0 && now - queue.Peek() > window)
            {
                queue.Dequeue();
            }
            if (queue.Count >= max)
            {
                return false;
            }
            queue.Enqueue(now);
            return true;
        }
    }

    private void Sweep(DateTimeOffset now, TimeSpan window)
    {
        foreach (var key in _hits.Where(p => p.Value.Count == 0 || now - p.Value.Peek() > window && now - p.Value.Last() > window)
                     .Select(p => p.Key).ToList())
        {
            _hits.Remove(key);
        }
    }
}
