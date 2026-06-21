using SwiftExport.Contracts.DTOs.Base;
using System;
using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.SalesOrders
{
    public class SalesOrderHeaderManagerUpdateRequestDto : BaseDto
    {
        public string InvoiceNo { get; set; }
        public string CustomerPoNo { get; set; }

        /// <summary> 交期（预计发货日期）</summary>
        public DateTime? DeliveryDate { get; set; }

        /// <summary> 回复客户的日期（如确认交期）</summary>
        public DateTime? ReplyDate { get; set; }

        /// <summary>订单文件夹路径（如存放附件）</summary>
        public string SoFolder { get; set; }

        /// <summary>SubNote</summary>
        public string Remark { get; set; }

    }

    public class BatchSalesOrderHeadManagerUpdateRequestDto
    {
        public List<SalesOrderHeaderManagerUpdateRequestDto> UpdateList { get; set; }
    }
    public class BatchSalesOrderHeadManagerUpdateResultDto
    {
        public List<SalesOrderHeaderManagerDto> Items { get; set; }

    }






}
