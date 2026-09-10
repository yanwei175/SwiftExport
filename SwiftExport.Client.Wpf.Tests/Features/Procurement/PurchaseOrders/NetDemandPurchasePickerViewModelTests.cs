using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;
using SwiftExport.Contracts.Apis.DemandPlanning;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Features.Procurement.PurchaseOrders.ViewModels;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.UI;

namespace SwiftExport.Client.Wpf.Tests.Features.Procurement.PurchaseOrders;

[TestClass]
public sealed class NetDemandPurchasePickerViewModelTests
{
    [TestMethod]
    public void Open_ShouldOnlyLoadGroups_AndKeepDetailEmpty()
    {
        StaTest.RunAsync(async () =>
        {
            var api = new CandidateApiStub();
            api.Groups.Add(CreateGroup("SO-1", api.FirstSourceUid, 205));
            using var viewModel = CreateViewModel(api);

            await viewModel.OpenAsync();

            Assert.AreEqual(1, api.GroupCallCount);
            Assert.AreEqual(0, api.DetailRequests.Count);
            Assert.AreEqual(2, viewModel.SourceGroups.Count);
            Assert.IsTrue(viewModel.SourceGroups[0].IsAllSources);
            Assert.IsNull(viewModel.SelectedSourceGroup);
            Assert.AreEqual(0, viewModel.Items.Count);
            StringAssert.Contains(viewModel.StatusText, "请选择左侧来源");
        });
    }

    [TestMethod]
    public void SourceAndAllSelection_ShouldLoadEveryCandidateWithoutPaging()
    {
        StaTest.RunAsync(async () =>
        {
            var api = new CandidateApiStub();
            api.Groups.Add(CreateGroup("SO-1", api.FirstSourceUid, 205));
            api.Candidates.AddRange(Enumerable.Range(1, 205)
                .Select(index => CreateCandidate(api.FirstSourceUid, "SO-1", index)));
            using var viewModel = CreateViewModel(api);
            await viewModel.OpenAsync();

            await viewModel.SelectSourceGroupCommand.ExecuteAsync(viewModel.SourceGroups[1]);
            Assert.AreEqual(205, viewModel.Items.Count);
            Assert.AreEqual("SO", api.DetailRequests[0].SourceType);
            Assert.AreEqual(api.FirstSourceUid, api.DetailRequests[0].SourceUid);

            await viewModel.SelectSourceGroupCommand.ExecuteAsync(viewModel.SourceGroups[0]);
            Assert.AreEqual(205, viewModel.Items.Count);
            Assert.IsNull(api.DetailRequests[1].SourceUid);
        });
    }

    [TestMethod]
    public void LocalFilterAndToggleAll_ShouldNotQuery_AndShouldPreserveHiddenSelection()
    {
        StaTest.RunAsync(async () =>
        {
            var api = new CandidateApiStub();
            api.Groups.Add(CreateGroup("SO-1", api.FirstSourceUid, 2));
            api.Candidates.Add(CreateCandidate(api.FirstSourceUid, "SO-1", 1, "MODEL-A", "供应商甲"));
            api.Candidates.Add(CreateCandidate(api.FirstSourceUid, "SO-1", 2, "MODEL-B", "供应商乙"));
            using var viewModel = CreateViewModel(api);
            await viewModel.OpenAsync();
            await viewModel.SelectSourceGroupCommand.ExecuteAsync(viewModel.SourceGroups[0]);
            var callsAfterLoad = api.DetailRequests.Count;

            viewModel.Items[0].IsChecked = true;
            viewModel.Keyword = "MODEL-B";
            viewModel.SourceNo = "SO-1";

            Assert.AreEqual(callsAfterLoad, api.DetailRequests.Count);
            Assert.AreEqual(1, viewModel.FilteredCount);
            Assert.AreEqual(1, viewModel.SelectedCount);
            viewModel.ToggleAllVisibleCommand.Execute(null);
            Assert.AreEqual(2, viewModel.SelectedCount);
            Assert.IsTrue(viewModel.Items.All(static item => item.IsChecked));
        });
    }

