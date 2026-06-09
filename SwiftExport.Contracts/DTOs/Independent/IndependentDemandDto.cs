using SwiftExport.Contracts.DTOs.Base;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using System;

namespace SwiftExport.Contracts.DTOs.Independent
{
    /// <summary>
    /// 毛需求 DTO。
    /// </summary>
    public class IndependentDemandDto : BaseDto
    {
        public IndependentDemandStatusEnum OrderStatus { get; set; } = IndependentDemandStatusEnum.未确认;     // 内部执行状态 (0:待处理, 1:采购中, 2:已到货, 3:已完成)
        public ConfirmationStatusEnum ConfirmationStatus { get; set; } = ConfirmationStatusEnum.待确认;


        /// <summary>来源单据类型 DocumentTypeStaticsEnum</summary>
        public string SourceType { get; set; }
        public Guid SourceUid { get; set; }
        public string SourceNo { get; set; }
        public Guid SourceLineId { get; set; }
        public bool IsNonStandard { get; set; } = false;

        public string SourceSku { get; set; }//非标件需要有一个来源的sku,比如客户的型号
        public string Sku { get; set; }
        public string Description { get; set; } // 非标件描述或产品名称
        public string ColorName { get; set; }   // 颜色名称
        public string Features { get; set; }   // 规格型号（针对非标件尤为重要）

        public decimal OriginalQty { get; set; }  // 原始订单数量[cite: 2]
        public decimal FilledQty { get; set; }    // 已填充数量（已匹配采购或库存的数量）
        public decimal ShippedQty { get; set; }   // 已发货数量[cite: 1, 2]

        public decimal KittingRate { get; set; } // 齐套率（已齐套数量 / 原始订单数量），值域 [0, 1]
        public bool IsKitted { get; set; }        // 是否齐套[cite: 1]
        public DateTime? HopefulDate { get; set; }    // 期望交期
        public DateTime? LastShippedDate { get; set; } // 最后发货日期[cite: 2]
        public string Remark { get; set; }           // 备注[cite: 1]
        public bool IsDecomposed { get; set; }
        public DateTime? DecomposedTime { get; set; }

 
        public string MainImage { get; set; } = string.Empty;


    }
}
