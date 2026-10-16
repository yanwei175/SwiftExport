using SwiftExportClient.Mvvm;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Messaging;
using Mapster;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;
using SwiftExport.Client.Wpf.Tests.Controls;
using SwiftExport.Contracts.Apis.ProductApis;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExport.Contracts.DTOs.Products.Responses;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Authentication;
using SwiftExportClient.Features.Products.Bom.ViewModels;
using SwiftExportClient.Features.Products.Components.Models;
using SwiftExportClient.Features.Products.Components;
using SwiftExportClient.Features.Products.ProductList.Models;
using SwiftExportClient.Features.Products.ProductList.ViewModels;
using SwiftExportClient.Features.Products.ProductList.Views;
using SwiftExportClient.Navigation;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.UI;

namespace SwiftExport.Client.Wpf.Tests.Features.Products;

/// <summary>验证物料详情入口只处理目标行，以及详情保存后的定向查询不覆盖列表草稿。</summary>
[TestClass]
[DoNotParallelize]
public sealed class ProductDetailNavigationTests
{
    /// <summary>实际页面填充完整关系事实，拒绝危险字段、外来行和只读行；既有放弃修改恢复基线。</summary>
    [TestMethod]
    public void FillDown_ShouldKeepRelationsAndRejectUnsafeRequestsWithoutLosingBaseline()
    {
        using var fixture = new Fixture();
        var vm = fixture.ViewModel;
        var source = vm.Products.Items[0];
        var target = vm.Products.Items[1];
        source.ProductMasterId = Guid.NewGuid(); source.MasterSku = "MASTER";
        source.CategoryId = Guid.NewGuid(); source.CategoryName = "分类";
        source.DefaultSupplierId = Guid.NewGuid(); source.DefaultSupplierCode = "SUP"; source.SupplierName = "供应商";
        source.ColorId = Guid.NewGuid(); source.Finish = "颜色";
        source.ManufacturerId = Guid.NewGuid(); source.ManufacturerCode = "M"; source.ManufacturerName = "制造方";
        source.DefaultWarehouseSiteId = Guid.NewGuid(); source.DefaultWarehouseCode = "W"; source.DefaultWarehouseName = "仓库";
        vm.Products.AcceptChanges();
        var oldMaster = target.ProductMasterId;
        var oldSupplier = target.DefaultSupplierId;
        var request = new FillDownRequest(source, [target],
            [nameof(ProductLineModel.ProductMasterId), nameof(ProductLineModel.DefaultSupplierId), nameof(ProductLineModel.ColorId),
             nameof(ProductLineModel.ManufacturerId), nameof(ProductLineModel.DefaultWarehouseSiteId)]);
        Assert.IsTrue(vm.FillDownCommand.CanExecute(request));
        vm.FillDownCommand.Execute(request);
        Assert.AreEqual(source.ProductMasterId, target.ProductMasterId);
        Assert.AreEqual(source.MasterSku, target.MasterSku);
        Assert.AreEqual(source.CategoryId, target.CategoryId);
        Assert.AreEqual(source.CategoryName, target.CategoryName);
        Assert.AreEqual(source.DefaultSupplierId, target.DefaultSupplierId);
        Assert.AreEqual(source.DefaultSupplierCode, target.DefaultSupplierCode);
        Assert.AreEqual(source.SupplierName, target.SupplierName);
        Assert.AreEqual(source.ColorId, target.ColorId); Assert.AreEqual(source.Finish, target.Finish);
        Assert.AreEqual(source.ManufacturerId, target.ManufacturerId); Assert.AreEqual(source.ManufacturerName, target.ManufacturerName);
        Assert.AreEqual(source.DefaultWarehouseSiteId, target.DefaultWarehouseSiteId); Assert.AreEqual(source.DefaultWarehouseName, target.DefaultWarehouseName);
        Assert.IsTrue(target.IsDirty);
        var sku = target.Sku;
        var forbidden = new FillDownRequest(source, [target], [nameof(ProductLineModel.Sku)]);
        Assert.IsFalse(vm.FillDownCommand.CanExecute(forbidden));
        vm.FillDownCommand.Execute(forbidden);
        Assert.AreEqual(sku, target.Sku);
        Assert.IsFalse(vm.FillDownCommand.CanExecute(new FillDownRequest(source, [target], [nameof(ProductLineModel.MainImage)])));
        Assert.IsFalse(vm.FillDownCommand.CanExecute(new FillDownRequest(source, [new ProductLineModel()], request.Fields)));
        target.IsReadOnly = true;
        Assert.IsFalse(vm.FillDownCommand.CanExecute(request));
        source.DefaultSupplierCode = "不可写";
        vm.FillDownCommand.Execute(request);
        Assert.AreEqual("SUP", target.DefaultSupplierCode);
        target.IsReadOnly = false;
        typeof(ViewModelBaseEx).GetProperty(nameof(ViewModelBaseEx.IsReadOnly))!.SetValue(vm, true);
        vm.FillDownCommand.Execute(request);
        Assert.AreEqual("SUP", target.DefaultSupplierCode);
        typeof(ViewModelBaseEx).GetProperty(nameof(ViewModelBaseEx.IsReadOnly))!.SetValue(vm, false);
        vm.RejectProductChangesCommand.Execute(null);
        Assert.AreEqual(oldMaster, target.ProductMasterId);
        Assert.AreEqual(oldSupplier, target.DefaultSupplierId);
        Assert.IsFalse(vm.Products.HasChanges);
    }

