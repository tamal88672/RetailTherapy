RT.ready(function () {
  var feed = document.getElementById("feed");
  var nav = document.getElementById("nav");
  RT.fillChrome();

  function current() { return decodeURIComponent(location.hash.slice(1)) || "All"; }

  function renderNav() {
    var cur = current();
    nav.innerHTML = ["All"].concat(RT.categories()).map(function (c) {
      return '<a href="#' + encodeURIComponent(c) + '"' + (c === cur ? ' class="on"' : "") + ">" + RT.esc(c) + "</a>";
    }).join("");
  }

  function pros(p) {
    return '<ul class="pros">' + p.pros.map(function (x) { return "<li>" + RT.esc(x) + "</li>"; }).join("") + "</ul>";
  }

  function render() {
    renderNav();
    var list = RT.filter(current());
    if (!list.length) { feed.innerHTML = '<p class="empty">Nothing here yet.</p>'; return; }
    var hero = list[0], rest = list.slice(1);
    var html = '<article class="hero">' + RT.art(hero) +
      "<div>" + (hero.badge ? '<span class="badge">' + RT.esc(hero.badge) + "</span>" : '<span class="badge">Top pick</span>') +
      "<h2>" + RT.esc(hero.title) + "</h2><p>" + RT.esc(hero.blurb) + "</p>" + pros(hero) + RT.cta(hero) + "</div></article>";
    html += rest.map(function (p) {
      return '<article class="row">' + RT.art(p) + "<div>" +
        '<div class="kicker">' + RT.esc(p.category) + (p.badge ? " · " + RT.esc(p.badge) : "") + "</div>" +
        "<h3>" + RT.esc(p.title) + "</h3><p>" + RT.esc(p.blurb) + "</p>" + pros(p) + RT.cta(p) + "</div></article>";
    }).join("");
    feed.innerHTML = html;
    RT.mountAds();
  }

  window.addEventListener("hashchange", render);
  render();
});
