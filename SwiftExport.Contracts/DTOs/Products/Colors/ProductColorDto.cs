using SwiftExport.Contracts.DTOs.Base;

namespace SwiftExport.Contracts.DTOs.Products
{
    /// <summary>传输产品颜色资料及全局显示顺序，供维护页和只读缓存共同使用。</summary>
    public class ProductColorDto : BaseDto
    {
        public string ColorCodeUnique { get; set; }
        public string ColorCode { get; set; }
        public string ColorNameEn { get; set; }
        public string ColorNameZh { get; set; }
        /// <summary>非负全局显示排序号，数值越小越靠前；允许同号。</summary>
        public int SortNo { get; set; }
    }
}
