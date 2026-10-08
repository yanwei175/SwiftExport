using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Net.Http;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.QualityControl.FlowManagement;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Features.QualityControl.FlowManagement.Models;
using SwiftExportClient.Mvvm;

namespace SwiftExport.Client.Wpf.Tests.Features.QualityControl.FlowManagement;

/// <summary>验证流量编辑页行为，全部使用离线端口。</summary>
[TestClass]
public sealed class FlowEditorTests
{
    /// <summary>完整新增保留两套测量、空值和零值，接纳服务器身份与版本。</summary>
    [TestMethod]
    public async Task CreateKeepsNullZeroAndAcceptsIdentity()
    {
        var host = new FlowTestHost(); using var vm = host.Editor();
        await vm.InitializeDocumentAsync(new FlowOpenRequest(), default);
        vm.SelectedMaterial = new ProductLookupDto { Uid = FlowTestHost.MaterialId, Sku = "SKU-1" };
        var temporary = vm.Lines[0].Uid;
        vm.Lines[0].Flow1BarLMin = 0; vm.Lines[0].Flow100PsiGpm = 1.2m;
        vm.AddLineCommand.Execute(null); vm.Lines[1].FunctionId = FlowTestHost.OtherFunctionId;
        vm.Lines[1].LimiterSpecificationId = FlowTestHost.OtherLimiterId; vm.Lines[1].Flow2BarLMin = 4;
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.AreEqual(1, host.Api.SaveCalls); Assert.AreEqual(2, host.Api.LastEdit!.Lines.Count);
        Assert.AreEqual(0m, host.Api.LastEdit.Lines[0].Flow1BarLMin); Assert.IsNull(host.Api.LastEdit.Lines[0].Flow2BarLMin);
        Assert.AreEqual(1.2m, host.Api.LastEdit.Lines[0].Flow100PsiGpm);
        Assert.IsTrue(host.Api.LastEdit.Lines.All(x => x.Uid == Guid.Empty && x.Version == 0));
        Assert.AreNotEqual(temporary, vm.Lines[0].Uid); Assert.IsTrue(vm.IsPersisted);
        Assert.IsFalse(vm.CanSelectMaterial); Assert.IsFalse(vm.IsDirty);
        Assert.AreEqual($"flow:{vm.Header.Uid:N}", host.Navigation.Key);
        Assert.AreEqual("服务端功能", vm.Lines[0].FunctionDisplayName);
    }

    /// <summary>整组更新提交最终保留行，包含新增、修改和移除。</summary>
    [TestMethod]
    public async Task UpdateSendsFinalSet()
    {
        var host = new FlowTestHost(); using var vm = host.Editor();
        await vm.InitializeDocumentAsync(new FlowOpenRequest { HeaderId = host.Api.Detail.Uid }, default);
        var first = vm.Lines[0]; var removed = vm.Lines[1].Uid;
        first.Flow1BarLMin = 5; vm.SelectedLine = vm.Lines[1]; vm.RemoveLineCommand.Execute(null);
        Assert.AreEqual(1, vm.LineList.DeletedItems.Count);
        vm.AddLineCommand.Execute(null); vm.Lines[1].FunctionId = FlowTestHost.OtherFunctionId; vm.Lines[1].Flow20PsiGpm = 0;
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.AreEqual(4, host.Api.LastUpdate!.Version); Assert.AreEqual(2, host.Api.LastEdit!.Lines[0].Version);
        Assert.AreEqual(first.Uid, host.Api.LastEdit.Lines[0].Uid);
        Assert.IsFalse(host.Api.LastEdit.Lines.Any(x => x.Uid == removed)); Assert.AreEqual(Guid.Empty, host.Api.LastEdit.Lines[1].Uid);
        Assert.AreEqual(5, vm.Header.Version); Assert.AreEqual(0, vm.LineList.DeletedItems.Count); Assert.IsFalse(vm.HasUnsavedChanges);
    }

