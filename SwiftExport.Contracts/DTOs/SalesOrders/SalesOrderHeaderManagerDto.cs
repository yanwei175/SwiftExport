using SwiftExport.Contracts.DTOs.Base;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using System;

namespace SwiftExport.Contracts.DTOs.SalesOrders
{
    public class SalesOrderHeaderManagerDto : BaseDto
    {

        #region 基本信息

        public SalesOrderStatusEnum OrderStatus { get; set; }

        public ConfirmationStatusEnum ConfirmationStatus { get; set; }

        public string OrderNo { get; set; }

        public string InvoiceNo { get; set; }

        public Guid CustomerId { get; set; }

        public string CustomerCode { get; set; }

        public string CustomerPoNo { get; set; }

        #endregion

        #region 数量字段

        public decimal? TotalQty { get; set; }
        public decimal? TotalShippedQty { get; set; }
        public decimal? TotalUnshippedQty { get; set; }

        #endregion

        #region 金额与汇率

        /// <summary>
        /// 币种（USD / RMB / EUR 等）
        /// </summary>
        public string Currency { get; set; }

        /// <summary>
        /// ExchangeRate（用于本币金额计算）
        /// </summary>
        public decimal ExchangeRate { get; set; }

        /// <summary>
        /// 原币总金额（由行集合自动计算）
        /// </summary>
        public decimal TotalAmountOriginalCurrency { get; set; }

        /// <summary>
        /// 本币总金额（原币金额 * ExchangeRate）
        /// </summary>
        public decimal TotalAmountLocalCurrency { get; set; }

        #endregion

        #region 日期字段

        /// <summary>
        /// 订单日期
        /// </summary>
        public DateTime? OrderDate { get; set; }

        /// <summary>
        /// 交期（预计发货日期）
        /// </summary>
        public DateTime? DeliveryDate { get; set; }

        /// <summary>
        /// 回复客户的日期（如确认交期）
        /// </summary>
        public DateTime? ReplyDate { get; set; }

        #endregion

        #region 备注

        /// <summary>
        /// 订单文件夹路径（如存放附件）
        /// </summary>
        public string SoFolder { get; set; }

        /// <summary>
        /// SubNote
        /// </summary>
        public string Remark { get; set; }

        #endregion

    }
}