    [TestMethod]
    public void RapidSwitchAndClose_ShouldRejectStaleResults()
    {
        StaTest.RunAsync(async () =>
        {
            var api = new CandidateApiStub();
            var secondSourceUid = Guid.NewGuid();
            api.Groups.Add(CreateGroup("SO-A", api.FirstSourceUid, 1));
            api.Groups.Add(CreateGroup("SO-B", secondSourceUid, 1));
            var firstCompletion = new TaskCompletionSource<ServiceResult<List<MaterialDemandWorkspaceItemDto>>>(TaskCreationOptions.RunContinuationsAsynchronously);
            api.DetailHandler = (request, _) => request.SourceUid == api.FirstSourceUid
                ? firstCompletion.Task
                : Task.FromResult(ServiceResult<List<MaterialDemandWorkspaceItemDto>>.Ok(
                    [CreateCandidate(secondSourceUid, "SO-B", 2)]));
            using var viewModel = CreateViewModel(api);
            await viewModel.OpenAsync();

            var firstLoad = viewModel.SelectSourceGroupCommand.ExecuteAsync(viewModel.SourceGroups[1]);
            await Task.Yield();
            await viewModel.SelectSourceGroupCommand.ExecuteAsync(viewModel.SourceGroups[2]);
            firstCompletion.SetResult(ServiceResult<List<MaterialDemandWorkspaceItemDto>>.Ok(
                [CreateCandidate(api.FirstSourceUid, "SO-A", 1)]));
            await firstLoad;

            Assert.AreEqual("SO-B", viewModel.Items.Single().SourceNo);
            var lateCompletion = new TaskCompletionSource<ServiceResult<List<MaterialDemandWorkspaceItemDto>>>(TaskCreationOptions.RunContinuationsAsynchronously);
            api.DetailHandler = (_, _) => lateCompletion.Task;
            var closingLoad = viewModel.SelectSourceGroupCommand.ExecuteAsync(viewModel.SourceGroups[1]);
            await Task.Yield();
            viewModel.Close();
            lateCompletion.SetResult(ServiceResult<List<MaterialDemandWorkspaceItemDto>>.Ok(
                [CreateCandidate(api.FirstSourceUid, "SO-A", 3)]));
            await closingLoad;
            Assert.AreEqual(0, viewModel.Items.Count);
        });
    }

    [TestMethod]
    public void SwitchWithSelection_ShouldAllowCancelWithoutLosingRows()
    {
        StaTest.RunAsync(async () =>
        {
            var api = new CandidateApiStub();
            var secondSourceUid = Guid.NewGuid();
            api.Groups.Add(CreateGroup("SO-A", api.FirstSourceUid, 1));
            api.Groups.Add(CreateGroup("SO-B", secondSourceUid, 1));
            api.Candidates.Add(CreateCandidate(api.FirstSourceUid, "SO-A", 1));
            var dialogs = new DialogServiceStub { ConfirmResult = false };
            using var viewModel = new NetDemandPurchasePickerViewModel(api, new DirectApiCallRunner(), dialogs);
            await viewModel.OpenAsync();
            await viewModel.SelectSourceGroupCommand.ExecuteAsync(viewModel.SourceGroups[1]);
            viewModel.Items.Single().IsChecked = true;
            var callsBeforeSwitch = api.DetailRequests.Count;

            await viewModel.SelectSourceGroupCommand.ExecuteAsync(viewModel.SourceGroups[2]);

            Assert.AreEqual(callsBeforeSwitch, api.DetailRequests.Count);
            Assert.AreEqual("SO-A", viewModel.Items.Single().SourceNo);
            Assert.AreEqual(1, viewModel.SelectedCount);
            Assert.AreSame(viewModel.SourceGroups[1], viewModel.SelectedSourceGroup);
        });
    }

    private static NetDemandPurchasePickerViewModel CreateViewModel(CandidateApiStub api) =>
        new(api, new DirectApiCallRunner(), new DialogServiceStub());

    private static PurchaseCandidateSourceGroupDto CreateGroup(string sourceNo, Guid sourceUid, int count) => new()
    {
        SourceType = "SO",
        SourceUid = sourceUid,
        SourceNo = sourceNo,
        CandidateCount = count,
    };

