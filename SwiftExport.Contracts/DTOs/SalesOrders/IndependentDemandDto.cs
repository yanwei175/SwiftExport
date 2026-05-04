using SwiftExport.Contracts.DTOs.Base;
using System;

namespace SwiftExport.Contracts.DTOs.SalesOrders
{
    public class IndependentDemandDto : BaseDto
    {
        public string Sku { get; set; } // Sku
        public decimal OriginalQty { get; set; } // OriginalQty
        public decimal FilledQty { get; set; } // FilledQty
        public decimal ShippedQty { get; set; } // ShippedQty
        public bool IsKitted { get; set; } // IsKitted
        public DateTime? HopefulDate { get; set; } // HopefulDate
        public string Remark { get; set; }

        /// <summary>
        /// 是否已执行拆解（防重复拆解）。
        /// </summary>
        public bool IsDecomposed { get; set; }

        /// <summary>
        /// 最近一次拆解时间。
        /// </summary>
        public DateTime? DecomposedTime { get; set; }


        public decimal 待补缺口 => OriginalQty - FilledQty;
        public string 进度百分比 => OriginalQty > 0 ? $"{Math.Round(FilledQty / OriginalQty * 100, 2)}%" : "0%";
        public string 齐套状态 => IsKitted ? "已齐套" : "缺料";

    }
}
