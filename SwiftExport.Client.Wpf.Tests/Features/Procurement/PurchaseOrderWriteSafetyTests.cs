using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.Apis.MasterData;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Procurement.OrderManagement;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderDetailDto;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderManagerDto;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Features.Procurement.PurchaseOrders.Models;
using SwiftExportClient.Features.Procurement.PurchaseOrders.Services;
using SwiftExportClient.Features.Procurement.PurchaseOrders.ViewModels;
using System.Reflection;
using System.Text.Json;

namespace SwiftExport.Client.Wpf.Tests.Features.Procurement;

public sealed partial class PurchaseOrderManagerBatchConfirmationTests
{
    /// <summary>真实未知码及命令忙都冻结原请求，详情不能另发确认，跨页重试保留完整负载。</summary>
    [TestMethod]
    [DataRow(ServiceErrorCodes.OutcomeUnknown)]
    [DataRow(ServiceErrorCodes.CommandBusy)]
    public async Task SharedConfirmation_ActualUnknownCodeRetainsOriginalRequest(string code)
    {
        var row = Order("PO-U"); var fixture = CreateFixture(row);
        fixture.Orders.ConfirmHandler = (_, _) => Task.FromResult(ServiceResult<ProcurementOrderCommandResult>.Fail(code, "请求结果待核实"));
        fixture.Orders.DetailHandler = id => Task.FromResult(ServiceResult<PurchaseOrderDetailDto>.Ok(Detail(id)));
        await fixture.ViewModel.InitializeAsync(); await fixture.ViewModel.ConfirmCommand.ExecuteAsync(null);
        var original = fixture.Orders.ConfirmCalls.Single().Command;
        using var detail = CreateDetail(fixture);
        await detail.InitializeDocumentAsync(new PurchaseOrderDocumentOpenRequest { PurchaseOrderId = row.PurchaseOrderId }, CancellationToken.None);
        Assert.IsTrue(detail.HasPendingWrite); Assert.IsFalse(detail.CanEdit); Assert.IsFalse(detail.ConfirmCommand.CanExecute(null)); Assert.IsFalse(await detail.CanCloseAsync(CancellationToken.None));
        await detail.ConfirmCommand.ExecuteAsync(null); Assert.AreEqual(1, fixture.Orders.ConfirmCalls.Count);
        fixture.Orders.ConfirmHandler = (id, command) => Task.FromResult(ServiceResult<ProcurementOrderCommandResult>.Ok(Receipt(id, command.ExpectedVersion)));
        await detail.RetryPendingCommand.ExecuteAsync(null);
        var retry = fixture.Orders.ConfirmCalls.Last().Command;
        Assert.AreEqual(JsonSerializer.Serialize(original), JsonSerializer.Serialize(retry)); Assert.IsFalse(detail.HasPendingWrite);
        Assert.IsFalse(fixture.ViewModel.ConfirmCommand.CanExecute(null), "其他页面不能继续使用旧草稿列表确认，须先刷新核实。");
    }

    /// <summary>只有采购身份而缺少版本和行结果的成功响应仍是未知，禁止批量继续。</summary>
    [TestMethod]
    public async Task IncompleteSuccessReceipt_StopsBatchAndKeepsFrozenCommand()
    {
        var row = Order("PO-1"); var fixture = CreateFixture(row, Order("PO-2"));
        fixture.Orders.ConfirmHandler = (id, _) => Task.FromResult(ServiceResult<ProcurementOrderCommandResult>.Ok(new() { PurchaseOrderId = id }));
        await fixture.ViewModel.InitializeAsync(); fixture.ViewModel.IsAllCurrentPageChecked = true;
        await fixture.ViewModel.BatchConfirmCommand.ExecuteAsync(null);
        Assert.AreEqual(1, fixture.Orders.ConfirmCalls.Count); Assert.IsNotNull(fixture.Session.Find("u", row.PurchaseOrderId));
        StringAssert.Contains(fixture.ViewModel.ResultSummary, "未知 1"); StringAssert.Contains(fixture.ViewModel.ResultSummary, "未执行 1");
    }

