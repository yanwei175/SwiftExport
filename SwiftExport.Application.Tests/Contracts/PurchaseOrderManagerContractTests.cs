using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Refit;
using SwiftExport.Contracts.Apis.Procurement;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderManagerDto;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Tests.Contracts;

/// <summary>验证采购 Manager 专用契约的路由、分页、多选状态和职责隔离。</summary>
[TestClass]
public sealed class PurchaseOrderManagerContractTests
{
    /// <summary>筛选使用多选状态集合并复用稳定分页基类，空集合可表达不限制。</summary>
    [TestMethod]
    public void SearchRequestUsesPagedMultiStatusCollections()
    {
        var request = new PurchaseOrderManagerSearchRequest();

        Assert.IsInstanceOfType<PagedRequestBase>(request);
        Assert.IsNotNull(request.ConfirmationStatuses);
        Assert.IsNotNull(request.LifecycleStatuses);
        Assert.HasCount(0, request.ConfirmationStatuses);
        Assert.HasCount(0, request.LifecycleStatuses);
        Assert.AreEqual(typeof(List<ConfirmationStatusEnum>),
            typeof(PurchaseOrderManagerSearchRequest).GetProperty(nameof(request.ConfirmationStatuses))!.PropertyType);
        Assert.AreEqual(typeof(List<PurchaseOrderLifecycleStatusEnum>),
            typeof(PurchaseOrderManagerSearchRequest).GetProperty(nameof(request.LifecycleStatuses))!.PropertyType);
        Assert.IsNull(typeof(PurchaseOrderManagerSearchRequest).GetProperty("ConfirmationStatus"));
        Assert.IsNull(typeof(PurchaseOrderManagerSearchRequest).GetProperty("LifecycleStatus"));
    }

    /// <summary>Refit 路由严格对应冻结的 Manager 查询边界。</summary>
    [TestMethod]
    public void ManagerApiUsesFrozenAbsoluteRoutes()
    {
        var methods = typeof(IPurchaseOrderManagerApi).GetMethods().ToDictionary(method => method.Name);

        Assert.AreEqual("/search",
            methods[nameof(IPurchaseOrderManagerApi.SearchAsync)].GetCustomAttribute<PostAttribute>()!.Path);
        Assert.AreEqual("/{purchaseOrderId}/lines",
            methods[nameof(IPurchaseOrderManagerApi.GetLinesAsync)].GetCustomAttribute<GetAttribute>()!.Path);
    }

    /// <summary>Manager DTO 只提供管理摘要，不泄露完整跟进、Trace、入库单或库存流水集合。</summary>
    [TestMethod]
    public void ManagerDtosRemainIsolatedFromHistoryAndInventoryDetails()
    {
        var forbidden = new[]
        {
            "ProgressHistory", "SupplierProgressReports", "Trace", "Operations",
            "InboundDocuments", "InboundLines", "InventoryLedgerEntries", "InventoryBalances"
        };
        var properties = typeof(PurchaseOrderManagerListItemDto).GetProperties()
            .Concat(typeof(PurchaseOrderManagerLineDto).GetProperties())
            .Select(property => property.Name)
            .ToArray();

        foreach (var name in forbidden)
        {
            Assert.DoesNotContain(name, properties);
        }

        Assert.AreEqual(typeof(decimal),
            typeof(PurchaseOrderManagerLineDto).GetProperty(nameof(PurchaseOrderManagerLineDto.UnreceivedQty))!.PropertyType);
        Assert.IsNull(typeof(PurchaseOrderManagerLineDto).GetProperty("UnreceivedQtyText"));
    }

    /// <summary>目录内每个公开契约均使用同名独立文件，避免重新形成 Workspace 聚合桶。</summary>
    [TestMethod]
    public void ManagerContractsUseOnePublicTypePerFile()
    {
        var directory = Path.Combine(Root(), "SwiftExport.Contracts", "DTOs", "Procurement", "PurchaseOrderManagerDto");
        var files = Directory.GetFiles(directory, "*.cs");
        CollectionAssert.AreEquivalent(new[]
        {
            "PurchaseOrderManagerSearchRequest.cs",
            "PurchaseOrderManagerListItemDto.cs",
            "PurchaseOrderManagerLineDto.cs"
        }, files.Select(Path.GetFileName).ToArray());

        foreach (var file in files)
        {
            var typeName = Path.GetFileNameWithoutExtension(file);
            StringAssert.Contains(File.ReadAllText(file), $"public sealed class {typeName}");
        }
    }

    private static string Root()
    {
        foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md")))
                {
                    return directory.FullName;
                }
            }
        }

        Assert.Fail("无法定位仓库根目录。");
        return string.Empty;
    }
}
