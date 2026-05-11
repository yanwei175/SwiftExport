using SwiftExport.Contracts.DTOs.Base;
using System;

namespace SwiftExport.Contracts.DTOs.Orders
{
    /// <summary>
    /// 拆解BOM之后的依赖需求 DTO。
    /// </summary>
    public class IndependentDemandDto : BaseDto
    {
        #region 来源信息 (Source)

        /// <summary>
        /// 来源单据类型（枚举：SalesOrder, SampleOrder, Forecast, StockPlan 等）
        /// </summary>
        public string SourceType { get; set; } = string.Empty;

        /// <summary>
        /// 来源单号（SO号、样品单号等）
        /// </summary>
        public string SourceNo { get; set; } = string.Empty;

        /// <summary>
        /// 来源单据行 ID
        /// </summary>
        public long SourceLineId { get; set; }

        #endregion


        #region 物料与层级 (Material & BOM)

        /// <summary>
        /// 当前物料 SKU
        /// </summary>
        public string Sku { get; set; } = string.Empty;

        /// <summary>
        /// 物料描述（用于非标件或补充说明）。
        /// </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// 颜色名称（用于非标件或颜色补充）。
        /// </summary>
        public string ColorName { get; set; } = string.Empty;

        /// <summary>
        /// 规格特征（用于非标件或型号补充）。
        /// </summary>
        public string Features { get; set; } = string.Empty;

        /// <summary>
        /// 物料主图（用于前端图片列显示）。
        /// </summary>
        public string MainImage { get; set; } = string.Empty;
        /// <summary>
        /// 父项 SKU（所属成品）
        /// </summary>
        public string ParentSku { get; set; } = string.Empty;

        /// <summary>
        /// BOM 层级（0-成品/需求头, 1-一级子件, 2-二级...）
        /// </summary>
        public int BomLevel { get; set; }

        /// <summary>
        /// 供货类型（枚举：Purchase-采购, Produce-生产, Outsourcing-委外, Stock-库存）
        /// </summary>
        public string SupplyType { get; set; } = string.Empty;

        #endregion

        #region 数量跟踪 (Quantity Tracking)
        /// <summary>
        /// 毛需求数量 (原始需要的量)
        /// </summary>
        public decimal GrossQty { get; set; }

        /// <summary>
        /// 净需求数量 (考虑库存/在途后的实际建议量)
        /// </summary>
        public decimal NetQty { get; set; }

        /// <summary>
        /// 已转化执行的数量（已生成 PO 的数量或已转生产的数量）
        /// </summary>
        public decimal ExecutedQty { get; set; }

        /// <summary>
        /// 已入库/完工数量
        /// </summary>
        public decimal ReceivedQty { get; set; }

        /// <summary>
        /// 已发货数量（仅针对 BomLevel=0 的成品行有意义）
        /// </summary>
        public decimal ShippedQty { get; set; }

        #endregion

        #region 执行状态 (Execution)

        /// <summary>
        /// 需求日期（什么时候要用）
        /// </summary>
        public DateTime? RequiredDate { get; set; }

        /// <summary>
        /// 状态（RequirementStatusEnums：草稿/待处理/采购中/已到货/已完成）
        /// </summary>
        public int StatusFlag { get; set; } 

        /// <summary>
        /// 是否齐套（针对 BomLevel=0 的行，判断其下方所有子项是否 ReceivedQty >= GrossQty）
        /// </summary>
        public bool IsKitted { get; set; }

        /// <summary>
        /// 备注
        /// </summary>
        public string Remark { get; set; }

        #endregion



        #region 执行关联 (Execution Link)

        /// <summary>
        /// 目标执行单号（如采购单号 PO-2024001）
        /// 理由：背靠背模式下，方便直接从需求池跳到采购单。
        /// </summary>
        public string TargetNo { get; set; }

        /// <summary>
        /// 目标执行行 ID
        /// </summary>
        public long? TargetLineId { get; set; }

        #endregion

        #region 用于界面显示和交互
        public string SupplierCode { get; set; } // 用户在界面选中的供应商代码
        public string SupplierName { get; set; } // 供应商名称
        public string Unit { get; set; }       // 物料单位，采购需要看
        #endregion


    }
}
