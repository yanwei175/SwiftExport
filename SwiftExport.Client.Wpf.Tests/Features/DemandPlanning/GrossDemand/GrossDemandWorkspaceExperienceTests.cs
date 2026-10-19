using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.DemandPlanning.GrossDemand;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Authentication;
using SwiftExportClient.Features.DemandPlanning.GrossDemand.Models;
using SwiftExportClient.Features.DemandPlanning.GrossDemand.ViewModels;

namespace SwiftExport.Client.Wpf.Tests.Features.DemandPlanning.GrossDemand;
public sealed partial class GrossDemandWorkspaceViewModelTests
{
    /// <summary>翻页刷新只用已应用条件，输入和失败页不能改写当前结果与页码。</summary>
    [TestMethod]
    public async Task AppliedQuery_DraftAndFailedPagePreserveAcceptedFacts()
    {
        var api = new FakeGrossDemandApi { SearchResponse = CreatePage(CreateItem("SO-OLD", false)) };
        using var vm = CreateViewModel(api); vm.Keyword = "old"; vm.SourceNosText = "SO-OLD"; await vm.InitializeAsync(); var row = vm.Items[0];
        vm.Keyword = "new"; vm.SourceNosText = "SO-NEW"; Assert.IsTrue(vm.HasUnappliedChanges); Assert.AreSame(row, vm.Items[0]);
        await vm.PageChangeCommand.ExecuteAsync(new() { NewIndex = 2, NewSize = 20, OldIndex = 1, OldSize = 20 });
        Assert.AreEqual("old", api.LastSearchRequest!.Keyword); CollectionAssert.AreEqual(new[] { "SO-OLD" }, api.LastSearchRequest.SourceNos); Assert.AreEqual(2, vm.PageIndex);
        api.SearchHandler = (_, _) => Task.FromResult(ServiceResult<PagedResponseBase<GrossDemandListItemDto>>.Fail("读取失败")); row = vm.Items[0];
        await vm.PageChangeCommand.ExecuteAsync(new() { NewIndex = 3, NewSize = 20, OldIndex = 2, OldSize = 20 }); Assert.AreEqual(2, vm.PageIndex); Assert.AreSame(row, vm.Items[0]);
        vm.RequiredDateFrom = new(2027, 1, 1); vm.RequiredDateTo = new(2026, 1, 1); var count = api.SearchCallCount; await vm.SearchCommand.ExecuteAsync(null); Assert.AreEqual(count, api.SearchCallCount); Assert.AreEqual(2, vm.PageIndex);
        await vm.RefreshCommand.ExecuteAsync(null); Assert.AreEqual("old", api.LastSearchRequest.Keyword); Assert.IsNull(api.LastSearchRequest.RequiredDateFrom);
    }

    /// <summary>来源成功后才接纳全量模式，失败保留原模式，返回恢复已应用普通查询。</summary>
    [TestMethod]
    public async Task SourceMode_UsesSuccessfulSnapshotAndLoadsAll301Rows()
    {
        var api = new FakeGrossDemandApi { SearchResponse = CreatePage(CreateItem("SO-1", false)) }; using var vm = CreateViewModel(api); vm.Keyword = "applied"; await vm.InitializeAsync();
        var group = GrossDemandSourceGroupModel.FromDto(new() { SourceUid = Guid.NewGuid(), SourceNo = "SO-SOURCE", SourceType = "SO", DemandCount = 301 });
        api.SearchHandler = (_, _) => Task.FromResult(ServiceResult<PagedResponseBase<GrossDemandListItemDto>>.Fail("来源失败")); await vm.SelectSourceGroupCommand.ExecuteAsync(group);
        Assert.IsFalse(vm.IsSourceSelected); Assert.AreEqual("SO-1", vm.Items[0].SourceNo);
        api.SearchHandler = (_, _) => Task.FromResult(ServiceResult<PagedResponseBase<GrossDemandListItemDto>>.Ok(CreatePage(Enumerable.Range(0, 301).Select(i => CreateItem("P-" + i, false)).ToArray())));
        await vm.SelectSourceGroupCommand.ExecuteAsync(group); Assert.IsTrue(vm.IsSourceSelected); Assert.IsFalse(vm.CanEditMainFilters); Assert.AreEqual(301, vm.Items.Count); Assert.AreEqual(group.SourceUid, api.LastSearchRequest!.SourceUid); Assert.AreEqual("", api.LastSearchRequest.Keyword);
        vm.Keyword = "unsent"; await vm.RefreshCommand.ExecuteAsync(null); Assert.AreEqual(group.SourceUid, api.LastSearchRequest.SourceUid);
        await vm.ReturnToAllCommand.ExecuteAsync(null); Assert.IsFalse(vm.IsSourceSelected); Assert.AreEqual("applied", api.LastSearchRequest.Keyword); Assert.IsTrue(vm.HasUnappliedChanges);
    }

