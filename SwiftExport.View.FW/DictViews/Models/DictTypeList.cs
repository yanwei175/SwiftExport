using SwiftExport.View.FW.Mvvm;

namespace SwiftExport.View.FW.DictViews.Models
{
    /// <summary>
    /// 字典类型列表模型
    /// 继承泛型 BaseListModel，天然支持 DataGrid 脏数据追踪、验证、勾选等功能
    /// </summary>
    public class DictTypeList : BaseListModel<DictTypeModel>
    {
    }
}
