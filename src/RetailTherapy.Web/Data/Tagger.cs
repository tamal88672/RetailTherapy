using System.Text.RegularExpressions;
using RetailTherapy.Web.Models;

namespace RetailTherapy.Web.Data;

/// <summary>
/// Short labels shown under each product ("gift idea", "viral", "cozy"...). They are worked out from the title and
/// feature lines when a product has none of its own, and they power the clickable tags and the curated lists.
/// </summary>
public static class Tagger
{
    private const RegexOptions Opts = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled;
    private static readonly TimeSpan Limit = TimeSpan.FromMilliseconds(250);

    // The full set of labels the site knows. Order is the tie-breaker when two labels match equally well.
    private static readonly (string Tag, Regex Rx)[] Vocabulary =
    {
        ("bestseller",      R(@"#\s?1\b|best[\s-]?sell(?:er|ers|ing)\b|amazon'?s choice|top[\s-]rated|award[\s-]winning")),
        ("viral",           R(@"\bviral\b|tiktok|booktok|trending|obsessed|internet'?s? favou?rite")),
        ("gift idea",       R(@"\bgifts?\b|\bgifting\b|stocking stuffer|birthday|mother'?s day|valentine|christmas|holiday")),
        ("self-care",       R(@"self[\s-]?care|\bspa\b|relax(?:ing|ation)?\b|massag(?:e|er|ing)\b|\bbath(?:s|ing)?\b|gua sha|face mask|scalp|aromatherapy|diffuser|soothing|pamper")),
        ("cozy",            R(@"\bcozy\b|\bcosy\b|fleece|sherpa|plush|slippers?\b|blanket|snuggl\w*|candles?\b|fuzzy|\bthrow\b")),
        ("glow",            R(@"\bglow(?:y|ing)?\b|radiant|radiance|dewy|luminous|illuminat\w*|brighten\w*|highlighter|vitamin c")),
        ("hydrating",       R(@"hydrat\w*|moisturi[sz]\w*|hyaluronic|plump\w*")),
        ("long-lasting",    R(@"long[\s-]?lasting|all[\s-]day|24[\s-]?hours?|waterproof|smudge[\s-]?proof|transfer[\s-]?proof|budge[\s-]?proof|stays? put")),
        ("travel-friendly", R(@"\btravel\w*|portable|on[\s-]the[\s-]go|compact|carry[\s-]?on|pocket|\bmini\b|foldable|collapsible|cordless")),
        ("time-saver",      R(@"time[\s-]saving|saves? time|quick(?:ly)?\b|\bfast\b|one[\s-]touch|automatic|effortless\w*|easy to use|easy[\s-]clean|in minutes|hands[\s-]free")),
        ("eco-friendly",    R(@"eco[\s-]?friendly|sustainab\w*|reusable|biodegradable|bamboo|recycled|plastic[\s-]free|organic|refillable|compostable|zero[\s-]waste")),
        ("clean beauty",    R(@"\bvegan\b|cruelty[\s-]free|fragrance[\s-]free|paraben[\s-]free|sulfate[\s-]free|hypoallergenic|dermatologist|sensitive skin|\bgentle\b")),
        ("summer",          R(@"\bsummer\b|\bspf\b|sunscreen|sunblock|swim\w*|bikini|\bbeach\b|sandals?\b|cooling|sun[\s-]?protect\w*|lightweight")),
        ("page-turner",     R(@"page[\s-]?turner|couldn'?t put (?:it )?down|gripping|\bbinge\b|addictive|unputdownable|edge of your seat|cliffhanger|\btwists?\b")),
        ("series",          R(@"\bbook\s*\d|\bseries\b|trilogy|\bsaga\b|box set|\bvol(?:ume)?\.?\s*\d")),
        ("dupe",            R(@"\bdupes?\b|alternative to|inspired by")),
        ("date night",      R(@"date[\s-]night|romantic|pheromone|perfume|fragrance|lip stain")),
    };

    private static Regex R(string pattern) => new(pattern, Opts, Limit);

    /// <summary>The labels the site can pick on its own (handy for the admin and for list rules).</summary>
    public static IEnumerable<string> Known => Vocabulary.Select(v => v.Tag);

    public static string[] Derive(Product p) =>
        Derive(p.Title, new[] { p.Blurb }.Concat(p.Pros ?? Array.Empty<string>()), p.Category, p.Badge);

    /// <summary>Up to three labels. A match in the title counts double compared with one in the feature lines.</summary>
    public static string[] Derive(string title, IEnumerable<string> features, string category, string? badge = null)
    {
        title = $"{title} {badge}".Trim();
        var rest = string.Join(" ", features.Where(f => !string.IsNullOrWhiteSpace(f)));
        var picked = new List<string>();
        try
        {
            picked = Vocabulary
                .Select((v, i) => (v.Tag, Order: i, Hits: (v.Rx.IsMatch(title) ? 2 : 0) + (v.Rx.IsMatch(rest) ? 1 : 0)))
                .Where(x => x.Hits > 0)
                .OrderByDescending(x => x.Hits).ThenBy(x => x.Order)
                .Take(3).Select(x => x.Tag).ToList();
        }
        catch (RegexMatchTimeoutException) { /* odd text: fall through to the group label */ }

        if (picked.Count == 0) picked.Add(Group(category));
        return picked.ToArray();
    }

    /// <summary>Every product gets at least its broad group label, even when nothing more specific matched.</summary>
    public static string Group(string category)
    {
        var c = (category ?? "").ToLowerInvariant();
        if (c.Contains("book") || c.Contains("fiction") || c.Contains("thriller") || c.Contains("romance")) return "books";
        if (c is "makeup & lips" or "hair & body" or "skincare" or "beauty") return "beauty";
        if (c is "kitchen appliances" or "drinkware & coffee" or "cookware & dining" or "kitchen") return "kitchen";
        if (c == "fashion") return "style";
        if (c is "home & gifts" or "home") return "home";
        var first = Clean(new[] { c }).FirstOrDefault();
        return first ?? "finds";
    }

    /// <summary>Lowercase letters, digits, spaces and dashes only; at most 24 characters; no duplicates; at most 6 labels.</summary>
    public static string[] Clean(IEnumerable<string>? tags)
    {
        if (tags is null) return Array.Empty<string>();
        return tags
            .Select(t => Regex.Replace((t ?? "").ToLowerInvariant().TrimStart('#'), "[^a-z0-9 -]+", " "))
            .Select(t => Regex.Replace(t, @"\s+", " ").Trim(' ', '-'))
            .Select(t => t.Length > 24 ? t[..24].Trim(' ', '-') : t)
            .Where(t => t.Length > 0)
            .Distinct()
            .Take(6)
            .ToArray();
    }

    /// <summary>Splits "gift idea, viral, cozy" (commas, semicolons or new lines) into clean labels.</summary>
    public static string[] Parse(string? text) =>
        Clean((text ?? "").Split(new[] { ',', ';', '\n', '|' }, StringSplitOptions.RemoveEmptyEntries));
}
