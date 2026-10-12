using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Threading;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;
using SwiftExport.Contracts.DTOs.Products.ProductMasters;
using SwiftExportClient.Features.Products.ProductList.Models;
using SwiftExportClient.Features.Products.ProductList.ViewModels;
using SwiftExportClient.Services.UI;

namespace SwiftExport.Client.Wpf.Tests.Features.Products;

/// <summary>验证主产品主图预览、命令刷新、删除选择与原生数值单元格空值提交。</summary>
[TestClass]
[DoNotParallelize]
public sealed class ProductMasterInteractionTests
{
    /// <summary>新增行不能打开服务端详情，但切回已保存行必须立即刷新编辑按钮。</summary>
    [TestMethod]
    public void ManagerSelection_ShouldNotifyDetailCommandAfterAddingRow()
    {
        using var vm = new ProductMasterManagerViewModel(null!, null!, null!, null!, null!, null!, null!, null!, new ImageAddressResolverStub(), new ProductMasterSyncDialogStub());
        vm.Masters.LoadFromDtos([Master("已保存")]);
        var saved = vm.Masters.Items.Single();
        vm.SelectedMaster = saved;
        vm.AddRowCommand.Execute(null);
        Assert.IsFalse(vm.OpenDetailCommand.CanExecute(null));
        var notified = false;
        vm.OpenDetailCommand.CanExecuteChanged += (_, _) => notified = true;
        vm.SelectedMaster = saved;
        Assert.IsTrue(notified, "选择改变必须通知按钮重新计算启用状态。");
        Assert.IsTrue(vm.OpenDetailCommand.CanExecute(null));
        Assert.AreEqual(2, vm.Masters.Items.Count);
    }