    /// <summary>列表选择主型号并录入自身型号，保存目标行仍保留其他行及 BOM 草稿。</summary>
    [TestMethod]
    public async Task SourceLookups_ShouldSaveIndependentIdsAndPreserveOtherDrafts()
    {
        using var fixture = new Fixture { Choice = DialogResult.Primary, SaveSucceeds = true };
        var target = fixture.ViewModel.Products.Items[0];
        var other = fixture.ViewModel.Products.Items[1];
        var masterId = Guid.NewGuid(); var neutralId = Guid.NewGuid(); var colorId = Guid.NewGuid();
        target.ColorId = colorId;
        other.Note = "其他行草稿";
        fixture.ViewModel.BomDrawer.Boms.AddNewItem("保留 BOM").Note = "抽屉草稿";
        fixture.ViewModel.EditProductMaster = new(new() { Uid = masterId, Data = new() { MasterSku = "主型号" } });
        fixture.ViewModel.CommitProductMasterCommand.Execute(target);
        fixture.ViewModel.EditProductNeutral = new(new() { Uid = neutralId, Data = new() { ProductMasterId = Guid.NewGuid(), ProductCode = "独立中性产品" } });
        fixture.ViewModel.CommitProductNeutralCommand.Execute(target);
        Assert.AreEqual(colorId, target.ColorId);
        Assert.AreEqual(masterId, target.ProductMasterId);
        Assert.AreEqual("独立中性产品", target.LinkSku);
        await fixture.ViewModel.OpenDetailCommand.ExecuteAsync(null);
        Assert.IsNotNull(fixture.LastSave);
        Assert.AreEqual(1, fixture.LastSave.UpdateItems.Count);
        Assert.AreEqual(masterId, fixture.LastSave.UpdateItems[0].Data.ProductMasterId);
        Assert.AreEqual("独立中性产品", fixture.LastSave.UpdateItems[0].Data.LinkSku);
        Assert.AreEqual("其他行草稿", other.Note);
        Assert.IsTrue(other.IsDirty);
        Assert.IsTrue(fixture.ViewModel.BomDrawer.Boms.HasChanges);
    }

    /// <summary>失效主型号身份仍能回传，显式清空引用不影响自由填写的物料自身型号。</summary>
    [TestMethod]
    public void MissingSourceIds_ShouldRemainUntilExplicitClear()
    {
        using var fixture = new Fixture();
        var line = fixture.ViewModel.Products.Items[0];
        var masterId = Guid.NewGuid();
        line.Load(new() { Uid = line.Uid, Version = 1, Sku = "A", ProductMasterId = masterId, LinkSku = "自由型号" });
        Assert.IsFalse(line.IsDirty);
        Assert.AreEqual(string.Empty, line.MasterSku);
        Assert.AreEqual("自由型号", line.LinkSku);
        Assert.AreEqual(masterId, line.ToDto().ProductMasterId);
        Assert.AreEqual("自由型号", line.ToDto().LinkSku);
        fixture.ViewModel.ClearProductMasterCommand.Execute(line);
        Assert.IsNull(line.ToDto().ProductMasterId);
        Assert.AreEqual("自由型号", line.ToDto().LinkSku);
        fixture.ViewModel.ClearProductNeutralCommand.Execute(line);
        Assert.AreEqual(string.Empty, line.ToDto().LinkSku);
        Assert.IsTrue(line.IsDirty);
    }

