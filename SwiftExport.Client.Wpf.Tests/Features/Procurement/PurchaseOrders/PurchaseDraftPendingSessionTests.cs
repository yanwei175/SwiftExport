using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExportClient.Features.Procurement.PurchaseOrders.Models;
using SwiftExportClient.Features.Procurement.PurchaseOrders.Services;

namespace SwiftExport.Client.Wpf.Tests.Features.Procurement.PurchaseOrders;

/// <summary>验证采购创建应用内会话的用户归属、不可变快照及覆盖和并发保护。</summary>
[TestClass]
public sealed class PurchaseDraftPendingSessionTests
{
    /// <summary>不同身份不能覆盖原会话，取得的显示对象也不能改写冻结输入。</summary>
    [TestMethod]
    public void PendingRequest_RejectsReplacementAndReturnsIndependentInputs()
    {
        var session = new PurchaseDraftPendingSession("https://api-a.example"); var id = Guid.NewGuid();
        Assert.IsTrue(session.TrySave("user-a", new() { ExecutionRequestId = id, CommandJson = "original", Items = [new() { PurchaseQty = 3 }] }));
        Assert.IsFalse(session.TrySave("user-a", new() { ExecutionRequestId = Guid.NewGuid(), CommandJson = "other" }));
        Assert.IsFalse(session.TrySave("user-a", new() { ExecutionRequestId = id, CommandJson = "changed" }));
        session.Find("user-a")!.Items[0].PurchaseQty = 99;
        Assert.AreEqual(3m, session.Find("user-a")!.Items[0].PurchaseQty);
        Assert.IsNull(session.Find("user-b"));
        session.Clear("user-a", Guid.NewGuid()); Assert.IsNotNull(session.Find("user-a"));
        session.Clear("user-a", id); Assert.IsNull(session.Find("user-a"));
    }

    /// <summary>同用户不能并行发送，其他用户不占用其发送许可。</summary>
    [TestMethod]
    public void SendingGate_IsUserScopedAndReleasedExplicitly()
    {
        var session = new PurchaseDraftPendingSession();
        Assert.IsTrue(session.TryBeginSend("u")); Assert.IsFalse(session.TryBeginSend("u"));
        Assert.IsTrue(session.TryBeginSend("other")); session.EndSend("other");
        session.EndSend("u"); Assert.IsTrue(session.TryBeginSend("u")); session.EndSend("u");
    }

    /// <summary>会话只属于当前应用实例；新应用或其他API环境不会继承未决输入。</summary>
    [TestMethod]
    public void NewApplicationInstance_HasNoPreviousPendingSession()
    {
        var running = new PurchaseDraftPendingSession("api-a");
        running.TrySave("u", new() { ExecutionRequestId = Guid.NewGuid(), CommandJson = "original" });
        Assert.IsNotNull(running.Find("u"));
        Assert.IsNull(new PurchaseDraftPendingSession("api-a").Find("u"));
        Assert.IsNull(new PurchaseDraftPendingSession("api-b").Find("u"));
    }
}
