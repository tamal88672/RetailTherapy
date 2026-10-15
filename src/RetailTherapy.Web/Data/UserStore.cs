using System.Text.Json;
using Google.Cloud.Firestore;
using RetailTherapy.Web.Models;

namespace RetailTherapy.Web.Data;

/// <summary>Accounts and their usernames. A username is claimed once, atomically, and never changes.</summary>
public interface IUserStore
{
    Task<UserProfile?> GetAsync(string uid, CancellationToken ct);
    /// <summary>The person's profile, created the first time they are seen. Updates "last seen" at most once a day.</summary>
    Task<UserProfile> EnsureAsync(string uid, string provider, CancellationToken ct);
    Task<bool> UsernameTakenAsync(string lowerName, CancellationToken ct);
    Task<ClaimResult> ClaimUsernameAsync(string uid, string lowerName, CancellationToken ct);
    /// <summary>Removes the profile. The username stays reserved so nobody can pose as the old account.</summary>
    Task DeleteAsync(string uid, CancellationToken ct);
    /// <summary>Everyone (up to <paramref name="limit"/>), newest first. For the admin page.</summary>
    Task<IReadOnlyList<UserProfile>> ListAsync(int limit, CancellationToken ct);
    /// <summary>"member" or "admin". False when the account does not exist.</summary>
    Task<bool> SetRoleAsync(string uid, string role, CancellationToken ct);
    /// <summary>"active" or "suspended". False when the account does not exist.</summary>
    Task<bool> SetStatusAsync(string uid, string status, CancellationToken ct);
    /// <summary>The owner of a username, or null when the name is free or its account was deleted.</summary>
    Task<string?> UidForUsernameAsync(string lowerName, CancellationToken ct);
}

public sealed class JsonUserStore : IUserStore
{
    private sealed class Data
    {
        public List<UserProfile> Users { get; set; } = new();
        public List<UsernameClaim> Names { get; set; } = new();
    }

    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonUserStore(string path) => _path = path;

    public async Task<UserProfile?> GetAsync(string uid, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try { return (await ReadAsync(ct)).Users.FirstOrDefault(u => u.Id == uid); }
        finally { _gate.Release(); }
    }

    public async Task<UserProfile> EnsureAsync(string uid, string provider, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var d = await ReadAsync(ct);
            var u = d.Users.FirstOrDefault(x => x.Id == uid);
            var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
            if (u is null)
            {
                u = new UserProfile { Id = uid, Provider = provider, CreatedOn = today, LastSeenOn = today };
                d.Users.Add(u);
                await WriteAsync(d, ct);
            }
            else if (u.LastSeenOn != today)
            {
                u.LastSeenOn = today;
                await WriteAsync(d, ct);
            }
            return u;
        }
        finally { _gate.Release(); }
    }

    public async Task<bool> UsernameTakenAsync(string lowerName, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try { return (await ReadAsync(ct)).Names.Any(n => n.Id == lowerName); }
        finally { _gate.Release(); }
    }

    public async Task<ClaimResult> ClaimUsernameAsync(string uid, string lowerName, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var d = await ReadAsync(ct);
            var u = d.Users.FirstOrDefault(x => x.Id == uid);
            if (u is null) return ClaimResult.NoUser;
            if (!string.IsNullOrEmpty(u.Username)) return ClaimResult.AlreadyHasUsername;
            if (d.Names.Any(n => n.Id == lowerName)) return ClaimResult.Taken;
            d.Names.Add(new UsernameClaim { Id = lowerName, Uid = uid, CreatedOn = DateTime.UtcNow.ToString("o") });
            u.Username = lowerName;
            await WriteAsync(d, ct);
            return ClaimResult.Ok;
        }
        finally { _gate.Release(); }
    }

    public async Task DeleteAsync(string uid, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var d = await ReadAsync(ct);
            var u = d.Users.FirstOrDefault(x => x.Id == uid);
            if (u is null) return;
            if (!string.IsNullOrEmpty(u.Username))
            {
                var claim = d.Names.FirstOrDefault(n => n.Id == u.Username);
                if (claim is not null) claim.Uid = "deleted";
            }
            d.Users.Remove(u);
            await WriteAsync(d, ct);
        }
        finally { _gate.Release(); }
    }

    public async Task<IReadOnlyList<UserProfile>> ListAsync(int limit, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try { return Newest((await ReadAsync(ct)).Users, limit); }
        finally { _gate.Release(); }
    }

    public async Task<bool> SetRoleAsync(string uid, string role, CancellationToken ct) =>
        await ChangeAsync(uid, u => u.Role = role, ct);

    public async Task<bool> SetStatusAsync(string uid, string status, CancellationToken ct) =>
        await ChangeAsync(uid, u => u.Status = status, ct);

    public async Task<string?> UidForUsernameAsync(string lowerName, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var uid = (await ReadAsync(ct)).Names.FirstOrDefault(n => n.Id == lowerName)?.Uid;
            return string.IsNullOrEmpty(uid) || uid == "deleted" ? null : uid;
        }
        finally { _gate.Release(); }
    }

    /// <summary>Newest sign-ups first (ties broken by id so the order is stable).</summary>
    internal static IReadOnlyList<UserProfile> Newest(IEnumerable<UserProfile> users, int limit) =>
        users.OrderByDescending(u => u.CreatedOn ?? "").ThenBy(u => u.Id, StringComparer.Ordinal).Take(Math.Max(1, limit)).ToList();

    private async Task<bool> ChangeAsync(string uid, Action<UserProfile> change, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var d = await ReadAsync(ct);
            var u = d.Users.FirstOrDefault(x => x.Id == uid);
            if (u is null) return false;
            change(u);
            await WriteAsync(d, ct);
            return true;
        }
        finally { _gate.Release(); }
    }

    private async Task<Data> ReadAsync(CancellationToken ct)
    {
        if (!File.Exists(_path)) return new Data();
        await using var s = File.OpenRead(_path);
        return await JsonSerializer.DeserializeAsync<Data>(s, JsonDefaults.Web, ct) ?? new Data();
    }

    private async Task WriteAsync(Data d, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        await using var s = File.Create(_path);
        await JsonSerializer.SerializeAsync(s, d, JsonDefaults.Web, ct);
    }
}

