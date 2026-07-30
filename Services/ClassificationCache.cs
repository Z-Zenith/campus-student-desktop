using System;
using System.Collections.Concurrent;

namespace StudentDesktop.Services;

// SDA-03 classification policy engine (SDA/SEK plan, Work Item A2). Purely a performance
// cache of results the backend already decided (site_classification_cache is the real
// shared source of truth) — avoids a network round trip on every repeat visit to the same
// host within a session. In-memory rather than SQLite-backed for now: this cache doesn't
// need to survive an app restart (a cold cache just means the first visit each session
// re-fetches, no correctness impact), so it doesn't need to wait on Work Item C1's
// SQLCipher-backed local store to land first — worth revisiting once that's in place, but
// not blocked on it.
public class ClassificationCache
{
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(24);

    public readonly record struct Entry(bool Allowed, double Score, DateTime CachedAtUtc)
    {
        public bool IsExpired => DateTime.UtcNow - CachedAtUtc > Ttl;
    }

    private readonly ConcurrentDictionary<string, Entry> _entries = new();

    public bool TryGet(string host, out Entry entry)
    {
        if (_entries.TryGetValue(host, out entry) && !entry.IsExpired)
        {
            return true;
        }
        entry = default;
        return false;
    }

    public void Set(string host, bool allowed, double score) =>
        _entries[host] = new Entry(allowed, score, DateTime.UtcNow);

    // Called after submitting feedback for a host (see BrowserTabViewModel) — mirrors the
    // backend's own cache-invalidation-on-feedback behavior so a "this is wrongly blocked"
    // report doesn't keep showing the stale local result for up to 24h either.
    public void Invalidate(string host) => _entries.TryRemove(host, out _);
}
