RT.ready(function () {
  var grid = document.getElementById("grid");
  var chips = document.getElementById("chips");
  var q = document.getElementById("q");
  var ratios = ["1/1", "4/5", "3/4", "5/6", "2/3", "1/1", "4/5"];
  var cat = "All";
  RT.fillChrome();

  function renderChips() {
    chips.innerHTML = ["All"].concat(RT.categories()).map(function (c) {
      return '<button class="chip' + (c === cat ? " on" : "") + '" data-c="' + RT.esc(c) + '">' + RT.esc(c) + "</button>";
    }).join("");
  }

  function render() {
    var list = RT.filter(cat, q.value);
    if (!list.length) { grid.innerHTML = '<p class="empty">No matches. Try another search.</p>'; return; }
    grid.innerHTML = list.map(function (p, i) {
      var art = RT.art(p, "", "--r:" + ratios[i % ratios.length]).replace(/<\/div>$/, (p.badge ? '<span class="tag">' + RT.esc(p.badge) + "</span>" : "") + "</div>");
      // .tile is the fixed hover area; .card inside it is what pops up (see style.css).
      return '<article class="tile"><div class="card">' + art + '<div class="body"><h3>' + RT.esc(p.title) + "</h3><p>" + RT.esc(p.blurb) + "</p>" + RT.cta(p, "See on Amazon") + "</div></div></article>";
    }).join("");
    RT.mountAds();
  }

  chips.addEventListener("click", function (e) {
    var b = e.target.closest(".chip");
    if (!b) return;
    cat = b.getAttribute("data-c");
    renderChips();
    render();
  });
  q.addEventListener("input", render);

  renderChips();
  render();
});
