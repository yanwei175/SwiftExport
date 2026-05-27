using SwiftExport.Contracts.DTOs.SalesOrders;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.View.FW.Mvvm;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;

namespace SwiftExport.View.FW.SalesOrderViews.Models
{
    /// <summary>
    /// 销售订单头列表模型。
    /// 用于订单管理页承载“大量订单头”的集合，不负责任何明细行计算。
    /// </summary>
    public class SalesOrderHeaderList : BaseListModel<SalesOrderHeaderModel>
    {
 

    }
}