    /// <summary>冲突不清空输入、删除池和版本。</summary>
    [TestMethod]
    public async Task ConflictKeepsEdits()
    {
        var host = new FlowTestHost(); using var vm = host.Editor();
        await vm.InitializeDocumentAsync(new FlowOpenRequest { HeaderId = host.Api.Detail.Uid }, default);
        vm.Header.Remark = "纠正"; vm.SelectedLine = vm.Lines[1]; vm.RemoveLineCommand.Execute(null);
        host.Api.SaveOverride = () => Task.FromResult(ServiceResult<FlowSaveResponse>.Fail(ServiceErrorCodes.ConcurrencyConflict, "请刷新"));
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.IsTrue(vm.IsDirty); Assert.AreEqual("纠正", vm.Header.Remark);
        Assert.AreEqual(4, vm.Header.Version); Assert.AreEqual(1, vm.LineList.DeletedItems.Count);
        Assert.AreEqual("请刷新", vm.ValidationSummary); Assert.IsFalse(vm.NeedsReview);
    }

    /// <summary>真实 Runner 把网络中断归为结果未定，禁止重复新增。</summary>
    [TestMethod]
    public async Task NetworkFailureBlocksResubmit()
    {
        var host = new FlowTestHost(); using var vm = host.Editor();
        await vm.InitializeDocumentAsync(new FlowOpenRequest(), default);
        vm.Header.MaterialId = FlowTestHost.MaterialId; vm.Lines[0].Flow1BarLMin = 1;
        host.Api.SaveOverride = () => throw new HttpRequestException("offline");
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.IsTrue(vm.NeedsReview); Assert.IsTrue(vm.IsDirty); Assert.IsFalse(vm.CanEdit);
        await vm.SaveCommand.ExecuteAsync(null); Assert.AreEqual(1, host.Api.SaveCalls);
    }

    /// <summary>成功响应缺少详情同样要求先核实。</summary>
    [TestMethod]
    public async Task MissingReceiptBlocksResubmit()
    {
        var host = new FlowTestHost(); using var vm = host.Editor();
        await vm.InitializeDocumentAsync(new FlowOpenRequest(), default);
        vm.Header.MaterialId = FlowTestHost.MaterialId; vm.Lines[0].Flow1BarLMin = 1;
        host.Api.SaveOverride = () => Task.FromResult(ServiceResult<FlowSaveResponse>.Ok(new()));
        await vm.SaveCommand.ExecuteAsync(null); Assert.IsTrue(vm.NeedsReview); Assert.IsFalse(vm.SaveCommand.CanExecute(null));
    }

    /// <summary>保存中的重入和关闭均被拒绝。</summary>
    [TestMethod]
    public async Task BusyBlocksReentryAndClose()
    {
        var host = new FlowTestHost(); using var vm = host.Editor();
        await vm.InitializeDocumentAsync(new FlowOpenRequest(), default);
        vm.Header.MaterialId = FlowTestHost.MaterialId; vm.Lines[0].Flow1BarLMin = 1;
        var gate = new TaskCompletionSource<ServiceResult<FlowSaveResponse>>(); host.Api.SaveOverride = () => gate.Task;
        var pending = vm.SaveCommand.ExecuteAsync(null); Assert.IsTrue(vm.IsBusy);
        await vm.SaveCommand.ExecuteAsync(null); Assert.IsFalse(await vm.CanCloseAsync(default)); Assert.AreEqual(1, host.Api.SaveCalls);
        gate.SetResult(ServiceResult<FlowSaveResponse>.Fail(ServiceErrorCodes.Validation, "拒绝"));
        await pending; Assert.IsFalse(vm.IsBusy); Assert.IsTrue(vm.IsDirty);
    }

