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
}

/// <summary>What the public API returns. The affiliate URL is built on the server.</summary>
public sealed record ProductDto(
    string Id, string Title, string Category, string Emoji, int Hue,
    string? Image, string Blurb, string[] Pros, string? Badge, string Url)
{
    public static ProductDto From(Product p, string amazonTag) => new(
        p.Id, p.Title, p.Category, p.Emoji, p.Hue,
        NullIfEmpty(p.Image), p.Blurb, p.Pros ?? Array.Empty<string>(), NullIfEmpty(p.Badge),
        AffiliateLinks.Build(p, amazonTag));

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
}

public static class AffiliateLinks
{
    /// <summary>Direct Amazon link with your Associates tag (no redirects or cloaking).</summary>
    public static string Build(Product p, string amazonTag)
    {
        if (!string.IsNullOrWhiteSpace(p.Url))
        {
            // Short links from SiteStripe (amzn.to, a.co) and links that already carry a tag are used as given.
            var given = p.Url!.Trim();
            if (given.Contains("tag=", StringComparison.OrdinalIgnoreCase) ||
                Uri.TryCreate(given, UriKind.Absolute, out var u) &&
                (u.Host.Equals("amzn.to", StringComparison.OrdinalIgnoreCase) ||
                 u.Host.Equals("a.co", StringComparison.OrdinalIgnoreCase)))
                return given;
        }

        var baseUrl = !string.IsNullOrWhiteSpace(p.Url)
            ? p.Url!
            : $"https://www.amazon.com/dp/{Uri.EscapeDataString(p.Asin ?? "")}";
        var sep = baseUrl.Contains('?') ? '&' : '?';
        return $"{baseUrl}{sep}tag={Uri.EscapeDataString(amazonTag)}";
    }
}