    /// <summary>保存期间阻断并发、刷新和关闭；未知后页面值变化不能改变同命令负载。</summary>
    [TestMethod]
    public async Task SaveUnknown_ProtectsConcurrencyAndFrozenPayloadAcrossPages()
    {
        var row = Order("PO-SAVE"); var fixture = CreateFixture([row], true, includeEditPermission: true);
        fixture.Orders.DetailHandler = id => Task.FromResult(ServiceResult<PurchaseOrderDetailDto>.Ok(Detail(id)));
        var completion = new TaskCompletionSource<ServiceResult<ProcurementOrderCommandResult>>(); fixture.Orders.SaveHandler = (_, _) => completion.Task;
        using var detail = CreateDetail(fixture); await detail.InitializeDocumentAsync(new PurchaseOrderDocumentOpenRequest { PurchaseOrderId = row.PurchaseOrderId }, CancellationToken.None);
        detail.Header.Remark = "原备注"; detail.Lines.Items[0].TaxPrice = 2.3456m;
        var saving = detail.SaveCommand.ExecuteAsync(null);
        Assert.IsTrue(detail.IsBusy); Assert.IsFalse(detail.CanInteractWithDocument); Assert.IsFalse(await detail.CanCloseAsync(CancellationToken.None));
        await detail.ConfirmCommand.ExecuteAsync(null); await detail.SaveCommand.ExecuteAsync(null); await detail.RefreshCommand.ExecuteAsync(null); Assert.AreEqual(1, fixture.Orders.SaveCalls.Count);
        completion.SetResult(ServiceResult<ProcurementOrderCommandResult>.Fail(ServiceErrorCodes.OutcomeUnknown, "写入超时")); await saving;
        var original = JsonSerializer.Serialize(fixture.Orders.SaveCalls[0]);
        Assert.IsTrue(detail.HasPendingWrite); Assert.IsFalse(detail.CanEdit);
        detail.Header.Remark = "迟到修改不能替换原请求"; detail.Lines.Items[0].TaxPrice = 99m;
        await detail.RefreshCommand.ExecuteAsync(null); Assert.IsTrue(detail.HasPendingWrite);
        await fixture.ViewModel.InitializeAsync(); Assert.IsFalse(fixture.ViewModel.ConfirmCommand.CanExecute(null));
        fixture.Orders.SaveHandler = (id, command) => Task.FromResult(ServiceResult<ProcurementOrderCommandResult>.Ok(Receipt(id, command.ExpectedVersion, confirmed: false)));
        await fixture.ViewModel.RetryPendingCommand.ExecuteAsync(null);
        Assert.AreEqual(2, fixture.Orders.SaveCalls.Count); Assert.AreEqual(original, JsonSerializer.Serialize(fixture.Orders.SaveCalls[1]));
        Assert.IsTrue(detail.IsReadOnly); Assert.IsFalse(detail.HasPendingWrite);
    }

    /// <summary>保存成功刷新失败保持旧详情只读，恢复查询后才开放编辑，写入不重复。</summary>
    [TestMethod]
    public async Task SuccessfulSave_FailedReloadStaysReadOnlyUntilRefresh()
    {
        var row = Order("PO-REFRESH"); var fixture = CreateFixture(row); var failRead = false;
        fixture.Orders.DetailHandler = id => Task.FromResult(failRead ? ServiceResult<PurchaseOrderDetailDto>.Fail("读取失败") : ServiceResult<PurchaseOrderDetailDto>.Ok(Detail(id)));
        using var detail = CreateDetail(fixture); await detail.InitializeDocumentAsync(new PurchaseOrderDocumentOpenRequest { PurchaseOrderId = row.PurchaseOrderId }, CancellationToken.None);
        detail.Header.Remark = "保存值"; fixture.Orders.SaveHandler = (id, command) => { failRead = true; return Task.FromResult(ServiceResult<ProcurementOrderCommandResult>.Ok(Receipt(id, command.ExpectedVersion, confirmed: false))); };
        await detail.SaveCommand.ExecuteAsync(null); Assert.IsTrue(detail.IsReadOnly); Assert.IsFalse(detail.CanEdit); StringAssert.Contains(detail.StatusText, "已成功");
        await detail.SaveCommand.ExecuteAsync(null); Assert.AreEqual(1, fixture.Orders.SaveCalls.Count);
        failRead = false; await detail.RefreshCommand.ExecuteAsync(null); Assert.IsFalse(detail.IsReadOnly); Assert.IsTrue(detail.CanEdit);
    }

