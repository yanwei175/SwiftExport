using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.Apis.Procurement;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Procurement.OrderManagement;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderManagerDto;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Authentication;
using SwiftExportClient.Features.Procurement.PurchaseOrders.ViewModels;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.UI;

namespace SwiftExport.Client.Wpf.Tests.Features.Procurement;

/// <summary>验证独立关联工作区的意图、容量、来源锁定和冻结命令协议，不调用业务数据库。</summary>
[TestClass]
public sealed class PurchaseDemandAssociationViewModelTests
{
    /// <summary>采购选择把待关联筛选交给服务端，初始化和行选择分别使用专用用例。</summary>
    [TestMethod]
    public async Task PickerAndLineQueriesUseDedicatedContextAndSourceLock()
    {
        using var f = new Fixture();
        await f.OpenAsync();
        Assert.IsTrue(f.Searches.Single().OnlyWithUnlinkedQuantity);
        Assert.HasCount(0, f.CandidateSearches);
        await f.SelectAsync(0);
        f.Vm.AllocationQuantity = 3; f.Vm.AddAllocationCommand.Execute(null);
        await f.Vm.NextCandidatePageCommand.ExecuteAsync(null);
        Assert.HasCount(1, f.Vm.Plan);
        Assert.AreEqual(f.Demand.SourceUid, f.CandidateSearches.Last().SourceUid);
        Assert.AreEqual(f.Demand.SourceType, f.CandidateSearches.Last().SourceType);
        f.Vm.OrderPicker.SelectedCustomer = new() { CustomerCode = "C-SELECTED" };
        var supplier = Guid.NewGuid(); f.Vm.OrderPicker.SelectedSupplier = new() { Uid = supplier };
        await f.Vm.OrderPicker.SearchCommand.ExecuteAsync(null);
        Assert.AreEqual("C-SELECTED", f.Searches.Last().CustomerCode);
        Assert.AreEqual(supplier, f.Searches.Last().SupplierId);
        await f.Vm.InitializeAsync();
        Assert.HasCount(2, f.Searches);
    }

    /// <summary>同配对更新而非重复；多采购行共同占用同一需求容量，不能按行各自放行。</summary>
    [TestMethod]
    public async Task RepeatedPairAndAggregateCapacityAreChecked()
    {
        using var f = new Fixture(); await f.OpenAsync(); await f.SelectAsync(0);
        f.Vm.AllocationQuantity = 6; f.Vm.AddAllocationCommand.Execute(null);
        f.Vm.AllocationQuantity = 7; f.Vm.AddAllocationCommand.Execute(null);
        Assert.HasCount(1, f.Vm.Plan); Assert.AreEqual(7m, f.Vm.Plan.Single().Quantity);
        await f.SelectAsync(1); f.Vm.AllocationQuantity = 9; f.Vm.AddAllocationCommand.Execute(null);
        Assert.HasCount(1, f.Vm.Plan); StringAssert.Contains(f.Vm.StatusText, "物料需求可分配");
        f.Vm.AllocationQuantity = 8; f.Vm.AddAllocationCommand.Execute(null);
        Assert.HasCount(2, f.Vm.Plan);
        await f.Vm.PreviewCommand.ExecuteAsync(null); Assert.IsNotNull(f.Vm.Preview);
        f.Vm.Plan[0].Quantity = 8;
        Assert.IsNull(f.Vm.Preview);
        await f.Vm.PreviewCommand.ExecuteAsync(null);
        Assert.HasCount(1, f.Previews); StringAssert.Contains(f.Vm.StatusText, "物料需求可分配");
    }

