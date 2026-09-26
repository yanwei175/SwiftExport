using System.Text;
using DvFileStorage.Abstractions;
using SwiftExport.Application.Interfaces.Files;
using SwiftExport.Application.Interfaces.Files.ProductImages;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.WebApi.Files;

namespace SwiftExport.Application.Tests.Files;

/// <summary>使用最小配置和临时目录验证宿主 RootPath 到独立文件存储的 DI 接线。</summary>
[TestClass]
public sealed class FileStorageRegistrationTests
{
    /// <summary>RootPath 缺失或空白时不注册底层存储，且不受图片 Enabled 开关影响。</summary>
    [TestMethod]
    public void MissingOrBlankRoot_DoesNotRegisterStorage()
    {
        foreach (var json in new[]
                 {
                     """{ "FileStorage": { "Enabled": false } }""",
                     """{ "FileStorage": { "Enabled": false, "RootPath": "   " } }"""
                 })
        {
            var services = new ServiceCollection();
            services.AddConfiguredFileStorage(BuildConfiguration(json));
            using var provider = services.BuildServiceProvider();
            Assert.IsNull(provider.GetService<IFileStorage>());
            Assert.IsFalse(provider.GetRequiredService<IFileContainerProvider>()
                .TryGetContainer("files", out _));
        }
    }

    /// <summary>多个合法容器按标识隔离，保持虚拟路径基址和各自上传上限。</summary>
    [TestMethod]
    public async Task ValidContainers_RegisterIndependentStorageSnapshots()
    {
        await using var scope = new TemporaryDirectory();
        var first = Directory.CreateDirectory(System.IO.Path.Combine(scope.Path, "one")).FullName;
        var second = Directory.CreateDirectory(System.IO.Path.Combine(scope.Path, "two")).FullName;
        var json = $$"""
            {
              "FileStorage": {
                "ApiBaseUrl": "https://example.test/erp/",
                "Containers": {
                  "files": { "RootPath": {{JsonString(first)}}, "MaxUploadBytes": 20 },
                  "manuals-2": { "RootPath": {{JsonString(second)}}, "MaxUploadBytes": 30 }
                }
              }
            }
            """;
        var services = new ServiceCollection();
        services.AddConfiguredFileStorage(BuildConfiguration(json));
        using var provider = services.BuildServiceProvider();
        var containers = provider.GetRequiredService<IFileContainerProvider>();

        Assert.IsTrue(containers.TryGetContainer("files", out var files));
        Assert.AreEqual(20L, files!.MaxUploadBytes);
        Assert.AreEqual("https://example.test/erp/", files.ApiBaseUri.AbsoluteUri);
        Assert.IsTrue(containers.TryGetContainer("manuals-2", out var manuals));
        Assert.AreNotSame(files.Storage, manuals!.Storage);
        Assert.IsFalse(containers.TryGetContainer("FILES", out _));
    }

    /// <summary>非法标识、上限、基址、容器重叠及公开图片目录重叠均在启动注册阶段失败。</summary>
    [TestMethod]
    public async Task InvalidContainerConfiguration_FailsRegistration()
    {
        await using var scope = new TemporaryDirectory();
        var one = Directory.CreateDirectory(System.IO.Path.Combine(scope.Path, "one")).FullName;
        var nested = Directory.CreateDirectory(System.IO.Path.Combine(one, "nested")).FullName;
        var legacy = Directory.CreateDirectory(System.IO.Path.Combine(scope.Path, "legacy")).FullName;
        var publicImages = Directory.CreateDirectory(System.IO.Path.Combine(legacy, "ProductImages")).FullName;
        var invalidJson = new[]
        {
            $$"""{ "FileStorage": { "ApiBaseUrl": "https://example.test/", "Containers": { "Bad_Id": { "RootPath": {{JsonString(one)}}, "MaxUploadBytes": 1 } } } }""",
            $$"""{ "FileStorage": { "ApiBaseUrl": "https://example.test/", "Containers": { "files": { "RootPath": {{JsonString(one)}}, "MaxUploadBytes": 0 } } } }""",
            $$"""{ "FileStorage": { "ApiBaseUrl": "https://user@example.test/", "Containers": { "files": { "RootPath": {{JsonString(one)}}, "MaxUploadBytes": 1 } } } }""",
            $$"""{ "FileStorage": { "ApiBaseUrl": "https://example.test/", "Containers": { "files": { "RootPath": {{JsonString(one)}}, "MaxUploadBytes": 1 }, "nested": { "RootPath": {{JsonString(nested)}}, "MaxUploadBytes": 1 } } } }""",
            $$"""{ "FileStorage": { "RootPath": {{JsonString(legacy)}}, "ApiBaseUrl": "https://example.test/", "Containers": { "files": { "RootPath": {{JsonString(publicImages)}}, "MaxUploadBytes": 1 } } } }"""
        };

        foreach (var json in invalidJson)
        {
            Assert.ThrowsExactly<InvalidOperationException>(() =>
                new ServiceCollection().AddConfiguredFileStorage(BuildConfiguration(json)), json);
        }
    }

