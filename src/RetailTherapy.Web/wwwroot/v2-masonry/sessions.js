// The curated-lists page ("Therapy Sessions"). Each list is a themed shelf of products.
RT.ready(function () {
  var host = document.getElementById("sessions");
  var chips = document.getElementById("chips");
  var ratios = ["1/1", "4/5", "3/4", "5/6", "2/3", "1/1", "4/5"];
  RT.fillChrome();
  document.title = (RT.site.listsName || "Therapy Sessions") + " | " + (RT.site.name || "");

  function get(u) { return fetch(u).then(function (r) { if (!r.ok) throw new Error(u + " " + r.status); return r.json(); }); }

  function section(list) {
    var products = list.productIds.map(RT.byId).filter(Boolean);
    if (!products.length) return "";
    return '<section class="session" id="' + RT.esc(list.id) + '" data-source="sessions:' + RT.esc(list.id) + '" style="--h:' + (list.hue || 320) + '">' +
      '<header class="s-head"><span class="s-emoji" aria-hidden="true">' + (list.emoji || "&#10022;") + "</span>" +
      "<div><h2>" + RT.esc(list.title) + "</h2>" + (list.blurb ? "<p>" + RT.esc(list.blurb) + "</p>" : "") + "</div>" +
      '<span class="s-count">' + products.length + (products.length === 1 ? " find" : " finds") + "</span></header>" +
      '<div class="grid">' + products.map(function (p, i) { return RT.tile(p, ratios[i % ratios.length]); }).join("") + "</div></section>";
  }

  get("/api/lists").then(function (lists) {
    lists = lists.filter(function (l) { return l.productIds && l.productIds.length; });
    if (!lists.length) {
      host.innerHTML = '<p class="empty">No sessions yet. <a href="/">Browse all finds</a>.</p>';
      return;
    }
    chips.innerHTML = '<a class="chip chip-link" href="/">&larr; All finds</a>' + lists.map(function (l) {
      return '<a class="chip" href="#' + RT.esc(l.id) + '">' + (l.emoji || "") + " " + RT.esc(l.title) + "</a>";
    }).join("");
    host.innerHTML = lists.map(section).join("");
    RT.wish.paint();
    RT.mountAds();
    // Open straight to a session when the link has one (/sessions#cozy-night-in).
    if (location.hash) {
      var t = document.getElementById(decodeURIComponent(location.hash.slice(1)));
      if (t) t.scrollIntoView();
    }
  }).catch(function () {
    host.innerHTML = '<p class="empty">Could not load the sessions. Please refresh.</p>';
  });

  // A tag under a product opens the main page filtered by that tag.
  document.addEventListener("click", function (e) {
    var h = e.target.closest(".hash[data-tag]");
    if (h) location.href = "/?tag=" + encodeURIComponent(h.getAttribute("data-tag"));
  });
});
