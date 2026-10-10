using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using RetailTherapy.Web.Models;

namespace RetailTherapy.Web.Data;

public sealed record ListSnapshot(IReadOnlyList<ListDto> Items, string ETag);

/// <summary>
/// The curated lists ("sessions") as the public site sees them. The list definitions are cached; the product
/// matching is redone on each request (a few hundred products, so it costs nothing) so it always follows the catalog.
/// </summary>
public sealed class ListCatalog
{
    private const string Key = "lists";
    private readonly IDocStore<CuratedList> _store;
    private readonly ProductCatalog _products;
    private readonly IMemoryCache _cache;
    private readonly TimeSpan _ttl;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public ListCatalog(IDocStore<CuratedList> store, ProductCatalog products, IMemoryCache cache, IConfiguration config)
    {
        _store = store;
        _products = products;
        _cache = cache;
        _ttl = TimeSpan.FromMinutes(Math.Max(1, config.GetValue("Storage:CacheMinutes", 10)));
    }

    public async Task<ListSnapshot> GetSnapshotAsync(CancellationToken ct)
    {
        var defs = await ActiveAsync(ct);
        var products = (await _products.GetSnapshotAsync(ct)).Items;
        var items = defs.Select(d => Resolve(d, products)).Where(x => x is not null).Select(x => x!).ToList();
        var etag = "\"" + Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(items)))[..16] + "\"";
        return new ListSnapshot(items, etag);
    }

    public async Task<IReadOnlyList<CuratedList>> ListAllAsync(CancellationToken ct) =>
        (await _store.ListAsync(ct)).OrderBy(l => l.Order).ThenBy(l => l.Title, StringComparer.OrdinalIgnoreCase).ToList();

    public async Task UpsertAsync(CuratedList list, CancellationToken ct)
    {
        await _store.UpsertAsync(list, ct);
        _cache.Remove(Key);
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken ct)
    {
        var removed = await _store.DeleteAsync(id, ct);
        _cache.Remove(Key);
        return removed;
    }

    private async Task<IReadOnlyList<CuratedList>> ActiveAsync(CancellationToken ct)
    {
        if (_cache.TryGetValue(Key, out IReadOnlyList<CuratedList>? hit) && hit is not null) return hit;
        await _gate.WaitAsync(ct);
        try
        {
            if (_cache.TryGetValue(Key, out hit) && hit is not null) return hit;
            var all = (await _store.ListAsync(CancellationToken.None))
                .Where(l => l.Active)
                .OrderBy(l => l.Order).ThenBy(l => l.Title, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (all.Count > 0) _cache.Set(Key, (IReadOnlyList<CuratedList>)all, _ttl);
            return all;
        }
        finally { _gate.Release(); }
    }

    /// <summary>Hand-placed products first (in the order given), then rule matches (best tag fit first). Empty lists are hidden.</summary>
    public static ListDto? Resolve(CuratedList l, IReadOnlyList<ProductDto> products)
    {
        var known = products.Select(p => p.Id).ToHashSet();
        var ids = new List<string>();
        foreach (var id in l.ProductIds ?? Array.Empty<string>())
            if (known.Contains(id) && !ids.Contains(id)) ids.Add(id);

        var tags = Tagger.Clean(l.Tags);
        var cats = (l.Categories ?? Array.Empty<string>()).Where(c => !string.IsNullOrWhiteSpace(c)).ToArray();
        if (tags.Length > 0 || cats.Length > 0)
        {
            // A product qualifies for the tag rule when one of the tags is in its top 3. Best fit first, then catalog order.
            var limit = l.Limit > 0 ? l.Limit : 12;
            var matches = products
                .Select((p, order) => (p, order, fit: tags.Length == 0 ? 0 : p.TopTagScore(tags)))
                .Where(x => !ids.Contains(x.p.Id) && x.fit >= 0 &&
                            (cats.Length == 0 || cats.Any(c => string.Equals(c, x.p.Category, StringComparison.OrdinalIgnoreCase))))
                .OrderByDescending(x => x.fit).ThenBy(x => x.order);
            foreach (var m in matches)
            {
                if (ids.Count >= limit) break;
                ids.Add(m.p.Id);
            }
        }
        return ids.Count == 0 ? null : new ListDto(l.Id, l.Title, l.Blurb, l.Emoji, l.Hue, ids.ToArray());
    }
}
