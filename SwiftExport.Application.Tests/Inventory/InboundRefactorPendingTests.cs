using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Interfaces.Inventory;
using SwiftExport.Application.Services.Inventory;

namespace SwiftExport.Application.Tests.Inventory;

/// <summary>入库链路清除期间，自动入库不能伪造成功或静默跳过。</summary>
[TestClass]
public sealed class InboundRefactorPendingTests
{
    /// <summary>即使没有待入库行，也明确阻断旧自动入库流程。</summary>
    [TestMethod]
    public async Task AutoInbound_ReturnsPendingFailure()
    {
        var port = new InboundPostingPort();
        try
        {
            await port.PostAutoReceiptsAsync(Guid.NewGuid(), "PL-TEST", Guid.NewGuid(), DateTime.Today, []);
            Assert.Fail("旧自动入库不得返回成功。");
        }
        catch (InventoryPostingException ex)
        {
            Assert.AreEqual("待重构", ex.Message);
        }
    }

    /// <summary>反向入口同样失败，供外层事务回滚而不是忽略入库影响。</summary>
    [TestMethod]
    public async Task ReverseAutoInbound_ReturnsPendingFailure()
    {
        var port = new InboundPostingPort();
        try
        {
            await port.ReverseAutoReceiptsAsync(Guid.NewGuid(), Guid.NewGuid(), "测试");
            Assert.Fail("旧自动入库反向不得返回成功。");
        }
        catch (InventoryPostingException ex)
        {
            Assert.AreEqual("待重构", ex.Message);
        }
    }
}
