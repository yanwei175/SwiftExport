using Mapster;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Client.Wpf.Tests.Controls;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Procurement.OrderManagement;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderDetailDto;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Features.Procurement.PurchaseOrders.Models;
using SwiftExportClient.Features.Procurement.PurchaseOrders.ViewModels;
using SwiftExportClient.Features.Procurement.PurchaseOrders.Views;
using SwiftExport.Contracts.Apis.MasterData;
using SwiftExport.Contracts.DTOs.Auth;
using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;

namespace SwiftExport.Client.Wpf.Tests.Features.Procurement;

/// <summary>采购详情两价必填、精度、真实编辑器及保存负载的定向回归。</summary>
public sealed partial class PurchaseOrderManagerBatchConfirmationTests
{
    /// <summary>两类价格都必须明确填写，零价合法；负价、超四位及越界值不能进入保存负载。</summary>
    [TestMethod]
    [DataRow(null, "1", "未税")]
    [DataRow("1", null, "含税")]
    [DataRow("-1", "1", "未税")]
    [DataRow("1", "-1", "含税")]
    [DataRow("1.00001", "1", "未税")]
    [DataRow("1", "1.00001", "含税")]
    [DataRow("100000000000000", "1", "未税")]
    [DataRow("1", "100000000000000", "含税")]
    public void DetailPrices_RejectMissingNegativeOrOutOfPrecision(string? unitPrice, string? taxPrice, string field)
    {
        var line = new PurchaseOrderLineEditModel();
        line.Load(Detail(Guid.NewGuid()).Lines[0]);
        line.UnitPrice = unitPrice is null ? null : decimal.Parse(unitPrice, CultureInfo.InvariantCulture);
        line.TaxPrice = taxPrice is null ? null : decimal.Parse(taxPrice, CultureInfo.InvariantCulture);
        StringAssert.Contains(line.ValidateRow(), field);
    }

    /// <summary>有效四位价格和明确零价保持原值映射；金额按四位舍入且不参与独立脏跟踪。</summary>
    [TestMethod]
    public void DetailPrices_PreserveFourDecimalsAndRoundAmounts()
    {
        var line = new PurchaseOrderLineEditModel();
        var dto = Detail(Guid.NewGuid()).Lines[0]; dto.Quantity = 0.1m;
        line.Load(dto);
        line.UnitPrice = 1.2345m; line.TaxPrice = 0m;
        Assert.IsNull(line.ValidateRow());
        Assert.AreEqual(0.1235m, line.UnitAmount);
        Assert.AreEqual(0m, line.TaxAmount);
        var request = line.Adapt<UpdatePurchaseOrderDraftLineCommand>();
        Assert.AreEqual(1.2345m, request.UnitPrice); Assert.AreEqual(0m, request.TaxPrice);
        var expectedVersion = dto.Version;
        Assert.AreEqual(dto.Uid, request.PurchaseOrderLineId); Assert.AreEqual(expectedVersion, request.ExpectedVersion);
        line.RejectChanges(); Assert.IsFalse(line.IsDirty);
        Assert.AreEqual(dto.UnitPrice, line.UnitPrice); Assert.AreEqual(dto.TaxPrice, line.TaxPrice);
    }

    /// <summary>单价的合法边界不能被提前截断；数量乘单价超出金额范围时阻断保存。</summary>
    [TestMethod]
    public void DetailPrices_ValidateMaximumAndAmountOverflow()
    {
        var line = new PurchaseOrderLineEditModel(); line.Load(Detail(Guid.NewGuid()).Lines[0]);
        line.UnitPrice = 99999999999999.9999m; line.TaxPrice = 0m;
        Assert.IsNull(line.ValidateRow()); Assert.AreEqual(99999999999999.9999m, line.UnitAmount);
        line.Quantity = 2m;
        StringAssert.Contains(line.ValidateRow(), "金额"); Assert.IsNull(line.UnitAmount);
    }

    /// <summary>空税价草稿只能保留编辑并提示补齐，不能继续调用保存或确认接口。</summary>
    [TestMethod]
    public async Task DetailPrices_MissingTaxPriceBlocksSaveAndConfirmation()
    {
        var row = Order("PO-NO-TAX"); var f = CreateFixture(row);
        f.Orders.DetailHandler = id => { var dto = Detail(id); dto.Lines[0].TaxPrice = null; return Task.FromResult(ServiceResult<PurchaseOrderDetailDto>.Ok(dto)); };
        f.Orders.SaveHandler = (_, _) => Task.FromResult(ServiceResult<ProcurementOrderCommandResult>.Fail(ServiceErrorCodes.BusinessBlocked, "不应提交"));
        using var vm = CreateDetail(f);
        await vm.InitializeDocumentAsync(new PurchaseOrderDocumentOpenRequest { PurchaseOrderId = row.PurchaseOrderId }, CancellationToken.None);
        await vm.ConfirmCommand.ExecuteAsync(null);
        Assert.AreEqual(0, f.Orders.ConfirmCalls.Count);
        vm.Header.Remark = "保留输入";
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.AreEqual(0, f.Orders.SaveCalls.Count); Assert.IsTrue(vm.HasUnsavedChanges);
        Assert.AreEqual("保留输入", vm.Header.Remark);
    }

