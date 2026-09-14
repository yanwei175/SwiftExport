using Mapster;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.DTOs.Inventory.Outbounds;
using SwiftExportClient.Features.Inventory.Outbounds.Models;

namespace SwiftExport.Client.Wpf.Tests.Features;

/// <summary>原因沿现有映射进入草稿命令，保留脏状态及草稿可空边界。</summary>
[TestClass]
public sealed class OutboundIssueReasonTests
{
    /// <summary>加载、编辑、回滚、长度验证及命令映射保持原因与备注独立。</summary>
    [TestMethod]
    public void IssueReason_TracksAndMapsWithoutRequiringDraftValue()
    {
        var row = new OutboundHeaderModel();
        row.Load(new() { Uid = Guid.NewGuid(), Version = 1, WarehouseSiteId = Guid.NewGuid(), IssueReason = "试制领用", Remark = "备注" });
        row.IsDetailLoaded = true;
        Assert.IsFalse(row.IsDirty);
        row.IssueReason = "样品领用";
        Assert.IsTrue(row.IsDirty);
        var draft = row.Adapt<OutboundDraftData>();
        Assert.AreEqual("样品领用", draft.IssueReason);
        Assert.AreEqual("备注", draft.Remark);
        row.RejectChanges();
        Assert.AreEqual("试制领用", row.IssueReason);
        row.IssueReason = ""; Assert.IsNull(row.ValidateRow());
        row.IssueReason = new string('因', 500); Assert.IsNull(row.ValidateRow());
        row.IssueReason = new string('因', 501); Assert.AreEqual("出库原因最多 500 字。", row.ValidateRow());
        row.Load(new() { IssueReason = null! });
        Assert.AreEqual("", row.IssueReason);
    }
}
