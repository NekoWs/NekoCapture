namespace FrameCapture.Core;

/// <summary>界面文案目录。</summary>
public static partial class K
{
    /// <summary>更新提示与设置文案。</summary>
    public static class Updates
    {
        public static readonly Str Software = new("updates.software", "软件更新", "Software updates");
        public static readonly Str AutoCheck = new("updates.auto", "自动检查更新", "Check for updates automatically");
        public static readonly Str AutoCheckHint = new("updates.autoHint", "启动时检查新版本", "Check for new versions at startup");
        public static readonly Str Check = new("updates.check", "检查更新", "Check for updates");
        public static readonly Str Checking = new("updates.checking", "正在检查更新…", "Checking for updates…");
        public static readonly Str Cancelling = new("updates.cancelling", "正在取消更新…", "Cancelling update…");
        public static readonly Str Cancelled = new("updates.cancelled", "已取消更新", "Update cancelled");
        public static readonly Str Skipped = new("updates.skipped", "已跳过这个版本", "This version is skipped");
        public static readonly Str Available = new("updates.available", "发现新版本 {0}", "Version {0} is available");
        public static readonly Str Versions = new("updates.versions", "{0} → {1}", "{0} → {1}");
        public static readonly Str Skip = new("updates.skip", "跳过这个版本", "Skip this version");
        public static readonly Str Update = new("updates.update", "更新", "Update");
        public static readonly Str Downloading = new("updates.downloading", "正在下载更新", "Downloading update");
        public static readonly Str Progress = new("updates.progress", "{0:0.0} / {1:0.0} MB", "{0:0.0} / {1:0.0} MB");
        public static readonly Str Waiting = new("updates.waiting", "当前任务结束后安装", "Waiting for current tasks to finish");
        public static readonly Str Installing = new("updates.installing", "正在升级 FrameCapture", "Upgrading FrameCapture");
        public static readonly Str Failed = new("updates.failed", "更新未完成，请重试", "Update could not finish. Try again.");
        public static readonly Str CheckFailed = new("updates.checkFailed", "暂时无法检查更新", "Unable to check for updates");
        public static readonly Str Current = new("updates.current", "已是最新版本", "You're up to date");
        public static readonly Str Retry = new("updates.retry", "重试更新", "Retry update");
        public static readonly Str ConfigUpgrade = new("updates.configUpgrade", "请运行安装包升级配置", "Run the installer to upgrade your settings");
        public static readonly Str ConfigNewer = new("updates.configNewer", "配置来自更新版本，请升级软件", "These settings require a newer version");
    }
}
