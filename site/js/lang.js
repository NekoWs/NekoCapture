/* 浏览器语言与页面语言不一致时补一行入口。
   只提示、不跳转：把人正在读的那一页换掉是越权。关掉写进 localStorage，之后不再出现。 */
(function () {
  var KEY = 'fc-langhint';
  var hint = document.getElementById('langHint');
  if (!hint) return;
  var dismissed = false;
  try { dismissed = window.localStorage.getItem(KEY) === 'off'; } catch (e) { dismissed = false; }
  var prefs = (navigator.language || '') + ' ' + (navigator.languages || []).join(',');
  /* 设计审查用 ?hint=1 强制显示，与 ?static=1 同一套审查开关的路子：拍这一行不必先改浏览器语言。 */
  var forced = /[?&]hint=1/.test(window.location.search);
  if (!dismissed && (forced || !/zh/i.test(prefs))) hint.classList.add('is-on');
  hint.querySelector('.langhint__close').addEventListener('click', function () {
    hint.classList.remove('is-on');
    try { window.localStorage.setItem(KEY, 'off'); } catch (e) { /* 隐私模式下写不进去，本次会话已经关掉了 */ }
  });
})();
