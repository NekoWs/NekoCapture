namespace FrameCapture.Core;

/// <summary>界面文案目录。</summary>
public static partial class K
{
    /// <summary>安装器的升级与迁移文案。</summary>
    public static class Setup
    {
        public static readonly Str Install = new("setup.install", "安装", "Install");
        public static readonly Str Upgrade = new("setup.upgrade", "升级", "Upgrade");
        public static readonly Str RepairTitle = new("setup.repairTitle", "重新安装 FrameCapture", "Reinstall FrameCapture");
        public static readonly Str Installed = new("setup.installed", "已安装 {0}", "Installed: {0}");
        public static readonly Str Repair = new("setup.repair", "重新安装", "Reinstall");
        public static readonly Str InstallTitle = new("setup.installTitle", "安装 FrameCapture", "Install FrameCapture");
        public static readonly Str UpgradeTitle = new("setup.upgradeTitle", "升级 FrameCapture", "Upgrade FrameCapture");
        public static readonly Str Existing = new("setup.existing", "已安装 {0}，升级到 {1}", "Version {0} installed. Upgrade to {1}");
        public static readonly Str Completed = new("setup.completed", "升级完成", "Upgrade complete");
        public static readonly Str Migrating = new("setup.migrating", "正在升级配置", "Upgrading settings");
        public static readonly Str Failed = new("setup.failed", "升级未完成，请重新运行安装包", "Upgrade did not finish. Run the installer again.");
        public static readonly Str NewerInstalled = new("setup.newerInstalled", "已安装更新的版本", "A newer version is installed");
        public static readonly Str UninstallTitle = new("setup.uninstallTitle", "卸载 FrameCapture", "Uninstall FrameCapture");
        public static readonly Str Uninstall = new("setup.uninstall", "卸载", "Uninstall");
        public static readonly Str ChangeLocation = new("setup.changeLocation", "更换位置", "Change location");
        public static readonly Str RuntimeMissing = new("setup.runtimeMissing", "需要下载 .NET Desktop Runtime，约 56 MB", "Download .NET Desktop Runtime, about 56 MB");
        public static readonly Str RuntimeOld = new("setup.runtimeOld", "需要更新 .NET Desktop Runtime {0}", "Update .NET Desktop Runtime {0}");
        public static readonly Str RemoveTarget = new("setup.removeTarget", "将删除 {0}、快捷方式、自启与个人设置", "Remove {0}, shortcuts, startup registration, and personal settings");
        public static readonly Str Target = new("setup.target", "目标位置 {0}", "Location: {0}");
        public static readonly Str AllUsers = new("setup.allUsers", "所有用户可用", "Available to all users");
        public static readonly Str ChooseLocation = new("setup.chooseLocation", "选择安装位置", "Choose install location");
        public static readonly Str UseLocation = new("setup.useLocation", "安装到此处", "Install here");
        public static readonly Str InvalidLocation = new("setup.invalidLocation", "位置不可用：{0}", "Location unavailable: {0}");
        public static readonly Str AdminRequired = new("setup.adminRequired", "已取消安装授权", "Installation permission cancelled");
        public static readonly Str AdminContinue = new("setup.adminContinue", "正在管理员进程中继续", "Continuing in the administrator process");
        public static readonly Str RuntimeDownloading = new("setup.runtimeDownloading", "正在下载 .NET Desktop Runtime", "Downloading .NET Desktop Runtime");
        public static readonly Str RuntimeProgress = new("setup.runtimeProgress", "运行时下载 {0} / {1} MB", "Runtime download: {0} / {1} MB");
        public static readonly Str RuntimeUnknownProgress = new("setup.runtimeUnknownProgress", "运行时已下载 {0} MB", "Runtime downloaded: {0} MB");
        public static readonly Str RuntimeDownloadFailed = new("setup.runtimeDownloadFailed", "运行时下载失败：{0}", "Runtime download failed: {0}");
        public static readonly Str RuntimeInstalling = new("setup.runtimeInstalling", "正在安装 .NET Desktop Runtime", "Installing .NET Desktop Runtime");
        public static readonly Str RuntimeInstallFailed = new("setup.runtimeInstallFailed", "运行时安装失败 {0}：{1}", "Runtime setup failed {0}: {1}");
        public static readonly Str OpenApp = new("setup.openApp", "打开 FrameCapture", "Open FrameCapture");
        public static readonly Str Error = new("setup.error", "安装错误：{0}", "Installation error: {0}");
        public static readonly Str UninstallAdminCancelled = new("setup.uninstallAdminCancelled", "已取消卸载授权", "Uninstall permission cancelled");
        public static readonly Str UninstallCopyFailed = new("setup.uninstallCopyFailed", "无法准备卸载程序", "Unable to prepare the uninstaller");
    }
}
