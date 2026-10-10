using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;

namespace RetailTherapy.Web.Data;

/// <summary>
/// Keeps the tracking endpoints cheap and hard to abuse: a visitor id must look like a random id,
/// one visitor can only send so many events in ten minutes, and there is a daily cap for the whole site.
/// (Counters live in memory, so they are per running instance, which is plenty for this.)
/// </summary>
public sealed class TrackingGuard
{
    public static readonly string[] EventTypes = { "wish_add", "wish_remove", "click" };
    private static readonly Regex VisitorPattern = new("^[A-Za-z0-9-]{16,64}$", RegexOptions.Compiled);
    private static readonly Regex SourcePattern = new("^[a-z0-9:_-]{1,48}$", RegexOptions.Compiled);

    private readonly IMemoryCache _cache;
    private readonly int _perVisitor;
    private readonly int _perDay;
    private readonly object _lock = new();
    private string _day = "";
    private int _today;

    public TrackingGuard(IMemoryCache cache, IConfiguration config)
    {
        _cache = cache;
        _perVisitor = config.GetValue("Tracking:MaxEventsPerVisitor", 120);
        _perDay = config.GetValue("Tracking:MaxEventsPerDay", 20000);
    }

    public static bool ValidVisitor(string? id) => id is not null && VisitorPattern.IsMatch(id);

    public static string CleanSource(string? s)
    {
        var v = (s ?? "").Trim().ToLowerInvariant();
        return SourcePattern.IsMatch(v) ? v : "site";
    }

    /// <summary>True if this visitor may record <paramref name="count"/> more events right now.</summary>
    public bool Allow(string visitorId, int count)
    {
        lock (_lock)
        {
            var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
            if (_day != today) { _day = today; _today = 0; }
            if (_today + count > _perDay) return false;

            var key = "trk:" + visitorId;
            var used = _cache.TryGetValue(key, out int n) ? n : 0;
            if (used + count > _perVisitor) return false;
            _cache.Set(key, used + count, TimeSpan.FromMinutes(10));
            _today += count;
            return true;
        }
    }

    /// <summary>Limits the account endpoints: a signed-in person may make this many calls in ten minutes.</summary>
    public bool AllowAccount(string uid, int perTenMinutes = 120)
    {
        var key = "acct:" + uid;
        lock (_lock)
        {
            var used = _cache.TryGetValue(key, out int n) ? n : 0;
            if (used + 1 > perTenMinutes) return false;
            _cache.Set(key, used + 1, TimeSpan.FromMinutes(10));
            return true;
        }
    }
}
