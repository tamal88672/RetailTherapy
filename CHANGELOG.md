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
