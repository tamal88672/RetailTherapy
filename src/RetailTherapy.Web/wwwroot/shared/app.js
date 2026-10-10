// Shared helpers used by every design. Plain script: works from file:// and any static host.
(function () {
  var S = {};
  var P = [];
  var RT = { site: S, products: P };

  // Loads site settings and products from the API, then runs cb. Every design starts from here.
  RT.ready = function (cb) {
    function get(u) { return fetch(u).then(function (r) { if (!r.ok) throw new Error(u + " " + r.status); return r.json(); }); }
    Promise.all([get("/api/site"), get("/api/products")]).then(function (res) {
      S = RT.site = res[0];
      P = RT.products = res[1];
      cb();
    }).catch(function () {
      document.body.insertAdjacentHTML("beforeend", '<p style="text-align:center;padding:48px 16px;font:16px system-ui">Could not load products. Please refresh.</p>');
    });
  };

  RT.rel = "sponsored nofollow noopener";

  RT.esc = function (s) {
    return String(s == null ? "" : s).replace(/[&<>"']/g, function (c) {
      return { "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c];
    });
  };

  // The server builds the direct Amazon link (with your tag) and sends it as p.url.
  RT.url = function (p) { return p.url; };

  // The "New" tab shows the most recent day's batch. Every product also keeps its normal category.
  RT.latestBatch = function () {
    var d = "";
    P.forEach(function (p) { if (p.addedOn && p.addedOn > d) d = p.addedOn; });
    return d;
  };

  RT.categories = function () {
    var seen = {}, out = [];
    if (RT.latestBatch()) { seen["New"] = 1; out.push("New"); }
    P.forEach(function (p) { if (!seen[p.category]) { seen[p.category] = 1; out.push(p.category); } });
    return out;
  };

  // Tags come from the server ranked by relevance (p.tags best first, p.tagScores 0-100 alongside).
  // A tag "counts" for a product when it is one of its top 3. Tiles show those three, best first.
  RT.TOP = 3;
  RT.tagsOf = function (p) { return (p.tags || []).slice(0, RT.TOP); };

  // How well a product fits a tag: its 0-100 score if the tag is in the product's top 3, otherwise -1.
  RT.tagFit = function (p, tag) {
    var t = p.tags || [], i = t.indexOf(tag);
    return i > -1 && i < RT.TOP ? (p.tagScores || [])[i] : -1;
  };
  // Looser fit for "also related": the tag is on the product but not in its top 3 (score, or -1).
  RT.tagRelated = function (p, tag) {
    var t = p.tags || [], i = t.indexOf(tag);
    return i >= RT.TOP ? (p.tagScores || [])[i] : -1;
  };

  RT.byId = function (id) {
    for (var i = 0; i < P.length; i++) if (P[i].id === id) return P[i];
    return null;
  };

  function catOk(p, cat, latest) {
    return !cat || cat === "All" || (cat === "New" ? (latest && p.addedOn === latest) : p.category === cat);
  }
  function textOk(p, q) {
    return !q || (p.title + " " + p.blurb + " " + p.category + " " + (p.tags || []).join(" ")).toLowerCase().indexOf(q) > -1;
  }

  // cat: a category chip ("All", "New" or a category name). q: search text (title, blurb, category, tags).
  // tag: when set, only products with that tag in their top 3, the best fit first.
  RT.filter = function (cat, q, tag) {
    q = (q || "").trim().toLowerCase();
    var latest = cat === "New" ? RT.latestBatch() : "";
    var out = P.filter(function (p) { return catOk(p, cat, latest) && textOk(p, q) && (!tag || RT.tagFit(p, tag) > -1); });
    if (tag) out.sort(function (a, b) { return RT.tagFit(b, tag) - RT.tagFit(a, tag); });   // stable: ties keep popularity order
    return out;
  };

  // "Also related": products that carry the tag but not in their top 3, best fit first. Only used to fill up a short
  // result list, so a rare tag still shows something useful (up to `fill` products in total).
  RT.related = function (cat, q, tag, have, fill) {
    if (!tag || have >= fill) return [];
    q = (q || "").trim().toLowerCase();
    var latest = cat === "New" ? RT.latestBatch() : "";
    var out = P.filter(function (p) { return catOk(p, cat, latest) && textOk(p, q) && RT.tagRelated(p, tag) > -1; });
    out.sort(function (a, b) { return RT.tagRelated(b, tag) - RT.tagRelated(a, tag); });
    return out.slice(0, fill - have);
  };

  // Product picture: real image if provided, otherwise a colored emoji tile.
  // extra: optional HTML placed on top of the picture (a badge, the heart).
  RT.art = function (p, cls, extraStyle, extra) {
    var inner = p.image
      ? '<img src="' + RT.esc(p.image) + '" alt="' + RT.esc(p.title) + '" data-emoji="' + RT.esc(p.emoji) + '" loading="lazy" referrerpolicy="no-referrer">'
      : '<span aria-hidden="true">' + p.emoji + "</span>";
    return '<div class="art ' + (cls || "") + '" style="--h:' + p.hue + ";" + (extraStyle || "") + '">' + inner + (extra || "") + "</div>";
  };

  // If a picture fails to load (dead link), fall back to the emoji tile.
  document.addEventListener("error", function (e) {
    var t = e.target;
    if (t && t.tagName === "IMG" && t.parentNode && t.parentNode.classList.contains("art")) {
      var s = document.createElement("span");
      s.setAttribute("aria-hidden", "true");
      s.textContent = t.getAttribute("data-emoji") || "🛍️";
      t.parentNode.replaceChild(s, t);
    }
  }, true);

  RT.cta = function (p, label, cls) {
    return '<a class="cta ' + (cls || "") + '" href="' + RT.esc(RT.url(p)) + '" target="_blank" rel="' + RT.rel + '">' +
      RT.esc(label || "See on Amazon") + "</a>";
  };

  // One product tile for the masonry design (used on the home page and the sessions page).
  // .tile is the fixed hover area; .card inside it is what pops up (see style.css).
  RT.tile = function (p, ratio) {
    var badge = p.badge ? '<span class="tag">' + RT.esc(p.badge) + "</span>" : "";
    var heart = RT.heart ? RT.heart(p) : "";
    var scores = p.tagScores || [];
    var tags = RT.tagsOf(p).map(function (t, i) {
      return '<button type="button" class="hash' + (i === 0 ? " top" : "") + '" data-tag="' + RT.esc(t) + '" title="' + RT.esc(t) + " &middot; " + (scores[i] || 0) +
        '% match" aria-label="Show finds tagged ' + RT.esc(t) + '">#' + RT.esc(t) + "</button>";
    }).join("");
    return '<article class="tile" data-id="' + RT.esc(p.id) + '"><div class="card">' +
      RT.art(p, "", "--r:" + (ratio || "1/1"), badge + heart) +
      '<div class="body"><h3>' + RT.esc(p.title) + "</h3><p>" + RT.esc(p.blurb) + "</p>" +
      RT.cta(p, "See on Amazon") +
      (tags ? '<div class="hashes">' + tags + "</div>" : "") +
      "</div></div></article>";
  };

  // Ads: fixed reserved size (no layout shift), clearly labelled, no popups or overlays.
  RT.mountAds = function () {
    var A = S.ads || {};
    var live = A.enabled && A.client && A.client.indexOf("XXXX") === -1;
    if (live && !window.__rtAdsScript) {
      var s = document.createElement("script");
      s.async = true; s.crossOrigin = "anonymous";
      s.src = "https://pagead2.googlesyndication.com/pagead/js/adsbygoogle.js?client=" + encodeURIComponent(A.client);
      document.head.appendChild(s);
      window.__rtAdsScript = true;
    }
    var slots = document.querySelectorAll("[data-ad]");
    Array.prototype.forEach.call(slots, function (el) {
      if (el.getAttribute("data-mounted")) return;
      if (el.offsetParent === null) return; // hidden at this screen size: mount later if it becomes visible
      el.setAttribute("data-mounted", "1");
      var id = (A.slots || {})[el.getAttribute("data-ad")];
      var label = '<span class="ad-label">Advertisement</span>';
      if (live && id) {
        el.innerHTML = label + '<ins class="adsbygoogle" style="display:block" data-ad-client="' + RT.esc(A.client) +
          '" data-ad-slot="' + RT.esc(id) + '" data-ad-format="auto" data-full-width-responsive="true"></ins>';
        try { (window.adsbygoogle = window.adsbygoogle || []).push({}); } catch (e) {}
      } else if (A.enabled !== false) {
        // Until real ads run, this box invites sponsors. The email link shows only once a real contact address is set.
        var mail = S.contactEmail || "";
        var link = mail && mail.indexOf("example.com") === -1
          ? '<a href="mailto:' + RT.esc(mail) + '?subject=' + encodeURIComponent("Advertising on " + (S.name || "the site")) + '">Advertise with us</a>'
          : "";
        el.innerHTML = label + '<div class="ad-ph pitch"><span><b>Advertise here</b>' +
          "Reach shoppers hunting for beauty, kitchen and book finds." + link + "</span></div>";
      }
    });
  };

  RT.fillChrome = function () {
    document.title = (S.name || "") + " | " + (S.tagline || "");
    var map = { "[data-site-name]": S.name, "[data-site-tag]": S.tagline, "[data-disclosure]": S.disclosure,
      "[data-lists-name]": S.listsName || "Therapy Sessions", "[data-lists-tagline]": S.listsTagline };
    Object.keys(map).forEach(function (sel) {
      Array.prototype.forEach.call(document.querySelectorAll(sel), function (n) { n.textContent = map[sel]; });
    });
    Array.prototype.forEach.call(document.querySelectorAll("[data-year]"), function (n) { n.textContent = new Date().getFullYear(); });
  };

  var t;
  window.addEventListener("resize", function () { clearTimeout(t); t = setTimeout(RT.mountAds, 200); });

  window.RT = RT;
})();
