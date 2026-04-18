using System.Collections.ObjectModel;

namespace SwiftExport.View.FW.ProductViews.Models
{
    public class BomTreeNodeModel
    {
        public string ChildCode { get; set; }

        public string ChildName { get; set; }

        public string QtyText { get; set; }

        public ObservableCollection<BomTreeNodeModel> Children { get; set; } = new ObservableCollection<BomTreeNodeModel>();
    }
}