    /// <summary>非标必须明确认可，未知单位及其他来源不进入可提交清单。</summary>
    [TestMethod]
    public async Task NonStandardAcceptanceUnitAndSingleSourceAreExplicit()
    {
        using var f = new Fixture();
        f.Context.Lines[0].IsNonStandard = true; f.Context.Lines[0].ProductId = null;
        f.Demand.IsNonStandard = true; f.Demand.ProductId = null;
        await f.OpenAsync(); await f.SelectAsync(0);
        f.Vm.AddAllocationCommand.Execute(null); Assert.HasCount(0, f.Vm.Plan);
        f.Vm.AcceptNonStandardMatch = true; f.Vm.AddAllocationCommand.Execute(null); Assert.HasCount(1, f.Vm.Plan);
        await f.Vm.PreviewCommand.ExecuteAsync(null);
        Assert.IsTrue(f.Previews.Single().Items.Single().AcceptNonStandardMatch);
        f.Vm.Plan[0].AcceptNonStandardMatch = false; Assert.IsNull(f.Vm.Preview);
        f.Vm.Plan[0].AcceptNonStandardMatch = true;
        f.Demand = Clone(f.Demand); f.Demand.NetDemandId = Guid.NewGuid(); f.Demand.SourceUid = Guid.NewGuid();
        await f.SelectAsync(0); f.Vm.AcceptNonStandardMatch = true; f.Vm.AddAllocationCommand.Execute(null);
        Assert.HasCount(1, f.Vm.Plan); StringAssert.Contains(f.Vm.StatusText, "同一张来源单");
        f.Vm.SelectedPlanRow = f.Vm.Plan[0]; f.Vm.RemoveAllocationCommand.Execute(null);
        f.Demand.Unit = null!; await f.SelectAsync(0); f.Vm.AcceptNonStandardMatch = true; f.Vm.AddAllocationCommand.Execute(null);
        Assert.HasCount(0, f.Vm.Plan); StringAssert.Contains(f.Vm.StatusText, "单位必须明确");
    }

    /// <summary>超时冻结全部请求和原因；重试成功后只重读，不再发送另一个命令。</summary>
    [TestMethod]
    public async Task UnknownOutcomeRetriesIdenticalPayloadAndSuccessRefreshFailureOnlyReads()
    {
        using var f = new Fixture(); await f.ReadyAsync();
        f.Send = _ => ServiceResult<ProcurementOrderCommandResult>.Fail(ServiceErrorCodes.OutcomeUnknown, "timeout");
        await f.Vm.SubmitCommand.ExecuteAsync(null);
        Assert.IsTrue(f.Vm.HasPendingCommand); Assert.IsFalse(f.Vm.CanInteract);
        Assert.IsFalse(await f.Vm.CanCloseAsync(default));
        var frozen = f.Writes.Single();
        f.Vm.Reason = "模拟外部修改"; f.Vm.Plan[0].Quantity = 2;
        f.Send = f.Success; f.FailContext = true;
        await f.Vm.RetryCommand.ExecuteAsync(null);
        Assert.AreEqual(frozen, f.Writes.Last());
        Assert.IsFalse(f.Vm.HasPendingCommand); Assert.IsTrue(f.Vm.RefreshRequired);
        Assert.HasCount(0, f.Vm.Plan); StringAssert.Contains(f.Vm.StatusText, "关联已成功，但刷新失败");
        f.FailContext = false; f.Context.Version = 1;
        await f.Vm.RefreshCommand.ExecuteAsync(null);
        Assert.IsTrue(f.Vm.RefreshRequired, "旧版本上下文不能重新开放已成功分配的数量。");
        f.Context.Version = 2; await f.Vm.RefreshCommand.ExecuteAsync(null);
        Assert.IsFalse(f.Vm.RefreshRequired); Assert.HasCount(2, f.Writes);
    }

