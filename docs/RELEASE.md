# 发布与更新

产品版本唯一来源为 `src/FrameCapture/FrameCapture.csproj`。
GitHub 仓库为 [NekoWs/FrameCapture](https://github.com/NekoWs/FrameCapture)。

## GitHub 设置

在仓库的 Settings → Secrets and variables → Actions 添加 `UPDATE_SIGNING_KEY`。
值为本地 `.release-keys/release.private.pem` 的完整内容。
私钥保存在本机并独立备份，不加入 git；客户端只内嵌 `assets/update-public.pem` 公钥。

安装 GitHub CLI 后也可从仓库目录设置：

```powershell
Get-Content -Raw .release-keys/release.private.pem | gh secret set UPDATE_SIGNING_KEY --repo NekoWs/FrameCapture
```

## 发布

提交代码并更新产品版本后，创建对应的 `v版本号` 标签并推送到 GitHub。

```powershell
git push origin HEAD:main
git push origin v0.9.26
```

`Release` 工作流使用 Windows、.NET 8 与 .NET 10，校验标签和产品版本一致，构建解决方案、检查隔离配置迁移并打包。
附件包含主安装包、视频组件安装器、`SHA256SUMS.txt` 与签名更新清单 `update.json`。
全部附件上传到草稿后才公开发布；已发布版本不覆盖，较早版本不能成为最新发行版。
Actions 页也可手动运行，标签取当前产品版本。

## 客户端

系统设置提供自动检查与手动检查。自动检查在同一次常驻会话中仅执行一次，默认开启。
更新与下载依次尝试 GitHub、GH-Proxy 和 GHFast；连接超时、停滞或速度过低时切换来源。
清单使用 RSA-SHA256 签名，安装包验证签名清单中的 SHA256 与长度；镜像不能修改版本与安装包。

右下角提示提供跳过版本、取消和更新。跳过版本保存在配置中，仅抑制该版本及更早版本的自动提示。
下载可取消；安装等待当前捕获、编辑、导出和保存任务完成，保留最近产物后调用安装器。
安装器已有管理员令牌时直接执行，否则由 Windows 请求 UAC。静默升级不显示安装确认页，完成后启动软件。

## 配置格式

`ConfigVersion` 为整数结构版本，当前为 1；产品版本与结构版本独立。
主程序只读取当前结构，遇到不兼容结构或损坏 JSON 时保留文件并提示。
缺少配置文件时创建当前格式的默认配置。

安装器识别注册表、默认目录与自身目录中的安装版本，复用已安装位置，并拒绝降级覆盖。
升级程序 `FrameCapture.ConfigUpgrade.exe` 在安装过程中迁移当前用户与已知用户配置。
所有配置先验证和生成临时文件，再备份并提交；失败恢复已提交配置，安装器恢复程序目录与卸载登记。
配置备份保存在原文件旁，文件名含目标结构版本和唯一标识。
增加不兼容结构时递增 `CurrentConfigVersion`，在配置升级程序中增加对应的逐版本迁移。

## 本地打包

```powershell
pwsh -NoProfile -File scripts/publish-release.ps1 -Flavor fd -SkipVerify -NoRestart
pwsh -NoProfile -File scripts/sign-update.ps1 -OutDir release -KeyPath .release-keys/release.private.pem
```

界面与安装验证由人工进行。构建结果记录在 `test-out/updates-build.log`，隔离迁移结果见 `test-out/updates-config-contract.log`。安装与 UAC 测试使用隔离配置。
