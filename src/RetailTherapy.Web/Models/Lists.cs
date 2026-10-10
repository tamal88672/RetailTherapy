using Google.Cloud.Firestore;

namespace RetailTherapy.Web.Models;

/// <summary>Anything stored as one document with a string id (Firestore document or entry in a JSON file).</summary>
public interface IHasId
{
    string Id { get; set; }
}

/// <summary>
/// A curated list ("session") on the lists page: a title, a short blurb and a set of products.
/// Products come from two places, pinned first: <see cref="ProductIds"/> in the order given, then every product that
/// matches the rule (<see cref="Categories"/> and/or <see cref="Tags"/>), most popular first, until <see cref="Limit"/>.
/// </summary>
[FirestoreData]
public sealed class CuratedList : IHasId
{
    [FirestoreProperty] public string Id { get; set; } = "";
    [FirestoreProperty] public string Title { get; set; } = "";
    [FirestoreProperty] public string Blurb { get; set; } = "";
    [FirestoreProperty] public string Emoji { get; set; } = "✨";
    [FirestoreProperty] public int Hue { get; set; } = 320;
    [FirestoreProperty] public int Order { get; set; }
    [FirestoreProperty] public bool Active { get; set; } = true;
    /// <summary>Products placed by hand, in this order.</summary>
    [FirestoreProperty] public string[] ProductIds { get; set; } = Array.Empty<string>();
    /// <summary>Rule: a product qualifies if it has ANY of these tags (empty = no tag condition).</summary>
    [FirestoreProperty] public string[] Tags { get; set; } = Array.Empty<string>();
    /// <summary>Rule: a product qualifies if its category is ANY of these (empty = no category condition).</summary>
    [FirestoreProperty] public string[] Categories { get; set; } = Array.Empty<string>();
    /// <summary>Most products to show from the rule (hand-placed ones are always shown).</summary>
    [FirestoreProperty] public int Limit { get; set; } = 12;
}

/// <summary>What the public lists API returns. Products are sent as ids; the page joins them with /api/products.</summary>
public sealed record ListDto(string Id, string Title, string Blurb, string Emoji, int Hue, string[] ProductIds);
