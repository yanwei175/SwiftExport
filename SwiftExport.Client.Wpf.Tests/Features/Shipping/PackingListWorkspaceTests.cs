using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.DTOs.Shipping.PackingLists;
using SwiftExportClient.Features.Shipping.PackingLists.Services;
using SwiftExportClient.Features.Shipping.PackingLists.ViewModels;
using SwiftExportClient.Navigation;
using System.IO.Compression;
using System.IO;
using System.Reflection;

namespace SwiftExport.Client.Wpf.Tests.Features.Shipping;

[TestClass]
public sealed class PackingListWorkspaceTests
{
    [TestMethod]
    public void Navigation_RegistersWorkspaceAndIndependentDetailTab()
    {
        var registry = new NavigationRouteRegistry();
        Assert.IsTrue(registry.TryGetRoute(PackingListWorkspaceViewModel.RouteKey, out var workspace));
        Assert.AreEqual(typeof(PackingListWorkspaceViewModel), workspace.ViewModelType);
        Assert.IsTrue(registry.TryGetRoute(PackingListDetailViewModel.RouteKey, out var detail));
        Assert.AreEqual(typeof(PackingListDetailViewModel), detail.ViewModelType);
    }

    [TestMethod]
    public void XlsxOutput_HasSummaryAndSourceTraceWorksheets()
    {
        var allocationId = Guid.NewGuid();
        var data = new PackingListExportDto
        {
            Header = new PackingListHeaderDto { PackingListNo = "PL-TEST", CustomerName = "Test" },
            ProductSummary = [new PackingListProductSummaryDto { ProductId = Guid.NewGuid(), Sku = "SKU-1", Quantity = 3 }],
            SourceTrace = [new PackingListContributionDto { LineNumber = 1, Sku = "SKU-1", Quantity = 3, NetDemandAllocationId = allocationId, SalesOrderNo = "SO-1" }]
        };
        var method = typeof(PackingListDocumentOutputService).GetMethod("WriteWorkbook", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.IsNotNull(method);
        using var stream = new MemoryStream();
        method.Invoke(null, [stream, data, CancellationToken.None]);
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        Assert.IsNotNull(archive.GetEntry("xl/worksheets/sheet1.xml"));
        var sourceSheet = archive.GetEntry("xl/worksheets/sheet2.xml");
        Assert.IsNotNull(sourceSheet);
        using var workbookReader = new StreamReader(archive.GetEntry("xl/workbook.xml")!.Open());
        var workbook = workbookReader.ReadToEnd();
        StringAssert.Contains(workbook, "产品汇总");
        StringAssert.Contains(workbook, "逐来源追溯");
        using var sourceReader = new StreamReader(sourceSheet.Open());
        StringAssert.Contains(sourceReader.ReadToEnd(), allocationId.ToString());
    }

    [TestMethod]
    public void DetailViewModel_ClearsPreviewWhenBusinessFieldsChange()
    {
        var source = Read("SwiftExport.Client.Wpf", "Features", "Shipping", "PackingLists", "ViewModels", "PackingListDetailViewModel.cs");
        StringAssert.Contains(source, "Preview = null; PreviewToken = null");
        StringAssert.Contains(source, "partial void OnShipmentDateChanged");
        StringAssert.Contains(source, "line.PropertyChanged += OnLineChanged");
        StringAssert.Contains(source, "if (!await SaveDraftCoreAsync()) return;");
    }

    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine(new[] { Root() }.Concat(parts).ToArray()));
    private static string Root()
    {
        foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
                if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))) return directory.FullName;
        Assert.Fail("无法定位仓库根目录。"); return string.Empty;
    }
}