    /// <summary>重复功能、空组和无测量点不发送请求。</summary>
    [TestMethod]
    public async Task InvalidGroupNeverSends()
    {
        var host = new FlowTestHost(); using var vm = host.Editor();
        await vm.InitializeDocumentAsync(new FlowOpenRequest(), default);
        vm.Header.MaterialId = FlowTestHost.MaterialId;
        await vm.SaveCommand.ExecuteAsync(null); Assert.AreEqual(0, host.Api.SaveCalls);
        vm.Lines[0].Flow1BarLMin = 0; vm.AddLineCommand.Execute(null); vm.Lines[1].Flow20PsiGpm = 0;
        await vm.SaveCommand.ExecuteAsync(null); Assert.AreEqual(0, host.Api.SaveCalls); Assert.IsTrue(vm.Lines[1].HasErrors);
        foreach (var line in vm.Lines.ToArray()) { vm.SelectedLine = line; vm.RemoveLineCommand.Execute(null); }
        await vm.SaveCommand.ExecuteAsync(null); Assert.AreEqual(0, host.Api.SaveCalls);
    }

    /// <summary>各压力列的负值错误关联到真实字段，零与空保留不同语义。</summary>
    [TestMethod]
    [DataRow("Flow1BarLMin")][DataRow("Flow2BarLMin")][DataRow("Flow3BarLMin")][DataRow("Flow4BarLMin")]
    [DataRow("Flow5BarLMin")][DataRow("Flow6BarLMin")][DataRow("Flow7BarLMin")][DataRow("Flow8BarLMin")]
    [DataRow("Flow20PsiGpm")][DataRow("Flow40PsiGpm")][DataRow("Flow60PsiGpm")][DataRow("Flow80PsiGpm")][DataRow("Flow100PsiGpm")]
    public void NegativeValuesHaveCorrectFieldErrors(string field)
    {
        var line = new FlowLineModel(); line.Load(FlowTestHost.Detail().Lines[0]); line.Flow1BarLMin = null;
        var property = typeof(FlowLineModel).GetProperty(field)!;
        property.SetValue(line, -0.1m); Assert.IsNotNull(line.ValidateRow()); Assert.IsTrue(line.GetErrors(field).Cast<string>().Any());
        property.SetValue(line, 0m); Assert.IsNull(line.ValidateRow()); Assert.IsFalse(line.HasErrors);
        property.SetValue(line, null); Assert.IsNotNull(line.ValidateRow());
    }

    /// <summary>过多小数拒绝不舍入，公共列表回滚恢复删除行和旧值。</summary>
    [TestMethod]
    public void PrecisionAndRollbackWork()
    {
        var list = new BaseListModel<FlowLineModel, FlowLineDto>(_ => new()); list.LoadFromDtos(FlowTestHost.Detail().Lines);
        var first = list.Items[0]; first.Flow1BarLMin = 1.12345m;
        Assert.IsNotNull(first.ValidateRow()); Assert.AreEqual(1.12345m, first.Flow1BarLMin);
        list.Remove(first); list.RejectChanges(); Assert.AreEqual(first, list.Items[0]);
        Assert.AreEqual(2m, first.Flow1BarLMin); Assert.IsFalse(list.HasChanges);
    }

    /// <summary>停用和改名不改历史快照，候选刷新不制造脏状态。</summary>
    [TestMethod]
    public async Task HistoricalDisabledSelectionIsEditable()
    {
        var dictionaries = FlowTestHost.Dictionaries().ToArray(); dictionaries[0].IsEnabled = false; dictionaries[0].DataNameZh = "改名";
        var host = new FlowTestHost(dictionaries); using var vm = host.Editor();
        await vm.InitializeDocumentAsync(new FlowOpenRequest { HeaderId = host.Api.Detail.Uid }, default);
        await vm.RefreshDictionariesCommand.ExecuteAsync(null);
        Assert.AreEqual("历史功能", vm.Lines[0].FunctionDisplayName); Assert.IsFalse(vm.IsDirty);
        Assert.IsFalse(vm.FunctionOptions.Single(x => x.Uid == FlowTestHost.FunctionId).IsEnabled);
        vm.Lines[0].Flow1BarLMin = 7; await vm.SaveCommand.ExecuteAsync(null); Assert.AreEqual(1, host.Api.SaveCalls);
        vm.AddLineCommand.Execute(null); vm.Lines[2].FunctionId = FlowTestHost.FunctionId; vm.Lines[2].Flow1BarLMin = 3;
        await vm.SaveCommand.ExecuteAsync(null); Assert.AreEqual(1, host.Api.SaveCalls);
    }

