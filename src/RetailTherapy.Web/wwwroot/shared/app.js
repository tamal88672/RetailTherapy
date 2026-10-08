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

  RT.categories = function () {
    var seen = {}, out = [];
    P.forEach(function (p) { if (!seen[p.category]) { seen[p.category] = 1; out.push(p.category); } });
    return out;
  };

  RT.filter = function (cat, q) {
    q = (q || "").trim().toLowerCase();
    return P.filter(function (p) {
      var okCat = !cat || cat === "All" || p.category === cat;
      var okQ = !q || (p.title + " " + p.blurb + " " + p.category).toLowerCase().indexOf(q) > -1;
      return okCat && okQ;
    });
  };

  // Product picture: real image if provided, otherwise a colored emoji tile.
  RT.art = function (p, cls, extraStyle) {
    var inner = p.image
      ? '<img src="' + RT.esc(p.image) + '" alt="' + RT.esc(p.title) + '" data-emoji="' + RT.esc(p.emoji) + '" loading="lazy" referrerpolicy="no-referrer">'
      : '<span aria-hidden="true">' + p.emoji + "</span>";
    return '<div class="art ' + (cls || "") + '" style="--h:' + p.hue + ";" + (extraStyle || "") + '">' + inner + "</div>";
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
        el.innerHTML = label + '<div class="ad-ph"><span>Ad space<small>' +
          (el.className.indexOf("tall") > -1 ? "300 × 600" : "300 × 250") + "</small></span></div>";
      }
    });
  };

  RT.fillChrome = function () {
    document.title = (S.name || "") + " | " + (S.tagline || "");
    var map = { "[data-site-name]": S.name, "[data-site-tag]": S.tagline, "[data-disclosure]": S.disclosure };
    Object.keys(map).forEach(function (sel) {
      Array.prototype.forEach.call(document.querySelectorAll(sel), function (n) { n.textContent = map[sel]; });
    });
    Array.prototype.forEach.call(document.querySelectorAll("[data-year]"), function (n) { n.textContent = new Date().getFullYear(); });
  };

  var t;
  window.addEventListener("resize", function () { clearTimeout(t); t = setTimeout(RT.mountAds, 200); });

  window.RT = RT;
})();
