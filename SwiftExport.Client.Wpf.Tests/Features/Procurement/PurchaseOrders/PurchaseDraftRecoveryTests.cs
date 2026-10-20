using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Features.Procurement.PurchaseOrders.Models;
using SwiftExportClient.Features.Procurement.PurchaseOrders.Services;
using SwiftExportClient.Features.Procurement.PurchaseOrders.ViewModels;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Threading;

namespace SwiftExport.Client.Wpf.Tests.Features.Procurement.PurchaseOrders;

public sealed partial class PurchaseDraftWorkflowViewModelTests
{
    /// <summary>旧准备弹窗不能覆盖已有未决会话，冲突后明确提示关窗重开，恢复前不会发送。</summary>
    [TestMethod]
    public async Task OlderPreparedWindow_DoesNotReplacePendingRequest()
    {
        var api = new DraftApi { Handler = c => Task.FromResult(SourceFailure(c, ServiceErrorCodes.OutcomeUnknown)) };
        var session = new PurchaseDraftPendingSession();
        using var first = Create(api, pendingSession: session); using var older = Create(api, pendingSession: session);
        await Open(first, Row()); await Open(older, Row());
        await first.CreateCommand.ExecuteAsync(null); var id = first.ExecutionRequestId;
        await older.CreateCommand.ExecuteAsync(null);
        Assert.AreEqual(1, api.Commands.Count); Assert.AreEqual(id, session.Find("draft-user")!.ExecutionRequestId);
        StringAssert.Contains(older.StatusText, "本次未发送"); StringAssert.Contains(older.StatusText, "关闭本弹窗后重开");
        older.Close(); older.OpenSession(); Assert.AreEqual(id, older.ExecutionRequestId);
        Assert.AreEqual(1, api.Commands.Count); Assert.IsTrue(older.HasUnknownCreateResult);
    }

    /// <summary>多个弹窗共享在途保护，原请求未返回前不能另发重试，完成后仍沿用原身份。</summary>
    [TestMethod]
    public async Task AnotherWindow_CannotRetryWhileOriginalIsInFlight()
    {
        var pending = new TaskCompletionSource<ServiceResult<CreatePurchaseDraftsResult>>();
        var api = new DraftApi { Handler = _ => pending.Task }; var session = new PurchaseDraftPendingSession();
        using var first = Create(api, pendingSession: session); await Open(first, Row());
        var sending = first.CreateCommand.ExecuteAsync(null); Assert.IsFalse(first.CanClose);
        using var reopened = Create(api, pendingSession: session); reopened.OpenSession();
        await reopened.CreateCommand.ExecuteAsync(null); Assert.AreEqual(1, api.Commands.Count);
        StringAssert.Contains(reopened.StatusText, "原请求正在发送");
        pending.SetResult(SourceFailure(api.Commands[0], ServiceErrorCodes.OutcomeUnknown)); await sending;
        api.Handler = c => Task.FromResult(Success(c)); await reopened.CreateCommand.ExecuteAsync(null);
        Assert.AreEqual(2, api.Commands.Count); Assert.AreEqual(api.Commands[0].ExecutionRequestId, api.Commands[1].ExecutionRequestId);
        Assert.IsNull(session.Find("draft-user"));
    }

