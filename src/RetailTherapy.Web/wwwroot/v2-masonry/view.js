RT.ready(function () {
  var grid = document.getElementById("grid");
  var chips = document.getElementById("chips");
  var note = document.getElementById("note");
  var relatedBox = document.getElementById("related");
  var FILL = 8;   // a tag page with few best matches is topped up with related finds to about this many
  var q = document.getElementById("q");
  var savedBtn = document.getElementById("saved");
  var ratios = ["1/1", "4/5", "3/4", "5/6", "2/3", "1/1", "4/5"];
  var params = new URLSearchParams(location.search);
  var cat = "All";
  var tag = (params.get("tag") || "").toLowerCase();   // a tag link from the sessions page arrives as /?tag=viral
  var saved = location.hash === "#saved";               // the heart button in the top bar shows only saved finds
  RT.fillChrome();

  function renderChips() {
    var link = '<a class="chip chip-link" href="/sessions"><span aria-hidden="true">&#10022;</span> ' + RT.esc(RT.site.listsName || "Therapy Sessions") + "</a>";
    chips.innerHTML = link + ["All"].concat(RT.categories()).map(function (c) {
      return '<button class="chip' + (c === cat ? " on" : "") + '" data-c="' + RT.esc(c) + '">' + RT.esc(c) + "</button>";
    }).join("");
  }

  function renderNote(best, more) {
    var html = "";
    if (saved) {
      html += '<div class="note-box"><span><b>Your saved finds</b> &middot; ' + RT.wish.count() + ' saved on this device. Sign-in and sharing lists with friends are coming soon.</span>' +
        '<button type="button" class="pill" data-clear="saved">Show all finds</button></div>';
    }
    if (tag) {
      html += '<div class="note-box"><span>' + (best
        ? "Best matches for <b>#" + RT.esc(tag) + "</b> &middot; " + best + (more ? " &middot; plus " + more + " related" : "") + " <small>(finds where it is a top-3 tag, best fit first)</small>"
        : "<b>#" + RT.esc(tag) + "</b> is not a top-3 tag on any find yet" + (more ? ", so here are the closest " + more : "")) + '</span>' +
        '<button type="button" class="pill" data-clear="tag" aria-label="Clear tag filter">Clear &#10005;</button></div>';
    }
    note.innerHTML = html;
  }

  function render() {
    var list = RT.filter(cat, q.value, tag);
    var more = RT.related(cat, q.value, tag, list.length, FILL);
    if (saved) {
      list = list.filter(function (p) { return RT.wish.has(p.id); });
      more = more.filter(function (p) { return RT.wish.has(p.id); });
    }
    renderNote(list.length, more.length);
    grid.setAttribute("data-source", saved ? "saved" : "home");
    if (!list.length && !more.length) {
      grid.innerHTML = saved && !RT.wish.count()
        ? '<p class="empty">Nothing saved yet. Tap the heart on any find to keep it here.</p>'
        : '<p class="empty">No matches. Try another search.</p>';
      relatedBox.innerHTML = "";
      return;
    }
    grid.innerHTML = list.map(function (p, i) { return RT.tile(p, ratios[i % ratios.length]); }).join("");
    relatedBox.setAttribute("data-source", saved ? "saved" : "home");
    relatedBox.innerHTML = more.length
      ? '<h2 class="more-h">Also related to <b>#' + RT.esc(tag) + '</b></h2><div class="grid">' +
        more.map(function (p, i) { return RT.tile(p, ratios[(i + 3) % ratios.length]); }).join("") + "</div>"
      : "";
    RT.mountAds();
  }

  function paintSavedBtn() { savedBtn.setAttribute("aria-pressed", saved ? "true" : "false"); }
  function setSaved(on) {
    saved = on;
    try { history.replaceState(null, "", on ? "#saved" : location.pathname + location.search); } catch (e) {}
    paintSavedBtn();
    render();
  }

  chips.addEventListener("click", function (e) {
    var b = e.target.closest(".chip[data-c]");
    if (!b) return;
    cat = b.getAttribute("data-c");
    renderChips();
    render();
  });
  q.addEventListener("input", render);

  // Tags under a product turn into a filter; the pill in the note clears it.
  document.addEventListener("click", function (e) {
    var h = e.target.closest(".hash[data-tag]");
    if (h) {
      tag = h.getAttribute("data-tag");
      render();
      window.scrollTo({ top: 0, behavior: "smooth" });
      return;
    }
    var c = e.target.closest("[data-clear]");
    if (c) {
      if (c.getAttribute("data-clear") === "tag") { tag = ""; try { history.replaceState(null, "", location.pathname + location.hash); } catch (er) {} render(); }
      else setSaved(false);
    }
  });
  savedBtn.addEventListener("click", function () { setSaved(!saved); });
  window.addEventListener("hashchange", function () { var on = location.hash === "#saved"; if (on !== saved) setSaved(on); });

  // Removing a heart while looking at saved finds takes the tile away (after the heart's little animation).
  RT.wish.onChange(function () {
    if (saved) setTimeout(function () { if (saved) render(); }, 350);
  });

  paintSavedBtn();
  renderChips();
  render();
  RT.wish.paint();
});
