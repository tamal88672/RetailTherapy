using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using RetailTherapy.Web.Models;

namespace RetailTherapy.Web.Data;

/// <summary>
/// Turns a raw sheet row (title, link, image, up to three feature lines) into a ready product:
/// stable id, category, emoji tile, popularity order. Used by the Make.com automation.
/// </summary>
public static class QuickAdd
{
    // Same category names the site already uses. Books are told apart from other products first.
    private static readonly (string Name, string Emoji, int Hue, string[] Words)[] BookCategories =
    {
        ("Romance Books", "💕", 320, new[] { "romance", "romantasy", "fae", "enemies to lovers", "lovers", "spicy", "love story", "rom-com", "booktok", "slow burn", "court of", "colleen hoover", "fourth wing", "empyrean", "vows", "hockey", "off-campus", "kindred" }),
        ("Self-Help Books", "🌱", 120, new[] { "self-help", "self help", "habit*", "mindset", "life-changing", "personal growth", "boundaries", "theory", "atomic", "productivity" }),
        ("Fiction & Thrillers", "📖", 255, new[] { "thriller", "mystery", "suspense", "psychological", "novel", "fantasy", "dystopian", "sci-fi", "crime", "murder", "hunger games", "bestselling author", "trilogy", "housemaid" }),
    };

    private static readonly (string Name, string Emoji, int Hue, string[] Words)[] ProductCategories =
    {
        ("Makeup & Lips", "💄", 350, new[] { "lip", "mascara", "foundation", "concealer", "blush", "eyeliner", "eyeshadow", "makeup", "bronzer", "primer", "lash", "lashes", "setting spray", "brow", "highlighter", "tint", "skin tint", "contour", "palette" }),
        ("Hair & Body", "💆", 28, new[] { "shampoo", "conditioner", "hair", "scalp", "body lotion", "body wash", "body scrub", "deodorant", "hand cream", "shower", "bath", "massage", "gua sha", "foot", "body butter", "roll-on", "pheromone", "perfume", "fragrance" }),
        ("Skincare", "🧴", 340, new[] { "serum", "moisturizer", "cleanser", "toner", "sunscreen", "spf", "retinol", "vitamin c", "hyaluronic", "face mask", "face mist", "eye cream", "pore", "skin", "collagen", "exfoliat*", "niacinamide", "glycolic", "pimple patch", "peel", "patch", "patches", "hypochlorous", "face wash", "acne", "lotion", "face" }),
        ("Kitchen Appliances", "🍳", 15, new[] { "air fryer", "blender", "espresso machine", "coffee maker", "ice cream", "toaster", "mixer", "food processor", "instant pot", "pressure cooker", "waffle", "kettle", "juicer", "rice cooker", "slow cooker", "microwave", "grinder", "cooker", "sealer", "drink maker", "appliance", "sous vide", "steamer", "popcorn", "machine", "maker", "oven", "fryer" }),
        ("Drinkware & Coffee", "☕", 35, new[] { "tumbler", "water bottle", "mug", "cup", "coffee", "tea", "stanley", "thermos", "flask", "wine glass", "straw", "frother", "bottle", "nespresso", "keurig", "k-cup", "pod" }),
        ("Cookware & Dining", "🍽️", 150, new[] { "pan", "skillet", "pot", "knife", "cutting board", "bakeware", "baking", "container", "plate", "bowl", "cookware", "utensil", "spatula", "dutch oven", "silicone", "chopper", "slicer", "dinnerware", "kitchen", "cast iron", "enameled" }),
        ("Fashion", "👗", 200, new[] { "dress", "shirt", "top for women", "tank", "leggings", "jeans", "shoe", "sneaker", "sandal", "jacket", "sweater", "hoodie", "bra", "bag", "purse", "jewelry", "necklace", "earrings", "women's", "womens", "watch", "sunglasses", "pack", "backpack", "north face", "wear", "outfit", "jester", "skirt", "maxi", "swim*", "tee", "crossbody", "halter", "romper", "bikini", "pants", "shorts", "blouse", "cardigan", "boots", "heels", "handbag", "tote" }),
        ("Home & Gifts", "🎁", 285, new[] { "blanket", "candle", "diffuser", "decor", "pillow", "organizer", "storage", "gift", "lamp", "towel", "sheet", "humidifier", "wall art", "planter" }),
    };

    /// <summary>Product-type words from the category lists (serum, air fryer, tumbler...). The tag ranker uses them as tags.</summary>
    public static IEnumerable<string> ProductTypeWords => ProductCategories.SelectMany(c => c.Words);

    private const string Fallback = "Home & Gifts";
    private static readonly Regex Price = new(@"\$\s*\d|\d\s*%\s*off|\bsave\s+\d", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static string Slug(string title, string url)
    {
        var s = Regex.Replace((title ?? "").ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
        if (s.Length > 40) s = s[..40].Trim('-');
        if (s.Length == 0) s = "item";
        var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes((url ?? "").Trim()))).ToLowerInvariant()[..6];
        return $"{s}-{hash}";
    }

    public static string Classify(string title, IEnumerable<string> features)
    {
        var feat = string.Join(" ", features);
        return IsBook(title, feat)
            ? Pick(BookCategories, title, feat, "Fiction & Thrillers")
            : Pick(ProductCategories, title, feat, Fallback);
    }