    /// <summary>真实未知状态支持点击原请求重试、保留关闭及重开恢复，不自动发送或显示本地校验完成。</summary>
    [TestMethod]
    public void UnknownUi_RetriesClosesAndReopensOriginalRequest() => Controls.DatePickerAssistTests.RunWithApplication(async () =>
    {
        TestInfrastructure.ApplicationResourceLoader.Load();
        var api = new DraftApi { Handler = command => Task.FromResult(SourceFailure(command, ServiceErrorCodes.OutcomeUnknown)) };
        var store = new PurchaseDraftPendingSession();
        using var vm = Create(api, pendingSession: store); using var picker = new PurchaseCandidateSelectionViewModel(api, new DirectApiCallRunner());
        using var container = new PurchaseOrderFromMaterialViewModel(picker, vm);
        await container.OpenAsync(); var row = Row(); row.IsChecked = true;
        await vm.UpdateSelectionAsync(new() { Rows = [row] });
        var view = new SwiftExportClient.Features.Procurement.PurchaseOrders.Views.PurchaseOrderFromMaterialView { DataContext = container };
        var window = new Window { Content = view, Width = 1600, Height = 1000, Left = -5000, Top = -5000, ShowActivated = false, ShowInTaskbar = false };
        try
        {
            window.Show(); await vm.CreateCommand.ExecuteAsync(null); view.UpdateLayout(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.IsTrue(vm.HasUnknownCreateResult); Assert.IsFalse(vm.HasValidPreparation); Assert.IsTrue(container.CanClose);
            Assert.IsFalse(picker.SelectionValidationText.Contains("本地输入校验完成")); Assert.IsFalse(picker.IsInputEnabled);
            var retry = Descendants(view).OfType<Button>().Single(x => Equals(x.Content, "重试原提交"));
            Assert.IsTrue(retry.IsEnabled); Assert.IsTrue(retry.IsVisible);
            var original = JsonSerializer.Serialize(api.Commands.Single()); var id = vm.ExecutionRequestId;
            ((IInvokeProvider)new ButtonAutomationPeer(retry).GetPattern(PatternInterface.Invoke)!).Invoke();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            if (vm.CreateCommand.ExecutionTask is { } task) await task;
            Assert.AreEqual(2, api.Commands.Count); Assert.AreEqual(2, vm.AttemptCount);
            Assert.AreEqual(original, JsonSerializer.Serialize(api.Commands.Last()));
            StringAssert.Contains(vm.AttemptText, "2 次"); StringAssert.Contains(vm.RequestIdentityText, id.ToString("D"));
            StringAssert.Contains(vm.RecoveryHint, "关闭不会取消服务端操作");
            container.Close(); Assert.IsNotNull(store.Find("draft-user"));
            using var reopened = Create(api, pendingSession: store); using var restoredPicker = new PurchaseCandidateSelectionViewModel(api, new DirectApiCallRunner());
            using var restoredContainer = new PurchaseOrderFromMaterialViewModel(restoredPicker, reopened);
            await restoredContainer.OpenAsync(); Assert.AreEqual(2, api.Commands.Count, "恢复不能自动调用创建。");
            Assert.AreEqual(id, reopened.ExecutionRequestId); Assert.AreEqual(row.Uid, restoredPicker.Items.Single().Uid);
            Assert.AreEqual(row.PurchaseQty, restoredPicker.Items.Single().PurchaseQty); Assert.AreEqual(row.UnitPrice, restoredPicker.Items.Single().UnitPrice);
            Assert.IsFalse(restoredPicker.IsInputEnabled); Assert.IsTrue(restoredContainer.CanClose);
            api.Handler = command => Task.FromResult(Success(command)); await reopened.CreateCommand.ExecuteAsync(null);
            Assert.AreEqual(original, JsonSerializer.Serialize(api.Commands.Last())); Assert.IsNull(store.Find("draft-user"));
            Assert.AreEqual(PurchaseOrderFromMaterialStage.Result, restoredContainer.Stage);
        }
        finally { window.Close(); }
    });

    /// <summary>已核实回滚的来源级WRITE_FAILED能消解未知，但混合其他未知来源时仍保留整个原请求。</summary>
    [TestMethod]
    [DataRow(false)][DataRow(true)]
    public async Task WriteFailed_IsDefiniteOnlyForThatSource(bool anotherUnknown)
    {
        var api = new DraftApi { Handler = c => Task.FromResult(SourceFailure(c, ServiceErrorCodes.OutcomeUnknown)) };
        var store = new PurchaseDraftPendingSession(); using var vm = Create(api, pendingSession: store);
        await Open(vm, anotherUnknown ? [Row(), Row()] : [Row()]); await vm.CreateCommand.ExecuteAsync(null);
        var id = vm.ExecutionRequestId;
        api.Handler = c => { var result = SourceFailure(c, ServiceErrorCodes.WriteFailed); if (anotherUnknown) result.Data.Sources[1].ErrorCode = ServiceErrorCodes.OutcomeUnknown; return Task.FromResult(result); };
        await vm.CreateCommand.ExecuteAsync(null);
        Assert.AreEqual(anotherUnknown, vm.HasUnknownCreateResult); Assert.IsTrue(vm.CanClose); Assert.AreEqual(anotherUnknown, store.Find("draft-user") is not null);
        Assert.AreEqual(id, vm.LastSubmittedRequestId);
        if (anotherUnknown) Assert.AreEqual(id, vm.ExecutionRequestId); else Assert.AreNotEqual(id, vm.ExecutionRequestId);
        Assert.AreEqual(anotherUnknown ? 2 : 1, vm.Items.Count); Assert.AreEqual(!anotherUnknown, vm.CanEditInputs);
        StringAssert.Contains(vm.ResultSummary, "WRITE_FAILED");
    }

    /// <summary>恢复时撤权仍可安全关闭；恢复不能覆盖当前只读状态。</summary>
    [TestMethod]
    public async Task RecoveredRequest_RespectsCurrentCreatePermissionAndReadOnlyState()
    {
        var api = new DraftApi { Handler = c => Task.FromResult(SourceFailure(c, ServiceErrorCodes.OutcomeUnknown)) }; var store = new PurchaseDraftPendingSession();
        using var first = Create(api, pendingSession: store); await Open(first, Row()); await first.CreateCommand.ExecuteAsync(null);
        var user = new CurrentUserStub(true) { CreateAllowed = false }; using var restored = Create(api, pendingSession: store, user: user);
        restored.OpenSession(); Assert.IsTrue(restored.HasUnknownCreateResult); Assert.IsTrue(restored.CanClose);
        Assert.IsFalse(restored.CreateCommand.CanExecute(null)); user.CreateAllowed = true;
        typeof(SwiftExportClient.Mvvm.ViewModelBaseEx).GetProperty(nameof(restored.IsReadOnly))!.SetValue(restored, true);
        Assert.IsFalse(restored.CreateCommand.CanExecute(null)); Assert.IsTrue(restored.CanClose); Assert.AreEqual(1, api.Commands.Count);
    }

    /// <summary>同一实例切换账号重开时不能恢复原账号数据，原账号应用内会话仍保留。</summary>
    [TestMethod]
    public async Task AccountSwitch_ReopensOwnEnvironmentWithoutDiscardingOriginalRecord()
    {
        var api = new DraftApi { Handler = c => Task.FromResult(SourceFailure(c, ServiceErrorCodes.OutcomeUnknown)) }; var store = new PurchaseDraftPendingSession(); var user = new CurrentUserStub(true);
        using var vm = Create(api, pendingSession: store, user: user); await Open(vm, Row()); await vm.CreateCommand.ExecuteAsync(null); var originalId = vm.ExecutionRequestId;
        user.SetCurrentUser(new MeDto { UserId = "new-user", Permissions = [PermissionKeys.ProcurementOrderCreate] });
        Assert.AreEqual(0, vm.Items.Count); Assert.AreEqual(string.Empty, vm.RequestIdentityText);
        vm.OpenSession(); Assert.AreEqual(0, vm.Items.Count); Assert.IsFalse(vm.HasUnknownCreateResult);
        Assert.AreNotEqual(originalId, vm.ExecutionRequestId); Assert.AreEqual(originalId, (store.Find("draft-user"))!.ExecutionRequestId);
        Assert.IsNull(store.Find("new-user")); Assert.AreEqual(1, api.Commands.Count);
    }

    /// <summary>客供件采购方与送达方、价格、数量和交期在重开后逐项保持，不重新采用推荐或丢失私有setter字段。</summary>
    [TestMethod]
    public async Task Recovery_PreservesCustomerSuppliedDeliveryAndCommercialInputs()
    {
        var api = new DraftApi { Handler = c => Task.FromResult(SourceFailure(c, ServiceErrorCodes.OutcomeUnknown)) }; var store = new PurchaseDraftPendingSession();
        var dto = new PurchaseDraftCandidateDto { Uid = Guid.NewGuid(), Version = 3, SourceType = "SO", SourceUid = Guid.NewGuid(), SourceNo = "SO-DELIVERY",
            CreatableQty = 5, IsCustomerSupplied = true, SuggestedPurchaseSupplierId = Guid.NewGuid(), SuggestedPurchaseSupplierCode = "P", SuggestedPurchaseSupplierName = "采购方",
            SuggestedDeliverySupplierId = Guid.NewGuid(), SuggestedDeliverySupplierCode = "D", SuggestedDeliverySupplierName = "送达方" };
        var row = PurchaseCandidateItemModel.FromDto(dto); row.PurchaseQty = 2; row.UnitPrice = 7.125m; row.TaxPrice = 8.345m; row.PlannedDeliveryDate = new DateTime(2026, 11, 3);
        using var first = Create(api, pendingSession: store); await Open(first, row); await first.CreateCommand.ExecuteAsync(null); first.Close();
        using var restored = Create(api, pendingSession: store); restored.OpenSession();
        var recovered = restored.RecoveryRows.Single(); var item = restored.Items.Single();
        Assert.AreEqual(row.SupplierId, recovered.SupplierId); Assert.AreEqual(row.DeliveryTargetSupplierId, recovered.DeliveryTargetSupplierId);
        Assert.AreEqual("D", recovered.DeliverySupplierCode); Assert.AreEqual("送达方", recovered.DeliverySupplierName);
        Assert.AreEqual(row.DeliveryTargetSupplierId, item.DeliveryTargetSupplierId); Assert.AreEqual(row.SupplierId, item.SupplierId);
        Assert.AreEqual(row.PurchaseQty, recovered.PurchaseQty); Assert.AreEqual(row.UnitPrice, recovered.UnitPrice); Assert.AreEqual(row.TaxPrice, recovered.TaxPrice);
        Assert.AreEqual(row.PlannedDeliveryDate, recovered.PlannedDeliveryDate);
    }

    private static ServiceResult<CreatePurchaseDraftsResult> SourceFailure(CreatePurchaseDraftsCommand command, string code)
    {
        var result = Success(command);
        foreach (var source in result.Data.Sources) { source.Outcome = NetDemandCommandOutcomeEnum.Failed; source.ErrorCode = code; source.Message = code == ServiceErrorCodes.WriteFailed ? "本来源未提交，已回滚。" : "采购创建结果待核实，请保留原执行请求和输入重试。"; source.PurchaseDrafts.Clear(); }
        return result;
    }
}
