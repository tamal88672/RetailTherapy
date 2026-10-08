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

admin.MapPut("/products/{id}", async (string id, Product p, ProductCatalog catalog, CancellationToken ct) =>
{
    if (!idPattern.IsMatch(id)) return Results.BadRequest(new { error = "id: lowercase letters, digits and dashes only" });
    if (string.IsNullOrWhiteSpace(p.Title) || string.IsNullOrWhiteSpace(p.Category))
        return Results.BadRequest(new { error = "title and category are required" });
    if (string.IsNullOrWhiteSpace(p.Asin) && string.IsNullOrWhiteSpace(p.Url))
        return Results.BadRequest(new { error = "provide asin or url" });
    p.Id = id;
    // New products are dated today (UTC). Existing products keep their date unless the request sets one.
    if (string.IsNullOrWhiteSpace(p.AddedOn))
    {
        var existing = (await catalog.ListAllAsync(ct)).FirstOrDefault(x => x.Id == id);
        p.AddedOn = existing is not null ? existing.AddedOn : DateTime.UtcNow.ToString("yyyy-MM-dd");
    }
    await catalog.UpsertAsync(p, ct);
    return Results.Ok(p);
});

admin.MapDelete("/products/{id}", async (string id, ProductCatalog catalog, CancellationToken ct) =>
    await catalog.DeleteAsync(id, ct) ? Results.NoContent() : Results.NotFound());

app.Run();
