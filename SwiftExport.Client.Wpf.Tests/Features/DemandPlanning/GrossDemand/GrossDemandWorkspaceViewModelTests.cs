using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.Apis.DemandPlanning;
using SwiftExport.Contracts.Apis.ProductApis;
using SwiftExport.Contracts.DTOs.DemandPlanning.GrossDemand;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Features.DemandPlanning.GrossDemand.ViewModels;
using SwiftExportClient.Authentication;
using SwiftExportClient.Navigation;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.UI;

namespace SwiftExport.Client.Wpf.Tests.Features.DemandPlanning.GrossDemand;

[TestClass]
public sealed partial class GrossDemandWorkspaceViewModelTests
{
    /// <summary>创建结果未知及后续限流均保持完整原请求；成功后才允许编辑及关闭。</summary>
    [TestMethod]
    public async Task ManualCreate_UnknownThenRateLimited_RetriesOriginalRequest()
    {
        var api = new FakeGrossDemandApi();
        var saved = new GrossDemandDetailDto { Uid = Guid.NewGuid(), Version = 1, SourceNo = "MD-1", IsNonStandard = true, DemandQty = 2m, Description = "非标" };
        api.CreateResults.Enqueue(ServiceResult<GrossDemandDetailDto>.Fail(ServiceErrorCodes.OutcomeUnknown, "结果未知"));
        api.CreateResults.Enqueue(ServiceResult<GrossDemandDetailDto>.Fail(ServiceErrorCodes.RateLimited, "稍后重试"));
        api.CreateResults.Enqueue(ServiceResult<GrossDemandDetailDto>.Ok(saved));
        api.DetailResults.Enqueue(ServiceResult<GrossDemandDetailDto>.Ok(saved));
        using var vm = CreateViewModel(api);
        vm.ManualEditor.OpenCreate();
        vm.ManualEditor.Edit.IsNonStandard = true;
        vm.ManualEditor.Edit.Description = "非标";
        vm.ManualEditor.Edit.DemandQty = 2m;
        await vm.ManualEditor.SaveCommand.ExecuteAsync(null);
        Assert.IsTrue(vm.ManualEditor.HasPendingSave);
        Assert.IsFalse(vm.ManualEditor.CanEditFields);
        Assert.IsFalse(await vm.CanCloseAsync(CancellationToken.None));
        await vm.ManualEditor.SaveCommand.ExecuteAsync(null);
        Assert.IsTrue(vm.ManualEditor.HasPendingSave);
        await vm.ManualEditor.SaveCommand.ExecuteAsync(null);
        Assert.IsFalse(vm.ManualEditor.HasPendingSave);
        Assert.AreEqual(saved.Uid, vm.ManualEditor.Edit.Uid);
        Assert.AreEqual(3, api.CreateRequests.Count);
        Assert.AreNotEqual(Guid.Empty, api.CreateRequests[0].CommandId);
        Assert.IsTrue(api.CreateRequests.All(x => ReferenceEquals(x, api.CreateRequests[0])));
        Assert.AreEqual(2m, api.CreateRequests[0].DemandQty);
    }

    /// <summary>更新未知期间冻结输入，限流后仍按原来源版本和内容重试。</summary>
    [TestMethod]
    public async Task ManualUpdate_UnknownThenRateLimited_PreservesVersionAndContent()
    {
        var api = new FakeGrossDemandApi();
        var uid = Guid.NewGuid();
        api.UpdateResults.Enqueue(ServiceResult<GrossDemandDetailDto>.Fail(ServiceErrorCodes.OutcomeUnknown, "结果未知"));
        api.UpdateResults.Enqueue(ServiceResult<GrossDemandDetailDto>.Fail(ServiceErrorCodes.RateLimited, "稍后重试"));
        var saved = new GrossDemandDetailDto { Uid = uid, Version = 5, SourceNo = "MD-2", IsNonStandard = true, Description = "新描述", DemandQty = 3m };
        api.UpdateResults.Enqueue(ServiceResult<GrossDemandDetailDto>.Ok(saved));
        api.DetailResults.Enqueue(ServiceResult<GrossDemandDetailDto>.Ok(saved));
        using var vm = CreateViewModel(api);
        vm.ManualEditor.OpenEdit(new GrossDemandDetailDto { Uid = uid, Version = 4, SourceNo = "MD-2", IsNonStandard = true, Description = "旧描述", DemandQty = 3m });
        vm.ManualEditor.Edit.Description = "新描述";
        await vm.ManualEditor.SaveCommand.ExecuteAsync(null);
        Assert.IsFalse(vm.ManualEditor.CanEditFields);
        await vm.ManualEditor.SaveCommand.ExecuteAsync(null);
        await vm.ManualEditor.SaveCommand.ExecuteAsync(null);
        Assert.AreEqual(3, api.UpdateRequests.Count);
        Assert.IsTrue(api.UpdateRequests.All(x => ReferenceEquals(x, api.UpdateRequests[0])));
        Assert.AreEqual(uid, api.UpdateRequests[0].Uid);
        Assert.AreEqual(4, api.UpdateRequests[0].Version);
        Assert.AreEqual("新描述", api.UpdateRequests[0].Description);
    }

