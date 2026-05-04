using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.Orders.DependentDemands
{
    /// <summary>
    /// 需求池转采购请求。
    /// </summary>
    public class DependentDemandConvertToPurchaseRequestDto
    {
        /// <summary>
        /// 分组模式：BySourceNo / BySupplierCode / ByDependentDemandIds。
        /// </summary>
        public string GroupMode { get; set; } = DependentDemandConvertGroupModes.BySourceNo;

        /// <summary>
        /// 可选：来源类型过滤（不传则不过滤）。
        /// </summary>
        public string SourceType { get; set; } = string.Empty;

        /// <summary>
        /// 可选：来源单号列表（不传则不过滤）。
        /// </summary>
        public List<string> SourceNos { get; set; } = new List<string>();

        /// <summary>
        /// 可选：需求池ID列表（用于按用户选中行转采购）。
        /// </summary>
        public List<int> DependentDemandIds { get; set; } = new List<int>();

        /// <summary>
        /// 可选：指定供应商编码（按供应商分组时可用于只生成该供应商采购单）。
        /// </summary>
        public string SupplierCode { get; set; } = string.Empty;
    }

    /// <summary>
    /// 需求池转采购分组模式常量。
    /// </summary>
    public static class DependentDemandConvertGroupModes
    {
        /// <summary>
        /// 按来源单号（每个来源单号内再按供应商拆单）。
        /// </summary>
        public const string BySourceNo = "BySourceNo";

        /// <summary>
        /// 按供应商（全局聚合）。
        /// </summary>
        public const string BySupplierCode = "BySupplierCode";

        /// <summary>
        /// 按用户选中需求池ID列表（选中范围内按来源单号+供应商拆单）。
        /// </summary>
        public const string ByDependentDemandIds = "ByDependentDemandIds";
    }
}
