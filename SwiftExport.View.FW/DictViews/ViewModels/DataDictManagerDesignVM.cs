using SwiftExport.View.FW.DictViews.Models;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace SwiftExport.View.FW.DictViews.ViewModels
{
    public class DataDictManagerDesignVM : DataDictManagerViewModel
    {
        public DataDictManagerDesignVM()
        {
            // 左侧分组（和正式版一样）
            Groups = new ObservableCollection<DictGroup>
        {
            new DictGroup
            {
                GroupName = "商品属性",
                DictMenuItems = new List<DictMenuItem> { new DictMenuItem { DictCode = "Color", DictName = "颜色" }, new DictMenuItem { DictCode = "Size", DictName = "尺码" }, new DictMenuItem { DictCode = "Grade", DictName = "产品等级" } }
            },
            new DictGroup
            {
                GroupName = "结算相关",
                DictMenuItems = new List<DictMenuItem> { new DictMenuItem { DictCode = "PayMethod", DictName = "结算方式" }, new DictMenuItem { DictCode = "PayTerm", DictName = "结算期限" } }
            }
        };

            // 左侧菜单项（模拟数据库）
            MenuItems = new ObservableCollection<DictMenuItem>
        {
            new DictMenuItem { DictCode="Color", DictName="颜色", IsSystem=true },
            new DictMenuItem { DictCode="Size", DictName="尺码", IsSystem=true },
            new DictMenuItem { DictCode="Grade", DictName="产品等级", IsSystem=true },
            new DictMenuItem { DictCode="PayMethod", DictName="结算方式", IsSystem=false },
        };

            // 默认选中第一个
            SelectedMenuItem = MenuItems.First();

            // 右侧字典项（模拟数据库）
            CurrentDictItems = new ObservableCollection<DictDataModel>
        {
            new DictDataModel { DictCode="Color", DictLabel="红色", DictValue="Red", SortOrder=1 },
            new DictDataModel { DictCode="Color", DictLabel="蓝色", DictValue="Blue", SortOrder=2 },
            new DictDataModel { DictCode="Color", DictLabel="绿色", DictValue="Green", SortOrder=3 }
        };
        }

    }
}
