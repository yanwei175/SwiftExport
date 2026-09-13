using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.DTOs.Inventory.Inbounds;
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
    /// <summary>确认来源直接回填，重复获取不覆盖已编辑数量，也不把来源版本当入库行版本。</summary>
    [TestMethod]
    public async Task SourceSelection_MapsSnapshotAndSkipsDuplicates()
    {
        var source = new SourceStub();
        using var vm = new InboundManagerViewModel(new WarehouseStub(), source, new DialogStub());
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
        using var vm = new InboundManagerViewModel(picker, new SourceStub(), new DialogStub());
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
        Assert.IsFalse(vm.AddNewLineRowCommand.CanExecute(null));
    }

    /// <summary>来源壳关闭不会增加明细，拒绝关闭时保留本地草稿。</summary>
    [TestMethod]
    public async Task SourceShell_DoesNotFabricateRows()
    {
        var source = new SourceStub();
        using var vm = new InboundManagerViewModel(new WarehouseStub(), source, new DialogStub());
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
