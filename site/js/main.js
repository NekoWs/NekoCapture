/* main.js — 装配
   各模块只暴露 init，顺序无关；主题先跑，避免首帧出现错误配色的闪跳。 */
(function () {
  'use strict';

  /* 下载按钮：发布通道还没接上，所以不假装能下载。
     点击是把本地构建产物的路径写进剪贴板，并把提示换成确认文案。 */
  function downloadButton() {
    var btn = document.getElementById('dlBtn');
    var hint = document.getElementById('dlHint');
    if (!btn || !hint) { return; }

    var path = 'src\\Setup\\bin\\x64\\Debug\\net48\\FrameCapture-Setup.exe';
    var original = hint.textContent;

    btn.addEventListener('click', async function () {
      var ok = false;
      try {
        await navigator.clipboard.writeText(path);
        ok = true;
      } catch (e) {
        // file:// 下剪贴板 API 可能被拒；退化成选中文本让用户自己复制
        var range = document.createRange();
        range.selectNodeContents(hint);
        var sel = window.getSelection();
        sel.removeAllRanges();
        sel.addRange(range);
      }

      hint.textContent = ok
        ? '已复制构建产物路径 · ' + path
        : '构建产物路径 · ' + path + '（已选中，可直接复制）';
      hint.classList.add('is-in');

      window.setTimeout(function () {
        hint.textContent = original;
        hint.classList.remove('is-in');
      }, 2600);
    });
  }

  function boot() {
    FC.theme.init();
    FC.reveal.init();
    FC.hero.init();
    FC.tabs.init();
    FC.pointer.init();
    downloadButton();
    document.body.classList.add('is-ready');
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', boot);
  } else {
    boot();
  }
})();
