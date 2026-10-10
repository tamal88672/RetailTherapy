# Retail Therapy: Amazon affiliate site

ASP.NET Core minimal API + plain JavaScript front end. Three designs, one ad rail, Firestore for data, Cloud Run for hosting.

```
src/RetailTherapy.Web/
  Program.cs               API, static files, admin endpoints
  Data/                    product stores (Firestore, JSON for local), ProductCatalog (memory cache), Tagger, ListCatalog,
                           DocStore (lists + wishlists), EventStore (saves/clicks), TrackingGuard
  Models/                  Product, ProductDto, CuratedList, Wishlist, TrackEvent, SiteOptions
  Seed/                    products.json and lists.json (starters, loaded into an empty store on first start)
  wwwroot/
    v1-magazine/ v2-masonry/ v3-compact/   the three designs
    v2-masonry/sessions.html  the curated-lists page, served at /sessions
    shared/                app.js (data, tiles, ads), wish.js (hearts + tracking), theme.js (day/night), ads.css
    preview/               side-by-side chooser at /preview/
    legal.html             affiliate disclosure + privacy template
Dockerfile, deploy.sh      Cloud Run
```

## Run locally
```
dotnet run --project src/RetailTherapy.Web
```
Open the URL it prints. `/` is the live design, `/preview/` shows all three. Local data lives in `App_Data/products.json` (no cloud needed). Targets `net10.0`; for an older SDK change `TargetFramework` in the csproj.

## How it is fast
- Products are cached in memory (default 10 min, `Storage:CacheMinutes`). Firestore is read only when the cache is empty or expired, one request at a time.
- `/api/products` sends an ETag (browsers get a 304), gzip, and `Cache-Control`.
- Admin edits clear the cache on that instance. Other instances pick up changes within the cache time.

## Deploy to Google Cloud (free tier)
In Cloud Shell, with billing enabled on the project:
```
PROJECT=my-project AMAZON_TAG=mytag-20 ./deploy.sh
```
This enables the APIs, creates Firestore and an admin key secret, builds the container, and deploys to Cloud Run (`min-instances 0`, `max-instances 2` to cap cost). Cloud Run, Firestore, Secret Manager, Cloud Build and Artifact Registry all have always-free tiers that a small site stays inside. Set a budget alert in Billing anyway, and check current limits on the pricing pages.

First request after idle is a cold start (about a second). Set `--min-instances 1` for none, which costs a little.

## Settings (env vars on Cloud Run, or appsettings.json)
| Setting | Meaning |
|---|---|
| `Site__AmazonTag` | your Associates tracking ID |
| `Site__LiveDesign` | `v1-magazine`, `v2-masonry` or `v3-compact` |
| `Site__Ads__Client` | AdSense publisher ID (placeholders show while it contains `XXXX`) |
| `Site__Ads__SlotSidebar` / `SlotInline` | AdSense ad unit IDs |
| `Site__ContactEmail`, `Site__Name`, `Site__Tagline` | text on the site |
| `Site__ListsName`, `Site__ListsTagline` | what the curated-lists page is called ("Therapy Sessions") and its subtitle |
| `Admin__ApiKey` | key for the admin API (a Secret Manager secret in deploy.sh) |

## Manage products (no redeploy)
```
KEY=$(gcloud secrets versions access latest --secret=retail-admin-key)
URL=https://your-service-url

# add or update (id goes in the path; give asin or url)
curl -X PUT "$URL/api/admin/products/vitamin-c-serum" -H "X-Admin-Key: $KEY" -H "Content-Type: application/json" \
  -d '{"title":"Vitamin C Serum","category":"Beauty","asin":"B0XXXXXXXX","emoji":"🍊","hue":30,"blurb":"Daily glow.","pros":["Lightweight"],"order":1}'

curl "$URL/api/admin/products" -H "X-Admin-Key: $KEY"            # list all
curl -X DELETE "$URL/api/admin/products/vitamin-c-serum" -H "X-Admin-Key: $KEY"
```
**New tab:** every product has an `addedOn` date (yyyy-MM-dd). A product added through the admin API is dated today unless you pass `"addedOn"`. The New tab shows the latest day's batch. Products are categorized when they are added, so after that day they simply drop out of New and stay in their category. **Sorting:** the site lists products by `order` (lowest first), so ranking by popularity means setting `order`.

