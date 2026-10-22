using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Refit;
using SwiftExport.Contracts.Apis.Procurement;
using SwiftExport.Contracts.Apis.MasterData;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Procurement.OrderManagement;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderDetailDto;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderManagerDto;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Authentication;
using SwiftExportClient.Features.Procurement.PurchaseOrders.Models;
using SwiftExportClient.Features.Procurement.PurchaseOrders.Services;
using SwiftExportClient.Features.Procurement.PurchaseOrders.ViewModels;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.UI;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace SwiftExport.Client.Wpf.Tests.Features.Procurement;

/// <summary>反向命令未知结果、权限撤销及预检失败恢复的离线回归。</summary>
public sealed partial class PurchaseOrderManagerBatchConfirmationTests
{
    /// <summary>首次认证拒绝可以解除；首次提交结果未知后撤权拒绝不能丢失原命令或污染其他账号。</summary>
    [TestMethod]
    [DataRow(ServiceErrorCodes.Forbidden, "transport")]
    [DataRow(ServiceErrorCodes.Unauthenticated, "transport")]
    [DataRow(ServiceErrorCodes.Forbidden, "reply")]
    [DataRow(ServiceErrorCodes.Unauthenticated, "reply")]
    [DataRow(ServiceErrorCodes.Forbidden, "receipt")]
    [DataRow(ServiceErrorCodes.Unauthenticated, "receipt")]
    public async Task ReversalRecovery_UnknownThenAuthorizationFailureRetainsOriginal(string error, string unknownKind)
    {
        using var f = CreateReversalRecovery(ProcurementReversalOperation.Void);
        var preview = RecoveryPreview(f.Row.PurchaseOrderId, ProcurementReversalOperation.Void, new() { ExpectedVersion = 1 });
        Assert.IsTrue(f.Session.Freeze("u", preview, "保留原作废原因"));
        f.Proxy.Handler = (_, _) => Task.FromResult(ServiceResult<ProcurementOrderCommandResult>.Fail(error, "撤权"));
        await f.Session.SendAsync("u", f.Row.PurchaseOrderId, f.Api);
        Assert.IsNull(f.Session.Find("u", f.Row.PurchaseOrderId));

        Assert.IsTrue(f.Session.Freeze("u", preview, "结果未知的原原因"));
        var original = f.Session.Find("u", f.Row.PurchaseOrderId)!.CommandJson;
        f.Proxy.Handler = (_, _) => unknownKind switch
        {
            "transport" => Task.FromException<ServiceResult<ProcurementOrderCommandResult>>(new HttpRequestException()),
            "reply" => Task.FromResult(ServiceResult<ProcurementOrderCommandResult>.Fail(ServiceErrorCodes.OutcomeUnknown, "结果未知")),
            _ => Task.FromResult(ServiceResult<ProcurementOrderCommandResult>.Ok(new() { PurchaseOrderId = f.Row.PurchaseOrderId }))
        };
        if (unknownKind == "transport")
            await Assert.ThrowsExactlyAsync<HttpRequestException>(() => f.Session.SendAsync("u", f.Row.PurchaseOrderId, f.Api));
        else
            Assert.IsFalse((await f.Session.SendAsync("u", f.Row.PurchaseOrderId, f.Api)).Success);
        f.Proxy.Handler = (_, _) => Task.FromResult(ServiceResult<ProcurementOrderCommandResult>.Fail(error, "暂时无权查询原命令"));
        await f.Session.SendAsync("u", f.Row.PurchaseOrderId, f.Api);
        Assert.AreEqual(original, f.Session.Find("u", f.Row.PurchaseOrderId)?.CommandJson);
        Assert.IsNull(f.Session.Find("other", f.Row.PurchaseOrderId));
        f.Proxy.Handler = (_, args) =>
        {
            Assert.AreEqual(original, JsonSerializer.Serialize(args![1]));
            return Task.FromResult(ServiceResult<ProcurementOrderCommandResult>.Ok(RecoveryReceipt(f.Row.PurchaseOrderId, ProcurementReversalOperation.Void, args![1]!)));
        };
        Assert.IsTrue((await f.Session.SendAsync("u", f.Row.PurchaseOrderId, f.Api)).Success);
        Assert.IsNull(f.Session.Find("u", f.Row.PurchaseOrderId));
    }

