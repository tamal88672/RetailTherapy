# Changelog

All notable changes to Retail Therapy (retailtherapy.com) are listed here, newest first.
Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/). Versions follow [Semantic Versioning](https://semver.org/): `MAJOR.MINOR.PATCH`.

## How we version

| Number | Goes up when | Example |
|---|---|---|
| **MAJOR** (2.0.0) | A redesign of the whole site, or a change that breaks the admin API or the stored product data | New site design replaces the old one |
| **MINOR** (1.6.0) | A new feature or visible improvement that works with what already exists | New tab, new theme, new admin endpoint |
| **PATCH** (1.5.1) | A fix or a small tweak with no new feature | Wrong color, broken link, typo |

Every deploy gets an entry here before it goes out. Group the lines under **Added**, **Changed**, **Fixed** or **Removed**.
Changes made outside the code (Make.com automations, product data) are listed at the bottom, with dates.

## [1.8.0] - 2026-10-10 (code pushed, waiting to be deployed with 1.5.0, 1.6.0 and 1.7.0)

### Added
- **Sign-in with Google or an email link.** No password, no phone/SMS, no Apple (those cost money or need a paid developer account). Firebase allows only 5 email-link emails a day on its free no-billing plan, so the project must be on the Blaze plan (the README explains; it still costs about $0). A "Sign in" button sits in the top bar (home and Sessions pages). It stays hidden until `Auth__ProjectId` and `Auth__ApiKey` are set, so the site works exactly as before until you finish the Firebase setup in the README.
- **One-time username.** After the first sign-in the person picks a unique username (3 to 20 characters). It can never be changed. The check happens as they type, and the claim is a single database transaction, so two people can never get the same name. Reserved words (admin, support...) and offensive names are refused. "Not now" is allowed; the account menu offers it again.
- **Saved list follows the account.** Items saved before signing in join the account's list; after that the list is the same on every device. Signing out leaves the browser clean. The old anonymous list is kept for the report and marked `claimedBy`.
- Account menu: username, sign out, **delete account** (removes lists and sign-in; the username stays reserved). Privacy text updated.
- API: `GET /api/me`, `GET /api/me/username/available`, `POST /api/me/username`, `GET|PUT /api/me/wishlist`, `POST /api/me/claim`, `DELETE /api/me`. Sign-in tokens are checked on the server (signature, project, issuer, expiry) with no new package. `GET /api/site` gains an `auth` block. New collections: `users`, `usernames`.
- Phone top bar: the Day / Auto / Night switch folds into one button (tap to cycle) to make room for the account button.

### Changed
- `deploy.sh` now uses `--update-env-vars`, so settings added later (the `Auth__*` sign-in values, `Site__ListsName`...) are kept when you deploy again.
- "No sign-up, no email" wording replaced: signing in is optional.
- Provisions for 1.9 (several lists, sharing, public profiles) and 1.10 (recommendations) are in place: the account list is a normal `wishlists` document with `ownerType: user`.

## [1.7.0] - 2026-10-10 (code pushed, waiting to be deployed with 1.5.0 and 1.6.0)

### Added
- **Ranked tags.** Every tag now has a relevance score (0 to 100) for each product. The site scores them with a TF-IDF method: a word in the title counts more than one in a feature line, and a tag found on few products counts more than one found on almost all of them. Product types (serum, air fryer, tumbler...) are tags too, so a tag leads to genuinely similar products.
- Tiles show each product's **top 3 tags, best first**. Hovering a tag shows its match score.
- **Tag pages follow a top-3 rule.** Clicking a tag lists the products where it is in the top 3, best fit first. A short list is topped up with an "Also related" group (the tag ranks lower on those products).
- Tags that repeat each other are merged (fryer / air fryer, patch / pimple patch), and "fragrance-free" is not read as a fragrance.
- API: `GET /api/products` includes `tags` (best first) and `tagScores`.

### Changed
- Sessions lists use the same top-3 rule and show the best tag fit first.
- Tags typed by hand (on `quick-add` or the product) always rank first. Without them, the site does the ranking, so nothing is saved for you.
- Search matches all of a product's ranked tags.

### Removed
- `POST /api/admin/retag`: tags are ranked automatically every time the catalog refreshes.

## [1.6.0] - 2026-10-09 (code pushed, waiting to be deployed with 1.5.0)

### Added
- **Therapy Sessions page** at `/sessions`: curated lists, each a themed shelf of products (title, blurb, emoji, product tiles). The name is a setting (`Site__ListsName`), so renaming it never touches the code. Eight starter lists: Glow-Up Session, Cozy Night In, Gift Shelf, Going Viral, Page-Turner Club, Kitchen Wins, Summer Ready, Pack & Go. A list can pin products by hand and/or pull in every product that matches a category or tag.
- **Tags under every product** (for example `#gift idea`, `#viral`, `#cozy`). Click one to filter the page by it. Products without saved tags get them worked out from their title and feature lines, so every tile has some from day one. Make.com can send an optional `tags` field.
- **Wishlist heart** on every product picture (works in Day and Night, with a small pop when saved). A heart button with a count in the top bar opens "Your saved finds". Saved items stay in the visitor's browser and are copied to the server.
- **Tracking:** every save, removal and click-through to Amazon is recorded with a random anonymous visitor id (no name or email). Rate limits keep it cheap and hard to abuse.
- **Ready for accounts and shared lists:** saved lists are stored as wishlist documents with an owner type and id (anonymous visitor today, user later), a visibility setting and a share code. Nothing else needs to change for a signed-in user to claim a visitor's list.
- Admin endpoints: `GET /api/admin/stats` (report of saves, removals, clicks, most-saved products, per-day counts), `GET/PUT/DELETE /api/admin/lists`, `POST /api/admin/retag`.
- Public endpoints: `GET /api/lists`, `POST /api/track`, `PUT /api/wishlist`, `GET /api/wishlist/{visitorId}`.
- Disclosure and Privacy page explains the saved list and the anonymous counts.

### Changed
- The top bar has a heart button next to the Day / Auto / Night switch, and the category row starts with a link to the Sessions page.
- The category row is a plain group of buttons (it was marked as tabs, which was not accurate).
- Search also looks at tags.
- Products now carry a `tags` field. Older products without it keep working.
- Night sparkles on pictures moved to the bottom-right corner to make room for the heart.

## [1.5.0] - 2026-10-09 (code pushed, waiting to be deployed)

### Added
- **Pop-out tiles.** The tile under the mouse lifts and grows. Moving into the small gaps between tiles puts every tile back down. Touch screens get a quick press effect instead.
- **Day / Auto / Night switch** in the top bar. The default follows the device setting. The choice is remembered in the browser and applied before the page paints, so there is no white flash.
- **Night theme.** Deep violet with pink and neon glow, twinkling stars, glitter, and sparkles on the product pictures. Picture tiles without a photo stay in the violet and pink range.
- The Disclosure and Privacy page follows the chosen theme.
- Respects "reduce motion": no lifting or twinkling for people who turn it off.

### Changed
- Colors on the live design now come from a single set of Day and Night color values instead of fixed colors.
- Each product is wrapped in a fixed hover area so the lifted card cannot flicker.

## [1.4.0] - 2026-10-08

### Added
- Admin endpoint `POST /api/admin/resolve`: turns a SiteStripe or short link (or a bare product ID) into the product ID and clean product link. Used by the planned link-intake automation.

## [1.3.0] - 2026-10-08

### Added
- "Why bookmark Retail Therapy?" panel (left column on wide screens, collapsed box on phones).
- Site icon and favicon files for browsers and phones.
- Check for `link.amazon` links.

### Changed
- **Every Amazon link now carries the store ID** (`tamoramo-20`). Links with a missing or different ID are corrected before they are saved.

## [1.2.0] - 2026-10-08

### Added
- Admin endpoint `POST /api/admin/quick-add` for the Make.com automation. The site builds the product ID, category, picture tile and popularity order itself.
- "Advertise here" box in the side ad rail.
- "New" tab (products carry the date they were added) and popularity ordering (reviews, ratings, buyers, "best seller").

### Changed
- Short links no longer need a tag added by hand.

## [1.1.0] - 2026-10-08

### Changed
- SiteStripe short links are used as given.
- Category chips wrap onto more lines on desktop.

### Fixed
- If a product picture is a dead link, the site shows the emoji tile instead of a broken image.
- The admin key is trimmed, and the key secret is written without a trailing newline.

## [1.0.0] - 2026-10-08

First public version.

### Added
- Affiliate catalog site on .NET with product tiles, search and category chips.
- Runs on Google Cloud Run (free tier) with Firestore for products and an admin API protected by a key.
- Three designs (magazine, masonry, compact). Masonry is live.
- Side ad rail that never covers or blocks the page, plus Disclosure and Privacy page.
- Site name shown as RetailTherapy.com.

### Fixed
- Docker build and deploy script issues found at first deploy.

---

## Outside the code (automations and content)

These do not change the version number. They are listed so the history is complete. Dates are US Central time.

| Date | Change |
|---|---|
| 2026-10-09 | Website Publisher email now sends **one email with all products** instead of one email per product (92 emails on the first scheduled run). |
| 2026-10-08 | New Make scenario "Planner Filler": fills the Daily Pin Planner from your own links (Status `new`) and adds 3 Amazon best sellers a day. Off until the Apify token and admin key are pasted in. |
| 2026-10-08 | 9 old Make scenarios moved to the folder "Old scenarios - delete me" (still to be deleted by hand). |
| 2026-10-08 | Pinterest step and Pinterest links removed from the publisher. Scenario renamed "Retail Therapy → Website Publisher". |
| 2026-10-08 | 3 products with dead `link.amazon` links hidden from the site (not deleted). |
| 2026-10-08 | Website Publisher scenario created: reads rows with Status `ready` from the Daily Pin Planner, adds them to the site, marks them Complete, runs daily at 9:00 AM Central. |
