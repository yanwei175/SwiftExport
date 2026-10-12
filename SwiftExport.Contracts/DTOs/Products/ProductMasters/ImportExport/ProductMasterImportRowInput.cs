using System;
using System.Collections.Generic;
namespace SwiftExport.Contracts.DTOs.Products.ProductMasters.ImportExport
{
    /// <summary>保存原文件行号及父级输入文本；空白表示未提供，不接受客户端指定身份或旧类型标签字段。</summary>
    public class ProductMasterImportRowInput
    {
        /// <summary>原文件一基行号。</summary>
        public int SourceRowNumber { get; set; }
        /// <summary>主型号编码。</summary>
        public string MasterSku { get; set; }
        /// <summary>中文描述。</summary>
        public string DefaultDescriptionZh { get; set; }
        /// <summary>英文描述。</summary>
        public string DefaultDescriptionEn { get; set; }
        /// <summary>中文配置。</summary>
        public string DefaultFeaturesZh { get; set; }
        /// <summary>英文配置。</summary>
        public string DefaultFeaturesEn { get; set; }
        /// <summary>装率输入，空白新增默认 1、更新保留原值。</summary>
        public string DefaultPcsPerPackage { get; set; }
        /// <summary>净重 KG 输入。</summary>
        public string DefaultNetWeight { get; set; }
        /// <summary>毛重 KG 输入。</summary>
        public string DefaultGrossWeight { get; set; }
        /// <summary>体积 CBM 输入。</summary>
        public string DefaultCbm { get; set; }
        /// <summary>完整分类路径。</summary>
        public string CategoryPath { get; set; }
        /// <summary>默认供应商编码。</summary>
        public string DefaultSupplierCode { get; set; }
    }
}
