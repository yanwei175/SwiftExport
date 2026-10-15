using System;
using System.Collections.Generic;
namespace SwiftExport.Contracts.DTOs.Products
{
    /// <summary>按物料版本原子保存详情资料与物料自身标签。</summary>
    public class SaveProductDetailRequest
    {
        /// <summary>待保存物料身份。</summary>
        public Guid ProductId { get; set; }
        /// <summary>读取时物料版本；本次仅编辑已保存物料。</summary>
        public int ExpectedVersion { get; set; }
        /// <summary>详情可写资料。</summary>
        public ProductDetailWriteDto Data { get; set; } = new ProductDetailWriteDto();
        /// <summary>最终选项；null保持、空集合清空、非空替换，不能默认初始化。</summary>
        public List<Guid> SelectedOptionIds { get; set; }
        /// <summary>包装显式变更；null 保持，空变更不删除任何既有关系。</summary>
        public ProductPackageChangesRequest PackagingChanges { get; set; }
    }
}
