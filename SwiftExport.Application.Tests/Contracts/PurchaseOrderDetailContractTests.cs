using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Refit;
using SwiftExport.Contracts.Apis.Procurement;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderDetailDto;

namespace SwiftExport.Application.Tests.Contracts;

/// <summary>采购详情专用契约的职责隔离、文件组织和相对路由门禁。</summary>
[TestClass]
public sealed class PurchaseOrderDetailContractTests
{
    /// <summary>查询 DTO 不得重新携带跟进、入库、库存、Trace 或出货集合。</summary>
    [TestMethod]
    public void DetailDtosExcludeCrossModuleHistories()
    {
        var names = typeof(PurchaseOrderDetailDto).GetProperties()
            .Concat(typeof(PurchaseOrderLineDetailDto).GetProperties())
            .Select(property => property.Name)
            .ToArray();
        foreach (var forbidden in new[] { "ProgressHistory", "LatestProgress", "Contributions", "InboundLines", "InventoryTransactions", "Operations", "Trace", "PackingLists" })
            Assert.DoesNotContain(forbidden, names);
    }

    /// <summary>详情 Refit 路由保持相对路径，避免与已配置 Controller 根地址重复拼接。</summary>
    [TestMethod]
    public void DetailRefitRoutesRemainRelativeToControllerBaseAddress()
    {
        var get = typeof(IProcurementOrderApi).GetMethod(nameof(IProcurementOrderApi.GetDetailAsync))!
            .GetCustomAttribute<GetAttribute>()!;
        var save = typeof(IProcurementOrderApi).GetMethod(nameof(IProcurementOrderApi.SaveAsync))!
            .GetCustomAttribute<PutAttribute>()!;
        Assert.AreEqual("/{uid}", get.Path);
        Assert.AreEqual("/{uid}/commercial", save.Path);
    }

    /// <summary>详情目录遵循一个公开类型一个文件。</summary>
    [TestMethod]
    public void DetailDtoFolderKeepsOnePublicTypePerFile()
    {
        var directory = Path.Combine(Root(), "SwiftExport.Contracts", "DTOs", "Procurement", "PurchaseOrderDetailDto");
        foreach (var file in Directory.GetFiles(directory, "*.cs"))
            Assert.AreEqual(1, File.ReadAllLines(file).Count(line => line.Contains("public sealed class", StringComparison.Ordinal)), Path.GetFileName(file));
    }

    private static string Root()
    {
        foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
                if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))) return directory.FullName;
        Assert.Fail("无法定位仓库根目录。");
        return string.Empty;
    }
}
