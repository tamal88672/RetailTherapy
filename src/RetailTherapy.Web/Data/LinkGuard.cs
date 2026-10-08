namespace RetailTherapy.Web.Data;

public enum LinkState
{
    /// <summary>Already carries your store ID.</summary>
    Ok,
    /// <summary>Was missing your store ID (or had someone else's); the link was rewritten with yours.</summary>
    Fixed,
    /// <summary>A short link that could not be opened to check; left as given.</summary>
    Unverified,
    /// <summary>Not an Amazon link at all.</summary>
    NotAmazon,
}

public sealed record LinkResult(string Url, LinkState State);

/// <summary>
/// Makes sure every Amazon link carries your Associates store ID (tag).
/// Plain amazon.com links get the tag set. Short links (amzn.to, a.co...) are opened once to read the tag
/// they carry; if it is not yours, the link is replaced by the full product link with your tag.
/// </summary>
public static class LinkGuard
{
    private static readonly string[] ShortHosts = { "amzn.to", "a.co", "amzn.com", "link.amazon.com" };
    private const string Agent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36";

    public static bool IsPlainAmazon(Uri u)
    {
        var h = u.Host.ToLowerInvariant();
        return (h == "amazon.com" || h.EndsWith(".amazon.com")) && h != "link.amazon.com";
    }

    private static bool IsShort(Uri u) => Array.IndexOf(ShortHosts, u.Host.ToLowerInvariant()) >= 0;

    public static string? GetTag(Uri u)
    {
        foreach (var pair in u.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var i = pair.IndexOf('=');
            if (i > 0 && pair[..i].Equals("tag", StringComparison.OrdinalIgnoreCase))
                return Uri.UnescapeDataString(pair[(i + 1)..]);
        }
        return null;
    }

    /// <summary>Returns the link with tag=yourTag (replacing any other tag).</summary>
    public static string SetTag(Uri u, string tag)
    {
        var pairs = u.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(p => !p.StartsWith("tag=", StringComparison.OrdinalIgnoreCase))
            .ToList();
        pairs.Add("tag=" + Uri.EscapeDataString(tag));
        var b = new UriBuilder(u) { Query = string.Join("&", pairs) };
        return b.Uri.ToString();
    }

    public static async Task<LinkResult> EnsureAsync(string url, string tag, HttpClient http, CancellationToken ct)
    {
        url = (url ?? "").Trim();
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u)) return new LinkResult(url, LinkState.NotAmazon);

        if (IsPlainAmazon(u))
        {
            var tagged = SetTag(u, tag);
            return new LinkResult(tagged, string.Equals(GetTag(u), tag, StringComparison.OrdinalIgnoreCase) ? LinkState.Ok : LinkState.Fixed);
        }

        if (!IsShort(u)) return new LinkResult(url, LinkState.NotAmazon);

        var final = await ResolveAsync(u, http, ct);
        if (final is null) return new LinkResult(url, LinkState.Unverified);
        if (string.Equals(GetTag(final), tag, StringComparison.OrdinalIgnoreCase)) return new LinkResult(url, LinkState.Ok);
        return new LinkResult(SetTag(final, tag), LinkState.Fixed);
    }

    /// <summary>Follows redirects one hop at a time until it reaches a plain amazon.com page (that page is not opened).</summary>
    private static async Task<Uri?> ResolveAsync(Uri start, HttpClient http, CancellationToken ct)
    {
        var cur = start;
        for (var hop = 0; hop < 6; hop++)
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(10));
                using var req = new HttpRequestMessage(HttpMethod.Get, cur);
                req.Headers.TryAddWithoutValidation("User-Agent", Agent);
                using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token);
                var loc = resp.Headers.Location;
                if ((int)resp.StatusCode < 300 || (int)resp.StatusCode >= 400 || loc is null) return null;
                var next = loc.IsAbsoluteUri ? loc : new Uri(cur, loc);
                if (IsPlainAmazon(next)) return next;
                cur = next;
            }
            catch
            {
                return null;
            }
        }
        return null;
    }
}
