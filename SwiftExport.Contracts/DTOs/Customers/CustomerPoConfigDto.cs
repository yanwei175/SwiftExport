using SwiftExport.Contracts.DTOs.Base;
using System;

namespace SwiftExport.Contracts.DTOs.Customers
{
    /// <summary>客户与供应商范围内的采购单默认币种、折扣、文字及经办人配置。</summary>
    public class CustomerPoConfigDto : BaseDto
    {
        /// <summary>关联的客户主数据标识。</summary>
        public Guid? CustomerId { get; set; }
        public string CustomerCode { get; set; }
        /// <summary>关联的供应商主数据标识。</summary>
        public Guid? SupplierId { get; set; }
        public string SupplierCode { get; set; }
        /// <summary>默认币种代码；空值表示未配置。</summary>
        public string Currency { get; set; }
        /// <summary>默认价格倍率，允许大于 1 表示涨价；空值表示未配置。</summary>
        public decimal? DefaultDiscountRate { get; set; }
        /// <summary>独立的默认折扣说明，可与折扣率分别配置。</summary>
        public string DiscountRemark { get; set; }
        public string InletHoseType { get; set; }
        public string AeratorType { get; set; }
        public string PalletType { get; set; }
        public string BoxType { get; set; }
        public string LabelType { get; set; }
        public string IMType { get; set; }
        public string CartonType { get; set; }
        public string LogoType { get; set; }
        public string SalesPerson { get; set; }
        public string Purchaser { get; set; }
        public string Note { get; set; }
    }
}
