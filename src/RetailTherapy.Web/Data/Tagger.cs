using System.Text.RegularExpressions;

namespace RetailTherapy.Web.Data;

/// <summary>
/// The tag vocabulary ("themes" such as gift idea, viral, cozy) and tag text helpers.
/// Which tags a product gets, and in what order, is decided by <see cref="TagRanker"/>.
/// </summary>
public static class Tagger
{
    private const RegexOptions Opts = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
    private static readonly TimeSpan Limit = TimeSpan.FromMilliseconds(250);

    /// <summary>Theme tags and the wording that signals them in a title, blurb or feature line.</summary>
    public static readonly (string Tag, Regex Rx)[] Themes =
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
        ("date night",      R(@"date[\s-]night|romantic|pheromone|perfume|fragrance(?![\s-]?free)|lip stain")),
        ("dupe",            R(@"\bdupes?\b|alternative to|inspired by")),
        // books
        ("page-turner",     R(@"page[\s-]?turner|couldn'?t put (?:it )?down|gripping|\bbinge\b|addictive|unputdownable|edge of your seat|cliffhanger|\btwists?\b")),
        ("series",          R(@"\bbook\s*\d|\bseries\b|trilogy|\bsaga\b|box set|\bvol(?:ume)?\.?\s*\d")),
        ("romance",         R(@"\bromance\b|romantasy|rom-com|enemies to lovers|slow burn|love story")),
        ("thriller",        R(@"thriller|suspense|psychological|whodunit|murder mystery|\bmystery\b")),
        ("fantasy",         R(@"\bfantasy\b|romantasy|\bfae\b|dragons?\b|magic\b")),
        ("self-help",       R(@"self[\s-]?help|\bhabits?\b|mindset|personal growth|productivity|boundaries")),
    };

    private static Regex R(string pattern) => new(pattern, Opts, Limit);

    /// <summary>The broad label every product can fall back on, so no tile is ever without a tag.</summary>
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
