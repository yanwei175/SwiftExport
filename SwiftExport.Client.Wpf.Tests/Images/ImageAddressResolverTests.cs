using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;
using SwiftExport.Contracts.Common;
using SwiftExportClient.Services.Images;

namespace SwiftExport.Client.Wpf.Tests.Images;

/// <summary>验证 URI 目录边界、编码与本地/托管/外部图片分流，不发出图片网络请求。</summary>
[TestClass]
public sealed class ImageAddressResolverTests
{
    /// <summary>托管小图只插入一个固定目录，原图删除已知缩略图目录，查询参数原样保留。</summary>
    [TestMethod]
    [DataRow("a.jpg", "a.jpg")]
    [DataRow("Thumbnails/a.jpg", "a.jpg")]
    [DataRow("%E4%B8%AD%E6%96%87%20%23%25.jpg", "%E4%B8%AD%E6%96%87%20%23%25.jpg")]
    public void ManagedUrls_ShouldResolveBothModes(string suffix, string escapedName)
    {
        using var fixture = new SystemParameterClientFixture();
        using var resolver = new ImageAddressResolver(fixture.Cache);
        const string root = "https://example.test/files/ProductImages/";
        var thumbnail = resolver.Resolve(root + suffix + "?v=42&token=x%2Fy", ImageDisplayMode.Thumbnail, 800)!;
        Assert.AreEqual(root + "Thumbnails/" + escapedName + "?v=42&token=x%2Fy", thumbnail.SourcePath);
        Assert.AreEqual(256, thumbnail.DecodePixelWidth);
        Assert.AreEqual(root + escapedName + "?v=42&token=x%2Fy", resolver.Resolve(root + suffix + "?v=42&token=x%2Fy", ImageDisplayMode.Original, 400)!.SourcePath);
    }

    /// <summary>主机、端口、协议、目录边界或大小写不同的外部图片保持原地址。</summary>
    [TestMethod]
    [DataRow("https://other.test/files/ProductImages/a.jpg")]
    [DataRow("http://example.test/files/ProductImages/a.jpg")]
    [DataRow("https://example.test:444/files/ProductImages/a.jpg")]
    [DataRow("https://example.test/files/ProductImages-other/a.jpg")]
    [DataRow("https://example.test/files/productimages/a.jpg")]
    [DataRow("https://example.test/files/ProductImages/other/a.jpg")]
    public void ExternalUrls_ShouldPassThrough(string url)
    {
        using var fixture = new SystemParameterClientFixture();
        using var resolver = new ImageAddressResolver(fixture.Cache);
        var address = resolver.Resolve(url, ImageDisplayMode.Thumbnail, 800)!;
        Assert.AreEqual(url, address.SourcePath);
        Assert.AreEqual(800, address.DecodePixelWidth);
    }

    /// <summary>本地绝对路径和 UNC 保持解码尺寸，相对路径及非图片协议拒绝。</summary>
    [TestMethod]
    [DataRow(@"D:\图片\a.jpg", true)]
    [DataRow(@"\\server\share\a.jpg", true)]
    [DataRow("a.jpg", false)]
    [DataRow("relative/a.jpg", false)]
    [DataRow("ftp://example.test/a.jpg", false)]
    [DataRow("data:image/png;base64,AAA", false)]
    public void LocalAndUnsupportedSources_ShouldKeepExistingBoundary(string path, bool valid)
    {
        using var fixture = new SystemParameterClientFixture(null);
        using var resolver = new ImageAddressResolver(fixture.Cache);
        var address = resolver.Resolve(path, ImageDisplayMode.Thumbnail, 800);
        Assert.AreEqual(valid, address is not null);
        if (valid) { Assert.AreEqual(path, address!.SourcePath); Assert.AreEqual(800, address.DecodePixelWidth); }
    }

    /// <summary>参数缺失时小图不加载网络原图；显式大图仍可使用保存的完整地址。</summary>
    [TestMethod]
    public void MissingConfiguration_ShouldDeferNetworkOriginalUntilRequested()
    {
        using var fixture = new SystemParameterClientFixture(null);
        using var resolver = new ImageAddressResolver(fixture.Cache);
        const string url = "https://example.test/files/ProductImages/a.jpg";
        Assert.IsNull(resolver.Resolve(url, ImageDisplayMode.Thumbnail, 96));
        Assert.AreEqual(url, resolver.Resolve(url, ImageDisplayMode.Original, 400)!.SourcePath);
        Assert.IsFalse(resolver.TryGetProductBaseUri(out _));
    }

    /// <summary>完整编码保留点号并正确转义特殊字符；危险文件主体不能生成地址。</summary>
    [TestMethod]
    [DataRow(" ABC.001 ", "ABC.001.jpg")]
    [DataRow("中文 #%.001", "中文 #%.001.jpg")]
    [DataRow("../a", null)]
    [DataRow("CON", null)]
    [DataRow("a.", null)]
    [DataRow("", null)]
    public void ProductCode_ShouldUseFullSafeStem(string code, string? expected)
    {
        Assert.AreEqual(expected is not null, ProductImageAddress.TryGetProductFileName(code, out var name));
        Assert.AreEqual(expected, name);
    }
}
