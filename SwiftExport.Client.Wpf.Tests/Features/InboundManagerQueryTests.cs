using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.Apis.Inventory;
using SwiftExport.Contracts.DTOs.Inventory.Inbounds;
using SwiftExport.Contracts.DTOs.Inventory.WareHouseSite.Picker;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Features.Inventory.Inbounds.Services;
using SwiftExportClient.Features.Inventory.Inbounds.Models;
using SwiftExportClient.Features.Inventory.Inbounds.ViewModels;
using SwiftExportClient.Features.Inventory.WarehouseSites.Services;
using SwiftExportClient.Resources.Controls;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.UI;

namespace SwiftExport.Client.Wpf.Tests.Features;

/// <summary>验证入库管理真实命令接线的分页、懒加载、失败保留和生命周期，不访问数据库。</summary>
[TestClass]
public sealed class InboundManagerQueryTests
{
    /// <summary>首次显示只搜索一次并沿用默认条件，再次激活不覆盖编辑。</summary>
    [TestMethod]
    public async Task Initialize_SearchesOnceWithCurrentDefaults()
    {
        var api = new ApiStub();
        using var vm = Create(api);
        await vm.InitializeAsync();
        Assert.AreEqual(1, api.Requests.Count);
        Assert.AreEqual(vm.SelectedDocumentType, api.Requests[0].DocumentType);
        Assert.AreEqual(vm.SelectedPostingStatus, api.Requests[0].PostingStatus);
        vm.Lines!.Items[0].Quantity = 12;
        await vm.InitializeAsync();
        Assert.AreEqual(1, api.Requests.Count);
        Assert.AreEqual(12m, vm.Lines.Items[0].Quantity);
    }

    /// <summary>每页仅加载选中单详情；未加载摘要不允许编辑，服务端合计不会被空明细清零。</summary>
    [TestMethod]
    public async Task Search_LoadsOnlySelectedDetail_AndTracksChildEdits()
    {
        var api = new ApiStub();
        using var vm = Create(api);
        await vm.SearchCommand.ExecuteAsync(null);
        Assert.AreEqual(2, vm.Headers.Items.Count);
        Assert.AreEqual(1, api.DetailCalls);
        Assert.IsTrue(vm.SelectedItem!.CanEdit);
        Assert.IsFalse(vm.SelectedItem.CanChangeDocumentType);
        Assert.IsFalse(vm.HasUnsavedChanges);
        Assert.AreEqual(15m, vm.Headers.Items[1].TotalQuantity);
        Assert.IsFalse(vm.Headers.Items[1].CanEdit);
        vm.Lines!.Items[0].Quantity = 3;
        Assert.IsTrue(vm.HasUnsavedChanges);
        Assert.IsTrue(vm.IsDirty);
        var first = vm.SelectedItem;
        vm.SelectedItem = vm.Headers.Items[1];
        await vm.LoadSelectedDetailCommand.ExecuteAsync(null);
        vm.SelectedItem = first;
        await vm.LoadSelectedDetailCommand.ExecuteAsync(null);
        Assert.AreEqual(2, api.DetailCalls);
        Assert.AreEqual(3m, vm.Lines!.Items[0].Quantity);
    }

    /// <summary>翻页使用已应用条件；更改筛选输入不触发请求，也不使当前详情失效。</summary>
    [TestMethod]
    public async Task Paging_UsesAppliedCriteria()
    {
        var api = new ApiStub();
        using var vm = Create(api);
        vm.Keyword = "first";
        await vm.SearchCommand.ExecuteAsync(null);
        vm.Keyword = "edited";
        vm.SelectedDocumentType = InboundDocumentTypeEnum.ProductionReceipt;
        vm.SelectedPostingStatus = InboundPostingStatusEnum.Posted;
        Assert.AreEqual(1, api.Requests.Count);
        Assert.IsTrue(vm.SelectedItem!.CanEdit);
        await vm.PageChangeCommand.ExecuteAsync(new PageChangeRequest { NewIndex = 2, NewSize = 50 });
        Assert.AreEqual("first", api.Requests[1].Keyword);
        Assert.AreEqual(InboundDocumentTypeEnum.PurchaseReceipt, api.Requests[1].DocumentType);
        Assert.AreEqual(InboundPostingStatusEnum.Unposted, api.Requests[1].PostingStatus);
        Assert.AreEqual(2, vm.PageIndex);
        Assert.AreEqual(100, vm.TotalCount);
        await vm.SearchCommand.ExecuteAsync(null);
        Assert.AreEqual(InboundPostingStatusEnum.Posted, api.Requests[2].PostingStatus);
    }

