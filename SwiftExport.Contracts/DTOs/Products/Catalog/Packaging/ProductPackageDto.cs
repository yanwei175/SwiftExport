using System;
using SwiftExport.Contracts.DTOs.Base;
namespace SwiftExport.Contracts.DTOs.Products
{
    /// <summary>物料包装规格读快照，关系版本与可写值、当前包材只读资料分开。</summary>
    public class ProductPackageDto : BaseDto
    {
        /// <summary>包装所属物料，不能经编辑换 Owner。</summary>
        public Guid OwnerProductId { get; set; }
        /// <summary>当前包装结果可写值。</summary>
        public ProductPackageWriteDto Data { get; set; } = new ProductPackageWriteDto();
        /// <summary>当前包材编号，缺失引用仍保留关系身份。</summary>
        public string PackagingMaterialSku { get; set; }
        /// <summary>当前包材中文描述。</summary>
        public string PackagingMaterialDescription { get; set; }
        /// <summary>当前包材基础单位。</summary>
        public string PackagingMaterialUnit { get; set; }
        /// <summary>未选包材或引用仍为启用包材；false 时显示失效提示并允许明确修复。</summary>
        public bool IsPackagingMaterialValid { get; set; }
    }
}