    /// <summary>产品图片上传默认关闭；显式启用后使用顶层根目录、配置上限和公开地址快照。</summary>
    [TestMethod]
    public async Task ProductImageUpload_IsOptInAndUsesValidatedSnapshot()
    {
        await using var scope = new TemporaryDirectory();
        Directory.CreateDirectory(System.IO.Path.Combine(scope.Path, "CatalogImages"));
        var disabled = new ServiceCollection();
        disabled.AddConfiguredFileStorage(BuildConfiguration(
            $$"""{ "FileStorage": { "RootPath": {{JsonString(scope.Path)}} } }"""));
        using (var provider = disabled.BuildServiceProvider())
        {
            Assert.IsFalse(provider.GetRequiredService<IProductImageStorageProvider>()
                .TryGetEnabledStorage(out _));
        }

        var enabledJson = $$"""
            {
              "FileStorage": {
                "Enabled": true,
                "RootPath": {{JsonString(scope.Path)}},
                "ApiBaseUrl": "https://example.test/erp/",
                "ProductImages": {
                  "UploadEnabled": true,
                  "DirectoryName": "CatalogImages",
                  "RequestPath": "/assets/products",
                  "MaxFileBytes": 100,
                  "MaxBatchBytes": 200,
                  "MaxFiles": 3,
                  "MaxPixels": 400,
                  "MaxConcurrentProcessing": 1
                }
              }
            }
            """;
        var enabled = new ServiceCollection();
        enabled.AddConfiguredFileStorage(BuildConfiguration(enabledJson));
        using var enabledProvider = enabled.BuildServiceProvider();
        Assert.IsTrue(enabledProvider.GetRequiredService<IProductImageStorageProvider>()
            .TryGetEnabledStorage(out var context));
        Assert.AreEqual("CatalogImages", context!.ImageDirectoryName);
        Assert.AreEqual("/assets/products", context.PublicRequestPath);
        Assert.AreEqual(100L, context.MaxFileBytes);
        Assert.IsFalse(Directory.Exists(System.IO.Path.Combine(scope.Path, "CatalogImages", "Thumbnails")));
        Assert.IsFalse(Directory.Exists(System.IO.Path.Combine(scope.Path, ".product-image-state")));
    }

