using Mapster;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Refit;
using SwiftExport.Contracts.Apis.QualityControl;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.QualityControl.AfterSales;
using SwiftExport.Contracts.DTOs.Suppliers;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Features.QualityControl.AfterSales.Models;
using SwiftExportClient.Features.QualityControl.AfterSales.Services;
using SwiftExportClient.Features.QualityControl.AfterSales.ViewModels;
using SwiftExportClient.Navigation;
using SwiftExportClient.Services.Api;

namespace SwiftExport.Client.Wpf.Tests.Features.QualityControl.AfterSales;

/// <summary>验证售后客户端的核心字段语义、两阶段确认及路由契约。</summary>
[TestClass]
public sealed class AfterSalesModelsAndContractsTests
{
    /// <summary>新非标补件无默认单位，明确单位进入保存 DTO，单位丢失时确认阻断。</summary>
    [TestMethod]
    public void ExplicitUnitsRoundTripAndUnknownBlocksConfirmation()
    {
        var line = AfterSalesLineModel.CreateNew(1);
        Assert.IsTrue(string.IsNullOrWhiteSpace(line.UnitSnapshot));
        line.IsNonStandard = true; line.Quantity = 1; line.DescriptionSnapshot = "非标";
        Assert.IsNotNull(line.Validate(true));
        line.UnitSnapshot = "SET";
        Assert.IsNull(line.Validate(true));
        Assert.AreEqual("SET", line.ToEditDto().UnitSnapshot);
        line.Load(new() { Uid = line.Uid, UnitSnapshot = null });
        Assert.IsTrue(string.IsNullOrWhiteSpace(line.UnitSnapshot));
    }

    /// <summary>实际页面提交忽略空新占位，保留显式零数量，并允许通过删除集合移除最后一个已有补件。</summary>
    [TestMethod]
    public void DetailSubmissionFiltersOnlyNewPlaceholders()
    {
        using var vm=new AfterSalesDetailViewModel(
            Port<IAfterSalesApi>(),Port<IAfterSalesFeatureService>(),new PassThroughApiCallRunner(),
            Port<INavigationService>(),Port<SwiftExportClient.Services.UI.IDialogService>(),
            Port<IAfterSalesReasonDialogService>(),Port<SwiftExportClient.Services.UI.IMessageService>(),
            Port<SwiftExportClient.Authentication.ICurrentUserService>(),
            Port<SwiftExportClient.Services.Caching.IReferenceDataCacheService>(),
            Port<SwiftExport.Contracts.Apis.ProductApis.IProductApi>());
        var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
        typeof(AfterSalesDetailViewModel).GetMethod("InitializeNew",flags)!.Invoke(vm,null);
        Assert.AreEqual(0,vm.Lines.Count);
        vm.Header.IssueDescription="投诉";
        vm.Header.CustomerComplaintNo="客户001";
        vm.HasInputErrors=true;
        Assert.IsFalse(vm.SaveCommand.CanExecute(null));Assert.IsFalse(vm.ConfirmDemandCommand.CanExecute(null));
        vm.HasInputErrors=false;Assert.IsTrue(vm.SaveCommand.CanExecute(null));
        vm.AddLineCommand.Execute(null);
        SaveAfterSalesCommand Submission()=>(SaveAfterSalesCommand)typeof(AfterSalesDetailViewModel)
            .GetMethod("CreateSaveCommand",flags)!.Invoke(vm,null)!;
        Assert.AreEqual(0,Submission().AddedLines.Count);
        vm.SelectedLine!.Quantity=0;
        var submitted=Submission();
        Assert.AreEqual(1,submitted.AddedLines.Count);Assert.AreEqual(0m,submitted.AddedLines[0].Quantity);
        Assert.AreEqual("客户001",submitted.Header.CustomerComplaintNo);
        Assert.IsTrue(vm.DeleteLineCommand.CanExecute(null));vm.DeleteLineCommand.Execute(null);
        Assert.AreEqual(0,vm.Lines.Count);Assert.AreEqual(0,Submission().DeletedLines.Count);
        vm.LineList.LoadFromDtos([new(){Uid=Guid.NewGuid(),Version=4}]);
        var existing=vm.Lines.Single();vm.SelectedLine=existing;
        Assert.AreEqual(0,Submission().UpdatedLines.Count);
        vm.DeleteLineCommand.Execute(null);
        Assert.AreEqual(0,vm.Lines.Count);Assert.AreEqual(existing.Uid,Submission().DeletedLines[0].Uid);
        Assert.AreEqual(4,Submission().DeletedLines[0].Version);
    }

