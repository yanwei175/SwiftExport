using System;
using System.Collections.Generic;
using SwiftExport.Contracts.DTOs.Base;
using SwiftExport.Contracts.DTOs.Products.Attributes;
namespace SwiftExport.Contracts.DTOs.Products
{
    /// <summary>物料详情完整读快照；可写资料与来源、包装、标签展示分开。</summary>
    public class ProductDetailDto : BaseDto
    {
        /// <summary>可编辑物料资料。</summary>
        public ProductDetailWriteDto Data { get; set; } = new ProductDetailWriteDto();
        /// <summary>已保存的来源主型号身份，只读。</summary>
        public Guid? ProductMasterId { get; set; }
        /// <summary>物料自身已保存的产品型号；不因中性产品缺失而变为空。</summary>
        public string LinkSku { get; set; }
        /// <summary>来源主型号当前编码，只读。</summary>
        public string MasterSku { get; set; }
        /// <summary>来源主型号当前分类身份。</summary>
        public Guid CategoryId { get; set; }
        /// <summary>分类名称。</summary>
        public string CategoryName { get; set; }
        /// <summary>从来源主型号取得的模板身份。</summary>
        public Guid? ProductAttributeProfileId { get; set; }
        /// <summary>来源模板名称。</summary>
        public string ProductAttributeProfileName { get; set; }
        /// <summary>当前启停状态，仅供展示。</summary>
        public bool IsActive { get; set; }
        /// <summary>当前BOM编号，仅供服务器保留和列表投影。</summary>
        public string BomNo { get; set; }
        /// <summary>当前BOM有效标记。</summary>
        public int BomVersion { get; set; }
        /// <summary>颜色显示名称。</summary>
        public string Finish { get; set; }
        /// <summary>供应商编码。</summary>
        public string DefaultSupplierCode { get; set; }
        /// <summary>供应商名称。</summary>
        public string SupplierName { get; set; }
        /// <summary>制造商编码。</summary>
        public string ManufacturerCode { get; set; }
        /// <summary>制造商名称。</summary>
        public string ManufacturerName { get; set; }
        /// <summary>参考仓库编码。</summary>
        public string DefaultWarehouseCode { get; set; }
        /// <summary>参考仓库名称。</summary>
        public string DefaultWarehouseName { get; set; }
        /// <summary>包装参考装箱数。</summary>
        public decimal? PcsOfCarton { get; set; }
        /// <summary>包装参考箱净重。</summary>
        public decimal? NwOfCarton { get; set; }
        /// <summary>包装参考箱毛重。</summary>
        public decimal? GwOfCarton { get; set; }
        /// <summary>包装参考箱体积。</summary>
        public decimal? CbmOfCarton { get; set; }
        /// <summary>包装关系完整快照；null 表示该服务端未提供或区块未加载，不能据此清空。</summary>
        public List<ProductPackageDto> Packages { get; set; }
        /// <summary>该物料自身已保存的选项身份。</summary>
        public List<Guid> SelectedOptionIds { get; set; } = new List<Guid>();
        /// <summary>该物料自身选择的展示信息，失效身份仍保留。</summary>
        public List<ProductAttributeSelectionDto> AttributeSelections { get; set; } = new List<ProductAttributeSelectionDto>();
    }
}