    /// <summary>保存失败保留编辑；确认前有未保存内容不能越过门禁。</summary>
    [TestMethod]
    public async Task UnsavedAndBusinessFailure_PreserveEditsAndBlockConfirmation()
    {
        var fixture = CreateFixture(Order("PO-EDIT")); var id = fixture.ViewModel.Orders.FirstOrDefault()?.PurchaseOrderId ?? Guid.NewGuid();
        fixture.Orders.DetailHandler = uid => Task.FromResult(ServiceResult<PurchaseOrderDetailDto>.Ok(Detail(uid)));
        using var detail = CreateDetail(fixture); await detail.InitializeDocumentAsync(new PurchaseOrderDocumentOpenRequest { PurchaseOrderId = id }, CancellationToken.None);
        detail.Header.Remark = "草稿"; Assert.IsFalse(detail.ConfirmCommand.CanExecute(null)); await detail.ConfirmCommand.ExecuteAsync(null); Assert.AreEqual(0, fixture.Orders.ConfirmCalls.Count);
        fixture.Orders.SaveHandler = (_, _) => Task.FromResult(ServiceResult<ProcurementOrderCommandResult>.Fail(ServiceErrorCodes.BusinessBlocked, "业务拒绝"));
        await detail.SaveCommand.ExecuteAsync(null); Assert.IsTrue(detail.HasUnsavedChanges); Assert.IsFalse(detail.HasPendingWrite); Assert.AreEqual("草稿", detail.Header.Remark); Assert.IsTrue(detail.CanEdit);
    }

    /// <summary>未决命令只归原账号，不能由另一个账号查找或重放。</summary>
    [TestMethod]
    public async Task PendingCommand_IsolatedByUserAndPayloadSnapshot()
    {
        var fixture = CreateFixture(Order("PO-USER")); var id = Guid.NewGuid(); var command = new ConfirmProcurementOrderCommand { CommandId = Guid.NewGuid(), ExpectedVersion = 2, ContextFingerprint = "original" };
        fixture.Session.Freeze("u", id, ProcurementCommandKind.Confirm, command); command.ContextFingerprint = "mutated";
        Assert.IsNull(fixture.Session.Find("other", id)); var result = await fixture.Session.SendAsync("other", id, fixture.Orders, new ApiCallRunnerStub()); Assert.IsFalse(result.Success); Assert.AreEqual(0, fixture.Orders.ConfirmCalls.Count);
        fixture.Orders.ConfirmHandler = (uid, request) => Task.FromResult(ServiceResult<ProcurementOrderCommandResult>.Ok(Receipt(uid, request.ExpectedVersion)));
        await fixture.Session.SendAsync("u", id, fixture.Orders, new ApiCallRunnerStub()); Assert.AreEqual("original", fixture.Orders.ConfirmCalls.Single().Command.ContextFingerprint);
    }

    private static PurchaseOrderDetailViewModel CreateDetail(Fixture fixture, IProcurementReversalDialogService? reversalDialogs = null) => new(fixture.Orders,
        DispatchProxy.Create<IProcurementMasterDataQueryApi, UnexpectedApiProxy>(), fixture.Dialogs, new MessageStub(), new NavigationStub(),
        new CurrentUserStub([PermissionKeys.ProcurementOrderRead, PermissionKeys.ProcurementOrderEdit, PermissionKeys.ProcurementOrderConfirm, PermissionKeys.ProcurementOrderCreate]), reversalDialogs: reversalDialogs, commandSession: fixture.Session, apiCallRunner: new ApiCallRunnerStub());

    private static ProcurementOrderCommandResult Receipt(Guid uid, int expectedVersion, bool confirmed = true) => new()
    {
        PurchaseOrderId = uid, Version = expectedVersion + 1, OrderNo = "PO-TEST", ConfirmationStatus = confirmed ? ConfirmationStatusEnum.已确认 : ConfirmationStatusEnum.待确认,
        LifecycleStatus = PurchaseOrderLifecycleStatusEnum.Active, ConfirmationId = confirmed ? Guid.NewGuid() : null,
        Lines = [new() { PurchaseOrderLineId = Guid.NewGuid(), Version = 2 }]
    };
    private static PurchaseOrderDetailDto Detail(Guid uid) => new()
    {
        PurchaseOrderId = uid, Version = 1, OrderNo = "PO-TEST", ConfirmationStatus = ConfirmationStatusEnum.待确认, LifecycleStatus = PurchaseOrderLifecycleStatusEnum.Active,
        CanEditDraft = true, CanConfirm = true, Lines = [new() { Uid = Guid.NewGuid(), Version = 1, ProductId = Guid.NewGuid(), Quantity = 1m, UnitSnapshot = "PCS", UnitPrice = 1m, TaxPrice = 1.1m }]
    };
    public class UnexpectedApiProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? args) => throw new NotSupportedException(method?.Name);
    }
}
