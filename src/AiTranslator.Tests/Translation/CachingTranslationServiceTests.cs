using System.Runtime.CompilerServices;
using AiTranslator.Core.Abstractions;
using AiTranslator.Core.Models;
using AiTranslator.Core.Translation;
using Xunit;

namespace AiTranslator.Tests;

public class CachingTranslationServiceTests
{
    private static readonly TranslationDirection EnFa = new("en", "fa");

    // ---- helpers -----------------------------------------------------------------------------

    /// <summary>An in-memory inner service: yields configured chunks, counts calls, and can throw
    /// (optionally after honoring cancellation) to exercise the "never cache a bad result" paths.</summary>
    private sealed class FakeInner : ITranslationService
    {
        private readonly string[] _chunks;
        private readonly Func<Exception>? _throwAfterFirst;
        private readonly bool _observeCancellation;

        public FakeInner(string[] chunks, Func<Exception>? throwAfterFirst = null, bool observeCancellation = false)
        {
            _chunks = chunks;
            _throwAfterFirst = throwAfterFirst;
            _observeCancellation = observeCancellation;
        }

        public int CallCount { get; private set; }

        public async IAsyncEnumerable<string> TranslateStreamAsync(
            string text, TranslationDirection direction, string model,
            [EnumeratorCancellation] CancellationToken ct)
        {
            CallCount++;
            await Task.Yield();

            bool first = true;
            foreach (var chunk in _chunks)
            {
                if (_observeCancellation)
                {
                    ct.ThrowIfCancellationRequested();
                }

                yield return chunk;

                if (first && _throwAfterFirst is not null)
                {
                    throw _throwAfterFirst();
                }

                first = false;
            }
        }
    }

    /// <summary>A controllable clock (no new package) — hand-rolled TimeProvider with Advance().</summary>
    private sealed class MutableTimeProvider(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }

    private static MutableTimeProvider Clock() => new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

    private static async Task<List<string>> Drain(IAsyncEnumerable<string> stream)
    {
        var list = new List<string>();
        await foreach (var chunk in stream)
        {
            list.Add(chunk);
        }

        return list;
    }

    private static CachingTranslationService Cache(
        ITranslationService inner, TimeProvider? clock = null, TimeSpan? ttl = null, int maxEntries = 200)
        => new(inner, clock, ttl, maxEntries);

    // ---- hit / miss --------------------------------------------------------------------------

    [Fact]
    public async Task Miss_then_hit_calls_inner_once()
    {
        var inner = new FakeInner(["Hel", "lo"]);
        var cache = Cache(inner, Clock());

        var first = string.Concat(await Drain(cache.TranslateStreamAsync("hi", EnFa, "m", default)));
        var second = string.Concat(await Drain(cache.TranslateStreamAsync("hi", EnFa, "m", default)));

        Assert.Equal(1, inner.CallCount);
        Assert.Equal("Hello", first);
        Assert.Equal("Hello", second);
    }

    [Fact]
    public async Task Hit_is_single_chunk()
    {
        var inner = new FakeInner(["a", "b", "c"]);
        var cache = Cache(inner, Clock());

        var miss = await Drain(cache.TranslateStreamAsync("x", EnFa, "m", default));
        var hit = await Drain(cache.TranslateStreamAsync("x", EnFa, "m", default));

        Assert.Equal(3, miss.Count);          // miss streams through chunk-by-chunk
        Assert.Single(hit);                   // hit returns the whole value at once
        Assert.Equal("abc", hit[0]);
    }

    // ---- results that must NOT be cached ------------------------------------------------------

