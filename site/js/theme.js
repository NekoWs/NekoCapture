(function () {
  "use strict";
  var value = null;
  try { value = localStorage.getItem("fc-theme"); } catch (_) {}
  if (value !== "light" && value !== "dark") {
    value = matchMedia("(prefers-color-scheme: dark)").matches ? "dark" : "light";
  }
  document.documentElement.dataset.theme = value;
})();