    // 本测试仅执行本地编辑和命令组装，未使用的网络及界面端口不产生副作用。
    private static T Port<T>() where T:class => System.Reflection.DispatchProxy.Create<T,LocalPort>();

    // 售后模型测试只使用本地端口，不访问业务 API。
    private static AfterSalesDetailViewModel NewDetail() => new(
        Port<IAfterSalesApi>(), Port<IAfterSalesFeatureService>(), new PassThroughApiCallRunner(),
        Port<INavigationService>(), Port<SwiftExportClient.Services.UI.IDialogService>(),
        Port<IAfterSalesReasonDialogService>(), Port<SwiftExportClient.Services.UI.IMessageService>(),
        Port<SwiftExportClient.Authentication.ICurrentUserService>(),
        Port<SwiftExportClient.Services.Caching.IReferenceDataCacheService>(),
        Port<SwiftExport.Contracts.Apis.ProductApis.IProductApi>());

    /// <summary>图片只用于显示；文件夹路径进入单头文本契约。</summary>
    [TestMethod]
    public void ImagesDoNotDirtyFactsOrEnterWritePayload()
    {
        var header = new AfterSalesHeaderEditModel();
        header.Load(new() { ImagePath = "old.png", LocalFileFolder = @"D:\投诉" });
        header.ImagePath = "new.png";
        Assert.IsFalse(header.IsDirty);
        header.LocalFileFolder = @"D:\客户投诉";
        Assert.IsTrue(header.IsDirty);
        Assert.AreEqual(@"D:\客户投诉", header.ToDto().LocalFileFolder);
        header.LocalFileFolder = null;
        Assert.IsNull(header.ToDto().LocalFileFolder);
        var line = new AfterSalesLineModel();
        line.Load(new() { ImagePath = "old.png" });
        line.ImagePath = "new.png";
        Assert.IsFalse(line.IsDirty);
        Assert.AreEqual("new.png", line.ToDto().ImagePath);
        Assert.IsFalse(System.Text.Json.JsonSerializer.Serialize(header.ToDto()).Contains("ImagePath"));
        Assert.IsFalse(System.Text.Json.JsonSerializer.Serialize(line.ToEditDto()).Contains("ImagePath"));
    }

