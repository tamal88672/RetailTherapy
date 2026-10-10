// Saved items ("wishlist") and anonymous tracking. Plain script, loads after app.js.
//
// - The saved list lives in the visitor's browser (localStorage), newest first, so it works with no sign-in.
// - Every save / remove / click-through to Amazon is sent to /api/track with a random visitor id (no name, no email).
// - The list is also copied to the server. Anonymous: /api/wishlist (by the random visitor id). Signed in (account.js):
//   /api/me/wishlist, the account's own list, which follows the person to every device. The browser copy stays the working
//   copy either way, so the page never waits for the network.
(function () {
  var RT = window.RT;
  var WKEY = "rt-wish", VKEY = "rt-vid", OWNER = "rt-acct", MAX = 200;
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
  var acct = null;           // set by account.js while someone is signed in: { uid, token: function -> Promise<string|null> }
  var lastToken = "";        // so a closing tab can still send its last change
  var touches = 0;           // counts changes made on this page, to spot edits made while a request was in flight
  var syncTimer = null;

  function authed(method, url, body) {
    return acct.token().then(function (t) {
      if (!t) throw new Error("signed out");
      lastToken = t;
      return fetch(url, {
        method: method, keepalive: !!body,
        headers: body ? { Authorization: "Bearer " + t, "Content-Type": "application/json" } : { Authorization: "Bearer " + t },
        body: body ? JSON.stringify(body) : undefined
      });
    });
  }
  function pushNow() {
    syncTimer = null;
    try {
      if (acct) return authed("PUT", "/api/me/wishlist", { productIds: ids }).catch(function () {});
      return fetch("/api/wishlist", {
        method: "PUT", headers: { "Content-Type": "application/json" }, keepalive: true,
        body: JSON.stringify({ visitorId: visitorId(), productIds: ids })
      }).catch(function () {});
    } catch (e) { return Promise.resolve(); }
  }
  function syncSoon() {
    clearTimeout(syncTimer);
    syncTimer = setTimeout(pushNow, 1500);
  }
  // A closing tab sends what it has right away (the cached token is used because a new one cannot be fetched in time).
  window.addEventListener("pagehide", function () {
    if (!syncTimer) return;
    clearTimeout(syncTimer); syncTimer = null;
    try {
      if (acct && lastToken) {
        fetch("/api/me/wishlist", { method: "PUT", keepalive: true, headers: { Authorization: "Bearer " + lastToken, "Content-Type": "application/json" }, body: JSON.stringify({ productIds: ids }) }).catch(function () {});
      } else if (!acct) pushNow();
    } catch (e) {}
  });

  function union(a, b) {
    var seen = {}, out = [];
    a.concat(b).forEach(function (x) { if (!seen[x]) { seen[x] = 1; out.push(x); } });
    return out.slice(0, MAX);
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
      touches++;
      syncSoon();
      changed(id);
      return added;
    },

    // ---- used by account.js ----
    // Someone signed in. The first time this browser meets this account, the items saved here join the account's list;
    // after that the account's list is the truth and this browser follows it.
    attach: function (uid, tokenFn) {
      acct = { uid: uid, token: tokenFn };
      var owned = false;
      try { owned = localStorage.getItem(OWNER) === uid; } catch (e) {}
      var before = touches, merge = !owned || touches > 0;
      var req = merge
        ? authed("POST", "/api/me/claim", { visitorId: visitorId(), productIds: ids })
        : authed("GET", "/api/me/wishlist");
      return req.then(function (r) { if (!r.ok) throw new Error("status " + r.status); return r.json(); })
        .then(function (j) {
          try { localStorage.setItem(OWNER, uid); } catch (e) {}
          var theirs = Array.isArray(j.productIds) ? j.productIds : [];
          // If something was saved while we were asking, keep it too (never lose a save).
          var next = touches !== before ? union(ids, theirs) : theirs.slice(0, MAX);
          var same = next.join("|") === ids.join("|");
          ids = next; writeIds();
          if (!same) changed();
          if (touches !== before) syncSoon();
        })
        .catch(function () { /* offline or not ready: keep the browser copy, try again next visit */ });
    },
    // Signed out: send any last change, then leave this browser clean (the list lives in the account now).
    detach: function () {
      var done = Promise.resolve();
      if (acct && syncTimer) { clearTimeout(syncTimer); done = pushNow(); }
      return done.then(function () {
        acct = null; ids = []; writeIds(); lastToken = "";
        try { localStorage.removeItem(OWNER); } catch (e) {}
        changed();
      });
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