    /// <summary>多分类筛选合并父级后代并去重；确定后回到第一页，重置恢复不限制分类。</summary>
    [TestMethod]
    public async Task CategoryFilters_MergeDescendantsAndResetRequest()
    {
        var parent = Guid.NewGuid(); var child = Guid.NewGuid(); var grandchild = Guid.NewGuid(); var other = Guid.NewGuid();
        var cache = new ReferenceDataCacheServiceStub(productCategories:
        [new() { Uid = parent, CategoryName = "English Parent", FakeName = "父分类" },
            new() { Uid = child, ParentId = parent, CategoryName = "English Child", FakeName = "子分类" },
            new() { Uid = grandchild, ParentId = child, CategoryName = "English Grandchild", FakeName = "孙分类" },
            new() { Uid = other, CategoryName = "English Other", FakeName = "其他分类" }]);
        // 此离线缓存只建立平面候选；按真实缓存关系补齐树边后再验证后代筛选。
        var childNode = cache.ProductCategories.Single(node => node.Uid == child);
        cache.ProductCategories.Single(node => node.Uid == parent).AddChild(childNode);
        childNode.AddChild(cache.ProductCategories.Single(node => node.Uid == grandchild));
        using var fixture = new Fixture(cache);
        var vm = fixture.ViewModel;
        foreach (var node in cache.ProductCategories.Where(node => node.Uid != grandchild)) vm.FilterCategories.Add(node);
        vm.PageIndex = 3;
        await vm.SearchCommand.ExecuteAsync(null);
        Assert.IsNotNull(fixture.LastSearch);
        Assert.AreEqual(Guid.Empty, fixture.LastSearch.CategoryId);
        CollectionAssert.AreEquivalent(new[] { parent, child, grandchild, other }, fixture.LastSearch.CategoryIds);
        Assert.AreEqual(1, fixture.LastSearch.PageIndex);
        await vm.ResetFiltersCommand.ExecuteAsync(null);
        Assert.IsEmpty(vm.FilterCategories);
        Assert.IsEmpty(fixture.LastSearch!.CategoryIds);
    }

    /// <summary>实际产品页面绑定公共多选分类器；确定触发查询，关闭弹层不提交分类草稿。</summary>
    [TestMethod]
    public void CategoryFilters_ViewUsesSharedPickerAndSearchCommand() => DatePickerAssistTests.RunWithApplication(async () =>
    {
        ApplicationResourceLoader.Load();
        using var fixture = new Fixture(new(productCategories:
            [new() { Uid = Guid.NewGuid(), CategoryName = "English Category", FakeName = "中文分类" }]));
        var view = new ProductsView { DataContext = fixture.ViewModel };
        var window = new Window { Content = view, Width = 1300, Height = 850, ShowInTaskbar = false };
        try
        {
            window.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var picker = Descendants(view).OfType<ProductCategoryMutiSelectButton>().Single();
            Assert.AreSame(fixture.ViewModel.ReferenceDataCache.ProductCategoryTree, picker.ItemsSource);
            Assert.AreSame(fixture.ViewModel.FilterCategories, picker.SelectedItems);
            Assert.AreSame(fixture.ViewModel.SearchCommand, picker.SelectionCommittedCommand);
            var inner = (SwiftExportClient.Resources.Controls.Filter.MultiSelectLookupButton)picker.FindName("CategoryPicker");
            var popup = (System.Windows.Controls.Primitives.Popup)inner.FindName("PART_Popup");
            popup.IsOpen = true;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            popup.IsOpen = false;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.AreEqual(0, fixture.PageReads);
            Assert.IsEmpty(fixture.ViewModel.FilterCategories);
            popup.IsOpen = true;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var check = Descendants(popup.Child).OfType<CheckBox>().Single();
            check.SetCurrentValue(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, true);
            var apply = Descendants(popup.Child).OfType<Button>().Single(button => Equals(button.Content, "确定"));
            apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitForAsync(() => fixture.PageReads == 1);
            Assert.HasCount(1, fixture.ViewModel.FilterCategories);
            CollectionAssert.AreEqual(new[] { fixture.ViewModel.ReferenceDataCache.ProductCategories.Single().Uid }, fixture.LastSearch!.CategoryIds);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        }
        finally { window.Close(); }
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
    });

