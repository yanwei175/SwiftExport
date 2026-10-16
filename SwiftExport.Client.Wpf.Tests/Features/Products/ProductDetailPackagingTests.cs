using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Client.Wpf.Tests.Controls;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Products.Responses;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Features.Products.ProductList.Models;
using SwiftExportClient.Features.Products.ProductList.ViewModels;
using SwiftExportClient.Features.Products.ProductList.Views;

namespace SwiftExport.Client.Wpf.Tests.Features.Products;

/// <summary>包装子区块与父详情的脏状态、请求、撤销、引用修复和真实 WPF 原生编辑验证，不访问数据库。</summary>
[TestClass, DoNotParallelize]
public sealed class ProductDetailPackagingTests
{
    /// <summary>包装类型常驻选择器保持字典代码回写、父页面编辑门禁和卸载绑定稳定。</summary>
    [TestMethod]
    public void PackageTypeEditor_EditLifecycleKeepsDictionaryBinding() => DatePickerAssistTests.RunWithApplication(async () =>
    {
        ApplicationResourceLoader.Load();
        PresentationTraceSources.Refresh();
        using var output = new StringWriter();
        using var listener = new TextWriterTraceListener(output);
        var trace = PresentationTraceSources.DataBindingSource;
        var previousLevel = trace.Switch.Level;
        trace.Switch.Level = SourceLevels.Error;
        trace.Listeners.Add(listener);
        var cache = new ReferenceDataCacheServiceStub(dictionaryValues:
        [
            new SysDictDataDto { DictCode = DictCodes.ProductPackageType, DataNameEn = ProductPackageTypeCodes.Carton, DataNameZh = "外箱", IsEnabled = true },
            new SysDictDataDto { DictCode = DictCodes.ProductPackageType, DataNameEn = ProductPackageTypeCodes.InnerBox, DataNameZh = "内盒", IsEnabled = true }
        ]);
        using var vm = new ProductPackagingEditorViewModel(null!, null!, cache) { CanEdit = true };
        var data = Package(Guid.NewGuid()); vm.Load(data.OwnerProductId, [data]);
        var window = new Window { Width = 1000, Height = 480, ShowInTaskbar = false };
        try
        {
            var view = new ProductPackagingEditorView { DataContext = vm };
            window.Content = view; window.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var grid = (DataGrid)view.FindName("PackageGrid");
            var row = vm.Rows.Items.Single();
            Assert.AreEqual(ProductPackageTypeCodes.Carton, row.PackageType);
            Assert.IsFalse(vm.HasChanges, "加载常驻字典选择器不能改写原值或制造脏状态。");
            // 模板实例在加入单元格之前也必须能解析页面字典，避免初始化阶段查找不存在的 DataGrid 祖先。
            var detachedEditor = (ComboBox)((DataGridTemplateColumn)grid.Columns[1]).CellTemplate.LoadContent();
            detachedEditor.DataContext = row;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.AreEqual(2, detachedEditor.Items.Count);
            for (var attempt = 0; attempt < 2; attempt++)
            {
                grid.SelectedItem = row; grid.CurrentCell = new DataGridCellInfo(row, grid.Columns[1]);
                Assert.IsTrue(grid.BeginEdit());
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                var combo = Descendants(grid.Columns[1].GetCellContent(row)).OfType<ComboBox>().Single();
                CollectionAssert.AreEqual(new[] { ProductPackageTypeCodes.Carton, ProductPackageTypeCodes.InnerBox },
                    combo.Items.Cast<SysDictDataDto>().Select(item => item.DataNameEn).ToArray());
                combo.IsDropDownOpen = true;
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                combo.SelectedIndex = 1; combo.IsDropDownOpen = false;
                Assert.IsTrue(grid.CommitEdit(DataGridEditingUnit.Cell, true));
                Assert.IsTrue(grid.CommitEdit(DataGridEditingUnit.Row, true));
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Assert.AreEqual(ProductPackageTypeCodes.InnerBox, row.PackageType);
                vm.CanEdit = false;
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Assert.IsFalse(combo.IsEnabled, "常驻显示模板的选择器也必须遵守父页面只读和忙碌门禁。");
                vm.CanEdit = true;
                // 常驻选择器继续使用页面代理；刷新与卸载都不依赖 DataGrid 祖先。
                combo.GetBindingExpression(ItemsControl.ItemsSourceProperty)!.UpdateTarget();
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            }
            window.Close();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            listener.Flush();
            Assert.IsFalse(output.ToString().Contains("PackageTypes", StringComparison.Ordinal), output.ToString());
        }
        finally
        {
            window.Close();
            trace.Listeners.Remove(listener); trace.Switch.Level = previousLevel;
        }
    });

