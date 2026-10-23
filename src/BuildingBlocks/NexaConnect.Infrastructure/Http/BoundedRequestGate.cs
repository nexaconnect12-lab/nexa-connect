namespace NexaConnect.Infrastructure.Http;

// Transport-only fixed-window accounting: bounded keys, no persisted/logged client addresses.
public sealed class BoundedRequestGate(TimeProvider clock, int perKey = 60, int global = 600, int maximumKeys = 4096)
{
    private readonly object sync = new();
    private readonly Dictionary<string, int> counts = [];
    private long window = -1;
    private int total;
    public bool Admit(string key)
    {
        lock (sync)
        {
            long current = clock.GetUtcNow().ToUnixTimeSeconds() / 60;
            if (current != window) { counts.Clear(); total = 0; window = current; }
            if (total >= global || (!counts.ContainsKey(key) && counts.Count >= maximumKeys)) return false;
            counts.TryGetValue(key, out int count);
            if (count >= perKey) return false;
            counts[key] = count + 1; total++; return true;
        }
    }
}
