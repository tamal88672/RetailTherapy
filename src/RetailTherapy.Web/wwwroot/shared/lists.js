// Named lists, private notes and "bought" ticks for signed-in people. Plain script, loads after account.js.
//
// - Everything lives on the server (/api/me/saved ...). The page keeps a copy in memory and asks the server for every change,
//   one change at a time, so quick taps cannot overwrite each other.
// - The "Saved" list itself is still the heart list handled by wish.js; named lists are groups of saved finds.
// - A list is private until its owner shares it: "anyone with the link" or "public" (also shown on their profile page).
//   Notes and "bought" ticks are never shared.
(function () {
  "use strict";
  var RT = window.RT;
  if (!RT || !RT.wish || !RT.account) return;

  var MAXLISTS = 10;
  var esc = RT.esc;
  var data = null;            // { details: { id: { note, bought } }, lists: [ { id, name, visibility, productIds } ] }
  var loadedFor = "";         // the account the data belongs to
  var loading = false;
  var current = "";           // the list tab being looked at ("" = all saved)
  var hideBought = false;
  var editing = "";           // the find whose note is being typed
  var listeners = [];
  var chain = Promise.resolve();

  function emit() { listeners.forEach(function (fn) { try { fn(); } catch (e) {} }); }
  function byId(id) {
    if (!data) return null;
    for (var i = 0; i < data.lists.length; i++) if (data.lists[i].id === id) return data.lists[i];
    return null;
  }

  // ---------- the copy in memory ----------
  function apply(p) {
    var details = {};
    (p.details || []).forEach(function (d) { details[d.productId] = { note: d.note || "", bought: !!d.bought }; });
    data = { details: details, lists: (p.lists || []).map(function (l) {
      return { id: l.id, name: l.name, visibility: l.visibility || "private", productIds: (l.productIds || []).slice() };
    }) };
    reconcile();
    if (current && !byId(current)) current = "";
  }
  // Something unsaved on this page may not have reached the server yet: leave it out of lists and notes here too.
  function reconcile() {
    if (!data) return;
    data.lists.forEach(function (l) { l.productIds = l.productIds.filter(RT.wish.has); });
    Object.keys(data.details).forEach(function (k) { if (!RT.wish.has(k)) delete data.details[k]; });
  }

  function sync() {
    var uid = RT.account.signedIn() ? RT.account.uid() : "";
    if (!uid) {
      if (data || loadedFor) { data = null; loadedFor = ""; current = ""; hideBought = false; editing = ""; }
      emit();
      return;
    }
    // wait until wish.js has merged this browser's hearts with the account, or the lists would be trimmed against stale hearts
    if (loadedFor === uid || loading || !RT.wish.attached()) { emit(); return; }
    loading = true;
    RT.account.api("GET", "/api/me/saved").then(function (r) {
      loading = false;
      if (r.ok && r.data) { loadedFor = uid; apply(r.data); }
      emit();
    }).catch(function () { loading = false; });
  }

  // One request at a time. Resolves { ok, id } or { ok:false, error }.
  function call(method, path, body) {
    var run = function () {
      return RT.account.api(method, path, body).then(function (r) {
        if (r.ok && r.data) { apply(r.data); emit(); return { ok: true, id: r.data.createdId || null }; }
        var msg = (r.data && r.data.error) || (r.status === 429 ? "Slow down a little, then try again." : "That didn't work. Please try again.");
        return { ok: false, error: msg };
      }).catch(function () { return { ok: false, error: "No connection. Please try again." }; });
    };
    chain = chain.then(run, run);
    return chain;
  }
  function seg(s) { return encodeURIComponent(s); }

  // ---------- the public API ----------
  var L = RT.lists = {
    active: function () { return !!data; },
    onChange: function (fn) { listeners.push(fn); },
    all: function () { return data ? data.lists : []; },
    get: byId,
    current: function () { return current; },
    note: function (id) { var d = data && data.details[id]; return d ? d.note : ""; },
    bought: function (id) { var d = data && data.details[id]; return !!(d && d.bought); },
    boughtCount: function () { var n = 0; if (data) Object.keys(data.details).forEach(function (k) { if (data.details[k].bought) n++; }); return n; },
    hideBought: function () { return hideBought; },
    link: function (id) { var u = RT.account.username(); return u ? location.origin + "/u/" + u + "/" + id : ""; },

    select: function (id) { current = id && byId(id) ? id : ""; editing = ""; emit(); },
    toggleHideBought: function () { hideBought = !hideBought; emit(); },

    setNote: function (id, text) { return call("PATCH", "/api/me/saved/" + seg(id), { note: text }); },
    setBought: function (id, on) {
      var prev = L.bought(id), d = data && data.details[id];   // show the tick at once; the server's answer replaces it
      if (data) { if (!d) d = data.details[id] = { note: "", bought: false }; d.bought = on; emit(); }
      return call("PATCH", "/api/me/saved/" + seg(id), { bought: on }).then(function (r) {
        if (!r.ok && data && data.details[id]) { data.details[id].bought = prev; emit(); }
        return r;
      });
    },
    create: function (name, productId) { return call("POST", "/api/me/lists", { name: name, productId: productId || null }); },
    rename: function (id, name) { return call("PATCH", "/api/me/lists/" + seg(id), { name: name }); },
    share: function (id, visibility) { return call("PATCH", "/api/me/lists/" + seg(id), { visibility: visibility }); },
    remove: function (id) { return call("DELETE", "/api/me/lists/" + seg(id)); },
    add: function (id, productId) { return call("PUT", "/api/me/lists/" + seg(id) + "/items/" + seg(productId)); },
    drop: function (id, productId) { return call("DELETE", "/api/me/lists/" + seg(id) + "/items/" + seg(productId)); },

    // Which of the saved finds are shown, given the list tab and the "hide bought" switch.
    filter: function (products) {
      if (!data) return products;
      var out = products;
      var l = current ? byId(current) : null;
      if (l) { var set = {}; l.productIds.forEach(function (id) { set[id] = 1; }); out = out.filter(function (p) { return set[p.id]; }); }
      if (hideBought) out = out.filter(function (p) { return !L.bought(p.id); });
      return out;
    },

    // ---------- HTML for the page ----------
    // The row under a find: note, "Lists" and "Bought".
    mine: function (id) { return '<div class="mine" data-mine="' + esc(id) + '">' + mineInner(id) + "</div>"; },

    // The tabs (Saved + each list) and, for a chosen list, who can see it.
    bar: function (savedCount) {
      var tabs = '<div class="ltabs" role="tablist" aria-label="Your lists">' + tab("", "Saved", savedCount) +
        data.lists.map(function (l) { return tab(l.id, l.name, l.productIds.length); }).join("") +
        (data.lists.length < MAXLISTS ? '<button type="button" class="ltab add" data-l="new">+ New list</button>' : "") +
        (L.boughtCount() ? '<button type="button" class="ltab sw" data-l="hide" aria-pressed="' + hideBought + '">Hide bought</button>' : "") +
        "</div>";
      var l = current ? byId(current) : null;
      if (!l) {
        var u = RT.account.username();
        var anyPublic = data.lists.some(function (x) { return x.visibility === "public"; });
        return tabs + (u && anyPublic ? '<p class="lhelp">Your public lists are on <a href="/u/' + esc(u) + '">your profile page</a>.</p>' : "");
      }
      return tabs + head(l);
    },

    // Re-draws the small controls under finds (not the one whose note is being typed) and marks bought ones.
    paintTiles: function (root) {
      Array.prototype.forEach.call((root || document).querySelectorAll(".tile[data-id]"), function (t) {
        var id = t.getAttribute("data-id");
        t.classList.toggle("bought", !!data && L.bought(id));
        var m = t.querySelector("[data-mine]");
        if (m && id !== editing) m.innerHTML = mineInner(id);
      });
    }
  };

  function tab(id, label, n) {
    var on = id === current;
    return '<button type="button" role="tab" class="ltab' + (on ? " on" : "") + '" data-l="tab" data-id="' + esc(id) + '" aria-selected="' + on + '">' +
      '<span class="ln">' + esc(label) + "</span><i>" + n + "</i></button>";
  }

  var HELP = {
    "private": "Only you can see this list.",
    "unlisted": "Anyone with the link can see this list. It isn't shown anywhere else.",
    "public": "Anyone can see this list, and it's shown on your profile."
  };
  function head(l) {
    var u = RT.account.username();
    var shared = l.visibility !== "private";
    var opts = [["private", "Private"], ["unlisted", "Anyone with the link"], ["public", "Public (on my profile)"]].map(function (o) {
      return '<option value="' + o[0] + '"' + (o[0] === l.visibility ? " selected" : "") + ">" + o[1] + "</option>";
    }).join("");
    return '<div class="lhead" data-list="' + esc(l.id) + '"><div class="lhead-top"><h2>' + esc(l.name) + "</h2>" +
      '<div class="lhead-btns"><button type="button" class="mine-b" data-l="rename">Rename</button>' +
      '<button type="button" class="mine-b danger" data-l="delete">Delete list</button></div></div>' +
      '<div class="lshare"><label for="lshare-s">Who can see it</label>' +
      '<select id="lshare-s" data-l="share"' + (u ? "" : " disabled") + ">" + opts + "</select>" +
      (shared && u ? '<button type="button" class="mine-b copy" data-l="copy">Copy link</button>' : "") + "</div>" +
      (u ? '<p class="lhelp">' + HELP[l.visibility] + (shared ? " Your notes and &ldquo;bought&rdquo; ticks always stay private." : "") + "</p>"
         : '<p class="lhelp">To share a list, first <button type="button" class="acct-link inl" data-l="name">choose a username</button>.</p>') +
      '<p class="lhelp bad" data-lerr role="alert" hidden></p></div>';
  }

  function mineInner(id) {
    var note = L.note(id), on = L.bought(id);
    if (editing === id) {
      return '<form class="mine-form" data-lform="note"><label class="sr" for="note-' + esc(id) + '">Private note</label>' +
        '<textarea id="note-' + esc(id) + '" rows="3" maxlength="280" placeholder="Size, color, who it\'s for…">' + esc(note) + "</textarea>" +
        '<p class="mine-fine" data-lerr role="alert" hidden></p>' +
        '<div class="mine-row"><button type="submit" class="mine-b main">Save note</button><button type="button" class="mine-b" data-l="note-cancel">Cancel</button></div></form>';
    }
    return (note ? '<p class="mine-note"><span class="sr">Your note: </span>' + esc(note) + "</p>" : "") +
      '<div class="mine-row"><button type="button" class="mine-b" data-l="note" data-id="' + esc(id) + '">' + (note ? "Edit note" : "Add note") + "</button>" +
      '<button type="button" class="mine-b" data-l="pick" data-id="' + esc(id) + '">Lists</button>' +
      '<button type="button" class="mine-b bought" data-l="bought" data-id="' + esc(id) + '" aria-pressed="' + on + '">' + (on ? "&#10003; Bought" : "Bought") + "</button></div>";
  }

  function showErr(root, msg) {
    var e = root && root.querySelector("[data-lerr]");
    if (e) { e.textContent = msg || ""; e.hidden = !msg; }
  }

  // ---------- clicks on the page ----------
  document.addEventListener("click", function (e) {
    var b = e.target.closest("[data-l]");
    if (!b || !data) return;
    var a = b.getAttribute("data-l"), id = b.getAttribute("data-id");
    if (a === "tab") return L.select(id);
    if (a === "new") return openDialog(newHtml());
    if (a === "hide") return L.toggleHideBought();
    if (a === "name") return RT.account.pick();
    if (a === "rename") { var r = byId(current); if (r) openDialog(renameHtml(r)); return; }
    if (a === "delete") { var d = byId(current); if (d) openDialog(deleteHtml(d)); return; }
    if (a === "copy") return copyLink(b);
    if (a === "pick") return openDialog(pickHtml(id), id);
    if (a === "bought") return void L.setBought(id, !L.bought(id));
    if (a === "note") {
      editing = id;
      paintOne(id);
      var ta = document.getElementById("note-" + id);
      if (ta) { ta.focus(); ta.setSelectionRange(ta.value.length, ta.value.length); }
      return;
    }
    if (a === "note-cancel") { var p = editing; editing = ""; return paintOne(p); }
  });

  document.addEventListener("change", function (e) {
    var s = e.target.closest('select[data-l="share"]');
    if (!s || !data || !current) return;
    var l = byId(current);
    var before = l ? l.visibility : "private";
    s.disabled = true;
    L.share(current, s.value).then(function (r) {
      s.disabled = false;
      if (!r.ok) { s.value = before; showErr(s.closest(".lhead"), r.error); }
    });
  });

  document.addEventListener("submit", function (e) {
    var f = e.target.closest('form[data-lform="note"]');
    if (!f) return;
    e.preventDefault();
    var m = f.closest("[data-mine]"), id = m && m.getAttribute("data-mine");
    var btn = f.querySelector('button[type="submit"]');
    if (!id) return;
    if (btn) btn.disabled = true;
    L.setNote(id, f.querySelector("textarea").value).then(function (r) {
      if (r.ok) { editing = ""; paintOne(id); return; }
      if (btn) btn.disabled = false;
      showErr(f, r.error);
    });
  });
  document.addEventListener("keydown", function (e) {
    var ta = e.target.closest && e.target.closest(".mine-form textarea");
    if (!ta) return;
    if (e.key === "Escape") { e.preventDefault(); var p = editing; editing = ""; paintOne(p); }
    else if (e.key === "Enter" && (e.ctrlKey || e.metaKey)) { e.preventDefault(); ta.form.requestSubmit(); }
  });

  function paintOne(id) {
    Array.prototype.forEach.call(document.querySelectorAll('[data-mine="' + (window.CSS && CSS.escape ? CSS.escape(id) : id) + '"]'), function (m) { m.innerHTML = mineInner(id); });
  }

  function copyLink(btn) {
    var text = L.link(current);
    if (!text) return;
    var done = function () { btn.textContent = "Copied!"; setTimeout(function () { if (btn.isConnected) btn.textContent = "Copy link"; }, 1800); };
    var fallback = function () {
      var t = document.createElement("textarea");
      t.value = text; t.setAttribute("readonly", ""); t.style.position = "fixed"; t.style.opacity = "0";
      document.body.appendChild(t); t.select();
      try { document.execCommand("copy"); done(); } catch (er) { window.prompt("Copy this link:", text); }
      document.body.removeChild(t);
    };
    if (navigator.clipboard && navigator.clipboard.writeText) navigator.clipboard.writeText(text).then(done, fallback); else fallback();
  }

  // ---------- the small dialog (add to a list, new list, rename, delete) ----------
  var dlg = null, forFind = "";
  var X = '<button type="button" class="acct-x" data-d="close" aria-label="Close">&times;</button>';
  function err(msg) { return '<p class="acct-err" role="alert"' + (msg ? "" : " hidden") + ">" + esc(msg || "") + "</p>"; }

  function openDialog(html, findId) {
    if (!dlg) {
      dlg = document.createElement("dialog");
      dlg.className = "acct-dialog ldlg";
      dlg.setAttribute("aria-labelledby", "ldlg-h");
      document.body.appendChild(dlg);
      dlg.addEventListener("click", function (e) {
        if (e.target === dlg) return dlg.close();
        var b = e.target.closest("[data-d]");
        if (b) dialogAction(b.getAttribute("data-d"), b);
      });
      dlg.addEventListener("submit", function (e) { e.preventDefault(); dialogSubmit(e.target); });
    }
    forFind = findId || "";
    dlg.innerHTML = html;
    if (!dlg.open) { if (dlg.showModal) dlg.showModal(); else dlg.setAttribute("open", ""); }
    var first = dlg.querySelector("input");
    if (first) setTimeout(function () { try { first.focus(); first.select(); } catch (e) {} }, 30);
  }
  function closeDialog() { if (dlg && dlg.open) dlg.close(); forFind = ""; }
  function dialogError(msg) { var e = dlg.querySelector(".acct-err"); if (e) { e.textContent = msg || ""; e.hidden = !msg; } }

  function nameForm(kind, value, button, extra) {
    return '<form data-form="' + kind + '"' + (extra || "") + '><label for="ldlg-n">List name</label>' +
      '<input id="ldlg-n" maxlength="40" autocomplete="off" required placeholder="e.g. Gifts for Mum" value="' + esc(value || "") + '">' +
      '<button type="submit" class="acct-primary">' + button + "</button></form>";
  }
  function newHtml() {
    return X + '<h2 id="ldlg-h">New list</h2><p class="acct-sub">Lists are private until you choose to share them.</p>' + nameForm("create", "", "Create list") + err("");
  }
  function renameHtml(l) {
    return X + '<h2 id="ldlg-h">Rename list</h2>' + nameForm("rename", l.name, "Save name", ' data-id="' + esc(l.id) + '"') + err("");
  }
  function deleteHtml(l) {
    return X + '<h2 id="ldlg-h">Delete &ldquo;' + esc(l.name) + '&rdquo;?</h2><p class="acct-sub">The finds stay in your Saved list. Only the list goes, and its share link stops working.</p>' +
      '<button type="button" class="acct-danger" data-d="delete-yes" data-id="' + esc(l.id) + '">Yes, delete the list</button>' +
      '<button type="button" class="acct-link" data-d="close">Cancel</button>' + err("");
  }
  function pickHtml(findId, message) {
    var p = RT.byId(findId);
    var rows = data.lists.map(function (l) {
      var on = l.productIds.indexOf(findId) > -1;
      return '<button type="button" class="lrow" data-d="toggle" data-id="' + esc(l.id) + '" aria-pressed="' + on + '">' +
        '<span class="lbox" aria-hidden="true"></span><span class="lname">' + esc(l.name) + "</span><small>" + l.productIds.length + "</small></button>";
    }).join("");
    var form = data.lists.length >= MAXLISTS
      ? '<p class="acct-fine">You have ' + MAXLISTS + " lists, the most allowed. Delete one to make another.</p>"
      : '<form data-form="create" data-find="' + esc(findId) + '"><label for="ldlg-n">' + (rows ? "Or start a new list" : "Start a list") + '</label>' +
        '<div class="lnew"><input id="ldlg-n" maxlength="40" autocomplete="off" placeholder="e.g. Gifts for Mum"><button type="submit" class="acct-primary">Create</button></div></form>';
    return X + '<h2 id="ldlg-h">Add to a list</h2><p class="acct-sub">' + esc(p ? p.title : "") + "</p>" +
      (rows ? '<div class="lrows">' + rows + "</div>" : "") + form + err(message) +
      '<button type="button" class="acct-link" data-d="close">Done</button>';
  }

  function dialogAction(a, el) {
    if (a === "close") return closeDialog();
    var id = el.getAttribute("data-id");
    if (a === "toggle") {
      var l = byId(id), has = l && l.productIds.indexOf(forFind) > -1;
      el.disabled = true;
      (has ? L.drop(id, forFind) : L.add(id, forFind)).then(function (r) {
        if (!dlg || !dlg.open) return;
        var scroll = (dlg.querySelector(".lrows") || {}).scrollTop || 0;
        dlg.innerHTML = pickHtml(forFind, r.ok ? "" : r.error);
        var rows = dlg.querySelector(".lrows"); if (rows) rows.scrollTop = scroll;
        var again = dlg.querySelector('[data-d="toggle"][data-id="' + id + '"]'); if (again) again.focus();
      });
      return;
    }
    if (a === "delete-yes") {
      el.disabled = true;
      L.remove(id).then(function (r) { if (r.ok) closeDialog(); else { el.disabled = false; dialogError(r.error); } });
    }
  }

  function dialogSubmit(form) {
    var kind = form.getAttribute("data-form");
    var input = form.querySelector("input");
    var name = input ? input.value : "";
    var btn = form.querySelector('button[type="submit"]');
    btn.disabled = true;
    var done = function (r, ok) { btn.disabled = false; if (r.ok) ok(r); else dialogError(r.error); };
    if (kind === "create") {
      var find = form.getAttribute("data-find") || "";
      L.create(name, find).then(function (r) {
        done(r, function (res) {
          if (find) { dlg.innerHTML = pickHtml(find, ""); var first = dlg.querySelector("input"); if (first) first.focus(); }
          else { closeDialog(); L.select(res.id); }
        });
      });
    } else if (kind === "rename") {
      L.rename(form.getAttribute("data-id"), name).then(function (r) { done(r, closeDialog); });
    }
  }

  // ---------- start ----------
  RT.wish.onChange(function () { if (data) { reconcile(); emit(); } });
  RT.account.onChange(sync);
  sync();
})();
