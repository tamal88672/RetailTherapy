using Google.Cloud.Firestore;

namespace RetailTherapy.Web.Models;

/// <summary>
/// A saved-items list. A visitor's list (id "v-{visitorId}") is one flat list of product ids. A signed-in person's list
/// (id "u-{uid}", OwnerType "user") is the "Saved" master list plus their own named <see cref="Lists"/> and private
/// <see cref="Details"/> (notes, bought). The old <see cref="Visibility"/> / <see cref="ShareCode"/> fields are unused:
/// sharing is per named list, see <see cref="SavedList"/>.
/// </summary>
[FirestoreData]
public sealed class Wishlist : IHasId
{
    /// <summary>"v-{visitorId}" for anonymous lists. Later: any id (one user may own several lists).</summary>
    [FirestoreProperty] public string Id { get; set; } = "";
    /// <summary>"visitor" today; "user" once there are accounts.</summary>
    [FirestoreProperty] public string OwnerType { get; set; } = "visitor";
    [FirestoreProperty] public string OwnerId { get; set; } = "";
    [FirestoreProperty] public string Name { get; set; } = "My wishlist";
    /// <summary>"private" today. Reserved for "unlisted" (anyone with the link) and "public".</summary>
    [FirestoreProperty] public string Visibility { get; set; } = "private";
    /// <summary>Reserved: short code used in a share link.</summary>
    [FirestoreProperty] public string? ShareCode { get; set; }
    [FirestoreProperty] public string[] ProductIds { get; set; } = Array.Empty<string>();
    [FirestoreProperty] public string? CreatedOn { get; set; }
    [FirestoreProperty] public string? UpdatedOn { get; set; }
    /// <summary>For an anonymous visitor's list: the user id that took it over when they signed in (the list is kept for the reports).</summary>
    [FirestoreProperty] public string? ClaimedBy { get; set; }
    /// <summary>Account lists only: a private note and a "bought" tick for saved items. Only items that have one appear here.</summary>
    [FirestoreProperty] public List<SavedItem> Details { get; set; } = new();
    /// <summary>Account lists only: the person's own named lists. Each one is a subset of <see cref="ProductIds"/> (the "Saved" master list).</summary>
    [FirestoreProperty] public List<SavedList> Lists { get; set; } = new();
}

/// <summary>A note and a "bought" tick on one saved item. Private to the owner; never shown on shared lists.</summary>
[FirestoreData]
public sealed class SavedItem
{
    [FirestoreProperty] public string ProductId { get; set; } = "";
    [FirestoreProperty] public string? Note { get; set; }
    [FirestoreProperty] public bool Bought { get; set; }
}

/// <summary>
/// A named list inside a person's saved items. The id is random and doubles as the share code:
/// the share link is /u/{username}/{id}, which only works while Visibility is "unlisted" or "public".
/// </summary>
[FirestoreData]
public sealed class SavedList
{
    [FirestoreProperty] public string Id { get; set; } = "";
    [FirestoreProperty] public string Name { get; set; } = "";
    /// <summary>"private" (only the owner), "unlisted" (anyone with the link) or "public" (also shown on the owner's profile page).</summary>
    [FirestoreProperty] public string Visibility { get; set; } = "private";
    [FirestoreProperty] public string[] ProductIds { get; set; } = Array.Empty<string>();
    [FirestoreProperty] public string? CreatedOn { get; set; }
    [FirestoreProperty] public string? UpdatedOn { get; set; }
}

/// <summary>One thing a visitor did, kept for the reports: saved a product, removed it, or clicked through to Amazon.</summary>
[FirestoreData]
public sealed class TrackEvent
{
    /// <summary>wish_add, wish_remove or click.</summary>
    [FirestoreProperty] public string Type { get; set; } = "";
    [FirestoreProperty] public string ProductId { get; set; } = "";
    /// <summary>Random anonymous id from the visitor's browser. Not linked to a name or email.</summary>
    [FirestoreProperty] public string VisitorId { get; set; } = "";
    /// <summary>Where it happened: "home", "saved" or "sessions:{list id}".</summary>
    [FirestoreProperty] public string Source { get; set; } = "home";
    /// <summary>yyyy-MM-dd (UTC). Used to pick a date range.</summary>
    [FirestoreProperty] public string Day { get; set; } = "";
    /// <summary>Full UTC time, ISO 8601.</summary>
    [FirestoreProperty] public string At { get; set; } = "";
}

public sealed record TrackItem(string Type, string ProductId, string? Source);
public sealed record TrackBatch(string VisitorId, List<TrackItem>? Events);
public sealed record WishlistPut(string VisitorId, string[]? ProductIds);
