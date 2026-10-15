using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using RetailTherapy.Web.Models;

namespace RetailTherapy.Web.Data;

/// <summary>Outcome of one change to a person's saved items: an HTTP status (200 = done), a short message for the person, and the document.</summary>
public sealed record EditResult(int Status, string? Error, Wishlist Doc)
{
    public bool Ok => Status == 200;
}

/// <summary>
/// Reads and changes one signed-in person's saved items ("Saved" + named lists + notes). Changes to the same person are
/// done one at a time (a lock per person), so two quick taps cannot overwrite each other inside one running instance.
/// </summary>
public sealed class SavedService
{
    private readonly IDocStore<Wishlist> _store;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    public SavedService(IDocStore<Wishlist> store) => _store = store;

    public static string DocId(string uid) => "u-" + uid;

    public async Task<Wishlist> GetAsync(string uid, CancellationToken ct) =>
        await _store.GetAsync(DocId(uid), ct) ?? Fresh(uid, DateTime.UtcNow.ToString("o"));

    public Task<bool> DeleteAsync(string uid, CancellationToken ct) => _store.DeleteAsync(DocId(uid), ct);

    /// <summary>Loads the document, runs <paramref name="edit"/> on it, tidies it and saves it, unless the edit refuses (status other than 200).</summary>
    public async Task<EditResult> EditAsync(string uid, HashSet<string> known, Func<Wishlist, (int Status, string? Error)> edit, CancellationToken ct)
    {
        var gate = _locks.GetOrAdd(uid, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            var now = DateTime.UtcNow.ToString("o");
            var w = await _store.GetAsync(DocId(uid), ct) ?? Fresh(uid, now);
            var (status, error) = edit(w);
            if (status != 200) return new EditResult(status, error, w);
            SavedOps.Tidy(w, known);
            w.UpdatedOn = now;
            await _store.UpsertAsync(w, ct);
            return new EditResult(200, null, w);
        }
        finally { gate.Release(); }
    }

    private static Wishlist Fresh(string uid, string now) =>
        new() { Id = DocId(uid), OwnerType = "user", OwnerId = uid, CreatedOn = now };
}

