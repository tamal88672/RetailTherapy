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
builder.Services.AddMemoryCache();
builder.Services.AddResponseCompression();
builder.Services.AddSingleton<ProductCatalog>();
// Used to open Amazon short links once (without following them into Amazon) to read the store ID they carry.
builder.Services.AddHttpClient("amazon").ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });

// Storage: "Firestore" in the cloud, "Json" (a local file) for development.
if (string.Equals(builder.Configuration["Storage:Provider"], "Firestore", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddFirestoreProductStore(builder.Configuration);
}
else
{
    var file = Path.Combine(builder.Environment.ContentRootPath, "App_Data", "products.json");
    builder.Services.AddSingleton<IProductStore>(_ => new JsonFileProductStore(file));
}

var app = builder.Build();

// First run: load the starter products if the store is empty.
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
        app.Logger.LogError(ex, "Seeding failed; continuing without it.");
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

app.MapGet("/api/site", (HttpContext ctx, IOptions<SiteOptions> o) =>
{
    var s = o.Value;
    ctx.Response.Headers.CacheControl = "public,max-age=300";
    return Results.Ok(new
    {
        s.Name,
        s.Tagline,
        s.Disclosure,
        s.ContactEmail,
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
    }
    p.AddedOn = string.IsNullOrWhiteSpace(existing?.AddedOn) ? DateTime.UtcNow.ToString("yyyy-MM-dd") : existing!.AddedOn;
    await catalog.UpsertAsync(p, ct);
    return Results.Ok(new { p.Id, p.Category, p.Order, p.AddedOn, created = existing is null, url = AffiliateLinks.Build(p, site.AmazonTag), linkCheck = link.State.ToString() });
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

admin.MapDelete("/products/{id}", async (string id, ProductCatalog catalog, CancellationToken ct) =>
    await catalog.DeleteAsync(id, ct) ? Results.NoContent() : Results.NotFound());

app.Run();
