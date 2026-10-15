// Admin: how many people have registered, and who they are. Change roles, suspend, delete.
// Opens with an admin account (sign in from the account button) or with the admin key (kept for this tab only).
// It never shows emails: the site does not store them.
(function () {
  "use strict";
  var RT = window.RT;
  var host = document.getElementById("admin");
  var toastEl = document.getElementById("toast");
  var KEYK = "rt-admin-key";
  var esc = RT.esc;
  var key = "";
  try { key = sessionStorage.getItem(KEYK) || ""; } catch (e) {}
  var st = { phase: "wait", users: [], sum: null, q: "", f: "all", note: "" };
  var arm = "", armTimer = null, loading = false, waited = false, toastTimer = null;

  RT.ready(function () { RT.fillChrome(); document.title = "Admin | " + (RT.site.name || ""); init(); });

  // ---------- talking to the server ----------
  function request(method, path, body) {
    if (key) {
      var h = { "X-Admin-Key": key };
      if (body) h["Content-Type"] = "application/json";
      return fetch(path, { method: method, headers: h, body: body ? JSON.stringify(body) : undefined }).then(function (r) {
        return r.text().then(function (t) { var d = null; try { d = t ? JSON.parse(t) : null; } catch (e) {} return { ok: r.ok, status: r.status, data: d }; });
      });
    }
    if (RT.account && RT.account.signedIn()) return RT.account.api(method, path, body);
    return Promise.resolve({ ok: false, status: 401, data: null });
  }

  function toast(msg, bad) {
    toastEl.textContent = msg; toastEl.hidden = false; toastEl.className = "toast" + (bad ? " bad" : "");
    clearTimeout(toastTimer); toastTimer = setTimeout(function () { toastEl.hidden = true; }, 3500);
  }

  // ---------- which screen ----------
  function init() {
    if (RT.account) RT.account.onChange(function () { if (st.phase !== "ready") start(); else if (!key && !RT.account.signedIn()) { st.phase = "gate"; st.note = ""; draw(); } });
    start();
    // a returning visitor's sign-in is restored in the background: give it a moment before showing the sign-in screen
    setTimeout(function () { waited = true; if (st.phase === "wait") start(); }, 4000);
  }

  function start() {
    if (loading) return;
    var signedIn = RT.account && RT.account.signedIn();
    if (!key && !signedIn) {
      if (RT.account && !RT.account.ready() && !waited) { st.phase = "wait"; return draw(); }
      st.phase = "gate"; return draw();
    }
    load();
  }

  function load() {
    loading = true;
    request("GET", "/api/admin/users").then(function (r) {
      loading = false;
      if (r.ok && r.data) { st.users = r.data.users || []; st.sum = r.data.summary || {}; st.phase = "ready"; st.note = ""; return draw(); }
      if (r.status === 401 && key) { forgetKey(); st.note = "That key didn't work."; }
      else if (r.status === 401) st.note = "Please sign in again.";
      else if (r.status === 403) st.note = "This account isn't an admin.";
      else st.note = "Couldn't load the list. Please try again.";
      st.phase = "gate"; draw();
    }).catch(function () { loading = false; st.phase = "gate"; st.note = "No connection. Please try again."; draw(); });
  }
  function forgetKey() { key = ""; try { sessionStorage.removeItem(KEYK); } catch (e) {} }

  // ---------- drawing ----------
  function draw() {
    if (st.phase === "wait") { host.innerHTML = '<p class="empty">Checking your sign-in&hellip;</p>'; return; }
    if (st.phase === "gate") return drawGate();
    var keep = document.activeElement && document.activeElement.id === "ad-q";
    host.innerHTML = '<div class="ad-head"><h1>Admin</h1><div class="ad-head-btns"><button type="button" class="mine-b" data-a="refresh">Refresh</button>' +
      (key ? '<button type="button" class="mine-b" data-a="forget">Forget key</button>' : "") + "</div></div>" +
      stats() + tools() + '<div id="ad-tbl">' + table() + "</div>" + foot();
    if (keep) { var q = document.getElementById("ad-q"); q.focus(); q.setSelectionRange(q.value.length, q.value.length); }
  }

  function drawGate() {
    var canSign = RT.account && RT.account.enabled();
    host.innerHTML = '<section class="gate"><h1>Admin</h1><p class="acct-sub">Sign in with an admin account, or enter the admin key.</p>' +
      (canSign ? '<button type="button" class="acct-primary" data-a="signin">Sign in</button><div class="acct-or"><span>or</span></div>' : "") +
      '<form data-a="keyform"><label for="ad-key">Admin key</label><input id="ad-key" type="password" autocomplete="off" spellcheck="false" required>' +
      '<button type="submit" class="acct-primary">Open admin</button></form>' +
      '<p class="acct-err" role="alert"' + (st.note ? "" : " hidden") + ">" + esc(st.note) + "</p></section>";
  }

  function card(n, label, sub) {
    return '<div class="stat"><b>' + esc(n) + "</b><span>" + esc(label) + "</span>" + (sub ? "<small>" + esc(sub) + "</small>" : "") + "</div>";
  }
  function stats() {
    var s = st.sum || {};
    return '<div class="stats">' +
      '<div class="stat big"><b>' + esc(s.total) + "</b><span>Registered users</span>" + (s.capped ? "<small>newest " + esc(s.total) + " shown</small>" : "") + "</div>" +
      card(s.newLast7Days, "New in 7 days") + card(s.activeLast7Days, "Active in 7 days") + card(s.withUsername, "Chose a username") +
      card(s.admins, "Admins") + card(s.suspended, "Suspended") + card(s.savedItems, "Saved finds") + card(s.lists, "Lists", s.sharedLists + " shared") + "</div>";
  }

  function tools() {
    var opts = [["all", "Everyone"], ["new", "New in 7 days"], ["admin", "Admins"], ["suspended", "Suspended"], ["nouser", "No username yet"]].map(function (o) {
      return '<option value="' + o[0] + '"' + (o[0] === st.f ? " selected" : "") + ">" + o[1] + "</option>";
    }).join("");
    return '<div class="ad-tools"><input id="ad-q" type="search" placeholder="Find a username or id" aria-label="Find a user" value="' + esc(st.q) + '">' +
      '<select id="ad-f" aria-label="Show">' + opts + "</select></div>";
  }

  function shown() {
    var q = st.q.trim().toLowerCase(), weekAgo = new Date(Date.now() - 6 * 864e5).toISOString().slice(0, 10);
    return st.users.filter(function (u) {
      if (q && (u.username || "").toLowerCase().indexOf(q) < 0 && u.id.toLowerCase().indexOf(q) < 0) return false;
      if (st.f === "admin") return u.role === "admin";
      if (st.f === "suspended") return u.status === "suspended";
      if (st.f === "nouser") return !u.username;
      if (st.f === "new") return (u.createdOn || "") >= weekAgo;
      return true;
    });
  }

  function date(d) {
    if (!d) return "&ndash;";
    try { return esc(new Date(d.slice(0, 10) + "T00:00:00Z").toLocaleDateString(undefined, { day: "numeric", month: "short", year: "numeric", timeZone: "UTC" })); } catch (e) { return esc(d); }
  }
  function how(p) { return p === "google.com" ? "Google" : p === "password" ? "Email link" : p || "?"; }
  function shortId(id) { return id.length > 10 ? id.slice(0, 8) + "…" : id; }

  function table() {
    var rows = shown();
    if (!rows.length) return '<p class="empty">' + (st.users.length ? "Nobody matches." : "Nobody has registered yet.") + "</p>";
    return '<table class="utable"><thead><tr><th>User</th><th>Joined</th><th>Last seen</th><th>Saved</th><th>Lists</th><th>Role</th><th>Status</th><th><span class="sr">Actions</span></th></tr></thead><tbody>' +
      rows.map(function (u) {
        var me = !!u.isMe, susp = u.status === "suspended", isAdmin = u.role === "admin";
        var del = arm === u.id;
        return '<tr data-id="' + esc(u.id) + '"' + (susp ? ' class="susp"' : "") + ">" +
          '<td data-label="User"><b>' + (u.username ? "@" + esc(u.username) : "<i>no username yet</i>") + "</b>" + (me ? ' <span class="badge">you</span>' : "") +
          "<small>" + esc(how(u.provider)) + " &middot; " + esc(shortId(u.id)) + "</small></td>" +
          '<td data-label="Joined">' + date(u.createdOn) + "</td><td data-label=\"Last seen\">" + date(u.lastSeenOn) + "</td>" +
          '<td data-label="Saved">' + u.saved + '</td><td data-label="Lists">' + u.lists + (u.shared ? " <small>(" + u.shared + " shared)</small>" : "") + "</td>" +
          '<td data-label="Role"><select data-a="role" aria-label="Role of ' + esc(u.username || u.id) + '"' + (me ? " disabled" : "") + ">" +
          '<option value="member"' + (isAdmin ? "" : " selected") + '>Member</option><option value="admin"' + (isAdmin ? " selected" : "") + ">Admin</option></select></td>" +
          '<td data-label="Status"><span class="badge ' + (susp ? "bad" : "good") + '">' + (susp ? "Suspended" : "Active") + "</span></td>" +
          '<td class="acts"><button type="button" class="mine-b" data-a="status"' + (me ? " disabled" : "") + ">" + (susp ? "Unsuspend" : "Suspend") + "</button> " +
          '<button type="button" class="mine-b danger' + (del ? " armed" : "") + '" data-a="delete"' + (me || isAdmin ? " disabled" : "") +
          (isAdmin && !me ? ' title="Make them a member first"' : "") + ">" + (del ? "Sure? Click again" : "Delete") + "</button></td></tr>";
      }).join("") + "</tbody></table>";
  }

  function foot() {
    return '<p class="ad-foot"><b>Suspend</b> hides a person&rsquo;s shared lists and stops them sharing, but they can still sign in. <b>Delete</b> removes their saved finds, lists and profile and keeps their username reserved. ' +
      "Their Google or email sign-in lives in Firebase (Authentication, Users): remove it there as well to stop someone returning. The site stores no emails.</p>";
  }

  // ---------- actions ----------
  function row(el) { var tr = el.closest("tr[data-id]"); return tr && tr.getAttribute("data-id"); }
  function find(id) { for (var i = 0; i < st.users.length; i++) if (st.users[i].id === id) return st.users[i]; return null; }
  function seg(s) { return encodeURIComponent(s); }
  function failed(r, fallback) { toast((r.data && r.data.error) || fallback, true); }

  host.addEventListener("click", function (e) {
    var b = e.target.closest("[data-a]");
    if (!b || b.tagName === "SELECT" || b.tagName === "FORM") return;
    var a = b.getAttribute("data-a");
    if (a === "signin") return RT.account && RT.account.open();
    if (a === "refresh") return load();
    if (a === "forget") { forgetKey(); st.phase = "gate"; st.note = ""; return start(); }
    var id = row(b), u = id && find(id);
    if (!u) return;
    if (a === "status") {
      var next = u.status === "suspended" ? "active" : "suspended";
      b.disabled = true;
      request("PUT", "/api/admin/users/" + seg(id) + "/status", { status: next }).then(function (r) {
        if (r.ok) { toast((u.username ? "@" + u.username : "User") + (next === "suspended" ? " suspended." : " is active again.")); load(); }
        else { b.disabled = false; failed(r, "Couldn't change the status."); }
      });
    }
    if (a === "delete") {
      if (arm !== id) {
        arm = id; clearTimeout(armTimer);
        armTimer = setTimeout(function () { arm = ""; if (st.phase === "ready") draw(); }, 4000);
        return draw();
      }
      arm = ""; b.disabled = true;
      request("DELETE", "/api/admin/users/" + seg(id)).then(function (r) {
        if (r.ok) { toast("Deleted."); load(); }
        else { draw(); failed(r, "Couldn't delete."); }
      });
    }
  });

  host.addEventListener("change", function (e) {
    var t = e.target;
    if (t.id === "ad-f") { st.f = t.value; return redrawTable(); }
    if (t.getAttribute("data-a") === "role") {
      var id = row(t), u = id && find(id);
      if (!u) return;
      var before = u.role;
      t.disabled = true;
      request("PUT", "/api/admin/users/" + seg(id) + "/role", { role: t.value }).then(function (r) {
        if (r.ok) { toast((u.username ? "@" + u.username : "User") + " is now " + (t.value === "admin" ? "an admin." : "a member.")); load(); }
        else { t.disabled = false; t.value = before; failed(r, "Couldn't change the role."); }
      });
    }
  });
  host.addEventListener("input", function (e) { if (e.target.id === "ad-q") { st.q = e.target.value; redrawTable(); } });
  host.addEventListener("submit", function (e) {
    if (e.target.getAttribute("data-a") !== "keyform") return;
    e.preventDefault();
    var v = e.target.querySelector("input").value.trim();
    if (!v) return;
    key = v; try { sessionStorage.setItem(KEYK, v); } catch (er) {}
    st.note = ""; load();
  });

  function redrawTable() { var t = document.getElementById("ad-tbl"); if (t) t.innerHTML = table(); }
})();