    /// <summary>仅包装变化也启用父保存并纳入关闭保护；保存回执一次接纳父、行版本及外箱摘要。</summary>
    [TestMethod]
    public async Task PackagingOnlyEdit_UsesUnifiedSaveAndAcceptsCompleteSnapshot()
    {
        var detail = ProductDetailViewModelTests.Detail(); detail.Packages = [];
        SaveProductDetailRequest? sent = null;
        using var vm = ProductDetailViewModelTests.Create(detail, null, save: request =>
        {
            sent = request;
            var accepted = ProductDetailViewModelTests.Detail(); accepted.Uid = detail.Uid; accepted.Version = 8;
            accepted.PcsOfCarton = 24;
            accepted.Packages = [new() { Uid = Guid.NewGuid(), OwnerProductId = detail.Uid, Version = 1, Data = request.PackagingChanges.CreateItems.Single().Data, IsPackagingMaterialValid = true }];
            return Task.FromResult(ServiceResult<SaveProductDetailResponse>.Ok(new() { Detail = accepted }));
        });
        await ProductDetailViewModelTests.Open(vm, detail.Uid);
        Assert.IsFalse(vm.Header.IsDirty); vm.Packaging.AddRowCommand.Execute(null);
        var row = vm.Packaging.Rows.Items.Single(); row.PackageType = ProductPackageTypeCodes.ShippingBox;
        Assert.AreEqual(1, row.PcsPerPackage); row.PackageType = ProductPackageTypeCodes.Carton; row.PcsPerPackage = 24;
        vm.Packaging.SelectedMaterial = new() { Uid = Guid.NewGuid(), Sku = "BOX", DescriptionZh = "纸箱", Unit = "Pcs", MaterialType = MaterialTypesEnum.包材 };
        vm.Packaging.CommitMaterialCommand.Execute(row);
        Assert.IsNull(row.Nw_KG); Assert.IsNull(row.Cbm);
        Assert.IsTrue(vm.HasUnsavedChanges); Assert.IsTrue(vm.SaveCommand.CanExecute(null));
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.IsNotNull(sent); Assert.IsNull(sent.SelectedOptionIds); Assert.HasCount(1, sent.PackagingChanges.CreateItems);
        Assert.AreEqual(24, sent.PackagingChanges.CreateItems.Single().Data.PcsPerPackage);
        Assert.IsFalse(vm.HasUnsavedChanges); Assert.AreEqual(8, vm.Header.Version);
        Assert.AreEqual(1, vm.Packaging.Rows.Items.Single().Version); Assert.AreEqual(24m, vm.Source!.PcsOfCarton);
    }

    /// <summary>区块未加载或未编辑时保持协议为 null，不能把默认空列表误当整表删除。</summary>
    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task UnloadedOrUntouchedPackages_SendNullAndKeepRows(bool loaded)
    {
        var detail = ProductDetailViewModelTests.Detail(); detail.Packages = loaded ? [Package(detail.Uid)] : null!;
        SaveProductDetailRequest? sent = null;
        using var vm = ProductDetailViewModelTests.Create(detail, null, save: request => { sent = request; return Task.FromResult(ServiceResult<SaveProductDetailResponse>.Fail("保留")); });
        await ProductDetailViewModelTests.Open(vm, detail.Uid); vm.Header.Note = "资料";
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.IsNotNull(sent); Assert.IsNull(sent.PackagingChanges);
        Assert.AreEqual(loaded, vm.Packaging.IsLoaded);
        if (!loaded) Assert.IsFalse(vm.Packaging.AddRowCommand.CanExecute(null));
    }