    private static MaterialDemandWorkspaceItemDto CreateCandidate(
        Guid sourceUid,
        string sourceNo,
        int line,
        string? linkSku = null,
        string? supplierName = null) => new()
    {
        Uid = Guid.NewGuid(),
        Version = 1,
        GrossDemandId = Guid.NewGuid(),
        LineNumber = line,
        SourceType = "SO",
        SourceUid = sourceUid,
        SourceNo = sourceNo,
        SourceLineId = Guid.NewGuid(),
        SourceLineNumber = line,
        RootNetDemandId = Guid.NewGuid(),
        Sku = $"SKU-{line}",
        LinkSku = linkSku ?? $"MODEL-{line}",
        Description = $"物料 {line}",
        SupplyType = SupplyTypeEnums.采购,
        SuggestedPurchaseSupplierName = supplierName ?? "测试供应商",
        NetQty = 10,
        AssociatedPurchaseQty = 2,
    };

    private sealed class CandidateApiStub : IMaterialDemandWorkspaceApi
    {
        public Guid FirstSourceUid { get; } = Guid.NewGuid();
        public List<PurchaseCandidateSourceGroupDto> Groups { get; } = [];
        public List<MaterialDemandWorkspaceItemDto> Candidates { get; } = [];
        public List<PurchaseCandidateQuery> DetailRequests { get; } = [];
        public int GroupCallCount { get; private set; }
        public Func<PurchaseCandidateQuery, CancellationToken, Task<ServiceResult<List<MaterialDemandWorkspaceItemDto>>>>? DetailHandler { get; set; }

        public Task<ServiceResult<List<PurchaseCandidateSourceGroupDto>>> GetPurchaseCandidateSourceGroupsAsync(CancellationToken cancellationToken = default)
        {
            GroupCallCount++;
            return Task.FromResult(ServiceResult<List<PurchaseCandidateSourceGroupDto>>.Ok(Groups.ToList()));
        }

        public Task<ServiceResult<List<MaterialDemandWorkspaceItemDto>>> GetPurchaseCandidatesAsync(PurchaseCandidateQuery request, CancellationToken cancellationToken = default)
        {
            DetailRequests.Add(request);
            if (DetailHandler is not null) return DetailHandler(request, cancellationToken);
            var rows = request.SourceUid.HasValue
                ? Candidates.Where(item => item.SourceUid == request.SourceUid
                    && string.Equals(item.SourceType, request.SourceType, StringComparison.Ordinal)).ToList()
                : Candidates.ToList();
            return Task.FromResult(ServiceResult<List<MaterialDemandWorkspaceItemDto>>.Ok(rows));
        }

        public Task<ServiceResult<PagedResponseBase<MaterialDemandWorkspaceItemDto>>> SearchAsync(MaterialDemandWorkspaceSearchRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ServiceResult<List<NetDemandSourceGroupDto>>> SearchSourceGroupsAsync(MaterialDemandSourceGroupSearchRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ServiceResult<UpdateSuggestedPurchaseSupplierResult>> UpdateSuggestedPurchaseSupplierAsync(UpdateSuggestedPurchaseSupplierCommand command, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ServiceResult<RollbackMaterialDemandsResult>> RollbackSourceEstablishmentAsync(RollbackMaterialDemandsCommand command, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class DirectApiCallRunner : IApiCallRunner
    {
        public Task<ServiceResult<T>> RunAsync<T>(Func<Task<ServiceResult<T>>> action, string failureMessage = "请求失败") => action();
        public Task<ServiceResult<T>> RunAsync<T>(Func<Task<ServiceResult<T>>> action, CancellationToken cancellationToken, string failureMessage = "请求失败") => action();
        public async Task<ServiceResult<T>> RunValueAsync<T>(Func<Task<T>> action, string successMessage = "成功", string failureMessage = "请求失败") => ServiceResult<T>.Ok(await action(), successMessage);
    }

    private sealed class DialogServiceStub : IDialogService
    {
        public bool ConfirmResult { get; set; } = true;
        public Task ShowContentAsync(string title, object content, string closeButtonText = "关闭", CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> ConfirmAsync(string title, string message, string primaryButtonText = "确定", string closeButtonText = "取消", CancellationToken cancellationToken = default) => Task.FromResult(ConfirmResult);
        public Task ShowErrorAsync(string title, string message, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