    /// <summary>取消与放弃仅影响目标物料，其他物料和 BOM 组成草稿始终保留。</summary>
    [TestMethod]
    [DataRow(DialogResult.Close)]
    [DataRow(DialogResult.Secondary)]
    public async Task OpenDetail_HandlesOnlyTargetDraft(DialogResult choice)
    {
        using var fixture = new Fixture { Choice = choice };
        var target = fixture.ViewModel.Products.Items[0];
        var other = fixture.ViewModel.Products.Items[1];
        target.DescriptionZh = "当前草稿";
        other.DescriptionZh = "其他草稿";
        var bomLine = fixture.ViewModel.BomDrawer.Boms.AddNewItem("保留BOM");
        bomLine.Note = "抽屉草稿";

        await fixture.ViewModel.OpenDetailCommand.ExecuteAsync(null);

        Assert.AreEqual(choice == DialogResult.Close ? "当前草稿" : "原描述", target.DescriptionZh);
        Assert.AreEqual("其他草稿", other.DescriptionZh);
        Assert.IsTrue(other.IsDirty);
        Assert.IsTrue(fixture.ViewModel.BomDrawer.Boms.HasChanges);
        Assert.AreEqual("抽屉草稿", bomLine.Note);
        Assert.HasCount(choice == DialogResult.Close ? 0 : 1, fixture.Opened);
        Assert.AreEqual(0, fixture.SaveCount);
        if (fixture.Opened.Count > 0)
        {
            Assert.AreEqual($"product:{target.Uid:N}", fixture.Opened[0].Key);
            Assert.AreEqual(target.Uid, ((ProductDocumentOpenRequest)fixture.Opened[0].Parameter!).ProductId);
        }
    }

    /// <summary>保存只提交当前一行；失败不打开详情，成功也不接纳其他行草稿或重新读分页。</summary>
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task OpenDetail_SaveTargetOnlyAndRequireSuccess(bool succeeds)
    {
        using var fixture = new Fixture { Choice = DialogResult.Primary, SaveSucceeds = succeeds };
        var target = fixture.ViewModel.Products.Items[0];
        var other = fixture.ViewModel.Products.Items[1];
        target.DescriptionZh = "待保存";
        other.DescriptionZh = "其他草稿";

        await fixture.ViewModel.OpenDetailCommand.ExecuteAsync(null);

        Assert.AreEqual(1, fixture.SaveCount);
        Assert.HasCount(1, fixture.LastSave!.UpdateItems);
        Assert.AreEqual(target.Uid, fixture.LastSave.UpdateItems.Single().Uid);
        Assert.IsEmpty(fixture.LastSave.CreateItems);
        Assert.IsEmpty(fixture.LastSave.DeleteItems);
        Assert.HasCount(succeeds ? 1 : 0, fixture.Opened);
        Assert.AreEqual(!succeeds, target.IsDirty);
        Assert.AreEqual(succeeds ? 2 : 1, target.Version);
        Assert.AreEqual("其他草稿", other.DescriptionZh);
        Assert.IsTrue(other.IsDirty);
        Assert.AreEqual(0, fixture.PageReads);
    }

    /// <summary>没有原生提交能力的有效返回不阻断；原生编辑转换失败则不弹窗、保存或导航。</summary>
    [TestMethod]
    public async Task OpenDetail_BlocksUncommittedCell()
    {
        using var fixture = new Fixture { Choice = DialogResult.Primary, SaveSucceeds = true };
        var target = fixture.ViewModel.Products.Items[0];
        target.DescriptionZh = "草稿";
        fixture.ViewModel.CommitPendingEdits = () => false;
        await fixture.ViewModel.OpenDetailCommand.ExecuteAsync(null);
        Assert.IsEmpty(fixture.Opened);
        Assert.AreEqual(0, fixture.SaveCount);
        Assert.AreEqual(0, fixture.DialogCount);
        Assert.IsTrue(target.IsDirty);
    }

