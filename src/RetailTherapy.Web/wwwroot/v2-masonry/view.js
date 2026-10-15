RT.ready(function () {
  var grid = document.getElementById("grid");
  var chips = document.getElementById("chips");
  var note = document.getElementById("note");
  var lbar = document.getElementById("lbar");
  var relatedBox = document.getElementById("related");
  var FILL = 8;   // a tag page with few best matches is topped up with related finds to about this many
  var q = document.getElementById("q");
  var savedBtn = document.getElementById("saved");
  var ratios = ["1/1", "4/5", "3/4", "5/6", "2/3", "1/1", "4/5"];
  var params = new URLSearchParams(location.search);
  var cat = "All";
  var tag = (params.get("tag") || "").toLowerCase();   // a tag link from the sessions page arrives as /?tag=viral
  var saved = location.hash === "#saved";               // the heart button in the top bar shows only saved finds
  var shown = "";     // which finds are on screen, so a change to a note or a tick does not redraw everything
  RT.fillChrome();

  // Signed-in people get a small row of controls under every saved find (note, lists, bought).
  var mine = function () { return saved && RT.lists && RT.lists.active(); };
  RT.tileExtra = function (p) { return mine() ? RT.lists.mine(p.id) : ""; };

  function renderChips() {
    var link = '<a class="chip chip-link" href="/sessions"><span aria-hidden="true">&#10022;</span> ' + RT.esc(RT.site.listsName || "Therapy Sessions") + "</a>";
    chips.innerHTML = link + ["All"].concat(RT.categories()).map(function (c) {
      return '<button class="chip' + (c === cat ? " on" : "") + '" data-c="' + RT.esc(c) + '">' + RT.esc(c) + "</button>";
    }).join("");
  }

  function renderNote(best, more) {
    var html = "";
    if (saved) {
      var signedIn = RT.account && RT.account.signedIn();
      var canSignIn = RT.account && RT.account.enabled();
      var line = signedIn
        ? RT.wish.count() + " saved. Add a note, tick &ldquo;bought&rdquo;, or put finds into lists you can share."
        : RT.wish.count() + " saved on this device." + (canSignIn ? " Sign in to keep them on every device, add notes and share lists with friends." : "");
      html += '<div class="note-box"><span><b>Your saved finds</b> &middot; ' + line + "</span>" +
        '<span class="note-btns">' + (!signedIn && canSignIn ? '<button type="button" class="pill" data-signin>Sign in</button>' : "") +
        '<button type="button" class="pill" data-clear="saved">Show all finds</button></span></div>';
    }
    if (tag) {
      html += '<div class="note-box"><span>' + (best
        ? "Best matches for <b>#" + RT.esc(tag) + "</b> &middot; " + best + (more ? " &middot; plus " + more + " related" : "") + " <small>(finds where it is a top-3 tag, best fit first)</small>"
        : "<b>#" + RT.esc(tag) + "</b> is not a top-3 tag on any find yet" + (more ? ", so here are the closest " + more : "")) + '</span>' +
        '<button type="button" class="pill" data-clear="tag" aria-label="Clear tag filter">Clear &#10005;</button></div>';
    }
    note.innerHTML = html;
  }

  function renderBar() {
    lbar.innerHTML = mine() ? RT.lists.bar(RT.wish.count()) : "";
  }

  function render() {
    var list = RT.filter(cat, q.value, tag);
    var more = RT.related(cat, q.value, tag, list.length, FILL);
    if (saved) {
      list = list.filter(function (p) { return RT.wish.has(p.id); });
      more = more.filter(function (p) { return RT.wish.has(p.id); });
      if (mine()) { list = RT.lists.filter(list); more = RT.lists.filter(more); }
    }
    shown = saved ? list.concat(more).map(function (p) { return p.id; }).join() : "";
    renderNote(list.length, more.length);
    renderBar();
    grid.setAttribute("data-source", saved ? "saved" : "home");
    if (!list.length && !more.length) {
      var msg = '<p class="empty">No matches. Try another search.</p>';
      if (saved && !RT.wish.count()) msg = '<p class="empty">Nothing saved yet. Tap the heart on any find to keep it here.</p>';
      else if (mine() && RT.lists.current() && !q.value && !tag && cat === "All")
        msg = '<p class="empty">' + (RT.lists.hideBought() ? "Everything in this list is marked bought." : "This list is empty. Open Saved, tap <b>Lists</b> under a find and add it here.") + "</p>";
      else if (mine() && RT.lists.hideBought() && !q.value && !tag && cat === "All") msg = '<p class="empty">Everything you saved is marked bought.</p>';
      grid.innerHTML = msg;
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
    if (mine()) RT.lists.paintTiles(document);
  }

  // Lists, notes or sign-in changed: redraw the finds only if the set shown changed; otherwise just the small controls.
  function onLists() {
    if (!saved) return;
    var list = RT.filter(cat, q.value, tag).filter(function (p) { return RT.wish.has(p.id); });
    var more = RT.related(cat, q.value, tag, list.length, FILL).filter(function (p) { return RT.wish.has(p.id); });
    if (mine()) { list = RT.lists.filter(list); more = RT.lists.filter(more); }
    var ids = list.concat(more).map(function (p) { return p.id; }).join();
    var hadControls = !!grid.querySelector("[data-mine]");
    if (ids !== shown || hadControls !== mine()) return render();
    renderNote(list.length, more.length);
    renderBar();
    if (mine()) RT.lists.paintTiles(document);
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
    if (e.target.closest("[data-signin]")) { if (RT.account) RT.account.open(); return; }
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

  if (RT.lists) RT.lists.onChange(onLists);
  paintSavedBtn();
  renderChips();
  render();
  RT.wish.paint();
});