    [Fact]
    public async Task Cancelled_request_is_not_cached()
    {
        var inner = new FakeInner(["one", "two", "three"], observeCancellation: true);
        var cache = Cache(inner, Clock());
        using var cts = new CancellationTokenSource();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in cache.TranslateStreamAsync("hi", EnFa, "m", cts.Token))
            {
                cts.Cancel();   // cancel mid-stream
            }
        });

        await Drain(cache.TranslateStreamAsync("hi", EnFa, "m", default));
        Assert.Equal(2, inner.CallCount);   // nothing was cached from the cancelled run
    }

    [Fact]
    public async Task Consumer_break_early_is_not_cached()
    {
        var inner = new FakeInner(["one", "two", "three"]);
        var cache = Cache(inner, Clock());

        await foreach (var _ in cache.TranslateStreamAsync("hi", EnFa, "m", default))
        {
            break;   // read the first chunk only, then abandon the stream
        }

        await Drain(cache.TranslateStreamAsync("hi", EnFa, "m", default));
        Assert.Equal(2, inner.CallCount);
    }

    [Fact]
    public async Task Inner_exception_is_not_cached()
    {
        var inner = new FakeInner(["partial"], throwAfterFirst: () => new InvalidOperationException("boom"));
        var cache = Cache(inner, Clock());

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await Drain(cache.TranslateStreamAsync("hi", EnFa, "m", default)));

        var good = new FakeInner(["ok"]);
        var cache2 = Cache(good, Clock());
        await Drain(cache2.TranslateStreamAsync("hi", EnFa, "m", default));

        Assert.Equal(1, inner.CallCount);   // the failed run was never stored
    }

    [Fact]
    public async Task Empty_result_is_not_cached()
    {
        var inner = new FakeInner([]);
        var cache = Cache(inner, Clock());

        await Drain(cache.TranslateStreamAsync("hi", EnFa, "m", default));
        await Drain(cache.TranslateStreamAsync("hi", EnFa, "m", default));

        Assert.Equal(2, inner.CallCount);
    }

    [Fact]
    public async Task Empty_input_is_passthrough_never_cached()
    {
        var inner = new FakeInner(["x"]);
        var cache = Cache(inner, Clock());

        await Drain(cache.TranslateStreamAsync("   ", EnFa, "m", default));
        await Drain(cache.TranslateStreamAsync("   ", EnFa, "m", default));

        Assert.Equal(2, inner.CallCount);
    }

    // ---- key composition ---------------------------------------------------------------------

    [Fact]
    public async Task Whitespace_is_trimmed_to_the_same_key()
    {
        var inner = new FakeInner(["v"]);
        var cache = Cache(inner, Clock());

        await Drain(cache.TranslateStreamAsync(" hello ", EnFa, "m", default));
        await Drain(cache.TranslateStreamAsync("hello", EnFa, "m", default));

        Assert.Equal(1, inner.CallCount);
    }

    [Fact]
    public async Task Text_case_is_a_distinct_key()
    {
        var inner = new FakeInner(["v"]);
        var cache = Cache(inner, Clock());

        await Drain(cache.TranslateStreamAsync("Hello", EnFa, "m", default));
        await Drain(cache.TranslateStreamAsync("hello", EnFa, "m", default));

        Assert.Equal(2, inner.CallCount);
    }

    [Theory]
    [InlineData("m1", "m2")]
    public async Task Different_model_is_a_miss(string a, string b)
    {
        var inner = new FakeInner(["v"]);
        var cache = Cache(inner, Clock());

        await Drain(cache.TranslateStreamAsync("hi", EnFa, a, default));
        await Drain(cache.TranslateStreamAsync("hi", EnFa, b, default));

        Assert.Equal(2, inner.CallCount);
    }

    [Fact]
    public async Task Different_target_or_source_is_a_miss()
    {
        var inner = new FakeInner(["v"]);
        var cache = Cache(inner, Clock());

        await Drain(cache.TranslateStreamAsync("hi", new TranslationDirection("en", "fa"), "m", default));
        await Drain(cache.TranslateStreamAsync("hi", new TranslationDirection("en", "de"), "m", default));   // target differs
        await Drain(cache.TranslateStreamAsync("hi", new TranslationDirection("fa", "en"), "m", default));   // source differs

        Assert.Equal(3, inner.CallCount);
    }

    [Fact]
    public async Task Language_codes_are_case_insensitive()
    {
        var inner = new FakeInner(["v"]);
        var cache = Cache(inner, Clock());

        await Drain(cache.TranslateStreamAsync("hi", new TranslationDirection("EN", "FA"), "m", default));
        await Drain(cache.TranslateStreamAsync("hi", new TranslationDirection("en", "fa"), "m", default));

        Assert.Equal(1, inner.CallCount);
    }

    // ---- TTL ---------------------------------------------------------------------------------

    [Fact]
    public async Task Hit_just_before_ttl_expiry()
    {
        var inner = new FakeInner(["v"]);
        var clock = Clock();
        var cache = Cache(inner, clock, ttl: TimeSpan.FromMinutes(5));

        await Drain(cache.TranslateStreamAsync("hi", EnFa, "m", default));
        clock.Advance(TimeSpan.FromMinutes(5) - TimeSpan.FromSeconds(1));
        await Drain(cache.TranslateStreamAsync("hi", EnFa, "m", default));

        Assert.Equal(1, inner.CallCount);
    }

    [Fact]
    public async Task Miss_after_ttl_expiry()
    {
        var inner = new FakeInner(["v"]);
        var clock = Clock();
        var cache = Cache(inner, clock, ttl: TimeSpan.FromMinutes(5));

        await Drain(cache.TranslateStreamAsync("hi", EnFa, "m", default));
        clock.Advance(TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(1));
        await Drain(cache.TranslateStreamAsync("hi", EnFa, "m", default));

        Assert.Equal(2, inner.CallCount);
    }

    // ---- LRU eviction ------------------------------------------------------------------------

    [Fact]
    public async Task Lru_evicts_the_oldest_over_cap()
    {
        var inner = new FakeInner(["v"]);
        var cache = Cache(inner, Clock(), maxEntries: 2);

        await Drain(cache.TranslateStreamAsync("A", EnFa, "m", default));
        await Drain(cache.TranslateStreamAsync("B", EnFa, "m", default));
        await Drain(cache.TranslateStreamAsync("C", EnFa, "m", default));   // evicts A (oldest)

        // Assert the survivors are hits BEFORE re-fetching A (re-caching A would itself evict B).
        await Drain(cache.TranslateStreamAsync("B", EnFa, "m", default));   // B -> still cached (hit)
        await Drain(cache.TranslateStreamAsync("C", EnFa, "m", default));   // C -> still cached (hit)
        Assert.Equal(3, inner.CallCount);                                   // no new calls from the hits

        await Drain(cache.TranslateStreamAsync("A", EnFa, "m", default));   // A -> miss again (was evicted)
        Assert.Equal(4, inner.CallCount);
    }

    [Fact]
    public async Task Lru_access_refreshes_recency()
    {
        var inner = new FakeInner(["v"]);
        var cache = Cache(inner, Clock(), maxEntries: 2);

        await Drain(cache.TranslateStreamAsync("A", EnFa, "m", default));
        await Drain(cache.TranslateStreamAsync("B", EnFa, "m", default));
        await Drain(cache.TranslateStreamAsync("A", EnFa, "m", default));   // HIT — refreshes A's recency
        await Drain(cache.TranslateStreamAsync("C", EnFa, "m", default));   // evicts B (now the oldest)

        await Drain(cache.TranslateStreamAsync("A", EnFa, "m", default));   // A -> still cached
        await Drain(cache.TranslateStreamAsync("B", EnFa, "m", default));   // B -> evicted, miss

        Assert.Equal(4, inner.CallCount);   // A(1) B(1) C(1) + B-again(1); the two A-hits cost nothing
    }

    // ---- robustness --------------------------------------------------------------------------

    [Fact]
    public async Task Persian_rtl_value_roundtrips_untouched()
    {
        const string persian = "سلام، حال شما چطور است؟";
        var inner = new FakeInner([persian]);
        var cache = Cache(inner, Clock());

        await Drain(cache.TranslateStreamAsync("hello", EnFa, "m", default));
        var hit = await Drain(cache.TranslateStreamAsync("hello", EnFa, "m", default));

        Assert.Single(hit);
        Assert.Equal(persian, hit[0]);   // byte-for-byte, no normalization mangling
    }

    [Fact]
    public async Task Concurrent_hits_are_consistent()
    {
        var inner = new FakeInner(["shared-value"]);
        var cache = Cache(inner, Clock());
        await Drain(cache.TranslateStreamAsync("k", EnFa, "m", default));   // seed the entry

        var tasks = Enumerable.Range(0, 32)
            .Select(_ => Task.Run(async () => string.Concat(await Drain(cache.TranslateStreamAsync("k", EnFa, "m", default)))))
            .ToArray();

        var results = await Task.WhenAll(tasks);
        Assert.All(results, r => Assert.Equal("shared-value", r));
    }
}