    /// <summary>产品控件回填各自的图片与身份，不覆盖补件稳定行身份；锁定时不能改产品。</summary>
    [TestMethod]
    public void ProductSelectionMapsImagesAndRespectsFactLock()
    {
        using var vm = NewDetail();
        vm.Capabilities = new() { CanSave = true, CanEditFacts = true };
        vm.AddLineCommand.Execute(null);
        var line = vm.Lines.Single();
        var uid = line.Uid;
        var issue = new ProductLookupDto { Uid = Guid.NewGuid(), Sku = "FG001", MainImage = "finished.png" };
        vm.SelectedProductCandidate = issue;
        vm.CommitIssueProductCommand.Execute(null);
        Assert.AreEqual(issue.Uid, vm.Header.IssueProductId);
        Assert.AreEqual(issue.MainImage, vm.Header.ImagePath);
        var part = new ProductLookupDto { Uid = Guid.NewGuid(), Sku = "PART001", MainImage = "part.png", LinkSku = "AQ1", Unit = "KG" };
        vm.SelectedProductCandidate = part;
        vm.CommitProductCommand.Execute(line);
        Assert.AreEqual(uid, line.Uid);
        Assert.AreEqual(part.Uid, line.ProductId);
        Assert.AreEqual(part.MainImage, line.ImagePath);
        Assert.AreEqual("AQ1", line.LinkSkuSnapshot);
        Assert.AreEqual("KG", line.UnitSnapshot);
        Assert.AreEqual(issue.Uid, vm.Header.IssueProductId);
        vm.Capabilities = new() { CanSave = true, CanEditFacts = false };
        vm.ClearProductCommand.Execute(line);
        vm.ClearIssueProductCommand.Execute(null);
        Assert.AreEqual(part.Uid, line.ProductId);
        Assert.AreEqual(issue.Uid, vm.Header.IssueProductId);
        vm.Capabilities = new() { CanSave = true, CanEditFacts = true };
        vm.ClearProductCommand.Execute(line);
        Assert.IsNull(line.ProductId); Assert.IsNull(line.ImagePath);
        Assert.IsTrue(string.IsNullOrWhiteSpace(line.UnitSnapshot));
    }

    /// <summary>原生行编辑取消仅恢复本次修改，保留此前待保存内容和新增状态。</summary>
    [TestMethod]
    public void RowEditCancellationPreservesEarlierChanges()
    {
        var line = new AfterSalesLineModel();
        line.Load(new() { Uid = Guid.NewGuid(), Quantity = 1 });
        line.Remark = "此前修改";
        line.BeginEdit(); line.Quantity = 5; line.Remark = "本次修改"; line.CancelEdit();
        Assert.AreEqual(1m, line.Quantity); Assert.AreEqual("此前修改", line.Remark); Assert.IsTrue(line.IsDirty);
        line.Remark = string.Empty; Assert.IsFalse(line.IsDirty);
        var added = AfterSalesLineModel.CreateNew(1);
        added.BeginEdit(); added.Quantity = 2; added.CancelEdit();
        Assert.IsTrue(added.IsNew); Assert.IsTrue(added.IsEmptyPlaceholder);
    }

    /// <summary>表格未能提交当前输入时保存不调用 API。</summary>
    [TestMethod]
    public async Task FailedGridCommitBlocksSave()
    {
        using var vm = NewDetail();
        vm.Capabilities = new() { CanSave = true, CanEditFacts = true };
        vm.Header.CustomerId = Guid.NewGuid(); vm.Header.IssueDescription = "投诉";
        var commits = 0;
        vm.CommitPendingEdits = () => { commits++; return false; };
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.AreEqual(1, commits);
        StringAssert.Contains(vm.ValidationSummary!, "补件表格");
        Assert.IsTrue(vm.Header.IsDirty);
    }

