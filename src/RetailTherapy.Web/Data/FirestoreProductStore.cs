using Google.Api.Gax;
using Google.Cloud.Firestore;
using RetailTherapy.Web.Models;

namespace RetailTherapy.Web.Data;

/// <summary>Production store: one Firestore document per product. Auth uses the Cloud Run service account.</summary>
public sealed class FirestoreProductStore : IProductStore
{
    private readonly CollectionReference _col;

    public FirestoreProductStore(FirestoreDb db, string collection) => _col = db.Collection(collection);

    public async Task<IReadOnlyList<Product>> ListAsync(CancellationToken ct)
    {
        var snap = await _col.GetSnapshotAsync(ct);
        return snap.Documents.Select(d => d.ConvertTo<Product>()).ToList();
    }

    public Task UpsertAsync(Product product, CancellationToken ct) =>
        _col.Document(product.Id).SetAsync(product, cancellationToken: ct);

    public async Task<bool> DeleteAsync(string id, CancellationToken ct)
    {
        var doc = _col.Document(id);
        var snap = await doc.GetSnapshotAsync(ct);
        if (!snap.Exists) return false;
        await doc.DeleteAsync(cancellationToken: ct);
        return true;
    }

    public async Task SeedIfEmptyAsync(IReadOnlyList<Product> seed, CancellationToken ct)
    {
        var first = await _col.Limit(1).GetSnapshotAsync(ct);
        if (first.Count > 0) return;
        var batch = _col.Database.StartBatch();
        foreach (var p in seed) batch.Set(_col.Document(p.Id), p);
        await batch.CommitAsync(ct);
    }
}

public static class FirestoreRegistration
{
    public static IServiceCollection AddFirestoreProductStore(this IServiceCollection services, IConfiguration config)
    {
        // Project id: explicit setting, else auto-detected from the Cloud Run metadata server.
        var projectId = config["Storage:ProjectId"] ?? Platform.Instance().ProjectId
            ?? throw new InvalidOperationException("Set Storage:ProjectId (env Storage__ProjectId) for Firestore.");
        var collection = config["Storage:Collection"] ?? "products";
        services.AddSingleton(_ => FirestoreDb.Create(projectId));
        services.AddSingleton<IProductStore>(sp => new FirestoreProductStore(sp.GetRequiredService<FirestoreDb>(), collection));
        return services;
    }
}