    /// <summary>保存成功但回执缺失时不清除草稿，避免把不明确的结果当作提交成功。</summary>
    [TestMethod]
    public async Task OpenDetail_RequiresTargetVersionReceipt()
    {
        using var fixture = new Fixture { Choice = DialogResult.Primary, SaveSucceeds = true, OmitReceipt = true };
        var target = fixture.ViewModel.Products.Items[0];
        target.DescriptionZh = "草稿";
        await fixture.ViewModel.OpenDetailCommand.ExecuteAsync(null);
        Assert.IsEmpty(fixture.Opened);
        Assert.IsTrue(target.IsDirty);
        Assert.AreEqual(1, target.Version);
    }

    /// <summary>已保存行只需要查看权限；新增、待删和空身份不能进入详情。</summary>
    [TestMethod]
    public async Task OpenDetail_ViewOnlyAndPersistedIdentityRules()
    {
        using var fixture = new Fixture { AllowEdit = false, Choice = DialogResult.Secondary };
        Assert.IsTrue(fixture.ViewModel.OpenDetailCommand.CanExecute(null));
        fixture.ViewModel.SelectedLine!.DescriptionZh = "旧草稿";
        await fixture.ViewModel.OpenDetailCommand.ExecuteAsync(null);
        Assert.IsNull(fixture.LastDialog!.PrimaryButtonText);
        Assert.HasCount(1, fixture.Opened);

        fixture.ViewModel.SelectedLine = fixture.ViewModel.Products.AddNewProduct();
        Assert.IsFalse(fixture.ViewModel.OpenDetailCommand.CanExecute(null));
        fixture.ViewModel.SelectedLine = fixture.ViewModel.Products.Items[0];
        fixture.ViewModel.Products.Remove(fixture.ViewModel.SelectedLine!);
        Assert.IsFalse(fixture.ViewModel.OpenDetailCommand.CanExecute(null));
        fixture.ViewModel.SelectedLine = null;
        Assert.IsFalse(fixture.ViewModel.OpenDetailCommand.CanExecute(null));
    }

    /// <summary>等待草稿确认期间入口和写命令共用 Busy 门禁，不能发起第二次编辑入口。</summary>
    [TestMethod]
    public async Task OpenDetail_ConfirmationSharesBusyBoundary()
    {
        using var fixture = new Fixture();
        fixture.ViewModel.SelectedLine!.DescriptionZh = "草稿";
        var confirmation = new TaskCompletionSource<DialogResult>();
        fixture.Confirmation = confirmation.Task;
        var opening = fixture.ViewModel.OpenDetailCommand.ExecuteAsync(null);
        Assert.IsTrue(fixture.ViewModel.IsBusy);
        Assert.IsFalse(fixture.ViewModel.OpenDetailCommand.CanExecute(null));
        Assert.IsFalse(fixture.ViewModel.SaveCommand.CanExecute(null));
        confirmation.SetResult(DialogResult.Close);
        await opening;
        Assert.IsFalse(fixture.ViewModel.IsBusy);
    }

    /// <summary>保存通知仅刷新匹配行的详情和版本，保留其他行身份、草稿与分页范围。</summary>
    [TestMethod]
    public void SavedDetail_UpdatesOnlyMatchingRow()
    {
        using var fixture = new Fixture();
        var target = fixture.ViewModel.Products.Items[0];
        var other = fixture.ViewModel.Products.Items[1];
        other.DescriptionZh = "其他草稿";
        fixture.DetailResponse = (id, _) => Task.FromResult(ServiceResult<ProductDetailDto>.Ok(Detail(id, 7)));

        WeakReferenceMessenger.Default.Send(new ProductDetailSavedMessage(target.Uid));

        Assert.AreEqual(7, target.Version);
        Assert.AreEqual("服务端描述", target.DescriptionZh);
        Assert.IsFalse(target.IsDirty);
        Assert.AreSame(other, fixture.ViewModel.Products.Items[1]);
        Assert.AreEqual("其他草稿", other.DescriptionZh);
        Assert.IsTrue(other.IsDirty);
        Assert.AreEqual(0, fixture.PageReads);
    }