    /// <summary>拒绝放弃不发请求；即使同意放弃，查询失败也保留本地草稿。</summary>
    [TestMethod]
    public async Task DirtyDraft_IsPreservedOnRefusalAndQueryFailure()
    {
        var api = new ApiStub();
        var dialog = new DialogStub();
        using var vm = Create(api, dialog);
        vm.AddNewHeaderRowCommand.Execute(null);
        var draft = vm.SelectedItem;
        await vm.SearchCommand.ExecuteAsync(null);
        Assert.AreEqual(0, api.Requests.Count);
        dialog.AllowDiscard = true;
        api.FailSearch = true;
        await vm.SearchCommand.ExecuteAsync(null);
        Assert.AreSame(draft, vm.SelectedItem);
        Assert.IsTrue(vm.HasUnsavedChanges);
        Assert.AreEqual("明确的查询错误", vm.ResultSummary);
    }

    /// <summary>详情失败不把摘要当作完整草稿；刷新可以只重试当前详情。</summary>
    [TestMethod]
    public async Task DetailFailure_RemainsReadOnly_AndRefreshRetries()
    {
        var api = new ApiStub { FailDetail = true };
        using var vm = Create(api);
        await vm.SearchCommand.ExecuteAsync(null);
        Assert.IsFalse(vm.SelectedItem!.CanEdit);
        Assert.AreEqual(15m, vm.SelectedItem.TotalQuantity);
        api.FailDetail = false;
        await vm.RefreshCommand.ExecuteAsync(null);
        Assert.AreEqual(1, api.Requests.Count);
        Assert.AreEqual(2, api.DetailCalls);
        Assert.IsTrue(vm.SelectedItem.CanEdit);
    }

    /// <summary>底层忽略取消时，新请求仍胜出；释放后响应不能重填集合。</summary>
    [TestMethod]
    public async Task LateResponses_AreIgnoredAfterReplacementAndDispose()
    {
        var pending = new TaskCompletionSource<ServiceResult<PagedResponseBase<InboundManagerListItemDto>>>();
        var api = new ApiStub { Pending = pending };
        using var vm = Create(api);
        var old = vm.SearchCommand.ExecuteAsync(null);
        api.Pending = null;
        vm.Keyword = "latest";
        await vm.SearchCommand.ExecuteAsync(null);
        var current = vm.SelectedItem;
        pending.SetResult(ServiceResult<PagedResponseBase<InboundManagerListItemDto>>.Ok(new() { Items = [], TotalCount = 0 }));
        await old;
        Assert.AreSame(current, vm.SelectedItem);
        Assert.AreEqual(100, vm.TotalCount);
        var closing = new TaskCompletionSource<ServiceResult<PagedResponseBase<InboundManagerListItemDto>>>();
        api.Pending = closing;
        var work = vm.SearchCommand.ExecuteAsync(null);
        vm.Dispose();
        closing.SetResult(ServiceResult<PagedResponseBase<InboundManagerListItemDto>>.Ok(new() { Items = [] }));
        await work;
        Assert.AreSame(current, vm.SelectedItem);
    }

    private static InboundManagerViewModel Create(ApiStub api, DialogStub? dialogs = null)
        => new(new WarehouseStub(), new SourceStub(), dialogs ?? new DialogStub(), api, new RunnerStub());