    /// <summary>来源状态刷新和清空都必须尊重独立需求的未保存保护。</summary>
    [TestMethod]
    public async Task SourceFilters_DirtyEditorRejectsRefreshAndClear()
    {
        var api = new FakeGrossDemandApi(); var dialog = new RecordingDialogService(false); using var vm = CreateViewModel(api, dialog);
        await vm.InitializeAsync(); await vm.ToggleSourcePanelCommand.ExecuteAsync(null); await vm.CreateManualCommand.ExecuteAsync(null); vm.ManualEditor.Edit.Remark = "未保存";
        var groups = api.SourceGroupCallCount; var searches = api.SearchCallCount;
        await vm.RefreshSourceGroupsCommand.ExecuteAsync(null); await vm.ClearSourceFiltersCommand.ExecuteAsync(null);
        Assert.AreEqual(groups, api.SourceGroupCallCount); Assert.AreEqual(searches, api.SearchCallCount); Assert.IsTrue(vm.ManualEditor.HasUnsavedChanges); Assert.IsTrue(dialog.ConfirmCount >= 2);
    }

    /// <summary>候选失败可独立重试，精确客户身份在查询后冻结。</summary>
    [TestMethod]
    public async Task Customers_RetryAndFreezeExactIds()
    {
        var api = new FakeGrossDemandApi(); var id = Guid.NewGuid(); api.CustomerHandler = _ => Task.FromResult(ServiceResult<List<GrossDemandCustomerOptionDto>>.Fail("候选失败"));
        using var vm = CreateViewModel(api); await vm.InitializeAsync(); Assert.AreEqual("候选失败", vm.CustomerLoadError);
        api.Customers.Add(new() { Id = id, Code = "C1", Name = "客户一" }); api.CustomerHandler = null; await vm.RefreshCustomerOptionsCommand.ExecuteAsync(null); Assert.IsNull(vm.CustomerLoadError);
        vm.SelectedCustomers.Add(vm.CustomerOptions[0]); await vm.SearchCommand.ExecuteAsync(null); var sent = api.LastSearchRequest!;
        vm.SelectedCustomers.Clear(); await vm.RefreshCommand.ExecuteAsync(null); CollectionAssert.AreEqual(new[] { id }, api.LastSearchRequest!.CustomerIds); CollectionAssert.AreEqual(new[] { id }, sent.CustomerIds);
    }

    /// <summary>真实原因必填；未知关闭结果继续使用原命令、版本及原因。</summary>
    [TestMethod]
    public async Task CloseReason_RequiredAndUnknownRetryKeepsOriginalCommand()
    {
        var item = CreateItem("MD-1", false); item.SourceType = "MD"; var api = new FakeGrossDemandApi { SearchResponse = CreatePage(item) }; var dialog = new RecordingDialogService(true) { Reason = " " };
        using var vm = CreateViewModel(api, dialog); await vm.InitializeAsync(); await vm.CloseSelectedManualCommand.ExecuteAsync(null); Assert.AreEqual(0, api.CloseRequests.Count);
        dialog.Reason = "客户取消本次需求"; api.CloseResults.Enqueue(ServiceResult<GrossDemandDetailDto>.Fail(ServiceErrorCodes.OutcomeUnknown, "未知")); api.CloseResults.Enqueue(ServiceResult<GrossDemandDetailDto>.Ok(new() { Uid = item.Uid }));
        await vm.CloseSelectedManualCommand.ExecuteAsync(null); Assert.IsTrue(vm.HasPendingClose); dialog.Reason = "不能替换原原因"; await vm.CloseSelectedManualCommand.ExecuteAsync(null);
        Assert.AreSame(api.CloseRequests[0], api.CloseRequests[1]); Assert.AreEqual("客户取消本次需求", api.CloseRequests[0].Reason); Assert.AreEqual(item.Version, api.CloseRequests[0].Version); Assert.AreEqual(2, dialog.ReasonCount);
    }
}
