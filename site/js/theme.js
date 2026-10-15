/* theme.js — 明暗主题与强调色
   对应桌面端 Theme.Apply(theme, accentHex)：跟随系统、可手动覆盖、切换是交叉淡入而非硬切。 */
window.FC = window.FC || {};

FC.theme = (function () {
  var KEY = 'fc-theme';
  var root = document.documentElement;
  var query = window.matchMedia('(prefers-color-scheme: dark)');

  function stored() {
    try { return localStorage.getItem(KEY); } catch (e) { return null; }
  }

  function save(value) {
    try { localStorage.setItem(KEY, value); } catch (e) { /* 隐私模式下写不了，本次会话内仍然生效 */ }
  }

  function systemTheme() { return query.matches ? 'dark' : 'light'; }

  function current() { return stored() || systemTheme(); }

  function paint(value) {
    root.setAttribute('data-theme', value);
    syncGlyph(value);
  }

  /* 图标字形与工具内一致：E706 太阳=浅色、E708 月亮=深色；按钮显示点下去要去的那个主题 */
  function syncGlyph(value) {
    var el = document.querySelector('#themeToggle .iconbtn__glyph');
    if (!el) { return; }
    var dark = value === 'dark';
    el.textContent = dark ? el.getAttribute('data-glyph-dark') : el.getAttribute('data-glyph-light');
    var btn = document.getElementById('themeToggle');
    if (btn) {
      btn.setAttribute('aria-label', dark ? '切换到浅色主题' : '切换到深色主题');
      btn.setAttribute('title', dark ? '切换到浅色主题' : '切换到深色主题');
    }
  }

  /* 从按钮位置圆形扩散。支持 View Transitions 的浏览器才有扩散，
     不支持的浏览器直接换色——主题可用比好看更重要。 */
  function flip(next, origin) {
    if (origin) {
      var r = origin.getBoundingClientRect();
      root.style.setProperty('--ox', ((r.left + r.width / 2) / window.innerWidth * 100).toFixed(2) + '%');
      root.style.setProperty('--oy', ((r.top + r.height / 2) / window.innerHeight * 100).toFixed(2) + '%');
    }

    if (!document.startViewTransition || FC.reduced) {
      paint(next);
      return;
    }

    root.classList.add('theme-flip');
    var transition = document.startViewTransition(function () { paint(next); });
    transition.finished.finally(function () { root.classList.remove('theme-flip'); });
  }

  function toggle(origin) {
    var next = current() === 'dark' ? 'light' : 'dark';
    save(next);
    flip(next, origin);
  }

  function bind() {
    var btn = document.getElementById('themeToggle');
    if (btn) { btn.addEventListener('click', function () { toggle(btn); }); }

    // 用户没手动选过时，系统改了就跟着改
    query.addEventListener('change', function () { if (!stored()) { paint(systemTheme()); } });
  }

  function init() {
    paint(current());
    bind();
  }

  return { init: init, toggle: toggle, current: current };
})();
