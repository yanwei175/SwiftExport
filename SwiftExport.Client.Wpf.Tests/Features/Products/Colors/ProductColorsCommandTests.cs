using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExportClient.Features.Products.Colors.Models;
using SwiftExportClient.Features.Products.Colors.ViewModels;

namespace SwiftExport.Client.Wpf.Tests.Features.Products.Colors;

[TestClass]
public sealed class ProductColorsCommandTests
{
    [TestMethod]
    public void DeleteSelectedCommand_ShouldReevaluateFromExplicitLineParameter()
    {
        var viewModel = new ProductColorsViewModel(null!, null!, null!, null!);
        var line = new ProductColorLineModel();

        Assert.IsFalse(viewModel.IsCellSelectionMode);
        Assert.IsFalse(viewModel.DeleteSelectedCommand.CanExecute(null));
        viewModel.SelectedLine = line;
        Assert.IsTrue(viewModel.DeleteSelectedCommand.CanExecute(null));
    }

}
