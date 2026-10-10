using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using RetailTherapy.Web.Data;
using RetailTherapy.Web.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<SiteOptions>(builder.Configuration.GetSection("Site"));
builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection("Auth"));
builder.Services.AddMemoryCache();
builder.Services.AddResponseCompression();
builder.Services.AddSingleton<ProductCatalog>();
builder.Services.AddSingleton<ListCatalog>();
builder.Services.AddSingleton<TrackingGuard>();
builder.Services.AddSingleton<FirebaseTokenVerifier>();
// Used to open Amazon short links once (without following them into Amazon) to read the store ID they carry.
builder.Services.AddHttpClient("amazon").ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });

// Storage: "Firestore" in the cloud, "Json" (a local file) for development.
if (string.Equals(builder.Configuration["Storage:Provider"], "Firestore", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddFirestoreProductStore(builder.Configuration);
}
else
{
    var data = Path.Combine(builder.Environment.ContentRootPath, "App_Data");
    builder.Services.AddSingleton<IProductStore>(_ => new JsonFileProductStore(Path.Combine(data, "products.json")));
    builder.Services.AddSingleton<IDocStore<CuratedList>>(_ => new JsonDocStore<CuratedList>(Path.Combine(data, "lists.json")));
    builder.Services.AddSingleton<IDocStore<Wishlist>>(_ => new JsonDocStore<Wishlist>(Path.Combine(data, "wishlists.json")));
    builder.Services.AddSingleton<IEventStore>(_ => new JsonLinesEventStore(Path.Combine(data, "events.jsonl")));
    builder.Services.AddSingleton<IUserStore>(_ => new JsonUserStore(Path.Combine(data, "users.json")));
}

var app = builder.Build();

// First run: load the starter products and lists if their stores are empty.
if (app.Configuration.GetValue("Storage:SeedOnStart", true))
{
    try
    {
        var seedPath = Path.Combine(app.Environment.ContentRootPath, "Seed", "products.json");
        if (File.Exists(seedPath))
        {
            await using var fs = File.OpenRead(seedPath);
            var seed = await JsonSerializer.DeserializeAsync<List<Product>>(fs, JsonDefaults.Web) ?? new List<Product>();
            await app.Services.GetRequiredService<IProductStore>().SeedIfEmptyAsync(seed, CancellationToken.None);
        }
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Seeding products failed; continuing without it.");
    }

    try
    {
        var listsPath = Path.Combine(app.Environment.ContentRootPath, "Seed", "lists.json");
        if (File.Exists(listsPath))
        {
            await using var fs = File.OpenRead(listsPath);
            var seed = await JsonSerializer.DeserializeAsync<List<CuratedList>>(fs, JsonDefaults.Web) ?? new List<CuratedList>();
            await app.Services.GetRequiredService<IDocStore<CuratedList>>().SeedIfEmptyAsync(seed, CancellationToken.None);
        }
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Seeding lists failed; continuing without it.");
    }
}

// ---- Static site: the live design is served at "/", every design stays under its own folder ----
var site = app.Services.GetRequiredService<IOptions<SiteOptions>>().Value;
var liveDir = Path.Combine(app.Environment.WebRootPath, site.LiveDesign);
if (!Directory.Exists(liveDir))
    throw new InvalidOperationException($"Site:LiveDesign '{site.LiveDesign}' is not a folder under wwwroot.");
var liveFiles = new PhysicalFileProvider(liveDir);
static void CacheFiles(StaticFileResponseContext c) => c.Context.Response.Headers.CacheControl = "public,max-age=300";

app.UseResponseCompression();
app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = liveFiles });
app.UseStaticFiles(new StaticFileOptions { FileProvider = liveFiles, OnPrepareResponse = CacheFiles });
app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions { OnPrepareResponse = CacheFiles });

// ---- Public API ----
app.MapGet("/healthz", () => Results.Text("ok"));