    /// <summary>关闭未知后切换选中行也不能改变原关闭目标、版本及原因。</summary>
    [TestMethod]
    public async Task ManualClose_UnknownThenRateLimited_RetriesOriginalTarget()
    {
        var row = CreateItem("MD-3", canDecompose: false);
        row.SourceType = "MD";
        var api = new FakeGrossDemandApi { SearchResponse = CreatePage(row) };
        api.CloseResults.Enqueue(ServiceResult<GrossDemandDetailDto>.Fail(ServiceErrorCodes.OutcomeUnknown, "结果未知"));
        api.CloseResults.Enqueue(ServiceResult<GrossDemandDetailDto>.Fail(ServiceErrorCodes.RateLimited, "稍后重试"));
        api.CloseResults.Enqueue(ServiceResult<GrossDemandDetailDto>.Ok(new GrossDemandDetailDto { Uid = row.Uid, Version = 2, SourceNo = row.SourceNo }));
        using var vm = CreateViewModel(api);
        await vm.InitializeAsync();
        vm.SelectedItem = vm.Items.Single();
        await vm.CloseSelectedManualCommand.ExecuteAsync(null);
        Assert.IsTrue(vm.HasPendingClose);
        Assert.IsFalse(await vm.CanCloseAsync(CancellationToken.None));
        await vm.CloseSelectedManualCommand.ExecuteAsync(null);
        Assert.IsTrue(vm.HasPendingClose);
        await vm.CloseSelectedManualCommand.ExecuteAsync(null);
        Assert.AreEqual(3, api.CloseRequests.Count);
        Assert.IsTrue(api.CloseRequests.All(x => ReferenceEquals(x, api.CloseRequests[0])));
        Assert.AreEqual(row.Uid, api.CloseRequests[0].Uid);
        Assert.AreEqual(row.Version, api.CloseRequests[0].Version);
    }

    /// <summary>首次明确限流未留下未知结果时可重新编辑并以新身份提交。</summary>
    [TestMethod]
    public async Task ManualCreate_FirstRateLimited_DoesNotLockEditor()
    {
        var api = new FakeGrossDemandApi();
        api.CreateResults.Enqueue(ServiceResult<GrossDemandDetailDto>.Fail(ServiceErrorCodes.RateLimited, "稍后重试"));
        var saved = new GrossDemandDetailDto { Uid = Guid.NewGuid(), Version = 1, SourceNo = "MD-4", IsNonStandard = true, Description = "非标", DemandQty = 1m };
        api.CreateResults.Enqueue(ServiceResult<GrossDemandDetailDto>.Ok(saved));
        api.DetailResults.Enqueue(ServiceResult<GrossDemandDetailDto>.Ok(saved));
        using var vm = CreateViewModel(api);
        vm.ManualEditor.OpenCreate();
        vm.ManualEditor.Edit.IsNonStandard = true;
        vm.ManualEditor.Edit.Description = "非标";
        await vm.ManualEditor.SaveCommand.ExecuteAsync(null);
        Assert.IsFalse(vm.ManualEditor.HasPendingSave);
        Assert.IsTrue(vm.ManualEditor.CanEditFields);
        await vm.ManualEditor.SaveCommand.ExecuteAsync(null);
        Assert.AreNotEqual(api.CreateRequests[0].CommandId, api.CreateRequests[1].CommandId);
    }

    /// <summary>手工保存请求发出后即冻结关闭和编辑器切换，未知结果仍保留原重试入口。</summary>
    [TestMethod]
    public async Task ManualCreate_InFlightCannotCloseOrSwitchEditor()
    {
        var api = new FakeGrossDemandApi();
        var completion = new TaskCompletionSource<ServiceResult<GrossDemandDetailDto>>(TaskCreationOptions.RunContinuationsAsynchronously);
        api.CreateHandler = _ => completion.Task;
        using var vm = CreateViewModel(api);
        vm.ManualEditor.OpenCreate();
        vm.ManualEditor.Edit.IsNonStandard = true;
        vm.ManualEditor.Edit.Description = "请求原文";
        var save = vm.ManualEditor.SaveCommand.ExecuteAsync(null);

        Assert.IsTrue(vm.ManualEditor.IsBusy);
        Assert.IsTrue(vm.ManualEditor.IsEditorLocked);
        Assert.IsFalse(await vm.CanCloseAsync(CancellationToken.None));
        Assert.IsFalse(vm.CreateManualCommand.CanExecute(null));
        Assert.IsFalse(vm.ManualEditor.CanEditFields);
        vm.ManualEditor.Close(discardChanges: true);
        vm.ManualEditor.OpenCreate();
        Assert.IsTrue(vm.ManualEditor.IsOpen);
        Assert.AreEqual("请求原文", vm.ManualEditor.Edit.Description);

        completion.SetResult(ServiceResult<GrossDemandDetailDto>.Fail(ServiceErrorCodes.OutcomeUnknown, "结果未知"));
        await save;
        Assert.IsTrue(vm.ManualEditor.HasPendingSave);
        Assert.IsTrue(vm.ManualEditor.SaveCommand.CanExecute(null));
    }

    /// <summary>命令成功后只载入当前详情；历史回执中的版本和内容不得进入可编辑表单。</summary>
    [TestMethod]
    public async Task ManualUpdate_ReplaySuccessLoadsCurrentDetail()
    {
        var api = new FakeGrossDemandApi();
        var uid = Guid.NewGuid();
        api.UpdateResults.Enqueue(ServiceResult<GrossDemandDetailDto>.Ok(new GrossDemandDetailDto
        { Uid = uid, Version = 5, SourceNo = "MD-5", Description = "历史回执", IsNonStandard = true, DemandQty = 1m }));
        api.DetailResults.Enqueue(ServiceResult<GrossDemandDetailDto>.Ok(new GrossDemandDetailDto
        { Uid = uid, Version = 7, SourceNo = "MD-5", Description = "当前详情", IsNonStandard = true, DemandQty = 2m }));
        using var vm = CreateViewModel(api);
        vm.ManualEditor.OpenEdit(new GrossDemandDetailDto { Uid = uid, Version = 4, SourceNo = "MD-5", Description = "原内容", IsNonStandard = true, DemandQty = 1m });
        vm.ManualEditor.Edit.Description = "提交内容";

        await vm.ManualEditor.SaveCommand.ExecuteAsync(null);

        Assert.AreEqual(1, api.DetailRequests.Count);
        Assert.AreEqual(uid, api.DetailRequests[0]);
        Assert.AreEqual(7, vm.ManualEditor.Edit.Version);
        Assert.AreEqual("当前详情", vm.ManualEditor.Edit.Description);
        Assert.IsTrue(vm.ManualEditor.CanEditFields);
    }

