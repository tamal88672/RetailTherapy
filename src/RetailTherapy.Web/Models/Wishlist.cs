using Google.Cloud.Firestore;

namespace RetailTherapy.Web.Models;

/// <summary>
/// A saved-items list. Today every list belongs to an anonymous visitor (a random id kept in the browser).
/// When accounts arrive, a visitor's list is claimed by a user (OwnerType "user", OwnerId = the user id), and
/// <see cref="Visibility"/> / <see cref="ShareCode"/> are where sharing lists between users plugs in.
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
