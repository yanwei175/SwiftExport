using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Services.Shipping;
using SwiftExport.Contracts.DTOs.Shipping.PackingLists;

namespace SwiftExport.Application.Tests.Shipping;

/// <summary>验证服务端包装计算和包装编辑的幂等内容边界，不访问数据库。</summary>
[TestClass]
public sealed class PackingListPackagingTests
{
    /// <summary>取消整箱向上取整，尾箱可恰好计算为一箱，未知仍为空。</summary>
    [TestMethod]
    public void PackageCountUsesFractionAndKeepsUnknown()
    {
        var method = typeof(SwiftExport.Application.Services.Shipping.PackingLists.PackingListDraftService).GetMethod("CalculatePackageCount", BindingFlags.NonPublic | BindingFlags.Static)!;
        Assert.AreEqual(5.25m, method.Invoke(null, new object?[] { 105m, 20m }));
        Assert.AreEqual(1m, method.Invoke(null, new object?[] { 5m, 5m }));
        Assert.AreEqual(.3333m, method.Invoke(null, new object?[] { 1m, 3m }));
        Assert.IsNull(method.Invoke(null, new object?[] { 5m, null }));
    }

    /// <summary>手填包装和头地点影响幂等内容，客户端伪造箱数不影响服务端派生结果。</summary>
    [TestMethod]
    public void FingerprintIncludesEditablePackagingAndHeaderLocation()
    {
        var method = typeof(SwiftExport.Application.Services.Shipping.PackingLists.PackingListDraftService).GetMethod("Fingerprint", BindingFlags.NonPublic | BindingFlags.Static)!;
        var header = new PackingListDraftHeaderInput();
        var line = new PackingListDraftLineInput { Qty = 5, PcsPerPackage = 5 };
        string Fingerprint() => (string)method.Invoke(null, ["Create", Guid.Empty, 0, header, new[] { line }, Array.Empty<PackingListDeletedLineInput>()])!;
        var original = Fingerprint();
        line.PackageCount = 999;
        Assert.AreEqual(original, Fingerprint());
        line.NwPerPackage = 2;
        Assert.AreNotEqual(original, Fingerprint());
        var packaged = Fingerprint();
        header.ShippingLocationSnapshot = "装柜点";
        Assert.AreNotEqual(packaged, Fingerprint());
    }
}