    /// <summary>真实含税编辑器把四位价格送入模型与保存请求；提交期间冻结界面，失败保留金额和编辑。</summary>
    [TestMethod]
    public void DetailPrices_ActualEditorPreservesFourDecimalsAndBusy() => DatePickerAssistTests.RunWithApplication(async () =>
    {
        ApplicationResourceLoader.Load(); var row = Order("PO-PRICE-UI"); var f = CreateFixture(row);
        f.Orders.DetailHandler = id => { var dto = Detail(id); dto.Lines[0].Quantity = 3m; return Task.FromResult(ServiceResult<PurchaseOrderDetailDto>.Ok(dto)); };
        var completion = new TaskCompletionSource<ServiceResult<ProcurementOrderCommandResult>>();
        f.Orders.SaveHandler = (_, _) => completion.Task;
        using var vm = CreateDetail(f);
        await vm.InitializeDocumentAsync(new PurchaseOrderDocumentOpenRequest { PurchaseOrderId = row.PurchaseOrderId }, CancellationToken.None);
        var view = new PurchaseOrderDetailView { DataContext = vm };
        var window = PriceWindow(view);
        try
        {
            window.Show(); await PriceIdle();
            var grid = PriceChild<DataGrid>(view)!; var line = vm.Lines.Items[0];
            await EditPrice(grid, line, nameof(line.UnitPrice), "1.2345");
            await EditPrice(grid, line, nameof(line.TaxPrice), "2.3456");
            Assert.AreEqual(1.2345m, line.UnitPrice); Assert.AreEqual(2.3456m, line.TaxPrice);
            Assert.AreEqual(3.7035m, line.UnitAmount); Assert.AreEqual(7.0368m, line.TaxAmount);
            Assert.IsTrue(vm.HasUnsavedChanges); Assert.IsFalse(vm.ConfirmCommand.CanExecute(null));
            var saving = vm.SaveCommand.ExecuteAsync(null);
            Assert.IsTrue(vm.IsBusy); Assert.IsFalse(grid.IsEnabled);
            Assert.AreEqual(1.2345m, f.Orders.SaveCalls.Single().Lines.Single().UnitPrice);
            Assert.AreEqual(2.3456m, f.Orders.SaveCalls.Single().Lines.Single().TaxPrice);
            completion.SetResult(ServiceResult<ProcurementOrderCommandResult>.Fail(ServiceErrorCodes.BusinessBlocked, "保留编辑"));
            await saving;
            Assert.IsTrue(vm.HasUnsavedChanges); Assert.AreEqual(2.3456m, line.TaxPrice);
        }
        finally { window.Close(); await PriceIdle(); Assert.IsNull(vm.CommitPendingEdits); }
    });

