using System.Reflection;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.Apis.Procurement;
using SwiftExport.Contracts.DTOs.Procurement.OrderManagement;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Features.Procurement.PurchaseOrders.Services;

namespace SwiftExport.Client.Wpf.Tests.Features.Procurement;

/// <summary>模拟丢失响应和错误回执，验证跨页面会话冻结原命令且不跨用户重放。</summary>
[TestClass]
public sealed class ProcurementReversalSessionTests
{
    /// <summary>删行结果未知时，后续修改预览不得污染重试负载；成功后只解除原用户的未决状态。</summary>
    [TestMethod]
    public async Task TimeoutRetryUsesExactFrozenCommand()
    {
        var session = new ProcurementReversalSession();
        var uid = Guid.NewGuid(); var deletedLine = Guid.NewGuid();
        var preview = new ProcurementReversalPreviewDto { PurchaseOrderId = uid, Version = 4, CanExecute = true,
            Operation = ProcurementReversalOperation.DeleteLines, Lines = [new() { PurchaseOrderLineId = deletedLine, Version = 2 }] };
        Assert.IsTrue(session.Freeze("user", preview, "删除选定需求"));
        Assert.IsNull(session.Find("other", uid));
        var api = DispatchProxy.Create<IProcurementOrderApi, ApiProxy>();
        var proxy = (ApiProxy)(object)api;
        var sent = new List<string>();
        proxy.Handler = (_, args) =>
        {
            sent.Add(JsonSerializer.Serialize((DeleteProcurementDraftLinesCommand)args![1]!));
            if (sent.Count == 1) return Task.FromException<ServiceResult<ProcurementOrderCommandResult>>(new TimeoutException());
            return Task.FromResult(ServiceResult<ProcurementOrderCommandResult>.Ok(new() { PurchaseOrderId = uid, Version = 5,
                Lines = [new() { PurchaseOrderLineId = Guid.NewGuid(), Version = 1 }] }));
        };
        await Assert.ThrowsExactlyAsync<TimeoutException>(() => session.SendAsync("user", uid, api));
        Assert.IsNotNull(session.Find("user", uid));
        preview.Version = 90; preview.Lines.Clear();
        Assert.IsFalse(session.Freeze("user", preview, "另一个操作"));
        var result = await session.SendAsync("user", uid, api);
        Assert.IsTrue(result.Success);
        Assert.AreEqual(sent[0], sent[1]);
        Assert.IsNull(session.Find("user", uid));
    }

    /// <summary>错误版本的成功回执不得解除冻结；原因未填写不得建立命令。</summary>
    [TestMethod]
    public async Task InvalidReceiptKeepsPendingCommand()
    {
        var session = new ProcurementReversalSession(); var uid = Guid.NewGuid();
        var preview = new ProcurementReversalPreviewDto { PurchaseOrderId = uid, Version = 1, CanExecute = true, Operation = ProcurementReversalOperation.Void };
        Assert.IsFalse(session.Freeze("user", preview, " "));
        Assert.IsTrue(session.Freeze("user", preview, "作废原因"));
        var api = DispatchProxy.Create<IProcurementOrderApi, ApiProxy>();
        ((ApiProxy)(object)api).Handler = (_, _) => Task.FromResult(ServiceResult<ProcurementOrderCommandResult>.Ok(new() { PurchaseOrderId = uid, Version = 10, Lines = [] }));
        var result = await session.SendAsync("user", uid, api);
        Assert.IsFalse(result.Success);
        Assert.IsNotNull(session.Find("user", uid));
    }

