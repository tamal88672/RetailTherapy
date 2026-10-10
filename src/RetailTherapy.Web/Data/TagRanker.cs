using System.Text.RegularExpressions;
using RetailTherapy.Web.Models;

namespace RetailTherapy.Web.Data;

/// <summary>A tag and how relevant it is to one product (0 to 100).</summary>
public sealed record RankedTag(string Tag, int Score);

/// <summary>
/// Works out which tags describe each product and how relevant each one is, so the best tags show first and a tag
/// page shows the products it truly fits.
///
/// How it scores (TF-IDF, the standard way search engines weigh words):
///  1. Candidates are the theme tags (gift idea, viral, cozy...) and product-type words (serum, air fryer, tumbler...).
///  2. Term weight: a match counts for more where it is more important: title 3, blurb 2, feature lines 1.5, badge 1.
///     Repeats in one place add a little (1 + ln count), not a lot.
///  3. Rarity weight (IDF): a tag found on a few products says more than one found on nearly all of them,
///     so raw = term weight x ( ln((N+1)/(df+1)) + 1 ).
///  4. Raw scores are squeezed onto 0-100 with 100 x (1 - e^(-raw/6)) so they can be compared between products.
///  5. Tags typed by hand (the product's own Tags) always come first, at 100 and falling slightly.
///  6. Weak tags (under <see cref="MinScore"/>) are dropped; every product keeps at least its broad group tag.
///
/// A product's top <see cref="TopN"/> tags are the ones that "count": the tag page and the lists show a product for a
/// tag only when the tag is in its top 3, best match first.
/// </summary>
public static class TagRanker
{
    public const int TopN = 3;
    public const int Keep = 6;
    public const int MinScore = 22;
    private const double Saturation = 7.0;
    private const double WTitle = 3.0, WBlurb = 2.0, WPros = 1.5, WBadge = 1.0, WGroup = 1.0;

    // Words too general to describe a product on their own.
    private static readonly HashSet<string> TooGeneral = new(StringComparer.OrdinalIgnoreCase)
    {
        "pack", "wear", "face", "skin", "machine", "maker", "appliance", "women's", "womens", "top for women",
        "outfit", "jester", "kitchen", "bath", "cooker", "tee", "pod", "cup", "pot", "set", "pan", "gift", "sheet",
    };

    // A few more product types that the category lists spell differently (lipstick is not "lip").
    private static readonly string[] ExtraTypeWords =
    {
        "lipstick", "lip gloss", "lip oil", "lip balm", "lip liner", "nail polish", "hair dryer", "blow dryer", "hair mask", "hair oil",
        "pillowcase", "humidifier", "hand soap", "sheet mask", "eye patch", "sleep mask", "yoga mat", "phone case", "earbuds",
        "headphones", "sweatshirt", "tote bag", "kindle", "audiobook", "journal", "planner", "puzzle",
    };

    private sealed record Candidate(string Tag, Regex Rx, string? Needle);

    private static readonly Candidate[] Candidates = BuildCandidates();

