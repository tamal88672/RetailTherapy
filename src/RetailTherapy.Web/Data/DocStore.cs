using System.Text.Json;
using Google.Cloud.Firestore;
using RetailTherapy.Web.Models;

namespace RetailTherapy.Web.Data;

/// <summary>A small keyed store for lists and wishlists. Firestore in the cloud, one JSON file for local development.</summary>
public interface IDocStore<T> where T : class, IHasId, new()
{
    Task<IReadOnlyList<T>> ListAsync(CancellationToken ct);
    Task<T?> GetAsync(string id, CancellationToken ct);
    Task UpsertAsync(T item, CancellationToken ct);
    Task<bool> DeleteAsync(string id, CancellationToken ct);
    Task SeedIfEmptyAsync(IReadOnlyList<T> seed, CancellationToken ct);
}

public sealed class JsonDocStore<T> : IDocStore<T> where T : class, IHasId, new()
{
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonDocStore(string path) => _path = path;

    public async Task<IReadOnlyList<T>> ListAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try { return await ReadAsync(ct); }
        finally { _gate.Release(); }
    }

    public async Task<T?> GetAsync(string id, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try { return (await ReadAsync(ct)).FirstOrDefault(x => x.Id == id); }
        finally { _gate.Release(); }
    }

    public async Task UpsertAsync(T item, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var all = await ReadAsync(ct);
            var i = all.FindIndex(x => x.Id == item.Id);
            if (i >= 0) all[i] = item; else all.Add(item);
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
            var removed = all.RemoveAll(x => x.Id == id) > 0;
            if (removed) await WriteAsync(all, ct);
            return removed;
        }
        finally { _gate.Release(); }
    }

    public async Task SeedIfEmptyAsync(IReadOnlyList<T> seed, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if ((await ReadAsync(ct)).Count == 0) await WriteAsync(seed.ToList(), ct);
        }
        finally { _gate.Release(); }
    }

    private async Task<List<T>> ReadAsync(CancellationToken ct)
    {
        if (!File.Exists(_path)) return new List<T>();
        await using var s = File.OpenRead(_path);
        return await JsonSerializer.DeserializeAsync<List<T>>(s, JsonDefaults.Web, ct) ?? new List<T>();
    }

    private async Task WriteAsync(List<T> list, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        await using var s = File.Create(_path);
        await JsonSerializer.SerializeAsync(s, list, JsonDefaults.Web, ct);
    }
}

public sealed class FirestoreDocStore<T> : IDocStore<T> where T : class, IHasId, new()
{
    private readonly CollectionReference _col;

    public FirestoreDocStore(FirestoreDb db, string collection) => _col = db.Collection(collection);

    public async Task<IReadOnlyList<T>> ListAsync(CancellationToken ct)
    {
        var snap = await _col.GetSnapshotAsync(ct);
        return snap.Documents.Select(d => d.ConvertTo<T>()).ToList();
    }

    public async Task<T?> GetAsync(string id, CancellationToken ct)
    {
        var snap = await _col.Document(id).GetSnapshotAsync(ct);
        return snap.Exists ? snap.ConvertTo<T>() : null;
    }

    public Task UpsertAsync(T item, CancellationToken ct) =>
        _col.Document(item.Id).SetAsync(item, cancellationToken: ct);

    public async Task<bool> DeleteAsync(string id, CancellationToken ct)
    {
        var doc = _col.Document(id);
        var snap = await doc.GetSnapshotAsync(ct);
        if (!snap.Exists) return false;
        await doc.DeleteAsync(cancellationToken: ct);
        return true;
    }

    public async Task SeedIfEmptyAsync(IReadOnlyList<T> seed, CancellationToken ct)
    {
        var first = await _col.Limit(1).GetSnapshotAsync(ct);
        if (first.Count > 0) return;
        var batch = _col.Database.StartBatch();
        foreach (var x in seed) batch.Set(_col.Document(x.Id), x);
        await batch.CommitAsync(ct);
    }
}