public sealed class FirestoreUserStore : IUserStore
{
    private readonly FirestoreDb _db;
    private readonly CollectionReference _users;
    private readonly CollectionReference _names;

    public FirestoreUserStore(FirestoreDb db, string usersCollection, string namesCollection)
    {
        _db = db;
        _users = db.Collection(usersCollection);
        _names = db.Collection(namesCollection);
    }

    public async Task<UserProfile?> GetAsync(string uid, CancellationToken ct)
    {
        var snap = await _users.Document(uid).GetSnapshotAsync(ct);
        return snap.Exists ? snap.ConvertTo<UserProfile>() : null;
    }

    public async Task<UserProfile> EnsureAsync(string uid, string provider, CancellationToken ct)
    {
        var doc = _users.Document(uid);
        var snap = await doc.GetSnapshotAsync(ct);
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        if (!snap.Exists)
        {
            var created = new UserProfile { Id = uid, Provider = provider, CreatedOn = today, LastSeenOn = today };
            await doc.SetAsync(created, cancellationToken: ct);
            return created;
        }
        var u = snap.ConvertTo<UserProfile>();
        if (u.LastSeenOn != today)
        {
            u.LastSeenOn = today;
            await doc.SetAsync(u, cancellationToken: ct);
        }
        return u;
    }

    public async Task<bool> UsernameTakenAsync(string lowerName, CancellationToken ct) =>
        (await _names.Document(lowerName).GetSnapshotAsync(ct)).Exists;

    // One transaction covers the person's profile and the name document, so a name is never given to two people
    // and one person can never end up with two names, even if requests arrive at the same moment.
    public Task<ClaimResult> ClaimUsernameAsync(string uid, string lowerName, CancellationToken ct) =>
        _db.RunTransactionAsync(async tx =>
        {
            var userRef = _users.Document(uid);
            var nameRef = _names.Document(lowerName);
            var userSnap = await tx.GetSnapshotAsync(userRef, ct);
            var nameSnap = await tx.GetSnapshotAsync(nameRef, ct);
            if (!userSnap.Exists) return ClaimResult.NoUser;
            var u = userSnap.ConvertTo<UserProfile>();
            if (!string.IsNullOrEmpty(u.Username)) return ClaimResult.AlreadyHasUsername;
            if (nameSnap.Exists) return ClaimResult.Taken;
            u.Username = lowerName;
            tx.Create(nameRef, new UsernameClaim { Id = lowerName, Uid = uid, CreatedOn = DateTime.UtcNow.ToString("o") });
            tx.Set(userRef, u);
            return ClaimResult.Ok;
        }, cancellationToken: ct);

    public async Task DeleteAsync(string uid, CancellationToken ct)
    {
        await _db.RunTransactionAsync(async tx =>
        {
            var userRef = _users.Document(uid);
            var userSnap = await tx.GetSnapshotAsync(userRef, ct);
            if (!userSnap.Exists) return false;
            var u = userSnap.ConvertTo<UserProfile>();
            if (!string.IsNullOrEmpty(u.Username))
                tx.Set(_names.Document(u.Username), new UsernameClaim { Id = u.Username, Uid = "deleted", CreatedOn = DateTime.UtcNow.ToString("o") });
            tx.Delete(userRef);
            return true;
        }, cancellationToken: ct);
    }

    public async Task<IReadOnlyList<UserProfile>> ListAsync(int limit, CancellationToken ct)
    {
        var snap = await _users.OrderByDescending("CreatedOn").Limit(Math.Max(1, limit)).GetSnapshotAsync(ct);
        return JsonUserStore.Newest(snap.Documents.Select(d => d.ConvertTo<UserProfile>()), limit);
    }

    public Task<bool> SetRoleAsync(string uid, string role, CancellationToken ct) => ChangeAsync(uid, u => u.Role = role, ct);

    public Task<bool> SetStatusAsync(string uid, string status, CancellationToken ct) => ChangeAsync(uid, u => u.Status = status, ct);

    public async Task<string?> UidForUsernameAsync(string lowerName, CancellationToken ct)
    {
        var snap = await _names.Document(lowerName).GetSnapshotAsync(ct);
        if (!snap.Exists) return null;
        var uid = snap.ConvertTo<UsernameClaim>().Uid;
        return string.IsNullOrEmpty(uid) || uid == "deleted" ? null : uid;
    }

    // Read-change-write inside one transaction, so a role change cannot overwrite a sign-in that happened at the same moment.
    private Task<bool> ChangeAsync(string uid, Action<UserProfile> change, CancellationToken ct) =>
        _db.RunTransactionAsync(async tx =>
        {
            var userRef = _users.Document(uid);
            var snap = await tx.GetSnapshotAsync(userRef, ct);
            if (!snap.Exists) return false;
            var u = snap.ConvertTo<UserProfile>();
            change(u);
            tx.Set(userRef, u);
            return true;
        }, cancellationToken: ct);
}
