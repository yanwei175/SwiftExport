using Mapster;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Client.Wpf.Tests.Controls;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;
using SwiftExport.Contracts.Apis.MasterData;
using SwiftExport.Contracts.Apis.Procurement;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.MasterData.ProcurementLookups;
using SwiftExport.Contracts.DTOs.Procurement.OrderManagement;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderDetailDto;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Authentication;
using SwiftExportClient.Features.Procurement.PurchaseOrders.Models;
using SwiftExportClient.Features.Procurement.PurchaseOrders.Services;
using SwiftExportClient.Features.Procurement.PurchaseOrders.ViewModels;
using SwiftExportClient.Features.Procurement.PurchaseOrders.Views;
using SwiftExportClient.Services.UI;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SwiftExport.Client.Wpf.Tests.Features.Procurement;

/// <summary>采购详情手工创建负载、头字段权限和新增行命令的定向回归。</summary>
public sealed partial class PurchaseOrderManagerBatchConfirmationTests
{
    /// <summary>无客户型号及同型号多产品都保留明确产品身份，创建负载不携带导入匹配意图。</summary>
    [TestMethod]
    [DataRow("")]
    [DataRow("相同客户型号")]
    public async Task DetailEditing_CreateUsesProductIdentityAndSubmitsDiscountRemark(string customerSku)
    {
        using var f = new DetailEditingFixture(PermissionKeys.ProcurementOrderCreate);
        await PrepareDetailCreate(f.Vm, customerSku);
        f.Vm.AddLineCommand.Execute(null);
        var second = f.Vm.Lines.Items[1];
        second.ApplyProduct(DetailEditingProduct(customerSku));
        f.Vm.Header.DiscountRemark = "整单折扣说明";
        await f.Vm.CreateCommand.ExecuteAsync(null);
        var request = f.Creates.Single();
        Assert.AreEqual("整单折扣说明", request.DiscountRemark);
        Assert.HasCount(2, request.Lines);
        for (var index = 0; index < request.Lines.Count; index++)
        {
            var input = request.Lines[index]; var line = f.Vm.Lines.Items[index];
            Assert.AreEqual(line.ProductId, input.ProductId);
            Assert.IsNull(input.CustomerSku); Assert.IsNull(input.SelectedCustomerProductId);
            Assert.AreEqual(line.LinkSku, input.LinkSku);
            Assert.AreEqual(line.SupplierSku, input.SupplierSku);
            Assert.AreEqual(line.UnitPrice, input.UnitPrice); Assert.AreEqual(line.TaxPrice, input.TaxPrice);
            Assert.IsNull(line.Adapt<CreateIndependentPurchaseOrderLineCommand>().CustomerSku);
        }
    }

    /// <summary>创建采用去除首尾空白后的五百字上限，超限不冻结或发送且保留原输入。</summary>
    [TestMethod]
    [DataRow(500, true)]
    [DataRow(501, false)]
    public async Task DetailEditing_CreateValidatesDiscountRemarkLength(int length, bool sends)
    {
        using var f = new DetailEditingFixture(PermissionKeys.ProcurementOrderCreate);
        await PrepareDetailCreate(f.Vm, "客户型号");
        var remark = " " + new string('折', length) + " "; f.Vm.Header.DiscountRemark = remark;
        await f.Vm.CreateCommand.ExecuteAsync(null);
        Assert.HasCount(sends ? 1 : 0, f.Creates);
        Assert.AreEqual(remark, f.Vm.Header.DiscountRemark);
        Assert.IsTrue(f.Vm.HasUnsavedChanges); Assert.IsFalse(f.Vm.HasPendingWrite);
        if (!sends) StringAssert.Contains(f.Messages.Warnings.Single(), "500");
    }

    /// <summary>创建长度门禁不得意外收紧现有保存契约，五百零一字仍交由保存端校验。</summary>
    [TestMethod]
    public async Task DetailEditing_SaveKeepsExistingDiscountRemarkRule()
    {
        using var f = new DetailEditingFixture(PermissionKeys.ProcurementOrderRead, PermissionKeys.ProcurementOrderEdit);
        await f.Vm.InitializeDocumentAsync(new PurchaseOrderDocumentOpenRequest { PurchaseOrderId = f.OrderId }, CancellationToken.None);
        f.Vm.Header.DiscountRemark = new string('折', 501);
        await f.Vm.SaveCommand.ExecuteAsync(null);
        Assert.AreEqual(f.Vm.Header.DiscountRemark, f.Saves.Single().DiscountRemark);
    }