    /// <summary>可控查询服务；所有写入口抛错，确保本批查询不会误调用保存或删除。</summary>
    private sealed class ApiStub : IInboundManagerApi
    {
        private readonly Guid[] _ids = [Guid.NewGuid(), Guid.NewGuid()];
        public bool FailSearch { get; set; }
        public bool FailDetail { get; set; }
        public int DetailCalls { get; private set; }
        public List<InboundManagerSearchRequest> Requests { get; } = [];
        public TaskCompletionSource<ServiceResult<PagedResponseBase<InboundManagerListItemDto>>>? Pending { get; set; }
        /// <inheritdoc/>
        public Task<ServiceResult<PagedResponseBase<InboundManagerListItemDto>>> SearchAsync(InboundManagerSearchRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Pending?.Task ?? Task.FromResult(FailSearch
                ? ServiceResult<PagedResponseBase<InboundManagerListItemDto>>.Fail("明确的查询错误")
                : ServiceResult<PagedResponseBase<InboundManagerListItemDto>>.Ok(new()
                {
                    Items = _ids.Select(id => new InboundManagerListItemDto { Uid = id, DocumentType = InboundDocumentTypeEnum.PurchaseReceipt, TotalQuantity = 15 }).ToList(),
                    TotalCount = 100, PageIndex = request.PageIndex, PageSize = request.PageSize
                }));
        }
        /// <inheritdoc/>
        public Task<ServiceResult<InboundManagerDetailDto>> GetAsync(Guid uid, CancellationToken cancellationToken = default)
        {
            DetailCalls++;
            return Task.FromResult(FailDetail ? ServiceResult<InboundManagerDetailDto>.Fail("详情错误") : ServiceResult<InboundManagerDetailDto>.Ok(new()
            {
                Header = new() { Uid = uid, Version = 3, WarehouseSiteId = Guid.NewGuid() },
                Lines = [new() { Uid = Guid.NewGuid(), InboundDocumentId = uid, Quantity = 2, SourceLineId = Guid.NewGuid() }]
            }));
        }
        /// <inheritdoc/>
        public Task<ServiceResult<InboundDraftCommandResult>> CreateAsync(CreateInboundDraftCommand command, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        /// <inheritdoc/>
        public Task<ServiceResult<InboundDraftCommandResult>> UpdateAsync(Guid uid, UpdateInboundDraftCommand command, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        /// <inheritdoc/>
        public Task<ServiceResult<InboundDraftCommandResult>> DeleteAsync(Guid uid, DeleteInboundDraftCommand command, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    /// <summary>测试直接执行请求，取消故意由响应身份门禁验证。</summary>
    private sealed class RunnerStub : IApiCallRunner
    {
        /// <inheritdoc/>
        public Task<ServiceResult<T>> RunAsync<T>(Func<Task<ServiceResult<T>>> action, string failureMessage = "请求失败") => action();
        /// <inheritdoc/>
        public Task<ServiceResult<T>> RunAsync<T>(Func<Task<ServiceResult<T>>> action, CancellationToken cancellationToken, string failureMessage = "请求失败") => action();
        /// <inheritdoc/>
        public Task<ServiceResult<T>> RunValueAsync<T>(Func<Task<T>> action, string successMessage = "成功", string failureMessage = "请求失败") => throw new NotSupportedException();
    }
    /// <summary>查询测试不打开仓库弹窗。</summary>
    private sealed class WarehouseStub : IWarehousePickerService
    {
        /// <inheritdoc/>
        public Task<WarehouseSitePickerDto?> PickAsync(CancellationToken cancellationToken = default) => Task.FromResult<WarehouseSitePickerDto?>(null);
    }
    /// <summary>查询测试不打开来源弹窗。</summary>
    private sealed class SourceStub : IInboundSourceDialogService
    {
        /// <inheritdoc/>
        public Task<IReadOnlyList<InboundSourceCandidateDto>?> PickAsync(InboundSourcePickerRequest request, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<InboundSourceCandidateDto>?>(null);
    }
    /// <summary>可控制是否放弃编辑。</summary>
    private sealed class DialogStub : IDialogService
    {
        public bool AllowDiscard { get; set; }
        /// <inheritdoc/>
        public Task<bool> ConfirmAsync(string title, string message, string primaryButtonText = "确定", string closeButtonText = "取消", CancellationToken cancellationToken = default) => Task.FromResult(AllowDiscard);
        /// <inheritdoc/>
        public Task ShowContentAsync(string title, object content, string closeButtonText = "关闭", CancellationToken cancellationToken = default) => Task.CompletedTask;
        /// <inheritdoc/>
        public Task ShowErrorAsync(string title, string message, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
