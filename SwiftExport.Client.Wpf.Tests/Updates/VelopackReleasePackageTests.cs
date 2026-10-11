using System.IO;
using System.Security.Cryptography;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Velopack;
using Velopack.Locators;
using Velopack.Sources;

namespace SwiftExport.Client.Wpf.Tests.Updates;

/// <summary>对显式提供的本地发布产物验证真实索引、下载及哈希，不安装或重启进程。</summary>
[TestClass]
public sealed class VelopackReleasePackageTests
{
    public TestContext TestContext { get; set; } = null!;

    /// <summary>使用真实 Velopack 引擎验证安装版本可升级至索引中的最新包。</summary>
    [TestMethod]
    [TestCategory("VelopackPackage")]
    public async Task GeneratedPackages_CanBeDiscoveredDownloadedAndVerified()
    {
        var releaseRoot = TestContext.Properties["ClientReleaseDirectory"] as string;
        if (string.IsNullOrWhiteSpace(releaseRoot)) Assert.Inconclusive("需要显式提供 ClientReleaseDirectory 本地发布目录。");
        var feed = VelopackAssetFeed.FromJson(await File.ReadAllTextAsync(Path.Combine(releaseRoot, "releases.win.json")));
        var oldest = feed.Assets.Where(asset => asset.Type == VelopackAssetType.Full).MinBy(asset => asset.Version)!;
        var latest = feed.Assets.Where(asset => asset.Type == VelopackAssetType.Full).MaxBy(asset => asset.Version)!;
        Assert.IsTrue(latest.Version > oldest.Version, "需先生成至少两个递增版本。");
        var testRoot = Path.Combine(Path.GetTempPath(), "SwiftExportUpdateTest", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testRoot);
        try
        {
            var packages = Path.Combine(testRoot, "packages");
            Directory.CreateDirectory(packages);
            var locator = new TestVelopackLocator("SwiftExportClient", oldest.Version.ToString(), packages,
                testRoot, testRoot, Path.Combine(testRoot, "Update.exe"), "win");
            var manager = new UpdateManager(new SimpleFileSource(new DirectoryInfo(releaseRoot)), locator: locator);
            var update = await manager.CheckForUpdatesAsync();
            Assert.IsNotNull(update);
            Assert.AreEqual(latest.Version, update.TargetFullRelease.Version);
            await manager.DownloadUpdatesAsync(update);
            var downloadedPath = Path.Combine(packages, latest.FileName);
            Assert.IsTrue(File.Exists(downloadedPath));
            Assert.AreEqual(latest.Size, new FileInfo(downloadedPath).Length);
            await using var downloaded = File.OpenRead(downloadedPath);
            Assert.AreEqual(latest.SHA256, Convert.ToHexString(await SHA256.HashDataAsync(downloaded)), true);
            Assert.AreEqual(latest.Version, manager.UpdatePendingRestart?.Version);
        }
        finally
        {
            // 只清理本测试刚创建的随机临时子目录，不触碰发布目录或用户安装目录。
            Directory.Delete(testRoot, recursive: true);
        }
    }
}
