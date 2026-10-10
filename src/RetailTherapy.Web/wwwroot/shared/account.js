// Sign-in: Google and "email me a link". Plain script, loads after wish.js.
//
// - Does nothing (and loads nothing) until the site has Auth__ProjectId and Auth__ApiKey set; then a "Sign in" button
//   appears in the top bar where the page has a [data-account] element.
// - Sign-in itself is Firebase Authentication. Its small browser library is only downloaded when someone signs in,
//   or when a returning visitor needs their session restored, so everyone else pays nothing.
// - The site stores a random account id, a username (chosen once, never changed) and the saved list. No email, no name.
(function () {
  "use strict";
  var RT = window.RT;
  var slot = document.querySelector("[data-account]");
  if (!RT || !RT.wish || !slot) return;

  var HINT = "rt-signed-in";     // "someone was signed in here": lets a returning visitor see their avatar before the library loads
  var NAMEKEY = "rt-acct-u";     // last known username, only to paint the button before the library has loaded
  var MAILKEY = "rt-email-link"; // the email a sign-in link was sent to (so opening the link here needs no retyping)
  var SDK = "https://www.gstatic.com/firebasejs/10.14.1/";

  var cfg = null, lib = null, auth = null, user = null, me = null;
  var dlg = null, menuOpen = false, wasIn = false, justSignedIn = false, view = "", authReady = false;

  function get(k) { try { return localStorage.getItem(k); } catch (e) { return null; } }
  function put(k, v) { try { if (v == null) localStorage.removeItem(k); else localStorage.setItem(k, v); } catch (e) {} }
  var esc = RT.esc;

  // ---------- Firebase library (loaded on demand) ----------
  function sdk() {
    if (lib) return lib;
    lib = Promise.all([import(SDK + "firebase-app.js"), import(SDK + "firebase-auth.js")]).then(function (m) {
      var app = m[0].initializeApp({ apiKey: cfg.apiKey, authDomain: cfg.authDomain, projectId: cfg.projectId, appId: cfg.appId || undefined });
      auth = m[1].getAuth(app);
      m[1].onAuthStateChanged(auth, onUser);
      return m[1];
    });
    lib.catch(function () { lib = null; });
    return lib;
  }

  function token() { return user ? user.getIdToken() : Promise.resolve(null); }

  // Calls our own API as the signed-in person. Resolves { ok, status, data }.
  function api(method, path, body) {
    return token().then(function (t) {
      if (!t) throw new Error("signed out");
      var h = { Authorization: "Bearer " + t };
      if (body) h["Content-Type"] = "application/json";
      return fetch(path, { method: method, headers: h, body: body ? JSON.stringify(body) : undefined });
    }).then(function (r) {
      return r.text().then(function (txt) {
        var d = null; try { d = txt ? JSON.parse(txt) : null; } catch (e) {}
        return { ok: r.ok, status: r.status, data: d };
      });
    });
  }

  // ---------- who is signed in ----------
  function onUser(u) {
    user = u;
    authReady = true;
    if (u) {
      wasIn = true;
      put(HINT, u.uid);
      render();
      RT.wish.attach(u.uid, token);
      loadMe();
    } else {
      if (wasIn) RT.wish.detach();      // signed out: the list lives in the account, leave this browser clean
      wasIn = false; me = null;
      put(HINT, null); put(NAMEKEY, null);
      render();
    }
  }

  function loadMe() {
    return api("GET", "/api/me").then(function (r) {
      if (!r.ok) throw new Error("status " + r.status);
      me = r.data;
      put(NAMEKEY, me.username || "");
      render();
      if (justSignedIn) {
        justSignedIn = false;
        if (!me.username) openDialog("username"); else closeDialog();
      }
    }).catch(function () {
      me = null; render();
      if (justSignedIn) { justSignedIn = false; closeDialog(); }
    });
  }

  // ---------- the button in the top bar ----------
  var PERSON = '<svg viewBox="0 0 24 24" aria-hidden="true"><circle cx="12" cy="8" r="4"/><path d="M4 21c0-4.2 3.6-7 8-7s8 2.8 8 7"/></svg>';

  function initial() {
    var n = (me && me.username) || get(NAMEKEY) || "";
    return n ? n.charAt(0) : "";
  }

  function render() {
    if (!cfg || !cfg.enabled) { slot.hidden = true; return; }
    slot.hidden = false;
    var signedIn = !!user || (!authReady && !!get(HINT));  // a returning visitor sees the avatar straight away
    if (!signedIn) {
      slot.innerHTML = '<button type="button" class="acct-btn" data-acct="open-signin" aria-haspopup="dialog">' + PERSON + "<span>Sign in</span></button>";
      return;
    }
    var noName = !!me && !me.username;
    var ch = initial();
    slot.innerHTML = '<button type="button" class="acct-btn acct-av" data-acct="menu" aria-haspopup="menu" aria-expanded="' + menuOpen +
      '" aria-label="Your account' + (me && me.username ? ": @" + esc(me.username) : "") + '">' +
      (ch ? esc(ch) : PERSON) + (noName ? '<i class="dot" title="Choose a username"></i>' : "") + "</button>" +
      (menuOpen ? menuHtml() : "");
  }

  function menuHtml() {
    var head = me && me.username
      ? '<div class="acct-who">@' + esc(me.username) + "</div>"
      : me ? '<button type="button" role="menuitem" data-acct="pick">Choose a username</button>'
           : '<div class="acct-who muted">Account unavailable right now</div>';
    return '<div class="acct-menu" role="menu">' + head +
      '<button type="button" role="menuitem" data-acct="signout">Sign out</button>' +
      '<button type="button" role="menuitem" class="danger" data-acct="delete">Delete account…</button></div>';
  }

  function setMenu(open) {
    menuOpen = open;
    render();
    if (open) { var f = slot.querySelector(".acct-menu button"); if (f) f.focus(); }
  }

  // ---------- dialogs ----------
  function ensureDialog() {
    if (dlg) return dlg;
    dlg = document.createElement("dialog");
    dlg.className = "acct-dialog";
    dlg.setAttribute("aria-labelledby", "acct-h");
    document.body.appendChild(dlg);
    dlg.addEventListener("click", function (e) {
      if (e.target === dlg) return closeDialog();                     // click on the dimmed backdrop
      var a = e.target.closest("[data-acct]");
      if (a) act(a.getAttribute("data-acct"), a);
    });
    dlg.addEventListener("submit", function (e) {
      e.preventDefault();
      var f = e.target.getAttribute("data-form");
      if (f === "email") submitEmail(e.target);
      else if (f === "confirm") submitConfirm(e.target);
      else if (f === "name") submitName(e.target);
    });
    dlg.addEventListener("input", function (e) {
      if (e.target.id === "acct-name") checkName(e.target);
    });
    dlg.addEventListener("close", function () { view = ""; });
    return dlg;
  }

  function closeDialog() { if (dlg && dlg.open) dlg.close(); view = ""; }

  var X = '<button type="button" class="acct-x" data-acct="close" aria-label="Close">&times;</button>';
  var FINE = '<p class="acct-fine">Your email stays with Google\'s sign-in service. This site keeps only a username and your saved lists. <a href="/legal.html">Privacy</a></p>';
  var ERR = '<p class="acct-err" role="alert" hidden></p>';

  function body(name, arg) {
    if (name === "signin") {
      return X + '<h2 id="acct-h">Sign in</h2><p class="acct-sub">Keep your saved finds on every device. No password to remember.</p>' +
        '<button type="button" class="acct-google" data-acct="google">Continue with Google</button>' +
        '<div class="acct-or"><span>or</span></div>' +
        '<form data-form="email"><label for="acct-email">Email me a sign-in link</label>' +
        '<input id="acct-email" type="email" autocomplete="email" inputmode="email" required placeholder="you@example.com" value="' + esc(arg || get(MAILKEY) || "") + '">' +
        '<button type="submit" class="acct-primary">Send the link</button></form>' + ERR + FINE;
    }
    if (name === "sent") {
      return X + '<h2 id="acct-h">Check your email</h2><p class="acct-sub">We sent a sign-in link to <b>' + esc(arg) + '</b>. Open it to finish, on this device or another one.</p>' +
        '<p class="acct-fine">Nothing there? Look in spam, or <button type="button" class="acct-link" data-acct="again">try another address</button>.</p>';
    }
    if (name === "confirm") {
      return X + '<h2 id="acct-h">Confirm your email</h2><p class="acct-sub">You opened the link on a different device, so please type the email address you used.</p>' +
        '<form data-form="confirm"><label for="acct-email2">Email</label><input id="acct-email2" type="email" autocomplete="email" inputmode="email" required>' +
        '<button type="submit" class="acct-primary">Finish signing in</button></form>' + ERR;
    }
    if (name === "username") {
      return X + '<h2 id="acct-h">Pick your username</h2><p class="acct-sub">Other shoppers will see this name on lists you share. Choose carefully: <b>you can\'t change it later.</b></p>' +
        '<form data-form="name"><label for="acct-name">Username</label><div class="acct-at"><span aria-hidden="true">@</span>' +
        '<input id="acct-name" maxlength="20" autocomplete="off" autocapitalize="none" spellcheck="false" required aria-describedby="acct-hint"></div>' +
        '<p id="acct-hint" class="acct-hint" aria-live="polite">3 to 20 characters: letters, numbers and _ (start with a letter).</p>' +
        '<button type="submit" class="acct-primary" disabled>Claim this username</button>' +
        '<button type="button" class="acct-link" data-acct="close">Not now</button></form>' + ERR;
    }
    if (name === "delete") {
      return X + '<h2 id="acct-h">Delete your account?</h2><p class="acct-sub">This removes your saved lists and signs you out everywhere. <b>This can\'t be undone</b>, and your username stays reserved so nobody else can use it.</p>' +
        '<button type="button" class="acct-danger" data-acct="delete-yes">Yes, delete my account</button>' +
        '<button type="button" class="acct-link" data-acct="close">Cancel</button>' + ERR;
    }
    // "message"
    return X + '<h2 id="acct-h">' + esc(arg && arg.title || "Sign in") + '</h2><p class="acct-sub">' + esc(arg && arg.text || "") + "</p>" +
      '<button type="button" class="acct-primary" data-acct="open-signin">' + esc(arg && arg.button || "Try again") + "</button>";
  }

  function openDialog(name, arg) {
    var d = ensureDialog();
    view = name;
    setMenuClosed();
    d.innerHTML = body(name, arg);
    if (!d.open) { if (d.showModal) d.showModal(); else d.setAttribute("open", ""); }
    var first = d.querySelector("input, .acct-google, .acct-primary, .acct-danger");
    if (first) setTimeout(function () { try { first.focus(); } catch (e) {} }, 30);
  }

  function setMenuClosed() { if (menuOpen) { menuOpen = false; render(); } }

  function showError(msg) {
    var e = dlg && dlg.querySelector(".acct-err");
    if (e) { e.textContent = msg; e.hidden = false; }
    else openDialog("message", { title: "Something went wrong", text: msg, button: "Try again" });
  }

  function busy(on) {
    if (!dlg) return;
    Array.prototype.forEach.call(dlg.querySelectorAll("button, input"), function (b) { if (b.getAttribute("data-acct") !== "close") b.disabled = on; });
    if (!on) { var n = dlg.querySelector("#acct-name"); if (n) checkName(n); }
  }

  // Friendly text for the errors people can actually fix.
  function explain(e) {
    var c = e && e.code || "";
    if (c === "auth/popup-closed-by-user" || c === "auth/cancelled-popup-request") return "";
    if (c === "auth/popup-blocked") return "Your browser blocked the sign-in window. Allow pop-ups for this site and try again, or use the email link instead.";
    if (c === "auth/network-request-failed") return "No connection. Check your internet and try again.";
    if (c === "auth/invalid-email") return "That email address doesn't look right.";
    if (c === "auth/expired-action-code" || c === "auth/invalid-action-code") return "That link has expired or was already used. Please request a new one.";
    if (c === "auth/too-many-requests") return "Too many tries. Please wait a few minutes and try again.";
    if (c === "auth/unauthorized-domain") return "This web address isn't allowed to sign in yet. (Site owner: add it under Authentication, Settings, Authorized domains in Firebase.)";
    if (c === "auth/operation-not-allowed") return "This sign-in method isn't switched on yet. (Site owner: enable it in Firebase, Authentication.)";
    if (c === "auth/requires-recent-login") return "For safety, please sign out, sign in again, and then delete your account straight away.";
    return "That didn't work. Please try again.";
  }
  function fail(e) {
    busy(false);
    var m = explain(e);
    if (m) showError(m);
  }

  // ---------- actions ----------
  function act(name, el) {
    if (name === "close") return closeDialog();
    if (name === "open-signin") return openDialog("signin");
    if (name === "again") return openDialog("signin");
    if (name === "google") return google();
    if (name === "delete-yes") return deleteAccount();
  }

  function google() {
    busy(true);
    justSignedIn = true;
    sdk().then(function (B) { return B.signInWithPopup(auth, new B.GoogleAuthProvider()); })
      .catch(function (e) { justSignedIn = false; fail(e); });
  }

  function submitEmail(form) {
    var email = form.querySelector("input").value.trim();
    if (!email) return;
    busy(true);
    sdk().then(function (B) {
      return B.sendSignInLinkToEmail(auth, email, { url: location.origin + "/", handleCodeInApp: true });
    }).then(function () { put(MAILKEY, email); openDialog("sent", email); })
      .catch(fail);
  }

  function submitConfirm(form) {
    var email = form.querySelector("input").value.trim();
    if (!email) return;
    finishLink(email);
  }

  function finishLink(email) {
    busy(true);
    justSignedIn = true;
    return sdk().then(function (B) { return B.signInWithEmailLink(auth, email, location.href); })
      .then(function () { put(MAILKEY, null); cleanUrl(); })
      .catch(function (e) {
        justSignedIn = false; cleanUrl();
        var m = explain(e);
        openDialog("message", { title: "Couldn't sign you in", text: m || "That didn't work. Please try again.", button: "Get a new link" });
      });
  }

  function cleanUrl() { try { history.replaceState(null, "", location.pathname + location.hash); } catch (e) {} }

  // The page was opened from the sign-in email.
  function checkLink() {
    sdk().then(function (B) {
      if (!B.isSignInWithEmailLink(auth, location.href)) return;
      var email = get(MAILKEY);
      if (email) finishLink(email); else openDialog("confirm");
    }).catch(function () {});
  }

  // Username: checks while typing (waits for a pause), claims once.
  var nameTimer = null, nameSeq = 0, nameOk = false;
  var SHAPE = /^[a-z][a-z0-9_]{2,19}$/;

  function setHint(text, kind) {
    var h = dlg && dlg.querySelector("#acct-hint");
    if (!h) return;
    h.textContent = text; h.className = "acct-hint" + (kind ? " " + kind : "");
  }
  function checkName(input) {
    var v = input.value.toLowerCase().replace(/\s+/g, "");
    if (v !== input.value) input.value = v;
    var btn = dlg.querySelector('form[data-form="name"] .acct-primary');
    nameOk = false; if (btn) btn.disabled = true;
    clearTimeout(nameTimer);
    if (!v) return setHint("3 to 20 characters: letters, numbers and _ (start with a letter).", "");
    if (!SHAPE.test(v) || v.indexOf("__") > -1 || /_$/.test(v)) {
      return setHint(v.length < 3 ? "Keep going, at least 3 characters." : "Start with a letter, then letters, numbers or single _ only.", "bad");
    }
    setHint("Checking…", "");
    var seq = ++nameSeq;
    nameTimer = setTimeout(function () {
      api("GET", "/api/me/username/available?name=" + encodeURIComponent(v)).then(function (r) {
        if (seq !== nameSeq || !dlg) return;
        if (!r.ok) return setHint("Couldn't check right now. Try again in a moment.", "bad");
        if (r.data.available) {
          nameOk = true;
          setHint("@" + v + " is free. Remember, you can't change it later.", "good");
          var b = dlg.querySelector('form[data-form="name"] .acct-primary'); if (b) b.disabled = false;
        } else setHint(r.data.reason || "That name isn't available.", "bad");
      }).catch(function () { if (seq === nameSeq) setHint("Couldn't check right now. Try again in a moment.", "bad"); });
    }, 350);
  }

  function submitName(form) {
    var v = form.querySelector("#acct-name").value.trim();
    if (!nameOk || !v) return;
    busy(true);
    api("POST", "/api/me/username", { name: v }).then(function (r) {
      if (r.ok) {
        me = me || {}; me.username = r.data.username; put(NAMEKEY, me.username);
        closeDialog(); render();
        return;
      }
      busy(false);
      showError((r.data && r.data.error) || "Couldn't save that username. Please try again.");
    }).catch(function () { busy(false); showError("No connection. Please try again."); });
  }

  function signOut() {
    setMenuClosed();
    sdk().then(function (B) { return B.signOut(auth); }).catch(function () {});
  }

  function deleteAccount() {
    busy(true);
    var saved = null;
    token().then(function (t) {
      saved = t;
      return sdk();
    }).then(function (B) { return B.deleteUser(user); })              // the sign-in itself first (may ask for a fresh sign-in)
      .then(function () {
        // The token stays valid for a while after the account is gone, which is what lets us remove the site's data now.
        return fetch("/api/me", { method: "DELETE", headers: { Authorization: "Bearer " + saved } }).catch(function () {});
      })
      .then(function () { closeDialog(); })
      .catch(fail);
  }

  // ---------- page events ----------
  slot.addEventListener("click", function (e) {
    var b = e.target.closest("[data-acct]");
    if (!b) return;
    var a = b.getAttribute("data-acct");
    if (a === "open-signin") return openDialog("signin");
    if (a === "menu") {
      if (!user && !me) {                                   // returning visitor: library is still loading
        sdk().then(function () { setMenu(!menuOpen); }).catch(function () { openDialog("signin"); });
        return;
      }
      return setMenu(!menuOpen);
    }
    if (a === "pick") return openDialog("username");
    if (a === "signout") return signOut();
    if (a === "delete") { setMenuClosed(); return openDialog("delete"); }
  });
  document.addEventListener("click", function (e) {
    // composedPath is taken when the click happens, so it still names the slot after a re-render removed the clicked button
    if (menuOpen && e.composedPath().indexOf(slot) < 0) setMenu(false);
  });
  document.addEventListener("keydown", function (e) {
    if (e.key === "Escape" && menuOpen) { setMenu(false); var m = slot.querySelector(".acct-btn"); if (m) m.focus(); }
  });

  // ---------- start ----------
  fetch("/api/site").then(function (r) { return r.json(); }).then(function (s) {
    var a = s && s.auth;
    if (!a || !a.enabled) { slot.hidden = true; return; }
    cfg = a;
    render();
    // Load the library right away only when it is needed: an email link was opened, or a returning visitor needs restoring.
    if (/[?&]oobCode=/.test(location.search)) checkLink();
    else if (get(HINT)) {
      var go = function () { sdk().catch(function () {}); };
      if (window.requestIdleCallback) requestIdleCallback(go, { timeout: 2500 }); else setTimeout(go, 800);
    }
  }).catch(function () { slot.hidden = true; });

  RT.account = {
    open: function () { if (cfg && cfg.enabled) openDialog("signin"); },
    signedIn: function () { return !!user; },
    username: function () { return me && me.username || null; }
  };
})();
