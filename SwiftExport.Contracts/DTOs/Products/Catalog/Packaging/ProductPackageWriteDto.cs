using System;
namespace SwiftExport.Contracts.DTOs.Products
{
    /// <summary>物料包装规格可写值；所属物料由详情请求决定，不接收包材档案的名称、自重或尺寸回写。</summary>
    public class ProductPackageWriteDto
    {
        /// <summary>ProductPackageType 字典的稳定英文代码，各层可以省略。</summary>
        public string PackageType { get; set; }
        /// <summary>可空包材物料身份，选择时必须为启用包材。</summary>
        public Guid? PackagingMaterialId { get; set; }
        /// <summary>该包装中成品基础单位数，必须为正整数；单套运输盒初始值为一。</summary>
        public int PcsPerPackage { get; set; } = 1;
        /// <summary>包装结果长度，CM。</summary>
        public decimal? Length_CM { get; set; }
        /// <summary>包装结果宽度，CM。</summary>
        public decimal? Width_CM { get; set; }
        /// <summary>包装结果高度，CM。</summary>
        public decimal? Height_CM { get; set; }
        /// <summary>包装结果净重，KG。</summary>
        public decimal? Nw_KG { get; set; }
        /// <summary>包装结果毛重，KG；同时有净重时不得小于净重。</summary>
        public decimal? Gw_KG { get; set; }
        /// <summary>手工维护的包装体积，CBM，不按尺寸自动计算。</summary>
        public decimal? Cbm { get; set; }
        /// <summary>包装规格备注。</summary>
        public string Note { get; set; }
    }
}