    /// <summary>确认成功但详情读取失败时冻结旧表单，保存入口只重试读取而不重复提交命令。</summary>
    [TestMethod]
    public async Task ManualCreate_DetailRefreshFailureRetriesReadOnly()
    {
        var api = new FakeGrossDemandApi();
        var uid = Guid.NewGuid();
        api.CreateResults.Enqueue(ServiceResult<GrossDemandDetailDto>.Ok(new GrossDemandDetailDto
        { Uid = uid, Version = 1, SourceNo = "MD-6", Description = "历史回执", IsNonStandard = true, DemandQty = 1m }));
        api.DetailResults.Enqueue(ServiceResult<GrossDemandDetailDto>.Fail(ServiceErrorCodes.ServiceUnavailable, "读取失败"));
        api.DetailResults.Enqueue(ServiceResult<GrossDemandDetailDto>.Ok(new GrossDemandDetailDto
        { Uid = uid, Version = 3, SourceNo = "MD-6", Description = "当前详情", IsNonStandard = true, DemandQty = 2m }));
        using var vm = CreateViewModel(api);
        vm.ManualEditor.OpenCreate();
        vm.ManualEditor.Edit.IsNonStandard = true;
        vm.ManualEditor.Edit.Description = "提交内容";
        await vm.ManualEditor.SaveCommand.ExecuteAsync(null);

        Assert.IsTrue(vm.ManualEditor.IsEditorLocked);
        Assert.IsFalse(vm.ManualEditor.CanEditFields);
        Assert.AreEqual("刷新详情", vm.ManualEditor.SaveButtonText);
        Assert.IsFalse(await vm.CanCloseAsync(CancellationToken.None));
        Assert.AreEqual("提交内容", vm.ManualEditor.Edit.Description);
        await vm.ManualEditor.SaveCommand.ExecuteAsync(null);

        Assert.AreEqual(1, api.CreateRequests.Count);
        Assert.AreEqual(2, api.DetailRequests.Count);
        Assert.AreEqual(3, vm.ManualEditor.Edit.Version);
        Assert.AreEqual("当前详情", vm.ManualEditor.Edit.Description);
        Assert.IsFalse(vm.ManualEditor.IsEditorLocked);
    }

    /// <summary>仅有工作台访问但缺独立维护权限时，创建与编辑实际入口不可执行。</summary>
    [TestMethod]
    public void ManualPermission_DoesNotFollowModuleAccess()
    {
        using var vm = CreateViewModel(new FakeGrossDemandApi(), allowManual: false);
        Assert.IsFalse(vm.CreateManualCommand.CanExecute(null));
        vm.ManualEditor.OpenCreate();
        Assert.IsFalse(vm.ManualEditor.CanEditFields);
        Assert.IsFalse(vm.ManualEditor.SaveCommand.CanExecute(null));
    }

    /// <summary>未决状态绑定在包围全部输入的表单容器上，保存按钮仍独立保留重试。</summary>
    [TestMethod]
    public void ManualEditor_FieldsBindToPendingEditGate()
    {
        var view = XDocument.Load(Path.Combine(FindRepositoryRoot(), "SwiftExport.Client.Wpf", "Features",
            "DemandPlanning", "GrossDemand", "Views", "GrossDemandManualEditorView.xaml"));
        XNamespace ns = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var form = view.Descendants(ns + "Grid")
            .Single(element => (string?)element.Attribute("DataContext") == "{Binding Edit}");
        StringAssert.Contains((string?)form.Attribute("IsEnabled") ?? string.Empty, "CanEditFields");
        var save = view.Descendants().Single(element => (string?)element.Attribute("Content") == "{Binding SaveButtonText}");
        Assert.AreEqual("{Binding SaveCommand}", (string?)save.Attribute("Command"));
    }
    [TestMethod]
    public async Task CurrentPageSelection_ShouldMaintainTriStateAndCommandCapability()
    {
        var api = new FakeGrossDemandApi
        {
            SearchResponse = CreatePage(
                CreateItem("SO-1", canDecompose: true),
                CreateItem("SO-2", canDecompose: false)),
        };
        using var viewModel = CreateViewModel(api);
        await viewModel.InitializeAsync();





        Assert.IsFalse(viewModel.Items[0].GetType().GetProperties().Any(p => p.Name == "IsChecked"));
        Assert.IsFalse(viewModel.GetType().GetProperties().Any(p => p.Name == "CheckedCount"));
    }

    [TestMethod]
    public async Task Search_ShouldSplitMultilineConditionsAndResetPage()
    {
        var api = new FakeGrossDemandApi { SearchResponse = CreatePage() };
        using var viewModel = CreateViewModel(api);
        await viewModel.InitializeAsync();
        viewModel.PageIndex = 3;
        viewModel.SourceNosText = "SO-1\nSO-2,SO-1";
        viewModel.SkusText = "A-1；A-2";
        viewModel.CustomerSkusText = "C-1;C-2";

        await viewModel.SearchCommand.ExecuteAsync(null);

        Assert.IsNotNull(api.LastSearchRequest);
        Assert.AreEqual(1, api.LastSearchRequest.PageIndex);
        CollectionAssert.AreEquivalent(new[] { "SO-1", "SO-2" }, api.LastSearchRequest.SourceNos);
        CollectionAssert.AreEquivalent(new[] { "A-1", "A-2" }, api.LastSearchRequest.Skus);
        CollectionAssert.AreEquivalent(new[] { "C-1", "C-2" }, api.LastSearchRequest.CustomerSkus);
    }

