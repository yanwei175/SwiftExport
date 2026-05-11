using SwiftExport.Contracts.DTOs.Base;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using System;

namespace SwiftExport.Contracts.DTOs.Orders
{
    /// <summary>
    /// 采购订单头 DTO。
    /// </summary>
    public class PurchaseOrderHeadDto : BaseDto
    {
        private decimal? totalUnshippedQty;

        /// <summary>
        /// 系统单号,不能为空，且唯一。格式：PO+年月日+4位流水号（当天内），如：PO202406120001。
        /// </summary>
        public string OrderNo { get; set; }
        public int OrderStatus { get; set; }
        /// <summary>
        /// 自定义Po号,应付那些不喜欢被系统单号约束的用户.允许重复,允许为空,允许任何东西.
        /// </summary>
        public string BoNo { get; set; }
        /// <summary>
        /// 关联销售单号
        /// </summary>
        public string SalesOrderNo { get; set; }
        /// <summary>
        /// 客户PO号,关联字段
        /// </summary>
        public string CustomerPoNo { get; set; }
        /// <summary>
        /// 客户代码,关联字段，允许为空，因为有些订单可能没有明确的客户信息（如内部订单或特殊项目）。如果订单确实关联到一个客户，则该字段应包含客户的唯一标识代码，以便于订单管理和客户关系维护。
        /// </summary>
        public string CustomerCode { get; set; }
        /// <summary>
        /// 采购币种,默认为人民币
        /// </summary>
        public string Currency { get; set; } = CurrencyEnum.CNY;
        /// <summary>
        /// 采购单必须有一个供应商对象
        /// </summary>
        public string SupplierCode { get; set; }
        public string SupplierName { get; set; } = string.Empty;


        #region 对于特殊老板使用的单头字段
        /// <summary>
        /// 业务员
        /// </summary>
        public string SoPerson { get; set; }
        /// <summary>
        /// 跟单员
        /// </summary>
        public string PoPerson { get; set; }
        public string InletrHoseType { get; set; }
        public string AeratorType { get; set; }
        public string PalletType { get; set; }
        public string BoxType { get; set; }
        public string LabelType { get; set; }
        public string IMType { get; set; }
        public string CartonType { get; set; }
        public string LogoType { get; set; }

        #endregion

        #region 审批字段
        /// <summary>
        ///  审核人
        /// </summary>
        public string ReviewedBy { get; set; }
        /// <summary>
        /// 审核状态
        /// </summary>
        public int ApprovalStatus { get; set; } = ApprovalStatusEnum.草稿;
        /// <summary>
        /// 批准人,当 ApprovalStatusEnum 为 已批准 时,该字段不能为空,且记录批准人的名字.
        /// </summary>
        public string ApprovedBy { get; set; }

        #endregion

        public string PoFolder { get; set; }
        public string Remark { get; set; }

        public bool IsDeleted { get; set; }
        public bool? IsLocked { get; set; }

        #region 汇总字段
        public int? LineCount { get; set; }
        public decimal? TotalAmountLocalCurrency { get; set; }
        public decimal? TotalQty { get; set; }
        public decimal? TotalShippedQty { get; set; }
        public decimal? TotalUnshippedQty { get => TotalQty - TotalShippedQty; set => totalUnshippedQty = value; }
        public decimal? TotalReadyQty { get; set; }
        #endregion

        /// <summary>
        /// 订单日期
        /// </summary>
        public DateTime? OrderDate { get; set; }
        /// <summary>
        /// 预计交货日期
        /// </summary>
        public DateTime? ExpectedDate { get; set; }
        /// <summary>
        /// 回复交货日期
        /// </summary>
        public DateTime? ReplyDate { get; set; }

        public DateTime? ChangeTime { get; set; }
        /// <summary>
        /// 直接优惠金额,有些人会要求订单直接优惠金额,而不是折扣率,这个字段用来满足他们的需求.当有直接优惠金额时,订单金额直接减,如果没有直接优惠金额,则使用折扣率字段进行折扣计算.
        /// </summary>
        public decimal? ZhiJieYouHuiJinE { get; set; }
        /// <summary>
        /// 折扣率
        /// </summary>
        public decimal? ZheKou { get; set; }
        /// <summary>
        /// 折扣备注,这张单为什么打折扣.
        /// </summary>
        public string ZheKouBeiZhu { get; set; }
    }
}