app.MapGet("/api/site", (HttpContext ctx, IOptions<SiteOptions> o, IOptions<AuthOptions> a) =>
{
    var s = o.Value;
    var au = a.Value;
    ctx.Response.Headers.CacheControl = "public,max-age=300";
    return Results.Ok(new
    {
        s.Name,
        s.Tagline,
        s.Disclosure,
        s.ContactEmail,
        s.ListsName,
        s.ListsTagline,
        // Sign-in settings for the browser. Public by design. "Enabled" is false until Auth__ProjectId and Auth__ApiKey are set.
        Auth = au.Enabled
            ? new { Enabled = true, ApiKey = au.ApiKey, AuthDomain = au.Domain, ProjectId = au.ProjectId, AppId = au.AppId }
            : new { Enabled = false, ApiKey = "", AuthDomain = "", ProjectId = "", AppId = "" },
        Ads = new { s.Ads.Enabled, s.Ads.Client, Slots = new { Sidebar = s.Ads.SlotSidebar, Inline = s.Ads.SlotInline } }
    });
});

app.MapGet("/api/products", async (HttpContext ctx, ProductCatalog catalog, CancellationToken ct) =>
{
    var snap = await catalog.GetSnapshotAsync(ct);
    ctx.Response.Headers.CacheControl = "public,max-age=60";
    ctx.Response.Headers.ETag = snap.ETag;
    if (ctx.Request.Headers.IfNoneMatch == snap.ETag) return Results.StatusCode(StatusCodes.Status304NotModified);
    return Results.Ok(snap.Items);
});

app.MapGet("/api/lists", async (HttpContext ctx, ListCatalog lists, CancellationToken ct) =>
{
    var snap = await lists.GetSnapshotAsync(ct);
    ctx.Response.Headers.CacheControl = "public,max-age=60";
    ctx.Response.Headers.ETag = snap.ETag;
    if (ctx.Request.Headers.IfNoneMatch == snap.ETag) return Results.StatusCode(StatusCodes.Status304NotModified);
    return Results.Ok(snap.Items);
});

// The curated-lists page (called "Therapy Sessions" on the site; the name is Site:ListsName). It lives in the live design's folder.
app.MapGet("/sessions", (HttpContext ctx) =>
{
    var file = liveFiles.GetFileInfo("sessions.html");
    if (!file.Exists) return Results.NotFound();
    ctx.Response.Headers.CacheControl = "public,max-age=300";
    return Results.File(file.CreateReadStream(), "text/html; charset=utf-8");
});

// ---- Tracking: which products people save and click. Anonymous (a random id kept in the visitor's browser). ----
app.MapPost("/api/track", async (TrackBatch? batch, ProductCatalog catalog, IEventStore events, TrackingGuard guard, ILogger<Program> log, CancellationToken ct) =>
{
    if (batch is null || batch.Events is null || batch.Events.Count == 0 || !TrackingGuard.ValidVisitor(batch.VisitorId))
        return Results.BadRequest(new { error = "visitorId and events are required" });

    var known = (await catalog.GetSnapshotAsync(ct)).Items.Select(p => p.Id).ToHashSet();
    var now = DateTime.UtcNow;
    var list = batch.Events.Take(20)
        .Where(e => e is not null && TrackingGuard.EventTypes.Contains(e.Type) && known.Contains(e.ProductId))
        .Select(e => new TrackEvent
        {
            Type = e.Type,
            ProductId = e.ProductId,
            VisitorId = batch.VisitorId,
            Source = TrackingGuard.CleanSource(e.Source),
            Day = now.ToString("yyyy-MM-dd"),
            At = now.ToString("o"),
        }).ToList();
    if (list.Count == 0) return Results.NoContent();
    if (!guard.Allow(batch.VisitorId, list.Count)) return Results.StatusCode(StatusCodes.Status429TooManyRequests);

    try { await events.AppendAsync(list, ct); }
    catch (Exception ex) { log.LogError(ex, "Could not record events"); } // tracking must never break the page
    return Results.NoContent();
});