    /// <summary>定向查询期间产生的新草稿不会被服务端详情覆盖，也不推进旧基线版本。</summary>
    [TestMethod]
    public async Task SavedDetail_PreservesDraftCreatedDuringRead()
    {
        using var fixture = new Fixture();
        var target = fixture.ViewModel.Products.Items[0];
        var pending = new TaskCompletionSource<ServiceResult<ProductDetailDto>>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.DetailResponse = (_, _) => pending.Task;
        WeakReferenceMessenger.Default.Send(new ProductDetailSavedMessage(target.Uid));
        target.DescriptionZh = "查询期间草稿";
        pending.SetResult(ServiceResult<ProductDetailDto>.Ok(Detail(target.Uid, 7)));
        await WaitForAsync(() => fixture.ViewModel.ResultSummary.Contains("查询期间"));
        Assert.AreEqual(1, target.Version);
        Assert.AreEqual("查询期间草稿", target.DescriptionZh);
        Assert.IsTrue(target.IsDirty);
    }

    /// <summary>同物料旧请求与释放后请求均不接纳，释放后消息也不再发起查询。</summary>
    [TestMethod]
    public async Task SavedDetail_LatestRequestAndDisposeProtectRows()
    {
        using var fixture = new Fixture();
        var target = fixture.ViewModel.Products.Items[0];
        var previous = new TaskCompletionSource<ServiceResult<ProductDetailDto>>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.DetailResponse = (_, _) => previous.Task;
        WeakReferenceMessenger.Default.Send(new ProductDetailSavedMessage(target.Uid));
        fixture.DetailResponse = (id, _) => Task.FromResult(ServiceResult<ProductDetailDto>.Ok(Detail(id, 8)));
        WeakReferenceMessenger.Default.Send(new ProductDetailSavedMessage(target.Uid));
        Assert.AreEqual(8, target.Version);
        previous.SetResult(ServiceResult<ProductDetailDto>.Ok(Detail(target.Uid, 7)));
        await Task.Yield();
        Assert.AreEqual(8, target.Version);

        var closing = new TaskCompletionSource<ServiceResult<ProductDetailDto>>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.DetailResponse = (_, _) => closing.Task;
        WeakReferenceMessenger.Default.Send(new ProductDetailSavedMessage(target.Uid));
        fixture.ViewModel.Dispose();
        closing.SetResult(ServiceResult<ProductDetailDto>.Ok(Detail(target.Uid, 9)));
        var reads = fixture.DetailReads;
        WeakReferenceMessenger.Default.Send(new ProductDetailSavedMessage(target.Uid));
        await Task.Yield();
        Assert.AreEqual(8, target.Version);
        Assert.AreEqual(reads, fixture.DetailReads);
    }

    /// <summary>目标行已有草稿时直接提示，完全不读取详情以免推进版本。</summary>
    [TestMethod]
    public void SavedDetail_DirtyTargetDoesNotRead()
    {
        using var fixture = new Fixture();
        var target = fixture.ViewModel.Products.Items[0];
        target.DescriptionZh = "已有草稿";
        WeakReferenceMessenger.Default.Send(new ProductDetailSavedMessage(target.Uid));
        Assert.AreEqual(0, fixture.DetailReads);
        Assert.AreEqual(1, target.Version);
        Assert.AreEqual("已有草稿", target.DescriptionZh);
    }

    /// <summary>原生编辑桥抛出异常时通知入口观察失败，保留旧数据且不启动分页刷新。</summary>
    [TestMethod]
    public void SavedDetail_ObservesCommitBridgeFailure()
    {
        using var fixture = new Fixture();
        fixture.ViewModel.CommitPendingEdits = () => throw new InvalidOperationException("测试编辑桥失败");
        WeakReferenceMessenger.Default.Send(new ProductDetailSavedMessage(fixture.ViewModel.SelectedLine!.Uid));
        Assert.IsTrue(fixture.ViewModel.ResultSummary.Contains("刷新未完成"));
        Assert.AreEqual(1, fixture.ViewModel.SelectedLine.Version);
        Assert.AreEqual(0, fixture.PageReads);
    }

