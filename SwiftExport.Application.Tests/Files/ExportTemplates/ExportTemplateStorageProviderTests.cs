using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Interfaces.Files;
using SwiftExport.WebApi.Files.ExportTemplates;

namespace SwiftExport.Application.Tests.Files.ExportTemplates;

/// <summary>验证独立模板存储的启动开关、绝对路径与既有文件目录隔离。</summary>
[TestClass]
public sealed class ExportTemplateStorageProviderTests
{
    /// <summary>默认关闭时不验证或创建模板根目录，提供器仍可由宿主稳定解析。</summary>
    [TestMethod]
    public void DisabledConfiguration_ShouldIgnoreUnusedPath()
    {
        var configuration = Configuration(new() { ["FileStorage:ExportTemplates:RootPath"] = "unused-relative" });
        var services = new ServiceCollection().AddConfiguredExportTemplates(configuration);
        using var provider = services.BuildServiceProvider();
        Assert.IsFalse(provider.GetRequiredService<IExportTemplateStorageProvider>().TryGetEnabledStorage(out _));
    }

    /// <summary>启用时要求已存在的绝对目录，不能由启动过程创建真实服务器目录。</summary>
    [TestMethod]
    [DataRow("")]
    [DataRow("relative-root")]
    public void EnabledInvalidRoot_ShouldFailStartup(string path)
    {
        Assert.ThrowsExactly<InvalidOperationException>(() => new ConfiguredExportTemplateStorageProvider(Configuration(new()
        {
            ["FileStorage:ExportTemplates:Enabled"] = "true", ["FileStorage:ExportTemplates:RootPath"] = path
        })));
    }

    /// <summary>同名、包含与被包含的图片、内部状态、临时目录及通用容器全部拒绝。</summary>
    [TestMethod]
    [DataRow("images", "same")]
    [DataRow("images", "nested")]
    [DataRow("images", "parent")]
    [DataRow("legacy", "same")]
    [DataRow("legacy", "nested")]
    [DataRow("legacy", "parent")]
    [DataRow("temporary", "same")]
    [DataRow("temporary", "nested")]
    [DataRow("temporary", "parent")]
    [DataRow("container", "same")]
    [DataRow("container", "nested")]
    [DataRow("container", "parent")]
    public void ReservedRoots_ShouldRejectOverlapInBothDirections(string category, string relation)
    {
        using var scope = new ExportTemplateTestScope();
        var storageRoot = Path.Combine(scope.Root, "base");
        var reserved = Path.Combine(storageRoot, category switch
        {
            "images" => "ProductImages", "legacy" => ".product-image-state",
            "temporary" => ".dvfilestorage-tmp", _ => "GeneralFiles"
        });
        var templateRoot = relation switch { "same" => reserved, "nested" => Path.Combine(reserved, "templates"), _ => storageRoot };
        Directory.CreateDirectory(templateRoot);
        var values = new Dictionary<string, string?>
        {
            ["FileStorage:ExportTemplates:Enabled"] = "true", ["FileStorage:ExportTemplates:RootPath"] = templateRoot,
            ["FileStorage:RootPath"] = storageRoot
        };
        if (category == "container") values["FileStorage:Containers:files:RootPath"] = reserved;
        Assert.ThrowsExactly<InvalidOperationException>(() => new ConfiguredExportTemplateStorageProvider(Configuration(values)));
    }

    /// <summary>同基础根下的安全兄弟目录允许使用，模板服务不能出现在通用容器白名单。</summary>
    [TestMethod]
    public void SeparateSiblingRoots_ShouldCreateIndependentStorage()
    {
        using var scope = new ExportTemplateTestScope();
        var root = Directory.CreateDirectory(Path.Combine(scope.Root, "base", "ExportTemplates")).FullName;
        var values = new Dictionary<string, string?>
        {
            ["FileStorage:ExportTemplates:Enabled"] = "true", ["FileStorage:ExportTemplates:RootPath"] = root,
            ["FileStorage:RootPath"] = Path.Combine(scope.Root, "base"),
            ["FileStorage:Containers:files:RootPath"] = Path.Combine(scope.Root, "GeneralFiles")
        };
        var provider = new ConfiguredExportTemplateStorageProvider(Configuration(values));
        Assert.IsTrue(provider.TryGetEnabledStorage(out var context));
        Assert.IsNotNull(context);
        Assert.AreNotSame(scope.Storage, context.Storage);
        Assert.AreEqual(0, Directory.GetFileSystemEntries(root).Length);
    }

    private static IConfiguration Configuration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
