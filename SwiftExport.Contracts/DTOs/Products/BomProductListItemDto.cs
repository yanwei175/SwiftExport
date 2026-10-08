using System;

namespace SwiftExport.Contracts.DTOs.Products
{
    /// <summary>成品 BOM 管理只读投影；数量按共享编号统计，不作为产品整包保存输入。</summary>
    public class BomProductListItemDto
    {
        public Guid Uid { get; set; }
        public int Version { get; set; }
        public string MasterSku { get; set; }
        public string Sku { get; set; }
        public string DescriptionEn { get; set; }
        public string DescriptionZh { get; set; }
        public string MainImage { get; set; }
        public string Finish { get; set; }
        public Guid CategoryId { get; set; }
        public string CategoryName { get; set; }
        public string SupplyType { get; set; }
        public string Unit { get; set; }
        public string BomNo { get; set; }
        public int BomVersion { get; set; }
        public bool IsBound { get; set; }
        public int ComponentCount { get; set; }
        public int SharedProductCount { get; set; }
    }
}