    /// <summary>主图预览响应路径编辑、默认切换、删除及回滚，未设置默认时为空。</summary>
    [TestMethod]
    public void DetailPreview_ShouldFollowMainImageAndListChanges()
    {
        using var vm = new ProductMasterDetailViewModel(null!, null!, null!, null!, null!, null!, null!, new ProductMasterSyncDialogStub());
        vm.Neutrals.LoadFromDtos([Neutral("首行", true), Neutral("末行")]);
        var first = vm.Neutrals.Items.First();
        var last = vm.Neutrals.Items.Last();
        Assert.AreEqual("首行.jpg", vm.MainImagePath);
        var notified = false;
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.MainImagePath)) notified = true; };
        first.ImagePath = "更换.jpg";
        Assert.IsTrue(notified);
        Assert.AreEqual("更换.jpg", vm.MainImagePath);
        last.IsMainImage = true;
        vm.SetMainImageCommand.Execute(last);
        Assert.AreEqual("末行.jpg", vm.MainImagePath);
        vm.Neutrals.Remove(last);
        Assert.IsNull(vm.MainImagePath);
        vm.Neutrals.RejectChanges();
        Assert.AreEqual("首行.jpg", vm.MainImagePath);
        vm.Neutrals.Clear();
        Assert.IsNull(vm.MainImagePath);
    }

    /// <summary>管理页仅新增行免确认；混合删除取消时保留数据，同意后选择最后一个剩余项。</summary>
    [TestMethod]
    public async Task ManagerDelete_ShouldConfirmOnlyPersistedRowsAndSelectLast()
    {
        var dialogs = new TestDialogs();
        using var vm = new ProductMasterManagerViewModel(null!, null!, null!, null!, null!, dialogs, null!, null!, new ImageAddressResolverStub(), new ProductMasterSyncDialogStub());
        vm.Masters.LoadFromDtos([Master("首行"), Master("末行")]);
        var first = vm.Masters.Items.First();
        var last = vm.Masters.Items.Last();
        vm.AddRowCommand.Execute(null);
        await vm.DeleteSelectedCommand.ExecuteAsync(null);
        Assert.AreEqual(0, dialogs.ConfirmCount);
        Assert.AreSame(last, vm.SelectedMaster);
        vm.AddRowCommand.Execute(null);
        var added = vm.SelectedMaster!;
        added.IsChecked = first.IsChecked = true;
        dialogs.Result = false;
        await vm.DeleteSelectedCommand.ExecuteAsync(null);
        Assert.AreEqual(1, dialogs.ConfirmCount);
        Assert.AreEqual(3, vm.Masters.Items.Count);
        Assert.AreSame(added, vm.SelectedMaster);
        dialogs.Result = true;
        await vm.DeleteSelectedCommand.ExecuteAsync(null);
        Assert.AreSame(last, vm.SelectedMaster);
        Assert.AreEqual(1, vm.Masters.Items.Count);
        await vm.DeleteSelectedCommand.ExecuteAsync(null);
        Assert.IsNull(vm.SelectedMaster);
    }

    /// <summary>详情页新增行直接移除；已保存行确认后删除，并选择末行或空选择。</summary>
    [TestMethod]
    public async Task DetailDelete_ShouldSelectLastAndKeepRowsWhenCancelled()
    {
        var dialogs = new TestDialogs();
        using var vm = new ProductMasterDetailViewModel(null!, null!, null!, null!, null!, dialogs, null!, new ProductMasterSyncDialogStub());
        vm.Neutrals.LoadFromDtos([Neutral("首行"), Neutral("末行")]);
        var first = vm.Neutrals.Items.First();
        var last = vm.Neutrals.Items.Last();
        vm.AddNeutralCommand.Execute(null);
        await vm.DeleteNeutralCommand.ExecuteAsync(null);
        Assert.AreEqual(0, dialogs.ConfirmCount);
        Assert.AreSame(last, vm.SelectedNeutral);
        vm.SelectedNeutral = first;
        dialogs.Result = false;
        await vm.DeleteNeutralCommand.ExecuteAsync(null);
        Assert.AreEqual(1, dialogs.ConfirmCount);
        Assert.AreSame(first, vm.SelectedNeutral);
        Assert.AreEqual(2, vm.Neutrals.Items.Count);
        dialogs.Result = true;
        await vm.DeleteNeutralCommand.ExecuteAsync(null);
        Assert.AreSame(last, vm.SelectedNeutral);
        await vm.DeleteNeutralCommand.ExecuteAsync(null);
        Assert.IsNull(vm.SelectedNeutral);
    }

    /// <summary>使用页面真实数值绑定验证空白能提交并进入其他单元格，非法非空文本仍被阻止。</summary>
    [TestMethod]
    [DataRow("ProductMasterManagerView.xaml", "DefaultNetWeight")]
    [DataRow("ProductMasterManagerView.xaml", "DefaultGrossWeight")]
    [DataRow("ProductMasterManagerView.xaml", "DefaultCbm")]
    [DataRow("ProductMasterManagerView.xaml", "DefaultSalesPriceUsd")]
    [DataRow("ProductMasterManagerView.xaml", "DefaultExchangeRate")]
    [DataRow("ProductMasterManagerView.xaml", "ProfitMargin")]
    [DataRow("ProductMasterManagerView.xaml", "DefaultPurchasePriceCny")]
    [DataRow("ProductMasterDetailView.xaml", "DefaultSalesPriceUsd")]
    [DataRow("ProductMasterDetailView.xaml", "DefaultExchangeRate")]
    [DataRow("ProductMasterDetailView.xaml", "ProfitMargin")]
    [DataRow("ProductMasterDetailView.xaml", "DefaultPurchasePriceCny")]
    public void NumericCell_ShouldCommitEmptyAsNull(string viewName, string field)
    {
        StaTest.RunAsync(async () =>
        {
            object row = field is "DefaultNetWeight" or "DefaultGrossWeight" or "DefaultCbm"
                ? new ProductMasterLineModel() : new ProductNeutralLineModel();
            var property = row.GetType().GetProperty(field)!;
            property.SetValue(row, 2m);
            var column = LoadNumericColumn(viewName, field);
            var next = new DataGridTextColumn { Binding = new Binding(field) };
            var grid = new DataGrid { AutoGenerateColumns = false, CanUserAddRows = false, ItemsSource = new[] { row } };
            grid.Columns.Add(column);
            grid.Columns.Add(next);
            var window = new Window { Content = grid, Width = 400, Height = 200, ShowInTaskbar = false };
            try
            {
                window.Show();
                await Dispatcher.Yield(DispatcherPriority.Background);
                grid.CurrentCell = new DataGridCellInfo(row, column);
                Assert.IsTrue(grid.BeginEdit());
                var editor = (TextBox)column.GetCellContent(row);
                editor.Text = string.Empty;
                editor.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
                Assert.IsFalse(System.Windows.Controls.Validation.GetHasError(editor));
                Assert.IsTrue(grid.CommitEdit(DataGridEditingUnit.Cell, true));
                Assert.IsTrue(grid.CommitEdit(DataGridEditingUnit.Row, true));
                Assert.IsNull(property.GetValue(row));
                grid.CurrentCell = new DataGridCellInfo(row, next);
                Assert.IsTrue(grid.BeginEdit());
                grid.CancelEdit(DataGridEditingUnit.Cell);
                grid.CancelEdit(DataGridEditingUnit.Row);
                grid.CurrentCell = new DataGridCellInfo(row, column);
                Assert.IsTrue(grid.BeginEdit());
                editor = (TextBox)column.GetCellContent(row);
                editor.Text = "abc";
                editor.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
                Assert.IsTrue(System.Windows.Controls.Validation.GetHasError(editor));
                Assert.IsFalse(grid.CommitEdit(DataGridEditingUnit.Cell, true));
            }
            finally
            {
                grid.CancelEdit(DataGridEditingUnit.Cell);
                grid.CancelEdit(DataGridEditingUnit.Row);
                window.Close();
            }
        });
    }

    /// <summary>从实际页面提取 Binding 到原生列，隔离主题资源但保留绑定和原生编辑事务。</summary>
    private static DataGridTextColumn LoadNumericColumn(string viewName, string field)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        Assert.IsNotNull(root);
        var doc = XDocument.Load(Path.Combine(root.FullName, "SwiftExport.Client.Wpf", "Features", "Products", "ProductList", "Views", viewName));
        var source = doc.Descendants().Single(element => element.Name.LocalName == "DataGridTextColumn"
            && ((string?)element.Attribute("Binding"))?.StartsWith("{Binding " + field + ",", StringComparison.Ordinal) == true);
        XNamespace ns = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        return (DataGridTextColumn)XamlReader.Parse(new XElement(ns + "DataGridTextColumn",
            new XAttribute("xmlns", ns.NamespaceName), new XAttribute("Binding", source.Attribute("Binding")!.Value)).ToString());
    }

    private static ProductMasterListItemDto Master(string sku) => new()
    {
        Uid = Guid.NewGuid(), Version = 1, Data = new ProductMasterWriteDto { MasterSku = sku }
    };

    private static ProductNeutralDto Neutral(string code, bool main = false) => new()
    {
        Uid = Guid.NewGuid(), Version = 1,
        Data = new ProductNeutralWriteDto { ProductCode = code, ImagePath = code + ".jpg", IsMainImage = main }
    };

    /// <summary>记录确认次数并返回指定结果，不创建真实弹窗。</summary>
    private sealed class TestDialogs : IDialogService
    {
        public bool Result { get; set; } = true;
        public int ConfirmCount { get; private set; }
        /// <summary>仅记录是否进入确认流程。</summary>
        public Task<bool> ConfirmAsync(string title, string message, string primaryButtonText = "确定", string closeButtonText = "取消", CancellationToken cancellationToken = default)
        {
            ConfirmCount++;
            return Task.FromResult(Result);
        }
        /// <summary>本测试不允许打开内容弹窗。</summary>
        public Task ShowContentAsync(string title, object content, string closeButtonText = "关闭", CancellationToken cancellationToken = default) => throw new NotSupportedException();
        /// <summary>本测试不允许打开错误弹窗。</summary>
        public Task ShowErrorAsync(string title, string message, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