// A visitor's saved items, kept on the server too (the browser copy is the one the page uses).
// This is the hook for accounts later: the same list is then claimed by a user and can be shared.
app.MapPut("/api/wishlist", async (WishlistPut? body, ProductCatalog catalog, IDocStore<Wishlist> store, TrackingGuard guard, ILogger<Program> log, CancellationToken ct) =>
{
    if (body is null || !TrackingGuard.ValidVisitor(body.VisitorId)) return Results.BadRequest(new { error = "visitorId is required" });
    if (!guard.Allow(body.VisitorId, 1)) return Results.StatusCode(StatusCodes.Status429TooManyRequests);

    var known = (await catalog.GetSnapshotAsync(ct)).Items.Select(p => p.Id).ToHashSet();
    var ids = (body.ProductIds ?? Array.Empty<string>()).Where(known.Contains).Distinct().Take(200).ToArray();
    var now = DateTime.UtcNow.ToString("o");
    try
    {
        var id = "v-" + body.VisitorId;
        var w = await store.GetAsync(id, ct) ?? new Wishlist { Id = id, OwnerType = "visitor", OwnerId = body.VisitorId, CreatedOn = now };
        w.ProductIds = ids;
        w.UpdatedOn = now;
        await store.UpsertAsync(w, ct);
    }
    catch (Exception ex) { log.LogError(ex, "Could not save wishlist"); }
    return Results.NoContent();
});

app.MapGet("/api/wishlist/{visitorId}", async (string visitorId, IDocStore<Wishlist> store, CancellationToken ct) =>
{
    if (!TrackingGuard.ValidVisitor(visitorId)) return Results.BadRequest(new { error = "bad visitor id" });
    var w = await store.GetAsync("v-" + visitorId, ct);
    return w is null ? Results.NotFound() : Results.Ok(new { w.Name, w.ProductIds, w.UpdatedOn });
});