    /// <summary>版本冲突保留意图；重读后逐条显式复核，多个待复核项不能互相阻断复核入口。</summary>
    [TestMethod]
    public async Task ConflictKeepsIntentAndRequiresExplicitReviewWithNewVersions()
    {
        using var f = new Fixture(); await f.OpenAsync();
        foreach (var index in new[] { 0, 1 }) { await f.SelectAsync(index); f.Vm.AllocationQuantity = 2; f.Vm.AddAllocationCommand.Execute(null); }
        await f.Vm.PreviewCommand.ExecuteAsync(null); f.Vm.Reason = "关联";
        f.Send = _ => ServiceResult<ProcurementOrderCommandResult>.Fail(ServiceErrorCodes.VersionConflict, "version changed");
        await f.Vm.SubmitCommand.ExecuteAsync(null);
        Assert.IsTrue(f.Vm.NeedsReload); Assert.HasCount(2, f.Vm.Plan); Assert.IsNull(f.Vm.Preview);
        f.Context.Version++; foreach (var line in f.Context.Lines) line.Version++;
        f.Demand.Version++; f.Demand.SourceDemandVersion++;
        await f.Vm.RefreshCommand.ExecuteAsync(null);
        Assert.IsTrue(f.Vm.Plan.All(x => x.NeedsReview));
        await f.Vm.PreviewCommand.ExecuteAsync(null); Assert.HasCount(1, f.Previews);
        foreach (var index in new[] { 0, 1 }) { await f.SelectAsync(index); f.Vm.AllocationQuantity = 2; f.Vm.AddAllocationCommand.Execute(null); }
        Assert.IsTrue(f.Vm.Plan.All(x => !x.NeedsReview));
        await f.Vm.PreviewCommand.ExecuteAsync(null);
        Assert.AreEqual(2, f.Previews.Last().ExpectedVersion);
        Assert.IsTrue(f.Previews.Last().Items.All(x => x.ExpectedDemandVersion == 2 && x.ExpectedSourceDemandVersion == 4));
    }

    /// <summary>在途预览不能接纳被修改的意图；释放页面后候选响应不得回写。</summary>
    [TestMethod]
    public async Task StalePreviewAndDisposedQueryCannotReplaceState()
    {
        using var f = new Fixture(); await f.ReadyAsync();
        var completion = new TaskCompletionSource<ServiceResult<ProcurementDemandAssociationPreviewDto>>();
        f.PreviewHandler = _ => completion.Task;
        var loading = f.Vm.PreviewCommand.ExecuteAsync(null);
        f.Vm.Plan[0].Quantity = 2;
        completion.SetResult(ServiceResult<ProcurementDemandAssociationPreviewDto>.Ok(f.MakePreview(f.Previews.Last())));
        await loading; Assert.IsNull(f.Vm.Preview);
        var query = new TaskCompletionSource<ServiceResult<PagedResponseBase<ProcurementDemandCandidateDto>>>();
        f.CandidateHandler = _ => query.Task;
        var candidates = f.Vm.SearchCandidatesCommand.ExecuteAsync(null);
        f.Vm.Dispose(); query.SetResult(ServiceResult<PagedResponseBase<ProcurementDemandCandidateDto>>.Ok(new() { Items = [], PageSize = 30 }));
        await candidates; Assert.HasCount(1, f.Vm.Candidates);
    }

    /// <summary>取消提交、缺少权限、在途重复提交及无效成功回执不得产生额外写入或解除冻结。</summary>
    [TestMethod]
    public async Task GatesProtectPermissionCancellationBusyAndMalformedReceipt()
    {
        using var f = new Fixture(); await f.ReadyAsync();
        f.Dialogs.Confirm = false; await f.Vm.SubmitCommand.ExecuteAsync(null); Assert.HasCount(0, f.Writes);
        f.Dialogs.Confirm = true;
        var response = new TaskCompletionSource<ServiceResult<ProcurementOrderCommandResult>>();
        f.SendAsync = _ => response.Task;
        var writing = f.Vm.SubmitCommand.ExecuteAsync(null);
        Assert.IsTrue(f.Vm.IsBusy); Assert.IsFalse(await f.Vm.CanCloseAsync(default));
        await f.Vm.RetryCommand.ExecuteAsync(null); Assert.HasCount(1, f.Writes);
        response.SetResult(ServiceResult<ProcurementOrderCommandResult>.Ok(new() { PurchaseOrderId = f.Context.PurchaseOrderId }));
        await writing; Assert.IsTrue(f.Vm.HasPendingCommand);
        f.User.Clear(); Assert.IsFalse(f.Vm.RetryCommand.CanExecute(null));
        using var denied = new Fixture(); denied.User.Clear(); await denied.Vm.InitializeAsync();
        Assert.HasCount(0, denied.Searches);
    }

