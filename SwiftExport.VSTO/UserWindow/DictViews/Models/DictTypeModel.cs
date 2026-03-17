using SwiftExport.VSTO.UserWindow.Mvvm;

namespace SwiftExport.VSTO.UserWindow.DictViews.Models
{
    // 1. 左侧的字典大类 (Master)
    public class DictTypeModel : ViewModelBase
    {
        public string DictCode { get; set; } // 比如 "CustomerType"
        public string DictName { get; set; } // 比如 "客户分类"
        public bool IsSystem { get; set; }   // true=不可删, false=可删
    }
}
