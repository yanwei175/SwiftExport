using SwiftExport.VSTO.UserWindow.CustomerViews.Models;
using SwiftExport.VSTO.UserWindow.Mvvm;
using System.Collections.ObjectModel;

namespace SwiftExport.VSTO.UserWindow.CustomerViews.ViewModels
{
    // 这个类纯粹是给 Visual Studio 设计器看的，程序运行后它连一丁点内存都不会占
    public class CustomerPoConfigDesignViewModel
    {
        public ObservableCollection<CustomerPoConfigModel> ItemsView { get; set; }
        public string SearchText { get; set; } = "测试搜索...";
        public string StatusMessage { get; set; } = "就绪 (假数据渲染中...)";

        public CustomerPoConfigDesignViewModel()
        {
            ItemsView = new ObservableCollection<CustomerPoConfigModel>
            {
                new CustomerPoConfigModel
                {
                    CustomerCode = "1007-AT1",
                    SupplierCode = "1007-AT1",
                    LogoType = "Aquavitaquatek",
                    LabelType = "Aquavitaquatek",
                    InletHoseType = "60cm 不锈钢",
                    AeratorType = "纽珀起泡器纽珀起泡器纽珀起泡器纽珀起泡器纽珀起泡器纽珀起泡器纽珀起泡器纽珀起泡器纽珀起泡器纽珀起泡器纽珀起泡器纽珀起泡器",
                    BoxType = "彩盒",
                    Note="纽珀起泡器纽珀起泡器纽珀起泡器纽珀起泡器纽珀起泡器纽珀起泡器纽珀起泡器纽珀起泡器纽珀起泡器纽珀起泡器纽珀起泡器纽珀起泡器",
                    State = ModelState.Unchanged // 正常状态
                },
                new CustomerPoConfigModel
                {
                    CustomerCode = "1007-AT1",
                    SupplierCode = "1007-AT1",
                    InletHoseType = "80cm 尼龙",
                    BoxType = "牛皮纸盒",
                    State = ModelState.Added // 会被渲染成绿色（你的新增样式）
                },
                new CustomerPoConfigModel
                {
                    CustomerCode = "CUST-003",
                    SupplierCode = "SUP-C",
                    State = ModelState.Deleted // 会被渲染成红色（你的删除样式）
                }
            };
        }
    }
}