    private static readonly Regex BookBy = new(@"\sby\s+[A-Z]", RegexOptions.Compiled);
    private static readonly Regex BookTitle = new(@"\bbook\s*\d|\bnovel\b|\(.*\b(series|book)\b.*\)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex BookWords = new(@"\b(booktok|trilogy|romantasy|paperback|hardcover|novel|author|readers?|romance)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static bool IsBook(string title, string feat) =>
        BookBy.IsMatch(title) || BookTitle.IsMatch(title) || BookWords.IsMatch(title + " " + feat);

    // A word in the title counts double compared with one in the feature lines.
    private static string Pick((string Name, string Emoji, int Hue, string[] Words)[] list, string title, string feat, string fallback)
    {
        var tl = title.ToLowerInvariant();
        var fl = feat.ToLowerInvariant();
        var best = fallback;
        var bestScore = 0;
        foreach (var c in list)
        {
            var hits = 0;
            foreach (var w in c.Words)
            {
                if (Matches(tl, w)) hits += 2;
                else if (Matches(fl, w)) hits += 1;
            }
            if (hits > bestScore) { bestScore = hits; best = c.Name; }
        }
        return best;
    }

    // Whole-word match (plural allowed); a trailing * means "starts with".
    private static bool Matches(string text, string word)
    {
        var stem = word.EndsWith('*');
        var w = Regex.Escape(word.TrimEnd('*'));
        return Regex.IsMatch(text, @"\b" + w + (stem ? "" : @"(?:s|es)?\b"));
    }

    public static (string Emoji, int Hue) Look(string category)
    {
        foreach (var c in BookCategories.Concat(ProductCategories)) if (c.Name == category) return (c.Emoji, c.Hue);
        return ("🛍️", 200);
    }

    /// <summary>Rough popularity from review / "bought this month" counts in the text. Higher = more popular.</summary>
    public static double Popularity(string text)
    {
        double best = 0, bought = 0;
        foreach (Match m in Regex.Matches(text, @"(\d[\d,.]*)\s*([KkMm])?\s*\+?\s*(?:five-star\s+|5-star\s+)?(?:ratings|reviews|readers|reviewers|buyers)", RegexOptions.IgnoreCase))
            best = Math.Max(best, Num(m.Groups[1].Value, m.Groups[2].Value));
        foreach (Match m in Regex.Matches(text, @"(\d[\d,.]*)\s*million\s*(?:copies|bottles|units)", RegexOptions.IgnoreCase))
            best = Math.Max(best, Num(m.Groups[1].Value, "m"));
        foreach (Match m in Regex.Matches(text, @"(\d[\d,.]*)\s*([KkMm])?\s*\+?\s*bought", RegexOptions.IgnoreCase))
            bought = Math.Max(bought, Num(m.Groups[1].Value, m.Groups[2].Value));

        var s = Math.Log10(1 + Math.Max(best, bought * 8));
        if (Regex.IsMatch(text, @"#1|best\s?seller|bestselling", RegexOptions.IgnoreCase)) s += 0.4;
        if (Regex.IsMatch(text, @"amazon'?s choice|overall pick", RegexOptions.IgnoreCase)) s += 0.3;
        if (Regex.IsMatch(text, @"viral|booktok|tiktok", RegexOptions.IgnoreCase)) s += 0.15;
        return s < 1 ? 2.5 : s;
    }

    /// <summary>Maps a popularity score onto the same 1..209 scale the existing products use (no signal = bottom).</summary>
    public static int OrderFor(double score) =>
        Math.Clamp((int)Math.Round(209 - (score - 2.5) / 5.5 * 208), 1, 209);

    private static double Num(string n, string unit)
    {
        if (!double.TryParse(n.Replace(",", "").TrimEnd('.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) return 0;
        return unit.ToLowerInvariant() switch { "k" => v * 1e3, "m" => v * 1e6, _ => v };
    }

    /// <summary>Builds the product from the posted fields. Returns null (with an error) when something required is missing.</summary>
    public static Product? Build(IFormCollection form, out string? error)
    {
        string F(string k) => form[k].ToString().Trim();
        var title = F("title");
        var url = F("url");
        if (title.Length == 0 || url.Length == 0)
        {
            error = "title and url are required";
            return null;
        }
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u) || (u.Scheme != "https" && u.Scheme != "http"))
        {
            error = "url must be an absolute http(s) link";
            return null;
        }

        var features = new[] { F("feature1"), F("feature2"), F("feature3") }
            .Where(f => f.Length > 0 && !Price.IsMatch(f)).ToList();
        var category = F("category");
        if (category.Length == 0) category = Classify(title, features);
        var (emoji, hue) = Look(category);
        var image = F("image");
        var score = Popularity(title + " " + string.Join(" ", features));

        // Tags sent by hand ("tags" field, comma separated) always come first. Otherwise the site ranks tags itself.
        var tags = Tagger.Parse(F("tags"));

        error = null;
        return new Product
        {
            Id = Slug(title, url),
            Title = title,
            Category = category,
            Url = url,
            Emoji = emoji,
            Hue = hue,
            Image = image.Length > 0 ? image : null,
            Blurb = features.FirstOrDefault() ?? "",
            Pros = features.Skip(1).ToArray(),
            Order = OrderFor(score),
            Active = true,
            Tags = tags,
        };
    }
}