    [TestMethod]
    public async Task SelectingSourceGroup_ShouldLoadWholeSourceWithoutChangingNormalPageSize()
    {
        var sourceUid = Guid.NewGuid();
        var items = Enumerable.Range(1, 250)
            .Select(index => CreateItem($"SO-WHOLE-{index}", canDecompose: true))
            .ToArray();
        var api = new FakeGrossDemandApi
        {
            SourceGroupResponse =
            [
                new GrossDemandSourceGroupDto
                {
                    SourceUid = sourceUid,
                    SourceType = "SO",
                    SourceNo = "SO-WHOLE",
                    DemandCount = items.Length,
                }
            ],
        };
        using var viewModel = CreateViewModel(api);
        await viewModel.InitializeAsync();
        await viewModel.ToggleSourcePanelCommand.ExecuteAsync(null);
        api.SearchResponse = new PagedResponseBase<GrossDemandListItemDto>
        {
            Items = items.ToList(),
            TotalCount = items.Length,
            PageIndex = 1,
            PageSize = items.Length,
        };

        await viewModel.SelectSourceGroupCommand.ExecuteAsync(viewModel.SourceGroups[0]);

        Assert.IsTrue(viewModel.IsSourceSelected);
        Assert.AreEqual(sourceUid, api.LastSearchRequest?.SourceUid);
        Assert.AreEqual(250, viewModel.Items.Count);
        Assert.AreEqual(20, viewModel.PageSize);
        Assert.AreEqual(1, viewModel.TotalPages);
        Assert.IsFalse(viewModel.PageChangeCommand.CanExecute(null));
        StringAssert.Contains(viewModel.ResultSummary, "已全部加载");
        Assert.AreEqual(1, api.SourceGroupCallCount);
        CollectionAssert.AreEqual(
            new[] { GrossDemandLifecycleStatusEnum.Active },
            api.LastSourceGroupRequest?.LifecycleStatuses);
        CollectionAssert.AreEqual(
            new[] { GrossDemandLifecycleStatusEnum.Active },
            api.LastSearchRequest?.LifecycleStatuses);
        Assert.IsTrue(string.IsNullOrWhiteSpace(api.LastSearchRequest?.Keyword));
    }

    [TestMethod]
    public async Task Refresh_WhenSelectedSourceNoLongerExists_ShouldRestoreMainSearch()
    {
        var sourceUid = Guid.NewGuid();
        var api = new FakeGrossDemandApi
        {
            SourceGroupResponse =
            [
                new GrossDemandSourceGroupDto
                {
                    SourceUid = sourceUid,
                    SourceType = "SO",
                    SourceNo = "SO-REMOVED",
                    DemandCount = 1,
                }
            ],
        };
        using var viewModel = CreateViewModel(api);
        await viewModel.InitializeAsync();
        await viewModel.ToggleSourcePanelCommand.ExecuteAsync(null);
        await viewModel.SelectSourceGroupCommand.ExecuteAsync(viewModel.SourceGroups[0]);
        api.SourceGroupResponse = [];

        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.IsFalse(viewModel.IsSourceSelected);
        Assert.IsNull(api.LastSearchRequest?.SourceUid);
        Assert.IsNull(viewModel.SelectedSourceGroup);
        Assert.IsTrue(viewModel.IsSourcePanelOpen);
    }

    [TestMethod]
    public async Task SourcePanel_ShouldUseIndependentDefaultsAndOpeningMustNotReloadGrid()
    {
        var initialItem = CreateItem("SO-INITIAL", canDecompose: true);
        var api = new FakeGrossDemandApi
        {
            SearchResponse = CreatePage(initialItem),
            SourceGroupResponse =
            [
                new GrossDemandSourceGroupDto
                {
                    SourceUid = initialItem.SourceUid,
                    SourceType = "SO",
                    SourceNo = initialItem.SourceNo,
                    DemandCount = 1,
                }
            ],
        };
        using var viewModel = CreateViewModel(api);
        await viewModel.InitializeAsync();

        Assert.IsFalse(viewModel.IsSourcePanelOpen);
        Assert.AreEqual(0, api.SourceGroupCallCount);
        Assert.AreEqual(1, api.SearchCallCount);
        CollectionAssert.AreEqual(
            new[] { GrossDemandLifecycleStatusEnum.Active },
            viewModel.SourceLifecycleStatuses.Select(item => item.Value).ToArray());

        await viewModel.ToggleSourcePanelCommand.ExecuteAsync(null);

        Assert.IsTrue(viewModel.IsSourcePanelOpen);
        Assert.AreEqual(1, api.SourceGroupCallCount);
        Assert.AreEqual(1, api.SearchCallCount);
        Assert.AreEqual(initialItem.Uid, viewModel.Items.Single().Uid);
    }

    [TestMethod]
    public async Task ClosingSourcePanel_ShouldClearSelectionAndRestoreMainPagedSearch()
    {
        var sourceUid = Guid.NewGuid();
        var api = new FakeGrossDemandApi
        {
            SourceGroupResponse =
            [
                new GrossDemandSourceGroupDto
                {
                    SourceUid = sourceUid,
                    SourceType = "SO",
                    SourceNo = "SO-CLOSE",
                    DemandCount = 1,
                }
            ],
        };
        using var viewModel = CreateViewModel(api);
        await viewModel.InitializeAsync();
        await viewModel.ToggleSourcePanelCommand.ExecuteAsync(null);
        await viewModel.SelectSourceGroupCommand.ExecuteAsync(viewModel.SourceGroups[0]);
        var callsBeforeClosing = api.SearchCallCount;

        await viewModel.ToggleSourcePanelCommand.ExecuteAsync(null);

        Assert.IsFalse(viewModel.IsSourcePanelOpen);
        Assert.IsFalse(viewModel.IsSourceSelected);
        Assert.IsNull(viewModel.SelectedSourceGroup);
        Assert.IsNull(api.LastSearchRequest?.SourceUid);
        Assert.AreEqual(callsBeforeClosing + 1, api.SearchCallCount);
    }