// ---- Accounts (sign-in is Firebase Authentication: Google and email link). The browser sends the sign-in proof as
// "Authorization: Bearer <token>"; we verify it ourselves and keep only a random id, a username and the saved list. ----
static async Task<SessionUser?> SignedIn(HttpContext ctx, FirebaseTokenVerifier auth, TrackingGuard guard, CancellationToken ct)
{
    ctx.Response.Headers.CacheControl = "no-store";
    var h = ctx.Request.Headers.Authorization.ToString();
    if (!h.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return null;
    var user = await auth.VerifyAsync(h[7..].Trim(), ct);
    if (user is null) return null;
    if (guard.AllowAccount(user.Uid)) return user;
    ctx.Items["limited"] = true;
    return null;
}

// 429 when the person is over the limit, otherwise 401 (not signed in).
static IResult Deny(HttpContext ctx) =>
    ctx.Items.ContainsKey("limited") ? Results.StatusCode(StatusCodes.Status429TooManyRequests) : Results.Unauthorized();

static object MeBody(UserProfile u) => new { username = u.Username, provider = u.Provider, createdOn = u.CreatedOn };

app.MapGet("/api/me", async (HttpContext ctx, FirebaseTokenVerifier auth, IUserStore users, TrackingGuard guard, ILogger<Program> log, CancellationToken ct) =>
{
    var me = await SignedIn(ctx, auth, guard, ct);
    if (me is null) return Deny(ctx);
    try { return Results.Ok(MeBody(await users.EnsureAsync(me.Uid, me.Provider, ct))); }
    catch (Exception ex) { log.LogError(ex, "Could not load account"); return Results.StatusCode(StatusCodes.Status503ServiceUnavailable); }
});

app.MapGet("/api/me/username/available", async (string? name, HttpContext ctx, FirebaseTokenVerifier auth, IUserStore users, IOptions<AuthOptions> opt, TrackingGuard guard, CancellationToken ct) =>
{
    var me = await SignedIn(ctx, auth, guard, ct);
    if (me is null) return Deny(ctx);
    var lower = Usernames.Normalize(name);
    var problem = Usernames.Validate(lower, opt.Value.BlockedNames);
    if (problem is not null) return Results.Ok(new { available = false, reason = problem });
    var taken = await users.UsernameTakenAsync(lower, ct);
    return Results.Ok(new { available = !taken, reason = taken ? "That name is taken." : null });
});

// The username is chosen once and can never be changed (the claim is a single transaction, see UserStore).
app.MapPost("/api/me/username", async (UsernameBody? body, HttpContext ctx, FirebaseTokenVerifier auth, IUserStore users, IOptions<AuthOptions> opt, TrackingGuard guard, ILogger<Program> log, CancellationToken ct) =>
{
    var me = await SignedIn(ctx, auth, guard, ct);
    if (me is null) return Deny(ctx);
    var lower = Usernames.Normalize(body?.Name);
    var problem = Usernames.Validate(lower, opt.Value.BlockedNames);
    if (problem is not null) return Results.BadRequest(new { error = problem });
    try
    {
        await users.EnsureAsync(me.Uid, me.Provider, ct);
        return await users.ClaimUsernameAsync(me.Uid, lower, ct) switch
        {
            ClaimResult.Ok => Results.Ok(new { username = lower }),
            ClaimResult.Taken => Results.Conflict(new { error = "That name is taken." }),
            ClaimResult.AlreadyHasUsername => Results.Conflict(new { error = "You already have a username, and it cannot be changed." }),
            _ => Results.Unauthorized(),
        };
    }
    catch (Exception ex) { log.LogError(ex, "Could not claim username"); return Results.StatusCode(StatusCodes.Status503ServiceUnavailable); }
});

app.MapGet("/api/me/wishlist", async (HttpContext ctx, FirebaseTokenVerifier auth, IDocStore<Wishlist> store, TrackingGuard guard, CancellationToken ct) =>
{
    var me = await SignedIn(ctx, auth, guard, ct);
    if (me is null) return Deny(ctx);
    var w = await store.GetAsync("u-" + me.Uid, ct);
    return Results.Ok(new { productIds = w?.ProductIds ?? Array.Empty<string>(), updatedOn = w?.UpdatedOn });
});

app.MapPut("/api/me/wishlist", async (WishBody? body, HttpContext ctx, FirebaseTokenVerifier auth, ProductCatalog catalog, IDocStore<Wishlist> store, TrackingGuard guard, ILogger<Program> log, CancellationToken ct) =>
{
    var me = await SignedIn(ctx, auth, guard, ct);
    if (me is null) return Deny(ctx);
    var known = (await catalog.GetSnapshotAsync(ct)).Items.Select(p => p.Id).ToHashSet();
    var ids = (body?.ProductIds ?? Array.Empty<string>()).Where(known.Contains).Distinct().Take(200).ToArray();
    try
    {
        var now = DateTime.UtcNow.ToString("o");
        var id = "u-" + me.Uid;
        var w = await store.GetAsync(id, ct) ?? new Wishlist { Id = id, OwnerType = "user", OwnerId = me.Uid, CreatedOn = now };
        w.ProductIds = ids;
        w.UpdatedOn = now;
        await store.UpsertAsync(w, ct);
        return Results.Ok(new { productIds = ids, updatedOn = now });
    }
    catch (Exception ex) { log.LogError(ex, "Could not save account wishlist"); return Results.StatusCode(StatusCodes.Status503ServiceUnavailable); }
});

// On first sign-in the saved items from this browser join the account's list (nothing is lost, nothing is duplicated).
app.MapPost("/api/me/claim", async (ClaimBody? body, HttpContext ctx, FirebaseTokenVerifier auth, ProductCatalog catalog, IDocStore<Wishlist> store, TrackingGuard guard, ILogger<Program> log, CancellationToken ct) =>
{
    var me = await SignedIn(ctx, auth, guard, ct);
    if (me is null) return Deny(ctx);
    var known = (await catalog.GetSnapshotAsync(ct)).Items.Select(p => p.Id).ToHashSet();
    var local = (body?.ProductIds ?? Array.Empty<string>()).Where(known.Contains).Distinct().Take(200).ToArray();
    try
    {
        var now = DateTime.UtcNow.ToString("o");
        var id = "u-" + me.Uid;
        var w = await store.GetAsync(id, ct) ?? new Wishlist { Id = id, OwnerType = "user", OwnerId = me.Uid, CreatedOn = now };
        // The account's items first, then this browser's new ones; the cap keeps the newest.
        var merged = w.ProductIds.Where(known.Contains).Concat(local).Distinct().ToArray();
        if (merged.Length > 200) merged = merged[^200..];
        w.ProductIds = merged;
        w.UpdatedOn = now;
        await store.UpsertAsync(w, ct);

        if (TrackingGuard.ValidVisitor(body?.VisitorId))
        {
            var v = await store.GetAsync("v-" + body!.VisitorId, ct);
            if (v is not null && string.IsNullOrEmpty(v.ClaimedBy))
            {
                v.ClaimedBy = me.Uid;
                await store.UpsertAsync(v, ct);
            }
        }
        return Results.Ok(new { productIds = merged, updatedOn = now });
    }
    catch (Exception ex) { log.LogError(ex, "Could not claim visitor list"); return Results.StatusCode(StatusCodes.Status503ServiceUnavailable); }
});

// Delete my account data. (The browser removes the sign-in itself first; the token stays valid for up to an hour.)
// The username stays reserved so nobody can pose as the old account.
app.MapDelete("/api/me", async (HttpContext ctx, FirebaseTokenVerifier auth, IUserStore users, IDocStore<Wishlist> store, TrackingGuard guard, ILogger<Program> log, CancellationToken ct) =>
{
    var me = await SignedIn(ctx, auth, guard, ct);
    if (me is null) return Deny(ctx);
    try
    {
        await store.DeleteAsync("u-" + me.Uid, ct);
        await users.DeleteAsync(me.Uid, ct);
        return Results.NoContent();
    }
    catch (Exception ex) { log.LogError(ex, "Could not delete account"); return Results.StatusCode(StatusCodes.Status503ServiceUnavailable); }
});

app.MapGet("/robots.txt", () => Results.Text(
    "User-agent: *\nDisallow: /api/\nDisallow: /preview/\nDisallow: /v1-magazine/\nDisallow: /v2-masonry/\nDisallow: /v3-compact/\n",
    "text/plain"));

// ---- Admin API (manage products without redeploying). Needs header X-Admin-Key. ----
var adminKey = (app.Configuration["Admin:ApiKey"] ?? "").Trim();
var idPattern = new Regex("^[a-z0-9][a-z0-9-]{0,63}$", RegexOptions.Compiled);

var admin = app.MapGroup("/api/admin");
admin.AddEndpointFilter(async (ctx, next) =>
{
    var given = ctx.HttpContext.Request.Headers["X-Admin-Key"].ToString();
    var ok = adminKey.Length > 0 &&
             CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(given), Encoding.UTF8.GetBytes(adminKey));
    if (!ok) return Results.Unauthorized();
    return await next(ctx);
});

