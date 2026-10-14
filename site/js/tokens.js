/* tokens.js — 动效时长常量
   与 src/FrameCapture/Core/Fluent.cs 的 Motion 常量同名同值。
   两边不一致时，桌面端与网页的节奏就会分叉——改一边必须改另一边。 */
window.FC = window.FC || {};

FC.Motion = {
  Enter: 190,        // 弹出层/浮层入场
  EditorEnter: 240,  // 大表面入场，慢半拍
  Exit: 150,         // 退场比入场干脆
  Stagger: 110,      // 逐项错位间隔
  Hover: 110,        // 悬停反馈
  State: 150,        // 状态切换
  Theme: 260,        // 主题交叉淡入
  Spring: 260,       // 弹性回弹
  Press: 70,         // 按压，必须短于 Hover
  Quick: 120,        // 即时短程过渡
  Dissolve: 320,     // 瞬时提示退场
  Flash: 450         // 捕获完成描边余晖，单向往落
};

/* 系统「减少动效」偏好。用 matchMedia 实时读，不做一次性快照——
   用户在系统设置里改了，页面下一帧就该照办。 */
FC.reducedQuery = window.matchMedia('(prefers-reduced-motion: reduce)');

Object.defineProperty(FC, 'reduced', {
  get: function () { return FC.reducedQuery.matches; }
});

/* 静态模式：地址栏带 ?static=1 时所有内容直接摆好，不做入场与循环。
   给打印、抓取和截图比对用；真人访问不受影响。 */
FC.staticMode = /[?&]static=1/.test(window.location.search);

/* 按令牌等待：减少动效时直接下一帧回调，序列因此仍然按顺序推进，只是不再"等"。 */
FC.wait = function (ms) {
  return new Promise(function (resolve) {
    setTimeout(resolve, FC.reduced ? 0 : ms);
  });
};

/* 只在元素可见时才跑的循环：切到后台标签页就停，回来接着转。 */
FC.whenVisible = function (run) {
  var stop = false;
  function loop() {
    if (stop) { return; }
    if (document.visibilityState === 'visible') { run(); }
  }
  document.addEventListener('visibilitychange', loop);
  return function () { stop = true; document.removeEventListener('visibilitychange', loop); };
};

/* 节流到一帧一次：光标跟随类效果都走它。 */
FC.perFrame = function (fn) {
  var queued = false;
  return function (event) {
    if (queued) { return; }
    queued = true;
    requestAnimationFrame(function () { queued = false; fn(event); });
  };
};
