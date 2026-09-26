using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Inventory.Outbounds;
using SwiftExport.Contracts.SysEnums.Inventory;
namespace SwiftExport.Application.Tests.Inventory;
/// <summary>验证普通库存出库的真实余额重验、权限、幂等及失败回滚，不连接数据库。</summary>
public sealed partial class OutboundManagerServiceTests
{
    /// <summary>预览不写库存，成功重放不重复扣减，同命令不同内容冲突。</summary>
    [TestMethod]
    public async Task Post_ReplayAndConflict_PreserveInventoryIdentity()
    {
        var f = new Fixture();
        Assert.IsTrue((await f.Service.CreateAsync(f.Create())).Success);
        var id = f.Header!.Uid;
        var preview = await f.Service.PreviewPostAsync(id, new() { ExpectedVersion = 1 });
        Assert.IsTrue(preview.Data.CanPost, preview.Message);
        Assert.AreEqual(90m, preview.Data.StockImpacts.Single().ExpectedQuantity);
        Assert.AreEqual(100m, f.Balance);
        var cmd = new PostOutboundCommand { CommandId = Guid.NewGuid(), ExpectedVersion = 1 };
        Assert.IsTrue((await f.Service.PostAsync(id, cmd)).Success);
        Assert.IsTrue((await f.Service.PostAsync(id, cmd)).Success);
        Assert.AreEqual(1, f.PostCalls);
        Assert.AreEqual(2, f.LogCount, "草稿和首次过账各写一条审计；重放不追加。");
        Assert.AreEqual(90m, f.Balance);
        var item = f.PostedItems.Single();
        Assert.AreEqual(-10m, item.QuantityDelta);
        Assert.AreEqual(InventoryTransactionTypeEnum.StockIssueOutbound, item.TransactionType);
        Assert.AreEqual(f.SavedLines[0].CommandItemId, item.CommandItemId);
        Assert.AreEqual(f.SavedLines[0].Uid, item.SourceLineId);
        Assert.AreEqual("OutboundDocument", item.SourceDocumentType);
        cmd.ExpectedVersion++;
        Assert.AreEqual(ServiceErrorCodes.CommandConflict, (await f.Service.PostAsync(id, cmd)).Error.Code);
    }
    /// <summary>草稿可超过当前余额，预览和提交重验库存、单位及操作权限。</summary>
    [TestMethod]
    public async Task Post_RechecksBalanceAndPermissions()
    {
        var f = new Fixture { Balance = 1 };
        Assert.IsTrue((await f.Service.CreateAsync(f.Create())).Success);
        var id = f.Header!.Uid;
        Assert.IsFalse((await f.Service.PreviewPostAsync(id, new() { ExpectedVersion = 1 })).Data.CanPost);
        f.Balance = 100;
        Assert.IsTrue((await f.Service.PreviewPostAsync(id, new() { ExpectedVersion = 1 })).Data.CanPost);
        f.Balance = 0;
        Assert.IsFalse((await f.Service.PostAsync(id, new() { CommandId = Guid.NewGuid(), ExpectedVersion = 1 })).Success);
        Assert.AreEqual(0, f.PostCalls);
        f.Balance = 100; f.AllowPost = false;
        Assert.AreEqual(ServiceErrorCodes.Forbidden, (await f.Service.PostAsync(id, new() { CommandId = Guid.NewGuid(), ExpectedVersion = 1 })).Error.Code);
        Assert.AreEqual(ServiceErrorCodes.Forbidden, (await f.Service.PreviewPostAsync(id, new() { ExpectedVersion = 1 })).Error.Code);
        f.AllowPost = true; f.PostingUnit = "KG";
        Assert.IsFalse((await f.Service.PreviewPostAsync(id, new() { ExpectedVersion = 1 })).Data.CanPost);
    }
    /// <summary>库存调用或日志失败，整单版本、状态及库存同回滚。</summary>
    [TestMethod]
    public async Task Post_LogOrInventoryFailure_RollsBack()
    {
        var f = new Fixture();
        await f.Service.CreateAsync(f.Create());
        f.FailLog = true;
        Assert.IsFalse((await f.Service.PostAsync(f.Header!.Uid, new() { CommandId = Guid.NewGuid(), ExpectedVersion = 1 })).Success);
        Assert.AreEqual(100m, f.Balance);
        Assert.AreEqual(1, f.Header.Version);
        Assert.AreEqual(OutboundPostingStatusEnum.Unposted, f.Header.PostingStatus);
        f.FailLog = false; f.FailInventory = true;
        Assert.IsFalse((await f.Service.PostAsync(f.Header.Uid, new() { CommandId = Guid.NewGuid(), ExpectedVersion = 1 })).Success);
        Assert.AreEqual(100m, f.Balance);
    }
}