    [TestMethod]
    public async Task ClearSourceFilters_WithoutSelection_ShouldOnlyRefreshSourceList()
    {
        var initialItem = CreateItem("SO-MAIN", canDecompose: true);
        var api = new FakeGrossDemandApi { SearchResponse = CreatePage(initialItem) };
        using var viewModel = CreateViewModel(api);
        await viewModel.InitializeAsync();
        await viewModel.ToggleSourcePanelCommand.ExecuteAsync(null);
        var searchCallsBeforeClear = api.SearchCallCount;

        await viewModel.ClearSourceFiltersCommand.ExecuteAsync(null);

        Assert.AreEqual(0, viewModel.SourceLifecycleStatuses.Count);
        Assert.AreEqual(searchCallsBeforeClear, api.SearchCallCount);
        Assert.AreEqual(initialItem.Uid, viewModel.Items.Single().Uid);
        Assert.AreEqual(2, api.SourceGroupCallCount);
        Assert.AreEqual(0, api.LastSourceGroupRequest?.LifecycleStatuses.Count);
    }

    [TestMethod]
    public async Task MainSearch_ShouldExitSourceModeButKeepPanelOpen()
    {
        var sourceUid = Guid.NewGuid();
        var api = new FakeGrossDemandApi
        {
            SourceGroupResponse =
            [
                new GrossDemandSourceGroupDto
                {
                    SourceUid = sourceUid,
                    SourceType = "SO",
                    SourceNo = "SO-MAIN-SEARCH",
                    DemandCount = 1,
                }
            ],
        };
        using var viewModel = CreateViewModel(api);
        await viewModel.InitializeAsync();
        await viewModel.ToggleSourcePanelCommand.ExecuteAsync(null);
        await viewModel.SelectSourceGroupCommand.ExecuteAsync(viewModel.SourceGroups[0]);
        viewModel.Keyword = "主区域条件";

        await viewModel.SearchCommand.ExecuteAsync(null);

        Assert.IsTrue(viewModel.IsSourcePanelOpen);
        Assert.IsFalse(viewModel.IsSourceSelected);
        Assert.IsNull(viewModel.SelectedSourceGroup);
        Assert.IsNull(api.LastSearchRequest?.SourceUid);
        Assert.AreEqual("主区域条件", api.LastSearchRequest?.Keyword);
    }

    [TestMethod]
    public async Task ClosingSourcePanel_WhenDirtyDiscardRejected_ShouldKeepSourceMode()
    {
        var sourceUid = Guid.NewGuid();
        var dialog = new RecordingDialogService(confirmResult: false);
        var api = new FakeGrossDemandApi
        {
            SourceGroupResponse =
            [
                new GrossDemandSourceGroupDto
                {
                    SourceUid = sourceUid,
                    SourceType = "SO",
                    SourceNo = "SO-DIRTY",
                    DemandCount = 1,
                }
            ],
        };
        using var viewModel = CreateViewModel(api, dialog);
        await viewModel.InitializeAsync();
        await viewModel.ToggleSourcePanelCommand.ExecuteAsync(null);
        await viewModel.SelectSourceGroupCommand.ExecuteAsync(viewModel.SourceGroups[0]);
        await viewModel.CreateManualCommand.ExecuteAsync(null);
        viewModel.ManualEditor.Edit.Remark = "未保存";

        await viewModel.ToggleSourcePanelCommand.ExecuteAsync(null);

        Assert.IsTrue(viewModel.IsSourcePanelOpen);
        Assert.IsTrue(viewModel.IsSourceSelected);
        Assert.AreEqual(sourceUid, viewModel.SelectedSourceGroup?.SourceUid);
        Assert.AreEqual(1, dialog.ConfirmCount);
    }

    [TestMethod]
    public async Task CloseDrawer_WhenManualEditorIsClean_ShouldCloseWithoutConfirmation()
    {
        var dialog = new RecordingDialogService(confirmResult: false);
        using var viewModel = CreateViewModel(new FakeGrossDemandApi(), dialog);
        await viewModel.InitializeAsync();
        await viewModel.CreateManualCommand.ExecuteAsync(null);

        await viewModel.CloseDrawerCommand.ExecuteAsync(null);

        Assert.IsFalse(viewModel.IsDrawerOpen);
        Assert.IsFalse(viewModel.ManualEditor.IsOpen);
        Assert.AreEqual(0, dialog.ConfirmCount);
    }

    [TestMethod]
    public async Task CloseDrawer_WhenManualEditorIsDirtyAndDiscardRejected_ShouldKeepEditorOpen()
    {
        var dialog = new RecordingDialogService(confirmResult: false);
        using var viewModel = CreateViewModel(new FakeGrossDemandApi(), dialog);
        await viewModel.InitializeAsync();
        await viewModel.CreateManualCommand.ExecuteAsync(null);
        viewModel.ManualEditor.Edit.Remark = "尚未保存";

        await viewModel.CloseDrawerCommand.ExecuteAsync(null);

        Assert.IsTrue(viewModel.IsDrawerOpen);
        Assert.IsTrue(viewModel.ManualEditor.IsOpen);
        Assert.IsTrue(viewModel.ManualEditor.HasUnsavedChanges);
        Assert.AreEqual(1, dialog.ConfirmCount);
    }

    [TestMethod]
    public async Task CloseDrawer_WhenManualEditorIsDirtyAndDiscardConfirmed_ShouldDiscardAndClose()
    {
        var dialog = new RecordingDialogService(confirmResult: true);
        using var viewModel = CreateViewModel(new FakeGrossDemandApi(), dialog);
        await viewModel.InitializeAsync();
        await viewModel.CreateManualCommand.ExecuteAsync(null);
        viewModel.ManualEditor.Edit.Remark = "尚未保存";

        await viewModel.CloseDrawerCommand.ExecuteAsync(null);

        Assert.IsFalse(viewModel.IsDrawerOpen);
        Assert.IsFalse(viewModel.ManualEditor.IsOpen);
        Assert.IsFalse(viewModel.ManualEditor.HasUnsavedChanges);
        Assert.AreEqual(1, dialog.ConfirmCount);
    }

