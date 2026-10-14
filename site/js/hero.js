/* hero.js — 首屏的捕获序列
   把图标里的「四角取景框 + 中心点」演成一次真实捕获：取景框落位 → 撑开选区 → 描边闪光 → 轻提示。
   每一段的时长都取 Motion 常量，和桌面端按下热键后看到的是同一种节奏。 */
window.FC = window.FC || {};

FC.hero = (function () {
  var M = FC.Motion;

  function el(id) { return document.getElementById(id); }

  function tick(index) {
    var ticks = document.querySelectorAll('.stage__timeline .tick');
    Array.prototype.forEach.call(ticks, function (t, i) {
      t.classList.toggle('is-on', i === index);
    });
  }

  function clear(node) {
    if (!node) { return; }
    node.classList.remove('is-in', 'is-out', 'is-open', 'is-flash');
  }

  async function sequence(stage, reticle, sel, snack) {
    // 归零：强制回流，动画才会真的从头播
    clear(reticle); clear(sel); clear(snack);
    void stage.offsetWidth;

    // 1) 四角依次落位，中心点最后弹入
    tick(0);
    reticle.classList.add('is-in');
    await FC.wait(M.Stagger * 3 + M.Spring);

    // 2) 取景框退场，选区从左上角撑开
    reticle.classList.remove('is-in');
    reticle.classList.add('is-out');
    sel.classList.add('is-open');
    await FC.wait(M.Enter + M.Quick);

    // 3) 捕获确认：描边闪光单向衰减，不回头
    tick(1);
    sel.classList.add('is-flash');
    await FC.wait(M.Flash);
    sel.classList.remove('is-open', 'is-flash');

    // 4) 轻提示从下方抬起，读完再淡出收起
    tick(2);
    snack.classList.add('is-in');
    await FC.wait(1800);
    snack.classList.remove('is-in');
    snack.classList.add('is-out');
    await FC.wait(M.Dissolve + 700);
  }

  function init() {
    var stage = document.getElementById('stageWindow');
    var reticle = document.getElementById('reticle');
    var sel = document.getElementById('sel');
    var snack = document.getElementById('snack');
    if (!stage || !reticle || !sel || !snack) { return; }

    if (FC.reduced) {
      // 减少动效：停在「选区已撑开」这一帧，取景框与选区都摆好
      reticle.classList.add('is-in');
      sel.classList.add('is-open');
      return;
    }

    if (FC.staticMode) {
      // 静态模式停在捕获那一刻。轻提示是 1.8 秒的瞬时态，冻在页面上只会压住选区描边
      reticle.classList.add('is-in');
      sel.classList.add('is-open');
      return;
    }

    var alive = true;

    async function loop() {
      while (alive) {
        // 页面不可见时退避到 600ms 一跳，不空转
        if (document.visibilityState === 'visible') {
          await sequence(stage, reticle, sel, snack);
        } else {
          await FC.wait(600);
        }
      }
    }

    loop();
  }

  return { init: init };
})();
