using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Tools.DbSeed;

namespace SwiftExport.Application.Tests.Systems;

/// <summary>离线验证参数初始化的旧配置推导和实例保护，不连接或写入数据库。</summary>
[TestClass]
public sealed class ProductImageParameterInitializerTests
{
    /// <summary>保留旧 API 虚拟目录并追加图片路由，缺失配置不能猜测公开地址。</summary>
    [TestMethod]
    public void Derivation_ShouldPreserveVirtualDirectoryAndRequireValidSource()
    {
        Assert.IsTrue(ProductImageParameterInitializer.TryDeriveBaseUrl("""{"FileStorage":{"ApiBaseUrl":"https://example.test/erp/"}}""", out var value));
        Assert.AreEqual("https://example.test/erp/files/ProductImages/", value.AbsoluteUri);
        Assert.IsTrue(ProductImageParameterInitializer.TryDeriveBaseUrl("""{"FileStorage":{"ApiBaseUrl":"https://example.test/","ProductImages":{"RequestPath":"/assets/products"}}}""", out value));
        Assert.AreEqual("https://example.test/assets/products/", value.AbsoluteUri);
        Assert.IsFalse(ProductImageParameterInitializer.TryDeriveBaseUrl("{}", out _));
        Assert.IsFalse(ProductImageParameterInitializer.TryDeriveBaseUrl("""{"FileStorage":{"ApiBaseUrl":"https://u:p@example.test/"}}""", out _));
        Assert.IsFalse(ProductImageParameterInitializer.TryDeriveBaseUrl("""{"FileStorage":{"ApiBaseUrl":"https://example.test/","ProductImages":{"RequestPath":"/../bad"}}}""", out _));
    }

    /// <summary>目标实例三项须逐项一致，避免维护工具写错库。</summary>
    [TestMethod]
    public void TargetGuard_ShouldRejectMismatchedHostPortOrDatabase()
    {
        const string connection = "Host=example.test;Port=5432;Database=parameter_tests;Username=test";
        ProductImageParameterInitializer.ValidateTarget(connection, "example.test", "5432", "parameter_tests");
        Assert.Throws<InvalidOperationException>(() => ProductImageParameterInitializer.ValidateTarget(connection, "other.test", "5432", "parameter_tests"));
        Assert.Throws<InvalidOperationException>(() => ProductImageParameterInitializer.ValidateTarget(connection, "example.test", "5433", "parameter_tests"));
        Assert.Throws<InvalidOperationException>(() => ProductImageParameterInitializer.ValidateTarget(connection, "example.test", "5432", "production"));
    }
}
