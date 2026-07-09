using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.DependentDemands.Requests
{
    /// <summary>
    /// 分解行请求数据传输对象,包含[来源单据类型、来源单据编号、分解行列表]等信息。
    /// </summary>
    public class DecomposeRequest
    {
        public string SourceType { get; set; } = string.Empty; // 如 "销售订单"
        public string SourceNo { get; set; } = string.Empty;    // 如 "SO20260429001"
        public List<DecomposeLineRequest> Lines { get; set; } = new List<DecomposeLineRequest>();
    }
}
