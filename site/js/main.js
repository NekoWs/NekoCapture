/* main.js — 装配
   各模块只暴露 init，顺序无关；主题先跑，避免首帧出现错误配色的闪跳。 */
(function () {
  'use strict';

  function boot() {
    FC.theme.init();
    FC.reveal.init();
    FC.tabs.init();
    FC.pointer.init();
    document.body.classList.add('is-ready');
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', boot);
  } else {
    boot();
  }
})();