    /// <summary>使用真实物料页验证原生数字编辑、详情入口按钮和卸载解绑，不以假委托代替视觉树接线。</summary>
    [TestMethod]
    public void View_NativeCommitAndUnloadPreserveDraftBoundary() => DatePickerAssistTests.RunWithApplication(async () =>
    {
        ApplicationResourceLoader.Load();
        using var fixture = new Fixture { Choice = DialogResult.Secondary };
        var view = new ProductsView { DataContext = fixture.ViewModel };
        var window = new Window { Content = view, Width = 1300, Height = 850, ShowInTaskbar = false };
        try
        {
            window.Show();
            await Dispatcher.Yield(DispatcherPriority.Background);
            var grid = Descendants(view).OfType<DataGrid>()
                .Single(candidate => ReferenceEquals(candidate.ItemsSource, fixture.ViewModel.Products.Items));
            var target = fixture.ViewModel.Products.Items[0];
            var other = fixture.ViewModel.Products.Items[1];
            other.DescriptionZh = "其他草稿";
            var price = grid.Columns.OfType<DataGridTextColumn>().Single(column => Equals(column.Header, "人民币价"));
            grid.ScrollIntoView(target, price);
            grid.UpdateLayout();
            grid.CurrentCell = new DataGridCellInfo(target, price);
            Assert.IsTrue(grid.BeginEdit());
            var editor = (TextBox)price.GetCellContent(target);
            editor.Text = "非法价格";
            editor.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
            Assert.IsTrue(Validation.GetHasError(editor));
            Assert.IsFalse(fixture.ViewModel.CommitPendingEdits!());
            await fixture.ViewModel.OpenDetailCommand.ExecuteAsync(null);
            Assert.IsEmpty(fixture.Opened);

            editor.Text = "12";
            editor.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
            Assert.IsTrue(fixture.ViewModel.CommitPendingEdits!());
            Assert.AreEqual(12m, target.CnyPrice);
            var editButton = Descendants(view).OfType<Button>().Single(button => Equals(button.Content, "编辑"));
            Assert.AreSame(fixture.ViewModel.OpenDetailCommand, editButton.Command);
            var toolbar = (WrapPanel)editButton.Parent;
            var buttons = toolbar.Children.OfType<Button>().ToList();
            Assert.AreEqual("保存", buttons[buttons.IndexOf(editButton) + 1].Content);
            await fixture.ViewModel.OpenDetailCommand.ExecuteAsync(null);
            Assert.HasCount(1, fixture.Opened);
            Assert.AreEqual("其他草稿", other.DescriptionZh);
            Assert.IsTrue(other.IsDirty);
        }
        finally
        {
            window.Close();
        }
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Assert.IsNull(fixture.ViewModel.CommitPendingEdits, "卸载后不能保留原生表格委托。");
    });

    /// <summary>遍历真实视觉树寻找已呈现入口，验证绑定而非读取 XAML 文本。</summary>
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    /// <summary>提供完整详情投影，由实际 Mapster 配置验证嵌套资料到列表的映射。</summary>
    private static ProductDetailDto Detail(Guid id, int version) => new()
    {
        Uid = id, Version = version, Data = new() { Sku = "A", DescriptionZh = "服务端描述" },
        MasterSku = "MASTER", CategoryName = "分类", BomNo = "BOM", BomVersion = 1
    };

    /// <summary>等待可观察结果，避免固定延迟或读取 ViewModel 私有字段。</summary>
    private static async Task WaitForAsync(Func<bool> completed)
    {
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (!completed() && DateTime.UtcNow < deadline) await Task.Delay(10);
        Assert.IsTrue(completed(), "物料定向查询未按预期完成。");
    }

    /// <summary>只模拟该管理入口实际依赖，真实 ViewModel 负责草稿、请求和回执接纳。</summary>
    private sealed class Fixture : IDisposable
    {
        internal DialogResult Choice = DialogResult.Close;
        internal bool SaveSucceeds, OmitReceipt;
        internal bool AllowEdit = true;
        internal int SaveCount, PageReads, DetailReads, DialogCount;
        internal Task<DialogResult>? Confirmation;
        internal DialogOptions? LastDialog;
        internal ProductBatchSaveRequest? LastSave;
        internal ProductSearchRequest? LastSearch;
        internal readonly List<(string Key, object? Parameter)> Opened = [];
        internal readonly List<ProductDto> Server = [new() { Uid = Guid.NewGuid(), Version = 1, Sku = "A", DescriptionZh = "原描述" },
            new() { Uid = Guid.NewGuid(), Version = 1, Sku = "B", DescriptionZh = "原描述" }];
        internal Func<Guid, CancellationToken, Task<ServiceResult<ProductDetailDto>>> DetailResponse;
        internal readonly ProductsViewModel ViewModel;

