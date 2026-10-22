using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.DTOs.Shipping.PackingLists;
using SwiftExport.Contracts.SysEnums.Shipping;
using SwiftExportClient.Features.Shipping.PackingLists.Models;

namespace SwiftExport.Client.Wpf.Tests.Features.Shipping;

/// <summary>验证管理摘要的映射、集合加载与查询条件，不依赖未实现的后端。</summary>
[TestClass]
public sealed class PackingListManagerModelTests
{
    /// <summary>服务端摘要加载保持干净，快照往返不丢失物流或状态。</summary>
    [TestMethod]
    public void Load_ShouldKeepSummaryCleanAndRoundTrip()
    {
        var dto = new PackingListManagerListItemDto
        {
            Uid = Guid.NewGuid(), CustomerId = Guid.NewGuid(), PackingListNo = "PL-001",
            CustomerNameSnapshot = "客户", InvoiceDate = new DateTime(2026, 9, 15),
            ContainerNo = "C1", SealNo = "S1", Etd = new DateTime(2026, 9, 16),
            Eta = new DateTime(2026, 9, 20), InvoiceNosSummary = "I1, I2",
            PostingStatus = PackingListPostingStatusEnum.Unposted
        };
        var list = new PackingListManagerListModel();
        list.LoadFromDtos([dto]);
        Assert.IsFalse(list.HasChanges);
        var model = list.Items.Single();
        Assert.IsFalse(model.IsDirty);
        var actual = model.ToDto();
        Assert.AreEqual(dto.Uid, actual.Uid);
        Assert.AreEqual(dto.CustomerId, actual.CustomerId);
        Assert.AreEqual(dto.PackingListNo, actual.PackingListNo);
        Assert.AreEqual(dto.CustomerNameSnapshot, actual.CustomerNameSnapshot);
        Assert.AreEqual(dto.InvoiceDate, actual.InvoiceDate);
        Assert.AreEqual(dto.Etd, actual.Etd);
        Assert.AreEqual(dto.Eta, actual.Eta);
        Assert.AreEqual(dto.ContainerNo, actual.ContainerNo);
        Assert.AreEqual(dto.SealNo, actual.SealNo);
        Assert.AreEqual(dto.InvoiceNosSummary, actual.InvoiceNosSummary);
        Assert.AreEqual(dto.PostingStatus, actual.PostingStatus);
        list.SelectedItem = model;
        Assert.IsFalse(list.HasChanges);
    }

    /// <summary>查询默认不限制过账状态且采用公共分页默认值。</summary>
    [TestMethod]
    public void Search_ShouldNotifyWithoutOwningCommands()
    {
        var search = new PackingListManagerSearchModel();
        Assert.HasCount(0, search.PostingStatuses);
        Assert.HasCount(0, search.SelectedCustomers);
        Assert.AreEqual(1, search.PageIndex);
        Assert.AreEqual(20, search.PageSize);
        var names = new List<string?>();
        search.PropertyChanged += (_, e) => names.Add(e.PropertyName);
        search.Keyword = "PL";
        CollectionAssert.Contains(names, nameof(search.Keyword));
    }
}
