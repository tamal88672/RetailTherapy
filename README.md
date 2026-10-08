# Retail Therapy: Amazon affiliate site

ASP.NET Core minimal API + plain JavaScript front end. Three designs, one ad rail, Firestore for data, Cloud Run for hosting.

```
src/RetailTherapy.Web/
  Program.cs               API, static files, admin endpoints
  Data/                    IProductStore, Firestore store, JSON store (local), ProductCatalog (memory cache)
  Models/                  Product, ProductDto, SiteOptions
  Seed/products.json       starter products (loaded into an empty store on first start)
  wwwroot/
    v1-magazine/ v2-masonry/ v3-compact/   the three designs
    shared/                app.js (loads /api data, ad mounting), ads.css
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
