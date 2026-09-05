using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.Apis.ProductApis;
using SwiftExport.Contracts.DTOs.Products.ProductMasters;
using SwiftExport.Contracts.DTOs.Products.ProductMasters.Responses;
using SwiftExport.Contracts.DTOs.Suppliers;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Features.Products.ProductList.ViewModels;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.Caching;
using SwiftExportClient.Services.UI;

namespace SwiftExport.Client.Wpf.Tests.Features.Products;

/// <summary>验证详情页供应商选择、显式清空和撤销同步；控件焦点行为由 Lookup 专项测试验证。</summary>
[TestClass]
public sealed class ProductMasterSupplierEditingTests
{
    /// <summary>新候选只在提交后改头部，清空不接纳其他草稿，撤销恢复原选择。</summary>
    [TestMethod]
    public void Supplier_ShouldCommitClearAndRestoreSelectionOnReject()
    {
        using var vm = CreateVm();
        var original = Detail();
        vm.Header.Load(original);
        vm.RejectDetailChangesCommand.Execute(null);
        Assert.AreEqual(original.Master.DefaultSupplierId, vm.SelectedSupplier!.Uid);
        vm.Header.ProductTag = "未保存标签";
        var selected = new SupplierDto { Uid = Guid.NewGuid(), SupplierName = "新供应商" };
        vm.SelectedSupplier = selected;
        Assert.AreEqual(original.Master.DefaultSupplierId, vm.Header.DefaultSupplierId);
        vm.CommitSupplierCommand.Execute(null);
        Assert.AreEqual(selected.Uid, vm.Header.ToWriteDto().DefaultSupplierId);
        Assert.AreEqual("新供应商", vm.Header.DefaultSupplierName);
        vm.ClearSupplierCommand.Execute(null);
        Assert.AreEqual(Guid.Empty, vm.Header.DefaultSupplierId);
        Assert.AreEqual(string.Empty, vm.Header.DefaultSupplierName);
        Assert.IsNull(vm.SelectedSupplier);
        Assert.AreEqual("未保存标签", vm.Header.ProductTag);
        Assert.IsNull(vm.Header.ValidateRow(), "供应商仍是可选资料。");
        Assert.IsTrue(vm.HasUnsavedChanges);
        vm.RejectDetailChangesCommand.Execute(null);
        Assert.AreEqual(original.Master.DefaultSupplierId, vm.SelectedSupplier!.Uid);
        Assert.AreEqual("原供应商", vm.SelectedSupplier.SupplierName);
        Assert.IsFalse(vm.HasUnsavedChanges);
    }

    /// <summary>原来没有供应商时，撤销新增选择恢复为空，不留下旧控件显示对象。</summary>
    [TestMethod]
    public void Reject_ShouldRestoreEmptySupplier()
    {
        using var vm = CreateVm();
        vm.Header.Load(new ProductMasterDetailDto { Master = new() { MasterSku = "M" } });
        vm.SelectedSupplier = new SupplierDto { Uid = Guid.NewGuid(), SupplierName = "临时供应商" };
        vm.CommitSupplierCommand.Execute(null);
        vm.RejectDetailChangesCommand.Execute(null);
        Assert.IsNull(vm.SelectedSupplier);
        Assert.AreEqual(Guid.Empty, vm.Header.DefaultSupplierId);
        Assert.AreEqual(string.Empty, vm.Header.DefaultSupplierName);
        Assert.IsFalse(vm.HasUnsavedChanges);
    }