    /// <summary>版本冲突和失败保留包装草稿、父预期版本及原行版本，显式删除只发送当前行身份。</summary>
    [TestMethod]
    public async Task ConflictAndDelete_KeepDraftAndVersion()
    {
        var detail = ProductDetailViewModelTests.Detail(); detail.Packages = [Package(detail.Uid)];
        SaveProductDetailRequest? sent = null;
        using var vm = ProductDetailViewModelTests.Create(detail, null, save: request => { sent = request; return Task.FromResult(ServiceResult<SaveProductDetailResponse>.Fail(ServiceErrorCodes.VersionConflict, "冲突")); });
        await ProductDetailViewModelTests.Open(vm, detail.Uid); var row = vm.Packaging.Rows.Items.Single(); row.Note = "草稿";
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.IsNotNull(sent); Assert.AreEqual(row.Uid, sent.PackagingChanges.UpdateItems.Single().Uid);
        Assert.AreEqual(3, sent.PackagingChanges.UpdateItems.Single().Version); Assert.AreEqual(7, vm.Header.Version); Assert.IsTrue(row.IsDirty);
        vm.Packaging.IsCellSelectionMode = true; row.IsChecked = true;
        vm.Packaging.DeleteRowsCommand.Execute(null); Assert.HasCount(1, vm.Packaging.Rows.Items);
        vm.Packaging.IsCellSelectionMode = false; vm.Packaging.DeleteRowsCommand.Execute(null);
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.HasCount(1, sent!.PackagingChanges.DeleteItems); Assert.HasCount(0, sent.PackagingChanges.UpdateItems);
        Assert.AreEqual(3, sent.PackagingChanges.DeleteItems.Single().Version); Assert.IsTrue(vm.HasUnsavedChanges);
        vm.RejectDetailChangesCommand.Execute(null); Assert.HasCount(1, vm.Packaging.Rows.Items); Assert.IsFalse(vm.HasUnsavedChanges);
    }

    /// <summary>改选/清除后撤销恢复同一身份的展示基线，包括原先失效的包材；展示本身不制造业务脏状态。</summary>
    [TestMethod]
    [DataRow(false, false)] [DataRow(false, true)] [DataRow(true, false)] [DataRow(true, true)]
    public void RevertMaterial_RestoresIdentityAndReadOnlyPresentation(bool broken, bool clear)
    {
        var dto = Package(Guid.NewGuid()); dto.IsPackagingMaterialValid = !broken;
        using var vm = new ProductPackagingEditorViewModel(null!, null!, null) { CanEdit = true };
        vm.Load(dto.OwnerProductId, [dto]); var row = vm.Rows.Items.Single();
        if (clear) vm.ClearMaterialCommand.Execute(row);
        else
        {
            vm.SelectedMaterial = new() { Uid = Guid.NewGuid(), Sku = "B", DescriptionZh = "包材B", Unit = "SET", MaterialType = MaterialTypesEnum.包材 };
            vm.CommitMaterialCommand.Execute(row);
        }
        Assert.IsTrue(vm.HasChanges); vm.RejectChangesCommand.Execute(null);
        Assert.AreEqual(dto.Data.PackagingMaterialId, row.PackagingMaterialId);
        Assert.AreEqual("A", row.PackagingMaterialSku); Assert.AreEqual("包材A", row.PackagingMaterialDescription); Assert.AreEqual("Pcs", row.PackagingMaterialUnit);
        Assert.AreEqual(!broken, row.IsPackagingMaterialValid); Assert.AreEqual(broken, row.MaterialStatus.Contains("失效", StringComparison.Ordinal));
        Assert.IsFalse(vm.HasChanges); Assert.IsNull(vm.CreateChangesRequest());
    }