    [TestMethod]
    public void ManualEditorCancelButton_ShouldBindWorkspaceCloseDrawerCommand()
    {
        var repositoryRoot = FindRepositoryRoot();
        var xamlPath = Path.Combine(
            repositoryRoot,
            "SwiftExport.Client.Wpf",
            "Features",
            "DemandPlanning",
            "GrossDemand",
            "Views",
            "GrossDemandManualEditorView.xaml");
        var document = XDocument.Load(xamlPath);
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var cancelButton = document
            .Descendants(presentation + "Button")
            .Single(element => string.Equals((string?)element.Attribute(x + "Name"), "CancelButton", StringComparison.Ordinal));

        Assert.AreEqual("取消", (string?)cancelButton.Attribute("Content"));
        StringAssert.Contains((string?)cancelButton.Attribute("Command") ?? string.Empty, "DataContext.CloseDrawerCommand");
        StringAssert.Contains((string?)cancelButton.Attribute("Command") ?? string.Empty, "GrossDemandWorkspaceView");
    }

    [TestMethod]
    public void Workspace_ShouldNotLoadDownstreamTraceFeature()
    {
        var repositoryRoot = FindRepositoryRoot();
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var workspace = XDocument.Load(Path.Combine(
            repositoryRoot,
            "SwiftExport.Client.Wpf",
            "Features",
            "DemandPlanning",
            "GrossDemand",
            "Views",
            "GrossDemandWorkspaceView.xaml"));
        Assert.IsFalse(workspace.Descendants(presentation + "ToggleButton")
            .Any(element => string.Equals((string?)element.Attribute("Content"), "拆解结果", StringComparison.Ordinal)));
        Assert.IsFalse(workspace.Descendants()
            .Any(element => ((string?)element.Attribute("DataType"))?.Contains("GrossDemandTraceViewModel", StringComparison.Ordinal) == true));
        Assert.IsFalse(workspace.Descendants()
            .Any(element => ((string?)element.Attribute("Placeholder"))?.Contains("执行进度", StringComparison.Ordinal) == true));
    }

    /// <summary>
    /// 验证多行筛选通过“更多条件”按钮控制全宽弹出层，不回退为折叠面板。
    /// </summary>
    [TestMethod]
    public void MultiLineFilters_ShouldUseToggleControlledFullWidthPopup()
    {
        var repositoryRoot = FindRepositoryRoot();
        var workspace = XDocument.Load(Path.Combine(
            repositoryRoot,
            "SwiftExport.Client.Wpf",
            "Features",
            "DemandPlanning",
            "GrossDemand",
            "Views",
            "GrossDemandWorkspaceView.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        Assert.IsFalse(workspace.Descendants(presentation + "Expander")
            .Any(element => (string?)element.Attribute("Header") is "多行条件" or "更多条件"));
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var toggle = workspace.Descendants(presentation + "ToggleButton")
            .Single(element => (string?)element.Attribute(xaml + "Name") == "OptionsToggle");
        Assert.AreEqual("更多条件", (string?)toggle.Attribute("Content"));

        var popup = workspace.Descendants(presentation + "Popup").Single();
        StringAssert.Contains((string?)toggle.Attribute("IsChecked") ?? string.Empty, "IsMultiLineFilterOpen");
        StringAssert.Contains((string?)popup.Attribute("IsOpen") ?? string.Empty, "IsMultiLineFilterOpen");
        StringAssert.Contains((string?)popup.Attribute("PlacementTarget") ?? string.Empty, "OptionsToggle");
        Assert.IsTrue(popup.Descendants(presentation + "Button").Any(element => (string?)element.Attribute("Content") == "应用并查询"));
        Assert.IsTrue(popup.Descendants(presentation + "Button").Any(element => (string?)element.Attribute("Content") == "清空更多条件"));
        Assert.IsTrue(popup.Descendants(presentation + "Button").Any(element => (string?)element.Attribute("Content") == "收起"));
        Assert.AreEqual("False", (string?)popup.Attribute("StaysOpen"));
        Assert.AreEqual(3, popup.Descendants().Count(element => element.Name.LocalName == "TextBox"));
    }

    /// <summary>
    /// 验证来源分组区使用独立筛选和整高列表，并在来源模式下隐藏分页区域。
    /// </summary>
    [TestMethod]
    public void SourceGroups_ShouldUseFullHeightListAndHidePagerInSourceMode()
    {
        var repositoryRoot = FindRepositoryRoot();
        var workspace = XDocument.Load(Path.Combine(
            repositoryRoot,
            "SwiftExport.Client.Wpf",
            "Features",
            "DemandPlanning",
            "GrossDemand",
            "Views",
            "GrossDemandWorkspaceView.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var sourceList = workspace.Descendants(presentation + "ListBox").Single();
        StringAssert.Contains((string?)sourceList.Attribute("ItemsSource") ?? string.Empty, "SourceGroups");
        StringAssert.Contains((string?)sourceList.Attribute("SelectedItem") ?? string.Empty, "SelectedSourceGroup");
        Assert.AreEqual("OnSourceGroupSelectionChanged", (string?)sourceList.Attribute("SelectionChanged"));

        var sourcePanel = sourceList.Ancestors(presentation + "Border").First();
        Assert.AreEqual("240", (string?)sourcePanel.Attribute("Width"));
        StringAssert.Contains((string?)sourcePanel.Attribute("Visibility") ?? string.Empty, "IsSourcePanelOpen");
        Assert.IsTrue(workspace.Descendants(presentation + "Thumb").Any());

        var sourceToggle = workspace.Descendants(presentation + "ToggleButton")
            .Single(element => ((string?)element.Attribute("Command"))
                ?.Contains("ToggleSourcePanelCommand", StringComparison.Ordinal) == true);
        Assert.AreEqual("来源导航", (string?)sourceToggle.Attribute("Content"));
        StringAssert.Contains((string?)sourceToggle.Attribute("Command") ?? string.Empty, "ToggleSourcePanelCommand");
        StringAssert.Contains((string?)sourceToggle.Attribute("IsChecked") ?? string.Empty, "IsSourcePanelOpen");
        var multilineToggle = workspace.Descendants(presentation + "ToggleButton")
            .Single(element => (string?)element.Attribute("Content") == "更多条件");
        Assert.AreEqual("更多条件", (string?)multilineToggle.Attribute("Content"));
        // 按文档顺序检查按钮先后，布局已由固定 Grid 列迁移为 WrapPanel。
        Assert.IsTrue(workspace.Descendants().TakeWhile(element => element != multilineToggle).Contains(sourceToggle));

        var sourceFilters = sourcePanel.Descendants().Where(element => element.Name.LocalName == "DropDownCheckBox").ToArray();
        Assert.AreEqual(1, sourceFilters.Length);
        Assert.IsTrue(sourceFilters.Any(element => ((string?)element.Attribute("SelectedItems"))?.Contains("SourceLifecycleStatuses", StringComparison.Ordinal) == true));
        Assert.IsFalse(sourceFilters.Any(element => ((string?)element.Attribute("SelectedItems"))?.Contains("SourceDecompositionStatuses", StringComparison.Ordinal) == true));
        Assert.IsTrue(sourcePanel.Descendants(presentation + "Button")
            .Any(element => string.Equals((string?)element.Attribute("Content"), "清空筛选", StringComparison.Ordinal)));
        Assert.IsFalse(sourcePanel.Descendants().Any(element => string.Equals((string?)element.Attribute("Content"), "全部", StringComparison.Ordinal)));

        var sourceModeTrigger = workspace.Descendants(presentation + "DataTrigger")
            .Single(element => ((string?)element.Attribute("Binding"))?.Contains("IsSourceSelected", StringComparison.Ordinal) == true);
        Assert.AreEqual("True", (string?)sourceModeTrigger.Attribute("Value"));
    }

    [TestMethod]
    public void WorkspaceViewModel_ShouldKeepRequestedChineseRegions()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "SwiftExport.Client.Wpf",
            "Features",
            "DemandPlanning",
            "GrossDemand",
            "ViewModels",
            "GrossDemandWorkspaceViewModel.cs"));

        foreach (var region in new[]
                 {
                     "来源导航与独立筛选",
                     "主区域筛选与分页",
                     "成品需求业务命令",
                     "抽屉与来源追溯",
                     "查询加载与并发控制",
                     "选择状态与辅助方法",
                 })
        {
            StringAssert.Contains(source, $"#region {region}");
        }
    }

