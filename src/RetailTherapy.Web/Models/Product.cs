using Google.Cloud.Firestore;

namespace RetailTherapy.Web.Models;

/// <summary>A product as stored (Firestore document or JSON file).</summary>
[FirestoreData]
public sealed class Product
{
    [FirestoreProperty] public string Id { get; set; } = "";
    [FirestoreProperty] public string Title { get; set; } = "";
    [FirestoreProperty] public string Category { get; set; } = "";
    /// <summary>Amazon ASIN. Either Asin or Url is required.</summary>
    [FirestoreProperty] public string? Asin { get; set; }
    /// <summary>Full Amazon URL (e.g. a SiteStripe link). Used instead of Asin when set.</summary>
    [FirestoreProperty] public string? Url { get; set; }
    [FirestoreProperty] public string Emoji { get; set; } = "🛍️";
    [FirestoreProperty] public int Hue { get; set; } = 200;
    [FirestoreProperty] public string? Image { get; set; }
    [FirestoreProperty] public string Blurb { get; set; } = "";
    [FirestoreProperty] public string[] Pros { get; set; } = Array.Empty<string>();
    [FirestoreProperty] public string? Badge { get; set; }
    [FirestoreProperty] public int Order { get; set; }
    [FirestoreProperty] public bool Active { get; set; } = true;
    /// <summary>Day the product was posted (yyyy-MM-dd). The newest day is shown in the "New" tab.</summary>
    [FirestoreProperty] public string? AddedOn { get; set; }
    /// <summary>Short lowercase labels shown under the product (e.g. "gift idea"). Empty = worked out from the text.</summary>
    [FirestoreProperty] public string[] Tags { get; set; } = Array.Empty<string>();
}

/// <summary>What the public API returns. The affiliate URL is built on the server.</summary>
public sealed record ProductDto(
    string Id, string Title, string Category, string Emoji, int Hue,
    string? Image, string Blurb, string[] Pros, string? Badge, string Url, string? AddedOn, string[] Tags)
{
    public static ProductDto From(Product p, string amazonTag) => new(
        p.Id, p.Title, p.Category, p.Emoji, p.Hue,
        NullIfEmpty(p.Image), p.Blurb, p.Pros ?? Array.Empty<string>(), NullIfEmpty(p.Badge),
        AffiliateLinks.Build(p, amazonTag), NullIfEmpty(p.AddedOn),
        // Products saved before tags existed get theirs worked out from the text, so every tile shows some.
        p.Tags is { Length: > 0 } ? p.Tags : RetailTherapy.Web.Data.Tagger.Derive(p));

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
}

public static class AffiliateLinks
{
    /// <summary>
    /// Direct Amazon link with your Associates tag (no redirects or cloaking).
    /// Plain amazon.com links always get YOUR tag (any other tag is replaced).
    /// Short links (amzn.to, a.co...) were checked when the product was added, so they are used as given.
    /// </summary>
    public static string Build(Product p, string amazonTag)
    {
        if (!string.IsNullOrWhiteSpace(p.Url))
        {
            var given = p.Url!.Trim();
            if (Uri.TryCreate(given, UriKind.Absolute, out var u) && RetailTherapy.Web.Data.LinkGuard.IsPlainAmazon(u))
                return RetailTherapy.Web.Data.LinkGuard.SetTag(u, amazonTag);
            return given;
        }

        var baseUrl = $"https://www.amazon.com/dp/{Uri.EscapeDataString(p.Asin ?? "")}";
        return $"{baseUrl}?tag={Uri.EscapeDataString(amazonTag)}";
    }
}
