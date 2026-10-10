// Day / night / system theme.
// A small script at the top of the page sets data-theme before the first paint (no white flash). This file adds the
// three-way switch, remembers the choice, and follows the device setting while "system" is selected.
(function () {
  var KEY = "rt-theme";
  var root = document.documentElement;
  var mq = window.matchMedia ? window.matchMedia("(prefers-color-scheme: dark)") : null;

  function saved() {
    try { var v = localStorage.getItem(KEY); return v === "light" || v === "dark" ? v : "system"; }
    catch (e) { return "system"; }
  }
  var mode = saved();

  function resolve(m) { return m === "dark" || (m === "system" && mq && mq.matches) ? "dark" : "light"; }

  function apply(animate) {
    if (animate) {
      root.classList.add("theme-anim");
      setTimeout(function () { root.classList.remove("theme-anim"); }, 450);
    }
    var t = resolve(mode);
    root.setAttribute("data-theme", t);
    var meta = document.querySelector('meta[name="theme-color"]');
    if (meta) meta.setAttribute("content", t === "dark" ? "#0e0518" : "#e8455a");
    Array.prototype.forEach.call(document.querySelectorAll("[data-theme-toggle] button"), function (b) {
      b.setAttribute("aria-pressed", b.getAttribute("data-mode") === mode ? "true" : "false");
    });
  }

  function set(m) {
    mode = m;
    try { if (m === "system") localStorage.removeItem(KEY); else localStorage.setItem(KEY, m); } catch (e) {}
    apply(true);
  }

  var svg = function (inner) {
    return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">' + inner + "</svg>";
  };
  var BUTTONS = [
    { mode: "light", label: "Day", icon: svg('<circle cx="12" cy="12" r="4"/><path d="M12 2v2M12 20v2M4.9 4.9l1.4 1.4M17.7 17.7l1.4 1.4M2 12h2M20 12h2M4.9 19.1l1.4-1.4M17.7 6.3l1.4-1.4"/>') },
    { mode: "system", label: "Match my device", icon: svg('<circle cx="12" cy="12" r="8"/><path d="M12 4a8 8 0 0 1 0 16z" fill="currentColor"/>') },
    { mode: "dark", label: "Night", icon: svg('<path d="M20 14.5A8.5 8.5 0 1 1 9.5 4a7 7 0 0 0 10.5 10.5z"/>') }
  ];

  function mount() {
    Array.prototype.forEach.call(document.querySelectorAll("[data-theme-toggle]"), function (box) {
      box.innerHTML = BUTTONS.map(function (b) {
        return '<button type="button" data-mode="' + b.mode + '" aria-label="' + b.label + '" title="' + b.label + '">' + b.icon + "</button>";
      }).join("");
      box.addEventListener("click", function (e) {
        var btn = e.target.closest("button[data-mode]");
        if (btn) set(btn.getAttribute("data-mode"));
      });
    });
    apply(false);
  }

  if (mq) {
    var onChange = function () { if (mode === "system") apply(false); };
    if (mq.addEventListener) mq.addEventListener("change", onChange); else if (mq.addListener) mq.addListener(onChange);
  }
  // Keep other open tabs in step.
  window.addEventListener("storage", function (e) { if (e.key === KEY) { mode = saved(); apply(false); } });

  if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", mount); else mount();
})();
