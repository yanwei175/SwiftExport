using Microsoft.Office.Tools.Ribbon;
using SwiftExport.UI.Views;
namespace SwiftExport.VSTO
{
    public partial class RibbonSwift
    {
        private void RibbonSwift_Load(object sender, RibbonUIEventArgs e)
        {

        }

        private void BtnTest_Click(object sender, RibbonControlEventArgs e)
        {
            MainWindow window = new MainWindow();
            Customer customer = new Customer();
            window.Content = customer;
            window.ShowDialog();
        }
    }
}
