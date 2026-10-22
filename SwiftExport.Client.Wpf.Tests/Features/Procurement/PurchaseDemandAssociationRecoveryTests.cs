using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Net.Http;
using Microsoft.Extensions.Logging.Abstractions;
using SwiftExportClient.Services.Api;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Procurement.OrderManagement;
using SwiftExport.Contracts.Wrappers;
using Fixture = SwiftExport.Client.Wpf.Tests.Features.Procurement.PurchaseDemandAssociationViewModelTests.Fixture;

namespace SwiftExport.Client.Wpf.Tests.Features.Procurement;

/// <summary>关联命令曾经未知后不能用后续拒绝否认原写入，重试保持完整冻结负载。</summary>
[TestClass]
public sealed class PurchaseDemandAssociationRecoveryTests
{
    /// <summary>传输、业务未知及畸形回执后，鉴权和其他拒绝均保留冻结请求，恢复后仍用同一命令成功。</summary>
    [TestMethod]
    [DataRow("transport", ServiceErrorCodes.Forbidden)]
    [DataRow("unknown", ServiceErrorCodes.Unauthenticated)]
    [DataRow("receipt", ServiceErrorCodes.Forbidden)]
    [DataRow("nullLines", ServiceErrorCodes.Unauthenticated)]
    [DataRow("receipt", ServiceErrorCodes.Validation)]
    [DataRow("unknown", ServiceErrorCodes.BusinessBlocked)]
    [DataRow("unknown", ServiceErrorCodes.VersionConflict)]
    [DataRow("unknown", ServiceErrorCodes.CommandConflict)]
    [DataRow("unknown", ServiceErrorCodes.NotFound)]
    [DataRow("unknown", ServiceErrorCodes.StateBlocked)]
    public async Task AssociationRecovery_UnknownRefusalAndRecoveryUseFrozenRequest(string unknown, string rejection)
    {
        using var f = new Fixture(); await f.ReadyAsync();
        if (unknown == "transport") f.SendAsync = _ => Task.FromException<ServiceResult<ProcurementOrderCommandResult>>(new HttpRequestException());
        else f.Send = _ => unknown switch
        {
            "receipt" => ServiceResult<ProcurementOrderCommandResult>.Ok(new() { PurchaseOrderId = f.Context.PurchaseOrderId }),
            "nullLines" => ServiceResult<ProcurementOrderCommandResult>.Ok(new() { PurchaseOrderId = f.Context.PurchaseOrderId,
                Version = 2, ConfirmationStatus = f.Context.ConfirmationStatus, Lines = null! }),
            _ => ServiceResult<ProcurementOrderCommandResult>.Fail(ServiceErrorCodes.OutcomeUnknown, "未知")
        };
        await f.Vm.SubmitCommand.ExecuteAsync(null); Assert.IsTrue(f.Vm.HasPendingCommand);
        var frozen = f.Writes.Single(); f.Vm.Reason = "外部修改"; f.Vm.Plan[0].Quantity = 2;
        f.SendAsync = null; f.Send = _ => ServiceResult<ProcurementOrderCommandResult>.Fail(rejection, "本次拒绝");
        await f.Vm.RetryCommand.ExecuteAsync(null); await f.Vm.RetryCommand.ExecuteAsync(null);
        Assert.IsTrue(f.Vm.HasPendingCommand); Assert.IsFalse(f.Vm.CanInteract); Assert.IsFalse(await f.Vm.CanCloseAsync(default));
        Assert.HasCount(3, f.Writes); Assert.IsTrue(f.Writes.All(x => x == frozen));
        f.Send = f.Success; await f.Vm.RetryCommand.ExecuteAsync(null);
        Assert.AreEqual(frozen, f.Writes.Last()); Assert.IsFalse(f.Vm.HasPendingCommand); Assert.IsTrue(f.Vm.CanEditPlan);
    }

    /// <summary>请求边界直接抛异常后同样记录未知历史，随后的鉴权拒绝不能解除冻结。</summary>
    [TestMethod]
    [DataRow(ServiceErrorCodes.Forbidden)][DataRow(ServiceErrorCodes.Unauthenticated)]
    public async Task AssociationRecovery_ThrownBoundaryRetainsUnknownHistory(string rejection)
    {
        var runner = new ThrowOnceRunner(); using var f = new Fixture(runner); await f.ReadyAsync();
        runner.ThrowNext = true; await f.Vm.SubmitCommand.ExecuteAsync(null);
        Assert.IsTrue(f.Vm.HasPendingCommand);
        f.Send = _ => ServiceResult<ProcurementOrderCommandResult>.Fail(rejection, "暂时拒绝");
        await f.Vm.RetryCommand.ExecuteAsync(null); Assert.IsTrue(f.Vm.HasPendingCommand);
        var frozen = f.Writes.Single(); f.Send = f.Success; await f.Vm.RetryCommand.ExecuteAsync(null);
        Assert.AreEqual(frozen, f.Writes.Last()); Assert.IsFalse(f.Vm.HasPendingCommand);
    }

    /// <summary>首次明确拒绝仍解除未决门禁并保留暂存意图，不将未执行误报为未知。</summary>
    [TestMethod]
    [DataRow(ServiceErrorCodes.Forbidden)][DataRow(ServiceErrorCodes.Unauthenticated)]
    [DataRow(ServiceErrorCodes.Validation)][DataRow(ServiceErrorCodes.BusinessBlocked)]
    [DataRow(ServiceErrorCodes.VersionConflict)][DataRow(ServiceErrorCodes.CommandConflict)]
    [DataRow(ServiceErrorCodes.NotFound)][DataRow(ServiceErrorCodes.StateBlocked)]
    public async Task AssociationRecovery_FirstDefiniteRefusalKeepsExistingBehavior(string rejection)
    {
        using var f = new Fixture(); await f.ReadyAsync();
        f.Send = _ => ServiceResult<ProcurementOrderCommandResult>.Fail(rejection, "首次拒绝");
        await f.Vm.SubmitCommand.ExecuteAsync(null);
        Assert.IsFalse(f.Vm.HasPendingCommand); Assert.HasCount(1, f.Vm.Plan); Assert.IsFalse(f.Vm.RetryCommand.CanExecute(null));
    }

    /// <summary>仅使指定一次请求越过正常错误转换边界抛异常，其他查询沿用真实 Runner。</summary>
    private sealed class ThrowOnceRunner : IApiCallRunner
    {
        private readonly ApiCallRunner _inner = new(NullLogger<ApiCallRunner>.Instance);
        public bool ThrowNext { get; set; }
        /// <inheritdoc/>
        public Task<ServiceResult<T>> RunAsync<T>(Func<Task<ServiceResult<T>>> action, string failureMessage = "请求失败")
        {
            if (ThrowNext) { ThrowNext = false; throw new InvalidOperationException("测试边界异常"); }
            return _inner.RunAsync(action, failureMessage);
        }
        /// <inheritdoc/>
        public Task<ServiceResult<T>> RunAsync<T>(Func<Task<ServiceResult<T>>> action, CancellationToken cancellationToken, string failureMessage = "请求失败")
            => _inner.RunAsync(action, cancellationToken, failureMessage);
        /// <inheritdoc/>
        public Task<ServiceResult<T>> RunValueAsync<T>(Func<Task<T>> action, string successMessage = "成功", string failureMessage = "请求失败")
            => _inner.RunValueAsync(action, successMessage, failureMessage);
    }
}