/// <summary>The rules for saved items, lists and notes. Pure functions on a document, so they are easy to test.</summary>
public static class SavedOps
{
    public const int MaxItems = 200, MaxLists = 10, MaxName = 40, MaxNote = 280;
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase) { "saved", "all saved", "all", "everything" };

    /// <summary>Random 12-character id. It is also the share code, so it must not be guessable.</summary>
    public static string NewId() => Convert.ToHexString(RandomNumberGenerator.GetBytes(6)).ToLowerInvariant();

    /// <summary>Collapses spaces and drops control characters. False when the result is longer than <paramref name="max"/>.</summary>
    public static bool TryClean(string? text, int max, out string clean)
    {
        var sb = new StringBuilder();
        var gap = false;
        foreach (var ch in text ?? "")
        {
            if (char.IsControl(ch) || char.IsWhiteSpace(ch)) { gap = sb.Length > 0; continue; }
            if (gap) { sb.Append(' '); gap = false; }
            sb.Append(ch);
        }
        clean = sb.ToString();
        return clean.Length <= max;
    }

    /// <summary>Makes the document consistent: known products only, no duplicates, lists and notes only for saved items, size caps.</summary>
    public static void Tidy(Wishlist w, HashSet<string> known)
    {
        w.ProductIds = w.ProductIds.Where(known.Contains).Distinct().Take(MaxItems).ToArray();
        var saved = w.ProductIds.ToHashSet();
        w.Details = w.Details.Where(d => saved.Contains(d.ProductId) && (!string.IsNullOrEmpty(d.Note) || d.Bought))
            .GroupBy(d => d.ProductId).Select(g => g.First()).ToList();
        foreach (var l in w.Lists) l.ProductIds = l.ProductIds.Where(saved.Contains).Distinct().Take(MaxItems).ToArray();
        w.Lists = w.Lists.Take(MaxLists).ToList();
    }

    /// <summary>Replaces the whole "Saved" list (this is what the heart buttons sync). Notes and lists follow what stays.</summary>
    public static (int, string?) SetSaved(Wishlist w, IEnumerable<string> ids)
    {
        w.ProductIds = ids.ToArray();
        return (200, null);
    }

    /// <summary>Adds browser-saved items after the account's own, keeping the newest when the cap is hit.</summary>
    public static (int, string?) MergeSaved(Wishlist w, IEnumerable<string> local, HashSet<string> known)
    {
        var merged = w.ProductIds.Where(known.Contains).Concat(local).Distinct().ToArray();
        if (merged.Length > MaxItems) merged = merged[^MaxItems..];
        w.ProductIds = merged;
        return (200, null);
    }

    public static (int, string?) PatchItem(Wishlist w, string productId, ItemPatch? patch)
    {
        if (patch is null || (patch.Note is null && patch.Bought is null)) return (400, "Nothing to change.");
        if (!w.ProductIds.Contains(productId)) return (404, "Save this find first.");
        var d = w.Details.FirstOrDefault(x => x.ProductId == productId);
        var note = d?.Note ?? "";
        var bought = d?.Bought ?? false;
        if (patch.Note is not null && !TryClean(patch.Note, MaxNote, out note)) return (400, $"Notes can be up to {MaxNote} characters.");
        if (patch.Bought is not null) bought = patch.Bought.Value;
        w.Details.RemoveAll(x => x.ProductId == productId);
        if (note.Length > 0 || bought) w.Details.Add(new SavedItem { ProductId = productId, Note = note.Length > 0 ? note : null, Bought = bought });
        return (200, null);
    }

    private static (int, string?) CheckName(Wishlist w, string? name, string? exceptId, string extraBlocked, out string clean)
    {
        if (!TryClean(name, MaxName, out clean)) return (400, $"Use up to {MaxName} characters.");
        if (clean.Length == 0) return (400, "Give the list a name.");
        if (Usernames.ContainsBlocked(clean, extraBlocked)) return (400, "Please choose another name.");
        var taken = clean;
        if (ReservedNames.Contains(taken) ||
            w.Lists.Any(l => l.Id != exceptId && string.Equals(l.Name, taken, StringComparison.OrdinalIgnoreCase)))
            return (409, "You already have a list with that name.");
        return (200, null);
    }

    /// <summary>A new private list, optionally holding one find straight away (it is saved too if it was not).</summary>
    public static (int, string?, SavedList?) CreateList(Wishlist w, string? name, string? productId, HashSet<string> known, string extraBlocked, string now)
    {
        if (w.Lists.Count >= MaxLists) return (409, $"You can have up to {MaxLists} lists.", null);
        var (s, e) = CheckName(w, name, null, extraBlocked, out var clean);
        if (s != 200) return (s, e, null);
        var list = new SavedList { Id = NewId(), Name = clean, Visibility = "private", CreatedOn = now, UpdatedOn = now };
        w.Lists.Add(list);
        if (!string.IsNullOrEmpty(productId))
        {
            var (s2, e2) = AddToList(w, list.Id, productId, known, now);
            if (s2 != 200) return (s2, e2, null);
        }
        return (200, null, list);
    }

    /// <summary>Renames a list and/or changes who can see it. Sharing needs a username and an account in good standing.</summary>
    public static (int, string?) PatchList(Wishlist w, string listId, ListPatch? patch, bool canShare, string cannotShareReason, string extraBlocked, string now)
    {
        var l = w.Lists.FirstOrDefault(x => x.Id == listId);
        if (l is null) return (404, "List not found.");
        if (patch is null || (patch.Name is null && patch.Visibility is null)) return (400, "Nothing to change.");
        var name = l.Name;
        if (patch.Name is not null)
        {
            var (s, e) = CheckName(w, patch.Name, l.Id, extraBlocked, out name);
            if (s != 200) return (s, e);
        }
        var visibility = l.Visibility;
        if (patch.Visibility is not null)
        {
            visibility = patch.Visibility.Trim().ToLowerInvariant();
            if (visibility is not ("private" or "unlisted" or "public")) return (400, "Choose private, link or public.");
            if (visibility != "private" && !canShare) return (409, cannotShareReason);
        }
        l.Name = name;
        l.Visibility = visibility;
        l.UpdatedOn = now;
        return (200, null);
    }

    public static (int, string?) DeleteList(Wishlist w, string listId) =>
        w.Lists.RemoveAll(x => x.Id == listId) > 0 ? (200, null) : (404, "List not found.");

    /// <summary>Puts a find in a list. If it was not saved yet it is saved too.</summary>
    public static (int, string?) AddToList(Wishlist w, string listId, string productId, HashSet<string> known, string now)
    {
        var l = w.Lists.FirstOrDefault(x => x.Id == listId);
        if (l is null) return (404, "List not found.");
        if (!known.Contains(productId)) return (404, "Unknown find.");
        if (!w.ProductIds.Contains(productId))
        {
            if (w.ProductIds.Length >= MaxItems) return (409, $"Your saved list is full ({MaxItems}).");
            w.ProductIds = new[] { productId }.Concat(w.ProductIds).ToArray();
        }
        if (!l.ProductIds.Contains(productId))
        {
            l.ProductIds = new[] { productId }.Concat(l.ProductIds).ToArray();
            l.UpdatedOn = now;
        }
        return (200, null);
    }

    public static (int, string?) RemoveFromList(Wishlist w, string listId, string productId, string now)
    {
        var l = w.Lists.FirstOrDefault(x => x.Id == listId);
        if (l is null) return (404, "List not found.");
        if (l.ProductIds.Contains(productId))
        {
            l.ProductIds = l.ProductIds.Where(x => x != productId).ToArray();
            l.UpdatedOn = now;
        }
        return (200, null);
    }

    /// <summary>Why this person cannot share lists right now, or null when they can.</summary>
    public static string? CannotShare(UserProfile? user) =>
        string.IsNullOrEmpty(user?.Username) ? "Pick a username first, so people can see who shared the list."
        : user.Status == "suspended" ? "Sharing is turned off for this account."
        : null;

    /// <summary>What the signed-in person's own page needs: everything, including notes and private lists.</summary>
    public static object Payload(Wishlist w, string? createdId = null) => new
    {
        productIds = w.ProductIds,
        details = w.Details.Select(d => new { productId = d.ProductId, note = d.Note ?? "", bought = d.Bought }),
        lists = w.Lists.Select(l => new { id = l.Id, name = l.Name, visibility = l.Visibility, productIds = l.ProductIds }),
        updatedOn = w.UpdatedOn,
        createdId,
    };

    public static bool IsShared(SavedList l) => l.Visibility is "unlisted" or "public";
}