    /// <summary>移除仅接纳当前选中的本页新增行，选择改变必须通知按钮重算，直接执行同样不能绕过。</summary>
    [TestMethod]
    public async Task DetailEditing_RemoveTracksCurrentOwnedNewRow()
    {
        using var f = new DetailEditingFixture(PermissionKeys.ProcurementOrderCreate);
        await PrepareDetailCreate(f.Vm, ""); var vm = f.Vm; var first = vm.Lines.Items[0];
        vm.AddLineCommand.Execute(null); var second = vm.Lines.Items[1];
        Assert.IsFalse(vm.RemoveLineCommand.CanExecute(first));
        var notifications = 0; vm.RemoveLineCommand.CanExecuteChanged += (_, _) => notifications++;
        vm.Lines.SelectedItem = first;
        Assert.IsTrue(vm.RemoveLineCommand.CanExecute(first)); Assert.IsTrue(notifications > 0);
        notifications = 0; vm.Lines.SelectedItem = second;
        Assert.IsTrue(notifications > 0); Assert.IsFalse(vm.RemoveLineCommand.CanExecute(first));
        vm.RemoveLineCommand.Execute(first); Assert.HasCount(2, vm.Lines.Items);
        var foreign = new PurchaseOrderLineListModel().AddNew(new PurchaseOrderLineDetailDto { Uid = Guid.NewGuid() });
        vm.Lines.SelectedItem = foreign;
        Assert.IsFalse(vm.RemoveLineCommand.CanExecute(foreign)); vm.RemoveLineCommand.Execute(foreign);
        Assert.HasCount(2, vm.Lines.Items);
        vm.Lines.SelectedItem = second; vm.RemoveLineCommand.Execute(second);
        Assert.HasCount(1, vm.Lines.Items); Assert.IsNull(vm.Lines.SelectedItem);
    }

    /// <summary>真实头字段绑定区分创建与编辑权限，保存态新增移除禁用；身份刷新后只读状态立即生效。</summary>
    [TestMethod]
    [DataRow(true, true)]
    [DataRow(false, true)]
    [DataRow(false, false)]
    public void DetailEditing_ActualBindingsRespectPermissionsAndReadOnly(bool create, bool editable) => DatePickerAssistTests.RunWithApplication(async () =>
    {
        ApplicationResourceLoader.Load();
        using var f = new DetailEditingFixture(create ? [PermissionKeys.ProcurementOrderCreate]
            : [PermissionKeys.ProcurementOrderRead, editable ? PermissionKeys.ProcurementOrderEdit : PermissionKeys.ProcurementOrderCreate]);
        if (create) await PrepareDetailCreate(f.Vm, "");
        else await f.Vm.InitializeDocumentAsync(new PurchaseOrderDocumentOpenRequest { PurchaseOrderId = f.OrderId }, CancellationToken.None);
        var view = new PurchaseOrderDetailView { DataContext = f.Vm }; var window = PriceWindow(view);
        try
        {
            window.Show(); await PriceIdle();
            var fields = DetailEditingHeaderFields(view);
            Assert.IsTrue(fields.All(field => field.IsEnabled == editable));
            var add = DetailEditingChildren<Button>(view).Single(button => Equals(button.Content, "新增采购行"));
            var remove = DetailEditingChildren<Button>(view).Single(button => Equals(button.Content, "删除新增行"));
            Assert.AreEqual(create, add.IsEnabled); Assert.IsFalse(remove.IsEnabled);
            f.Vm.Lines.SelectedItem = f.Vm.Lines.Items[0]; await PriceIdle();
            Assert.AreEqual(create, remove.IsEnabled);
            if (editable)
            {
                fields[0].SetCurrentValue(TextBox.TextProperty, "保留头字段修改");
                fields[0].GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
                Assert.IsTrue(f.Vm.HeaderHasChanges);
            }
            f.User.SetCurrentUser(f.User.CurrentUser!); await PriceIdle();
            Assert.IsTrue(f.Vm.IsReadOnly); Assert.IsTrue(fields.All(field => !field.IsEnabled));
            Assert.IsFalse(add.IsEnabled); Assert.IsFalse(remove.IsEnabled);
            var count = f.Vm.Lines.Items.Count;
            f.Vm.AddLineCommand.Execute(null); f.Vm.RemoveLineCommand.Execute(f.Vm.Lines.SelectedItem);
            Assert.HasCount(count, f.Vm.Lines.Items);
        }
        finally { window.Close(); await PriceIdle(); }
    });