    /// <summary>管理和详情重试按原操作检查权限；恢复权限后详情即使只读仍可核实原命令。</summary>
    [TestMethod]
    [DataRow(ProcurementReversalOperation.CancelConfirmation, false)]
    [DataRow(ProcurementReversalOperation.DeleteDraft, false)]
    [DataRow(ProcurementReversalOperation.Void, false)]
    [DataRow(ProcurementReversalOperation.DeleteLines, false)]
    [DataRow(ProcurementReversalOperation.CancelConfirmation, true)]
    [DataRow(ProcurementReversalOperation.DeleteDraft, true)]
    [DataRow(ProcurementReversalOperation.Void, true)]
    [DataRow(ProcurementReversalOperation.DeleteLines, true)]
    public async Task ReversalRecovery_RetryRequiresOriginalOperationPermission(ProcurementReversalOperation operation, bool detail)
    {
        using var f = CreateReversalRecovery(operation); await f.Manager.InitializeAsync();
        await f.Detail.InitializeDocumentAsync(new PurchaseOrderDocumentOpenRequest { PurchaseOrderId = f.Row.PurchaseOrderId }, CancellationToken.None);
        var preview = RecoveryPreview(f.Row.PurchaseOrderId, operation, new() { ExpectedVersion = 1,
            Lines = [new() { PurchaseOrderLineId = Guid.NewGuid(), Version = 1 }] });
        Assert.IsTrue(f.Session.Freeze("u", preview, "原操作")); var original = f.Session.Find("u", f.Row.PurchaseOrderId)!.CommandJson;
        var permission = operation == ProcurementReversalOperation.CancelConfirmation ? PermissionKeys.ProcurementOrderConfirm
            : operation == ProcurementReversalOperation.Void ? PermissionKeys.ProcurementOrderVoid : PermissionKeys.ProcurementOrderEdit;
        var unrelatedPermission = permission == PermissionKeys.ProcurementOrderConfirm ? PermissionKeys.ProcurementOrderEdit : PermissionKeys.ProcurementOrderConfirm;
        f.User.SetCurrentUser(new() { UserId = "u", Permissions = [PermissionKeys.ProcurementOrderRead, unrelatedPermission] });
        await f.Manager.RefreshCommand.ExecuteAsync(null);
        var retry = detail ? f.Detail.RetryReversalCommand : f.Manager.RetryReversalCommand;
        Assert.IsFalse(retry.CanExecute(null)); await retry.ExecuteAsync(null);
        Assert.AreEqual(0, f.Messages.Writes); Assert.AreEqual(original, f.Session.Find("u", f.Row.PurchaseOrderId)?.CommandJson);
        f.User.SetCurrentUser(new() { UserId = "u", Permissions = [PermissionKeys.ProcurementOrderRead, permission] });
        await f.Manager.RefreshCommand.ExecuteAsync(null);
        Assert.IsTrue(retry.CanExecute(null)); await retry.ExecuteAsync(null);
        Assert.AreEqual(1, f.Messages.Writes); Assert.IsNull(f.Session.Find("u", f.Row.PurchaseOrderId));
    }

