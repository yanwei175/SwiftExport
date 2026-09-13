using Mapster;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Client.Wpf.Tests.Controls;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;
using SwiftExport.Contracts.DTOs.Suppliers;
using SwiftExportClient.Features.Suppliers.Components.Model;
using SwiftExportClient.Features.Suppliers.Components.Services;
using SwiftExportClient.Features.Suppliers.Components.View;
using SwiftExportClient.Features.Suppliers.Components.ViewModel;
using SwiftExportClient.Services.UI;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace SwiftExport.Client.Wpf.Tests.Features.Suppliers;

/// <summary>验证供应商选择器的缓存快照、单选确认、双击和 Dialog 结果边界。</summary>
[TestClass]
[DoNotParallelize]
public sealed class SupplierPickerTests
{
    /// <summary>缓存 DTO 应通过 Mapster 形成独立会话模型，并能返回不泄漏 UI 类型的 DTO。</summary>
    [TestMethod]
    public async Task OpenAndConfirm_ShouldMapIndependentSupplierDto()
    {
        var source = Supplier("S001", "供应商甲");
        var cache = new ReferenceDataCacheServiceStub(suppliers: [source]);
        using var picker = new SupplierPickerViewModel(cache);

        await picker.OpenAsync();

        Assert.AreEqual(1, cache.EnsureLoadedCallCount);
        var candidate = picker.Items.Single();
        Assert.IsInstanceOfType<SupplierPickerModel>(candidate);
        Assert.AreNotSame(source, candidate);
        Assert.IsFalse(picker.IsPrimaryButtonEnabled);

        picker.SelectedItem = candidate;
        Assert.IsTrue(picker.IsPrimaryButtonEnabled);
        var result = picker.GetSelectedResult();
        Assert.IsNotNull(result);
        Assert.IsNotInstanceOfType<SupplierPickerModel>(result);
        Assert.AreEqual(source.Uid, result.Uid);
        Assert.AreEqual(source.SupplierCode, result.SupplierCode);
        Assert.AreEqual(source.SupplierName, result.SupplierName);
        Assert.AreEqual(source.Currency, result.Currency);
    }

    /// <summary>代码和名称筛选均应生效，并在结果替换时清除旧选择。</summary>
    [TestMethod]
    public async Task Keyword_ShouldFilterCodeAndNameAndInvalidateSelection()
    {
        var cache = new ReferenceDataCacheServiceStub(suppliers:
        [
            Supplier("S001", "供应商甲"),
            Supplier("V002", "供应商乙"),
        ]);
        using var picker = new SupplierPickerViewModel(cache);
        await picker.OpenAsync();
        picker.SelectedItem = picker.Items[0];

        picker.Keyword = "V002";
        Assert.IsNull(picker.SelectedItem);
        Assert.AreEqual("V002", picker.Items.Single().SupplierCode);

        picker.Keyword = "甲";
        Assert.AreEqual("S001", picker.Items.Single().SupplierCode);
    }

    /// <summary>刷新命令必须调用供应商专用缓存刷新，并在成功后重建会话模型。</summary>
    [TestMethod]
    public async Task Refresh_ShouldUseSupplierCacheRefresh()
    {
        var cache = new ReferenceDataCacheServiceStub(suppliers: [Supplier("S001", "供应商甲")]);
        using var picker = new SupplierPickerViewModel(cache);
        await picker.OpenAsync();
        var original = picker.Items.Single();

        await picker.RefreshCommand.ExecuteAsync(null);

        Assert.AreEqual(1, cache.RefreshSuppliersCallCount);
        Assert.AreEqual("S001", picker.Items.Single().SupplierCode);
        Assert.AreNotSame(original, picker.Items.Single());
        Assert.IsNull(picker.SelectedItem);
    }

    /// <summary>确认命令只在有效单选存在时发出 Primary 关闭请求。</summary>
    [TestMethod]
    public async Task ConfirmCommand_ShouldRequestPrimaryOnlyForCurrentCandidate()
    {
        var cache = new ReferenceDataCacheServiceStub(suppliers: [Supplier("S001", "供应商甲")]);
        using var picker = new SupplierPickerViewModel(cache);
        await picker.OpenAsync();
        DialogResult? requested = null;
        picker.CloseRequested += (_, args) => requested = args.Result;

        Assert.IsFalse(picker.ConfirmCommand.CanExecute(null));
        picker.SelectedItem = picker.Items.Single();
        Assert.IsTrue(picker.ConfirmCommand.CanExecute(null));

        picker.ConfirmCommand.Execute(null);
        Assert.AreEqual(DialogResult.Primary, requested);
    }