    /// <summary>真实绑定在创建或保存忙碌及未知结果期间冻结头字段和新增移除，保留原输入。</summary>
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void DetailEditing_ActualBindingsFreezeWhileBusyAndPending(bool create) => DatePickerAssistTests.RunWithApplication(async () =>
    {
        ApplicationResourceLoader.Load();
        using var f = new DetailEditingFixture(PermissionKeys.ProcurementOrderRead, PermissionKeys.ProcurementOrderEdit, PermissionKeys.ProcurementOrderCreate);
        if (create) await PrepareDetailCreate(f.Vm, "");
        else await f.Vm.InitializeDocumentAsync(new PurchaseOrderDocumentOpenRequest { PurchaseOrderId = f.OrderId }, CancellationToken.None);
        f.Vm.Header.Remark = "待核实备注"; f.Vm.Lines.SelectedItem = f.Vm.Lines.Items[0];
        var completion = new TaskCompletionSource<ServiceResult<ProcurementOrderCommandResult>>();
        f.CreateHandler = _ => completion.Task; f.SaveHandler = _ => completion.Task;
        var view = new PurchaseOrderDetailView { DataContext = f.Vm }; var window = PriceWindow(view);
        try
        {
            window.Show(); await PriceIdle(); var fields = DetailEditingHeaderFields(view);
            Assert.IsTrue(fields.All(field => field.IsEnabled));
            var submitting = (create ? f.Vm.CreateCommand : f.Vm.SaveCommand).ExecuteAsync(null);
            await PriceIdle(); Assert.IsTrue(f.Vm.IsBusy);
            Assert.IsTrue(fields.All(field => !field.IsEnabled));
            Assert.IsFalse(f.Vm.AddLineCommand.CanExecute(null)); Assert.IsFalse(f.Vm.RemoveLineCommand.CanExecute(f.Vm.Lines.SelectedItem));
            completion.SetResult(ServiceResult<ProcurementOrderCommandResult>.Fail(ServiceErrorCodes.OutcomeUnknown, "结果未知"));
            await submitting; await PriceIdle();
            Assert.IsFalse(f.Vm.IsBusy); Assert.IsTrue(f.Vm.HasPendingWrite);
            Assert.IsTrue(fields.All(field => !field.IsEnabled));
            var count = f.Vm.Lines.Items.Count;
            f.Vm.AddLineCommand.Execute(null); f.Vm.RemoveLineCommand.Execute(f.Vm.Lines.SelectedItem);
            Assert.HasCount(count, f.Vm.Lines.Items); Assert.AreEqual("待核实备注", f.Vm.Header.Remark);
        }
        finally { completion.TrySetResult(ServiceResult<ProcurementOrderCommandResult>.Fail(ServiceErrorCodes.OutcomeUnknown, "结束测试")); window.Close(); await PriceIdle(); }
    });

    /// <summary>先选择采购主体再选择产品，避免主体切换清除有效候选。</summary>
    private static async Task PrepareDetailCreate(PurchaseOrderDetailViewModel vm, string customerSku)
    {
        await vm.InitializeDocumentAsync(null, CancellationToken.None);
        vm.SelectedCustomer = new() { Uid = Guid.NewGuid() }; vm.SelectedSupplier = new() { Uid = Guid.NewGuid() };
        vm.Lines.Items[0].ApplyProduct(DetailEditingProduct(customerSku));
    }

    /// <summary>提供明确产品身份和完整两价，客户型号只作为候选显示信息。</summary>
    private static ProcurementProductCandidateDto DetailEditingProduct(string customerSku) => new()
    { Uid = Guid.NewGuid(), Sku = "SKU", LinkSku = "LINK", CustomerSku = customerSku, SupplierSku = "SUP", Unit = "PCS", UnitPrice = 1m, TaxPrice = 1.1m };

