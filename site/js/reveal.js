/* reveal.js — 滚动入场与逐项错位
   同一容器内的 [data-stagger] 依次延迟 StaggerMs，与桌面端 CascadeIn 的 leadMs 同一套做法：
   延迟写在 transition-delay 上，元素从第一帧起就持有起始状态，不会先闪一下再动。 */
window.FC = window.FC || {};

FC.reveal = (function () {
  var seen = new WeakSet();

  /* 给同组元素算错位延迟：组 = 最近的共同父级 */
  function delays(nodes) {
    var byParent = new Map();
    nodes.forEach(function (el) {
      if (!el.hasAttribute('data-stagger')) { return; }
      var key = el.parentElement;
      if (!byParent.has(key)) { byParent.set(key, []); }
      byParent.get(key).push(el);
    });
    byParent.forEach(function (group) {
      group.forEach(function (el, i) {
        el.style.setProperty('--delay', (i * FC.Motion.Stagger) + 'ms');
      });
    });
  }

  function show(el) {
    if (seen.has(el)) { return; }
    seen.add(el);
    el.classList.add('is-in');
  }

  /* 标题逐行：每行延迟一个 Stagger，读起来是一行一行落下来 */
  function titleLines() {
    var lines = Array.prototype.slice.call(document.querySelectorAll('.hero__title .line'));
    lines.forEach(function (line, i) {
      line.style.setProperty('--delay', (i * FC.Motion.Stagger) + 'ms');
    });
  }

  function init() {
    var nodes = Array.prototype.slice.call(document.querySelectorAll('[data-reveal]'));
    titleLines();

    if (FC.reduced || FC.staticMode || !('IntersectionObserver' in window)) {
      nodes.forEach(show);
      document.querySelectorAll('.hero__title .line').forEach(function (l) { l.classList.add('is-in'); });
      return;
    }

    delays(nodes);

    var io = new IntersectionObserver(function (entries) {
      entries.forEach(function (entry) {
        if (!entry.isIntersecting) { return; }
        show(entry.target);
        io.unobserve(entry.target);
      });
    }, { rootMargin: '0px 0px -12% 0px', threshold: 0.08 });

    nodes.forEach(function (el) { io.observe(el); });

    // 标题不在 [data-reveal] 的可见判定里，单独交给一个观察器
    var titleIo = new IntersectionObserver(function (entries) {
      entries.forEach(function (entry) {
        if (!entry.isIntersecting) { return; }
        entry.target.querySelectorAll('.line').forEach(show);
        titleIo.unobserve(entry.target);
      });
    }, { threshold: 0.3 });

    var title = document.querySelector('.hero__title');
    if (title) { titleIo.observe(title); }

    /* 兜底：观察器因任何原因没派发（脚本被拦、极端渲染环境、截图器不等），
       视口内的内容也必须可见。动效可以没有，内容不能没有。 */
    window.setTimeout(function () {
      var bottom = window.innerHeight;
      nodes.forEach(function (el) {
        if (el.getBoundingClientRect().top < bottom) { show(el); }
      });
      document.querySelectorAll('.hero__title .line').forEach(show);
    }, 1600);
  }

  return { init: init };
})();
