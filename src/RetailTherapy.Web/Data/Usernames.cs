using System.Text.RegularExpressions;

namespace RetailTherapy.Web.Data;

/// <summary>Username rules: 3-20 characters, lowercase letters, digits and single underscores, starting with a letter.</summary>
public static class Usernames
{
    private static readonly Regex Shape = new("^[a-z][a-z0-9_]{2,19}$", RegexOptions.Compiled);

    private static readonly HashSet<string> Reserved = new(StringComparer.Ordinal)
    {
        "admin", "administrator", "root", "support", "help", "info", "contact", "mail", "email", "official", "staff", "team",
        "moderator", "mod", "system", "api", "www", "app", "web", "site", "sessions", "saved", "login", "signin", "signup",
        "logout", "account", "accounts", "settings", "profile", "user", "users", "me", "you", "null", "undefined", "anonymous",
        "guest", "retailtherapy", "retail_therapy", "retail", "therapy", "amazon", "google", "firebase", "apple", "legal",
        "privacy", "terms", "about", "home", "new", "search", "tag", "tags", "list", "lists", "wishlist", "u",
    };

    // Obvious abuse. Extend with Auth__BlockedNames (comma separated).
    private static readonly string[] BlockedParts = { "fuck", "shit", "cunt", "nigg", "fagg", "whore", "rapist", "nazi", "hitler" };

    public static string Normalize(string? name) => (name ?? "").Trim().ToLowerInvariant();

    /// <summary>Null when the (already normalized) name is allowed; otherwise a short reason for the person to read.</summary>
    public static string? Validate(string name, string? extraBlocked = null)
    {
        if (name.Length < 3 || name.Length > 20) return "Use 3 to 20 characters.";
        if (!Shape.IsMatch(name)) return "Start with a letter, then use letters, numbers or _ only.";
        if (name.Contains("__") || name.EndsWith('_')) return "No double underscores, and do not end with _.";
        if (Reserved.Contains(name)) return "That name is reserved.";
        var extra = (extraBlocked ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => x.ToLowerInvariant());
        if (BlockedParts.Concat(extra).Any(b => b.Length > 0 && name.Contains(b))) return "Please choose another name.";
        return null;
    }
}
