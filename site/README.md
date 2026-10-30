# FrameCapture 宣传页

中文页位于站点根目录，英文页位于 en/。页面为纯静态 HTML、CSS 和 JavaScript，无构建依赖。

- css/site.css 提供响应式布局、明暗主题和减少动态效果的支持。
- js/theme.js 在首帧读取主题偏好；js/main.js 负责主题切换、可键盘操作的页签和最新版本链接。
- assets/shots/ 保存当前软件的中文界面图，英文图位于 assets/shots/en/。
- 下载入口默认指向 GitHub 最新 Release。成功读取最新版本后指向主安装包；请求失败不影响入口。

预览执行 python -m http.server 4173 --bind 127.0.0.1 --directory site。
检查执行 python scripts/check-site.py。

GitHub Pages 地址：https://nekows.github.io/FrameCapture/
Website 工作流在 main 分支的网站文件变动时独立部署，不创建软件版本或 Release。