    /// <summary>通过真实绑定路径定位本轮三项头字段，不依赖控件排列。</summary>
    private static TextBox[] DetailEditingHeaderFields(DependencyObject root) => new[] { "Header.Remark", "Header.PackagingRequirements", "Header.DiscountRemark" }
        .Select(path => DetailEditingChildren<TextBox>(root).Single(field => field.GetBindingExpression(TextBox.TextProperty)?.ParentBinding.Path.Path == path)).ToArray();

    /// <summary>仅在测试中枚举已经生成的控件，不在产品代码中缓存视觉容器。</summary>
    private static IEnumerable<T> DetailEditingChildren<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match) yield return match;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (var child in DetailEditingChildren<T>(VisualTreeHelper.GetChild(root, index))) yield return child;
    }

    /// <summary>采购详情离线夹具；捕获真实创建保存负载，默认明确拒绝以保留输入而不连接服务端。</summary>
    private sealed class DetailEditingFixture : IDisposable
    {
        public Guid OrderId { get; } = Guid.NewGuid();
        public CurrentUserService User { get; } = new();
        public DetailEditingMessages Messages { get; } = new();
        public List<CreateIndependentPurchaseOrderCommand> Creates { get; } = [];
        public List<UpdatePurchaseOrderDraftCommand> Saves { get; } = [];
        public Func<CreateIndependentPurchaseOrderCommand, Task<ServiceResult<ProcurementOrderCommandResult>>>? CreateHandler { get; set; }
        public Func<UpdatePurchaseOrderDraftCommand, Task<ServiceResult<ProcurementOrderCommandResult>>>? SaveHandler { get; set; }
        public PurchaseOrderDetailViewModel Vm { get; }

        /// <summary>只注入明确权限和内存 API；不启用应用壳层或数据库。</summary>
        public DetailEditingFixture(params string[] permissions)
        {
            User.SetCurrentUser(new() { UserId = "u", Permissions = permissions.ToList() });
            var api = DispatchProxy.Create<IProcurementOrderApi, ProcurementReversalSessionTests.ApiProxy>();
            ((ProcurementReversalSessionTests.ApiProxy)(object)api).Handler = (method, args) => method!.Name switch
            {
                nameof(IProcurementOrderApi.GetDetailAsync) => Task.FromResult(ServiceResult<PurchaseOrderDetailDto>.Ok(Detail((Guid)args![0]!))),
                nameof(IProcurementOrderApi.CreateAsync) => Create((CreateIndependentPurchaseOrderCommand)args![0]!),
                nameof(IProcurementOrderApi.SaveAsync) => Save((UpdatePurchaseOrderDraftCommand)args![1]!),
                _ => throw new NotSupportedException(method.Name)
            };
            Vm = new(api, DispatchProxy.Create<IProcurementMasterDataQueryApi, UnexpectedApiProxy>(), new DialogServiceStub(), Messages,
                new NavigationStub(), User, apiCallRunner: new ApiCallRunnerStub());
        }

        /// <summary>捕获冻结后实际发送的创建命令。</summary>
        private Task<ServiceResult<ProcurementOrderCommandResult>> Create(CreateIndependentPurchaseOrderCommand command)
        { Creates.Add(command); return CreateHandler?.Invoke(command) ?? Task.FromResult(ServiceResult<ProcurementOrderCommandResult>.Fail(ServiceErrorCodes.BusinessBlocked, "保留创建输入")); }

        /// <summary>捕获冻结后实际发送的保存命令。</summary>
        private Task<ServiceResult<ProcurementOrderCommandResult>> Save(UpdatePurchaseOrderDraftCommand command)
        { Saves.Add(command); return SaveHandler?.Invoke(command) ?? Task.FromResult(ServiceResult<ProcurementOrderCommandResult>.Fail(ServiceErrorCodes.BusinessBlocked, "保留保存输入")); }

        /// <summary>解除页面事件订阅和查询资源。</summary>
        public void Dispose() => Vm.Dispose();
    }

    /// <summary>收集本地校验反馈，便于断言阻断原因而不弹出真实消息。</summary>
    private sealed class DetailEditingMessages : IMessageService
    {
        public List<string> Warnings { get; } = [];
        /// <inheritdoc/>
        public void ShowWarning(string title, string message) => Warnings.Add(message);
        /// <inheritdoc/>
        public void ShowInfo(string title, string message) { }
        /// <inheritdoc/>
        public void ShowSuccess(string title, string message) { }
        /// <inheritdoc/>
        public void ShowError(string title, string message) { }
    }
}
