// Saved items ("wishlist") and anonymous tracking. Plain script, loads after app.js.
//
// - The saved list lives in the visitor's browser (localStorage), newest first, so it works with no sign-in.
// - Every save / remove / click-through to Amazon is sent to /api/track with a random visitor id (no name, no email).
// - The list is also copied to the server (/api/wishlist). That copy is what accounts and shared lists will build on:
//   later the same list can be claimed by a signed-in user and shared.
(function () {
  var RT = window.RT;
  var WKEY = "rt-wish", VKEY = "rt-vid", MAX = 200;
  var ids = [];
  var listeners = [];
  var memVid = "";

  function readIds() {
    try {
      var v = JSON.parse(localStorage.getItem(WKEY) || "[]");
      return Array.isArray(v) ? v.filter(function (x) { return typeof x === "string"; }).slice(0, MAX) : [];
    } catch (e) { return []; }
  }
  function writeIds() { try { localStorage.setItem(WKEY, JSON.stringify(ids)); } catch (e) {} }
  ids = readIds();

  // Random anonymous id, made once per browser.
  function visitorId() {
    try {
      var v = localStorage.getItem(VKEY);
      if (v && /^[A-Za-z0-9-]{16,64}$/.test(v)) return v;
    } catch (e) {}
    if (memVid) return memVid;
    var n = "";
    try {
      if (window.crypto && crypto.randomUUID) n = crypto.randomUUID();
      else if (window.crypto && crypto.getRandomValues) {
        var a = new Uint8Array(16); crypto.getRandomValues(a);
        n = Array.prototype.map.call(a, function (b) { return (b < 16 ? "0" : "") + b.toString(16); }).join("");
      }
    } catch (e) {}
    if (!n) n = String(Date.now()) + Math.random().toString(16).slice(2, 12) + "0000000000";
    memVid = n;
    try { localStorage.setItem(VKEY, n); } catch (e) {}
    return n;
  }

  // ---- tracking (batched; never blocks the page; failures are ignored) ----
  var queue = [], timer = null;
  function flush() {
    clearTimeout(timer); timer = null;
    if (!queue.length) return;
    var events = queue.splice(0, 20);
    try {
      fetch("/api/track", {
        method: "POST", headers: { "Content-Type": "application/json" }, keepalive: true,
        body: JSON.stringify({ visitorId: visitorId(), events: events })
      }).catch(function () {});
    } catch (e) {}
    if (queue.length) timer = setTimeout(flush, 300);
  }
  RT.track = function (type, productId, source) {
    queue.push({ type: type, productId: productId, source: source || "home" });
    if (!timer) timer = setTimeout(flush, 800);
  };
  window.addEventListener("pagehide", flush);
  document.addEventListener("visibilitychange", function () { if (document.visibilityState === "hidden") flush(); });

  // ---- server copy of the list (debounced) ----
  var syncTimer = null;
  function syncSoon() {
    clearTimeout(syncTimer);
    syncTimer = setTimeout(function () {
      try {
        fetch("/api/wishlist", {
          method: "PUT", headers: { "Content-Type": "application/json" }, keepalive: true,
          body: JSON.stringify({ visitorId: visitorId(), productIds: ids })
        }).catch(function () {});
      } catch (e) {}
    }, 1500);
  }

  // ---- the saved list ----
  var W = RT.wish = {
    has: function (id) { return ids.indexOf(id) > -1; },
    ids: function () { return ids.slice(); },
    count: function () { return ids.length; },
    onChange: function (fn) { listeners.push(fn); },
    // Adds or removes a product. source says where the tap happened ("home", "saved", "sessions:{id}").
    toggle: function (id, source) {
      var i = ids.indexOf(id), added = i < 0;
      if (added) { ids.unshift(id); if (ids.length > MAX) ids.length = MAX; } else ids.splice(i, 1);
      writeIds();
      RT.track(added ? "wish_add" : "wish_remove", id, source);
      syncSoon();
      changed(id);
      return added;
    }
  };

  var HEART = '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M12 21.2l-1.5-1.3C5.3 15.3 2 12.3 2 8.6 2 5.6 4.4 3.2 7.4 3.2c1.7 0 3.4.8 4.6 2.1 1.2-1.3 2.9-2.1 4.6-2.1 3 0 5.4 2.4 5.4 5.4 0 3.7-3.3 6.7-8.5 11.3L12 21.2z"/></svg>';

  // The heart button on a product picture.
  RT.heart = function (p) {
    var on = W.has(p.id);
    return '<button type="button" class="heart" data-wish="' + RT.esc(p.id) + '" aria-pressed="' + on + '" aria-label="Save to wishlist: ' +
      RT.esc(p.title) + '" title="' + (on ? "Remove from wishlist" : "Add to wishlist") + '">' + HEART + "</button>";
  };

  // Brings every heart and counter on the page in line with the saved list.
  function paint() {
    Array.prototype.forEach.call(document.querySelectorAll("button.heart[data-wish]"), function (b) {
      var on = W.has(b.getAttribute("data-wish"));
      if ((b.getAttribute("aria-pressed") === "true") !== on) b.setAttribute("aria-pressed", on ? "true" : "false");
      b.title = on ? "Remove from wishlist" : "Add to wishlist";
    });
    Array.prototype.forEach.call(document.querySelectorAll("[data-saved-count]"), function (n) {
      n.textContent = ids.length;
      n.hidden = ids.length === 0;
    });
    Array.prototype.forEach.call(document.querySelectorAll("[data-saved-btn]"), function (b) {
      b.setAttribute("aria-label", "Saved items: " + ids.length);
    });
  }
  RT.wish.paint = paint;

  function changed(id) {
    paint();
    listeners.forEach(function (fn) { try { fn(id); } catch (e) {} });
  }

  // Taps: the heart, and click-throughs to Amazon.
  function sourceOf(el) {
    var s = el.closest("[data-source]");
    return s ? s.getAttribute("data-source") : "home";
  }
  document.addEventListener("click", function (e) {
    var heart = e.target.closest("button.heart[data-wish]");
    if (heart) {
      e.preventDefault();
      var added = W.toggle(heart.getAttribute("data-wish"), sourceOf(heart));
      if (added) { heart.classList.remove("pop"); void heart.offsetWidth; heart.classList.add("pop"); }
      return;
    }
    var go = e.target.closest("a.cta");
    if (go) {
      var tile = go.closest("[data-id]");
      if (tile) RT.track("click", tile.getAttribute("data-id"), sourceOf(go));
    }
  });

  // Another tab changed the list: follow it.
  window.addEventListener("storage", function (e) {
    if (e.key === WKEY) { ids = readIds(); changed(); }
  });

  if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", paint); else paint();
})();
