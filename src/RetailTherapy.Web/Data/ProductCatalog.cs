using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using RetailTherapy.Web.Models;

namespace RetailTherapy.Web.Data;

public sealed record CatalogSnapshot(IReadOnlyList<ProductDto> Items, string ETag);

/// <summary>
/// Reads go to memory. The store (Firestore) is only hit when the cache is empty or expired,
/// and only one request refreshes it at a time. Admin writes clear the cache.
/// </summary>
public sealed class ProductCatalog
{
    private const string Key = "catalog";
    private readonly IProductStore _store;
    private readonly IMemoryCache _cache;
    private readonly IOptions<SiteOptions> _site;
    private readonly TimeSpan _ttl;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public ProductCatalog(IProductStore store, IMemoryCache cache, IOptions<SiteOptions> site, IConfiguration config)
    {
        _store = store;
        _cache = cache;
        _site = site;
        _ttl = TimeSpan.FromMinutes(Math.Max(1, config.GetValue("Storage:CacheMinutes", 10)));
    }

    public async Task<CatalogSnapshot> GetSnapshotAsync(CancellationToken ct)
    {
        if (_cache.TryGetValue(Key, out CatalogSnapshot? hit) && hit is not null) return hit;

        await _gate.WaitAsync(ct);
        try
        {
            if (_cache.TryGetValue(Key, out hit) && hit is not null) return hit;

            var all = await _store.ListAsync(CancellationToken.None);
            var tag = _site.Value.AmazonTag;
            var active = all.Where(p => p.Active)
                .OrderBy(p => p.Order).ThenBy(p => p.Title, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var ranks = TagRanker.RankAll(active);   // tags need the whole catalog (rarer tags are worth more)
            var items = active.Select(p => ProductDto.From(p, tag, ranks[p.Id])).ToList();
            var snap = new CatalogSnapshot(items, ComputeETag(items));
            if (items.Count > 0) _cache.Set(Key, snap, _ttl); // never cache an empty result
            return snap;
        }
        finally { _gate.Release(); }
    }

    public async Task<IReadOnlyList<Product>> ListAllAsync(CancellationToken ct) =>
        (await _store.ListAsync(ct)).OrderBy(p => p.Order).ThenBy(p => p.Title, StringComparer.OrdinalIgnoreCase).ToList();

    public async Task UpsertAsync(Product product, CancellationToken ct)
    {
        await _store.UpsertAsync(product, ct);
        _cache.Remove(Key);
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken ct)
    {
        var removed = await _store.DeleteAsync(id, ct);
        _cache.Remove(Key);
        return removed;
    }

    private static string ComputeETag(List<ProductDto> items)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(items);
        return "\"" + Convert.ToHexString(SHA256.HashData(bytes))[..16] + "\"";
    }
}
