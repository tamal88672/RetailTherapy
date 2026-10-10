using System.Text.Json;
using Google.Cloud.Firestore;
using RetailTherapy.Web.Models;

namespace RetailTherapy.Web.Data;

/// <summary>Append-only log of visitor actions (saved, removed, clicked). Read back only for the admin report.</summary>
public interface IEventStore
{
    Task AppendAsync(IReadOnlyList<TrackEvent> events, CancellationToken ct);
    /// <summary>Events on or after the given day (yyyy-MM-dd), newest first, at most <paramref name="max"/>.</summary>
    Task<IReadOnlyList<TrackEvent>> ListSinceAsync(string sinceDay, int max, CancellationToken ct);
}

/// <summary>Local development: one JSON object per line in a file.</summary>
public sealed class JsonLinesEventStore : IEventStore
{
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonLinesEventStore(string path) => _path = path;

    public async Task AppendAsync(IReadOnlyList<TrackEvent> events, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var lines = events.Select(e => JsonSerializer.Serialize(e, JsonSerializerOptions.Web));
            await File.AppendAllLinesAsync(_path, lines, ct);
        }
        finally { _gate.Release(); }
    }

    public async Task<IReadOnlyList<TrackEvent>> ListSinceAsync(string sinceDay, int max, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (!File.Exists(_path)) return Array.Empty<TrackEvent>();
            var all = new List<TrackEvent>();
            foreach (var line in await File.ReadAllLinesAsync(_path, ct))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var e = JsonSerializer.Deserialize<TrackEvent>(line, JsonSerializerOptions.Web);
                if (e is not null && string.CompareOrdinal(e.Day, sinceDay) >= 0) all.Add(e);
            }
            return all.OrderByDescending(e => e.At, StringComparer.Ordinal).Take(max).ToList();
        }
        finally { _gate.Release(); }
    }
}

public sealed class FirestoreEventStore : IEventStore
{
    private readonly CollectionReference _col;

    public FirestoreEventStore(FirestoreDb db, string collection) => _col = db.Collection(collection);

    public async Task AppendAsync(IReadOnlyList<TrackEvent> events, CancellationToken ct)
    {
        var batch = _col.Database.StartBatch();
        foreach (var e in events) batch.Set(_col.Document(Guid.NewGuid().ToString("N")), e);
        await batch.CommitAsync(ct);
    }

    public async Task<IReadOnlyList<TrackEvent>> ListSinceAsync(string sinceDay, int max, CancellationToken ct)
    {
        var snap = await _col.WhereGreaterThanOrEqualTo("Day", sinceDay).OrderByDescending("Day").Limit(max).GetSnapshotAsync(ct);
        return snap.Documents.Select(d => d.ConvertTo<TrackEvent>()).ToList();
    }
}
