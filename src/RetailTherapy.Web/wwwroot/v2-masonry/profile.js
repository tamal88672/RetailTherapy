// A shared list (/u/{username}/{code}) or a person's profile (/u/{username}) with their public lists.
// Only lists the owner chose to share are ever sent here by the server, and never notes or "bought" ticks.
RT.ready(function () {
  var host = document.getElementById("shared");
  var parts = location.pathname.split("/").filter(Boolean);        // ["u", name, code?]
  var name = "", code = "";
  try { name = decodeURIComponent(parts[1] || "").toLowerCase(); code = decodeURIComponent(parts[2] || "").toLowerCase(); } catch (e) {}
  var ratios = ["1/1", "4/5", "3/4", "5/6", "2/3", "1/1", "4/5"];
  var esc = RT.esc;
  RT.fillChrome();

  function get(u) {
    return fetch(u).then(function (r) {
      if (r.status === 404) return null;
      if (!r.ok) throw new Error(u + " " + r.status);
      return r.json();
    });
  }
  function plural(n, one, many) { return n + " " + (n === 1 ? one : many); }

  function gone() {
    document.title = "Not available | " + (RT.site.name || "");
    host.innerHTML = '<section class="intro"><h1>This page isn&rsquo;t available</h1>' +
      "<p>The list may be private, or the link may be wrong.</p></section>" +
      '<p class="shared-foot"><a class="pill linkpill" href="/">Browse all finds</a></p>';
  }
  function broken() { host.innerHTML = '<p class="empty">Could not load this page. Please refresh.</p>'; }

  function profile(d) {
    var since = "";
    try { since = new Date(d.since + "T00:00:00Z").toLocaleDateString(undefined, { month: "long", year: "numeric", timeZone: "UTC" }); } catch (e) {}
    document.title = "@" + d.username + " | " + (RT.site.name || "");
    var cards = d.lists.map(function (l) {
      var products = l.productIds.map(RT.byId).filter(Boolean);
      if (!products.length) return "";
      return '<a class="pcard" href="/u/' + esc(d.username) + "/" + esc(l.id) + '"><div class="pthumbs">' +
        products.slice(0, 4).map(function (p) { return RT.art(p, "th", "--r:1/1"); }).join("") + "</div>" +
        "<h2>" + esc(l.name) + "</h2><p>" + plural(products.length, "find", "finds") + "</p></a>";
    }).join("");
    host.innerHTML = '<section class="intro"><p class="kicker">Profile</p><h1>@' + esc(d.username) + "</h1>" +
      "<p>" + (since ? "Here since " + esc(since) + ". " : "") + "Lists they&rsquo;ve chosen to share.</p></section>" +
      (cards ? '<div class="pcards">' + cards + "</div>" : '<p class="empty">No public lists yet.</p>') +
      '<p class="shared-foot"><a href="/">Browse all finds</a></p>';
    RT.mountAds();
  }

  function listPage(d) {
    var l = d.list;
    var products = l.productIds.map(RT.byId).filter(Boolean);
    document.title = l.name + " | " + (RT.site.name || "");
    host.innerHTML = '<section class="intro"><p class="kicker">Shared by <a href="/u/' + esc(d.username) + '">@' + esc(d.username) + "</a></p>" +
      "<h1>" + esc(l.name) + "</h1><p>" + plural(products.length, "find", "finds") + "." +
      ' Tap the heart on anything to save it to your own list.</p>' +
      '<p class="shared-actions"><button type="button" class="pill" data-copy>Copy link</button></p></section>' +
      (products.length
        ? '<section class="session" data-source="shared"><div class="grid">' +
          products.map(function (p, i) { return RT.tile(p, ratios[i % ratios.length]); }).join("") + "</div></section>"
        : '<p class="empty">Nothing in this list right now.</p>') +
      '<p class="shared-foot">Want your own? <a href="/#saved">Save finds with the heart</a> and sign in to make lists you can share.</p>';
    RT.wish.paint();
    RT.mountAds();
  }

  if (!/^[a-z][a-z0-9_]{2,19}$/.test(name) || (code && !/^[a-f0-9]{12}$/.test(code))) return gone();
  get(code ? "/api/u/" + encodeURIComponent(name) + "/" + encodeURIComponent(code) : "/api/u/" + encodeURIComponent(name))
    .then(function (d) { if (!d) return gone(); if (code) listPage(d); else profile(d); })
    .catch(broken);

  document.addEventListener("click", function (e) {
    var h = e.target.closest(".hash[data-tag]");
    if (h) { location.href = "/?tag=" + encodeURIComponent(h.getAttribute("data-tag")); return; }
    var c = e.target.closest("[data-copy]");
    if (!c) return;
    var done = function () { c.textContent = "Copied!"; setTimeout(function () { c.textContent = "Copy link"; }, 1800); };
    if (navigator.clipboard && navigator.clipboard.writeText) navigator.clipboard.writeText(location.href).then(done, function () { window.prompt("Copy this link:", location.href); });
    else window.prompt("Copy this link:", location.href);
  });
});