    /// <summary>原生包装单元格先提交给顶部统一保存；转换错误不能切换页签或丢失输入，撤销后可正常切换。</summary>
    [TestMethod]
    public void NativePackagingGrid_CommitsBuffersAndProtectsTabSwitch() => DatePickerAssistTests.RunWithApplication(async () =>
    {
        ApplicationResourceLoader.Load();
        var detail = ProductDetailViewModelTests.Detail(); detail.Packages = [Package(detail.Uid)]; SaveProductDetailRequest? sent = null;
        using var vm = ProductDetailViewModelTests.Create(detail, null, save: request => { sent = request; return Task.FromResult(ServiceResult<SaveProductDetailResponse>.Fail("测试保留")); });
        await ProductDetailViewModelTests.Open(vm, detail.Uid);
        var view = new ProductDetailView { DataContext = vm };
        var window = new Window { Content = view, Width = 1400, Height = 780, ShowInTaskbar = false };
        try
        {
            window.Show(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var tabs = (TabControl)view.FindName("DetailTabs"); tabs.SelectedIndex = 1;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var editorView = Descendants(view).OfType<ProductPackagingEditorView>().Single(); var grid = (DataGrid)editorView.FindName("PackageGrid");
            var row = vm.Packaging.Rows.Items.Single(); Assert.IsFalse(row.IsDirty); Assert.AreEqual("A", row.PackagingMaterialSku);
            grid.SelectedItem = row; grid.CurrentCell = new DataGridCellInfo(row, grid.Columns[6]);
            Assert.IsTrue(grid.BeginEdit()); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var input = Descendants(grid.Columns[6].GetCellContent(row)).OfType<TextBox>().Single(); input.Focus(); input.Text = "24";
            Assert.AreEqual(12, row.PcsPerPackage);
            await vm.SaveCommand.ExecuteAsync(null); Assert.AreEqual(24, sent!.PackagingChanges.UpdateItems.Single().Data.PcsPerPackage);
            Assert.AreEqual(7, vm.Header.Version); Assert.IsTrue(vm.HasUnsavedChanges);
            grid.CurrentCell = new DataGridCellInfo(row, grid.Columns[6]); Assert.IsTrue(grid.BeginEdit());
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            input = Descendants(grid.Columns[6].GetCellContent(row)).OfType<TextBox>().Single(); input.Focus(); input.Text = "不是整数";
            tabs.SelectedIndex = 0; await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.AreEqual(1, tabs.SelectedIndex); Assert.AreEqual("不是整数", input.Text);
            sent = null; await vm.SaveCommand.ExecuteAsync(null); Assert.IsNull(sent);
            vm.Packaging.RejectChangesCommand.Execute(null); Assert.AreEqual(12, row.PcsPerPackage);
            tabs.SelectedIndex = 0; await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); Assert.AreEqual(0, tabs.SelectedIndex);
        }
        finally { window.Close(); }
    });

    /// <summary>建立包含包材身份及只读展示的包装快照。</summary>
    private static ProductPackageDto Package(Guid owner) => new()
    {
        Uid = Guid.NewGuid(), OwnerProductId = owner, Version = 3,
        Data = new() { PackageType = ProductPackageTypeCodes.Carton, PcsPerPackage = 12, PackagingMaterialId = Guid.NewGuid(), Nw_KG = 10, Gw_KG = 11, Cbm = 0.2m },
        PackagingMaterialSku = "A", PackagingMaterialDescription = "包材A", PackagingMaterialUnit = "Pcs", IsPackagingMaterialValid = true
    };
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    { yield return root; for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var child in Descendants(VisualTreeHelper.GetChild(root, i))) yield return child; }
}
