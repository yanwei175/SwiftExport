using SwiftExport.Contracts.DTOs.Inventory.Inbounds.Sources;
using SwiftExport.Contracts.DTOs.Inventory.Inbounds.Query;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.DTOs.Inventory.WareHouseSite.Picker;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExportClient.Features.Inventory.Inbounds.Models;
using SwiftExportClient.Features.Inventory.Inbounds.Services;
using SwiftExportClient.Features.Inventory.Inbounds.ViewModels;
using SwiftExportClient.Features.Inventory.WarehouseSites.Services;
using SwiftExportClient.Services.UI;

namespace SwiftExport.Client.Wpf.Tests.Features;

/// <summary>验证入库管理本地模型、仓库回填和未接后端边界。</summary>
[TestClass]
public sealed class InboundManagerTests
{
    /// <summary>编辑状态随真实单据资格更新，冲突阻止仓库和获取操作，显示变化不污染草稿。</summary>
    [TestMethod]
    public void EditingStatus_FollowsDraftAndReadonlyBoundaries()
    {
        using var vm = new InboundManagerViewModel(new WarehouseStub(), new SourceStub(), new DialogStub(), postingUser: InboundTestUsers.Create());
        vm.AddNewHeaderRowCommand.Execute(null);
        var row = vm.SelectedItem!;
        Assert.AreEqual("新建草稿，可编辑", row.EditingStatus);
        Assert.IsFalse(row.CanChangeDocumentType);
        row.Load(new InboundHeaderDetailDto { Uid = row.Uid, Version = 1 });
        Assert.AreEqual("已保存草稿，可编辑", row.EditingStatus);
        Assert.IsTrue(vm.OpenInboundSourceDialogCommand.CanExecute(null));
        var notifications = new List<string?>();
        row.PropertyChanged += (_, e) => notifications.Add(e.PropertyName);
        row.HasVersionConflict = true;
        Assert.AreEqual("版本冲突，请刷新", row.EditingStatus);
        Assert.IsFalse(row.CanEditInPage);
        Assert.IsFalse(vm.OpenWarehousePickerCommand.CanExecute(row));
        Assert.IsFalse(vm.OpenInboundSourceDialogCommand.CanExecute(null));
        CollectionAssert.Contains(notifications, nameof(InboundHeaderModel.EditingStatus));
        Assert.IsFalse(row.IsDirty);
        row.HasVersionConflict = false;
        row.IsPersistencePending = true;
        Assert.AreEqual("结果待恢复", row.EditingStatus);
        Assert.IsFalse(row.CanEditInPage);
        row.IsPersistencePending = false;
        row.PageAllowsEditing = false;
        Assert.AreEqual("当前页面不可编辑", row.EditingStatus);
        Assert.IsFalse(row.CanEditInPage);
        Assert.IsFalse(row.IsDirty);
        row.PostingStatus = InboundPostingStatusEnum.Posted;
        Assert.AreEqual("已过账，只读", row.EditingStatus);
        row.PostingStatus = InboundPostingStatusEnum.Reversed;
        Assert.AreEqual("已反过账，只读", row.EditingStatus);
        Assert.IsFalse(row.CanEdit);
    }

    /// <summary>重复草稿信息仅用于提示，更新查询快照不能把已有行标记为修改。</summary>
    [TestMethod]
    public void DraftWarning_DoesNotReserveOrDirtyRow()
    {
        var row = new InboundLineModel();
        row.Load(new InboundLineDetailDto { Quantity = 10, RemainingReceivableQty = 1,
            ProductId = Guid.NewGuid(), SourceLineId = Guid.NewGuid(), UnitSnapshot = "PCS" });
        Assert.AreEqual(string.Empty, row.OtherDraftWarning);
        row.OtherDraftCount = 2; row.OtherDraftQuantity = 30;
        StringAssert.Contains(row.OtherDraftWarning, "2 张");
        Assert.IsFalse(row.IsDirty);
        Assert.IsNull(row.ValidateRow());
    }