    /// <summary>未保存的新行使用当前候选名称，刷新不把旧候选误作历史快照。</summary>
    [TestMethod]
    public async Task NewRowsFollowRefreshedDictionaryNames()
    {
        var dictionaries = FlowTestHost.Dictionaries().ToArray();
        var host = new FlowTestHost(dictionaries); using var vm = host.Editor();
        await vm.InitializeDocumentAsync(new FlowOpenRequest(), default);
        dictionaries[0].DataNameZh = "新功能名"; dictionaries[2].DataNameZh = "新规格名";
        await vm.RefreshDictionariesCommand.ExecuteAsync(null);
        Assert.AreEqual(1, host.Cache.RefreshDictionariesCallCount);
        Assert.AreEqual("新功能名", vm.Lines[0].FunctionDisplayName);
        Assert.AreEqual("新规格名", vm.Lines[0].LimiterDisplayName);
        Assert.IsTrue(vm.IsDirty); Assert.IsFalse(vm.Lines[0].IsOriginalFunction);
    }

    /// <summary>删除与编辑权限分开，权限撤回实时影响操作。</summary>
    [TestMethod]
    public async Task DeleteDoesNotRequireEdit()
    {
        var host = new FlowTestHost(); host.User.SetCurrentUser(new MeDto { Permissions =
            [PermissionKeys.QualityControlFlowView, PermissionKeys.QualityControlFlowDelete] });
        using var vm = host.Editor(); await vm.InitializeDocumentAsync(new FlowOpenRequest { HeaderId = host.Api.Detail.Uid }, default);
        Assert.IsFalse(vm.CanEdit); Assert.IsTrue(vm.DeleteCommand.CanExecute(null));
        await vm.DeleteCommand.ExecuteAsync(null); Assert.AreEqual(1, host.Api.DeleteCalls); Assert.IsFalse(vm.HasUnsavedChanges);
        host.User.Clear(); Assert.IsFalse(vm.HasAccess); Assert.IsFalse(vm.DeleteCommand.CanExecute(null));
    }

    /// <summary>取消刷新和关闭保留输入，绑定转换失败不能保存旧值。</summary>
    [TestMethod]
    public async Task CancelAndInputErrorsKeepEdits()
    {
        var host = new FlowTestHost(); using var vm = host.Editor();
        await vm.InitializeDocumentAsync(new FlowOpenRequest { HeaderId = host.Api.Detail.Uid }, default);
        vm.Header.Remark = "未保存"; host.Dialogs.Confirm = false;
        await vm.ReloadCommand.ExecuteAsync(null); Assert.AreEqual(1, host.Api.ReadCalls); Assert.AreEqual("未保存", vm.Header.Remark);
        Assert.IsFalse(await vm.CanCloseAsync(default)); vm.CommitPendingEdits = () => false;
        await vm.SaveCommand.ExecuteAsync(null); Assert.AreEqual(0, host.Api.SaveCalls);
    }

    /// <summary>释放后取消初次查询并拒绝迟到响应。</summary>
    [TestMethod]
    public async Task DisposeRejectsLateResponse()
    {
        var host = new FlowTestHost(); var gate = new TaskCompletionSource<ServiceResult<FlowDetailDto>>(); CancellationToken captured = default;
        host.Api.ReadOverride = token => { captured = token; return gate.Task; };
        var vm = host.Editor(); var pending = vm.InitializeDocumentAsync(new FlowOpenRequest { HeaderId = host.Api.Detail.Uid }, default);
        vm.Dispose(); Assert.IsTrue(captured.IsCancellationRequested);
        gate.SetResult(ServiceResult<FlowDetailDto>.Ok(host.Api.Detail)); await pending;
        Assert.AreEqual(0, vm.Lines.Count); Assert.IsFalse(vm.SaveCommand.CanExecute(null));
    }
}

