using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExportClient.Bootstrapper;
using SwiftExportClient.Services.Files;
using SwiftExportClient.Services.Files.Csv;
using SwiftExportClient.Services.Files.Excel;
using SwiftExportClient.Services.Files.Templates;

namespace SwiftExport.Client.Wpf.Tests.Services.Files.Registration;

/// <summary>验证公共文件服务可以脱离 UI、API 和 VSTO 解析，并已接入客户端默认启动注册。</summary>
[TestClass]
public sealed class FileServiceRegistrationTests
{
    /// <summary>独立注册满足 DI 验证且保持无状态 Singleton 生命周期，重复注册不产生重复实例。</summary>
    [TestMethod]
    public void AddClientFileServices_ShouldResolveIndependentSingletons()
    {
        var services = new ServiceCollection();
        services.AddClientFileServices().AddClientFileServices();
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });

        Assert.AreSame(provider.GetRequiredService<IExcelReadService>(), provider.GetRequiredService<IExcelReadService>());
        Assert.AreSame(provider.GetRequiredService<ICsvReadService>(), provider.GetRequiredService<ICsvReadService>());
        Assert.AreSame(provider.GetRequiredService<IExcelTemplateExportService>(), provider.GetRequiredService<IExcelTemplateExportService>());
        Assert.HasCount(1, provider.GetServices<IExcelReadService>().ToArray());
        Assert.HasCount(1, provider.GetServices<ICsvReadService>().ToArray());
        Assert.HasCount(1, provider.GetServices<IExcelTemplateExportService>().ToArray());
    }

    /// <summary>默认应用注册提供全部文件接口，无需创建窗口、登录或连接服务器。</summary>
    [TestMethod]
    public void AddSwiftExportClientServices_ShouldIncludeFileServices()
    {
        var services = new ServiceCollection();
        services.AddSwiftExportClientServices(new ConfigurationBuilder().Build());
        using var provider = services.BuildServiceProvider();

        Assert.IsNotNull(provider.GetRequiredService<IExcelReadService>());
        Assert.IsNotNull(provider.GetRequiredService<ICsvReadService>());
        Assert.IsNotNull(provider.GetRequiredService<IExcelTemplateExportService>());
    }

    /// <summary>读取结果不受传入集合后续变化影响，允许输入流及解析器先结束生命周期。</summary>
    [TestMethod]
    public void TableReadResult_ShouldKeepDetachedReadOnlySnapshot()
    {
        var header = new FileCell(1, 1, FileCellKind.Text, "编码", "编码");
        var first = new FileCell(2, 1, FileCellKind.Text, "000123", "000123");
        var headers = new List<FileCell> { header };
        var row = new List<FileCell> { first };
        var rows = new List<IReadOnlyList<FileCell>> { row };
        var issues = new List<FileIssue>();
        var result = new TableReadResult("明细", headers, rows, issues);
        headers.Clear();
        row.Clear();
        rows.Clear();
        issues.Add(new FileIssue("Changed", "测试后续变更。"));

        Assert.HasCount(1, result.Headers);
        Assert.HasCount(1, result.Rows);
        Assert.AreEqual("000123", result.Rows[0][0].RawValue);
        Assert.HasCount(0, result.Issues);
    }
}
