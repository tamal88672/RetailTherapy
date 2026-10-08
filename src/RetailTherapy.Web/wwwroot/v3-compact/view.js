RT.ready(function () {
  var list = document.getElementById("list");
  var tabs = document.getElementById("tabs");
  var sort = document.getElementById("sort");
  var cat = "All";
  RT.fillChrome();
  document.getElementById("upd").textContent = new Date().toLocaleDateString(undefined, { month: "short", day: "numeric", year: "numeric" });

  function renderTabs() {
    tabs.innerHTML = ["All"].concat(RT.categories()).map(function (c) {
      return '<button class="tab' + (c === cat ? " on" : "") + '" data-c="' + RT.esc(c) + '">' + RT.esc(c) + "</button>";
    }).join("");
  }

  function render() {
    var items = RT.filter(cat).slice();
    if (sort.value === "az") items.sort(function (a, b) { return a.title.localeCompare(b.title); });
    if (!items.length) { list.innerHTML = '<p class="empty">Nothing here yet.</p>'; return; }
    list.innerHTML = items.map(function (p, i) {
      return '<article class="row"><div class="rank">' + (i + 1) + "</div>" + RT.art(p) +
        '<div class="info"><h3>' + RT.esc(p.title) + "</h3><p>" + RT.esc(p.blurb) + '</p><div class="tags">' +
        (p.badge ? '<span class="hot">' + RT.esc(p.badge) + "</span>" : "") +
        "<span>" + RT.esc(p.category) + "</span>" +
        p.pros.slice(0, 2).map(function (x) { return "<span>" + RT.esc(x) + "</span>"; }).join("") +
        "</div></div>" + RT.cta(p, "Check on Amazon") + "</article>";
    }).join("");
    RT.mountAds();
  }

  tabs.addEventListener("click", function (e) {
    var b = e.target.closest(".tab");
    if (!b) return;
    cat = b.getAttribute("data-c");
    renderTabs();
    render();
  });
  sort.addEventListener("change", render);

  renderTabs();
  render();
});