Set `"active": false` to hide a product without deleting it. `"image"` takes any image URL (Cloudinary works well; a dead link falls back to the emoji tile). `"url"` takes a SiteStripe link (amzn.to / a.co links are opened once when saved to check they carry your tag; if not, they are replaced by the full product link with your tag). A Make.com scenario can call the same PUT endpoint.

## Tags, Therapy Sessions, saved items and the report
**Tags.** Every product shows its top three `#tags` under it, most relevant first (hover one to see its match score). The site ranks tags itself (`Data/TagRanker.cs`):
1. Candidates are theme tags (gift idea, viral, cozy... listed in `Data/Tagger.cs`) and product types (serum, air fryer, tumbler...).
2. A word counts more where it matters more: title 3, blurb 2, feature lines 1.5, badge 1.
3. A tag found on few products is worth more than one found on nearly all (rarity weight, as search engines do).
4. Scores are put on a 0 to 100 scale. Tags under 22 are dropped; tags that repeat another (fryer / air fryer) are merged.
5. Tags you type yourself (the `tags` field) always rank first.

Clicking a tag shows the products where it is in the **top three**, best fit first. If that gives fewer than 8, the list is topped up under "Also related" with products that carry the tag further down. Sessions lists use the same top-three rule. Ranking is redone whenever the catalog refreshes, so it adapts as products are added. Make.com can send an optional `tags` field to `quick-add`; leave it out and the site does the ranking. `GET /api/products` returns `tags` (best first) and `tagScores` (0-100) for each product.

**Therapy Sessions** (`/sessions`) is the curated-lists page. Each list has a title, blurb, emoji and a set of products: ones you pin by id (`productIds`, in order), then every product matching the rule (`categories` and/or `tags`, most popular first) up to `limit`. Eight starter lists load on first start. Rename the page with `Site__ListsName`.
```
curl "$URL/api/admin/lists" -H "X-Admin-Key: $KEY"
curl -X PUT "$URL/api/admin/lists/mothers-day" -H "X-Admin-Key: $KEY" -H "Content-Type: application/json" \
  -d '{"title":"Mother'"'"'s Day","emoji":"💐","hue":340,"blurb":"For her.","order":9,"productIds":["vitamin-c-serum"],"tags":["gift idea"],"categories":["Skincare"],"limit":10}'
curl -X DELETE "$URL/api/admin/lists/mothers-day" -H "X-Admin-Key: $KEY"
```
Set `"active": false` to hide a list. Public: `GET /api/lists` (ids only; the page joins them with `/api/products`).

**Saved items (heart).** Each product has a heart. The list is kept in the visitor's browser and shown by the heart button in the top bar. Every save, removal and click-through to Amazon is sent to `POST /api/track` with a random anonymous visitor id (no name or email); the list itself is also copied to the server (`PUT /api/wishlist`). Collections: `wishlists` (one document per visitor, with `ownerType`, `ownerId`, `visibility` and `shareCode` ready for accounts and sharing lists between users) and `events`.

**Report.** `GET /api/admin/stats?days=30` returns saves, removals, click-throughs, visitors, the most saved products (now and in the period), the most clicked, and a per-day count. Each event read is one database read, so it reads at most `limit` events (default 5000).
```
curl "$URL/api/admin/stats?days=7" -H "X-Admin-Key: $KEY"
```

## Ads that don't get in the way
- Desktop: sticky 300x600 side rail that never overlays content.
- Mobile: one 300x250 slot after the products, never above the fold.
- Slots reserve their size, so the page does not jump when an ad loads.
- In AdSense, turn **off** Auto ads formats that block the page (anchor, vignette, side rails). Keep only your manual units.

## Amazon rules to remember
- Keep the disclosure visible (header and footer already have it).
- Do not show prices unless pulled live from Amazon's API. This site avoids prices on purpose.
- Every Amazon link is forced to carry your tag: plain amazon.com links always get it, short links are checked when added (also `GET /api/admin/audit-links`, add `?fix=true` to repair). Links go straight to Amazon with your tag (no redirects or cloaking) and use `rel="sponsored nofollow noopener"`.
- Associates needs qualifying sales within 180 days of signup to stay active.
- `legal.html` is template text. Edit it to match how you run the site.
