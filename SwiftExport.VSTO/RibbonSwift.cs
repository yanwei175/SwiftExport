using Microsoft.Office.Tools.Ribbon;
using SwiftExport.VSTO.Common;
using SwiftExport.VSTO.UserWindow;
namespace SwiftExport.VSTO
{
    public partial class RibbonSwift
    {
        private void RibbonSwift_Load(object sender, RibbonUIEventArgs e)
        {

        }

        private void BtnTest_Click(object sender, RibbonControlEventArgs e)
        {
            //WindowHelper.ShowUnique(() => new TestWindow());
            var win = new TestWindow();
            win.ShowDialog();
        }

        private void button1_Click(object sender, RibbonControlEventArgs e)
        {

            WindowHelper.ShowUnique(() => new MainWindow());
        }
    }
}
