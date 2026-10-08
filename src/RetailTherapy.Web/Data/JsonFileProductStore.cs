using System.Text.Json;
using RetailTherapy.Web.Models;

namespace RetailTherapy.Web.Data;

/// <summary>Local-development store: one JSON file. Not for Cloud Run (its disk is temporary).</summary>
public sealed class JsonFileProductStore : IProductStore
{
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonFileProductStore(string path) => _path = path;

    public async Task<IReadOnlyList<Product>> ListAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try { return await ReadAsync(ct); }
        finally { _gate.Release(); }
    }

    public async Task UpsertAsync(Product product, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var all = await ReadAsync(ct);
            var i = all.FindIndex(p => p.Id == product.Id);
            if (i >= 0) all[i] = product; else all.Add(product);
            await WriteAsync(all, ct);
        }
        finally { _gate.Release(); }
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var all = await ReadAsync(ct);
            var removed = all.RemoveAll(p => p.Id == id) > 0;
            if (removed) await WriteAsync(all, ct);
            return removed;
        }
        finally { _gate.Release(); }
    }

    public async Task SeedIfEmptyAsync(IReadOnlyList<Product> seed, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if ((await ReadAsync(ct)).Count == 0) await WriteAsync(seed.ToList(), ct);
        }
        finally { _gate.Release(); }
    }

    private async Task<List<Product>> ReadAsync(CancellationToken ct)
    {
        if (!File.Exists(_path)) return new List<Product>();
        await using var s = File.OpenRead(_path);
        return await JsonSerializer.DeserializeAsync<List<Product>>(s, JsonDefaults.Web, ct) ?? new List<Product>();
    }

    private async Task WriteAsync(List<Product> list, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        await using var s = File.Create(_path);
        await JsonSerializer.SerializeAsync(s, list, JsonDefaults.Web, ct);
    }
}

public static class JsonDefaults
{
    public static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web) { WriteIndented = true };
}