admin.MapGet("/products", async (ProductCatalog catalog, CancellationToken ct) =>
    Results.Ok(await catalog.ListAllAsync(ct)));

admin.MapPut("/products/{id}", async (string id, Product p, ProductCatalog catalog, IHttpClientFactory http, CancellationToken ct) =>
{
    if (!idPattern.IsMatch(id)) return Results.BadRequest(new { error = "id: lowercase letters, digits and dashes only" });
    if (string.IsNullOrWhiteSpace(p.Title) || string.IsNullOrWhiteSpace(p.Category))
        return Results.BadRequest(new { error = "title and category are required" });
    if (string.IsNullOrWhiteSpace(p.Asin) && string.IsNullOrWhiteSpace(p.Url))
        return Results.BadRequest(new { error = "provide asin or url" });
    p.Id = id;
    p.Tags = Tagger.Clean(p.Tags);   // hand-typed tags only; the site ranks the rest itself
    if (!string.IsNullOrWhiteSpace(p.Url))
    {
        var checkedLink = await LinkGuard.EnsureAsync(p.Url!, site.AmazonTag, http.CreateClient("amazon"), ct);
        if (checkedLink.State == LinkState.NotAmazon) return Results.BadRequest(new { error = "url must be an Amazon link" });
        p.Url = checkedLink.Url;
    }
    // New products are dated today (UTC). Existing products keep their date unless the request sets one.
    if (string.IsNullOrWhiteSpace(p.AddedOn))
    {
        var existing = (await catalog.ListAllAsync(ct)).FirstOrDefault(x => x.Id == id);
        p.AddedOn = existing is not null ? existing.AddedOn : DateTime.UtcNow.ToString("yyyy-MM-dd");
    }
    await catalog.UpsertAsync(p, ct);
    return Results.Ok(p);
});