        internal Fixture(ReferenceDataCacheServiceStub? cache = null)
        {
            DetailResponse = (id, _) => Task.FromResult(ServiceResult<ProductDetailDto>.Ok(Detail(id, Server.Single(item => item.Uid == id).Version)));
            var api = Proxy<IProductApi>((method, args) =>
            {
                if (method.Name == nameof(IProductApi.GetDetailAsync))
                {
                    DetailReads++;
                    return DetailResponse((Guid)args[0]!, (CancellationToken)args[1]!);
                }
                if (method.Name == nameof(IProductApi.GetPagedAsync))
                {
                    PageReads++;
                    LastSearch = (ProductSearchRequest)args[0]!;
                    return Task.FromResult(ServiceResult<PagedResponseBase<ProductDto>>.Ok(new() { Items = Server.Adapt<List<ProductDto>>() }));
                }
                Assert.AreEqual(nameof(IProductApi.BatchSaveAsync), method.Name);
                SaveCount++;
                LastSave = (ProductBatchSaveRequest)args[0]!;
                if (!SaveSucceeds) return Task.FromResult(ServiceResult<ProductBatchSaveResponse>.Fail("测试保存失败"));
                var response = new ProductBatchSaveResponse();
                foreach (var item in LastSave.UpdateItems)
                {
                    var saved = Server.Single(row => row.Uid == item.Uid);
                    item.Data.Adapt(saved);
                    saved.Version++;
                    if (!OmitReceipt) response.Items.Add(new SaveItemResult
                    {
                        Uid = item.Uid, Success = true, Operation = SaveOperation.Update,
                        OldVersion = item.Version, NewVersion = saved.Version
                    });
                }
                return Task.FromResult(ServiceResult<ProductBatchSaveResponse>.Ok(response));
            });
            var runner = Proxy<IApiCallRunner>((_, args) => ((Delegate)args[0]!).DynamicInvoke());
            var dialogs = Proxy<IDialogService>((method, args) =>
            {
                if (method.Name != nameof(IDialogService.ShowAsync)) return Task.FromResult(true);
                DialogCount++;
                LastDialog = (DialogOptions)args[1]!;
                return Confirmation ?? Task.FromResult(Choice);
            });
            var navigation = Proxy<INavigationService>((method, args) =>
            {
                Assert.AreEqual(nameof(INavigationService.OpenDocumentAsync), method.Name);
                Assert.AreEqual(ProductDetailViewModel.RouteKey, args[0]);
                Opened.Add(((string)args[1]!, args[2]));
                return Task.CompletedTask;
            });
            var user = Proxy<ICurrentUserService>((method, args) => method.Name == nameof(ICurrentUserService.HasPermission)
                ? (string)args[0]! == PermissionKeys.ProductsProductView || AllowEdit && (string)args[0]! == PermissionKeys.ProductsProductEdit : null);
            var messages = Proxy<IMessageService>((_, _) => null);
            var bom = new BomDrawerViewModel(null!, api, runner, messages, dialogs);
            ViewModel = new ProductsViewModel(api, null!, null!, runner, cache ?? new ReferenceDataCacheServiceStub(), messages,
                dialogs, bom, null!, user, navigation);
            ViewModel.Products.LoadFromDtos(Server.Adapt<List<ProductDto>>());
            ViewModel.SelectedLine = ViewModel.Products.Items[0];
        }

        /// <summary>解除实际页面的消息及资料订阅，防止测试之间互相影响。</summary>
        public void Dispose() => ViewModel.Dispose();
    }

    /// <summary>复用现有测试代理，以窄 API 行为替身控制输入和响应。</summary>
    private static T Proxy<T>(Func<MethodInfo, object?[], object?> invoke) where T : class
    {
        var proxy = DispatchProxy.Create<T, ProductMasterSupplierEditingTests.TestProxy>();
        ((ProductMasterSupplierEditingTests.TestProxy)(object)proxy).InvokeMethod = invoke;
        return proxy;
    }
}