    /// <summary>成功后的刷新失败不能保留旧命令资格；恢复查询后采用服务端新状态及资格。</summary>
    [TestMethod]
    [DataRow(ProcurementReversalOperation.Void)]
    [DataRow(ProcurementReversalOperation.CancelConfirmation)]
    public async Task ReversalRecovery_SuccessfulWriteFailedRefreshInvalidatesOldRow(ProcurementReversalOperation operation)
    {
        using var f = CreateReversalRecovery(operation); await f.Manager.InitializeAsync(); var row = f.Manager.SelectedOrder!;
        var notifications = new List<string?>(); row.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);
        f.Queries.SearchHandler = _ => Task.FromResult(ServiceResult<PagedResponseBase<PurchaseOrderManagerListItemDto>>.Fail("刷新失败"));
        await RecoveryCommand(f.Manager, operation).ExecuteAsync(null);
        Assert.AreEqual(1, f.Messages.Writes); Assert.AreEqual(2, row.Version);
        Assert.IsFalse(f.Manager.ConfirmCommand.CanExecute(null)); Assert.IsFalse(f.Manager.DeleteDraftCommand.CanExecute(null));
        Assert.IsFalse(f.Manager.VoidOrderCommand.CanExecute(null)); Assert.IsFalse(f.Manager.CancelConfirmationCommand.CanExecute(null));
        Assert.IsTrue(notifications.Contains(string.Empty) || notifications.Contains(nameof(row.ConfirmationStatus)));
        StringAssert.Contains(f.Manager.ResultSummary, "操作已成功"); StringAssert.Contains(f.Manager.ResultSummary, "刷新失败");
        f.Row.Version = 2; f.Row.ConfirmationStatus = ConfirmationStatusEnum.待确认; f.Row.CanDeleteDraft = false;
        f.Row.LifecycleStatus = operation == ProcurementReversalOperation.Void ? PurchaseOrderLifecycleStatusEnum.Voided : PurchaseOrderLifecycleStatusEnum.Active;
        f.Row.CanConfirm = operation != ProcurementReversalOperation.Void; f.Queries.SearchHandler = null;
        await f.Manager.RefreshCommand.ExecuteAsync(null);
        Assert.AreEqual(operation != ProcurementReversalOperation.Void, f.Manager.ConfirmCommand.CanExecute(null));
    }

    /// <summary>同账号刷新身份快照后旧预检不能继续冻结或提交，页面只接纳当前会话的操作。</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ReversalRecovery_SessionRefreshInvalidatesInFlightPreview(bool detail)
    {
        using var f = CreateReversalRecovery(ProcurementReversalOperation.Void); await f.Manager.InitializeAsync();
        await f.Detail.InitializeDocumentAsync(new PurchaseOrderDocumentOpenRequest { PurchaseOrderId = f.Row.PurchaseOrderId }, CancellationToken.None);
        var completion = new TaskCompletionSource<ServiceResult<ProcurementReversalPreviewDto>>(); var normal = f.Proxy.Handler;
        f.Proxy.Handler = (method, args) => method!.Name.StartsWith("Preview", StringComparison.Ordinal) ? completion.Task : normal(method, args);
        var submitting = (detail ? f.Detail.VoidOrderCommand : f.Manager.VoidOrderCommand).ExecuteAsync(null);
        f.User.SetCurrentUser(f.User.CurrentUser!);
        completion.SetResult(ServiceResult<ProcurementReversalPreviewDto>.Ok(RecoveryPreview(f.Row.PurchaseOrderId,
            ProcurementReversalOperation.Void, new() { ExpectedVersion = 1 })));
        await submitting;
        Assert.AreEqual(0, f.Messages.Writes); Assert.IsNull(f.Session.Find("u", f.Row.PurchaseOrderId));
    }

    /// <summary>三种预检的网络和 Refit 异常必须给出实际摘要或消息，失败不建立写命令。</summary>
    [TestMethod]
    [DataRow(ProcurementReversalOperation.CancelConfirmation, false, false)]
    [DataRow(ProcurementReversalOperation.DeleteDraft, false, false)]
    [DataRow(ProcurementReversalOperation.Void, false, false)]
    [DataRow(ProcurementReversalOperation.CancelConfirmation, true, false)]
    [DataRow(ProcurementReversalOperation.DeleteDraft, true, false)]
    [DataRow(ProcurementReversalOperation.Void, true, false)]
    [DataRow(ProcurementReversalOperation.CancelConfirmation, false, true)]
    [DataRow(ProcurementReversalOperation.DeleteDraft, false, true)]
    [DataRow(ProcurementReversalOperation.Void, false, true)]
    [DataRow(ProcurementReversalOperation.CancelConfirmation, true, true)]
    [DataRow(ProcurementReversalOperation.DeleteDraft, true, true)]
    [DataRow(ProcurementReversalOperation.Void, true, true)]
    public async Task ReversalRecovery_PreviewFailureHasVisibleFeedback(ProcurementReversalOperation operation, bool detail, bool refit)
    {
        using var f = CreateReversalRecovery(operation); await f.Manager.InitializeAsync();
        await f.Detail.InitializeDocumentAsync(new PurchaseOrderDocumentOpenRequest { PurchaseOrderId = f.Row.PurchaseOrderId }, CancellationToken.None);
        Exception failure = new HttpRequestException("不应暴露的传输详情");
        if (refit)
            failure = await ApiException.Create(new HttpRequestMessage(HttpMethod.Post, "https://unit.test/preview"), HttpMethod.Post,
                new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent("{}") }, new RefitSettings());
        var normal = f.Proxy.Handler;
        f.Proxy.Handler = (method, args) => method!.Name.StartsWith("Preview", StringComparison.Ordinal)
            ? Task.FromException<ServiceResult<ProcurementReversalPreviewDto>>(failure) : normal(method, args);
        await (detail ? RecoveryCommand(f.Detail, operation) : RecoveryCommand(f.Manager, operation)).ExecuteAsync(null);
        Assert.AreEqual(1, f.Messages.Warnings.Count); Assert.AreEqual(0, f.Messages.Writes);
        var summary = detail ? f.Detail.StatusText : f.Manager.ResultSummary;
        Assert.IsFalse(string.IsNullOrWhiteSpace(summary)); Assert.AreEqual(summary, f.Messages.Warnings.Single());
        Assert.IsFalse(summary.Contains("不应暴露", StringComparison.Ordinal)); Assert.IsNull(f.Session.Find("u", f.Row.PurchaseOrderId));
    }

    /// <summary>复用真实 API Runner 和用户权限快照，所有业务 API 均由内存代理提供。</summary>
    private static RecoveryFixture CreateReversalRecovery(ProcurementReversalOperation operation)
    {
        var row = Order("PO-RECOVERY"); row.CanDeleteDraft = true;
        if (operation == ProcurementReversalOperation.CancelConfirmation) row.ConfirmationStatus = ConfirmationStatusEnum.已确认;
        var queries = new ManagerApiStub([row]); var messages = new RecoveryMessages();
        queries.LinesHandler = (_, _) => Task.FromResult(ServiceResult<List<PurchaseOrderManagerLineDto>>.Ok([new() { PurchaseOrderLineId = row.PurchaseOrderId, Version = 1 }]));
        var user = new CurrentUserService(); user.SetCurrentUser(new() { UserId = "u", Permissions = [PermissionKeys.ProcurementOrderRead,
            PermissionKeys.ProcurementOrderEdit, PermissionKeys.ProcurementOrderConfirm, PermissionKeys.ProcurementOrderVoid] });
        var session = new ProcurementReversalSession(); var api = DispatchProxy.Create<IProcurementOrderApi, ProcurementReversalSessionTests.ApiProxy>();
        var proxy = (ProcurementReversalSessionTests.ApiProxy)(object)api;
        proxy.Handler = (method, args) =>
        {
            if (method!.Name == nameof(IProcurementOrderApi.GetDetailAsync))
            {
                var dto = Detail(row.PurchaseOrderId); dto.CanDeleteDraft = true; dto.ConfirmationStatus = row.ConfirmationStatus;
                return Task.FromResult(ServiceResult<PurchaseOrderDetailDto>.Ok(dto));
            }
            if (method.Name.StartsWith("Preview", StringComparison.Ordinal))
                return Task.FromResult(ServiceResult<ProcurementReversalPreviewDto>.Ok(RecoveryPreview((Guid)args![0]!, operation, (ProcurementReversalPreviewRequest)args[1]!)));
            messages.Writes++;
            return Task.FromResult(ServiceResult<ProcurementOrderCommandResult>.Ok(RecoveryReceipt((Guid)args![0]!, operation, args[1]!)));
        };
        var runner = new ApiCallRunner(NullLogger<ApiCallRunner>.Instance); var dialogs = new DialogServiceStub();
        var manager = new PurchaseOrderManagerViewModel(queries, api, new ProcurementDialogStub(), new NavigationStub(), dialogs, runner, messages, user,
            reversalSession: session, reversalDialogs: new DeleteDialog());
        var detail = new PurchaseOrderDetailViewModel(api, DispatchProxy.Create<IProcurementMasterDataQueryApi, UnexpectedApiProxy>(), dialogs, messages,
            new NavigationStub(), user, reversalSession: session, reversalDialogs: new DeleteDialog(), apiCallRunner: runner);
        return new(manager, detail, user, session, queries, api, proxy, row, messages);
    }

    /// <summary>构造身份和全部行版本一致的预检，不能借测试替身绕过客户端检查。</summary>
    private static ProcurementReversalPreviewDto RecoveryPreview(Guid uid, ProcurementReversalOperation operation, ProcurementReversalPreviewRequest request) => new()
    {
        PurchaseOrderId = uid, Version = request.ExpectedVersion, Operation = operation, CanExecute = true,
        Lines = request.Lines.Select(line => new ProcurementReversalLineDto { PurchaseOrderLineId = line.PurchaseOrderLineId, Version = line.Version }).ToList()
    };

    /// <summary>按已有协议返回版本递增及删除事实，不模拟后端业务校验。</summary>
    private static ProcurementOrderCommandResult RecoveryReceipt(Guid uid, ProcurementReversalOperation operation, object command)
    {
        var input = JsonSerializer.SerializeToElement(command);
        return new() { PurchaseOrderId = uid, Version = input.GetProperty("ExpectedVersion").GetInt32() + 1,
            LifecycleStatus = operation == ProcurementReversalOperation.Void ? PurchaseOrderLifecycleStatusEnum.Voided : PurchaseOrderLifecycleStatusEnum.Active,
            ConfirmationStatus = ConfirmationStatusEnum.待确认, IsDeleted = operation == ProcurementReversalOperation.DeleteDraft,
            Lines = operation == ProcurementReversalOperation.DeleteDraft
                ? input.GetProperty("Lines").EnumerateArray().Select(line => new ProcurementLineVersionDto
                    { PurchaseOrderLineId = line.GetProperty("PurchaseOrderLineId").GetGuid(), Version = line.GetProperty("Version").GetInt32() + 1 }).ToList()
                : [new() { PurchaseOrderLineId = Guid.NewGuid(), Version = 2 }] };
    }

    /// <summary>选择真实管理页命令，覆盖公开命令入口而非调用私有实现。</summary>
    private static IAsyncRelayCommand RecoveryCommand(PurchaseOrderManagerViewModel vm, ProcurementReversalOperation operation) => operation switch
    { ProcurementReversalOperation.CancelConfirmation => vm.CancelConfirmationCommand, ProcurementReversalOperation.DeleteDraft => vm.DeleteDraftCommand, _ => vm.VoidOrderCommand };

    /// <summary>选择详情同一业务操作的公开命令。</summary>
    private static IAsyncRelayCommand RecoveryCommand(PurchaseOrderDetailViewModel vm, ProcurementReversalOperation operation) => operation switch
    { ProcurementReversalOperation.CancelConfirmation => vm.CancelConfirmationCommand, ProcurementReversalOperation.DeleteDraft => vm.DeleteDraftCommand, _ => vm.VoidOrderCommand };

    /// <summary>收集真正发送的写入及页面消息，证明错误反馈和无重复写入。</summary>
    private sealed class RecoveryMessages : IMessageService
    {
        public int Writes { get; set; }
        public List<string> Warnings { get; } = [];
        public void ShowWarning(string title, string message) => Warnings.Add(message);
        public void ShowError(string title, string message) => Warnings.Add(message);
        public void ShowInfo(string title, string message) { }
        public void ShowSuccess(string title, string message) { }
    }

    /// <summary>释放两个页面订阅，保留每个用例独立的用户和命令会话。</summary>
    private sealed record RecoveryFixture(PurchaseOrderManagerViewModel Manager, PurchaseOrderDetailViewModel Detail, CurrentUserService User,
        ProcurementReversalSession Session, ManagerApiStub Queries, IProcurementOrderApi Api, ProcurementReversalSessionTests.ApiProxy Proxy,
        PurchaseOrderManagerListItemDto Row, RecoveryMessages Messages) : IDisposable
    {
        /// <summary>对称释放管理页和详情的会话订阅。</summary>
        public void Dispose() { Manager.Dispose(); Detail.Dispose(); }
    }
}
