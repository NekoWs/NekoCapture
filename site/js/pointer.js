/* pointer.js — 光标与滚动带来的反馈
   卡片镜面高光与轻微倾斜、氛围光跟随、滚动进度、顶栏加深、导航当前项。
   全部节流到一帧一次；减少动效偏好下倾斜直接关掉。 */
window.FC = window.FC || {};

FC.pointer = (function () {
  var MAX_TILT = 4;   // 度。再大就变成"玩具卡片"，不是 Fluent

  function tiltCards() {
    if (FC.reduced) { return; }
    var cards = document.querySelectorAll('[data-tilt]');

    Array.prototype.forEach.call(cards, function (card) {
      var move = FC.perFrame(function (e) {
        var r = card.getBoundingClientRect();
        var x = (e.clientX - r.left) / r.width;
        var y = (e.clientY - r.top) / r.height;
        card.style.setProperty('--mx', (x * 100).toFixed(1) + '%');
        card.style.setProperty('--my', (y * 100).toFixed(1) + '%');
        card.style.transform =
          'perspective(900px) rotateX(' + ((0.5 - y) * MAX_TILT).toFixed(2) + 'deg)' +
          ' rotateY(' + ((x - 0.5) * MAX_TILT).toFixed(2) + 'deg)' +
          ' translateZ(0)';
      });

      card.addEventListener('pointermove', move);
      card.addEventListener('pointerleave', function () {
        card.style.transform = '';
        card.style.setProperty('--mx', '50%');
        card.style.setProperty('--my', '0%');
      });
    });
  }

  function ambient() {
    var layer = document.querySelector('.ambient');
    if (!layer) { return; }
    var move = FC.perFrame(function (e) {
      layer.style.setProperty('--px', (e.clientX / window.innerWidth * 100).toFixed(1) + '%');
      layer.style.setProperty('--py', (e.clientY / window.innerHeight * 100).toFixed(1) + '%');
    });
    window.addEventListener('pointermove', move, { passive: true });
  }

  function progress() {
    var bar = document.createElement('div');
    bar.className = 'progress';
    bar.setAttribute('aria-hidden', 'true');
    document.body.appendChild(bar);

    var topbar = document.getElementById('topbar');

    var onScroll = FC.perFrame(function () {
      var max = document.documentElement.scrollHeight - window.innerHeight;
      var p = max > 0 ? Math.min(1, window.scrollY / max) : 0;
      bar.style.setProperty('--p', p.toFixed(4));
      if (topbar) { topbar.classList.toggle('is-stuck', window.scrollY > 8); }
    });

    window.addEventListener('scroll', onScroll, { passive: true });
    window.addEventListener('resize', onScroll);
    onScroll();
  }

  /* 导航当前项：区块进入视口中段才算"当前"，避免边界抖动 */
  function navActive() {
    var links = Array.prototype.slice.call(document.querySelectorAll('.topnav a'));
    if (!links.length || !('IntersectionObserver' in window)) { return; }

    var map = new Map();
    links.forEach(function (a) {
      var target = document.querySelector(a.getAttribute('href'));
      if (target) { map.set(target, a); }
    });

    var io = new IntersectionObserver(function (entries) {
      entries.forEach(function (entry) {
        var link = map.get(entry.target);
        if (!link) { return; }
        if (entry.isIntersecting) {
          links.forEach(function (l) { l.classList.remove('is-active'); });
          link.classList.add('is-active');
        }
      });
    }, { rootMargin: '-45% 0px -45% 0px' });

    map.forEach(function (_link, target) { io.observe(target); });
  }

  function init() {
    tiltCards();
    ambient();
    progress();
    navActive();
  }

  return { init: init };
})();