    private static GrossDemandWorkspaceViewModel CreateViewModel(
        FakeGrossDemandApi api,
        IDialogService? dialogService = null,
        bool allowManual = true,
        ICurrentUserService? currentUser = null,
        INavigationService? navigationService = null)
    {
        var runner = new PassThroughApiCallRunner();
        var messages = new TestMessageService();
        var user = currentUser ?? DispatchProxy.Create<ICurrentUserService, PermissionProxy>();
        if (currentUser is null) ((PermissionProxy)(object)user).Allow = allowManual;
        var manual = new GrossDemandManualEditorViewModel(
            api,
            DispatchProxy.Create<IProductApi, NoCallProxy>(),
            runner,
            messages,
            user);
        var navigation = navigationService ?? DispatchProxy.Create<INavigationService, NoCallProxy>();
        return new GrossDemandWorkspaceViewModel(
            api,
            runner,
            navigation,
            dialogService ?? new RecordingDialogService(confirmResult: true),
            messages,
            manual,
            user);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SwiftExport-Frontend.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        Assert.Fail("无法定位 SwiftExport 仓库根目录。");
        return string.Empty;
    }

    private static GrossDemandListItemDto CreateItem(string sourceNo, bool canDecompose) => new()
    {
        Uid = Guid.NewGuid(),
        Version = 1,
        LifecycleStatus = GrossDemandLifecycleStatusEnum.Active,
        SourceType = "SO",
        SourceNo = sourceNo,
        SourceUid = Guid.NewGuid(),
        SourceLineId = Guid.NewGuid(),
        Sku = sourceNo,
        Description = sourceNo,
        DemandQty = 1m,
    };

    private static PagedResponseBase<GrossDemandListItemDto> CreatePage(params GrossDemandListItemDto[] items) => new()
    {
        Items = items.ToList(),
        TotalCount = items.Length,
        PageIndex = 1,
        PageSize = 20,
    };

    private sealed class FakeGrossDemandApi : IGrossDemandWorkspaceApi
    {
        public Queue<ServiceResult<GrossDemandDetailDto>> CreateResults { get; } = new();
        public Queue<ServiceResult<GrossDemandDetailDto>> UpdateResults { get; } = new();
        public Queue<ServiceResult<GrossDemandDetailDto>> CloseResults { get; } = new();
        public Queue<ServiceResult<GrossDemandDetailDto>> DetailResults { get; } = new();
        public List<Guid> DetailRequests { get; } = [];
        public Func<CreateManualGrossDemandRequest, Task<ServiceResult<GrossDemandDetailDto>>>? CreateHandler { get; set; }
        public List<CreateManualGrossDemandRequest> CreateRequests { get; } = [];
        public List<UpdateManualGrossDemandRequest> UpdateRequests { get; } = [];
        public List<CloseGrossDemandCommand> CloseRequests { get; } = [];
        public PagedResponseBase<GrossDemandListItemDto> SearchResponse { get; set; } = CreatePage();
        public List<GrossDemandSourceGroupDto> SourceGroupResponse { get; set; } = [];
        public GrossDemandSearchRequest? LastSearchRequest { get; private set; }
        public GrossDemandSourceGroupSearchRequest? LastSourceGroupRequest { get; private set; }
        public int SearchCallCount { get; private set; }
        public int SourceGroupCallCount { get; private set; }
        public int TraceCallCount { get; private set; }
        public List<GrossDemandSearchRequest> SearchRequests { get; } = [];
        public List<CancellationToken> SearchTokens { get; } = [];
        public List<GrossDemandSourceGroupSearchRequest> SourceRequests { get; } = [];
        public int CustomerCalls { get; private set; }
        public List<GrossDemandCustomerOptionDto> Customers { get; } = [];
        public Func<GrossDemandSearchRequest, CancellationToken, Task<ServiceResult<PagedResponseBase<GrossDemandListItemDto>>>>? SearchHandler { get; set; }
        public Func<GrossDemandSourceGroupSearchRequest, CancellationToken, Task<ServiceResult<List<GrossDemandSourceGroupDto>>>>? SourceHandler { get; set; }
        public Func<CancellationToken, Task<ServiceResult<List<GrossDemandCustomerOptionDto>>>>? CustomerHandler { get; set; }

