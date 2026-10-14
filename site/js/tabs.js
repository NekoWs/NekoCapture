/* tabs.js — 演示区标签页
   切换用 StateMs：状态切换比入场干脆，和桌面端开关/选中同速。
   键盘可达：左右箭头换标签，Home/End 跳首尾。 */
window.FC = window.FC || {};

FC.tabs = (function () {
  function init() {
    var tabs = Array.prototype.slice.call(document.querySelectorAll('.tab'));
    var panels = Array.prototype.slice.call(document.querySelectorAll('.panel'));
    if (!tabs.length || !panels.length) { return; }

    function select(tab) {
      tabs.forEach(function (t) {
        var on = t === tab;
        t.classList.toggle('is-on', on);
        t.setAttribute('aria-selected', on ? 'true' : 'false');
        t.tabIndex = on ? 0 : -1;
      });
      panels.forEach(function (p) {
        var on = p.id === tab.dataset.panel;
        p.classList.toggle('is-on', on);
        if (on) {
          // 重新触发落位动画：先摘类，强制回流，再加回
          p.classList.remove('is-on');
          void p.offsetWidth;
          p.classList.add('is-on');
        }
      });
    }

    tabs.forEach(function (tab, i) {
      tab.addEventListener('click', function () { select(tab); });
      tab.addEventListener('keydown', function (e) {
        var next = null;
        if (e.key === 'ArrowRight') { next = tabs[(i + 1) % tabs.length]; }
        if (e.key === 'ArrowLeft') { next = tabs[(i - 1 + tabs.length) % tabs.length]; }
        if (e.key === 'Home') { next = tabs[0]; }
        if (e.key === 'End') { next = tabs[tabs.length - 1]; }
        if (!next) { return; }
        e.preventDefault();
        next.focus();
        select(next);
      });
      if (!tab.classList.contains('is-on')) { tab.tabIndex = -1; }
    });
  }

  return { init: init };
})();
