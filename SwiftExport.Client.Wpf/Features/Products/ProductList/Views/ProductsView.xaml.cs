using System.Windows.Controls;
using System.Windows.Input;

namespace SwiftExportClient.Features.Products.ProductList.Views;

public partial class ProductsView : UserControl
{
    public ProductsView()
    {
        InitializeComponent();
    }

    private void Root_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.D1)
        {
            SearchTextBox.Focus();
            SearchTextBox.SelectAll();

            e.Handled = true;
        }
    }
}