    /// <summary>真实补件表格执行原生编辑、输入转换和确认后的列权限，卸载释放提交回调。</summary>
    [TestMethod]
    [DoNotParallelize]
    public void DetailGridCommitsNumbersAndLocksFacts()
    {
        Controls.DatePickerAssistTests.RunWithApplication(async () =>
        {
            TestInfrastructure.ApplicationResourceLoader.Load();
            using var vm = NewDetail();
            vm.Capabilities = new() { CanSave = true, CanEditFacts = true };
            vm.LineList.LoadFromDtos([new() { Uid = Guid.NewGuid(), LineNumber = 1, Quantity = 1 }]);
            var view = new SwiftExportClient.Features.QualityControl.AfterSales.Views.AfterSalesDetailView { DataContext = vm };
            var window = new System.Windows.Window { Content = view, Width = 1400, Height = 1000, Left = -10000, ShowActivated = false };
            try
            {
                window.Show(); view.UpdateLayout();
                await view.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                var grid = (System.Windows.Controls.DataGrid)view.FindName("ReplacementGrid");
                var line = vm.Lines.Single();
                var quantity = grid.Columns.Single(x => x.SortMemberPath == "Quantity");
                grid.SelectedItem = line; grid.ScrollIntoView(line, quantity); grid.UpdateLayout();
                grid.CurrentCell = new(line, quantity); grid.Focus();
                Assert.IsTrue(grid.BeginEdit());
                grid.UpdateLayout();
                var editor = (System.Windows.Controls.TextBox)quantity.GetCellContent(line);
                editor.Text = "bad-number";
                editor.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty)!.UpdateSource();
                Assert.IsTrue(vm.HasInputErrors);
                Assert.IsFalse(vm.CommitPendingEdits!());
                Assert.AreEqual(1m, line.Quantity);
                editor.Text = "12";
                editor.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty)!.UpdateSource();
                Assert.IsFalse(vm.HasInputErrors);
                Assert.IsTrue(vm.CommitPendingEdits!());
                Assert.AreEqual(12m, line.ToEditDto().Quantity);
                var unitColumn = grid.Columns.Single(x => x.SortMemberPath == "UnitSnapshot");
                grid.ScrollIntoView(line, unitColumn); grid.UpdateLayout(); grid.CurrentCell = new(line, unitColumn);
                Assert.IsFalse(grid.BeginEdit());
                line.IsNonStandard = true;
                Assert.IsTrue(grid.BeginEdit()); grid.UpdateLayout();
                var unitEditor = (System.Windows.Controls.TextBox)unitColumn.GetCellContent(line);
                unitEditor.Text = "SET";
                unitEditor.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty)!.UpdateSource();
                Assert.IsTrue(vm.CommitPendingEdits!());
                Assert.AreEqual("SET", line.ToEditDto().UnitSnapshot);
                var productColumn = grid.Columns.Single(x => x.SortMemberPath == "ProductId");
                grid.ScrollIntoView(line, productColumn); grid.UpdateLayout(); grid.CurrentCell = new(line, productColumn);
                Assert.IsTrue(grid.BeginEdit()); grid.UpdateLayout();
                var lookup = VisualChildren<SwiftExportClient.Features.Products.Components.ProductLookupTextBox>(grid).Single();
                var part = new ProductLookupDto { Uid = Guid.NewGuid(), Sku = "P1", MainImage = "part.png" };
                lookup.SelectedProduct = part;
                lookup.GetBindingExpression(SwiftExportClient.Features.Products.Components.ProductLookupTextBox.SelectedProductProperty)!.UpdateSource();
                lookup.SelectionCommittedCommand!.Execute(lookup.SelectionCommittedCommandParameter);
                Assert.AreEqual(part.Uid, line.ProductId); Assert.AreEqual(part.MainImage, line.ImagePath);
                Assert.IsTrue(vm.CommitPendingEdits!());
                vm.Capabilities = new() { CanSave = true, CanEditFacts = false };
                grid.CurrentCell = new(line, quantity);
                Assert.IsFalse(grid.BeginEdit());
                var remark = grid.Columns.Single(x => x.SortMemberPath == "Remark");
                grid.ScrollIntoView(line, remark); grid.UpdateLayout(); grid.CurrentCell = new(line, remark);
                Assert.IsTrue(grid.BeginEdit());
                grid.CancelEdit(System.Windows.Controls.DataGridEditingUnit.Cell);
                grid.CancelEdit(System.Windows.Controls.DataGridEditingUnit.Row);
                vm.Capabilities = new();
                Assert.IsFalse(grid.BeginEdit());
            }
            finally { window.Close(); }
            await view.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            Assert.IsNull(vm.CommitPendingEdits);
        });
    }

    /// <summary>管理页真实分页控件更新页大小并把翻页请求交给售后查询。</summary>
    [TestMethod]
    [DoNotParallelize]
    public void ManagerUsesNavigatorForPageSizeAndIndex()
    {
        Controls.DatePickerAssistTests.RunWithApplication(async () =>
        {
            TestInfrastructure.ApplicationResourceLoader.Load();
            var api = new RecordingAfterSalesApi
            {
                SaveResult = ServiceResult<AfterSalesCommandResult>.Ok(new()),
                ConfirmResult = ServiceResult<AfterSalesCommandResult>.Ok(new())
            };
            using var vm = new AfterSalesManagerViewModel(api, new PassThroughApiCallRunner(), Port<INavigationService>(),
                Port<SwiftExportClient.Authentication.ICurrentUserService>(), Port<SwiftExportClient.Services.Caching.IReferenceDataCacheService>());
            vm.TotalCount = 125;
            var view = new SwiftExportClient.Features.QualityControl.AfterSales.Views.AfterSalesManagerView { DataContext = vm };
            var window = new System.Windows.Window { Content = view, Width = 1400, Height = 900, Left = -10000, ShowActivated = false };
            try
            {
                window.Show(); view.UpdateLayout();
                var navigator = VisualChildren<SwiftExportClient.Resources.Controls.PageNavigator>(view).Single();
                Assert.AreEqual(125, navigator.TotalCount);
                var size = (System.Windows.Controls.ComboBox)navigator.FindName("CboPageSize");
                size.SelectedItem = 50;
                if (vm.PageChangeCommand.ExecutionTask is { } request) await request;
                Assert.AreEqual(50, api.SearchRequest!.PageSize); Assert.AreEqual(1, api.SearchRequest.PageIndex);
                view.UpdateLayout();
                var next = VisualChildren<System.Windows.Controls.Button>(navigator)
                    .Single(button => button.DataContext is SwiftExportClient.Resources.Controls.PageItem item && item.PageIndex == 2);
                next.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                if (vm.PageChangeCommand.ExecutionTask is { } page) await page;
                Assert.AreEqual(2, api.SearchRequest.PageIndex); Assert.AreEqual(50, vm.PageSize);
                Assert.AreEqual(2, navigator.PageIndex);
            }
            finally { window.Close(); }
        });
    }

    // 查找已生成的真实控件，不依赖模板内部命名。
    private static IEnumerable<T> VisualChildren<T>(System.Windows.DependencyObject root) where T : System.Windows.DependencyObject
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is T typed) yield return typed;
            foreach (var nested in VisualChildren<T>(child)) yield return nested;
        }
    }

    /// <summary>本地页面测试依赖：允许权限检查，其他端口均不执行外部操作。</summary>
    public class LocalPort : System.Reflection.DispatchProxy
    {
        /// <summary>返回权限允许值，不访问网络、缓存或窗口。</summary>
        protected override object? Invoke(System.Reflection.MethodInfo? method,object?[]? args)
            => method!.ReturnType==typeof(bool)?true:null;
    }

    /// <summary>投诉字段在单头映射并跟踪修改，保留历史空值及长度校验。</summary>
    [TestMethod] public void HeaderComplaintRoundTripsAndTracksChanges()
    {
        var header=new AfterSalesHeaderEditModel();
        header.Load(new(){Uid=Guid.NewGuid(),IssueDescription="投诉",CustomerComplaintNo="客户001"});
        Assert.IsFalse(header.IsDirty);Assert.AreEqual("客户001",header.ToDto().CustomerComplaintNo);
        header.CustomerComplaintNo="客户002";Assert.IsTrue(header.IsDirty);
        header.CustomerComplaintNo=null;Assert.IsNull(header.ToDto().CustomerComplaintNo);Assert.IsNull(header.Validate(false));
        header.CustomerComplaintNo=new string('中',101);StringAssert.Contains(header.Validate(false)!,"Header.CustomerComplaintNo");
        header.Load(new(){IssueDescription="历史投诉"});Assert.IsNull(header.CustomerComplaintNo);Assert.IsFalse(header.IsDirty);
    }
    /// <summary>仅空白新占位可忽略；显式零、文字和已保存空行均保留其事实。</summary>
    [TestMethod] public void PlaceholderAndQuantityRules()
    {
        var line=AfterSalesLineModel.CreateNew(1);Assert.IsTrue(line.IsEmptyPlaceholder);
        line.Quantity=0;Assert.IsFalse(line.IsEmptyPlaceholder);Assert.IsNull(line.Validate(true));
        line.Quantity=-1;Assert.AreEqual("补发数量不能为负数。",line.Validate(false));
        line.Quantity=1;Assert.AreEqual("标准补件必须选择产品。",line.Validate(true));
        line.ProductId=Guid.NewGuid();Assert.IsNotNull(line.Validate(true));
        line.UnitSnapshot="SET";Assert.IsNull(line.Validate(true));
        line.IsNonStandard=true;Assert.AreEqual("非标补件不能同时选择标准产品。",line.Validate(false));
        line.ProductId=null;line.DescriptionSnapshot=new string('中',301);Assert.AreEqual("确认时补发描述不能超过 300 字。",line.Validate(true));
        line.Load(new(){Uid=Guid.NewGuid()});Assert.IsFalse(line.IsNew);Assert.IsTrue(line.IsEmptyPlaceholder);Assert.IsFalse(line.IsDirty);
    }
    /// <summary>确认校验整单责任方和成本；问题产品与实际配件属于不同契约。</summary>
    [TestMethod] public void HeaderAndReplacementHaveIndependentFacts()
    {
        var header=new AfterSalesHeaderEditModel {IssueDescription="成品故障",IssueProductId=Guid.NewGuid(),IssueCustomerSkuSnapshot="成品型号"};
        var line=AfterSalesLineModel.CreateNew(1);line.ProductId=Guid.NewGuid();line.CustomerSkuSnapshot="配件型号";
        StringAssert.Contains(header.Validate(true)!,"Header.ResponsibleSupplierId");
        header.ResponsibleSupplierId=Guid.NewGuid();StringAssert.Contains(header.Validate(true)!,"Header.ServiceAmountCny");
        header.ServiceAmountCny=0;Assert.IsNull(header.Validate(true));
        Assert.AreEqual(header.IssueProductId,header.ToDto().IssueProductId);Assert.AreEqual(line.ProductId,line.ToDto().ProductId);
        Assert.AreEqual("成品型号",header.ToDto().IssueCustomerSkuSnapshot);Assert.AreEqual("配件型号",line.ToDto().CustomerSkuSnapshot);
        Assert.IsTrue(string.IsNullOrWhiteSpace(line.ToDto().UnitSnapshot));
        Assert.IsNull(typeof(AfterSalesEditLineDto).GetProperty("IssueProductId"));
    }
    /// <summary>供应商缓存回显不制造修改，用户选择和清空更新整单责任方。</summary>
    [TestMethod] public void SupplierSelectionBelongsToHeader()
    {
        var id=Guid.NewGuid();var header=new AfterSalesHeaderEditModel();header.Load(new(){ResponsibleSupplierId=id});
        var supplier=new SupplierDto {Uid=id};header.SynchronizeSupplier(supplier);
        Assert.IsFalse(header.IsDirty);Assert.AreSame(supplier,header.SelectedResponsibleSupplier);Assert.AreEqual(id,header.ResponsibleSupplierId);
        header.SelectedResponsibleSupplier=new(){Uid=Guid.NewGuid()};Assert.IsTrue(header.IsDirty);
        Assert.AreEqual(header.SelectedResponsibleSupplier.Uid,header.ToDto().ResponsibleSupplierId);
        header.SelectedResponsibleSupplier=null;Assert.IsNull(header.ResponsibleSupplierId);
    }
    /// <summary>列表金额保留“已知金额”和“仍有待定”两种事实。</summary>
    [TestMethod]
    public void ListMapping_ShouldExposeUndeterminedAmount()
    {
        var row = new AfterSalesListItemDto
        {
            KnownAmountCny = 0,
            HasUndeterminedAmount = true,
            CustomerCodeSnapshot = "C001",
            CustomerNameSnapshot = "客户一"
        }.Adapt<AfterSalesManagerRowModel>();

        Assert.AreEqual("待定", row.AmountText);
        Assert.AreEqual("C001 · 客户一", row.CustomerDisplay);
        Assert.AreEqual("免费",new AfterSalesListItemDto {KnownAmountCny=0}.Adapt<AfterSalesManagerRowModel>().AmountText);
        StringAssert.Contains(new AfterSalesListItemDto {KnownAmountCny=12}.Adapt<AfterSalesManagerRowModel>().AmountText,"12.00");
    }

    /// <summary>保存成功后才确认，并使用保存返回的最新版本。</summary>
    [TestMethod]
    public async Task FeatureService_ShouldPreserveSaveSuccessWhenConfirmFails()
    {
        var api = new RecordingAfterSalesApi
        {
            SaveResult = ServiceResult<AfterSalesCommandResult>.Ok(new AfterSalesCommandResult
            {
                Uid = Guid.NewGuid(), Version = 7, AfterSalesNo = "CR2026090001"
            }),
            ConfirmResult = ServiceResult<AfterSalesCommandResult>.Fail("SOURCE_EXIT_BLOCKED", "来源暂不可确认")
        };
        var service = new AfterSalesFeatureService(api, new PassThroughApiCallRunner());
        var command = new SaveAfterSalesCommand { Uid = api.SaveResult.Data.Uid, Version = 6 };

        var saved = await service.SaveAsync(command, false);
        Assert.IsTrue(saved.Success);
        var confirmed = await service.ConfirmAsync(command.Uid,
            new ConfirmAfterSalesDemandCommand { CommandId = Guid.NewGuid(), Version = saved.Data.Version });
        Assert.IsFalse(confirmed.Success);
        Assert.AreEqual(7, api.ConfirmedVersion);
        Assert.AreEqual(1, api.SaveCalls);
        Assert.AreEqual(1, api.ConfirmCalls);
    }

    /// <summary>Refit 路由与后端根路径一致，所有接口保留取消参数。</summary>
    [TestMethod]
    public void ApiContract_ShouldMatchAbsoluteAfterSalesRoutes()
    {
        AssertRoute(nameof(IAfterSalesApi.SearchAsync), "/api/quality-control/after-sales", typeof(GetAttribute));
        AssertRoute(nameof(IAfterSalesApi.GetAsync), "/api/quality-control/after-sales/{uid}", typeof(GetAttribute));
        AssertRoute(nameof(IAfterSalesApi.CreateAsync), "/api/quality-control/after-sales", typeof(PostAttribute));
        AssertRoute(nameof(IAfterSalesApi.SaveAsync), "/api/quality-control/after-sales/{uid}", typeof(PutAttribute));
        AssertRoute(nameof(IAfterSalesApi.ConfirmDemandAsync), "/api/quality-control/after-sales/{uid}/confirm-demand", typeof(PostAttribute));
        AssertRoute(nameof(IAfterSalesApi.CancelDemandConfirmationAsync), "/api/quality-control/after-sales/{uid}/cancel-demand-confirmation", typeof(PostAttribute));
        foreach (var method in typeof(IAfterSalesApi).GetMethods())
            Assert.AreEqual(typeof(CancellationToken), method.GetParameters().Last().ParameterType, method.Name);
    }

    /// <summary>品质菜单只暴露 Manager，详情作为受同一模块权限保护的文档路由。</summary>
    [TestMethod]
    public void Navigation_ShouldRegisterManagerAndDetail()
    {
        var registry = new NavigationRouteRegistry();
        Assert.IsTrue(registry.TryGetRoute(AfterSalesManagerViewModel.RouteKey, out var manager));
        Assert.AreEqual("QualityControl.AfterSales.View", manager.RequiredPermission);
        Assert.IsTrue(registry.TryGetRoute(AfterSalesDetailViewModel.RouteKey, out var detail));
        Assert.AreEqual("QualityControl.AfterSales.View", detail.RequiredPermission);
        var quality = registry.MenuItems.Single(item => item.RouteKey == NavigationModuleKeys.QualityControl);
        Assert.IsTrue(quality.Children.Any(item => item.RouteKey == AfterSalesManagerViewModel.RouteKey));
        Assert.IsFalse(quality.Children.Any(item => item.RouteKey == AfterSalesDetailViewModel.RouteKey));
    }

    private static void AssertRoute(string methodName, string expectedPath, Type attributeType)
    {
        var method = typeof(IAfterSalesApi).GetMethod(methodName)!;
        var attribute = method.GetCustomAttributes(attributeType, false).Single();
        var path = (string)attributeType.GetProperty("Path")!.GetValue(attribute)!;
        Assert.AreEqual(expectedPath, path);
    }

    private sealed class PassThroughApiCallRunner : IApiCallRunner
    {
        public Task<ServiceResult<T>> RunAsync<T>(Func<Task<ServiceResult<T>>> action, string failureMessage = "请求失败") => action();
        public Task<ServiceResult<T>> RunAsync<T>(Func<Task<ServiceResult<T>>> action, CancellationToken cancellationToken, string failureMessage = "请求失败") => action();
        public async Task<ServiceResult<T>> RunValueAsync<T>(Func<Task<T>> action, string successMessage = "成功", string failureMessage = "请求失败")
            => ServiceResult<T>.Ok(await action(), successMessage);
    }

    private sealed class RecordingAfterSalesApi : IAfterSalesApi
    {
        public required ServiceResult<AfterSalesCommandResult> SaveResult { get; init; }
        public required ServiceResult<AfterSalesCommandResult> ConfirmResult { get; init; }
        public int SaveCalls { get; private set; }
        public int ConfirmCalls { get; private set; }
        public int ConfirmedVersion { get; private set; }

        public Task<ServiceResult<AfterSalesCommandResult>> SaveAsync(Guid uid, SaveAfterSalesCommand command, CancellationToken cancellationToken = default)
        {
            SaveCalls++;
            return Task.FromResult(SaveResult);
        }

        public Task<ServiceResult<AfterSalesCommandResult>> ConfirmDemandAsync(Guid uid, ConfirmAfterSalesDemandCommand command, CancellationToken cancellationToken = default)
        {
            ConfirmCalls++;
            ConfirmedVersion = command.Version;
            return Task.FromResult(ConfirmResult);
        }

        public AfterSalesSearchRequest? SearchRequest { get; private set; }
        public Task<ServiceResult<PagedResponseBase<AfterSalesListItemDto>>> SearchAsync(AfterSalesSearchRequest request, CancellationToken cancellationToken = default)
        {
            SearchRequest = request;
            return Task.FromResult(ServiceResult<PagedResponseBase<AfterSalesListItemDto>>.Ok(new()
            { PageIndex = request.PageIndex, PageSize = request.PageSize, TotalCount = 125, Items = [] }));
        }
        public Task<ServiceResult<AfterSalesDetailDto>> GetAsync(Guid uid, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ServiceResult<PagedResponseBase<AfterSalesLookupDto>>> LookupAsync(string kind, AfterSalesLookupRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ServiceResult<AfterSalesCommandResult>> CreateAsync(SaveAfterSalesCommand command, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ServiceResult<AfterSalesCommandResult>> CancelDemandConfirmationAsync(Guid uid, CancelAfterSalesDemandConfirmationCommand command, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ServiceResult<AfterSalesCommandResult>> CloseAsync(Guid uid, CloseAfterSalesCommand command, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ServiceResult<AfterSalesCommandResult>> ReopenAsync(Guid uid, ReopenAfterSalesCommand command, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ServiceResult<AfterSalesCommandResult>> VoidAsync(Guid uid, VoidAfterSalesCommand command, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