// Used by the Make.com automation: send the raw sheet row as form fields (title, url, image, feature1-3,
// optional category). The site builds the id, category, picture tile and popularity order itself.
admin.MapPost("/quick-add", async (HttpContext ctx, ProductCatalog catalog, IHttpClientFactory http, CancellationToken ct) =>
{
    if (!ctx.Request.HasFormContentType)
        return Results.BadRequest(new { error = "send form fields (application/x-www-form-urlencoded)" });
    var form = await ctx.Request.ReadFormAsync(ct);
    var p = QuickAdd.Build(form, out var error);
    if (p is null) return Results.BadRequest(new { error });

    // Every link must carry your store ID: checked (and rewritten if needed) before it is saved.
    var link = await LinkGuard.EnsureAsync(p.Url!, site.AmazonTag, http.CreateClient("amazon"), ct);
    if (link.State == LinkState.NotAmazon) return Results.BadRequest(new { error = "url must be an Amazon link" });
    p.Url = link.Url;

    // Posting the same row again updates it but keeps its category, order and date.
    var existing = (await catalog.ListAllAsync(ct)).FirstOrDefault(x => x.Id == p.Id);
    if (existing is not null)
    {
        p.Category = existing.Category;
        p.Emoji = existing.Emoji;
        p.Hue = existing.Hue;
        p.Order = existing.Order;
        if (string.IsNullOrWhiteSpace(form["tags"]) && existing.Tags is { Length: > 0 }) p.Tags = existing.Tags;
    }
    p.AddedOn = string.IsNullOrWhiteSpace(existing?.AddedOn) ? DateTime.UtcNow.ToString("yyyy-MM-dd") : existing!.AddedOn;
    await catalog.UpsertAsync(p, ct);
    return Results.Ok(new { p.Id, p.Category, p.Tags, p.Order, p.AddedOn, created = existing is null, url = AffiliateLinks.Build(p, site.AmazonTag), linkCheck = link.State.ToString() });
});

// Used by the "link intake" automation: turns a SiteStripe / short link (or a bare product id) into the
// product's ASIN and clean product URL, so the product details can be looked up.
admin.MapPost("/resolve", async (HttpContext ctx, IHttpClientFactory http, CancellationToken ct) =>
{
    var link = ctx.Request.HasFormContentType
        ? (await ctx.Request.ReadFormAsync(ct))["link"].ToString().Trim()
        : ctx.Request.Query["link"].ToString().Trim();
    if (link.Length == 0) return Results.BadRequest(new { error = "link is required" });

    string? asin;
    if (Regex.IsMatch(link, "^[A-Za-z0-9]{10}$"))
    {
        asin = link.ToUpperInvariant();
    }
    else
    {
        var final = await LinkGuard.ResolveFinalAsync(link, http.CreateClient("amazon"), ct);
        if (final is null) return Results.UnprocessableEntity(new { error = "could not open this link" });
        asin = LinkGuard.GetAsin(final);
    }
    if (asin is null) return Results.UnprocessableEntity(new { error = "no product id (ASIN) found in this link" });

    var productUrl = $"https://www.amazon.com/dp/{asin}";
    return Results.Ok(new { asin, productUrl, affiliateUrl = LinkGuard.SetTag(new Uri(productUrl), site.AmazonTag) });
});

// Checks every product link for your store ID. Add ?fix=true to rewrite wrong ones with your tag.
admin.MapGet("/audit-links", async (bool? fix, ProductCatalog catalog, IHttpClientFactory http, CancellationToken ct) =>
{
    var all = (await catalog.ListAllAsync(ct)).Where(p => !string.IsNullOrWhiteSpace(p.Url)).ToList();
    var client = http.CreateClient("amazon");
    var gate = new SemaphoreSlim(8);
    var results = await Task.WhenAll(all.Select(async p =>
    {
        await gate.WaitAsync(ct);
        try { return (p, r: await LinkGuard.EnsureAsync(p.Url!, site.AmazonTag, client, ct)); }
        finally { gate.Release(); }
    }));

    var changed = new List<object>();
    foreach (var (p, r) in results.Where(x => x.r.State != LinkState.Ok))
    {
        changed.Add(new { p.Id, state = r.State.ToString(), url = p.Url });
        if (fix == true && r.State == LinkState.Fixed)
        {
            p.Url = r.Url;
            await catalog.UpsertAsync(p, ct);
        }
    }
    return Results.Ok(new
    {
        tag = site.AmazonTag,
        total = results.Length,
        ok = results.Count(x => x.r.State == LinkState.Ok),
        fixedOrNeedsFix = results.Count(x => x.r.State == LinkState.Fixed),
        unverified = results.Count(x => x.r.State == LinkState.Unverified),
        notAmazon = results.Count(x => x.r.State == LinkState.NotAmazon),
        applied = fix == true,
        changed
    });
});

