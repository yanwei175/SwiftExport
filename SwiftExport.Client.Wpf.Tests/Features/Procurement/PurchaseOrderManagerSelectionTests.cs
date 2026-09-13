using Microsoft.VisualStudio.TestTools.UnitTesting;
using Mapster;
using System;
using System.Linq;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderManagerDto;
using SwiftExport.Contracts.SysEnums;
using SwiftExportClient.Features.Procurement.PurchaseOrders.Models;

namespace SwiftExport.Client.Wpf.Tests.Features.Procurement;

/// <summary>采购管理页的新链选择身份与只读投影测试。</summary>
[TestClass]
public sealed class PurchaseOrderManagerSelectionTests
{
    [TestMethod]
    public void ManagerRowMustKeepStableNewChainIdentityWithoutLegacyScope()
    {
        var newId = Guid.NewGuid();
        var current = new PurchaseOrderManagerListItemDto
        { PurchaseOrderId = newId, OrderNo = "PO-N", CustomerCode = "C-001", SupplierName = "供应商", ConfirmationStatus = ConfirmationStatusEnum.待确认 }
            .Adapt<PurchaseOrderManagementRowModel>();
        Assert.AreEqual(newId, current.PurchaseOrderId);
        Assert.AreEqual("PO-N", current.OrderNo);
        Assert.AreEqual("C-001", current.CustomerCode);
        Assert.IsNull(typeof(PurchaseOrderManagementRowModel).GetProperty("CustomerName"));
        Assert.IsNull(typeof(PurchaseOrderManagementRowModel).GetProperty("IsLegacy"));
        Assert.IsNull(typeof(PurchaseOrderManagementRowModel).GetProperty("DataScopeText"));
    }

    [TestMethod]
    public void ReadOnlyLineProjectionMustNotExposeWriteCommandsOrSourceCollections()
    {
        var names = typeof(PurchaseOrderManagementLineModel).GetProperties().Select(x => x.Name).ToArray();
        foreach (var forbidden in new[] { "Contributions", "ProgressHistory", "CanEdit", "CommandId", "ExpectedVersion" })
            Assert.DoesNotContain(forbidden, names);
    }

    [TestMethod]
    public void SharedDocumentOpenRequestMayRetainLegacyDecisionForSeparateLegacyEntry()
    {
        CollectionAssert.AreEquivalent(new[] { "PurchaseOrderId", "IsLegacy" },
            typeof(PurchaseOrderDocumentOpenRequest).GetProperties().Select(x => x.Name).ToArray());
    }
}