    private static Candidate[] BuildCandidates()
    {
        var list = Tagger.Themes.Select(t => new Candidate(t.Tag, t.Rx, null)).ToList();
        var seen = new HashSet<string>(list.Select(c => Singular(c.Tag)));
        foreach (var raw in QuickAdd.ProductTypeWords.Concat(ExtraTypeWords))
        {
            var stem = raw.EndsWith('*');
            var word = raw.TrimEnd('*').Trim().ToLowerInvariant();
            if (word.Length < 3 || TooGeneral.Contains(word) || !seen.Add(Singular(word))) continue;
            // "-free" after the word means the opposite (fragrance-free is not a fragrance).
            var rx = new Regex(@"\b" + Regex.Escape(word) + (stem ? "" : @"(?:s|es)?\b(?![\s-]?free\b)"),
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250));
            list.Add(new Candidate(word, rx, word));
        }
        return list.ToArray();
    }

    // patches -> patch, lashes -> lash: one tag per idea.
    private static string Singular(string w) =>
        Regex.IsMatch(w, "(?:ch|sh|x|ss)es$") ? w[..^2] : w.EndsWith("s") && !w.EndsWith("ss") ? w[..^1] : w;

    private static string[] Tokens(string tag) => tag.Split(new[] { ' ', '-' }, StringSplitOptions.RemoveEmptyEntries);

    // "fryer" next to "air fryer", "patch" next to "pimple patch": keep the more specific tag.
    private static List<RankedTag> DropRedundant(List<RankedTag> tags) =>
        tags.Where(b => !tags.Any(a => a.Tag != b.Tag && a.Score >= b.Score - 12 &&
                                       Tokens(a.Tag).Length > Tokens(b.Tag).Length &&
                                       Tokens(b.Tag).All(t => Tokens(a.Tag).Contains(t)))).ToList();

    /// <summary>Ranked tags for every product, best first, keyed by product id.</summary>
    public static Dictionary<string, RankedTag[]> RankAll(IReadOnlyList<Product> products)
    {
        // 1) term weight per product and tag
        var weights = new List<Dictionary<string, double>>(products.Count);
        var df = new Dictionary<string, int>();
        foreach (var p in products)
        {
            var w = Weigh(p);
            weights.Add(w);
            foreach (var tag in w.Keys) df[tag] = df.GetValueOrDefault(tag) + 1;
        }

        // 2) rarity weight, then 0-100 scores
        var n = Math.Max(1, products.Count);
        var result = new Dictionary<string, RankedTag[]>(products.Count);
        for (var i = 0; i < products.Count; i++)
        {
            var p = products[i];
            var computed = weights[i]
                .Select(kv => new RankedTag(kv.Key, Score(kv.Value * (Math.Log((n + 1.0) / (df[kv.Key] + 1.0)) + 1.0))))
                .Where(t => t.Score >= MinScore)
                // equal scores: the more specific (longer) tag first
                .OrderByDescending(t => t.Score).ThenByDescending(t => Tokens(t.Tag).Length).ThenByDescending(t => t.Tag.Length).ThenBy(t => t.Tag, StringComparer.Ordinal)
                .ToList();
            computed = DropRedundant(computed);

            // 3) hand-typed tags first, then the computed ones
            var ranked = new List<RankedTag>();
            var manual = Tagger.Clean(p.Tags);
            for (var m = 0; m < manual.Length; m++) ranked.Add(new RankedTag(manual[m], Math.Max(MinScore, 100 - m * 4)));
            foreach (var t in computed)
                if (ranked.All(r => r.Tag != t.Tag)) ranked.Add(t);

            if (ranked.Count == 0) ranked.Add(new RankedTag(Tagger.Group(p.Category), MinScore));
            result[p.Id] = ranked.Take(Keep).ToArray();
        }
        return result;
    }

    private static int Score(double raw) => (int)Math.Round(100.0 * (1.0 - Math.Exp(-raw / Saturation)));

    // Term weight of every candidate tag that shows up in this product's text.
    private static Dictionary<string, double> Weigh(Product p)
    {
        var fields = new (string Text, double Weight)[]
        {
            (p.Title ?? "", WTitle),
            (p.Blurb ?? "", WBlurb),
            (string.Join(" ", p.Pros ?? Array.Empty<string>()), WPros),
            (p.Badge ?? "", WBadge),
        };
        var w = new Dictionary<string, double>();
        foreach (var c in Candidates)
        {
            double tf = 0;
            foreach (var (text, weight) in fields)
            {
                if (text.Length == 0) continue;
                if (c.Needle is not null && text.IndexOf(c.Needle, StringComparison.OrdinalIgnoreCase) < 0) continue;
                int count;
                try { count = c.Rx.Matches(text).Count; }
                catch (RegexMatchTimeoutException) { continue; }
                if (count > 0) tf += weight * (1 + Math.Log(count));
            }
            if (tf > 0) w[c.Tag] = tf;
        }
        var group = Tagger.Group(p.Category);
        w[group] = w.GetValueOrDefault(group) + WGroup;
        return w;
    }
}