// ---- Curated lists ("Therapy Sessions") ----
admin.MapGet("/lists", async (ListCatalog lists, CancellationToken ct) => Results.Ok(await lists.ListAllAsync(ct)));

admin.MapPut("/lists/{id}", async (string id, CuratedList l, ListCatalog lists, CancellationToken ct) =>
{
    if (!idPattern.IsMatch(id)) return Results.BadRequest(new { error = "id: lowercase letters, digits and dashes only" });
    if (string.IsNullOrWhiteSpace(l.Title)) return Results.BadRequest(new { error = "title is required" });
    l.Id = id;
    l.Tags = Tagger.Clean(l.Tags);
    l.ProductIds = (l.ProductIds ?? Array.Empty<string>()).Select(x => x.Trim()).Where(x => x.Length > 0).Distinct().ToArray();
    l.Categories = (l.Categories ?? Array.Empty<string>()).Select(x => x.Trim()).Where(x => x.Length > 0).Distinct().ToArray();
    if (l.Limit <= 0) l.Limit = 12;
    await lists.UpsertAsync(l, ct);
    return Results.Ok(l);
});

admin.MapDelete("/lists/{id}", async (string id, ListCatalog lists, CancellationToken ct) =>
    await lists.DeleteAsync(id, ct) ? Results.NoContent() : Results.NotFound());

// ---- Report: what people save and click. ?days=30 (default) and ?limit=5000 (most events read; each one is a database read). ----
admin.MapGet("/stats", async (int? days, int? limit, ProductCatalog catalog, IEventStore events, IDocStore<Wishlist> wishlists, CancellationToken ct) =>
{
    var span = Math.Clamp(days ?? 30, 1, 365);
    var max = Math.Clamp(limit ?? 5000, 100, 50000);
    var since = DateTime.UtcNow.AddDays(-(span - 1)).ToString("yyyy-MM-dd");
    var log = await events.ListSinceAsync(since, max, ct);
    var lists = await wishlists.ListAsync(ct);
    var titles = (await catalog.ListAllAsync(ct)).ToDictionary(p => p.Id, p => p.Title);

    object Top(IEnumerable<IGrouping<string, string>> groups, string label) => groups
        .OrderByDescending(g => g.Count()).Take(15)
        .Select(g => new Dictionary<string, object> { ["id"] = g.Key, ["title"] = titles.GetValueOrDefault(g.Key, g.Key), [label] = g.Count() }).ToList();

    return Results.Ok(new
    {
        days = span,
        since,
        eventsCounted = log.Count,
        mayBeIncomplete = log.Count >= max,
        totals = new
        {
            saves = log.Count(e => e.Type == "wish_add"),
            removals = log.Count(e => e.Type == "wish_remove"),
            clicks = log.Count(e => e.Type == "click"),
            visitors = log.Select(e => e.VisitorId).Distinct().Count(),
        },
        savedNow = new
        {
            wishlists = lists.Count(w => w.ProductIds.Length > 0),
            items = lists.Sum(w => w.ProductIds.Length),
        },
        topSavedNow = Top(lists.SelectMany(w => w.ProductIds.Select(id => (w, id))).GroupBy(x => x.id, x => x.id), "saved"),
        topSavesInPeriod = Top(log.Where(e => e.Type == "wish_add").GroupBy(e => e.ProductId, e => e.ProductId), "saves"),
        topClicksInPeriod = Top(log.Where(e => e.Type == "click").GroupBy(e => e.ProductId, e => e.ProductId), "clicks"),
        perDay = log.GroupBy(e => e.Day).OrderBy(g => g.Key).Select(g => new
        {
            day = g.Key,
            saves = g.Count(e => e.Type == "wish_add"),
            removals = g.Count(e => e.Type == "wish_remove"),
            clicks = g.Count(e => e.Type == "click"),
        }),
    });
});

admin.MapDelete("/products/{id}", async (string id, ProductCatalog catalog, CancellationToken ct) =>
    await catalog.DeleteAsync(id, ct) ? Results.NoContent() : Results.NotFound());

app.Run();
