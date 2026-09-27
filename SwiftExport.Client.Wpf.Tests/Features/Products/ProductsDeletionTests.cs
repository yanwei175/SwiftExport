using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Suppliers;
using SwiftExportClient.Features.Products.Bom.ViewModels;
using SwiftExportClient.Features.Products.ProductList.ViewModels;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Services.UI;

namespace SwiftExport.Client.Wpf.Tests.Features.Products;

[TestClass]
public sealed class ProductsDeletionTests
{
    [TestMethod]
    public void SelectionMode_ShouldDefaultToFullRowAndRemainBindable()
    {
        var viewModel = CreateViewModel(new RecordingDialogService());

        Assert.IsFalse(viewModel.IsCellSelectionMode);
        viewModel.IsCellSelectionMode = true;
        Assert.IsTrue(viewModel.IsCellSelectionMode);
    }

    [TestMethod]
    public void FieldMode_ShouldDefaultToSimpleAndRemainBindable()
    {
        var viewModel = CreateViewModel(new RecordingDialogService());

        Assert.IsFalse(viewModel.IsComplexFieldMode);
        viewModel.IsComplexFieldMode = true;
        Assert.IsTrue(viewModel.IsComplexFieldMode);
    }

    [TestMethod]
    public void SupplierCommands_ShouldUpdateAndClearIdCodeAndNameAtomically()
    {
        var viewModel = CreateViewModel(new RecordingDialogService());
        var line = viewModel.Products.AddNewProduct();
        var supplierId = Guid.NewGuid();
        viewModel.EditSupplier = new SupplierDto
        {
            Uid = supplierId,
            SupplierCode = "SUP-01",
            SupplierName = "测试供应商",
        };

        viewModel.CommitSupplierCommand.Execute(line);

        Assert.AreEqual(supplierId, line.DefaultSupplierId);
        Assert.AreEqual("SUP-01", line.DefaultSupplierCode);
        Assert.AreEqual("测试供应商", line.SupplierName);
        Assert.IsNull(viewModel.EditSupplier);

        viewModel.ClearSupplierCommand.Execute(line);

        Assert.IsNull(line.DefaultSupplierId);
        Assert.AreEqual(string.Empty, line.DefaultSupplierCode);
        Assert.AreEqual(string.Empty, line.SupplierName);
    }

    [TestMethod]
    public async Task AddedSelectedLine_ShouldBeRemovedCompletelyWithoutConfirmation()
    {
        var dialog = new RecordingDialogService();
        var viewModel = CreateViewModel(dialog);
        var added = viewModel.Products.AddNewProduct();
        added.Sku = "NEW";
        viewModel.SelectedLine = added;

        await viewModel.DeleteSelectedCommand.ExecuteAsync(null);

        Assert.AreEqual(0, dialog.ConfirmCallCount);
        Assert.IsEmpty(viewModel.Products.Items);
        Assert.IsEmpty(viewModel.Products.AllLines);
    }

    [TestMethod]
    public async Task CheckedLines_ShouldTakePriorityAndKeepPersistedDeletionInChangeSet()
    {
        var dialog = new RecordingDialogService { ConfirmationResult = true };
        var viewModel = CreateViewModel(dialog);
        viewModel.Products.LoadFromDtos(
        [
            CreateDto("KEEP"),
            CreateDto("DELETE")
        ]);
        var kept = viewModel.Products.Items[0];
        var persisted = viewModel.Products.Items[1];
        var added = viewModel.Products.AddNewProduct();
        added.Sku = "NEW";
        persisted.IsChecked = true;
        added.IsChecked = true;
        viewModel.SelectedLine = kept;

        await viewModel.DeleteSelectedCommand.ExecuteAsync(null);

        Assert.AreEqual(1, dialog.ConfirmCallCount);
        Assert.HasCount(1, viewModel.Products.Items);
        Assert.AreSame(kept, viewModel.Products.Items[0]);
        Assert.AreSame(kept, viewModel.SelectedLine);
        Assert.IsTrue(viewModel.Products.AllLines.Contains(persisted));
        Assert.AreEqual(LineState.Deleted, persisted.State);
        Assert.IsFalse(viewModel.Products.AllLines.Contains(added));
        Assert.HasCount(1, viewModel.Products.DeletedItems);
    }

    [TestMethod]
    public async Task AddedCheckedLine_ShouldBeRemovedWithoutDeletingCurrentPersistedLine()
    {
        var dialog = new RecordingDialogService();
        var viewModel = CreateViewModel(dialog);
        viewModel.Products.LoadFromDtos([CreateDto("CURRENT")]);
        var current = viewModel.Products.Items[0];
        var added = viewModel.Products.AddNewProduct();
        added.IsChecked = true;
        viewModel.SelectedLine = current;

        Assert.IsTrue(viewModel.DeleteSelectedCommand.CanExecute(null));
        await viewModel.DeleteSelectedCommand.ExecuteAsync(null);

        Assert.AreEqual(0, dialog.ConfirmCallCount);
        Assert.HasCount(1, viewModel.Products.Items);
        Assert.AreSame(current, viewModel.Products.Items[0]);
        Assert.AreSame(current, viewModel.SelectedLine);
    }

    [TestMethod]
    public void CheckedLine_ShouldEnableDeleteWithoutCurrentSelection()
    {
        var viewModel = CreateViewModel(new RecordingDialogService());
        var line = viewModel.Products.AddNewProduct();
        viewModel.SelectedLine = null;
        line.IsChecked = true;

        Assert.IsTrue(viewModel.DeleteSelectedCommand.CanExecute(null));
    }

    private static ProductsViewModel CreateViewModel(RecordingDialogService dialogService)
    {
        var bomDrawer = new BomDrawerViewModel(null!, null!, null!, null!, dialogService);
        return new ProductsViewModel(
            null!, null!, null!, null!, null!, null!, dialogService, bomDrawer, null!);
    }

    private static ProductDto CreateDto(string sku) => new()
    {
        Uid = Guid.NewGuid(),
        Sku = sku
    };

    private sealed class RecordingDialogService : IDialogService
    {
        public int ConfirmCallCount { get; private set; }

        public bool ConfirmationResult { get; init; } = true;

        public Task ShowContentAsync(
            string title,
            object content,
            string closeButtonText = "关闭",
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<bool> ConfirmAsync(
            string title,
            string message,
            string primaryButtonText = "确定",
            string closeButtonText = "取消",
            CancellationToken cancellationToken = default)
        {
            ConfirmCallCount++;
            return Task.FromResult(ConfirmationResult);
        }

        public Task ShowErrorAsync(
            string title,
            string message,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
