using System.Runtime.CompilerServices;
using System.Text;
using AiTranslator.Core.Abstractions;
using AiTranslator.Core.Models;

namespace AiTranslator.Core.Translation;

/// <summary>
/// A decorator over <see cref="ITranslationService"/> that caches the FULL translation of a
/// (normalized text, source, target, model) key for a short TTL with LRU eviction. Repeated requests
/// for the same selection during a reading/editing session return instantly — no API call, no cost.
/// <para>
/// Misses stream through transparently and are stored only on a clean, non-empty completion (the store
/// happens after the enumeration, never in a <c>finally</c>, so cancelled/failed/partial/empty results
/// are never cached). Hits are returned instantly as a single chunk. A pure Core type with no provider
/// dependency, so it is fully unit-testable via an injected <see cref="TimeProvider"/>.
/// </para>
/// </summary>
public sealed class CachingTranslationService : ITranslationService
{
    private readonly ITranslationService _inner;
    private readonly TimeProvider _time;
    private readonly TimeSpan _ttl;
    private readonly int _maxEntries;

    private readonly object _lock = new();
    private readonly Dictionary<CacheKey, LinkedListNode<Entry>> _map = new();
    private readonly LinkedList<Entry> _lru = new();   // front = most-recently-used, tail = eviction victim

    public CachingTranslationService(
        ITranslationService inner,
        TimeProvider? timeProvider = null,
        TimeSpan? ttl = null,
        int maxEntries = 200)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _time = timeProvider ?? TimeProvider.System;
        _ttl = ttl ?? TimeSpan.FromMinutes(5);
        _maxEntries = maxEntries > 0
            ? maxEntries
            : throw new ArgumentOutOfRangeException(nameof(maxEntries));
    }

    public async IAsyncEnumerable<string> TranslateStreamAsync(
        TranslationRequest request, [EnumeratorCancellation] CancellationToken ct)
    {
        var key = BuildKey(request);

        // Never key/cache empty input — just pass through.
        if (key.Text.Length == 0)
        {
            await foreach (var chunk in _inner.TranslateStreamAsync(request, ct).ConfigureAwait(false))
            {
                yield return chunk;
            }

            yield break;
        }

        if (TryGet(key, out var cached))
        {
            ct.ThrowIfCancellationRequested();
            yield return cached;   // HIT: the whole result in one chunk — instant paint.
            yield break;
        }

        // MISS: stream through while accumulating, then store only on a clean, non-empty completion.
        var sb = new StringBuilder();
        await foreach (var chunk in _inner.TranslateStreamAsync(request, ct).ConfigureAwait(false))
        {
            sb.Append(chunk);
            yield return chunk;
        }

        // Reached only when the consumer enumerated to the end AND inner completed without throwing.
        // An early break / cancellation / exception skips this, so partials are never cached.
        if (!ct.IsCancellationRequested && sb.Length > 0)
        {
            Set(key, sb.ToString());
        }
    }

    // Style, Humanize, and CorrectSource are all part of the key: each changes the prompt, so each
    // produces a different result and must be cached independently (switching any of them is correctly
    // a miss).
    private static CacheKey BuildKey(TranslationRequest r)
        => new(
            r.Text.Trim(),
            r.Direction.SourceLang.ToLowerInvariant(),
            r.Direction.TargetLang.ToLowerInvariant(),
            r.Model,
            r.Style,
            r.Humanize,
            r.CorrectSource);

    private bool TryGet(CacheKey key, out string value)
    {
        lock (_lock)
        {
            if (_map.TryGetValue(key, out var node))
            {
                if (node.Value.ExpiresAt <= _time.GetUtcNow())
                {
                    _lru.Remove(node);
                    _map.Remove(key);   // stale — evict and fall through to a miss
                }
                else
                {
                    _lru.Remove(node);
                    _lru.AddFirst(node);   // refresh recency
                    value = node.Value.Value;
                    return true;
                }
            }
        }

        value = string.Empty;
        return false;
    }

    private void Set(CacheKey key, string value)
    {
        var entry = new Entry(key, value, _time.GetUtcNow() + _ttl);
        lock (_lock)
        {
            if (_map.TryGetValue(key, out var existing))
            {
                existing.Value = entry;
                _lru.Remove(existing);
                _lru.AddFirst(existing);
                return;
            }

            var node = _lru.AddFirst(entry);
            _map[key] = node;

            if (_map.Count > _maxEntries)
            {
                var victim = _lru.Last!;   // the least-recently-used tail
                _lru.RemoveLast();
                _map.Remove(victim.Value.Key);
            }
        }
    }

    private readonly record struct CacheKey(
        string Text, string SourceLang, string TargetLang, string Model, TranslationStyle Style,
        bool Humanize, bool CorrectSource);

    private sealed record Entry(CacheKey Key, string Value, DateTimeOffset ExpiresAt);
}