    /// <summary>数字转换失败不能用旧税价提交；修正为零后可保存，已确认单据仍保持原生只读。</summary>
    [TestMethod]
    public void DetailPrices_InvalidEditorTextCannotSubmitOldValue() => DatePickerAssistTests.RunWithApplication(async () =>
    {
        ApplicationResourceLoader.Load(); var row = Order("PO-BAD-PRICE"); var f = CreateFixture(row);
        var confirmed = false;
        f.Orders.DetailHandler = id =>
        {
            var dto = Detail(id); dto.CanEditDraft = !confirmed; dto.CanConfirm = !confirmed;
            if (confirmed) dto.ConfirmationStatus = SwiftExport.Contracts.SysEnums.ConfirmationStatusEnum.已确认;
            return Task.FromResult(ServiceResult<PurchaseOrderDetailDto>.Ok(dto));
        };
        f.Orders.SaveHandler = (_, _) => Task.FromResult(ServiceResult<ProcurementOrderCommandResult>.Fail(ServiceErrorCodes.BusinessBlocked, "保留编辑"));
        using var vm = CreateDetail(f);
        await vm.InitializeDocumentAsync(new PurchaseOrderDocumentOpenRequest { PurchaseOrderId = row.PurchaseOrderId }, CancellationToken.None);
        var view = new PurchaseOrderDetailView { DataContext = vm }; var window = PriceWindow(view);
        try
        {
            window.Show(); await PriceIdle(); vm.Header.Remark = "保留备注";
            var grid = PriceChild<DataGrid>(view)!; var line = vm.Lines.Items[0];
            var editor = await EditPrice(grid, line, nameof(line.TaxPrice), "invalid");
            Assert.IsTrue(Validation.GetHasError(editor));
            await vm.SaveCommand.ExecuteAsync(null);
            Assert.AreEqual(0, f.Orders.SaveCalls.Count); Assert.AreEqual("invalid", editor.Text);
            editor.Text = ""; editor.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
            await vm.SaveCommand.ExecuteAsync(null); Assert.AreEqual(0, f.Orders.SaveCalls.Count);
            editor = await EditPrice(grid, line, nameof(line.TaxPrice), "0");
            await vm.SaveCommand.ExecuteAsync(null);
            Assert.AreEqual(0m, f.Orders.SaveCalls.Single().Lines.Single().TaxPrice);
            confirmed = true; await vm.RefreshCommand.ExecuteAsync(null); await PriceIdle();
            Assert.IsTrue(grid.IsReadOnly); Assert.IsFalse(vm.SaveCommand.CanExecute(null));
        }
        finally { window.Close(); await PriceIdle(); }
    });

    /// <summary>只有读取和确认权限的用户不能进入价格编辑；确认权限不隐含草稿编辑权限。</summary>
    [TestMethod]
    public void DetailPrices_ConfirmPermissionDoesNotEnableEditing() => DatePickerAssistTests.RunWithApplication(async () =>
    {
        ApplicationResourceLoader.Load(); var row = Order("PO-READ-PRICE"); var f = CreateFixture(row);
        f.Orders.DetailHandler = id => Task.FromResult(ServiceResult<PurchaseOrderDetailDto>.Ok(Detail(id)));
        using var vm = new PurchaseOrderDetailViewModel(f.Orders,
            DispatchProxy.Create<IProcurementMasterDataQueryApi, UnexpectedApiProxy>(), f.Dialogs, new MessageStub(), new NavigationStub(),
            new CurrentUserStub([PermissionKeys.ProcurementOrderRead, PermissionKeys.ProcurementOrderConfirm]),
            commandSession: f.Session, apiCallRunner: new ApiCallRunnerStub());
        await vm.InitializeDocumentAsync(new PurchaseOrderDocumentOpenRequest { PurchaseOrderId = row.PurchaseOrderId }, CancellationToken.None);
        var view = new PurchaseOrderDetailView { DataContext = vm }; var window = PriceWindow(view);
        try
        {
            window.Show(); await PriceIdle();
            Assert.IsTrue(PriceChild<DataGrid>(view)!.IsReadOnly); Assert.IsFalse(vm.SaveCommand.CanExecute(null));
        }
        finally { window.Close(); await PriceIdle(); }
    });

    /// <summary>仅创建定向验证所需的离屏窗口，不启动完整客户端或连接业务 API。</summary>
    private static Window PriceWindow(object content) => new()
    { Content = content, Width = 1600, Height = 1000, Left = -5000, Top = -5000, ShowActivated = false, ShowInTaskbar = false };

    /// <summary>使用真实原生单元格编辑器输入，保留数字转换与行提交语义。</summary>
    private static async Task<TextBox> EditPrice(DataGrid grid, PurchaseOrderLineEditModel line, string field, string text)
    {
        var column = grid.Columns.OfType<DataGridTextColumn>().Single(column => (column.Binding as Binding)?.Path.Path == field);
        grid.ScrollIntoView(line, column); grid.CurrentCell = new DataGridCellInfo(line, column);
        grid.Focus(); Assert.IsTrue(grid.BeginEdit()); await PriceIdle();
        var editor = (TextBox)column.GetCellContent(line); editor.Text = text;
        editor.GetBindingExpression(TextBox.TextProperty)!.UpdateSource(); await PriceIdle(); return editor;
    }

    /// <summary>只定位当前已生成的验证控件；不用于生产代码或缓存虚拟化容器。</summary>
    private static T? PriceChild<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match) return match;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            if (PriceChild<T>(VisualTreeHelper.GetChild(root, index)) is { } child) return child;
        return null;
    }

    /// <summary>等待当前绑定和布局完成，不使用任意时长延迟。</summary>
    private static async Task PriceIdle() => await Dispatcher.CurrentDispatcher.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);
}
