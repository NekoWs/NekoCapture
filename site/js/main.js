(function () {
  "use strict";
  var root = document.documentElement;
  var toggle = document.getElementById("theme-toggle");
  function paintThemeButton() {
    toggle.textContent = root.dataset.theme === "dark" ? toggle.dataset.light : toggle.dataset.dark;
    toggle.setAttribute("aria-pressed", String(root.dataset.theme === "dark"));
    document.querySelector('meta[name="theme-color"]').content = root.dataset.theme === "dark" ? "#171b21" : "#f5f7fa";
  }
  toggle.addEventListener("click", function () {
    root.dataset.theme = root.dataset.theme === "dark" ? "light" : "dark";
    try { localStorage.setItem("fc-theme", root.dataset.theme); } catch (_) {}
    paintThemeButton();
  });
  matchMedia("(prefers-color-scheme: dark)").addEventListener("change", function (event) {
    var saved = null;
    try { saved = localStorage.getItem("fc-theme"); } catch (_) {}
    if (saved) return;
    root.dataset.theme = event.matches ? "dark" : "light";
    paintThemeButton();
  });
  paintThemeButton();

  document.querySelectorAll("[data-tabs]").forEach(function (group) {
    var list = group.querySelector('[role="tablist"]');
    var tabs = Array.from(list.querySelectorAll('[role="tab"]'));
    function select(tab, focus) {
      tabs.forEach(function (item) {
        var active = item === tab;
        item.classList.toggle("is-active", active);
        item.setAttribute("aria-selected", String(active));
        item.tabIndex = active ? 0 : -1;
        var panel = document.getElementById(item.getAttribute("aria-controls"));
        panel.hidden = !active;
        panel.classList.toggle("is-active", active);
      });
      if (focus) tab.focus();
    }
    tabs.forEach(function (tab, index) {
      tab.addEventListener("click", function () { select(tab, false); });
      tab.addEventListener("keydown", function (event) {
        var vertical = list.getAttribute("aria-orientation") === "vertical";
        var forward = vertical ? "ArrowDown" : "ArrowRight";
        var backward = vertical ? "ArrowUp" : "ArrowLeft";
        var next = null;
        if (event.key === forward) next = (index + 1) % tabs.length;
        if (event.key === backward) next = (index + tabs.length - 1) % tabs.length;
        if (event.key === "Home") next = 0;
        if (event.key === "End") next = tabs.length - 1;
        if (next === null) return;
        event.preventDefault();
        select(tabs[next], true);
      });
    });
  });

  var request = new AbortController();
  var timer = setTimeout(function () { request.abort(); }, 5000);
  fetch("https://api.github.com/repos/NekoWs/NekoCapture/releases/latest", { signal: request.signal })
    .then(function (response) { if (!response.ok) throw new Error("Release unavailable"); return response.json(); })
    .then(function (release) {
      if (!/^v?\d+\.\d+\.\d+$/.test(release.tag_name) || release.draft || release.prerelease) return;
      var asset = (release.assets || []).find(function (item) {
        return /^NekoCapture-Setup-\d+\.\d+\.\d+-x64\.exe$/.test(item.name);
      });
      if (!asset || !asset.browser_download_url) return;
      var url = new URL(asset.browser_download_url);
      if (url.origin !== "https://github.com" || !url.pathname.startsWith("/NekoWs/NekoCapture/releases/download/")) return;
      document.querySelectorAll(".download-link").forEach(function (link) { link.href = url.href; });
      document.querySelectorAll(".release-status").forEach(function (label) {
        label.textContent = label.dataset.prefix + " " + release.tag_name + " · " + label.dataset.suffix;
      });
    })
    .catch(function () {})
    .finally(function () { clearTimeout(timer); });
})();