    /// <summary>空数量可进入编辑模型并显示软错误，修正后清除；非法文本仍是原生转换错误。</summary>
    [TestMethod]
    public void QuantityBinding_AllowsEmptyAndReportsSoftValidation()
    {
        SwiftExport.Client.Wpf.Tests.TestInfrastructure.StaTest.RunAsync(async () =>
        {
            var row = new InboundLineModel();
            row.Load(new InboundLineDetailDto { Quantity = 2, SourceLineId = Guid.NewGuid(), ProductId = Guid.NewGuid(), UnitSnapshot = "PCS" });
            var box = new System.Windows.Controls.TextBox();
            box.SetBinding(System.Windows.Controls.TextBox.TextProperty, new System.Windows.Data.Binding(nameof(row.Quantity))
            {
                Source = row, Mode = System.Windows.Data.BindingMode.TwoWay,
                TargetNullValue = string.Empty,
                UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged,
                ValidatesOnDataErrors = false, ValidatesOnNotifyDataErrors = false
            });
            await box.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.DataBind);
            Assert.AreEqual("2", box.Text);
            box.SetCurrentValue(System.Windows.Controls.TextBox.TextProperty, "");
            await box.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.DataBind);
            Assert.IsNull(row.Quantity);
            Assert.IsTrue(row.IsDirty);
            Assert.IsFalse(System.Windows.Controls.Validation.GetHasError(box));
            Assert.AreEqual("请输入本次入库数量。", row[nameof(row.Quantity)]);
            Assert.IsNotNull(row.ValidateRow());
            box.SetCurrentValue(System.Windows.Controls.TextBox.TextProperty, "1.2345");
            await box.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.DataBind);
            Assert.AreEqual(1.2345m, row.Quantity);
            Assert.AreEqual(string.Empty, row[nameof(row.Quantity)]);
            Assert.IsNull(row.ValidateRow());
            Assert.AreEqual(1.2345m, row.ToDto().Quantity);
            box.SetCurrentValue(System.Windows.Controls.TextBox.TextProperty, "abc");
            await box.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.DataBind);
            Assert.IsTrue(System.Windows.Controls.Validation.GetHasError(box));
        });
    }

    /// <summary>确认来源直接回填，重复获取不覆盖已编辑数量，也不把来源版本当入库行版本。</summary>
    [TestMethod]
    public async Task SourceSelection_MapsSnapshotAndSkipsDuplicates()
    {
        var source = new SourceStub();
        using var vm = new InboundManagerViewModel(new WarehouseStub(), source, new DialogStub(), postingUser: InboundTestUsers.Create());
        vm.AddNewHeaderRowCommand.Execute(null);
        var header = vm.SelectedItem!;
        source.Result = [new InboundSourceCandidateDto { DocumentType = InboundDocumentTypeEnum.PurchaseReceipt, SourceLineId = Guid.NewGuid(), SourceVersion = 9, ProductId = Guid.NewGuid(), Sku = "SKU-A", Unit = "PCS", RemainingReceivableQty = 10 }];
        await vm.OpenInboundSourceDialogCommand.ExecuteAsync(null);
        Assert.AreEqual(1, header.Lines.Items.Count);
        var line = header.Lines.Items[0];
        Assert.AreEqual(0m, line.Quantity);
        Assert.AreEqual(9, line.SourceVersion);
        Assert.AreEqual(0, line.Version);
        Assert.AreEqual("SKU-A", line.SkuSnapshot);
        Assert.IsFalse(header.CanChangeDocumentType);
        line.Quantity = 3;
        await vm.OpenInboundSourceDialogCommand.ExecuteAsync(null);
        Assert.AreEqual(1, header.Lines.Items.Count);
        Assert.AreEqual(3m, line.Quantity);
    }

    /// <summary>下拉枚举保持类型值且排除历史分类；设计时数据提供真实头行绑定。</summary>
    [TestMethod]
    public void StaticTypesAndDesignData_AreAvailableWithoutServices()
    {
        var types = SwiftExport.Contracts.SysEnums.StaticEnums.InboundDocumentTypes.All;
        CollectionAssert.AreEqual(new[] { InboundDocumentTypeEnum.PurchaseReceipt, InboundDocumentTypeEnum.ProductionReceipt }, types.ToArray());
        var design = new InboundManagerDesignViewModel();
        Assert.AreEqual(2, design.Headers.Items.Count);
        Assert.AreEqual(2, design.Lines!.Items.Count);
        Assert.AreEqual(30m, design.SelectedItem!.TotalQuantity);
        design.SelectedDocumentType = InboundDocumentTypeEnum.ProductionReceipt;
        Assert.AreEqual(InboundDocumentTypeEnum.ProductionReceipt, design.SelectedDocumentType);
    }

    /// <summary>头行映射保留身份快照，勾选不算业务修改，数量编辑可追踪。</summary>
    [TestMethod]
    public void Models_RoundTripAndTrackOnlyEdits()
    {
        var sourceId = Guid.NewGuid();
        var row = new InboundLineModel();
        row.Load(new InboundLineDetailDto { Uid = Guid.NewGuid(), Version = 4, SourceLineId = sourceId, ProductId = Guid.NewGuid(), UnitSnapshot = "PCS", Quantity = 2, RemainingReceivableQty = 10 });
        row.IsChecked = true;
        Assert.IsFalse(row.IsDirty);
        row.Quantity = 3;
        Assert.IsTrue(row.IsDirty);
        Assert.AreEqual(sourceId, row.ToDto().SourceLineId);
        Assert.AreEqual(4, row.ToDto().Version);
        Assert.IsNull(row.ValidateRow());
        row.Quantity = 11;
        Assert.IsNull(row.ValidateRow()); // 保存不使用可收快照做容量判断。
        row.Quantity = 0;
        Assert.IsNotNull(row.ValidateRow());
        row.Quantity = 1.00001m;
        Assert.IsNotNull(row.ValidateRow());
        var header = new InboundHeaderModel();
        header.Load(new InboundHeaderDetailDto { WarehouseSiteId = Guid.NewGuid(), WarehouseNameSnapshot = "仓库 A" });
        Assert.AreEqual("仓库 A", header.ToDto().WarehouseNameSnapshot);
        Assert.IsTrue(header.CanEdit);
        header.CreationMode = InboundCreationModeEnum.Automatic;
        Assert.IsFalse(header.CanEdit);
    }

    /// <summary>选择成功只回填当前单；取消不清仓库，切换单头保留独立明细。</summary>
    [TestMethod]
    public async Task WarehousePicker_CancelPreservesSelection()
    {
        var picker = new WarehouseStub { Result = new WarehouseSitePickerDto { Uid = Guid.NewGuid(), Code = "W1", Name = "仓库 A", IsActive = true } };
        using var vm = new InboundManagerViewModel(picker, new SourceStub(), new DialogStub(), postingUser: InboundTestUsers.Create());
        vm.AddNewHeaderRowCommand.Execute(null);
        var first = vm.SelectedItem!;
        await vm.OpenWarehousePickerCommand.ExecuteAsync(first);
        Assert.AreEqual(picker.Result.Uid, first.WarehouseSiteId);
        var warehouseId = first.WarehouseSiteId;
        picker.Result = null;
        await vm.OpenWarehousePickerCommand.ExecuteAsync(first);
        Assert.AreEqual(warehouseId, first.WarehouseSiteId);
        first.Lines.AddNew(new InboundLineDetailDto { Quantity = 5 });
        vm.AddNewHeaderRowCommand.Execute(null);
        Assert.AreEqual(0, vm.Lines!.Items.Count);
        vm.SelectedItem = first;
        Assert.AreEqual(5m, first.TotalQuantity);
        Assert.AreEqual(1, vm.Lines!.Items.Count);
        Assert.IsTrue(vm.HasUnsavedChanges);
        Assert.IsFalse(vm.SaveCommand.CanExecute(null));
        Assert.IsFalse(vm.PostCommand.CanExecute(null));
    }

    /// <summary>来源壳关闭不会增加明细，拒绝关闭时保留本地草稿。</summary>
    [TestMethod]
    public async Task SourceShell_DoesNotFabricateRows()
    {
        var source = new SourceStub();
        using var vm = new InboundManagerViewModel(new WarehouseStub(), source, new DialogStub(), postingUser: InboundTestUsers.Create());
        vm.AddNewHeaderRowCommand.Execute(null);
        await vm.OpenInboundSourceDialogCommand.ExecuteAsync(null);
        Assert.AreEqual(1, source.OpenCount);
        Assert.AreEqual(0, vm.Lines!.Items.Count);
        Assert.IsFalse(await vm.CanCloseAsync(CancellationToken.None));
    }

    /// <summary>模拟仓库确认与取消，不连接缓存或网络。</summary>
    private sealed class WarehouseStub : IWarehousePickerService
    {
        public WarehouseSitePickerDto? Result { get; set; }
        public Task<WarehouseSitePickerDto?> PickAsync(CancellationToken cancellationToken = default) => Task.FromResult(Result);
    }

    /// <summary>记录来源壳打开，不提供虚假采购数据。</summary>
    private sealed class SourceStub : IInboundSourceDialogService
    {
        public int OpenCount { get; private set; }
        public IReadOnlyList<InboundSourceCandidateDto>? Result { get; set; }
        public Task<IReadOnlyList<InboundSourceCandidateDto>?> PickAsync(InboundSourcePickerRequest request, CancellationToken cancellationToken = default)
        { OpenCount++; return Task.FromResult(Result); }
    }

    /// <summary>默认拒绝放弃编辑的测试弹窗。</summary>
    private sealed class DialogStub : IDialogService
    {
        public Task ShowContentAsync(string title, object content, string closeButtonText = "关闭", CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> ConfirmAsync(string title, string message, string primaryButtonText = "确定", string closeButtonText = "取消", CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task ShowErrorAsync(string title, string message, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