    /// <summary>读取和保存回填同步选择；保存失败不清除本地供应商编辑。</summary>
    [TestMethod]
    public async Task LoadAndSave_ShouldSynchronizeSelectionOnlyOnSuccess()
    {
        var detail = Detail();
        var failSave = true;
        var api = Proxy<IProductMasterApi>((method, _) => method.Name switch
        {
            nameof(IProductMasterApi.GetDetailAsync) => Task.FromResult(ServiceResult<ProductMasterDetailDto>.Ok(detail)),
            nameof(IProductMasterApi.SaveDetailAsync) => Task.FromResult(failSave
                ? ServiceResult<SaveProductMasterDetailResponse>.Fail("保存失败")
                : ServiceResult<SaveProductMasterDetailResponse>.Ok(new() { Detail = detail })),
            _ => throw new AssertFailedException(method.Name)
        });
        using var vm = CreateVm(api);
        vm.Header.Load(detail);
        await vm.RefreshCommand.ExecuteAsync(null);
        Assert.AreEqual("原供应商", vm.SelectedSupplier!.SupplierName);
        var selected = new SupplierDto { Uid = Guid.NewGuid(), SupplierName = "本地修改" };
        vm.SelectedSupplier = selected;
        vm.CommitSupplierCommand.Execute(null);
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.AreSame(selected, vm.SelectedSupplier);
        Assert.AreEqual(selected.Uid, vm.Header.DefaultSupplierId);
        Assert.IsTrue(vm.HasUnsavedChanges);
        failSave = false;
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.AreEqual(detail.Master.DefaultSupplierId, vm.SelectedSupplier!.Uid);
        Assert.AreEqual("原供应商", vm.SelectedSupplier.SupplierName);
        Assert.IsFalse(vm.HasUnsavedChanges);
    }

    /// <summary>页面仍用通用控件并显式启用保留选择、名称展示和清空命令。</summary>
    [TestMethod]
    public void View_ShouldPreserveSelectionAndWireExplicitClear()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        Assert.IsNotNull(root);
        var view = XDocument.Load(Path.Combine(root.FullName, "SwiftExport.Client.Wpf", "Features", "Products", "ProductList", "Views", "ProductMasterDetailView.xaml"));
        var lookup = view.Descendants().Single(node => node.Name.LocalName == "LookupTextBox"
            && (string?)node.Attribute("SelectedItem") == "{Binding SelectedSupplier, Mode=TwoWay}");
        Assert.AreEqual("False", (string?)lookup.Attribute("ClearSelectionOnEdit"));
        Assert.AreEqual("SupplierName", (string?)lookup.Attribute("DisplayMemberPath"));
        Assert.AreEqual("{Binding Header.DefaultSupplierName, Mode=OneWay}", (string?)lookup.Attribute("Text"));
        Assert.AreEqual("{Binding ClearSupplierCommand}", (string?)lookup.Attribute("SelectionClearedCommand"));
        Assert.AreEqual("{Binding CommitSupplierCommand}", (string?)lookup.Attribute("SelectionCommittedCommand"));
    }

    private static ProductMasterDetailDto Detail() => new()
    {
        Uid = Guid.NewGuid(), Version = 1, DefaultSupplierName = "原供应商",
        Master = new() { MasterSku = "M", DefaultSupplierId = Guid.NewGuid() }
    };

    private static ProductMasterDetailViewModel CreateVm(IProductMasterApi? api = null) => new(
        api!, null!, Proxy<IApiCallRunner>((_, args) => ((Delegate)args[0]!).DynamicInvoke()),
        Proxy<IReferenceDataCacheService>((method, _) => method.Name == "get_ProductCategories"
            ? new ReadOnlyObservableCollection<ProductCategoryCacheNode>(new ObservableCollection<ProductCategoryCacheNode>())
            : throw new AssertFailedException(method.Name)),
        Proxy<IMessageService>((_, _) => null), null!, null!, new ProductMasterSyncDialogStub());

    private static T Proxy<T>(Func<MethodInfo, object?[], object?> invoke) where T : class
    {
        var proxy = DispatchProxy.Create<T, TestProxy>();
        ((TestProxy)(object)proxy).InvokeMethod = invoke;
        return proxy;
    }

    /// <summary>仅提供当前测试需要的依赖，禁止隐式请求网络或弹窗。</summary>
    public class TestProxy : DispatchProxy
    {
        public Func<MethodInfo, object?[], object?> InvokeMethod { get; set; } = null!;
        /// <summary>转发受控调用，未配置的方法由测试直接拒绝。</summary>
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => InvokeMethod(targetMethod!, args ?? []);
    }
}
