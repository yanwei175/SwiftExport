using System.Windows.Controls;

namespace SwiftExport.View.FW.DictViews.Views
{
    /// <summary>
    /// GenericDictDataManagerView.xaml µÄ½»»¥Âß¼­
    /// </summary>
    public partial class GenericDictDataManagerView : UserControl
    {
        public GenericDictDataManagerView()
        {
            InitializeComponent();
        }
        public GenericDictDataManagerView(GenericDictDataManagerView vm):this()
        {
            this.DataContext = vm;
        }
    }
}