    /// <summary>上传开启但公开入口关闭、危险目录/路由和内部状态目录容器重叠均在启动阶段失败。</summary>
    [TestMethod]
    public async Task InvalidProductImageConfiguration_FailsRegistration()
    {
        await using var scope = new TemporaryDirectory();
        Directory.CreateDirectory(System.IO.Path.Combine(scope.Path, "ProductImages"));
        Directory.CreateDirectory(System.IO.Path.Combine(scope.Path, ".dvfilestorage-tmp"));
        var stateRoot = Directory.CreateDirectory(System.IO.Path.Combine(scope.Path, ".product-image-state")).FullName;
        var cases = new[]
        {
            $$"""{ "FileStorage": { "RootPath": {{JsonString(scope.Path)}}, "ApiBaseUrl": "https://example.test/", "ProductImages": { "UploadEnabled": true } } }""",
            $$"""{ "FileStorage": { "Enabled": true, "RootPath": {{JsonString(scope.Path)}}, "ProductImages": { "DirectoryName": "../bad" } } }""",
            $$"""{ "FileStorage": { "Enabled": true, "RootPath": {{JsonString(scope.Path)}}, "ProductImages": { "DirectoryName": ".dvfilestorage-tmp" } } }""",
            $$"""{ "FileStorage": { "Enabled": true, "RootPath": {{JsonString(scope.Path)}}, "ProductImages": { "DirectoryName": ".PRODUCT-IMAGE-STATE" } } }""",
            $$"""{ "FileStorage": { "Enabled": true, "RootPath": {{JsonString(scope.Path)}}, "ProductImages": { "RequestPath": "relative" } } }""",
            $$"""{ "FileStorage": { "RootPath": {{JsonString(scope.Path)}}, "ApiBaseUrl": "https://example.test/", "Containers": { "files": { "RootPath": {{JsonString(stateRoot)}}, "MaxUploadBytes": 1 } } } }"""
        };
        foreach (var json in cases)
        {
            Assert.ThrowsExactly<InvalidOperationException>(() =>
                new ServiceCollection().AddConfiguredFileStorage(BuildConfiguration(json)), json);
        }
    }

    /// <summary>有效 RootPath 即使 Enabled=false 也注册，并确实把文件写入该配置目录。</summary>
    [TestMethod]
    public async Task ValidRoot_RegistersStorageIndependentlyFromHttpEnabled()
    {
        await using var scope = new TemporaryDirectory();
        var json = $$"""
            {
              "FileStorage": {
                "Enabled": false,
                "RootPath": {{JsonString(scope.Path)}}
              }
            }
            """;
        var services = new ServiceCollection();
        services.AddConfiguredFileStorage(BuildConfiguration(json));
        using var provider = services.BuildServiceProvider();
        var storage = provider.GetRequiredService<IFileStorage>();

        await using var source = new MemoryStream([7, 8, 9]);
        await storage.WriteAsync("宿主/接线.bin", source);

        CollectionAssert.AreEqual(
            new byte[] { 7, 8, 9 },
            await File.ReadAllBytesAsync(System.IO.Path.Combine(scope.Path, "宿主", "接线.bin")));
    }

    /// <summary>非空但相对或不存在的 RootPath 在注册阶段明确失败。</summary>
    [TestMethod]
    public async Task InvalidConfiguredRoot_FailsRegistration()
    {
        await using var scope = new TemporaryDirectory();
        var missing = System.IO.Path.Combine(scope.Path, "missing");
        var relativeConfiguration = BuildConfiguration(
            $$"""{ "FileStorage": { "Enabled": false, "RootPath": {{JsonString("relative-root")}} } }""");
        Assert.ThrowsExactly<ArgumentException>(() =>
            new ServiceCollection().AddConfiguredFileStorage(relativeConfiguration));

        var missingConfiguration = BuildConfiguration(
            $$"""{ "FileStorage": { "Enabled": false, "RootPath": {{JsonString(missing)}} } }""");
        Assert.ThrowsExactly<DirectoryNotFoundException>(() =>
            new ServiceCollection().AddConfiguredFileStorage(missingConfiguration));
    }

    private static IConfiguration BuildConfiguration(string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        return new ConfigurationBuilder().AddJsonStream(new MemoryStream(bytes)).Build();
    }

    private static string JsonString(string value) =>
        System.Text.Json.JsonSerializer.Serialize(value);

    /// <summary>只创建并清理当前测试拥有的随机临时根目录。</summary>
    private sealed class TemporaryDirectory : IAsyncDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "SwiftExport-file-storage-host-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public ValueTask DisposeAsync()
        {
            Directory.Delete(Path, recursive: true);
            return ValueTask.CompletedTask;
        }
    }
}