    internal static T Clone<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;
    /// <summary>成功后使用新的上下文和候选继续分配剩余量，新分配不得沿用旧命令身份。</summary>
    [TestMethod]
    public async Task SuccessfulPartialAllocationRefreshesAndCanAllocateAgain()
    {
        using var f = new Fixture(); await f.ReadyAsync();
        await f.Vm.SubmitCommand.ExecuteAsync(null);
        Assert.AreEqual(2, f.Vm.Context!.Version);
        Assert.AreEqual(7m, f.Vm.Lines[0].UnlinkedQty);
        Assert.AreEqual(3m, f.Vm.Lines[0].Links.Single().Quantity);
        Assert.HasCount(0, f.Vm.Plan); Assert.IsFalse(f.Vm.RefreshRequired);
        await f.SelectAsync(0); f.Vm.AllocationQuantity = 7; f.Vm.AddAllocationCommand.Execute(null);
        await f.Vm.PreviewCommand.ExecuteAsync(null); f.Vm.Reason = "分配剩余";
        await f.Vm.SubmitCommand.ExecuteAsync(null);
        var first = JsonSerializer.Deserialize<AssociateProcurementDemandsCommand>(f.Writes[0])!;
        var second = JsonSerializer.Deserialize<AssociateProcurementDemandsCommand>(f.Writes[1])!;
        Assert.AreNotEqual(first.CommandId, second.CommandId);
        Assert.AreNotEqual(first.Items[0].CommandItemId, second.Items[0].CommandItemId);
        Assert.AreEqual(0m, f.Vm.Lines[0].UnlinkedQty);
        Assert.AreEqual(3, f.Vm.Context.Version); Assert.HasCount(2, f.Vm.Lines[0].Links);
    }
    /// <summary>协议内存夹具，未安排的旧 API 调用直接失败，防止页面接错链路。</summary>
    internal sealed class Fixture : IDisposable
    {
        public readonly ProcurementDemandAssociationContextDto Context;
        public ProcurementDemandCandidateDto Demand;
        public readonly PurchaseDemandAssociationViewModel Vm;
        public readonly CurrentUserService User = new();
        public readonly DialogStub Dialogs = new();
        public readonly List<PurchaseOrderManagerSearchRequest> Searches = [];
        public readonly List<ProcurementDemandCandidateSearchRequest> CandidateSearches = [];
        public readonly List<PreviewProcurementDemandAssociationRequest> Previews = [];
        public readonly List<string> Writes = [];
        public bool FailContext;
        public Func<AssociateProcurementDemandsCommand, ServiceResult<ProcurementOrderCommandResult>>? Send;
        public Func<AssociateProcurementDemandsCommand, Task<ServiceResult<ProcurementOrderCommandResult>>>? SendAsync;
        public Func<PreviewProcurementDemandAssociationRequest, Task<ServiceResult<ProcurementDemandAssociationPreviewDto>>>? PreviewHandler;
        public Func<ProcurementDemandCandidateSearchRequest, Task<ServiceResult<PagedResponseBase<ProcurementDemandCandidateDto>>>>? CandidateHandler;
        public Fixture()
        {
            var product = Guid.NewGuid();
            Context = new() { PurchaseOrderId = Guid.NewGuid(), OrderNo = "PO-ASSOC", CustomerCode = "C1", SupplierName = "供应商",
                Version = 1, ConfirmationStatus = ConfirmationStatusEnum.已确认,
                Lines = Enumerable.Range(1, 2).Select(i => new ProcurementDemandAssociationLineDto { PurchaseOrderLineId = Guid.NewGuid(), Version = 1,
                    LineNumber = i, ProductId = product, Sku = "P1", Unit = "PCS", Quantity = 10, UnlinkedQty = 10 }).ToList() };
            Demand = new() { NetDemandId = Guid.NewGuid(), Version = 1, SourceDemandVersion = 3, ProductId = product,
                SourceUid = Guid.NewGuid(), SourceType = "SalesOrder", SourceNo = "SO-1", Sku = "P1", Unit = "PCS", NetQty = 20, LinkedQty = 5, RemainingQty = 15 };
            User.SetCurrentUser(new() { UserId = "association-test", Permissions = [PermissionKeys.ProcurementOrderRead, PermissionKeys.ProcurementOrderEdit] });
            var api = DispatchProxy.Create<IProcurementOrderApi, ApiProxy>();
            ((ApiProxy)(object)api).Call = (method, args) => method switch
            {
                nameof(IProcurementOrderApi.GetDemandAssociationContextAsync) => Task.FromResult(FailContext
                    ? ServiceResult<ProcurementDemandAssociationContextDto>.Fail("读取失败") : ServiceResult<ProcurementDemandAssociationContextDto>.Ok(Clone(Context))),
                nameof(IProcurementOrderApi.SearchDemandAssociationCandidatesAsync) => Candidates((ProcurementDemandCandidateSearchRequest)args[1]!),
                nameof(IProcurementOrderApi.PreviewDemandAssociationAsync) => Preview((PreviewProcurementDemandAssociationRequest)args[1]!),
                nameof(IProcurementOrderApi.AssociateDemandsAsync) => Write((AssociateProcurementDemandsCommand)args[1]!),
                _ => throw new NotSupportedException(method)
            };
            var manager = DispatchProxy.Create<IPurchaseOrderManagerApi, ApiProxy>();
            ((ApiProxy)(object)manager).Call = (method, args) =>
            {
                Assert.AreEqual(nameof(IPurchaseOrderManagerApi.SearchAsync), method);
                Searches.Add(Clone((PurchaseOrderManagerSearchRequest)args[0]!));
                return Task.FromResult(ServiceResult<PagedResponseBase<PurchaseOrderManagerListItemDto>>.Ok(new() { PageSize = 20, TotalCount = 1,
                    Items = [new() { PurchaseOrderId = Context.PurchaseOrderId, OrderNo = Context.OrderNo }] }));
            };
            Vm = new(api, manager, new ApiCallRunner(NullLogger<ApiCallRunner>.Instance), Dialogs, User, NullLogger<PurchaseDemandAssociationViewModel>.Instance);
        }
        private Task<ServiceResult<PagedResponseBase<ProcurementDemandCandidateDto>>> Candidates(ProcurementDemandCandidateSearchRequest request)
        {
            CandidateSearches.Add(Clone(request));
            return CandidateHandler?.Invoke(request) ?? Task.FromResult(ServiceResult<PagedResponseBase<ProcurementDemandCandidateDto>>.Ok(new() { Items = [Clone(Demand)], TotalCount = 1, PageSize = 30 }));
        }
        private Task<ServiceResult<ProcurementDemandAssociationPreviewDto>> Preview(PreviewProcurementDemandAssociationRequest request)
        {
            Previews.Add(Clone(request)); return PreviewHandler?.Invoke(request) ?? Task.FromResult(ServiceResult<ProcurementDemandAssociationPreviewDto>.Ok(MakePreview(request)));
        }
        public ProcurementDemandAssociationPreviewDto MakePreview(PreviewProcurementDemandAssociationRequest request) => new()
        {
            PurchaseOrderId = Context.PurchaseOrderId, Version = request.ExpectedVersion, Token = "preview-token",
            Items = request.Items.Select(x => new ProcurementDemandAssociationPreviewItemDto { CommandItemId = x.CommandItemId,
                PurchaseOrderLineId = x.PurchaseOrderLineId, NetDemandId = x.NetDemandId, Quantity = x.Quantity }).ToList()
        };
        private Task<ServiceResult<ProcurementOrderCommandResult>> Write(AssociateProcurementDemandsCommand command)
        {
            Writes.Add(JsonSerializer.Serialize(command));
            return SendAsync?.Invoke(command) ?? Task.FromResult(Send?.Invoke(command) ?? Success(command));
        }
        public ServiceResult<ProcurementOrderCommandResult> Success(AssociateProcurementDemandsCommand command)
        {
            // 写入替身推进权威版本及余额；重读返回新的快照，不能靠旧版上下文使客户端验收通过。
            Context.Version = command.ExpectedVersion + 1;
            Context.SourceType = Demand.SourceType; Context.SourceUid = Demand.SourceUid; Context.SourceNo = Demand.SourceNo;
            Demand.Version++; Demand.RemainingQty -= command.Items.Sum(x => x.Quantity); Demand.LinkedQty += command.Items.Sum(x => x.Quantity);
            foreach (var group in command.Items.GroupBy(x => x.PurchaseOrderLineId))
            {
                var line = Context.Lines.Single(x => x.PurchaseOrderLineId == group.Key);
                line.Version = group.First().ExpectedLineVersion + 1;
                line.UnlinkedQty -= group.Sum(x => x.Quantity); line.LinkedQty += group.Sum(x => x.Quantity);
                foreach (var item in group) line.Links.Add(new() { LinkId = Guid.NewGuid(), Version = 1, NetDemandId = item.NetDemandId,
                    SourceNo = Demand.SourceNo, DemandSku = Demand.Sku, Unit = Demand.Unit, Quantity = item.Quantity });
            }
            return ServiceResult<ProcurementOrderCommandResult>.Ok(new()
            {
                PurchaseOrderId = Context.PurchaseOrderId, Version = Context.Version, LifecycleStatus = PurchaseOrderLifecycleStatusEnum.Active,
                ConfirmationStatus = Context.ConfirmationStatus, Lines = command.Items.DistinctBy(x => x.PurchaseOrderLineId)
                    .Select(x => new ProcurementLineVersionDto { PurchaseOrderLineId = x.PurchaseOrderLineId, Version = x.ExpectedLineVersion + 1 }).ToList()
            });
        }
        public async Task OpenAsync()
        { await Vm.InitializeAsync(); Vm.OrderPicker.SelectedOrder = Vm.OrderPicker.Orders.Single(); await Vm.ChooseOrderCommand.ExecuteAsync(null); }
        public async Task SelectAsync(int index)
        { await Vm.SelectLineCommand.ExecuteAsync(Vm.Lines[index]); Vm.SelectedCandidate = Vm.Candidates.Single(); }
        public async Task ReadyAsync()
        { await OpenAsync(); await SelectAsync(0); Vm.AllocationQuantity = 3; Vm.AddAllocationCommand.Execute(null); await Vm.PreviewCommand.ExecuteAsync(null); Vm.Reason = "关联测试"; }
        public void Dispose() => Vm.Dispose();
    }
    /// <summary>为测试按 API 方法安排行为，禁止默认放行未覆盖用例。</summary>
    public class ApiProxy : DispatchProxy
    {
        /// <summary>接口行为由各用例明确提供。</summary>
        public Func<string, object?[], object?> Call { get; set; } = null!;
        /// <inheritdoc/>
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Call(targetMethod!.Name, args!);
    }
    /// <summary>控制真实提交确认的用户选择。</summary>
    internal sealed class DialogStub : IDialogService
    {
        public bool Confirm = true;
        public Task<bool> ConfirmAsync(string title, string message, string primaryButtonText = "确定", string closeButtonText = "取消", CancellationToken cancellationToken = default) => Task.FromResult(Confirm);
        public Task ShowContentAsync(string title, object content, string closeButtonText = "关闭", CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task ShowErrorAsync(string title, string message, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
