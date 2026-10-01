using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.ForExcel.SalesOrders
{
    /// <summary>Excel 已解析数据的只读检查请求，不接收二维数组或执行保存。</summary>
    public class CheckSalesOrderImportRequest
    {
        /// <summary>选定客户的业务编码，不是明细列中的客户产品型号。</summary>
        public string CustomerCode { get; set; }
        /// <summary>需检查的单头业务值。</summary>
        public SalesOrderImportHeaderInput Header { get; set; }
        /// <summary>最多 500 行；行标识必须非空且唯一。</summary>
        public List<SalesOrderImportLineInput> Lines { get; set; } = new List<SalesOrderImportLineInput>();
        /// <summary>用户查看重复订单候选后明确同意继续新建；不是更新已有订单。</summary>
        public bool AcceptExistingOrders { get; set; }
    }
}