    /// <summary>整张草稿删除冻结全部头行版本，丢失响应后仍使用同一命令，成功以删除事实接纳。</summary>
    [TestMethod]
    public async Task DeleteDraft_RetriesAllLineVersionsAndAcceptsDeletedReceipt()
    {
        var session = new ProcurementReversalSession(); var uid = Guid.NewGuid(); var line = Guid.NewGuid(); var second = Guid.NewGuid();
        var preview = new ProcurementReversalPreviewDto { PurchaseOrderId = uid, Version = 6, CanExecute = true,
            Operation = ProcurementReversalOperation.DeleteDraft, Lines = [new() { PurchaseOrderLineId = line, Version = 3 }, new() { PurchaseOrderLineId = second, Version = 8 }] };
        Assert.IsTrue(session.Freeze("user", preview, "删除尚未确认草稿"));
        var sent = new List<string>(); var api = DispatchProxy.Create<IProcurementOrderApi, ApiProxy>();
        ((ApiProxy)(object)api).Handler = (method, args) =>
        {
            Assert.AreEqual(nameof(IProcurementOrderApi.DeleteDraftAsync), method!.Name);
            var command = (DeleteProcurementDraftCommand)args![1]!; sent.Add(JsonSerializer.Serialize(command));
            Assert.AreEqual(2, command.Lines.Count); Assert.AreEqual(6, command.ExpectedVersion);
            if (sent.Count == 1) return Task.FromException<ServiceResult<ProcurementOrderCommandResult>>(new TimeoutException());
            return Task.FromResult(ServiceResult<ProcurementOrderCommandResult>.Ok(new() { PurchaseOrderId = uid, Version = 7, IsDeleted = true,
                ConfirmationStatus = ConfirmationStatusEnum.待确认, LifecycleStatus = PurchaseOrderLifecycleStatusEnum.Active,
                Lines = [new() { PurchaseOrderLineId = line, Version = 4 }, new() { PurchaseOrderLineId = second, Version = 9 }] }));
        };
        await Assert.ThrowsExactlyAsync<TimeoutException>(() => session.SendAsync("user", uid, api)); preview.Lines.Clear();
        var result = await session.SendAsync("user", uid, api);
        Assert.IsTrue(result.Success); Assert.IsNull(session.Find("user", uid)); Assert.AreEqual(sent[0], sent[1]);
    }

    /// <summary>缺少删除事实、少行或行版本不递增时不能接纳成功，继续保留原请求。</summary>
    [TestMethod]
    [DataRow("notDeleted")][DataRow("missingLine")][DataRow("wrongVersion")]
    public async Task DeleteDraft_IncompleteReceiptRetainsPending(string defect)
    {
        var session = new ProcurementReversalSession(); var uid = Guid.NewGuid(); var line = Guid.NewGuid(); var other = Guid.NewGuid();
        Assert.IsTrue(session.Freeze("u", new() { PurchaseOrderId = uid, Version = 2, CanExecute = true, Operation = ProcurementReversalOperation.DeleteDraft,
            Lines = [new() { PurchaseOrderLineId = line, Version = 3 }, new() { PurchaseOrderLineId = other, Version = 1 }] }, "删除草稿"));
        var receipt = new ProcurementOrderCommandResult { PurchaseOrderId = uid, Version = 3, IsDeleted = defect != "notDeleted",
            Lines = [new() { PurchaseOrderLineId = line, Version = defect == "wrongVersion" ? 3 : 4 }, new() { PurchaseOrderLineId = other, Version = 2 }] };
        if (defect == "missingLine") receipt.Lines.RemoveAt(1);
        var api = DispatchProxy.Create<IProcurementOrderApi, ApiProxy>();
        ((ApiProxy)(object)api).Handler = (_, _) => Task.FromResult(ServiceResult<ProcurementOrderCommandResult>.Ok(receipt));
        Assert.IsFalse((await session.SendAsync("u", uid, api)).Success); Assert.IsNotNull(session.Find("u", uid));
    }
    /// <summary>只为本测试分派 API 响应，不连接服务端。</summary>
    public class ApiProxy : DispatchProxy
    {
        public Func<MethodInfo?, object?[]?, object?> Handler { get; set; } = null!;
        /// <summary>转交当前用例指定的响应或故障。</summary>
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Handler(targetMethod, args);
    }
}
