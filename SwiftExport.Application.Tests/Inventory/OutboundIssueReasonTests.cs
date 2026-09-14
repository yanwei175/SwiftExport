using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.SysEnums.Inventory;

namespace SwiftExport.Application.Tests.Inventory;

/// <summary>出库原因的草稿可空、过账必填、长度及命令内容边界。</summary>
public sealed partial class OutboundManagerServiceTests
{
    /// <summary>空白可暂存，但无论预览还是直接提交都不能扣库存。</summary>
    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow(" \t\r\n")]
    public async Task IssueReason_EmptyDraftAllowed_PostBlocked(string? reason)
    {
        var f = new Fixture();
        var command = f.Create(); command.Draft.IssueReason = reason!;
        Assert.IsTrue((await f.Service.CreateAsync(command)).Success);
        var id = f.Header!.Uid;
        var preview = await f.Service.PreviewPostAsync(id, new() { ExpectedVersion = 1 });
        Assert.IsFalse(preview.Data.CanPost);
        Assert.IsTrue(preview.Data.Blockers.Any(x => x.Code == ServiceErrorCodes.Validation && x.Message.Contains("出库原因")));
        Assert.IsFalse((await f.Service.PostAsync(id, new() { CommandId = Guid.NewGuid(), ExpectedVersion = 1 })).Success);
        Assert.AreEqual(0, f.PostCalls);
        Assert.AreEqual(100m, f.Balance);
    }

    /// <summary>原因独立于备注，保存和预览不丢失，过账后禁止普通修改。</summary>
    [TestMethod]
    public async Task IssueReason_RoundTrip_LengthAndCommandConflict()
    {
        var f = new Fixture(); var command = f.Create();
        command.Draft.IssueReason = new string('因', 501);
        Assert.AreEqual(ServiceErrorCodes.Validation, (await f.Service.CreateAsync(command)).Error.Code);
        command.Draft.IssueReason = new string('因', 500); command.Draft.Remark = "其他说明";
        Assert.IsTrue((await f.Service.CreateAsync(command)).Success);
        Assert.AreEqual(command.Draft.IssueReason, f.Header!.IssueReason);
        var preview = await f.Service.PreviewPostAsync(f.Header.Uid, new() { ExpectedVersion = 1 });
        Assert.IsTrue(preview.Data.CanPost);
        Assert.AreEqual(command.Draft.IssueReason, preview.Data.IssueReason);
        command.Draft.IssueReason = "不同用途";
        Assert.AreEqual(ServiceErrorCodes.CommandConflict, (await f.Service.CreateAsync(command)).Error.Code);
        Assert.IsTrue((await f.Service.PostAsync(f.Header.Uid, new() { CommandId = Guid.NewGuid(), ExpectedVersion = 1 })).Success);
        Assert.AreEqual(OutboundPostingStatusEnum.Posted, f.Header.PostingStatus);
        Assert.IsFalse((await f.Service.UpdateAsync(f.Header.Uid, new() { CommandId = Guid.NewGuid(), ExpectedVersion = f.Header.Version, Draft = command.Draft })).Success);
    }
}