        public Task<ServiceResult<List<GrossDemandCustomerOptionDto>>> GetCustomerOptionsAsync(CancellationToken cancellationToken = default)
        { CustomerCalls++; return CustomerHandler?.Invoke(cancellationToken) ?? Task.FromResult(ServiceResult<List<GrossDemandCustomerOptionDto>>.Ok(Customers)); }
        public Task<ServiceResult<PagedResponseBase<GrossDemandListItemDto>>> SearchAsync(GrossDemandSearchRequest request, CancellationToken cancellationToken = default)
        {
            LastSearchRequest = request;
            SearchCallCount++;
            SearchRequests.Add(request); SearchTokens.Add(cancellationToken);
            return SearchHandler?.Invoke(request, cancellationToken) ?? Task.FromResult(ServiceResult<PagedResponseBase<GrossDemandListItemDto>>.Ok(SearchResponse));
        }

        public Task<ServiceResult<List<GrossDemandSourceGroupDto>>> SearchSourceGroupsAsync(
            GrossDemandSourceGroupSearchRequest request,
            CancellationToken cancellationToken = default)
        {
            LastSourceGroupRequest = request;
            SourceGroupCallCount++;
            SourceRequests.Add(request);
            return SourceHandler?.Invoke(request, cancellationToken) ?? Task.FromResult(ServiceResult<List<GrossDemandSourceGroupDto>>.Ok(SourceGroupResponse));
        }

        public Task<ServiceResult<GrossDemandDetailDto>> GetDetailAsync(Guid uid, CancellationToken cancellationToken = default)
        { DetailRequests.Add(uid); return Task.FromResult(DetailResults.Dequeue()); }
        public Task<ServiceResult<GrossDemandDetailDto>> CreateManualAsync(CreateManualGrossDemandRequest request)
        { CreateRequests.Add(request); return CreateHandler is null ? Task.FromResult(CreateResults.Dequeue()) : CreateHandler(request); }
        public Task<ServiceResult<GrossDemandDetailDto>> UpdateManualAsync(Guid uid, UpdateManualGrossDemandRequest request)
        { UpdateRequests.Add(request); return Task.FromResult(UpdateResults.Dequeue()); }
        public Task<ServiceResult<GrossDemandDetailDto>> CloseManualAsync(Guid uid, CloseGrossDemandCommand command)
        { CloseRequests.Add(command); return Task.FromResult(CloseResults.Dequeue()); }
    }

    private sealed class PassThroughApiCallRunner : IApiCallRunner
    {
        public Task<ServiceResult<T>> RunAsync<T>(Func<Task<ServiceResult<T>>> action, string failureMessage = "请求失败") => action();
        public Task<ServiceResult<T>> RunAsync<T>(Func<Task<ServiceResult<T>>> action, CancellationToken cancellationToken, string failureMessage = "请求失败") => action();
        public async Task<ServiceResult<T>> RunValueAsync<T>(Func<Task<T>> action, string successMessage = "成功", string failureMessage = "请求失败") => ServiceResult<T>.Ok(await action(), successMessage);
    }

    public class NoCallProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => throw new NotSupportedException(targetMethod?.Name);
    }

    /// <summary>只对本页业务按钮提供允许权限的内存会话。</summary>
    public class PermissionProxy : DispatchProxy
    {
        public bool Allow { get; set; } = true;
        protected override object? Invoke(MethodInfo? method, object?[]? args) => method?.Name switch
        {
            "HasPermission" => (string?)args?[0] != SwiftExport.Contracts.DTOs.Auth.PermissionKeys.DemandPlanningSourceManualManage || Allow,
            "get_CurrentUser" => null,
            "add_CurrentUserChanged" or "remove_CurrentUserChanged" => null,
            _ => throw new NotSupportedException(method?.Name)
        };
    }

    private sealed class RecordingDialogService(bool confirmResult) : IDialogService
    {
        public int ConfirmCount { get; private set; }
        public string Reason { get; set; } = "业务结案";
        public int ReasonCount { get; private set; }
        public Task<DialogResult> ShowAsync(object content, DialogOptions options, IDialogContentState? state = null, CancellationToken cancellationToken = default)
        { ReasonCount++; if(content is GrossDemandCloseReasonViewModel model) model.Reason=Reason; return Task.FromResult(confirmResult?DialogResult.Primary:DialogResult.Close); }

        public Task ShowContentAsync(string title, object content, string closeButtonText = "关闭", CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> ConfirmAsync(string title, string message, string primaryButtonText = "确定", string closeButtonText = "取消", CancellationToken cancellationToken = default)
        {
            ConfirmCount++;
            return Task.FromResult(confirmResult);
        }
        public Task ShowErrorAsync(string title, string message, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class TestMessageService : IMessageService
    {
        public void ShowInfo(string title, string message) { }
        public void ShowSuccess(string title, string message) { }
        public void ShowWarning(string title, string message) { }
        public void ShowError(string title, string message) { }
    }
}