    /// <summary>Feature 服务只在 Primary 时返回 DTO，并使用公共 Dialog 的表格滚动配置。</summary>
    [TestMethod]
    public void Service_ShouldReturnDtoOnlyAfterPrimaryConfirmation() =>
        DatePickerAssistTests.RunWithApplication(async () =>
        {
            ApplicationResourceLoader.Load();
            var cache = new ReferenceDataCacheServiceStub(suppliers: [Supplier("S001", "供应商甲")]);
            var primaryDialog = new DialogStub(DialogResult.Primary, state =>
            {
                var picker = (SupplierPickerViewModel)state!;
                picker.SelectedItem = picker.Items.Single();
            });
            SupplierPickerViewModel? primarySession = null;
            var service = new SupplierPickerService(
                () => primarySession = new SupplierPickerViewModel(cache),
                primaryDialog);

            var result = await service.PickAsync();

            Assert.IsNotNull(result);
            Assert.AreEqual("S001", result.SupplierCode);
            Assert.IsNotInstanceOfType<SupplierPickerModel>(result);
            Assert.IsInstanceOfType<SupplierPickerView>(primaryDialog.Content);
            Assert.AreEqual("选择供应商", primaryDialog.Options?.Title);
            Assert.AreEqual("确定", primaryDialog.Options?.PrimaryButtonText);
            Assert.AreEqual(DialogScrollStrategy.Content, primaryDialog.Options?.ScrollStrategy);
            Assert.IsNotNull(primarySession);
            Assert.IsEmpty(primarySession.Items, "弹窗结束后必须释放本次会话候选。");
            Assert.IsNull(primarySession.SelectedItem);

            SupplierPickerViewModel? cancelledSession = null;
            var cancelled = new SupplierPickerService(
                () => cancelledSession = new SupplierPickerViewModel(cache),
                new DialogStub(DialogResult.Close));
            Assert.IsNull(await cancelled.PickAsync());
            Assert.IsNotNull(cancelledSession);
            Assert.AreNotSame(primarySession, cancelledSession);
            Assert.IsEmpty(cancelledSession.Items);
        });

    /// <summary>真实视图应为只读整行单选，且有效数据行双击等同确认。</summary>
    [TestMethod]
    public void View_ShouldUseSingleRowSelectionAndConfirmOnRowDoubleClick() =>
        DatePickerAssistTests.RunWithApplication(async () =>
        {
            ApplicationResourceLoader.Load();
            var cache = new ReferenceDataCacheServiceStub(suppliers: [Supplier("S001", "供应商甲")]);
            using var picker = new SupplierPickerViewModel(cache);
            await picker.OpenAsync();
            var view = new SupplierPickerView { DataContext = picker };
            var window = new Window
            {
                Width = 900,
                Height = 600,
                ShowInTaskbar = false,
                ShowActivated = false,
                Left = -10000,
                Top = -10000,
                Content = view
            };

            try
            {
                window.Show();
                await Dispatcher.Yield(DispatcherPriority.Loaded);
                var grid = (DataGrid)view.FindName("SupplierGrid")!;
                Assert.IsTrue(grid.IsReadOnly);
                Assert.AreEqual(DataGridSelectionMode.Single, grid.SelectionMode);
                Assert.AreEqual(DataGridSelectionUnit.FullRow, grid.SelectionUnit);
                Assert.AreEqual(8, grid.Columns.Count);
                Assert.IsFalse(grid.Columns.OfType<DataGridTemplateColumn>().Any());

                var row = (DataGridRow)grid.ItemContainerGenerator.ContainerFromItem(picker.Items.Single());
                Assert.IsNotNull(row);
                DialogResult? requested = null;
                picker.CloseRequested += (_, args) => requested = args.Result;
                picker.SelectedItem = picker.Items.Single();
                grid.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                {
                    RoutedEvent = Control.MouseDoubleClickEvent,
                    Source = grid
                });
                Assert.IsNull(requested, "双击表格空白区域不得确认已有选择。");

                picker.SelectedItem = null;
                grid.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                {
                    RoutedEvent = Control.MouseDoubleClickEvent,
                    Source = row
                });

                Assert.AreSame(picker.Items.Single(), picker.SelectedItem);
                Assert.AreEqual(DialogResult.Primary, requested);
            }
            finally
            {
                window.Close();
            }
        });

    private static SupplierDto Supplier(string code, string name) => new()
    {
        Uid = Guid.NewGuid(),
        Version = 1,
        SupplierCode = code,
        SupplierName = name,
        Currency = "CNY",
    };

    private sealed class DialogStub(
        DialogResult result,
        Action<IDialogContentState?>? beforeReturn = null) : IDialogService
    {
        public object? Content { get; private set; }
        public DialogOptions? Options { get; private set; }

        public Task<DialogResult> ShowAsync(
            object content,
            DialogOptions options,
            IDialogContentState? state = null,
            CancellationToken cancellationToken = default)
        {
            Content = content;
            Options = options;
            beforeReturn?.Invoke(state);
            return Task.FromResult(result);
        }

        public Task ShowContentAsync(string title, object content, string closeButtonText = "关闭", CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<bool> ConfirmAsync(string title, string message, string primaryButtonText = "确定", string closeButtonText = "取消", CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task ShowErrorAsync(string title, string message, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
